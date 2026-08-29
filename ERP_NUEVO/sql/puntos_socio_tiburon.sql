-- ============================================================================
-- Programa de puntos (Socio Tiburón) — esquema srs
--
-- CONSOLIDADO. Este archivo creció en dos tandas —primero el ledger, después las
-- reglas de cuándo se generan puntos— y esa historia por partes provocó un
-- despliegue a medias: columnas puestas sin sus restricciones, índices viejos que
-- sobrevivieron y una vista que quedó sin crear. Aquí ya no hay historia: el
-- script lleva el esquema a su forma FINAL, venga del estado que venga.
--
-- Es el gemelo de sql/puntos_socio_tiburon_prod.sql; sólo cambia el esquema.
--
-- QUÉ INSTALA
--   puntos_tarifas       los 22 escalones por clasificación, con vigencia
--   puntos_movimientos   el ledger: cada punto ligado al hecho que lo generó
--   v_puntos_saldo       el saldo, que NO se guarda: es la suma de movimientos
--   permiso              puntos_socio_tiburon, colgado de credito_cobranza
--
-- LA REGLA QUE ORDENA EL MÓDULO
-- Los puntos siguen al DINERO RECIBIDO, no a la facturación: la factura de
-- contado otorga al emitirse, la de crédito no otorga hasta que su complemento
-- de pago entra, el anticipo otorga al recibirse y la factura que lo aplica
-- descuenta esa parte, la nota de crédito resta y la cancelación reversa.
--
-- TODO VA CALIFICADO CON 
-- A propósito, y no por estilo: este script NO usa `SET search_path`. En el
-- despliegue a `srs` un ALTER TABLE quedó fuera porque se ejecutaron fragmentos
-- sueltos sin arrastrar el SET de la primera línea, y la tabla terminó a medio
-- migrar. Con el esquema escrito en cada objeto, ejecutar de a pedazos deja de
-- ser peligroso: cada sentencia sabe sola dónde vive.
--
-- CÓMO CORRERLO
-- Va todo en una transacción y TERMINA EN ROLLBACK a propósito. Córrelo una vez,
-- revisa el bloque de verificación del final y, si cuadra, cambia el ROLLBACK
-- por COMMIT y vuelve a correrlo. Es idempotente: pasar dos veces por lo ya
-- aplicado no rompe ni duplica nada.
-- ============================================================================

BEGIN;

-- ── 0. Red de seguridad: que exista de qué colgarse ─────────────────────────
-- Sin estas tablas el script fallaría a media creación, con unos objetos puestos
-- y otros no. Vale más abortar antes de tocar nada y con un mensaje que diga qué
-- falta, en vez de un error de llave foránea a mitad del camino.
DO $$
DECLARE
    v_faltan text := '';
BEGIN
    IF to_regclass('catclientes')   IS NULL THEN v_faltan := v_faltan || ' catclientes';   END IF;
    IF to_regclass('encabezadomov') IS NULL THEN v_faltan := v_faltan || ' encabezadomov'; END IF;
    IF to_regclass('factura')       IS NULL THEN v_faltan := v_faltan || ' factura';       END IF;
    IF to_regclass('permisos')      IS NULL THEN v_faltan := v_faltan || ' permisos';      END IF;

    IF v_faltan <> '' THEN
        RAISE EXCEPTION
            'ABORTADO: faltan tablas en srs:%. ¿Es el esquema correcto?', v_faltan;
    END IF;
END $$;

-- Una versión temprana de este módulo contemplaba un tipo `saldo_inicial` para
-- arrastrar los saldos de Socio Tiburón. Se descartó: el saldo se reconstruye
-- calculando el histórico completo, y cualquier corrección puntual es un `ajuste`,
-- que además obliga a escribir un motivo.
--
-- Si quedaran filas con ese tipo, el CHECK nuevo las rechazaría y el script
-- fallaría con un error de restricción que no dice qué hacer. Mejor abortar aquí
-- con instrucciones: convertirlas es una decisión sobre saldos de clientes reales
-- y no puede tomarla un script.
DO $$
DECLARE
    v_viejos integer := 0;
BEGIN
    IF to_regclass('puntos_movimientos') IS NOT NULL THEN
        EXECUTE 'SELECT COUNT(*) FROM puntos_movimientos WHERE tipo = ''saldo_inicial'''
           INTO v_viejos;
    END IF;

    IF v_viejos > 0 THEN
        RAISE EXCEPTION
            'ABORTADO: % movimiento(s) con tipo ''saldo_inicial'', que este modelo ya no admite. '
            'Decide antes qué hacer con ellos: convertirlos a ''ajuste'' con un motivo escrito '
            '(conserva el saldo) o borrarlos (lo reduce). Después vuelve a correr el script.',
            v_viejos;
    END IF;
END $$;

-- ── 1. Tarifas ──────────────────────────────────────────────────────────────
-- Las tarifas salen del código a la base porque son 22 números que mercadotecnia
-- va a querer mover. Hoy cambiar un porcentaje obliga a recompilar y redistribuir
-- una aplicación de escritorio.
--
-- `vigente_desde` permite cambiarlas sin perder la que se usó para los puntos ya
-- otorgados: el movimiento guarda la tasa que se le aplicó.
CREATE TABLE IF NOT EXISTS puntos_tarifas (
    id_tarifa      integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    clasificacion  varchar(20)   NOT NULL,
    importe_min    numeric(15,2) NOT NULL,
    importe_max    numeric(15,2),          -- NULL = sin tope superior
    tasa           numeric(10,6) NOT NULL,
    vigente_desde  date          NOT NULL DEFAULT CURRENT_DATE,
    activo         boolean       NOT NULL DEFAULT true,

    CONSTRAINT puntos_tarifas_rango_ck CHECK (importe_max IS NULL OR importe_max >= importe_min)
);

CREATE INDEX IF NOT EXISTS puntos_tarifas_busqueda_idx
    ON puntos_tarifas (clasificacion, activo, importe_min);

-- ── 2. Movimientos ──────────────────────────────────────────────────────────
-- El saldo del cliente es SUM(puntos) de esta tabla. Nunca se guarda un total:
-- eso es lo que arregla el problema de origen, donde el saldo vivía en cuatro
-- lugares que no coincidían y cada recálculo lo sobrescribía.
CREATE TABLE IF NOT EXISTS puntos_movimientos (
    id_movimiento  integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    cliente_id     integer      NOT NULL REFERENCES catclientes (id_cliente),
    -- La clave también, porque es la que liga con Socio Tiburón (usuarios.idkep)
    -- y con Kepler (kdm1.c10).
    cve_cli        varchar(20)  NOT NULL,

    -- acumulacion (+) | devolucion (−) | reverso (±) | canje (−) | ajuste (±) | expiracion (−)
    tipo           varchar(20)  NOT NULL,
    puntos         numeric(14,2) NOT NULL,

    -- Qué hecho económico lo generó. Sin esto, dos renglones de +100 puntos sobre
    -- la misma factura son indistinguibles: uno pudo ser la emisión de contado y
    -- el otro el complemento que la pagó, y sólo uno de los dos debe existir.
    evento         varchar(30),

    -- Identidad del evento y única llave de idempotencia. Formato:
    --     erp|factura_contado|<encabezado>
    --     erp|anticipo|<encabezado>
    --     erp|complemento_pago|<encabezado cp>|<encabezado factura>
    --     erp|nota_credito|<encabezado>
    --     erp|cancelacion|<clave del movimiento reversado>
    --     kepler|<suc>|<gen>|<nat>|<gpo>|<tipo>|<folio>
    doc_clave      varchar(200),

    -- Documento que causó el movimiento (encabezadomov.id_encabezado). Es por
    -- donde se detecta una cancelación: se compara contra factura.statusfactura.
    doc_ref        integer,

    -- Documento relacionado: la factura que el complemento paga, o la que la nota
    -- de crédito afecta. Permite preguntar "cuántos puntos otorgó esta factura"
    -- sumando doc_ref y doc_ref_rel.
    doc_ref_rel    integer,

    -- ── Rastro del documento origen ────────────────────────────────────────
    -- 'kepler' | 'erp'. Las ventas históricas viven en Kepler y las nuevas en el
    -- ERP; durante la transición ambos orígenes conviven.
    origen         varchar(10),

    -- Llave natural del documento. En Kepler son la primaria de kdm1
    -- (c1,c2,c3,c4,c5,c6) y en el ERP las mismas de encabezadomov. Que coincidan
    -- no es casualidad: el ERP heredó la estructura documental.
    doc_sucursal   varchar(10),
    doc_genero     varchar(10),
    doc_naturaleza varchar(10),
    doc_grupo      numeric(4,0),
    doc_tipo       numeric(4,0),
    doc_folio      varchar(20),
    doc_fecha      timestamp,

    -- Total con impuestos y base sobre la que se calculó (total − IVA). Se guardan
    -- las dos para poder auditar sin volver al sistema origen.
    doc_importe    numeric(15,2),
    doc_base       numeric(15,2),

    clasificacion  varchar(20),
    tasa_aplicada  numeric(10,6),

    comentario     varchar(300),
    usuario_id     integer,
    fecha_registro timestamp    NOT NULL DEFAULT NOW()
);

-- Por si el esquema ya traía una versión anterior de la tabla: agrega lo que le
-- falte sin tocar lo que ya tenga.
ALTER TABLE puntos_movimientos
    ADD COLUMN IF NOT EXISTS evento      varchar(30),
    ADD COLUMN IF NOT EXISTS doc_clave   varchar(200),
    ADD COLUMN IF NOT EXISTS doc_ref     integer,
    ADD COLUMN IF NOT EXISTS doc_ref_rel integer;

-- ── 3. Restricciones ────────────────────────────────────────────────────────
-- DROP + ADD porque ADD CONSTRAINT no admite IF NOT EXISTS. Es el único modo de
-- que el script se pueda volver a correr sin fallar.
ALTER TABLE puntos_movimientos
    DROP CONSTRAINT IF EXISTS puntos_movimientos_tipo_ck,
    DROP CONSTRAINT IF EXISTS puntos_movimientos_signo_ck,
    DROP CONSTRAINT IF EXISTS puntos_movimientos_origen_ck,
    DROP CONSTRAINT IF EXISTS puntos_movimientos_evento_ck;

ALTER TABLE puntos_movimientos
    ADD CONSTRAINT puntos_movimientos_tipo_ck
        CHECK (tipo IN ('acumulacion', 'devolucion', 'reverso',
                        'canje', 'ajuste', 'expiracion')),

    -- `reverso` queda fuera de la regla de signo, igual que `ajuste`, y no por
    -- descuido: un reverso es el signo contrario de lo que deshace. Reversar una
    -- factura cancelada resta, pero reversar una NOTA DE CRÉDITO cancelada SUMA,
    -- porque devuelve los puntos que esa nota había quitado. Forzarlo a negativo
    -- haría fallar el CHECK y con él la transacción completa del periodo.
    ADD CONSTRAINT puntos_movimientos_signo_ck
        CHECK ((tipo = 'acumulacion' AND puntos >= 0)
            OR (tipo IN ('devolucion', 'canje', 'expiracion') AND puntos <= 0)
            OR  tipo IN ('ajuste', 'reverso')),

    ADD CONSTRAINT puntos_movimientos_origen_ck
        CHECK (origen IS NULL OR origen IN ('kepler', 'erp')),

    ADD CONSTRAINT puntos_movimientos_evento_ck
        CHECK (evento IS NULL OR evento IN ('factura_contado', 'complemento_pago',
                                            'anticipo', 'nota_credito', 'cancelacion'));

-- ── 4. Rellenar la clave de lo que ya estuviera registrado ──────────────────
-- En una instalación nueva esto no toca nada. Sólo importa si producción ya trae
-- movimientos de una versión anterior: esos renglones tendrían doc_clave NULL, el
-- cálculo no los reconocería como registrados, volvería a ofrecer los mismos
-- documentos y el cliente cobraría los puntos DOS VECES.
--
-- Va ANTES del índice único, que es lo que después impide el doble abono.
UPDATE puntos_movimientos
   SET evento = 'factura_contado'
 WHERE evento IS NULL
   AND tipo   = 'acumulacion';

-- Kepler: la llave natural es la misma que el código sigue construyendo.
UPDATE puntos_movimientos m
   SET doc_clave = 'kepler|' || COALESCE(m.doc_sucursal, '')
                 || '|'      || COALESCE(m.doc_genero, '')
                 || '|'      || COALESCE(m.doc_naturaleza, '')
                 || '|'      || COALESCE(m.doc_grupo, 0)
                 || '|'      || COALESCE(m.doc_tipo, 0)
                 || '|'      || COALESCE(m.doc_folio, '')
 WHERE m.doc_clave IS NULL
   AND m.origen    = 'kepler'
   AND m.tipo      = 'acumulacion';

-- ERP: la clave nueva se apoya en el encabezado, que los movimientos viejos no
-- guardaban; se recupera buscando el documento por su llave natural.
--
-- Los tipos NO coinciden entre las dos tablas y hay que forzarlos:
-- encabezadomov.suc es integer y doc_sucursal es varchar, porque el movimiento
-- guarda la sucursal como texto para que la misma columna sirva a Kepler, donde
-- no es un número. Sin el ::text esto revienta con "el operador no existe:
-- integer = character varying". nro_gpo_doc y nro_tp_doc sí son numéricos de los
-- dos lados. El folio se compara contra las dos columnas porque la versión
-- anterior del cálculo leía `fol_doc` y la actual lee `folio`.
UPDATE puntos_movimientos m
   SET doc_ref   = em.id_encabezado,
       doc_clave = 'erp|factura_contado|' || em.id_encabezado
  FROM encabezadomov em
 WHERE m.doc_clave IS NULL
   AND m.origen    = 'erp'
   AND m.tipo      = 'acumulacion'
   AND em.suc::text   = TRIM(m.doc_sucursal)
   AND TRIM(em.gen)   = TRIM(m.doc_genero)
   AND TRIM(em.nat)   = TRIM(m.doc_naturaleza)
   AND em.nro_gpo_doc = m.doc_grupo
   AND em.nro_tp_doc  = m.doc_tipo
   AND (TRIM(em.folio) = TRIM(m.doc_folio) OR TRIM(em.fol_doc) = TRIM(m.doc_folio));

-- ── 5. Índices ──────────────────────────────────────────────────────────────
-- El corazón de la idempotencia: un evento sólo se registra una vez. Si el
-- proceso se corre dos veces sobre el mismo periodo, el segundo intento choca
-- aquí y no duplica puntos.
--
-- Va sobre doc_clave y no sobre las seis columnas del documento porque un
-- complemento de pago liquida VARIAS facturas: su folio aparece repetido con
-- distinta factura pagada, y un índice por folio lo rechazaría como duplicado.
--
-- Antes hay que retirar los índices únicos de versiones anteriores. El de siete
-- columnas es el que rompe los complementos de pago; el de saldo_inicial pertenece
-- a un tipo de movimiento que ya no existe. Dejarlos puestos no es inocuo: el
-- primero rechazaría como duplicado el segundo cobro de un mismo complemento.
DROP INDEX IF EXISTS puntos_movimientos_documento_unq;
DROP INDEX IF EXISTS puntos_movimientos_saldo_inicial_unq;

-- Si esta creación falla por clave duplicada, NO la fuerces: significa que el
-- relleno del paso 4 encontró dos movimientos para el mismo documento, o sea que
-- ya existe un doble abono. Resuélvelo a mano; el ROLLBACK del final deja la base
-- como estaba mientras tanto.
CREATE UNIQUE INDEX IF NOT EXISTS puntos_movimientos_clave_unq
    ON puntos_movimientos (doc_clave)
    WHERE doc_clave IS NOT NULL;

CREATE INDEX IF NOT EXISTS puntos_movimientos_cliente_idx
    ON puntos_movimientos (cliente_id, fecha_registro DESC);

CREATE INDEX IF NOT EXISTS puntos_movimientos_cve_idx
    ON puntos_movimientos (cve_cli);

-- Para localizar rápido los movimientos de un documento al reversarlo, o al
-- calcular cuánto puede restar una nota de crédito.
CREATE INDEX IF NOT EXISTS puntos_movimientos_ref_idx
    ON puntos_movimientos (doc_ref)
    WHERE doc_ref IS NOT NULL;

CREATE INDEX IF NOT EXISTS puntos_movimientos_ref_rel_idx
    ON puntos_movimientos (doc_ref_rel)
    WHERE doc_ref_rel IS NOT NULL;

-- ── 6. Saldo ────────────────────────────────────────────────────────────────
-- Vista, no tabla: el saldo no puede desincronizarse de sus movimientos porque no
-- existe por separado. Las tres formas de perder puntos van separadas porque un
-- cliente que reclama tiene que poder ver si bajó por canjear, por devolver
-- mercancía o porque le cancelaron una factura.
--
-- DROP y no CREATE OR REPLACE: si ya existiera una versión con otras columnas,
-- reemplazarla falla con "no se pueden eliminar columnas de una vista". Como no
-- guarda datos, tirarla y rehacerla es inocuo.
DROP VIEW IF EXISTS v_puntos_saldo;

CREATE VIEW v_puntos_saldo AS
SELECT m.cliente_id,
       m.cve_cli,
       SUM(m.puntos)                                                     AS saldo,
       SUM(m.puntos) FILTER (WHERE m.tipo = 'acumulacion')               AS acumulados,
      -SUM(m.puntos) FILTER (WHERE m.tipo = 'canje')                     AS canjeados,
      -SUM(m.puntos) FILTER (WHERE m.tipo = 'devolucion')                AS devoluciones,
      -SUM(m.puntos) FILTER (WHERE m.tipo = 'reverso')                   AS reversos,
       SUM(m.puntos) FILTER (WHERE m.tipo = 'ajuste')                    AS ajustes,
       COUNT(*)      FILTER (WHERE m.tipo = 'acumulacion')               AS documentos,
       COUNT(*)      FILTER (WHERE m.origen = 'kepler')                  AS documentos_kepler,
       COUNT(*)      FILTER (WHERE m.origen = 'erp')                     AS documentos_erp,
       SUM(m.puntos) FILTER (WHERE m.evento = 'factura_contado')         AS por_contado,
       SUM(m.puntos) FILTER (WHERE m.evento = 'complemento_pago')        AS por_pagos,
       SUM(m.puntos) FILTER (WHERE m.evento = 'anticipo')                AS por_anticipos,
       MAX(m.doc_fecha)                                                  AS ultimo_documento
FROM puntos_movimientos m
GROUP BY m.cliente_id, m.cve_cli;

-- ── 7. Tarifas iniciales ────────────────────────────────────────────────────
-- Trasladadas de CalcularPuntos() de la aplicación de escritorio, para que el ERP
-- arranque calculando exactamente lo mismo.
--
-- CLIENTEVN no es una tasa sino (total/1000)*5, que equivale a 0.005 y por eso se
-- representa así.
INSERT INTO puntos_tarifas (clasificacion, importe_min, importe_max, tasa)
SELECT * FROM (VALUES
    ('CLIENTEVN',   200.00,  NULL::numeric, 0.005000),

    ('CLASICO',      10.00,  400.00,        0.013750),
    ('CLASICO',     401.00,  1200.00,       0.015000),
    ('CLASICO',    1201.00,  3200.00,       0.018750),
    ('CLASICO',    3201.00,  5000.00,       0.020000),
    ('CLASICO',    5001.00,  10000.00,      0.021250),
    ('CLASICO',   10001.00,  30000.00,      0.022500),
    ('CLASICO',   30001.00,  NULL,          0.023750),

    ('VIP',          10.00,  400.00,        0.026250),
    ('VIP',         401.00,  1200.00,       0.027500),
    ('VIP',        1201.00,  3200.00,       0.031250),
    ('VIP',        3201.00,  5000.00,       0.032500),
    ('VIP',        5001.00,  10000.00,      0.033750),
    ('VIP',       10001.00,  30000.00,      0.035000),
    ('VIP',       30001.00,  NULL,          0.036250),

    ('PREMIUM',      10.00,  400.00,        0.038750),
    ('PREMIUM',     401.00,  1200.00,       0.040000),
    ('PREMIUM',    1201.00,  3200.00,       0.043750),
    ('PREMIUM',    3201.00,  5000.00,       0.045000),
    ('PREMIUM',    5001.00,  10000.00,      0.046250),
    ('PREMIUM',   10001.00,  30000.00,      0.047500),
    ('PREMIUM',   30001.00,  NULL,          0.048750)
) AS t(clasificacion, importe_min, importe_max, tasa)
WHERE NOT EXISTS (SELECT 1 FROM puntos_tarifas);

-- ── 8. Permiso ──────────────────────────────────────────────────────────────
-- El padre puede no existir en producción; en ese caso el permiso se crea suelto
-- en vez de fallar, y la verificación del final lo delata.
INSERT INTO permisos (nombre, descripcion, es_modulo, modulo_padre)
SELECT 'puntos_socio_tiburon', 'Puntos Socio Tiburón: calcular y otorgar', false,
       (SELECT id_permiso FROM permisos WHERE nombre = 'credito_cobranza')
WHERE NOT EXISTS (SELECT 1 FROM permisos WHERE nombre = 'puntos_socio_tiburon');

-- ── 9. Verificación ─────────────────────────────────────────────────────────
-- Revisa estos cuatro resultados ANTES de cambiar el ROLLBACK por COMMIT.

-- (a) Los objetos, con sus columnas nuevas. Esperado: 4 columnas y 5 índices.
SELECT 'columnas' AS que, string_agg(column_name, ', ' ORDER BY column_name) AS detalle
  FROM information_schema.columns
 WHERE table_schema = 'srs' AND table_name = 'puntos_movimientos'
   AND column_name IN ('evento', 'doc_clave', 'doc_ref', 'doc_ref_rel')
UNION ALL
SELECT 'indices', string_agg(indexname, ', ' ORDER BY indexname)
  FROM pg_indexes
 WHERE schemaname = 'srs' AND tablename = 'puntos_movimientos'
UNION ALL
SELECT 'restricciones', string_agg(conname, ', ' ORDER BY conname)
  FROM pg_constraint
 WHERE conrelid = 'puntos_movimientos'::regclass AND contype = 'c'
UNION ALL
SELECT 'permiso', COALESCE((SELECT CASE WHEN modulo_padre IS NULL
                                        THEN 'creado SIN padre credito_cobranza'
                                        ELSE 'creado bajo credito_cobranza' END
                              FROM permisos
                             WHERE nombre = 'puntos_socio_tiburon'), 'NO CREADO');

-- (b) Las 22 tarifas, repartidas 1/7/7/7.
SELECT clasificacion, COUNT(*) AS escalones
  FROM puntos_tarifas
 GROUP BY clasificacion ORDER BY clasificacion;

-- (c) Movimientos preexistentes que el relleno NO pudo emparejar. Si sale algo,
--     esos documentos se volverían a ofrecer y otorgarían puntos por segunda vez:
--     hay que resolverlos antes de confirmar.
SELECT origen, COUNT(*) AS sin_clave
  FROM puntos_movimientos
 WHERE doc_clave IS NULL AND tipo = 'acumulacion'
 GROUP BY origen;

-- (d) El saldo queda legible desde la vista.
SELECT * FROM v_puntos_saldo ORDER BY saldo DESC LIMIT 10;

-- ── Cambia esto por COMMIT cuando la verificación cuadre ────────────────────
ROLLBACK;
