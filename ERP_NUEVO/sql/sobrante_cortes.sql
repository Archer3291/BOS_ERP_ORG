-- ============================================================================
--  Sobrante trazable al crear piezas  —  tarima_productos_cortes.es_sobrante
--
--  Bug: al declarar piezas nuevas desde existencia sin desglosar (p. ej. de
--  500 m cortar 5 piezas de 5 m), el remanente (475 m) no quedaba como un
--  corte real: era solo la resta `existencia_total - metros_configurados`,
--  sin folio y por lo tanto sin código de barras.
--
--  `DividirPieza` ya resuelve esto bien para una pieza que SÍ tiene corte:
--  el sobrante se inserta como un corte nuevo con su propio folio. Esta
--  columna deja que `Crear` (que arranca desde existencia sin ningún corte
--  todavía) haga lo mismo: cada vez que se declaran piezas nuevas, el
--  remanente anterior se desactiva y se reemplaza por un corte fresco
--  marcado `es_sobrante = true`, que el trigger de corte_piezas ya existente
--  convierte automáticamente en una pieza física con su código.
--
--  Ejecutar una sola vez.
-- ============================================================================

BEGIN;

ALTER TABLE tarima_productos_cortes
    ADD COLUMN IF NOT EXISTS es_sobrante BOOLEAN NOT NULL DEFAULT false;

COMMENT ON COLUMN tarima_productos_cortes.es_sobrante IS
    'true = remanente sin desglosar que Crear() mantiene automáticamente. '
    'Se reemplaza por un folio nuevo cada vez que se declara otra pieza del '
    'mismo producto; no se edita in-place.';

COMMIT;
