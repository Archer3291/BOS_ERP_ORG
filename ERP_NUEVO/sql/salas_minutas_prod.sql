-- ============================================================================
-- Recursos Humanos / Salas: minutas de las reservaciones
--
-- POR QUÉ
-- Una reservación de sala termina y no queda rastro de lo que se acordó. El
-- calendario sabe quién apartó, cuándo y con quién, pero el plan o el acta que
-- salió de la junta vive en el correo de alguien. Esta tabla le da a cada
-- reservación ya terminada un lugar donde colgar hasta tres PDFs: la minuta.
--
-- DECISIONES
--   * Sólo PDF. Es el formato en el que se firma y se comparte un acta; admitir
--     .docx invitaría a subir borradores editables. Lo fija un CHECK, no sólo
--     la validación de C#.
--   * Máximo 3 por reservación. El límite lo aplica el controlador, pero también
--     un trigger: dos pestañas subiendo a la vez no deben poder meter una cuarta.
--   * `path` es relativo a {ContentRoot}/Minutas y NO es una URL servible. La
--     carpeta vive fuera de wwwroot a propósito -una minuta es información
--     interna- y la descarga pasa siempre por DescargarMinuta, que valida
--     permisos. Se guarda igual que en tkts_adj para que el patrón sea uno solo.
--   * ON DELETE CASCADE contra salas_reservadas: si algún día se depura una
--     reservación, sus minutas no deben quedar apuntando a la nada. Los archivos
--     en disco sobreviven, pero sin fila nadie puede pedirlos.
--
-- CÓMO USARLO
--   Ejecutar contra la base de PRODUCCIÓN. NO se ha corrido.
--   Gemelo de sql/salas_minutas.sql; sólo cambia el esquema.
--   Es idempotente: se puede volver a correr sin efectos secundarios.
-- ============================================================================

BEGIN;

-- srs_prod primero: un CREATE sin calificar debe aterrizar en el esquema de la
-- aplicación, no en public.
SET search_path TO srs_prod, public;


-- ----------------------------------------------------------------------------
-- 0) GUARDAS
--
-- Correr esto contra el esquema equivocado dejaría una tabla huérfana con las
-- FKs rotas. Mejor abortar y decirlo.
-- ----------------------------------------------------------------------------
DO $$
DECLARE v_faltan text := '';
BEGIN
    IF to_regclass('srs_prod.salas_reservadas') IS NULL THEN v_faltan := v_faltan || ' salas_reservadas'; END IF;
    IF to_regclass('srs_prod.usuarios')         IS NULL THEN v_faltan := v_faltan || ' usuarios';         END IF;

    IF v_faltan <> '' THEN
        RAISE EXCEPTION
            'ABORTADO: faltan tablas en srs_prod:%. ¿Es el esquema correcto?', v_faltan;
    END IF;
END $$;

-- public va PRIMERO en el search_path de la aplicación, así que una tabla suelta
-- ahí taparía a la de srs_prod y el módulo leería la equivocada.
DO $$
BEGIN
    IF to_regclass('public.reservaciones_archivos') IS NOT NULL THEN
        RAISE EXCEPTION
            'ABORTADO: existe public.reservaciones_archivos y taparía a la de srs_prod. '
            'Muévela con: ALTER TABLE public.reservaciones_archivos SET SCHEMA srs_prod; '
            'y vuelve a correr este script.';
    END IF;
END $$;


-- ----------------------------------------------------------------------------
-- 1) TABLA
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS srs_prod.reservaciones_archivos (
    archivo_id        integer      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Dueño del archivo. Sin esto la tabla no sabría de qué junta es la minuta.
    sala_reservada_id integer      NOT NULL,

    -- Ruta relativa a la carpeta de minutas ("Minutas/<uuid>.pdf"). Se guarda
    -- para poder localizar el archivo si algún día cambia la convención de
    -- nombres; la descarga la reconstruye desde el uuid.
    path              varchar(500) NOT NULL,

    -- Como lo nombró quien lo subió. Es el nombre con el que se descarga.
    nombre_original   varchar(255) NOT NULL,

    -- Nombre real en disco. Un uuid evita colisiones y que el nombre del archivo
    -- filtre información o permita adivinar rutas.
    uuid              uuid         NOT NULL,

    extension         varchar(10)  NOT NULL,
    fecha             timestamp    NOT NULL DEFAULT now(),

    -- Quién la subió: el organizador o Recursos Humanos.
    subido_por        integer      NOT NULL,

    CONSTRAINT reservaciones_archivos_uuid_uk
        UNIQUE (uuid),

    -- Sólo PDF, y siempre con punto: la C# guarda Path.GetExtension() tal cual.
    CONSTRAINT reservaciones_archivos_extension_chk
        CHECK (lower(extension) = '.pdf')
);


-- ----------------------------------------------------------------------------
-- 2) INTEGRIDAD
-- ----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'srs_prod.reservaciones_archivos'::regclass
          AND conname = 'reservaciones_archivos_reservacion_fk'
    ) THEN
        ALTER TABLE srs_prod.reservaciones_archivos
            ADD CONSTRAINT reservaciones_archivos_reservacion_fk
            FOREIGN KEY (sala_reservada_id)
            REFERENCES srs_prod.salas_reservadas(id_sala_reservada) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'srs_prod.reservaciones_archivos'::regclass
          AND conname = 'reservaciones_archivos_usuario_fk'
    ) THEN
        ALTER TABLE srs_prod.reservaciones_archivos
            ADD CONSTRAINT reservaciones_archivos_usuario_fk
            FOREIGN KEY (subido_por) REFERENCES srs_prod.usuarios(usuarioid);
    END IF;
END $$;

-- La consulta del módulo siempre es "las minutas de estas reservaciones".
CREATE INDEX IF NOT EXISTS ix_reservaciones_archivos_reservacion
    ON srs_prod.reservaciones_archivos (sala_reservada_id);


-- ----------------------------------------------------------------------------
-- 3) LÍMITE DE 3 MINUTAS
--
-- El controlador ya cuenta antes de guardar, pero entre ese SELECT y el INSERT
-- cabe otra petición. Un trigger AFTER cierra la ventana: la cuarta fila hace
-- fallar el INSERT y la transacción se revierte.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION srs_prod.reservaciones_archivos_limite()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE v_total integer;
BEGIN
    SELECT COUNT(*) INTO v_total
    FROM srs_prod.reservaciones_archivos
    WHERE sala_reservada_id = NEW.sala_reservada_id;

    IF v_total > 3 THEN
        RAISE EXCEPTION 'Una reservación admite como máximo 3 minutas (reservación %)',
            NEW.sala_reservada_id;
    END IF;

    RETURN NULL;   -- AFTER trigger: el valor de retorno se ignora
END $$;

DROP TRIGGER IF EXISTS tg_reservaciones_archivos_limite ON srs_prod.reservaciones_archivos;

CREATE TRIGGER tg_reservaciones_archivos_limite
    AFTER INSERT ON srs_prod.reservaciones_archivos
    FOR EACH ROW
    EXECUTE FUNCTION srs_prod.reservaciones_archivos_limite();


-- ----------------------------------------------------------------------------
-- 4) VERIFICACIÓN
-- ----------------------------------------------------------------------------
SELECT a.attname                            AS columna,
       format_type(a.atttypid, a.atttypmod) AS tipo,
       NOT a.attnotnull                     AS acepta_null
FROM pg_attribute a
WHERE a.attrelid = 'srs_prod.reservaciones_archivos'::regclass
  AND a.attnum > 0
  AND NOT a.attisdropped
ORDER BY a.attnum;

COMMIT;
