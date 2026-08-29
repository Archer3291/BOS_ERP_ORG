-- ============================================================================
--  Punto de Venta · Venta a crédito — esquema srs
--
--  QUÉ HABILITA
--  El punto de venta ya sabía vender de contado nada más. Con esto una cotización
--  de sucursal marcada como CRÉDITO (mdp = PPD) se puede vender en mostrador: no se
--  cobra en caja, la factura nominativa PPD se timbra al registrar la venta y el
--  importe queda en la cartera del cliente, para cobrarse después por Crédito y
--  Cobranza con su complemento de pago.
--
--  QUÉ TOCA ESTE SCRIPT
--  Nada de estructura: no hay tablas ni columnas nuevas. Sólo CATÁLOGOS de
--  contabilidad, que es lo único que el código no puede resolver solo:
--
--    1. poliza_documento   la plantilla de póliza de los documentos "_credito"
--                          del punto de venta (pedido 50, remisión 51, factura 52).
--                          Se CLONA la del contado: la póliza de la factura carga a
--                          clientes y genera la cartera; lo que cambia a crédito no
--                          es el asiento de la factura sino que NO se registra el
--                          cobro después.
--    2. poliza_cat_tipo_proceso   los tipo_proceso nuevos, para que la pantalla de
--                                 configuración de pólizas los muestre por nombre.
--
--  SIN ESTE SCRIPT la venta a crédito falla al facturar con "No se generó la
--  póliza del documento", porque GenerarDatosPoliza resuelve la plantilla por el
--  par (idtpdoc, tipo_proceso) y no encontraría ninguna para factura_credito.
--
--  TODO VA CALIFICADO CON srs. La aplicación conecta con `Search Path=public,srs`,
--  así que ejecutar fragmentos sueltos con el esquema escrito en cada sentencia
--  deja de ser peligroso.
--
--  CÓMO CORRERLO
--  Va en una transacción y TERMINA EN ROLLBACK a propósito. Córrelo, revisa la
--  verificación del final y, si cuadra, cambia el ROLLBACK por COMMIT y vuelve a
--  correrlo. Es idempotente.
-- ============================================================================

BEGIN;

-- ── 0. Red de seguridad ─────────────────────────────────────────────────────
DO $$
DECLARE
    v_faltan text := '';
BEGIN
    IF to_regclass('srs.poliza_documento')         IS NULL THEN v_faltan := v_faltan || ' poliza_documento';         END IF;
    IF to_regclass('srs.poliza_cat_tipo_proceso')  IS NULL THEN v_faltan := v_faltan || ' poliza_cat_tipo_proceso';  END IF;

    IF v_faltan <> '' THEN
        RAISE EXCEPTION 'ABORTADO: faltan tablas en srs:%. ¿Es el esquema correcto?', v_faltan;
    END IF;
END $$;

-- Sin la plantilla de contado no hay nada que clonar, y clonar "algo parecido"
-- sería peor que no hacer nada: la factura a crédito saldría con el asiento de
-- otro documento.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM srs.poliza_documento
        WHERE idtpdoc = 52 AND tipo_proceso = 'factura_contado'
    ) THEN
        RAISE EXCEPTION
            'ABORTADO: no existe la plantilla de póliza (idtpdoc 52, factura_contado) en srs. '
            'Es la que se clona para el crédito; configúrala primero en Contabilidad.';
    END IF;
END $$;


-- ── 1. tipo_proceso nuevos del punto de venta ───────────────────────────────
-- Sólo alimentan el selector de la pantalla de configuración de pólizas; el
-- encabezado no tiene llave foránea contra este catálogo.
INSERT INTO srs.poliza_cat_tipo_proceso (clave, descripcion, modulo, orden)
VALUES
    ('venta_sucursal_credito',         'Venta de Sucursal a Crédito',                  'Punto de venta', 241),
    ('venta_sucursal_credito_parcial', 'Venta de Sucursal a Crédito Parcial',          'Punto de venta', 242),
    ('venta_sucursal_credito_entrega', 'Entrega de Pendientes de Venta a Crédito',     'Punto de venta', 243)
ON CONFLICT (clave) DO NOTHING;


-- ── 2. Plantillas de póliza de los documentos a crédito ─────────────────────
-- Se clonan las del contado, renglón por renglón y evento por evento. El pedido y
-- la remisión del punto de venta no generan asiento hoy, pero se mapean igual para
-- que el par (idtpdoc, tipo_proceso) nunca quede huérfano si mañana lo generan.
INSERT INTO srs.poliza_documento (idtpdoc, id_tipo_poliza, evento, orden, activo, tipo_proceso)
SELECT pd.idtpdoc,
       pd.id_tipo_poliza,
       pd.evento,
       pd.orden,
       pd.activo,
       replace(pd.tipo_proceso, '_contado', '_credito')
FROM   srs.poliza_documento pd
WHERE  pd.idtpdoc IN (50, 51, 52)
  AND  pd.tipo_proceso IN ('pedido_contado', 'remision_contado', 'factura_contado')
  AND  NOT EXISTS (
           SELECT 1
           FROM   srs.poliza_documento x
           WHERE  x.idtpdoc      = pd.idtpdoc
             AND  x.tipo_proceso = replace(pd.tipo_proceso, '_contado', '_credito')
             AND  x.evento       = pd.evento
       );


-- ── 3. Verificación ─────────────────────────────────────────────────────────
-- Se espera al menos una fila para (52, factura_credito) apuntando a la MISMA
-- plantilla que (52, factura_contado).

SELECT 'plantillas del punto de venta' AS chequeo,
       pd.idtpdoc,
       pd.tipo_proceso,
       pt.nombre AS plantilla,
       pd.evento,
       pd.activo
FROM   srs.poliza_documento pd
LEFT   JOIN srs.poliza_tipo pt ON pt.id_tipo_poliza = pd.id_tipo_poliza
WHERE  pd.idtpdoc IN (50, 51, 52)
ORDER  BY pd.idtpdoc, pd.tipo_proceso, pd.evento;

SELECT 'tipo_proceso a crédito' AS chequeo, clave, descripcion, modulo
FROM   srs.poliza_cat_tipo_proceso
WHERE  clave LIKE 'venta_sucursal_credito%'
ORDER  BY orden;

-- Cambia por COMMIT cuando la verificación cuadre.
ROLLBACK;
