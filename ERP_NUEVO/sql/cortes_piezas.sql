-- ============================================================================
--  Identidad por barra física  —  srs.corte_piezas
--
--  Hasta ahora una fila de `tarima_productos_cortes` representaba N barras
--  idénticas (folio + longitud + cantidad). Eso basta para contar metros, pero
--  no para pegarle una etiqueta a UNA barra: las N compartían folio.
--
--  Esta tabla da una fila —y un código de barras— por barra real, sin cambiar
--  nada de lo que ya existe: `tarima_productos_cortes` sigue siendo la fuente
--  de verdad de cuántas hay, y un trigger mantiene las piezas en sincronía.
--  Por eso los flujos que ya tocan `cantidad` (asignación de cortes, remisión,
--  operación de corte…) siguen funcionando sin tocarles una línea.
--
--  ── Sobre el esquema ───────────────────────────────────────────────────────
--  Todo lo NUEVO se crea explícitamente en `srs`. Las tablas que ya existen se
--  referencian SIN cualificar, para que se resuelvan por search_path igual que
--  lo hacen las consultas de la aplicación (no hace falta saber en qué esquema
--  vive cada una).
--
--  La aplicación conecta con `Search Path=public,srs`, así que encontrará
--  srs.corte_piezas sin cambiar una línea de C#. Ojo con el orden: como
--  `public` va PRIMERO, cualquier objeto homónimo en `public` tapa al de `srs`.
--  El script avisa si detecta ese caso.
--
--  Ejecutar una sola vez.
-- ============================================================================

BEGIN;

-- Igual que la aplicación, para que los nombres sin cualificar resuelvan igual.
SET LOCAL search_path = public, srs;

CREATE SCHEMA IF NOT EXISTS srs;

-- ── Comprobaciones previas ──────────────────────────────────────────────────
DO $$
DECLARE
    v_esquema text;
BEGIN
    -- ¿Dónde vive de verdad la tabla padre? Se informa para que quede en el log.
    SELECT n.nspname INTO v_esquema
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relname = 'tarima_productos_cortes'
      AND c.relkind = 'r'
      AND n.nspname = ANY (current_schemas(true))
    ORDER BY array_position(current_schemas(true), n.nspname)
    LIMIT 1;

    IF v_esquema IS NULL THEN
        RAISE EXCEPTION
            'No se encontró la tabla tarima_productos_cortes en el search_path actual (%). '
            'Ajusta el search_path antes de ejecutar esta migración.',
            array_to_string(current_schemas(true), ', ');
    END IF;

    RAISE NOTICE 'tarima_productos_cortes está en el esquema "%". corte_piezas se creará en "srs".', v_esquema;

    -- Una copia en `public` ganaría siempre por el orden del search_path.
    IF to_regclass('public.corte_piezas') IS NOT NULL THEN
        RAISE WARNING
            'Existe public.corte_piezas. Como "public" va antes que "srs" en el search_path, '
            'la aplicación seguirá viendo ESA tabla y no la nueva. Elimínala (DROP TABLE public.corte_piezas CASCADE) '
            'si esta migración la sustituye.';
    END IF;
END $$;

-- ── Secuencia y generador de código ─────────────────────────────────────────
CREATE SEQUENCE IF NOT EXISTS srs.seq_codigo_barra_pieza START WITH 1 INCREMENT BY 1;

CREATE OR REPLACE FUNCTION srs.fn_generar_codigo_pieza()
RETURNS text AS $$
BEGIN
    -- Prefijo + 9 dígitos. Sin letras ambiguas ni separadores: entra tal cual
    -- en un Code128 y el lector lo teclea como una sola ráfaga.
    RETURN 'BRR' || LPAD(nextval('srs.seq_codigo_barra_pieza')::text, 9, '0');
END;
$$ LANGUAGE plpgsql
-- El search_path de una función es el del LLAMADOR salvo que se fije aquí.
-- Sin esto, quién invoque la función decidiría a qué secuencia apunta.
SET search_path = public, srs;

-- ── Tabla ───────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS srs.corte_piezas (
    id_pieza          BIGSERIAL PRIMARY KEY,
    codigo            TEXT NOT NULL UNIQUE DEFAULT srs.fn_generar_codigo_pieza(),
    corte_id          INTEGER NOT NULL
                      REFERENCES tarima_productos_cortes (id_corte) ON DELETE CASCADE,

    -- Se copia la longitud del corte al crear la pieza. Guardarla aquí permite
    -- que una pieza conserve su medida aunque el corte padre cambie después.
    longitud          NUMERIC(12,4) NOT NULL,

    estado            TEXT NOT NULL DEFAULT 'disponible'
                      CHECK (estado IN ('disponible', 'usada', 'baja')),

    -- Trazabilidad: de qué barra salió esta (al subdividir) y en qué
    -- asignación de pedido se consumió.
    pieza_madre_id    BIGINT REFERENCES srs.corte_piezas (id_pieza) ON DELETE SET NULL,
    asignacion_id     INTEGER,

    fecha_creacion    TIMESTAMP NOT NULL DEFAULT NOW(),
    fecha_baja        TIMESTAMP,
    usuario_creacion  TEXT,
    comentario        TEXT
);

CREATE INDEX IF NOT EXISTS ix_corte_piezas_corte  ON srs.corte_piezas (corte_id);
CREATE INDEX IF NOT EXISTS ix_corte_piezas_estado ON srs.corte_piezas (estado);
CREATE INDEX IF NOT EXISTS ix_corte_piezas_madre  ON srs.corte_piezas (pieza_madre_id);

-- El escaneo busca por código exacto: el UNIQUE ya deja el índice hecho.

-- ── Sincronía automática con tarima_productos_cortes ────────────────────────
--
--  Invariante: nº de piezas 'disponible' de un corte == tarima_productos_cortes.cantidad
--
--  Se resuelve con trigger y no desde C# a propósito: `cantidad` la modifican
--  varios módulos (AdminCortes, la asignación de cortes del pedido, la
--  operación de corte, la remisión…). Replicar el mantenimiento en cada uno
--  sería garantizar que algún día se desincronizan.
CREATE OR REPLACE FUNCTION srs.fn_sincronizar_corte_piezas()
RETURNS TRIGGER AS $$
DECLARE
    v_disponibles INTEGER;
    v_objetivo    INTEGER;
    v_faltan      INTEGER;
    v_sobran      INTEGER;
BEGIN
    -- Una pieza es una barra entera; las cantidades fraccionarias se redondean
    -- hacia arriba (media barra sigue siendo una barra que puedes escanear).
    v_objetivo := GREATEST(0, CEIL(COALESCE(NEW.cantidad, 0))::int);

    IF NOT COALESCE(NEW.activo, true) THEN
        v_objetivo := 0;
    END IF;

    SELECT COUNT(*) INTO v_disponibles
    FROM srs.corte_piezas
    WHERE corte_id = NEW.id_corte AND estado = 'disponible';

    IF v_objetivo > v_disponibles THEN
        v_faltan := v_objetivo - v_disponibles;
        INSERT INTO srs.corte_piezas (corte_id, longitud, usuario_creacion)
        SELECT NEW.id_corte, NEW.longitud, NEW.usuario_creacion
        FROM generate_series(1, v_faltan);

    ELSIF v_objetivo < v_disponibles THEN
        v_sobran := v_disponibles - v_objetivo;
        -- Se consumen las más antiguas primero (FIFO), que es como se gasta
        -- el material en el piso.
        UPDATE srs.corte_piezas
        SET estado     = CASE WHEN COALESCE(NEW.activo, true) THEN 'usada' ELSE 'baja' END,
            fecha_baja = NOW()
        WHERE id_pieza IN (
            SELECT id_pieza FROM srs.corte_piezas
            WHERE corte_id = NEW.id_corte AND estado = 'disponible'
            ORDER BY id_pieza
            LIMIT v_sobran
        );
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql
SET search_path = public, srs;

DROP TRIGGER IF EXISTS trg_corte_piezas_sync ON tarima_productos_cortes;

CREATE TRIGGER trg_corte_piezas_sync
AFTER INSERT OR UPDATE OF cantidad, activo ON tarima_productos_cortes
FOR EACH ROW
EXECUTE FUNCTION srs.fn_sincronizar_corte_piezas();

-- ── Relleno de lo que ya existe ─────────────────────────────────────────────
-- Crea las piezas que faltan para los cortes activos actuales. Es idempotente:
-- si se vuelve a ejecutar no duplica nada, solo completa lo que falte.
INSERT INTO srs.corte_piezas (corte_id, longitud, usuario_creacion, comentario)
SELECT c.id_corte, c.longitud, c.usuario_creacion, 'Alta inicial por migración'
FROM tarima_productos_cortes c
CROSS JOIN LATERAL generate_series(
    1,
    GREATEST(0, CEIL(COALESCE(c.cantidad, 0))::int -
        (SELECT COUNT(*) FROM srs.corte_piezas p
          WHERE p.corte_id = c.id_corte AND p.estado = 'disponible'))
) AS g
WHERE COALESCE(c.activo, true) = true;

COMMIT;

-- ── Comprobación ────────────────────────────────────────────────────────────
-- Debe devolver 0 filas: cada corte activo tiene tantas piezas disponibles
-- como dice su cantidad.
--
-- SET search_path = public, srs;
--
-- SELECT c.id_corte, c.folio, c.cantidad,
--        (SELECT COUNT(*) FROM srs.corte_piezas p
--          WHERE p.corte_id = c.id_corte AND p.estado = 'disponible') AS piezas
--   FROM tarima_productos_cortes c
--  WHERE COALESCE(c.activo, true)
--    AND CEIL(COALESCE(c.cantidad,0))::int <>
--        (SELECT COUNT(*) FROM srs.corte_piezas p
--          WHERE p.corte_id = c.id_corte AND p.estado = 'disponible');

-- Y para confirmar que quedó donde debe:
--
-- SELECT n.nspname AS esquema, c.relname
--   FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
--  WHERE c.relname = 'corte_piezas';
