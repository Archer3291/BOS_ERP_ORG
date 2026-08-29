-- ============================================================================
-- Soporte / Tickets: adjuntos en las respuestas del hilo
--
-- POR QUÉ
-- El formulario de respuesta de un ticket permite adjuntar archivos y el JS los
-- mete en el FormData, pero RespuestaController.CrearRespuesta no tenía ningún
-- parámetro de archivos: se descartaban en silencio y el usuario veía
-- "Respuesta enviada correctamente" con sus archivos perdidos.
--
-- tkts_adj sólo sabía colgar del ticket (id_tkts). Con esta columna un adjunto
-- puede colgar además de un seguimiento concreto:
--
--     id_seg_tkts IS NULL  -> adjunto del ticket original (como hasta ahora)
--     id_seg_tkts = N      -> adjunto de esa respuesta del hilo
--
-- Es aditiva y nullable, así que las filas existentes siguen siendo válidas y
-- significan lo mismo que antes.
--
-- CÓMO USARLO
--   Este archivo es el de DESARROLLO (srs).
--   Para producción usar el gemelo sql/soporte_adjuntos_respuesta_prod.sql.
--   Es idempotente: se puede volver a correr sin efectos secundarios.
-- ============================================================================

BEGIN;

SET search_path TO public, srs;

-- ----------------------------------------------------------------------------
-- 1) COLUMNA
-- ----------------------------------------------------------------------------
ALTER TABLE tkts_adj
    ADD COLUMN IF NOT EXISTS id_seg_tkts INTEGER NULL;


-- ----------------------------------------------------------------------------
-- 2) INTEGRIDAD
--
-- ON DELETE CASCADE: si algún día se borra una respuesta, sus adjuntos se van con
-- ella en vez de quedar apuntando a un seguimiento inexistente.
-- ----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'tkts_adj'::regclass   -- ::regclass resuelve por search_path
          AND conname = 'fk_tkts_adj_seg'
    ) THEN
        ALTER TABLE tkts_adj
            ADD CONSTRAINT fk_tkts_adj_seg
            FOREIGN KEY (id_seg_tkts) REFERENCES seg_tkts(id_seg_tkts) ON DELETE CASCADE;
    END IF;
END $$;


-- ----------------------------------------------------------------------------
-- 3) ÍNDICES
--
-- Las dos consultas de la vista de ticket son "adjuntos de este ticket" y
-- "adjuntos de esta respuesta"; sin índice ambas hacen scan completo.
-- ----------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_tkts_adj_tkt ON tkts_adj (id_tkts);
CREATE INDEX IF NOT EXISTS ix_tkts_adj_seg ON tkts_adj (id_seg_tkts) WHERE id_seg_tkts IS NOT NULL;


-- ----------------------------------------------------------------------------
-- 4) VERIFICACIÓN
-- ----------------------------------------------------------------------------
SELECT a.attname                                AS columna,
       format_type(a.atttypid, a.atttypmod)     AS tipo,
       NOT a.attnotnull                         AS acepta_null
FROM pg_attribute a
WHERE a.attrelid = 'tkts_adj'::regclass
  AND a.attnum > 0
  AND NOT a.attisdropped
ORDER BY a.attnum;

COMMIT;
