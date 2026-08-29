-- ============================================================================
-- Soporte / Tickets: enlace entre el ticket y su documento del ERP
--
-- POR QUÉ
-- El alta de ticket genera un documento con sp_generar_documento_con_partidas
-- (tipo 1 = "Ticket de soporte", área 1 = Sistemas, tp_mov = TKS), pero el folio
-- que devolvía no se guardaba en ninguna parte: no había forma de saber qué
-- documento correspondía a qué ticket, ni de llegar al ticket desde el documento.
--
-- Esta columna cierra ese hueco. Es nullable a propósito:
--   * los tickets creados antes de este cambio no tienen documento;
--   * si algún día el documento deja de generarse, la columna simplemente queda
--     vacía y nada se rompe.
--
-- CÓMO USARLO
--   Este archivo es el de DESARROLLO (srs).
--   Para producción usar el gemelo sql/soporte_documento_ticket_prod.sql.
--   Es idempotente: se puede volver a correr sin efectos secundarios.
-- ============================================================================

BEGIN;

SET search_path TO public, srs;

-- ----------------------------------------------------------------------------
-- 1) COLUMNA
-- ----------------------------------------------------------------------------
ALTER TABLE tkts
    ADD COLUMN IF NOT EXISTS id_encabezado INTEGER NULL;

COMMENT ON COLUMN tkts.id_encabezado IS
    'Documento del ERP generado al crear el ticket (encabezadomov.id_encabezado). '
    'NULL en tickets anteriores a la generación automática.';


-- ----------------------------------------------------------------------------
-- 2) INTEGRIDAD
--
-- ON DELETE SET NULL: si el documento se elimina, el ticket sobrevive sin él. Un
-- ticket es un reporte de un usuario y no debe desaparecer porque se depure un
-- documento contable.
-- ----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'tkts'::regclass   -- ::regclass resuelve por search_path
          AND conname = 'fk_tkts_encabezado'
    ) THEN
        ALTER TABLE tkts
            ADD CONSTRAINT fk_tkts_encabezado
            FOREIGN KEY (id_encabezado) REFERENCES encabezadomov(id_encabezado)
            ON DELETE SET NULL;
    END IF;
END $$;

-- Para ir del documento al ticket sin recorrer toda la tabla.
CREATE INDEX IF NOT EXISTS ix_tkts_encabezado
    ON tkts (id_encabezado) WHERE id_encabezado IS NOT NULL;


-- ----------------------------------------------------------------------------
-- 3) VERIFICACIÓN
-- ----------------------------------------------------------------------------
SELECT a.attname                            AS columna,
       format_type(a.atttypid, a.atttypmod) AS tipo,
       NOT a.attnotnull                     AS acepta_null
FROM pg_attribute a
WHERE a.attrelid = 'tkts'::regclass
  AND a.attname = 'id_encabezado'
  AND NOT a.attisdropped;

COMMIT;
