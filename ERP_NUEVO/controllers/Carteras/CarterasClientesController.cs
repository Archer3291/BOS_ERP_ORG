using BOS_ERP.Controllers;
using Npgsql;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.CuentasContables;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class CarterasController : Utilities
    {
        #region Get Data
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetClientes(IFormCollection fc)
        {
            try
            {
                int page = Convert.ToInt32(fc["page"].ToString());
                int pageSize = Convert.ToInt32(fc["pageSize"].ToString());
                var parameters = new Dictionary<string, object>();
                parameters.Add("offset", (page - 1) * pageSize);
                parameters.Add("pageSize", pageSize);
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                string where = "";

                if (!string.IsNullOrWhiteSpace(fc["nombre"].ToString()))
                {
                    parameters.Add("nombre", $"%{fc["nombre"].ToString()}%");
                    where = " AND (c.n_cli ILIKE @nombre OR cf.codigo ILIKE @nombre) ";
                }

                string query = "WITH cartera AS (" +
                    "   SELECT cliente_id, SUM(saldo_pendiente) AS saldo, SUM(monto_total - saldo_pendiente) AS total_pagado " +
                    "   FROM cartera_clientes " +
                    "   WHERE empresa_id = @empresa_id " +
                    "   GROUP BY cliente_id), " +
                    "cobros AS (" +
                    "   SELECT cliente_id, SUM(saldo_disponible) AS saldo_favor " +
                    "   FROM cobros_cliente " +
                    "   GROUP BY cliente_id) " +
                    "SELECT c.id_cliente, c.cve_cli, c.n_cli, cf.codigo, c.rfc, COALESCE(car.saldo, 0) AS saldo, c.lim_crd, " +
                    "   COALESCE(cob.saldo_favor, 0) AS saldo_favor, car.total_pagado " +
                    "FROM catclientes c " +
                    "INNER JOIN cuentas_finanzas cf ON cf.cliente_id = c.id_cliente " +
                    "LEFT JOIN cartera car ON car.cliente_id = c.id_cliente " +
                    "LEFT JOIN cobros cob ON cob.cliente_id = c.id_cliente " +
                    $"WHERE c.empresa_id = @empresa_id AND (COALESCE(car.saldo, 0) > 0 OR COALESCE(cob.saldo_favor, 0) > 0) {where} " +
                    "ORDER BY saldo ASC " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);

                query = "WITH cartera AS (" +
                    "   SELECT cliente_id, SUM(saldo_pendiente) AS saldo " +
                    "   FROM cartera_clientes " +
                    "   WHERE empresa_id = @empresa_id " +
                    "   GROUP BY cliente_id), " +
                    "cobros AS (" +
                    "   SELECT cliente_id, SUM(saldo_disponible) AS saldo_favor " +
                    "   FROM cobros_cliente " +
                    "   GROUP BY cliente_id) " +
                    "SELECT COUNT(*) " +
                    "FROM catclientes c " +
                    "INNER JOIN cuentas_finanzas cf ON cf.cliente_id = c.id_cliente " +
                    "LEFT JOIN cartera car ON car.cliente_id = c.id_cliente " +
                    "LEFT JOIN cobros cob ON cob.cliente_id = c.id_cliente " +
                    $"WHERE c.empresa_id = @empresa_id {where}" +
                    "AND (COALESCE(car.saldo, 0) > 0 OR COALESCE(cob.saldo_favor, 0) > 0)";
                var total = RunScalar(query, parameters);

                query = "SELECT COALESCE(SUM(saldo), 0) AS saldo_cobrar " +
                    "FROM (" +
                    "   SELECT cc.cliente_id, SUM(cc.saldo_pendiente) AS saldo " +
                    "   FROM cartera_clientes cc " +
                    "   INNER JOIN catclientes c ON c.id_cliente = cc.cliente_id AND c.empresa_id = @empresa_id " +
                    "   INNER JOIN cuentas_finanzas cf ON cf.cliente_id = c.id_cliente " +
                    $"   WHERE cc.empresa_id = @empresa_id {where} " +
                    "   GROUP BY cc.cliente_id) " +
                    "t;";
                var saldoCobrar = RunScalar(query, parameters);

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { data, total, saldoCobrar, icon = "success" });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Carteras/GetClientes");
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error" });
            }
        }

        public JsonResult GetCarteraCliente(IFormCollection fc)
        {
            int clienteId = int.TryParse(fc["clienteId"].ToString(), out int c) ? c : 0;
            int page = int.TryParse(fc["page"].ToString(), out int p) ? p : 1;
            int pageSize = int.TryParse(fc["pageSize"].ToString(), out int ps) ? ps : 10;
            string nombre = fc["nombre"].ToString();
            string where = "";
            var parameters = new Dictionary<string, object>
            {
                { "clienteId", clienteId },
                { "nombre", $"%{nombre}%" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
            };

            if (!string.IsNullOrEmpty(nombre))
            {
                where = " AND (f.uuid::text ILIKE @nombre " +
                " OR (e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END) ILIKE '%' || @nombre || '%') ";
            }

            string query = "SELECT e.tipo_proceso, cc.id_cartera_cliente factura_original, cc.monto_total, cc.saldo_pendiente, " +
                "   e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio, " +
                "   cc.fecha_emision , cc.fecha_vencimiento, c.n_cli, f.uuid factura_folio " +
                "FROM cartera_clientes cc " +
                "LEFT JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "LEFT JOIN catclientes c ON c.id_cliente = cc.cliente_id " +
                "LEFT JOIN factura f ON f.encabezado_id = cc.encabezado_id " +
                $"WHERE (cc.cliente_id = @clienteId AND cc.cancelada = false) {where} " +
                "ORDER BY CASE WHEN cc.saldo_pendiente > 0 THEN 1 ELSE 0 END DESC, cc.fecha_emision ASC " +
                //"ORDER BY (e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END) desc " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(cc.*) " +
                "FROM cartera_clientes cc " +
                "LEFT JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "LEFT JOIN catclientes c ON c.id_cliente = cc.cliente_id " +
                "LEFT JOIN factura f ON f.encabezado_id = cc.encabezado_id " +
                $"WHERE (cc.cliente_id = @clienteId AND cc.cancelada = false) {where} ";

            var total = RunScalar(query, parameters);

            parameters = new Dictionary<string, object>();
            parameters.Add("id_cliente", clienteId);
            query = "SELECT COALESCE(SUM(cc.saldo_disponible), 0) " +
                "FROM cobros_cliente cc " +
                "WHERE cc.cliente_id = @id_cliente";
            int saldoFavor = Convert.ToInt32(RunScalar(query, parameters));

            query = "SELECT COALESCE(SUM(cc.monto_total ), 0) " +
                "FROM cartera_clientes cc " +
                "WHERE cc.cliente_id = @id_cliente " +
                "   AND fecha_creacion >= date_trunc('month', CURRENT_DATE) " +
                "   AND fecha_creacion < date_trunc('month', CURRENT_DATE) + INTERVAL '1 month'";
            decimal totalVendido = Convert.ToDecimal(RunScalar(query, parameters));

            query = "SELECT COALESCE(SUM(cc.saldo_pendiente ), 0) " +
                "FROM cartera_clientes cc " +
                "WHERE cc.cliente_id = @id_cliente";
            decimal totalPendiente = Convert.ToDecimal(RunScalar(query, parameters));

            Response.StatusCode = (int)HttpStatusCode.OK;
            return Json(new { data, total, totales = new { saldoFavor, totalVendido, totalPendiente } });
        }

        public JsonResult GetDetallesCompra(IFormCollection fc)
        {
            int clienteId = int.TryParse(fc["clienteId"].ToString(), out int c) ? c : 0;
            int page = int.TryParse(fc["page"].ToString(), out int p) ? p : 1;
            int pageSize = int.TryParse(fc["pageSize"].ToString(), out int ps) ? ps : 10;
            string nombre = fc["nombre"].ToString();
            string where = "";
            var parameters = new Dictionary<string, object>
            {
                { "clienteId", clienteId },
                { "nombre", $"%{nombre}%" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize }
            };

            if (!string.IsNullOrEmpty(nombre))
            {
                where = " AND (e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END) ILIKE '%' || @nombre || '%') ";
            }

            string query = "SELECT e.tipo_proceso, cc.fecha_cobro, cc2.estado, cc2.monto_total, cc.monto monto_pago, cc2.saldo_pendiente, " +
                "   cc2.id_cartera_cliente factura_original, " +
                "   e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio " +
                "FROM aplicaciones_cobro_cliente acc " +
                "INNER JOIN cobros_cliente cc ON cc.id_cobro = acc.cobro_id " +
                "INNER JOIN cartera_clientes cc2 ON cc2.id_cartera_cliente = acc.cartera_id " +
                "LEFT JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                $"WHERE acc.cartera_id = @poliza_padre {where} " +
                "ORDER BY acc.encabezado_id DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            parameters.Add("poliza_padre", Convert.ToInt32(fc["id_factura_padre"].ToString()));
            var pagos = RunQuery(query, parameters);

            return Json(pagos);
        }

        public JsonResult GetCuentasBancos()
        {
            string query = "SELECT codigo as id, nombre from cuentas_finanzas WHERE codigo LIKE '%1-1-02%'";
            var bancos = RunQuery(query);

            return Json(bancos);
        }
        #endregion

        #region Registrar Movimientos
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GenerarAbono(IFormCollection fc, IFormFile comprobante)
        {
            try
            {
                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 20;
                encabezado.IdTpDoc = 72;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "ABNCLI";
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.Imp = Convert.ToDecimal(fc["monto"].ToString());
                encabezado.CliProv = fc["clave"].ToString();
                encabezado.Ref = Convert.ToInt32(fc["cliente"].ToString());
                encabezado.Estatus = 1;
                encabezado.TipoPoceso = "aplicacion_cobro";
                encabezado.CentroCostos = 20;
                encabezado.Coment1 = fc["observaciones"].ToString();
                encabezado.Coment2 = fc["referencia"].ToString();
                encabezado.Ccy = "PESOS"; //fc["moneda"].ToString();

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
                            List<PolizaData> datosPoliza = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), fc["banco"].ToString(), null, conn, tx);
                            var poliza = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), datosPoliza, false, null, conn, tx);
                            CrearCobroCliente(poliza[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Normal, conn, tx);

                            string query = "INSERT INTO archivos_pagos_cliente (nombre_original, uuid, extencion, path, encabezado_id) " +
                                "   VALUES (@nombre, @uuid, @extencion, @path, @encabezado)";

                            var parameters = new Dictionary<string, object>();
                            if (comprobante != null && comprobante.Length > 0)
                            {
                                var nombreOriginal = comprobante.FileName;
                                var extension = Path.GetExtension(comprobante.FileName);
                                var ruta = "content/comprobantes_pagos_cliente/";
                                var uuid = Guid.NewGuid().ToString();
                                UploadFormFileToPath(ruta, comprobante, uuid, extension);

                                parameters.Add("nombre", nombreOriginal);
                                parameters.Add("uuid", uuid);
                                parameters.Add("extencion", extension);
                                parameters.Add("path", $"{ruta}{uuid}{extension}");
                                parameters.Add("encabezado", Convert.ToInt32(documento["IdEncabezado"]));

                                RunUpdate(query, parameters, false, conn, tx);
                            }
                            tx.Commit(); 
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = $"Abono registrado correctamente al cliente - {fc["clave"].ToString()}" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error al registrar el abono.", html = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SaldarFacturas(AplicarCobros cobro)
        {
            try
            {

                var parameters = new Dictionary<string, object>();
                parameters.Add("cliente", cobro.ClienteId);

                string query = "SELECT SUM(saldo_disponible) FROM cobros_cliente WHERE cliente_id = @cliente";
                int cartera = Convert.ToInt32(RunScalar(query, parameters));

                if (cartera <= 0)
                {
                    return Json(new { icon = "error", title = "Saldo insufiente.", html = "El cliente no tiene saldo en cartera para realizar esta accion." });
                }

                query = "SELECT SUM(saldo_pendiente) FROM cartera_clientes WHERE id_cartera_cliente = ANY(@cartera) AND cancelada = false";
                parameters.Add("cartera", cobro.CarteraIds);
                decimal totalPendiente = Convert.ToDecimal(RunScalar(query, parameters));

                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 20;
                encabezado.IdTpDoc = 72;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "ABNCLI";
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.Imp = totalPendiente;
                encabezado.Ref = cobro.ClienteId;
                encabezado.Estatus = 1;
                encabezado.TipoPoceso = "aplicacion_cobro";
                encabezado.CentroCostos = 20;
                encabezado.Ccy = "PESOS";
                encabezado.CliProv = "";

                var documento = new Dictionary<string, object>();
                cobro.UsuarioId = GetUserId(User.Identity.Name);

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
                            //List<PolizaData> datosPoliza = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);
                            //var poliza = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), datosPoliza, false, null, conn, tx);
                            AplicarCobrosCliente(cobro, conn, tx);
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Pagos aplicados correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error inesperado", html = ex.Message });
            }
        }
        #endregion
    }
}