-- ============================================================================
-- Facturación libre — alta del tipo de documento y permisos contables
--
-- Emite un CFDI sin cadena documental: se captura cliente y conceptos, y se
-- timbra. No toca inventario (para eso está la remisión), pero sí genera
-- documento, póliza, cartera y —si es de contado— su cobro con documento CXC.
--
-- Correr una sola vez, contra el esquema 
-- ============================================================================

SET search_path TO srs;

BEGIN;

-- ── 1. Tipo de documento ────────────────────────────────────────────────────
-- Área 24 (Facturación), la misma donde vive la factura de arrendamiento (FAR).
-- Se eligió un área propia y no un canal de ventas porque la factura libre no
-- pertenece a Nacionales, Industriales ni Sucursales: el folio sería engañoso.
-- idtpdoc es GENERATED ALWAYS, de ahí el OVERRIDING: el 92 va fijo porque el código
-- lo trae como constante (FacturaLibreController.TpDocFacturaLibre), igual que GLFAC=91
-- o CXC=70. Después se adelanta la secuencia para que el siguiente alta no choque.
INSERT INTO tpdoc (idtpdoc, idarea, tpdoc, abreviaturatpdoc, consec, fch)
OVERRIDING SYSTEM VALUE
VALUES (92, 24, 'Factura Libre', 'FACLIB', '1', NOW())
ON CONFLICT (idtpdoc) DO NOTHING;

SELECT setval('tpdoc_idtpdoc_seq', GREATEST((SELECT MAX(idtpdoc) FROM tpdoc), 92));


-- ── 2. Permitir cartera para FACLIB ─────────────────────────────────────────
-- registrar_cartera valida la naturaleza contra una lista blanca y aborta con
-- TIPO_PROCESO_INVALIDO para cualquier otra. Sin esta línea, toda factura libre
-- reventaría al intentar generar su cartera.
--
-- Se reemplaza únicamente esa condición; el resto del cuerpo queda igual.
CREATE OR REPLACE FUNCTION registrar_cartera(
    p_id_poliza integer, p_creado_por integer, p_id_empresa integer)
RETURNS TABLE(cartera_id integer, monto_total numeric, saldo_pendiente numeric, tipo text)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_tipo_poliza TEXT;
    v_estado_poliza TEXT;
    v_referencia INTEGER;
    v_fecha DATE;
    v_usuario INTEGER;
    v_tipo_proceso TEXT;
    v_monto NUMERIC;
    v_comentarios TEXT;
    v_referencia_b TEXT;
    v_moneda TEXT;
    v_nat TEXT;
    v_plazo_dias NUMERIC;
    v_cartera_id INTEGER;
BEGIN
    ------------------------------------------------------------------
    -- 1. Validar póliza
    ------------------------------------------------------------------
    IF EXISTS (
        SELECT 1 FROM cartera_clientes
        WHERE poliza_id = p_id_poliza AND empresa_id = p_id_empresa
    )
    OR EXISTS (
        SELECT 1 FROM cartera_proveedores
        WHERE poliza_id = p_id_poliza AND empresa_id = p_id_empresa
    )
    THEN
        RAISE EXCEPTION USING
            ERRCODE = 'P2001',
            MESSAGE = 'POLIZA_YA_REGISTRADA',
            DETAIL = FORMAT('La póliza %s ya generó cartera', p_id_poliza);
    END IF;

    SELECT cp.nombre, p.estado, p.referencia::int, p.fecha
    INTO v_tipo_poliza, v_estado_poliza, v_referencia, v_fecha
    FROM polizas p
    INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = p.tipo
    WHERE p.id_poliza = p_id_poliza AND p.empresa_id = p_id_empresa;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING
            ERRCODE = 'P2002',
            MESSAGE = 'POLIZA_INEXISTENTE',
            DETAIL = FORMAT('La póliza %s no existe', p_id_poliza);
    END IF;

    ------------------------------------------------------------------
    -- 2. Traer encabezado
    ------------------------------------------------------------------
    SELECT refe, tipo_proceso, imp, coment1, coment2, ccy, nat
    INTO v_usuario, v_tipo_proceso, v_monto, v_comentarios, v_referencia_b, v_moneda, v_nat
    FROM encabezadomov
    WHERE id_encabezado = v_referencia;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING
            ERRCODE = 'P2003',
            MESSAGE = 'POLIZA_ENCABEZADO_INVALIDO',
            DETAIL = FORMAT('La póliza %s no tiene encabezado válido', p_id_poliza);
    END IF;

    IF v_monto < 0 THEN
        RAISE EXCEPTION USING
            ERRCODE = 'P2004',
            MESSAGE = 'MONTO_FACTURA_INVALIDO',
            DETAIL = 'El monto de la factura debe ser mayor a cero';
    END IF;

    IF v_nat IS NULL THEN
        RAISE EXCEPTION 'NAT no puede ser NULL para la póliza %', p_id_poliza;
    END IF;

    ------------------------------------------------------------------
    -- 3. FACTURA PROVEEDOR (egreso)
    ------------------------------------------------------------------
    IF v_nat IN ('OC', 'GTO', 'OCD', 'OCDI') THEN

        SELECT pl_crd INTO v_plazo_dias
        FROM catproveedores WHERE id_prov = v_usuario;

        INSERT INTO cartera_proveedores (
            proveedor_id, encabezado_id, monto_total, saldo_pendiente, estado,
            creado_por, poliza_id, fecha_emision, fecha_vencimiento, moneda, empresa_id
        )
        VALUES (
            v_usuario, v_referencia, v_monto, v_monto, 'pendiente',
            p_creado_por, p_id_poliza, now(),
            now() + (COALESCE(v_plazo_dias, 0) * INTERVAL '1 day'),
            v_moneda, p_id_empresa
        )
        RETURNING id_cartera_proveedor INTO v_cartera_id;

        RETURN QUERY SELECT v_cartera_id, v_monto, v_monto, 'proveedor'::text;
        RETURN;

    END IF;

    ------------------------------------------------------------------
    -- 4. FACTURA CLIENTE (ingreso)
    --    FACLIB se agrega aquí: la factura libre genera cargo al cliente
    --    igual que cualquier factura de venta.
    ------------------------------------------------------------------
    IF v_nat IN ('VNFAC', 'VIFAC', 'VSFAC', 'VINFAC',
                 'VNREM', 'VIREM', 'VSREM', 'VINREM',
                 'ABNCLI', 'RICD', 'GLFAC', 'FACLIB') THEN

        SELECT pl_crd INTO v_plazo_dias
        FROM catclientes WHERE id_cliente = v_usuario;

        INSERT INTO cartera_clientes (
            cliente_id, encabezado_id, monto_total, saldo_pendiente, estado,
            creado_por, poliza_id, fecha_emision, fecha_vencimiento, moneda, empresa_id
        )
        VALUES (
            v_usuario, v_referencia, v_monto, v_monto, 'pendiente',
            p_creado_por, p_id_poliza, now(),
            now() + (COALESCE(v_plazo_dias, 0) * INTERVAL '1 day'),
            v_moneda, p_id_empresa
        )
        RETURNING id_cartera_cliente INTO v_cartera_id;

        RETURN QUERY SELECT v_cartera_id, v_monto, v_monto, 'cliente'::text;
        RETURN;

    END IF;

    ------------------------------------------------------------------
    -- 5. Seguridad extra
    ------------------------------------------------------------------
    RAISE EXCEPTION USING
        ERRCODE = 'P2005',
        MESSAGE = FORMAT('TIPO_PROCESO_INVALIDO - No se puede generar cartera para el movimiento: %s', v_nat),
        DETAIL = FORMAT('No se puede generar cartera para el movimiento: %s', v_nat);

END;
$function$;

-- ── 3. Plantilla contable de la factura libre ───────────────────────────────
-- Las pólizas NO las arman los handlers de C# (handlers/Polizas/* quedó sin uso):
-- las arma PolizaConfigFactory leyendo poliza_documento → poliza_tipo → poliza_partida.
-- Sin estas filas, GenerarDatosPoliza lanza "No hay pólizas configuradas para este
-- documento" y la factura libre no se puede emitir.
--
-- La plantilla es la de una venta (tipo 7) MENOS los dos renglones de costo: la
-- factura libre no descarga almacén, así que no hay salida a costo de venta que
-- registrar. Las cuentas son las mismas que ya usan VIFAC y VSFAC.
-- Van DOS plantillas, una por condición de pago, porque poliza_documento es único por
-- (idtpdoc, id_tipo_poliza, evento) y no incluye tipo_proceso: una sola plantilla no
-- puede mapearse a contado y a crédito a la vez. Es la misma razón por la que VIFAC
-- tiene sus tipos 7 y 19. Los renglones contables son idénticos en ambas: lo que cambia
-- entre contado y crédito no es el cargo al cliente, sino si la cartera se salda
-- enseguida con el documento CXC del cobro o queda abierta.
INSERT INTO poliza_tipo (id_categoria, nombre, descripcion, activo, fecha_creacion, empresa_id, clasificacion_id)
SELECT pt.id_categoria, n.nombre, n.descripcion, true, NOW(), pt.empresa_id, pt.clasificacion_id
FROM poliza_tipo pt
CROSS JOIN (VALUES
    ('Factura libre contado', 'Poliza de factura libre de contado'),
    ('Factura libre credito', 'Poliza de factura libre a credito')
) AS n(nombre, descripcion)
WHERE pt.id_tipo_poliza = 7
  AND NOT EXISTS (
      SELECT 1 FROM poliza_tipo x WHERE x.nombre = n.nombre AND x.empresa_id = pt.empresa_id
  );

-- Renglones de cada plantilla, copiando las cuentas de la venta ya configurada.
INSERT INTO poliza_partida
    (id_tipo_poliza, orden, tipo_cuenta, id_cuenta_contable, lado, origen_monto, factor, descripcion_template, impuesto_id)
SELECT nuevo.id_tipo_poliza, v.orden, v.tipo_cuenta, v.id_cuenta_contable,
       v.lado, v.origen_monto, v.factor, v.descripcion_template, v.impuesto_id
FROM poliza_tipo nuevo
CROSS JOIN LATERAL (
    SELECT orden, tipo_cuenta, id_cuenta_contable, lado, origen_monto, factor,
           CASE WHEN origen_monto = 'SUBTOTAL'
                THEN 'Ingresos por factura libre'
                ELSE descripcion_template END AS descripcion_template,
           impuesto_id
    FROM poliza_partida
    WHERE id_tipo_poliza = 7
      AND origen_monto <> 'COSTO'   -- sin inventario no hay costo de venta que registrar
) v
WHERE nuevo.nombre IN ('Factura libre contado', 'Factura libre credito')
  AND NOT EXISTS (
      SELECT 1 FROM poliza_partida pp WHERE pp.id_tipo_poliza = nuevo.id_tipo_poliza
  );

-- Mapeo documento → plantilla, una por condición de pago.
INSERT INTO poliza_documento (idtpdoc, id_tipo_poliza, evento, orden, activo, tipo_proceso)
SELECT 92, pt.id_tipo_poliza, 'AL_CREAR', 1, true, m.tipo_proceso
FROM poliza_tipo pt
JOIN (VALUES
    ('Factura libre contado', 'factura_contado'),
    ('Factura libre credito', 'factura_credito')
) AS m(nombre, tipo_proceso) ON m.nombre = pt.nombre
WHERE NOT EXISTS (
    SELECT 1 FROM poliza_documento pd
    WHERE pd.idtpdoc = 92 AND pd.id_tipo_poliza = pt.id_tipo_poliza AND pd.evento = 'AL_CREAR'
);

COMMIT;

-- ── Verificación ────────────────────────────────────────────────────────────
-- SELECT idtpdoc, idarea, tpdoc, abreviaturatpdoc FROM tpdoc WHERE idtpdoc = 92;
--
-- SELECT pd.idtpdoc, pd.tipo_proceso, pt.nombre, pp.orden, pp.tipo_cuenta, pp.lado,
--        pp.origen_monto, cf.codigo
-- FROM poliza_documento pd
-- JOIN poliza_tipo    pt ON pt.id_tipo_poliza = pd.id_tipo_poliza
-- JOIN poliza_partida pp ON pp.id_tipo_poliza = pt.id_tipo_poliza
-- LEFT JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = pp.id_cuenta_contable
-- WHERE pd.idtpdoc = 92 ORDER BY pd.tipo_proceso, pp.orden;
