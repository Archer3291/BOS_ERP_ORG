-- ============================================================================
-- Catálogos de la pantalla /Contabilidad/ConfiguracionPolizas
--
-- Qué resuelve: la vista traía cuatro listas escritas a mano en el JavaScript
-- (tipos de cuenta, lados, orígenes de monto y tipos de proceso). Cada valor
-- nuevo obligaba a editar ConfiguracionPolizas.cshtml y volver a publicar.
-- Ahora salen de estas tablas, vía /ConfiguracionPolizas/GetCatalogosPoliza.
--
-- IMPORTANTE — no todos los catálogos crecen igual:
--
--   * poliza_cat_tipo_proceso  → crece libre. Es solo la llave de cruce de
--     poliza_documento.tipo_proceso contra encabezado.TipoPoceso
--     (PolizaConfigFactory.ObtenerTiposPoliza). Agregar un renglón aquí basta
--     para que aparezca en el selector de "Tipo proceso".
--
--   * poliza_cat_tipo_cuenta / _lado / _origen_monto → NO crecen solos.
--     Sus claves son valores de los ENUM de Postgres tipo_cuenta_poliza,
--     lado_poliza y origen_monto_poliza (ConfiguracionPolizasController
--     castea con ::tipo_cuenta_poliza al guardar la partida), y además
--     PolizaConfigFactory.ResolverCuenta / ResolverMonto hacen switch sobre
--     ellas. Para dar de alta una clave nueva hacen falta tres cosas:
--         1) ALTER TYPE <enum> ADD VALUE '<clave>';
--         2) el case correspondiente en PolizaConfigFactory;
--         3) el INSERT en la tabla de aquí abajo.
--     Lo que sí se puede editar sin tocar código en estos tres catálogos es
--     la metadata: descripción, etiqueta, reglas de captura, monto de ejemplo,
--     orden y si está activo.
--
-- Idempotente: se puede correr varias veces sin duplicar renglones.
-- ============================================================================

-- ── Ubicar el esquema donde viven las tablas de pólizas ─────────────────────
-- La conexión ERP_SRS trae "Search Path=public,srs_prod" y el ERP también usa
-- el esquema srs, así que no damos por hecho public: creamos los catálogos
-- junto a poliza_tipo, sea cual sea su esquema.
DO $$
DECLARE
    esquema text;
BEGIN
    SELECT n.nspname INTO esquema
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relname = 'poliza_tipo'
      AND c.relkind = 'r'
    ORDER BY array_position(current_schemas(false), n.nspname) NULLS LAST
    LIMIT 1;

    IF esquema IS NULL THEN
        RAISE EXCEPTION 'No se encontró la tabla poliza_tipo en el search_path actual (%). Revisa la conexión antes de correr este script.', current_setting('search_path');
    END IF;

    EXECUTE format('SET search_path TO %I, %s', esquema, current_setting('search_path'));
    RAISE NOTICE 'Creando catálogos de pólizas en el esquema %', esquema;
END $$;


-- ════════════════════════════════════════════════════════════════════════════
-- 1. Tipos de cuenta          (columna poliza_partida.tipo_cuenta)
-- ════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS poliza_cat_tipo_cuenta (
    id_tipo_cuenta       serial  PRIMARY KEY,
    clave                text    NOT NULL UNIQUE,
    descripcion          text    NOT NULL,
    etiqueta_cuenta      text,
    requiere_cuenta      boolean NOT NULL DEFAULT false,
    requiere_impuesto    boolean NOT NULL DEFAULT false,
    origen_monto_forzado text,
    orden                integer NOT NULL DEFAULT 0,
    activo               boolean NOT NULL DEFAULT true
);

COMMENT ON TABLE  poliza_cat_tipo_cuenta                      IS 'Catálogo de tipos de cuenta de una partida. Las claves deben existir en el ENUM tipo_cuenta_poliza y estar contempladas en PolizaConfigFactory.ResolverCuenta.';
COMMENT ON COLUMN poliza_cat_tipo_cuenta.clave                IS 'Valor guardado en poliza_partida.tipo_cuenta (ENUM tipo_cuenta_poliza).';
COMMENT ON COLUMN poliza_cat_tipo_cuenta.descripcion          IS 'Ayuda que se muestra bajo el select en la tabla de partidas.';
COMMENT ON COLUMN poliza_cat_tipo_cuenta.etiqueta_cuenta      IS 'Texto que muestra la vista previa cuando la cuenta se resuelve en automático (no hay cuenta fija capturada).';
COMMENT ON COLUMN poliza_cat_tipo_cuenta.requiere_cuenta      IS 'true = la partida no se guarda sin cuenta contable.';
COMMENT ON COLUMN poliza_cat_tipo_cuenta.requiere_impuesto    IS 'true = la partida no se guarda sin impuesto.';
COMMENT ON COLUMN poliza_cat_tipo_cuenta.origen_monto_forzado IS 'Si trae valor, al elegir este tipo de cuenta el origen de monto se fija a esa clave.';

INSERT INTO poliza_cat_tipo_cuenta
    (clave, descripcion, etiqueta_cuenta, requiere_cuenta, requiere_impuesto, origen_monto_forzado, orden)
VALUES
    ('FIJA',                  'Cuenta contable específica.',        NULL,                true,  false, NULL,        10),
    ('CLIENTE',               'Cuenta del cliente automática.',     'Cuenta cliente',    false, false, NULL,        20),
    ('PROVEEDOR',             'Cuenta del proveedor automática.',   'Cuenta proveedor',  false, false, NULL,        30),
    ('BANCO',                 'Cuenta bancaria del documento.',     'Cuenta banco',      false, false, NULL,        40),
    ('IMPUESTO',              'Cuenta del impuesto seleccionado.',  'Cuenta impuesto',   true,  true,  'IMPUESTOS', 50),
    ('DESCUENTO',             'Cuenta de descuentos.',              'Cuenta descuento',  true,  false, 'DESCUENTO', 60),
    ('DESGLOSE_FACTURA_PAGO', 'Desglose de facturas en complemento.','Cuenta cliente',   false, false, NULL,        70)
ON CONFLICT (clave) DO NOTHING;

-- Red de seguridad: si el ENUM tiene claves que este script no conoce, que
-- aparezcan igual en la pantalla (inactivas, para que alguien las revise).
INSERT INTO poliza_cat_tipo_cuenta (clave, descripcion, orden, activo)
SELECT e.enumlabel, e.enumlabel, 900 + e.enumsortorder, false
FROM pg_enum e
JOIN pg_type t ON t.oid = e.enumtypid
WHERE t.typname = 'tipo_cuenta_poliza'
ON CONFLICT (clave) DO NOTHING;


-- ════════════════════════════════════════════════════════════════════════════
-- 2. Lados                    (columna poliza_partida.lado)
-- ════════════════════════════════════════════════════════════════════════════
-- Por partida doble esto no va a crecer nunca; existe para que la vista no
-- conserve ni un arreglo escrito a mano y para poder renombrar la etiqueta.
CREATE TABLE IF NOT EXISTS poliza_cat_lado (
    id_lado     serial  PRIMARY KEY,
    clave       text    NOT NULL UNIQUE,
    descripcion text    NOT NULL,
    orden       integer NOT NULL DEFAULT 0,
    activo      boolean NOT NULL DEFAULT true
);

COMMENT ON TABLE  poliza_cat_lado       IS 'Catálogo de lados de la partida. Las claves deben existir en el ENUM lado_poliza; PolizaConfigFactory valida explícitamente DEBE/HABER.';
COMMENT ON COLUMN poliza_cat_lado.clave IS 'Valor guardado en poliza_partida.lado (ENUM lado_poliza).';

INSERT INTO poliza_cat_lado (clave, descripcion, orden) VALUES
    ('DEBE',  'Cargo a la cuenta.', 10),
    ('HABER', 'Abono a la cuenta.', 20)
ON CONFLICT (clave) DO NOTHING;

INSERT INTO poliza_cat_lado (clave, descripcion, orden, activo)
SELECT e.enumlabel, e.enumlabel, 900 + e.enumsortorder, false
FROM pg_enum e
JOIN pg_type t ON t.oid = e.enumtypid
WHERE t.typname = 'lado_poliza'
ON CONFLICT (clave) DO NOTHING;


-- ════════════════════════════════════════════════════════════════════════════
-- 3. Orígenes de monto        (columna poliza_partida.origen_monto)
-- ════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS poliza_cat_origen_monto (
    id_origen_monto serial        PRIMARY KEY,
    clave           text          NOT NULL UNIQUE,
    descripcion     text          NOT NULL,
    monto_ejemplo   numeric(18,2) NOT NULL DEFAULT 0,
    orden           integer       NOT NULL DEFAULT 0,
    activo          boolean       NOT NULL DEFAULT true
);

COMMENT ON TABLE  poliza_cat_origen_monto               IS 'Catálogo de orígenes de monto. Las claves deben existir en el ENUM origen_monto_poliza y estar contempladas en PolizaConfigFactory.ResolverMonto (lo que no cae en un case vale 0).';
COMMENT ON COLUMN poliza_cat_origen_monto.clave         IS 'Valor guardado en poliza_partida.origen_monto (ENUM origen_monto_poliza).';
COMMENT ON COLUMN poliza_cat_origen_monto.monto_ejemplo IS 'Importe ficticio que usa la vista previa para mostrar si la póliza cuadra. No afecta ninguna póliza real.';

INSERT INTO poliza_cat_origen_monto (clave, descripcion, monto_ejemplo, orden) VALUES
    ('TOTAL',     'Total del documento.',      116.00, 10),
    ('SUBTOTAL',  'Subtotal del documento.',   100.00, 20),
    ('IMPUESTOS', 'Monto del impuesto.',        16.00, 30),
    ('COSTO',     'Costo promedio de compra.',  60.00, 40),
    ('DESCUENTO', 'Total de descuentos.',        0.00, 50)
ON CONFLICT (clave) DO NOTHING;

INSERT INTO poliza_cat_origen_monto (clave, descripcion, monto_ejemplo, orden, activo)
SELECT e.enumlabel, e.enumlabel, 0, 900 + e.enumsortorder, false
FROM pg_enum e
JOIN pg_type t ON t.oid = e.enumtypid
WHERE t.typname = 'origen_monto_poliza'
ON CONFLICT (clave) DO NOTHING;


-- ════════════════════════════════════════════════════════════════════════════
-- 4. Tipos de proceso         (columna poliza_documento.tipo_proceso)
-- ════════════════════════════════════════════════════════════════════════════
-- Este es el catálogo que de verdad crece. La lista que traía la vista tenía
-- 14 renglones y el código emite más de 45 valores distintos: varias pólizas
-- solo se podían amarrar editando el .cshtml. El seed de abajo se armó
-- recorriendo todas las asignaciones de TipoPoceso en el proyecto.
CREATE TABLE IF NOT EXISTS poliza_cat_tipo_proceso (
    id_tipo_proceso serial  PRIMARY KEY,
    clave           text    NOT NULL UNIQUE,
    descripcion     text    NOT NULL,
    modulo          text,
    orden           integer NOT NULL DEFAULT 0,
    activo          boolean NOT NULL DEFAULT true
);

COMMENT ON TABLE  poliza_cat_tipo_proceso             IS 'Catálogo de tipos de proceso que pueden disparar una póliza. Crece libre: la clave solo se cruza contra encabezado.TipoPoceso en PolizaConfigFactory.ObtenerTiposPoliza.';
COMMENT ON COLUMN poliza_cat_tipo_proceso.clave       IS 'Debe coincidir EXACTO (mayúsculas incluidas) con el TipoPoceso que arma el controlador que genera el documento.';
COMMENT ON COLUMN poliza_cat_tipo_proceso.modulo      IS 'Agrupador del selector; se usa como <optgroup> en la pantalla.';

INSERT INTO poliza_cat_tipo_proceso (clave, descripcion, modulo, orden) VALUES
    -- ── Ventas ──────────────────────────────────────────────────────────────
    ('cotizacion_contado',                 'Cotización de Contado',                  'Ventas',      10),
    ('cotizacion_credito',                 'Cotización de Crédito',                  'Ventas',      20),
    ('pedido_contado',                     'Pedido de Contado',                      'Ventas',      30),
    ('pedido_credito',                     'Pedido de Crédito',                      'Ventas',      40),
    ('pedido_anticipo',                    'Pedido de Anticipo',                     'Ventas',      50),
    ('remision_contado',                   'Remisión de Contado',                    'Ventas',      60),
    ('remision_credito',                   'Remisión de Crédito',                    'Ventas',      70),
    ('remision_anticipo',                  'Remisión de Anticipo',                   'Ventas',      80),
    ('factura_contado',                    'Factura de Contado',                     'Ventas',      90),
    ('factura_credito',                    'Factura de Crédito',                     'Ventas',     100),
    ('factura_anticipo',                   'Factura de Anticipo',                    'Ventas',     110),
    ('factura_global',                     'Factura Global',                         'Ventas',     120),
    ('factura_complemento',                'Factura de Complemento',                 'Ventas',     130),
    ('factura_arrendamiento',              'Factura de Arrendamiento',               'Ventas',     140),
    ('nc_devolucion',                      'Nota de Crédito por Devolución',         'Ventas',     150),
    ('nc_descuento_bonificacion',          'Nota de Crédito por Descuento o Bonificación', 'Ventas', 160),
    ('egreso_devolucion_cliente',          'Egreso por Devolución a Cliente',        'Ventas',     170),
    ('aplicacion_anticipo',                'Aplicación de Anticipo',                 'Ventas',     180),

    -- ── Punto de venta ──────────────────────────────────────────────────────
    ('venta_sucursal',                     'Venta de Sucursal',                      'Punto de venta', 210),
    ('venta_sucursal_parcial',             'Venta de Sucursal Parcial Pagada',       'Punto de venta', 220),
    ('venta_sucursal_parcial_por_cobrar',  'Venta de Sucursal Parcial por Cobrar',   'Punto de venta', 230),
    ('venta_sucursal_entrega_pendiente',   'Venta de Sucursal con Entrega Pendiente','Punto de venta', 240),
    ('apertura_caja',                      'Apertura de Caja',                       'Punto de venta', 250),
    ('apertura_caja_aprobada',             'Apertura de Caja Aprobada',              'Punto de venta', 260),

    -- ── Crédito y cobranza / Tesorería ──────────────────────────────────────
    ('cobro_cliente',                      'Cobro a Cliente',                        'Crédito y cobranza', 310),
    ('aplicacion_cobro',                   'Aplicación de Cobro',                    'Crédito y cobranza', 320),
    ('complemento_pago',                   'Factura Complemento de Pago',            'Crédito y cobranza', 330),
    ('pago_proveedor',                     'Pago a Proveedor',                       'Crédito y cobranza', 340),
    ('registro_carteras_cliente',          'Carga Inicial de Carteras Cliente',      'Crédito y cobranza', 350),
    ('registro_carteras_proveedor',        'Carga Inicial de Carteras Proveedor',    'Crédito y cobranza', 360),

    -- ── Compras y gastos ────────────────────────────────────────────────────
    ('orden_compra',                       'Orden de Compra',                        'Compras',    410),
    ('orden_compra_directa',               'Orden de Compra Directa',                'Compras',    420),
    ('oc_internacional',                   'Orden de Compra Internacional',          'Compras',    430),
    ('orden_gasto',                        'Orden de Gasto',                         'Compras',    440),

    -- ── Inventario y almacén ────────────────────────────────────────────────
    ('ingreso_directo',                    'Ingreso Directo',                        'Inventario', 510),
    ('ingreso_parcial',                    'Ingreso Parcial',                        'Inventario', 520),
    ('ingreso_completo',                   'Ingreso Completo',                       'Inventario', 530),
    ('ingreso_almacen',                    'Ingreso a Almacén',                      'Inventario', 540),
    ('ingreso_cuerantena',                 'Ingreso a Cuarentena',                   'Inventario', 550),
    ('entrada_extraordinaria',             'Entrada Extraordinaria',                 'Inventario', 560),
    ('salida-donacion',                    'Salida por Donación',                    'Inventario', 570),
    ('Devolucion',                         'Devolución a Proveedor',                 'Inventario', 580),
    ('Parcial',                            'Recepción Parcial',                      'Inventario', 590),
    ('discrepancia',                       'Discrepancia de Almacén',                'Inventario', 600),
    ('carga_layout',                       'Carga de Layout de Productos',           'Inventario', 610),

    -- ── Traslados ───────────────────────────────────────────────────────────
    ('Traslado',                           'Traslado',                               'Traslados',  710),
    ('envio',                              'Envío de Traslado',                      'Traslados',  720),
    ('Recepcion',                          'Recepción de Traslado',                  'Traslados',  730),
    ('traspaso_inventario',                'Traspaso de Inventario',                 'Traslados',  740)
ON CONFLICT (clave) DO NOTHING;

-- Nada de lo ya configurado se puede perder del selector: si alguna póliza
-- apunta a un tipo_proceso que no está arriba, se da de alta solo.
-- 'ingreso_cuerantena' y las claves con mayúscula (Devolucion, Parcial,
-- Traslado, Recepcion) están escritas tal cual el código las emite: si se
-- "corrigen" aquí, esas pólizas dejan de cruzar.
INSERT INTO poliza_cat_tipo_proceso (clave, descripcion, modulo, orden)
SELECT DISTINCT pd.tipo_proceso, pd.tipo_proceso, 'Sin clasificar', 990
FROM poliza_documento pd
WHERE pd.tipo_proceso IS NOT NULL
  AND btrim(pd.tipo_proceso) <> ''
ON CONFLICT (clave) DO NOTHING;


-- ── Verificación ────────────────────────────────────────────────────────────
-- Tipos de proceso ya configurados en pólizas que quedaron "Sin clasificar":
--
--   SELECT c.clave, COUNT(pd.*) AS polizas
--   FROM poliza_cat_tipo_proceso c
--   LEFT JOIN poliza_documento pd ON pd.tipo_proceso = c.clave
--   WHERE c.modulo = 'Sin clasificar'
--   GROUP BY c.clave ORDER BY 1;
--
-- Claves inactivas que el ENUM tenía y el seed no conocía:
--
--   SELECT 'tipo_cuenta' cat, clave FROM poliza_cat_tipo_cuenta   WHERE NOT activo
--   UNION ALL SELECT 'lado',         clave FROM poliza_cat_lado          WHERE NOT activo
--   UNION ALL SELECT 'origen_monto', clave FROM poliza_cat_origen_monto  WHERE NOT activo;
