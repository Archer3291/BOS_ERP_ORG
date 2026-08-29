-- ============================================================================
--  Resincroniza corte_piezas.id_pieza tras convertirla a INT4 IDENTITY
--
--  CAUSA RAÍZ (confirmada 2026-08-17): al convertir id_pieza de BIGSERIAL a
--  INT4 GENERATED ALWAYS AS IDENTITY, la secuencia VIEJA del serial original
--  (p. ej. corte_piezas_id_pieza_seq) no se eliminó — quedó huérfana, con
--  dependencia deptype='a' sobre la columna. La conversión creó una segunda
--  secuencia NUEVA (p. ej. corte_piezas_id_pieza_seq1) con deptype='i', que es
--  la que la columna usa REALMENTE.
--
--  pg_get_serial_sequence('corte_piezas','id_pieza') devuelve la vieja
--  (deptype 'a'), no la que de verdad usa la identity — así que resincronizar
--  "la secuencia que Postgres dice que es" NO resuelve nada: los inserts
--  reales siguen usando la otra, que sigue entregando valores bajos (1, 2, 3…)
--  y choca con id_pieza que ya existían desde antes de la conversión:
--
--      23505: llave duplicada viola restricción de unicidad «corte_piezas_pkey»
--
--  Este script busca la secuencia REAL vía pg_depend (deptype='i', la que
--  Postgres asocia de verdad a una columna IDENTITY) y solo esa la
--  resincroniza. No cambia el tipo de columna ni borra la secuencia huérfana
--  (eso se puede limpiar aparte si se quiere, no es necesario para el fix).
--
--  Idempotente: se puede correr varias veces sin efecto secundario más allá
--  de reajustar el contador. Ejecutar en cada esquema que tenga esta misma
--  columna convertida a IDENTITY (por ejemplo, si srs_prod tiene el mismo
--  historial de conversión, cambia el 'corte_piezas'::regclass de abajo).
-- ============================================================================

DO $$
DECLARE
    v_seq  regclass;
    v_next bigint;
BEGIN
    SELECT s.oid::regclass INTO v_seq
    FROM pg_depend d
    JOIN pg_class s ON s.oid = d.objid AND s.relkind = 'S'
    JOIN pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
    WHERE d.refobjid = 'corte_piezas'::regclass
      AND a.attname = 'id_pieza'
      AND d.deptype = 'i';

    IF v_seq IS NULL THEN
        RAISE EXCEPTION
            'No se encontró una secuencia IDENTITY (deptype=i) para corte_piezas.id_pieza. '
            '¿La columna sigue siendo IDENTITY? Revisa information_schema.columns.';
    END IF;

    SELECT COALESCE(MAX(id_pieza), 0) + 1 INTO v_next FROM corte_piezas;

    PERFORM setval(v_seq, v_next, false);

    RAISE NOTICE 'corte_piezas.id_pieza: secuencia real = %, resincronizada a %', v_seq, v_next;
END $$;

-- ── Comprobación ────────────────────────────────────────────────────────────
-- El NOTICE de arriba ya dice el nombre real de la secuencia y a qué valor
-- quedó. Para volver a encontrarla más tarde sin adivinar el nombre:
--
-- SELECT s.oid::regclass AS secuencia_real
-- FROM pg_depend d
-- JOIN pg_class s ON s.oid = d.objid AND s.relkind = 'S'
-- JOIN pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
-- WHERE d.refobjid = 'corte_piezas'::regclass AND a.attname = 'id_pieza' AND d.deptype = 'i';
--
-- Con ese nombre: SELECT last_value, is_called FROM <secuencia_real>;
