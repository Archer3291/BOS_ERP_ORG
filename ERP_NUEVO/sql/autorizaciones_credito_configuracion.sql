-- Guarda la configuración capturada en la pantalla de PEDIDO al solicitar la autorización
-- de crédito (forma de pago, uso de CFDI, método de pago, plazo, fecha de pago, moneda,
-- paridad, vendedor, concepto, comentarios y orden de compra).
--
-- Por qué hace falta: cuando el vendedor pide autorización, el pedido todavía no existe.
-- Al aprobar, VIPedido.AprobarPedido reconstruye el pedido desde el documento padre (la
-- cotización), y la cotización NO conoce la forma de pago ni el uso de CFDI: esos datos se
-- definen hasta el pedido. Sin esta columna se pierden y la remisión los pide de nuevo.
--
-- El código funciona con o sin esta columna (la detecta en information_schema); mientras no
-- se ejecute, el pedido autorizado seguirá naciendo sin esos datos.

ALTER TABLE autorizaciones_credito
    ADD COLUMN IF NOT EXISTS configuracion_documento text;

COMMENT ON COLUMN autorizaciones_credito.configuracion_documento IS
    'JSON con la configuración del formulario de pedido al momento de solicitar la '
    'autorización de crédito. Lo escribe VIPedido.EnviarSolicitudGerente y lo aplica '
    'VIPedido.AprobarPedido al generar el pedido.';
