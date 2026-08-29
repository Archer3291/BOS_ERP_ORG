using BOS_ERP.Controllers;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Npgsql;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Resultado de evaluar el crédito de un cliente contra el total de un documento de venta.
    /// </summary>
    public class ResultadoCreditoVenta
    {
        /// <summary>False cuando la venta no es a crédito: no hay nada que validar.</summary>
        public bool Aplica { get; set; }

        /// <summary>False solo cuando el documento debe rechazarse.</summary>
        public bool Permitido { get; set; } = true;

        /// <summary>SIN_LIMITE · DISPONIBLE · POR_VENCER · EXCEDIDO · SUSPENDIDO</summary>
        public string Estatus { get; set; } = "SIN_LIMITE";

        public string Mensaje { get; set; } = "";

        /// <summary>True si un gerente ya autorizó este documento (o alguno de sus padres).</summary>
        public bool Autorizado { get; set; }

        public decimal Limite { get; set; }
        public decimal Usado { get; set; }
        public decimal Disponible => Limite - Usado;
        public decimal TotalDocumento { get; set; }
        public decimal Excedente { get; set; }
    }

    /// <summary>
    /// Validación de crédito compartida por todos los canales de venta (VI/VN/VS/VIN/VT).
    ///
    /// Regla: solo aplica cuando la venta es a CRÉDITO. En contado o anticipo el cliente
    /// paga al momento, así que no se compromete línea de crédito y no se valida nada.
    ///
    /// El crédito usado se toma de cartera_clientes (saldo pendiente no cancelado), igual
    /// que en DatosGeneralesController.BuscarCliente, para que el front y el back siempre
    /// evalúen contra la misma cifra.
    /// </summary>
    public static class CreditoVentasHelper
    {
        /// <summary>A partir de este % de uso se avisa (sin bloquear).</summary>
        public const decimal UmbralAviso = 0.80m;

        /// <summary>
        /// El toggle del front manda "credito"/"contado"/"anticipo"; el encabezado guarda
        /// el método de pago SAT ("PPD" = pago en parcialidades/diferido = crédito).
        /// </summary>
        public static bool EsVentaCredito(string tipoPago)
        {
            string t = (tipoPago ?? "").Trim();
            return t.Equals("credito", StringComparison.OrdinalIgnoreCase)
                || t.Equals("crédito", StringComparison.OrdinalIgnoreCase)
                || t.Equals("PPD", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Evalúa el crédito del cliente contra el total del documento.
        /// Devuelve <c>Aplica = false</c> si la venta no es a crédito.
        /// </summary>
        /// <param name="cveCli">Clave del cliente (catclientes.cve_cli).</param>
        /// <param name="totalDocumento">Total del documento que se está guardando.</param>
        /// <param name="documentoId">
        /// Encabezado del documento origen (documentid del front). Se usa para buscar una
        /// autorización de gerente en él o en cualquiera de sus padres.
        /// </param>
        public static ResultadoCreditoVenta ValidarCreditoVenta(
            this Utilities utils,
            string cveCli,
            int empresaId,
            decimal totalDocumento,
            string tipoPago,
            int documentoId = 0,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null,
            string tokenAutorizacion = null)
        {
            var res = new ResultadoCreditoVenta { TotalDocumento = totalDocumento };

            if (!EsVentaCredito(tipoPago))
            {
                res.Aplica = false;
                res.Mensaje = "La venta no es a crédito; no se valida línea de crédito.";
                return res;
            }

            res.Aplica = true;

            if (string.IsNullOrWhiteSpace(cveCli))
            {
                res.Permitido = false;
                res.Mensaje = "Debe seleccionar un cliente para validar su crédito.";
                return res;
            }

            var param = new Dictionary<string, object>
            {
                { "cve_cli",    cveCli    },
                { "empresa_id", empresaId }
            };

            var filas = utils.RunQuery(@"
                SELECT  cc.id_cliente,
                        COALESCE(cc.lim_crd, 0)         AS lim_crd,
                        cc.estatus_cliente::text        AS estatus_cliente,
                        COALESCE((
                            SELECT SUM(ca.saldo_pendiente)
                            FROM   cartera_clientes ca
                            WHERE  ca.cliente_id = cc.id_cliente
                              AND  ca.cancelada  = false
                        ), 0)                           AS credito_usado
                FROM    catclientes cc
                WHERE   cc.cve_cli    = @cve_cli
                  AND   cc.empresa_id = @empresa_id",
                param, false, conn, tx);

            if (filas == null || filas.Count == 0)
            {
                res.Permitido = false;
                res.Mensaje = $"El cliente {cveCli} no existe en el catálogo de la empresa.";
                return res;
            }

            var row = filas[0];
            res.Limite = Convert.ToDecimal(row["lim_crd"]);
            res.Usado = Convert.ToDecimal(row["credito_usado"]);

            string estatusCliente = (row["estatus_cliente"]?.ToString() ?? "").Trim().ToLower();
            decimal comprometido = res.Usado + res.TotalDocumento;

            // Una autorización de gerente levanta el bloqueo (suspensión o excedente).
            res.Autorizado = ExisteAutorizacionAprobada(
                                 utils, documentoId, cveCli, conn, tx,
                                 tokenAutorizacion, res.TotalDocumento);

            if (estatusCliente == "suspendido")
            {
                res.Estatus = "SUSPENDIDO";
                res.Excedente = res.Limite > 0 && comprometido > res.Limite
                                    ? comprometido - res.Limite
                                    : 0;
                res.Permitido = res.Autorizado;
                res.Mensaje = res.Autorizado
                    ? "Cliente suspendido — operación autorizada por gerencia."
                    : $"El cliente {cveCli} está suspendido. Se requiere autorización de gerencia " +
                      "para continuar con una venta a crédito.";
                return res;
            }

            if (res.Limite <= 0)
            {
                // Sin línea configurada no hay contra qué comparar: se informa y se deja pasar,
                // igual que en el front, para no frenar clientes que aún no tienen límite asignado.
                res.Estatus = "SIN_LIMITE";
                res.Permitido = true;
                res.Mensaje = $"El cliente {cveCli} no tiene límite de crédito configurado.";
                return res;
            }

            if (comprometido > res.Limite)
            {
                res.Estatus = "EXCEDIDO";
                res.Excedente = comprometido - res.Limite;
                res.Permitido = res.Autorizado;
                res.Mensaje = res.Autorizado
                    ? "Crédito excedido — operación autorizada por gerencia."
                    : $"El documento excede el crédito disponible del cliente {cveCli}. " +
                      $"Límite: {res.Limite:C2} · Usado: {res.Usado:C2} · " +
                      $"Disponible: {res.Disponible:C2} · Documento: {res.TotalDocumento:C2} · " +
                      $"Excedente: {res.Excedente:C2}. Se requiere autorización de gerencia.";
                return res;
            }

            res.Permitido = true;
            res.Estatus = comprometido / res.Limite >= UmbralAviso ? "POR_VENCER" : "DISPONIBLE";
            res.Mensaje = res.Estatus == "POR_VENCER"
                ? $"El cliente quedará al {(comprometido / res.Limite) * 100:F1}% de su límite de crédito."
                : "Crédito disponible.";
            return res;
        }

        /// <summary>
        /// Busca una autorización de crédito aprobada para el documento, para cualquiera de sus
        /// ancestros (cotización → pedido → remisión → factura) o para los hermanos en que se
        /// haya dividido el pedido (Stock / Modula / Tubos), de modo que lo que el gerente
        /// autorizó una vez no vuelva a frenarse en el siguiente paso del flujo.
        /// </summary>
        /// <param name="cveCli">
        /// Si se indica, la autorización solo cuenta cuando es de ese mismo cliente: así, si
        /// en el documento se cambia el cliente, la autorización previa deja de aplicar.
        /// </param>
        /// <param name="tokenAutorizacion">
        /// Token de la solicitud. Es la única vía cuando el documento aún no existe (un
        /// pedido capturado desde cero, sin cotización de origen): ahí no hay encabezado al
        /// que colgar la autorización, así que se identifica por su propio token.
        /// </param>
        public static bool ExisteAutorizacionAprobada(
            Utilities utils, int documentoId, string cveCli = null,
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null,
            string tokenAutorizacion = null, decimal totalDocumento = 0m)
        {
            if (!string.IsNullOrWhiteSpace(tokenAutorizacion)
                && ExisteAutorizacionPorToken(
                       utils, tokenAutorizacion, cveCli, totalDocumento, conn, tx))
                return true;

            if (documentoId <= 0) return false;

            var param = new Dictionary<string, object> { { "doc", documentoId } };

            string filtroCliente = "";
            if (!string.IsNullOrWhiteSpace(cveCli))
            {
                filtroCliente = @"
                  AND EXISTS (
                      SELECT 1 FROM catclientes cc
                      WHERE cc.id_cliente = ac.cliente_id
                        AND cc.cve_cli    = @cve_cli
                  )";
                param.Add("cve_cli", cveCli);
            }

            // nivel < 10 corta cualquier ciclo en encabezados_padre.
            object total = utils.RunScalar($@"
                WITH RECURSIVE cadena AS (
                    SELECT id_encabezado, encabezados_padre, 0 AS nivel
                    FROM   encabezadomov
                    WHERE  id_encabezado = @doc
                    UNION ALL
                    SELECT e.id_encabezado, e.encabezados_padre, c.nivel + 1
                    FROM   encabezadomov e
                    JOIN   cadena c ON e.id_encabezado = c.encabezados_padre
                    WHERE  c.nivel < 10
                ),
                -- Un pedido nacional se parte en hasta tres documentos (Stock, Modula y Tubos)
                -- que son EL MISMO pedido: la autorización quedó colgada de uno solo de ellos.
                -- La remisión, en cambio, se carga contra el padre del grupo, cuya línea de
                -- ancestros nunca pasa por esos hijos, así que sin este paso una venta ya
                -- autorizada volvía a bloquearse al remisionar. Los NULL de UNNEST los
                -- descarta el JOIN.
                grupo AS (
                    SELECT id_encabezado FROM cadena
                    UNION
                    SELECT UNNEST(ARRAY[dr.id_encabezado_normal,
                                        dr.id_encabezado_modula,
                                        dr.id_encabezado_tubo])
                    FROM   documentos_relacionados dr
                    JOIN   cadena c ON c.id_encabezado = dr.id_encabezado_padre
                )
                SELECT COUNT(*)
                FROM   autorizaciones_credito ac
                JOIN   grupo g ON g.id_encabezado = ac.pedido_id
                WHERE  ac.estatus = 'aprobado'{filtroCliente}",
                param, false, conn, tx);

            return total != null && Convert.ToInt32(total) > 0;
        }

        /// <summary>
        /// Autorización identificada por el token de su propia solicitud, para los documentos
        /// que todavía no existen en encabezadomov.
        /// </summary>
        private static bool ExisteAutorizacionPorToken(
            Utilities utils, string token, string cveCli, decimal totalDocumento = 0m,
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            var param = new Dictionary<string, object> { { "token", token } };

            string filtroCliente = "";
            if (!string.IsNullOrWhiteSpace(cveCli))
            {
                filtroCliente = @"
                  AND EXISTS (
                      SELECT 1 FROM catclientes cc
                      WHERE cc.id_cliente = ac.cliente_id
                        AND cc.cve_cli    = @cve_cli
                  )";
                param.Add("cve_cli", cveCli);
            }

            // El gerente autorizó un importe concreto: la autorización no puede reutilizarse
            // para un documento mayor. Sin este tope, el token quedaría abierto para cualquier
            // venta posterior del mismo cliente. Se permite 1% de holgura por redondeos.
            string filtroMonto = "";
            if (totalDocumento > 0)
            {
                filtroMonto = " AND ac.monto_pedido * 1.01 >= @total";
                param.Add("total", totalDocumento);
            }

            object total = utils.RunScalar($@"
                SELECT COUNT(*)
                FROM   autorizaciones_credito ac
                WHERE  ac.token   = @token
                  AND  ac.estatus = 'aprobado'{filtroCliente}{filtroMonto}",
                param, false, conn, tx);

            return total != null && Convert.ToInt32(total) > 0;
        }

        /// <summary>
        /// Total del documento a validar contra el crédito: el que declara el front, con el
        /// subtotal de las partidas como piso.
        /// </summary>
        public static decimal TotalDocumentoParaCredito(
            IFormCollection fc, string campoTotal = "total", string campoProductos = "productosJSON")
        {
            decimal declarado = decimal.TryParse(fc[campoTotal].ToString(), out decimal t) ? t : 0m;
            decimal piso = TotalMinimoDesdeProductos(fc[campoProductos].ToString());
            return Math.Max(declarado, piso);
        }

        /// <summary>
        /// Igual que la sobrecarga de IFormCollection, para los flujos que ya recibieron el
        /// formulario aplanado a diccionario (VIFactura.Guardar_ConRemisiones).
        /// </summary>
        public static decimal TotalDocumentoParaCredito(
            Dictionary<string, string> fc, string campoTotal = "total", string campoProductos = "productosJSON")
        {
            string totalStr = fc != null && fc.ContainsKey(campoTotal) ? fc[campoTotal] : "0";
            string prodStr = fc != null && fc.ContainsKey(campoProductos) ? fc[campoProductos] : "";

            decimal declarado = decimal.TryParse(totalStr, out decimal t) ? t : 0m;
            return Math.Max(declarado, TotalMinimoDesdeProductos(prodStr));
        }

        /// <summary>
        /// Piso del total a validar, calculado desde las partidas que manda el front.
        /// El total declarado ya trae flete e IVA (que solo suman), así que se usa el mayor
        /// de los dos: evita que un POST manipulado mande total = 0 y esquive la validación.
        /// </summary>
        public static decimal TotalMinimoDesdeProductos(string productosJSON)
        {
            if (string.IsNullOrWhiteSpace(productosJSON)) return 0m;

            try
            {
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJSON);
                if (productos == null) return 0m;

                decimal subtotal = 0m;
                foreach (var p in productos)
                {
                    decimal cant = LeerDecimal(p, "cantidad");
                    decimal precio = LeerDecimal(p, "precio");
                    decimal dto = LeerDecimal(p, "descuento");
                    subtotal += cant * precio * (1 - dto / 100m);
                }

                return subtotal < 0 ? 0m : subtotal;
            }
            catch
            {
                // El detalle ya se valida en cada controlador; aquí solo es un piso de seguridad.
                return 0m;
            }
        }

        private static decimal LeerDecimal(Dictionary<string, string> fila, string clave)
        {
            if (fila == null || !fila.ContainsKey(clave)) return 0m;
            return decimal.TryParse(fila[clave], out decimal v) ? v : 0m;
        }
    }
}
