-- ============================================================================
--  Enlace pieza física ↔ asignación de pedido
--
--  corte_piezas.asignacion_id ya existía (ver sql/cortes_piezas.sql) pero
--  ningún flujo lo llenaba: la Operación de Cortes solo sabía el FOLIO del
--  lote, no qué barra concreta se tomó. Ahora VTPedidoController (al reservar)
--  y CorteOperacionController (al reasignar) etiquetan la pieza que el
--  trigger trg_corte_piezas_sync acaba de marcar 'usada', así CorteOperacion
--  puede mostrar el código Code128 de la barra exacta junto al folio origen.
--
--  Este script solo añade el índice que le falta a esa columna para las
--  búsquedas por asignación (LEFT JOIN corte_piezas ON asignacion_id = a.id
--  en ObtenerCortesPendientes / ObtenerCortePorAsignacion). No crea nada que
--  no exista ya si sql/cortes_piezas.sql corrió antes.
--
--  Ejecutar una sola vez (después de sql/cortes_piezas.sql).
-- ============================================================================

SET LOCAL search_path = public, srs;

CREATE INDEX IF NOT EXISTS ix_corte_piezas_asignacion
    ON corte_piezas (asignacion_id)
    WHERE asignacion_id IS NOT NULL;
