-- ============================================================================
-- Soporte / Tickets: captura automática de errores del sistema
--
-- POR QUÉ ESTE SCRIPT
-- Hoy, cuando el ERP truena, no queda rastro salvo que alguien levante un ticket
-- a mano. El módulo de captura automática convierte cada falla en un ticket con
-- diagnóstico completo, pero necesita dos cosas que no existen en la base:
--
--   1. Un registro de DEDUPLICACIÓN. Sin él, una consulta rota que usan 40
--      personas produce 40 tickets idénticos en una hora y el módulo queda
--      inservible en una semana. `tkt_error_huella` guarda una fila por *tipo*
--      de error (identificado por un sha256 del tipo de excepción + el mensaje
--      normalizado + el frame de origen), con el ticket asociado y el contador.
--
--   2. Un lugar donde vivan los DETALLES. Un stack trace crudo dentro de
--      tkts.descr hace ilegible la vista del ticket. `tkt_error_detalle` guarda
--      cada ocurrencia individual con su payload en jsonb; en el ticket queda
--      sólo el resumen y el detalle se despliega bajo demanda.
--
-- QUIÉN CREA EL TICKET
-- El usuario logueado al momento de la falla (decisión del área). Por eso NO se
-- crea ningún usuario "bot": tkts.id_usr apunta a quien de verdad estaba usando
-- el sistema. Cuando no hay sesión -jobs de Hangfire, sesión expirada- la capa
-- de C# cae al responsable de la categoría, que siempre es un usuario real.
--
-- CÓMO USARLO
--   Este archivo es el de DESARROLLO (srs).
--   Para producción usar el gemelo sql/soporte_errores_auto_prod.sql.
--   Es idempotente: se puede volver a correr sin efectos secundarios.
-- ============================================================================

BEGIN;

SET search_path TO public, srs;


-- ----------------------------------------------------------------------------
-- 0) GUARDAS
--
-- Correr esto contra el esquema equivocado crearía tablas huérfanas con FKs
-- rotas. Mejor abortar y decirlo.
-- ----------------------------------------------------------------------------
DO $$
DECLARE v_faltan text := '';
BEGIN
    IF to_regclass('srs.tkts')            IS NULL THEN v_faltan := v_faltan || ' tkts';            END IF;
    IF to_regclass('srs.usuarios')        IS NULL THEN v_faltan := v_faltan || ' usuarios';        END IF;
    IF to_regclass('srs.prio')            IS NULL THEN v_faltan := v_faltan || ' prio';            END IF;
    IF to_regclass('srs.tkts_categorias') IS NULL THEN v_faltan := v_faltan || ' tkts_categorias'; END IF;

    IF v_faltan <> '' THEN
        RAISE EXCEPTION
            'ABORTADO: faltan tablas en srs:%. ¿Es el esquema correcto?', v_faltan;
    END IF;
END $$;

-- Un CREATE TABLE sin calificar aterriza en `public`, que va PRIMERO en el
-- search_path de la aplicación y taparía a la de srs. Si eso ya pasó, mejor
-- detenerse y decirlo que crear una segunda y quedarse leyendo la equivocada.
DO $$
BEGIN
    IF to_regclass('public.tkt_error_huella') IS NOT NULL THEN
        RAISE EXCEPTION
            'ABORTADO: existe public.tkt_error_huella y taparía a la de srs. '
            'Muévela con: ALTER TABLE public.tkt_error_huella SET SCHEMA srs; '
            'y vuelve a correr este script.';
    END IF;
    IF to_regclass('public.tkt_error_detalle') IS NOT NULL THEN
        RAISE EXCEPTION
            'ABORTADO: existe public.tkt_error_detalle y taparía a la de srs. '
            'Muévela con: ALTER TABLE public.tkt_error_detalle SET SCHEMA srs; '
            'y vuelve a correr este script.';
    END IF;
END $$;


-- ----------------------------------------------------------------------------
-- 1) REGISTRO DE HUELLAS
--
-- Una fila por *tipo* de error. Es la tabla que hace posible la deduplicación:
-- el servicio hace INSERT ... ON CONFLICT (huella) DO UPDATE y decide, según lo
-- que devuelva, si crea ticket nuevo, engorda el existente o se calla.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS srs.tkt_error_huella (
    id_huella     integer     GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- sha256 en hex del tipo de excepción + mensaje normalizado + frame origen.
    -- char(64) y no text: siempre mide exactamente eso, y este UNIQUE es el
    -- corazón del ON CONFLICT que deduplica.
    huella        char(64)    NOT NULL UNIQUE,

    tipo_exc      varchar(200),
    -- El mensaje con GUIDs, fechas, números y literales ya sustituidos por
    -- marcadores. Es lo que hace que "no existe el folio 4471" y "...4472"
    -- cuenten como el mismo error.
    mensaje_norm  text,
    -- Primer frame del stack que pertenece a BOS_ERP: "Archivo.cs:123".
    origen        varchar(300),

    -- El ticket que representa a este error. NULL mientras el cortacircuitos
    -- impida crearlo. ON DELETE SET NULL: si alguien borra el ticket, la huella
    -- sobrevive con su histórico y el siguiente error genera uno nuevo.
    id_tkt        integer     NULL REFERENCES srs.tkts (id_tkts) ON DELETE SET NULL,

    ocurrencias   integer     NOT NULL DEFAULT 1,
    -- Usuarios distintos afectados. Se incrementa sólo cuando aparece uno nuevo,
    -- así que sobrevive a la poda de tkt_error_detalle.
    afectados     integer     NOT NULL DEFAULT 1,

    primera_vez   timestamptz NOT NULL DEFAULT now(),
    ultima_vez    timestamptz NOT NULL DEFAULT now(),

    -- Apaga el ruido conocido SIN borrar el histórico: se sigue registrando el
    -- detalle, pero nunca vuelve a crear ni tocar un ticket.
    silenciada    boolean     NOT NULL DEFAULT false
);

COMMENT ON TABLE  srs.tkt_error_huella IS
    'Deduplicación de errores automáticos: una fila por tipo de error, con su ticket y contador.';
COMMENT ON COLUMN srs.tkt_error_huella.huella IS
    'sha256 hex de tipo_exc + mensaje normalizado + frame de origen. Clave de deduplicación.';
COMMENT ON COLUMN srs.tkt_error_huella.silenciada IS
    'true = se registra el detalle pero nunca se crea ni actualiza ticket. Para ruido conocido.';


-- ----------------------------------------------------------------------------
-- 2) DETALLE POR OCURRENCIA
--
-- Cada vez que el error sucede. payload en jsonb porque lo que se guarda cambia
-- según la capa que capturó (el navegador no tiene stack de servidor, el
-- middleware sí) y según el tipo de excepción (PostgresException aporta
-- SqlState, tabla y constraint).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS srs.tkt_error_detalle (
    id_detalle  bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_huella   integer     NOT NULL REFERENCES srs.tkt_error_huella (id_huella) ON DELETE CASCADE,
    fch         timestamptz NOT NULL DEFAULT now(),

    -- Qué capa lo capturó: middleware | controlador | navegador | job
    capa        varchar(16) NOT NULL,

    -- Quién lo sufrió. NULL cuando no hay sesión (jobs de Hangfire).
    -- ON DELETE SET NULL para no perder el diagnóstico si se da de baja al usuario.
    id_usr      integer     NULL REFERENCES srs.usuarios (usuarioid) ON DELETE SET NULL,
    empresa_id  integer     NULL,
    sucursal_id integer     NULL,

    ruta        text,
    metodo      varchar(10),
    -- HttpContext.TraceIdentifier: permite cruzar esta fila contra log_sistema.
    trace_id    varchar(100),
    ip          varchar(64),
    user_agent  text,

    -- stack completo, cadena de InnerException, SqlState/tabla/constraint,
    -- parámetros del request YA SANITIZADOS (sin contraseñas ni tokens).
    payload     jsonb       NOT NULL
);

COMMENT ON TABLE  srs.tkt_error_detalle IS
    'Una fila por ocurrencia de error. Se poda periódicamente; el contador vive en tkt_error_huella.';
COMMENT ON COLUMN srs.tkt_error_detalle.payload IS
    'Diagnóstico completo en jsonb. Sanitizado: nunca contraseñas, tokens, API keys ni datos de CSD.';


-- ----------------------------------------------------------------------------
-- 3) ÍNDICES
-- ----------------------------------------------------------------------------
-- Para llegar a la huella desde el ticket (panel de diagnóstico en VerTicket).
CREATE INDEX IF NOT EXISTS ix_err_huella_tkt
    ON srs.tkt_error_huella (id_tkt) WHERE id_tkt IS NOT NULL;

-- Orden por defecto del tablero de huellas.
CREATE INDEX IF NOT EXISTS ix_err_huella_ult
    ON srs.tkt_error_huella (ultima_vez DESC);

-- El cortacircuitos cuenta huellas nacidas en la última hora, en cada error.
CREATE INDEX IF NOT EXISTS ix_err_huella_primera
    ON srs.tkt_error_huella (primera_vez DESC);

-- Últimas ocurrencias de una huella, que es como se lee el panel.
CREATE INDEX IF NOT EXISTS ix_err_detalle_huella
    ON srs.tkt_error_detalle (id_huella, fch DESC);


-- ----------------------------------------------------------------------------
-- 4) CATEGORÍA "Errores del sistema"
--
-- LA PRIORIDAD NO SE PUEDE HARDCODEAR.
-- La primera versión de este script ponía id_prio = 1 porque en srs el catálogo
-- prio tiene 1 = Alta. En srs_prod ese id NO EXISTE y el INSERT reventó con
-- 23503 (violación de fk_categorias_prioridades). Los dos esquemas no comparten
-- los ids del catálogo, así que se resuelve por NOMBRE en tiempo de ejecución.
--
-- Recordar además que el orden de prio está invertido respecto a lo intuitivo
-- (Alta es la primera, no la última); ver el comentario de
-- Views/Soporte/FormularioTicket.cshtml, que documenta cuándo esto guardaba
-- todos los tickets al revés.
--
-- El responsable se resuelve en cascada, igual que hace EnviarTicketsController:
-- rol del ERP -> rol del módulo de tickets. Si no hay ninguno, se aborta en vez
-- de dejar una categoría apuntando a un usuario inexistente: tkts.id_usr_asig
-- tiene FK y el primer error del sistema fallaría al insertarse.
-- ----------------------------------------------------------------------------
DO $$
DECLARE
    v_resp integer;
    v_prio integer;
    v_prio_n text;
BEGIN
    IF EXISTS (SELECT 1 FROM srs.tkts_categorias WHERE n_cat = 'Errores del sistema') THEN
        RAISE NOTICE 'La categoría "Errores del sistema" ya existe. Sin cambios.';
        RETURN;
    END IF;

    -- Prioridad ALTA: por nombre primero ('Alta' / 'Alto'), y si el catálogo usa
    -- otra nomenclatura, la de id más bajo, que por convención de esta base es
    -- la más urgente. Se deja dicho en un NOTICE cuál se eligió.
    SELECT id_prio, n INTO v_prio, v_prio_n
    FROM srs.prio
    ORDER BY CASE WHEN lower(n) LIKE 'alt%' THEN 0 ELSE 1 END, id_prio
    LIMIT 1;

    IF v_prio IS NULL THEN
        RAISE EXCEPTION
            'ABORTADO: el catálogo prio está vacío en este esquema. Sin prioridades no '
            'se puede crear ninguna categoría (tkts_categorias.id_prio tiene FK), y el '
            'módulo de tickets completo estaría roto. Puebla prio antes de continuar.';
    END IF;

    SELECT COALESCE(
        (SELECT u.usuarioid
           FROM srs.usuarios u
           JOIN srs.roles r ON r.rolid = u.rolid
          WHERE r.nombre IN ('Sistemas', 'Super Administrador')
          ORDER BY u.usuarioid
          LIMIT 1),
        (SELECT tur.id_usr
           FROM srs.tkt_usuario_rol tur
           JOIN srs.usuarios u2 ON u2.usuarioid = tur.id_usr
          WHERE tur.id_rol_tkt IN (1, 2)
          ORDER BY tur.id_usr
          LIMIT 1)
    ) INTO v_resp;

    IF v_resp IS NULL THEN
        RAISE EXCEPTION
            'ABORTADO: no hay ningún usuario de Sistemas ni staff de tickets al que '
            'asignar la categoría "Errores del sistema". Corre antes sql/soporte_staff.sql '
            'o designa un responsable a mano.';
    END IF;

    INSERT INTO srs.tkts_categorias (n_cat, responsable, id_prio, icon_class, activo)
    VALUES ('Errores del sistema', v_resp, v_prio, 'fa-solid fa-triangle-exclamation', true);

    RAISE NOTICE 'Categoría "Errores del sistema" creada. Responsable = %, prioridad = % (%)',
        v_resp, v_prio, v_prio_n;
END $$;


-- ----------------------------------------------------------------------------
-- 5) VERIFICACIÓN
-- ----------------------------------------------------------------------------
SELECT 'tkt_error_huella'  AS tabla, to_regclass('srs.tkt_error_huella')::text  AS creada
UNION ALL
SELECT 'tkt_error_detalle', to_regclass('srs.tkt_error_detalle')::text;

-- El catálogo de prioridades tal cual está en ESTE esquema. Los ids no coinciden
-- entre srs y srs_prod, así que ErrorTicketService también los resuelve por
-- nombre en vez de asumirlos: esta salida es la que confirma qué va a usar.
SELECT id_prio, n AS nombre, color FROM srs.prio ORDER BY id_prio;

SELECT c.id_cat, c.n_cat, c.responsable, u.nombreusuario AS responsable_nombre,
       c.id_prio, p.n AS prioridad, c.activo
FROM srs.tkts_categorias c
JOIN srs.usuarios u ON u.usuarioid = c.responsable
JOIN srs.prio p     ON p.id_prio   = c.id_prio
WHERE c.n_cat = 'Errores del sistema';

COMMIT;
