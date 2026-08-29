using Npgsql;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.CuentasContables;
using System.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    [RightAuthorize("cobro_clientes")]
    public class PagosClienteController : Utilities
    {
        // Para calendario
        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            // now() viaja con cada renglón para que el cálculo de días de la
            // vista se haga contra la fecha del servidor y no la del navegador.
            string query = "SELECT e.tipo_proceso, e.folio, cc.id_cartera_cliente id_cartera, cc.monto_total, cc.saldo_pendiente, " +
                "   cc.fecha_emision, now() current_date, cc.fecha_vencimiento, c.n_cli, cc.encabezado_id, c.es_especial " +
                "FROM cartera_clientes cc " +
                "LEFT JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "LEFT JOIN catclientes c ON c.id_cliente = cc.cliente_id AND c.empresa_id = @empresa_id " +
                "WHERE e.suc = @sucursal AND cc.saldo_pendiente > 0 AND e.tipo_proceso NOT IN ('registro_carteras_cliente', 'factura_credito')";

            var facturas = RunQuery(query, parameters);

            result.Add("facturas", facturas);

            return Json(result);
        }

        // Para tabla
        public JsonResult GetDocumentosPaginados(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();

            int page = Convert.ToInt32(fc["page"].ToString());
            int pageSize = Convert.ToInt32(fc["pageSize"].ToString());
            string buscar = fc["nombre"].ToString();
            string sortDir = (fc["sortDir"].ToString() ?? "asc").ToLower() == "desc" ? "DESC" : "ASC";
            string where = "";

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                where = " AND (" +
                    "   e.folio ILIKE '%' || @buscar || '%' " +
                    "   OR c.n_cli ILIKE '%' || @buscar || '%' " +
                    " )";
            }

            // La tabla manda el nombre de la propiedad de la fila (columns[].data),
            // no la columna real: aquí se traduce a la expresión SQL que le toca.
            var allowedColumns = new Dictionary<string, string> {
                { "fecha_emision",     "cc.fecha_emision" },
                { "fecha_vencimiento", "cc.fecha_vencimiento" },
                { "monto_total",       "cc.monto_total" },
                { "saldo_pendiente",   "cc.saldo_pendiente" },
                { "folio",             "e.folio" },
                { "n_cli",             "c.n_cli" },
                { "es_especial",       "c.es_especial" }
            };

            if (!allowedColumns.TryGetValue(fc["sortColumn"].ToString() ?? "", out string sortColumn))
                sortColumn = "cc.fecha_vencimiento";

            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("buscar", buscar);

            string query = "SELECT e.tipo_proceso, e.folio, cc.id_cartera_cliente id_cartera, cc.monto_total, cc.saldo_pendiente, " +
                "   cc.fecha_emision, now() current_date, cc.fecha_vencimiento, c.n_cli, cc.encabezado_id, c.es_especial " +
                "FROM cartera_clientes cc " +
                "LEFT JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "LEFT JOIN catclientes c ON c.id_cliente = cc.cliente_id AND c.empresa_id = @empresa_id " +
                "WHERE e.suc = @sucursal AND cc.saldo_pendiente > 0 AND e.tipo_proceso NOT IN ('registro_carteras_cliente', 'factura_credito') " +
                $"{where} " +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var facturas = RunQuery(query, parameters);

            // El conteo lleva el mismo filtro de búsqueda que la página; si no,
            // el paginador ofrece páginas vacías al buscar.
            query = "SELECT COUNT(*) qty " +
                "FROM cartera_clientes cc " +
                "LEFT JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "LEFT JOIN catclientes c ON c.id_cliente = cc.cliente_id AND c.empresa_id = @empresa_id " +
                "WHERE e.suc = @sucursal AND cc.saldo_pendiente > 0 AND e.tipo_proceso NOT IN ('registro_carteras_cliente', 'factura_credito') " +
                $"{where}";
            int total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new {data = facturas, total});
        }

        public JsonResult GetDetallesPagos(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("cartera_id", Convert.ToInt32(fc["documento_padre"].ToString()));

            string query = "SELECT e.tipo_proceso, cc.fecha_cobro, cc2.estado, cc2.monto_total, cc2.saldo_pendiente,  cc.monto monto_pago, " +
                "   cc2.id_cartera_cliente id_cartera, e.folio, cc2.encabezado_id " +
                "FROM aplicaciones_cobro_cliente acc " +
                "INNER JOIN cobros_cliente cc ON cc.id_cobro = acc.cobro_id " +
                "INNER JOIN cartera_clientes cc2 ON cc2.id_cartera_cliente = acc.cartera_id " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = acc.encabezado_id " +
                "WHERE cc2.id_cartera_cliente = @cartera_id";

            var pagos = RunQuery(query, parameters);

            return Json(pagos);
        }

        public JsonResult RegistrarPagoCliente(IFormCollection fc, List<IFormFile> files)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                var _parameters = new List<Dictionary<string, object>>();

                string query = "SELECT saldo_pendiente " +
                    "FROM cartera_clientes " +
                    "WHERE id_cartera_cliente = @compra_id ";
                parameters.Add("compra_id", Convert.ToInt32(fc["carteraId"].ToString()));
                var costo = RunScalar(query, parameters);

                decimal costo_venta = 0;

                if (costo != null && costo != DBNull.Value)
                {
                    costo_venta = Convert.ToDecimal(costo);
                }

                if (costo_venta <= 0)
                {
                    return Json(new { icon = "warning", title = "No fue posible realizar el cobro", html = "Esta factura ya quedo saldada, por favor intenta de nuevo con otra, si cree que esto es un error contacte al equipo de soporte. Puede consultar el registro de los pagos en <a href='../Carteras/CarteraClientes' target='_blank'>Cartera clientes</a>", showCancelButton = false });
                }

                decimal montoPago;
                if (!decimal.TryParse(fc["amount"].ToString(), out montoPago) || montoPago <= 0)
                {
                    return Json(new { icon = "warning", title = "Monto inválido", html = "El monto del pago no es válido." });
                }

                if (montoPago > costo_venta)
                {
                    return Json(new { icon = "warning", title = "Monto excedido", html = "El monto del pago excede el saldo pendiente." });
                }

                string usrquery = "SELECT em.id_encabezado, em.centro_costos, em.cli_prov, em.refe, em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, em.usr3, " +
                    "   em.fch3, em.usr4, em.fch4, tipo_proceso, tipo_producto, em.imp " +
                    "FROM encabezadomov em " +
                    "WHERE em.folio = @folio AND em.suc = @sucursal";
                parameters.Add("folio", fc["documentNumber"].ToString());
                parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                var usrId = RunQuery(usrquery, parameters)[0];

                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 20;
                encabezado.IdTpDoc = 70;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "CXC";
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.Imp = Convert.ToDecimal(fc["amount"].ToString());
                encabezado.CliProv = usrId["cli_prov"].ToString();
                encabezado.Ref = Convert.ToInt32(usrId["refe"]);
                encabezado.Estatus = 1;
                encabezado.TipoPoceso = "cobro_cliente";
                encabezado.CentroCostos = Convert.ToInt32(usrId["centro_costos"]);
                encabezado.EncabezadoPadre = Convert.ToInt32(usrId["id_encabezado"]);
                encabezado.Coment1 = fc["notes"].ToString();
                encabezado.Coment2 = fc["reference"].ToString();
                encabezado.IdCartera = Convert.ToInt32(fc["carteraId"].ToString());
                encabezado.Ccy = "PESOS";

                // ====== Calcular IVA proporcional ======
                decimal totalFactura = Convert.ToDecimal(usrId["imp"]); // total con IVA
                decimal pago = Convert.ToDecimal(fc["amount"].ToString());         // pago parcial

                decimal importeBase = Math.Round(totalFactura / 1.16m, 2, MidpointRounding.AwayFromZero);
                decimal ivaTotal = totalFactura - importeBase;

                decimal proporcion = pago / totalFactura;
                decimal ivaProporcional = Math.Round(ivaTotal * proporcion, 2, MidpointRounding.AwayFromZero);
                decimal subtotal = Math.Round(pago - ivaProporcional, 2, MidpointRounding.AwayFromZero);

                encabezado.Sub = subtotal;

                var documento = new Dictionary<string, object>();

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var partidas = new List<PartidaDocumento>();
                            documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                            query = "INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom) " +
                                "VALUES (@encabezado_id, @impuesto, @subtotal, @importe, 1, 16, @prov_nom)";

                            parameters = new Dictionary<string, object>();
                            parameters.Add("impuesto", Convert.ToInt32(GetSetting("impuesto")));
                            parameters.Add("encabezado_id", Convert.ToInt32(documento["IdEncabezado"]));
                            parameters.Add("subtotal", subtotal);
                            parameters.Add("importe", ivaProporcional);
                            parameters.Add("prov_nom", usrId["cli_prov"].ToString());

                            RunUpdate(query, parameters, false, conn, tx);

                            query = "INSERT INTO archivos_pagos_cliente (nombre_original, uuid, extencion, path, encabezado_id) " +
                                "   VALUES (@nombre, @uuid, @extencion, @path, @encabezado)";

                            foreach (var file in files)
                            {
                                parameters = new Dictionary<string, object>();
                                if (file != null && file.Length > 0)
                                {
                                    var nombreOriginal = file.FileName;
                                    var extension = Path.GetExtension(file.FileName);
                                    var ruta = "content/comprobantes_pagos_cliente/";
                                    var uuid = Guid.NewGuid().ToString();
                                    UploadFormFileToPath(ruta, file, uuid, extension);

                                    parameters.Add("nombre", nombreOriginal);
                                    parameters.Add("uuid", uuid);
                                    parameters.Add("extencion", extension);
                                    parameters.Add("path", $"{ruta}{uuid}{extension}");
                                    parameters.Add("encabezado", Convert.ToInt32(documento["IdEncabezado"]));

                                    RunUpdate(query, parameters, false, conn, tx);
                                }
                            }

                            List<PolizaData> polizas = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), fc["bankId"].ToString(), null, conn, tx);
                            var poliza = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), polizas, false, null, conn, tx);
                            CobroClienteResult cobro = CrearCobroCliente(poliza[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Normal,  conn, tx);
                            AplicarCobros aplicacion = new AplicarCobros();
                            aplicacion.CarteraIds.Add(Convert.ToInt32(fc["carteraId"].ToString()));
                            aplicacion.CobrosIds.Add(cobro.PagoId);
                            aplicacion.UsuarioId = GetUserId(User.Identity.Name);
                            aplicacion.ClienteId = Convert.ToInt32(usrId["refe"]);
                            AplicarCobrosCliente(aplicacion, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Pago registrado", html = $"El pago fue registrado exitosamente con el documento: {documento["folio_generado"]}" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error inesperado", html = ex.Message });
            }
        }

        public JsonResult GetCuentasBancos()
        {
            string query = "SELECT codigo as id, nombre from cuentas_finanzas WHERE codigo LIKE '%1-1-02%'";
            var bancos = RunQuery(query);

            return Json(bancos);
        }
    }
}