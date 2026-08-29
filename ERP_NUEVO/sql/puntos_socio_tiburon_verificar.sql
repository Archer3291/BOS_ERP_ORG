-- ============================================================================
-- Verificación del módulo de puntos (Socio Tiburón)
--
-- Sólo lee: no modifica nada, se puede correr cuantas veces quieras y en
-- cualquier momento. Devuelve un renglón por revisión con OK / FALTA / REVISAR,
-- para no tener que interpretar listados de índices y columnas a ojo.
--
-- PARA PRODUCCIÓN: reemplaza srs por srs_prod en todo el archivo.
--
-- Si la consulta falla con "no existe la relación", ésa ES la respuesta: el
-- objeto que menciona no se creó y hay que correr el script de despliegue.
-- ============================================================================

SELECT n, revision, estado, detalle FROM (

-- ── Objetos ─────────────────────────────────────────────────────────────────
SELECT 1 AS n, 'Tabla puntos_movimientos' AS revision,
       CASE WHEN to_regclass('srs.puntos_movimientos') IS NOT NULL THEN 'OK' ELSE 'FALTA' END AS estado,
       '' AS detalle

UNION ALL SELECT 2, 'Tabla puntos_tarifas',
       CASE WHEN to_regclass('srs.puntos_tarifas') IS NOT NULL THEN 'OK' ELSE 'FALTA' END, ''

UNION ALL SELECT 3, 'Vista v_puntos_saldo',
       CASE WHEN to_regclass('srs.v_puntos_saldo') IS NOT NULL THEN 'OK' ELSE 'FALTA' END, ''

-- ── Columnas del modelo de eventos ──────────────────────────────────────────
-- Sin ellas el cálculo no puede distinguir emitir de cobrar, ni saber qué ya
-- registró: es el corazón de las reglas nuevas.
UNION ALL SELECT 4, 'Columnas evento / doc_clave / doc_ref / doc_ref_rel',
       CASE WHEN COUNT(*) = 4 THEN 'OK' ELSE 'FALTA' END,
       COUNT(*) || ' de 4' ||
       CASE WHEN COUNT(*) = 4 THEN '' ELSE ': ' || COALESCE(string_agg(column_name, ', ' ORDER BY column_name), 'ninguna') END
  FROM information_schema.columns
 WHERE table_schema = 'srs' AND table_name = 'puntos_movimientos'
   AND column_name IN ('evento', 'doc_clave', 'doc_ref', 'doc_ref_rel')

-- ── Restricciones ───────────────────────────────────────────────────────────
-- La versión vieja de estas dos no conoce 'devolucion' ni 'reverso', así que
-- registrar una nota de crédito o una cancelación falla con error de CHECK.
UNION ALL SELECT 5, 'tipo_ck admite devolucion y reverso',
       CASE WHEN COUNT(*) FILTER (WHERE def LIKE '%devolucion%' AND def LIKE '%reverso%') = 1
            THEN 'OK' ELSE 'REVISAR' END,
       COALESCE(MAX(def), 'la restricción no existe')
  FROM (SELECT pg_get_constraintdef(oid) AS def FROM pg_constraint
         WHERE conrelid = to_regclass('srs.puntos_movimientos')
           AND conname  = 'puntos_movimientos_tipo_ck') t5

UNION ALL SELECT 6, 'signo_ck admite devolucion y reverso',
       CASE WHEN COUNT(*) FILTER (WHERE def LIKE '%devolucion%' AND def LIKE '%reverso%') = 1
            THEN 'OK' ELSE 'REVISAR' END,
       COALESCE(MAX(def), 'la restricción no existe')
  FROM (SELECT pg_get_constraintdef(oid) AS def FROM pg_constraint
         WHERE conrelid = to_regclass('srs.puntos_movimientos')
           AND conname  = 'puntos_movimientos_signo_ck') t6

UNION ALL SELECT 7, 'evento_ck existe',
       CASE WHEN COUNT(*) = 1 THEN 'OK' ELSE 'FALTA' END, ''
  FROM pg_constraint
 WHERE conrelid = to_regclass('srs.puntos_movimientos')
   AND conname  = 'puntos_movimientos_evento_ck'

-- ── Índices ─────────────────────────────────────────────────────────────────
-- clave_unq es TODA la idempotencia: sin él, recalcular el mismo periodo vuelve
-- a insertar y duplica puntos.
UNION ALL SELECT 8, 'Índice único puntos_movimientos_clave_unq',
       CASE WHEN COUNT(*) = 1 THEN 'OK' ELSE 'FALTA' END, ''
  FROM pg_indexes
 WHERE schemaname = 'srs' AND tablename = 'puntos_movimientos'
   AND indexname  = 'puntos_movimientos_clave_unq'

-- El de siete columnas rechaza como duplicado el segundo cobro de un mismo
-- complemento de pago; el de saldo_inicial es de un tipo que ya no existe.
UNION ALL SELECT 9, 'Índices viejos retirados',
       CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'REVISAR' END,
       COALESCE(string_agg(indexname, ', ' ORDER BY indexname), 'ninguno presente')
  FROM pg_indexes
 WHERE schemaname = 'srs' AND tablename = 'puntos_movimientos'
   AND indexname IN ('puntos_movimientos_documento_unq', 'puntos_movimientos_saldo_inicial_unq')

-- ── Vista con el desglose por evento ────────────────────────────────────────
UNION ALL SELECT 10, 'v_puntos_saldo trae devoluciones/reversos/desglose',
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FALTA' END, COUNT(*) || ' de 5'
  FROM information_schema.columns
 WHERE table_schema = 'srs' AND table_name = 'v_puntos_saldo'
   AND column_name IN ('devoluciones', 'reversos', 'por_contado', 'por_pagos', 'por_anticipos')

-- ── Catálogos ───────────────────────────────────────────────────────────────
UNION ALL SELECT 11, 'Tarifas cargadas (22 escalones)',
       CASE WHEN COUNT(*) = 22 THEN 'OK' ELSE 'REVISAR' END, COUNT(*) || ' escalones'
  FROM srs.puntos_tarifas WHERE activo

UNION ALL SELECT 12, 'Permiso puntos_socio_tiburon',
       CASE WHEN COUNT(*) = 1 THEN 'OK' ELSE 'FALTA' END,
       COALESCE(MAX(CASE WHEN modulo_padre IS NULL THEN 'creado SIN padre'
                         ELSE 'bajo credito_cobranza' END), '')
  FROM srs.permisos WHERE nombre = 'puntos_socio_tiburon'

-- ── Datos: lo que delataría un doble abono ──────────────────────────────────
UNION ALL SELECT 13, 'Sin movimientos tipo saldo_inicial',
       CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'REVISAR' END, COUNT(*) || ' fila(s)'
  FROM srs.puntos_movimientos WHERE tipo = 'saldo_inicial'

-- Una acumulación sin clave es invisible para el cálculo: volvería a ofrecer
-- ese documento y el cliente cobraría los puntos por segunda vez.
UNION ALL SELECT 14, 'Acumulaciones sin doc_clave',
       CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'REVISAR' END, COUNT(*) || ' fila(s)'
  FROM srs.puntos_movimientos WHERE tipo = 'acumulacion' AND doc_clave IS NULL

-- Dos movimientos para el mismo hecho: si sale algo, ya hay puntos duplicados.
UNION ALL SELECT 15, 'Sin claves duplicadas',
       CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'REVISAR' END, COUNT(*) || ' clave(s)'
  FROM (SELECT doc_clave FROM srs.puntos_movimientos
         WHERE doc_clave IS NOT NULL
         GROUP BY doc_clave HAVING COUNT(*) > 1) t15

) revisiones
ORDER BY n;
