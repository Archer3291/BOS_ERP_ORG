using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using System.Text;
using Microsoft.AspNetCore.Mvc;


namespace BOS_ERP.Controllers.Ventas.Ventas_Nacionales
{
    [Authorize]
    public class VNCotizacionController : Utilities
    {

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Nacionales", Accion = "Creacion de cotizacion")]
        public JsonResult Guardar(IFormCollection fc)
        {
            try
            {
                int idCliente = 0;
                string clienteNombre = "";
                if (!string.IsNullOrEmpty(fc["cliente"].ToString()))
                {
                    var clienteParameter = new Dictionary<string, object>();
                    string clientequery = "SELECT cl.id_cliente, cl.n_cli " +
                                          "FROM catclientes cl " +
                                          "WHERE cl.cve_cli = @cliente AND cl.empresa_id = @empresa_id";
                    clienteParameter.Add("cliente", fc["cliente"].ToString());
                    clienteParameter.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    var clienteResult = RunQuery(clientequery, clienteParameter);
                    if (clienteResult.Count > 0)
                    {
                        var cliente = clienteResult[0];
                        idCliente = Convert.ToInt32(cliente["id_cliente"]);
                        clienteNombre = cliente["n_cli"].ToString();

                    }
                }

                // ── Validar las partidas contra las reglas de precio ──────────
                // Mismo criterio que ventas industriales (Helpers/ReglasPrecioHelper.cs).
                // El front ya lo impone, pero el endpoint es alcanzable directo.
                TokenStore.LimpiarExpirados();
                string usuarioReglas = User.Identity.Name;
                string descuentoToken = fc["descuentoToken"].ToString() ?? "";
                string precioToken = fc["precioToken"].ToString() ?? "";

                var reglas = this.ValidarPartidas(
                    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    idCliente,
                    JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(
                        fc["productosJSON"].ToString() ?? "[]"),
                    TokenStore.Validar(precioToken, usuarioReglas, "CAMBIO DE PRECIO"),
                    TokenStore.Validar(descuentoToken, usuarioReglas, "DESCUENTO"));

                if (!reglas.Permitido)
                    return Json(new { success = false, message = reglas.Mensaje });

                TokenStore.Invalidar(descuentoToken);
                TokenStore.Invalidar(precioToken);
                // La cotización solo define si la venta es de contado o a crédito; la forma
                // de pago y el uso de CFDI se capturan hasta el pedido. Sin guardarlo aquí,
                // el pedido no puede preseleccionar el toggle al cargar la cotización.
                string tipoPagoCot = fc["tipoPago"].ToString()?.ToLower() ?? "contado";

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 12,
                    IdTpDoc = 41, //tipo de documento
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "VNCOT",
                    ComentAut = fc["comentarios"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Imp = Convert.ToDecimal(fc["total"].ToString()),
                    CliProv = fc["cliente"].ToString(),
                    Ccy = fc["moneda"].ToString(),
                    Ref = idCliente,
                    Estatus = 1,
                    Flete = Convert.ToDecimal(fc["flete"].ToString()), //Flete
                    VdrCpr = fc["vendedor"].ToString(),
                    Coment1 = fc["concepto"].ToString(),
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString(),
                    Mdp = tipoPagoCot == "credito" ? "PPD" : "PUE",
                    TipoPoceso = "cotizacion_" + tipoPagoCot
                };
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                var partidas = new List<PartidaDocumento>();
                foreach (var p in productos)
                {
                    var parametersP = new Dictionary<string, object>();
                    string queryId = "select id_catproductos from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id ";
                    parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
                    parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    int productId = Convert.ToInt32(RunScalar(queryId, parametersP));
                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
                        PvProd = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0,
                        Dto1 = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0,
                        ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) * (p.ContainsKey("costoUnitario") ? decimal.Parse(p["costoUnitario"]) : 0),
                        Ud = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA",
                        IdProducto = productId,
                        TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                    });
                }
                var folio = GenerarDocumentoConPartidas(encabezado, partidas);
                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString()
                };
                return Json(new { success = true, message = "Cotizacion creada exitosamente.", folio_generado = folio["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNCotizacion/Guardar");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}