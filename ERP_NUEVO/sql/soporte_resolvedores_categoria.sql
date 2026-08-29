-- ============================================================================
-- Soporte / Tickets: resolvedores por categoría y alcance de Administración
--
-- QUÉ PROBLEMA CIERRA
-- Hoy el módulo tiene UN asignador por categoría (tkts_categorias.responsable),
-- pero los resolvedores son globales: cualquiera con rol Revisor/Asignador puede
-- recibir cualquier ticket, y en Administración todo el staff ve la cola completa
-- de todas las categorías. Eso hace que:
--
--   * el combo "Asignar usuario" ofrezca gente que no atiende esa categoría;
--   * un asignador de Almacén vea y pueda mover tickets de Contabilidad;
--   * la pantalla de Administración crezca sin filtro conforme entra más gente.
--
-- Esta tabla ata cada resolvedor a las categorías que sí atiende. A partir de
-- ella, helpers/SoporteAlcance.cs calcula "qué categorías puede ver esta persona"
-- y todas las consultas de Administración se acotan con ese conjunto.
--
-- QUIÉN SIGUE VIENDO TODO
-- Sistemas, por cualquiera de estos tres caminos (helpers/SoporteAlcance.VeTodo):
--   1. Rol del ERP llamado 'Sistemas'  (usuarios.rolid -> roles.nombre)
--   2. Área Sistemas                    (usuarios.areaid = 1)
--   3. Permiso 'sistemas' o 'super_admin' (permisos_usuario)
--
-- Nótese que el rol 'Super Administrador' NO abre el alcance por sí solo: es
-- decisión explícita del negocio. Quien lo tenga y necesite ver todo debe además
-- traer el permiso 'super_admin', que es el mecanismo que ya usa el menú lateral.
--
-- SIEMBRA
-- Se llena con TODO el staff actual en TODAS las categorías activas. El día del
-- despliegue nadie pierde acceso -el comportamiento queda idéntico al de hoy- y
-- la depuración se hace después, categoría por categoría, desde la pantalla de
-- Soporte > Administración > Equipo. Es reversible: quitar una fila devuelve el
-- estado anterior para esa pareja.
--
-- CÓMO USARLO
--   Este archivo es el de DESARROLLO (srs). Para producción, el gemelo
--   sql/soporte_resolvedores_categoria_prod.sql.
--   Es idempotente: se puede volver a correr sin efectos secundarios.
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- 0) GUARDA CONTRA LA TRAMPA DE public
--
-- Un CREATE TABLE sin esquema aterriza en public, y public va antes que srs en el
-- search_path de la aplicación: la tabla nueva ECLIPSA a la del esquema real y el
-- ERP escribe en una y lee de otra sin que nada falle. Ya pasó en este repo
-- (ver la nota en sql/pos_formas_pago_bancos_prod.sql). Si existe la gemela en
-- public, este script se niega a seguir.
-- ----------------------------------------------------------------------------
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_tables
        WHERE schemaname = 'public' AND tablename = 'tkt_categoria_resolvedor'
    ) THEN
        RAISE EXCEPTION
            'Existe public.tkt_categoria_resolvedor y eclipsaría a la de  Bórrala antes de continuar.';
    END IF;
END $$;


-- ----------------------------------------------------------------------------
-- 1) LA TABLA
--
-- Clave primaria compuesta y no un id sintético: la fila ES la pareja, no tiene
-- identidad propia, y así el alta repetida choca sola sin necesitar un UNIQUE
-- aparte. ON DELETE CASCADE en las dos FK porque una pareja huérfana -categoría
-- borrada o usuario dado de baja- no significa nada.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tkt_categoria_resolvedor (
    id_cat      integer     NOT NULL REFERENCES tkts_categorias (id_cat) ON DELETE CASCADE,
    id_usr      integer     NOT NULL REFERENCES usuarios (usuarioid)     ON DELETE CASCADE,
    fch_alta    timestamptz NOT NULL DEFAULT now(),
    alta_por    integer     NULL REFERENCES usuarios (usuarioid),
    PRIMARY KEY (id_cat, id_usr)
);

-- La PK ya sirve para "¿quiénes atienden esta categoría?" (id_cat va primero).
-- Este índice cubre la pregunta inversa -"¿qué categorías atiende esta persona?"-
-- que es la que hace SoporteAlcance en CADA petición de Administración.
CREATE INDEX IF NOT EXISTS ix_tkt_cat_resolvedor_usr
    ON tkt_categoria_resolvedor (id_usr);

COMMENT ON TABLE tkt_categoria_resolvedor IS
    'Qué resolvedores atienden qué categorías. Acota el combo de asignación y el alcance de Administración.';


-- ----------------------------------------------------------------------------
-- 2) SIEMBRA: todo el staff actual en todas las categorías activas
--
-- Staff = quien hoy puede atender tickets según SoporteAuthz, por cualquiera de
-- los dos catálogos que conviven:
--   * rol propio del módulo: tkt_usuario_rol.id_rol_tkt IN (1 Revisor, 2 Asignador)
--   * rol del ERP: 'Sistemas', 'Super Administrador', 'Resolvedor Tickets',
--                  'Asignador Tickets'
--
-- Se toman los dos porque en srs nadie tiene rol del ERP y en srs_prod casi nadie
-- tiene rol del módulo: quedarse con uno solo dejaría media plantilla fuera.
-- ----------------------------------------------------------------------------
INSERT INTO tkt_categoria_resolvedor (id_cat, id_usr)
SELECT c.id_cat, s.usuarioid
FROM tkts_categorias c
CROSS JOIN (
    SELECT DISTINCT u.usuarioid
    FROM usuarios u
    LEFT JOIN tkt_usuario_rol tur ON tur.id_usr = u.usuarioid
    LEFT JOIN roles r             ON r.rolid    = u.rolid
    WHERE tur.id_rol_tkt IN (1, 2)
       OR r.nombre IN ('Sistemas', 'Super Administrador',
                       'Resolvedor Tickets', 'Asignador Tickets')
) s
WHERE c.activo = true
ON CONFLICT (id_cat, id_usr) DO NOTHING;


-- ----------------------------------------------------------------------------
-- 3) EL RESPONSABLE DE CADA CATEGORÍA TAMBIÉN LA ATIENDE
--
-- tkts_categorias.responsable es el asignador. SoporteAlcance ya lo cuenta como
-- miembro aunque no tenga fila aquí, pero dejarlo explícito hace que la pantalla
-- de Equipo lo muestre donde corresponde en vez de parecer que no pertenece a la
-- categoría que dirige.
-- ----------------------------------------------------------------------------
INSERT INTO tkt_categoria_resolvedor (id_cat, id_usr)
SELECT c.id_cat, c.responsable
FROM tkts_categorias c
WHERE c.activo = true
  AND c.responsable IS NOT NULL
  AND EXISTS (SELECT 1 FROM usuarios u WHERE u.usuarioid = c.responsable)
ON CONFLICT (id_cat, id_usr) DO NOTHING;


-- ----------------------------------------------------------------------------
-- 4) VERIFICACIÓN
-- ----------------------------------------------------------------------------

-- Cuántos resolvedores quedó atendiendo cada categoría. Ninguna debería salir
-- en 0: una categoría sin resolvedores no tiene a quién asignarle tickets.
SELECT c.id_cat,
       c.n_cat,
       u.nombreusuario                    AS asignador,
       COUNT(cr.id_usr)                   AS resolvedores
FROM tkts_categorias c
LEFT JOIN usuarios u                  ON u.usuarioid = c.responsable
LEFT JOIN tkt_categoria_resolvedor cr ON cr.id_cat   = c.id_cat
WHERE c.activo = true
GROUP BY c.id_cat, c.n_cat, u.nombreusuario
ORDER BY resolvedores ASC, c.n_cat;

-- Quién ve todo pase lo que pase (no se le aplica el filtro por categoría).
SELECT DISTINCT u.usuarioid, u.nombreusuario, r.nombre AS rol_erp, u.areaid
FROM usuarios u
LEFT JOIN roles r            ON r.rolid       = u.rolid
LEFT JOIN permisos_usuario pu ON pu.usuario_id = u.usuarioid
LEFT JOIN permisos p          ON p.id_permiso  = pu.permiso_id
WHERE r.nombre = 'Sistemas'
   OR u.areaid = 1
   OR p.nombre IN ('sistemas', 'super_admin')
ORDER BY u.nombreusuario;

COMMIT;
