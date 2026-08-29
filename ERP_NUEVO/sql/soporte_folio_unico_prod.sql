-- ============================================================================
-- Soporte / Tickets: unicidad del folio público
--
-- POR QUÉ
-- folio_tkt son 10 caracteres al azar con formato AAA-BBB-CCCC y nada garantizaba
-- que no se repitiera: no había índice único ni comprobación previa. Toda la
-- navegación del módulo va por folio (la URL del ticket, el buscador, el correo de
-- aviso, la asignación masiva), así que dos tickets con el mismo folio harían que
-- uno de ellos fuera inalcanzable.
--
-- Con este índice, una colisión hace fallar el INSERT con SQLSTATE 23505 y
-- EnviarTicketsController.InsertarTicketConFolio reintenta con otro folio.
--
-- CÓMO USARLO
--   Ejecutar contra la base de PRODUCCIÓN. NO se ha corrido.
--   Gemelo de sql/soporte_folio_unico.sql; sólo cambia el esquema.
--   Es idempotente: se puede volver a correr sin efectos secundarios.
-- ============================================================================

BEGIN;

SET search_path TO public, srs_prod;

-- ----------------------------------------------------------------------------
-- 1) COMPROBACIÓN PREVIA
--
-- Si ya hubiera folios repetidos, el índice no se puede crear. Esta consulta los
-- lista: debe devolver 0 filas antes de continuar.
-- ----------------------------------------------------------------------------
SELECT folio_tkt, COUNT(*) AS repeticiones
FROM tkts
GROUP BY folio_tkt
HAVING COUNT(*) > 1;


-- ----------------------------------------------------------------------------
-- 2) ÍNDICE ÚNICO
--
-- Sirve además para buscar por folio, que es como el módulo resuelve casi todas
-- sus pantallas (TicketT/id, BuscarT, la asignación masiva).
-- ----------------------------------------------------------------------------
CREATE UNIQUE INDEX IF NOT EXISTS ux_tkts_folio ON tkts (folio_tkt);


-- ----------------------------------------------------------------------------
-- 3) VERIFICACIÓN
-- ----------------------------------------------------------------------------
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'tkts'
  AND schemaname = (SELECT nspname FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.oid = 'tkts'::regclass)
ORDER BY indexname;

COMMIT;
