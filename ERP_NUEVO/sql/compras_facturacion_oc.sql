-- ============================================================================
-- Paso 8 del flujo de compras: facturacion de la orden de compra.
--
-- archivos_compras_oc guarda cotizaciones, facturas y XML en la misma tabla,
-- pero no habia forma de distinguirlos. La columna 'tipo' permite saber que
-- ordenes ya tienen factura registrada y cuales siguen pendientes.
--
-- Es aditiva y reversible:  ALTER TABLE archivos_compras_oc DROP COLUMN tipo;
-- ============================================================================

ALTER TABLE archivos_compras_oc
    ADD COLUMN IF NOT EXISTS tipo varchar(20);

COMMENT ON COLUMN archivos_compras_oc.tipo IS
    'cotizacion | factura | xml — origen del archivo dentro del proceso de compras';

-- Los registros previos son cotizaciones cargadas al definir el metodo de pago
UPDATE archivos_compras_oc
SET tipo = 'cotizacion'
WHERE tipo IS NULL;

-- La consulta de "ordenes por facturar" filtra por encabezado + tipo
CREATE INDEX IF NOT EXISTS archivos_compras_oc_encabezado_tipo_idx
    ON archivos_compras_oc (encabezado_id, tipo);

-- Datos del CFDI del proveedor, para no re-parsear el XML en cada consulta
-- y para poder auditar diferencias contra la orden de compra.
CREATE TABLE IF NOT EXISTS factura_proveedor_oc (
    id_factura_oc   integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    encabezado_id   integer NOT NULL REFERENCES encabezadomov (id_encabezado),
    uuid            varchar(64),
    serie           varchar(32),
    folio           varchar(64),
    rfc_emisor      varchar(20),
    nombre_emisor   varchar(255),
    fecha_emision   timestamp,
    moneda          varchar(10),
    metodo_pago     varchar(10),
    forma_pago      varchar(10),
    subtotal        numeric(18, 4),
    descuento       numeric(18, 4),
    impuestos       numeric(18, 4),
    retenciones     numeric(18, 4),
    total           numeric(18, 4),
    diferencias     jsonb,
    registrado_por  integer,
    fecha_registro  timestamp NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS factura_proveedor_oc_uuid_idx
    ON factura_proveedor_oc (uuid)
    WHERE uuid IS NOT NULL;

CREATE INDEX IF NOT EXISTS factura_proveedor_oc_encabezado_idx
    ON factura_proveedor_oc (encabezado_id);
