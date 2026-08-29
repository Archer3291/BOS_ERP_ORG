using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;

using System.Text;

using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Sucursales
{
    [Authorize]
    public partial class VSPedidoController : Utilities
    {
        // Los usa el flujo de autorizacion de credito (VSAutorizacionCredito.cs).
        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;

        public VSPedidoController(
            BOS_ERP.Services.EmailSender emailSenderService,
            BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Creacion de pedido")]
        public JsonResult Guardar(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string tipo = fc["tipo"].ToString()?.ToLower() ?? ""; // contado, credito, anticipo

                // 🔹 Validaciones previas
                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un cliente." });

                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una moneda." });

                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un vendedor." });

                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una forma de pago." });

                // 🔹 Validar productos solo si NO es anticipo
                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
                if (tipo != "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });

                    productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                    if (productos == null || productos.Count == 0)
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });
                }

                // 🔹 Validar precios y descuentos contra las reglas de precio.
                // El front bloquea los campos y manda los tokens, pero hasta ahora el
                // servidor los ignoraba: capturar el pedido directo saltaba la regla.
                TokenStore.LimpiarExpirados();
                string usuarioReglas = User.Identity.Name;
                string descuentoToken = fc["descuentoToken"].ToString() ?? "";
                string precioToken = fc["precioToken"].ToString() ?? "";
                int empresaReglas = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                var reglas = this.ValidarPartidas(
                    empresaReglas,
                    this.ResolverClienteId(fc["cliente"].ToString(), empresaReglas),
                    productos,
                    TokenStore.Validar(precioToken, usuarioReglas, "CAMBIO DE PRECIO"),
                    TokenStore.Validar(descuentoToken, usuarioReglas, "DESCUENTO"));

                if (!reglas.Permitido)
                    return Json(new { success = false, message = reglas.Mensaje });

                // Un solo uso: el front recarga tras guardar y vuelve a pedir autorizacion.
                TokenStore.Invalidar(descuentoToken);
                TokenStore.Invalidar(precioToken);

                // 🔹 Validar fecha de pago solo si es crédito
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaPago"].ToString()))
                        return Json(new { success = false, message = "Debe ingresar la fecha de pago (solo para crédito)." });

                    if (!DateTime.TryParse(fc["fechaPago"].ToString(), out DateTime fch))
                        return Json(new { success = false, message = "Formato de fecha de pago no válido." });

                    fechaPago = fch;
                }else if (tipo == "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaAnticipo"].ToString()))
                        return Json(new { success = false, message = "Debe ingresar la fecha del anticipo." });
                    if (!DateTime.TryParse(fc["fechaAnticipo"].ToString(), out DateTime fch))
                        return Json(new { success = false, message = "Formato de fecha del anticipo no válido." });
                    fechaPago = fch;
                }
                // 🔹 Validar crédito — solo aplica cuando la venta es a crédito
                var credito = this.ValidarCreditoVenta(
                    fc["cliente"].ToString(),
                    empresaReglas,
                    CreditoVentasHelper.TotalDocumentoParaCredito(fc),
                    tipo,
                    int.TryParse(fc["documentid"].ToString(), out int docCredito) ? docCredito : 0,
                    null, null, fc["creditoToken"].ToString());

                if (!credito.Permitido)
                    return Json(new { success = false, message = credito.Mensaje });

                int idEncabezadoPadre = GetUserId(User.Identity.Name);
                int usrId0 = 0;
                DateTime usrFch0 = DateTime.Now;

                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                {
                    // 🔹 Cargar datos del encabezado existente
                    var usrParameter = new Dictionary<string, object>();
                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3,  "+
                                      "       em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto "+
                                      "FROM encabezadomov em "+
                                      "WHERE em.id_encabezado = @id";

                    usrParameter.Add("id", Convert.ToInt32(fc["documentid"].ToString()));

                    var result = RunQuery(usrquery, usrParameter);

                    if (result.Count > 0)
                    {
                        var usrId = result[0];
                        usrId0 = Convert.ToInt32(usrId["usr0"]);
                        usrFch0 = Convert.ToDateTime(usrId["fch0"]);
                        idEncabezadoPadre = Convert.ToInt32(fc["documentid"].ToString());
                    }
                }
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
                // 🔹 Crear encabezado
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 50,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    // La sucursal viaja en un select oculto: si el TomSelect todavía no se
                    // pobló, llegaba vacío y Convert.ToInt32("") reventaba. Manda la del
                    // usuario, que es la única con la que puede vender de todos modos.
                    Suc = int.TryParse(fc["sucursal"].ToString(), out int sucDoc) && sucDoc > 0
                            ? sucDoc
                            : Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = fc["almacen"].ToString(),
                    Fch = DateTime.Now,
                    TpMov = "VSPED",
                    ComentAut = fc["comentarios"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = usrId0,
                    Fch0 = usrFch0,
                    Usr1 = GetUserId(User.Identity.Name),
                    Fch1 = DateTime.Now,
                    Imp = Convert.ToDecimal(fc["total"].ToString()),
                    CliProv = fc["cliente"].ToString(),
                    Ccy = fc["moneda"].ToString(),
                    Estatus = 1,
                    Ref = idCliente,
                    Flete = Convert.ToDecimal(fc["flete"].ToString()),
                    VdrCpr = fc["vendedor"].ToString(),
                    Coment1 = fc["concepto"].ToString(),
                    EncabezadoPadre = idEncabezadoPadre,
                    PlDias = string.IsNullOrWhiteSpace(fc["plazo"].ToString()) ? 0 : Convert.ToInt32(fc["plazo"].ToString()),
                    FchPgEntrega = fechaPago ?? DateTime.Now, // si no hay, usa fecha actual
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    // f_pago guarda el id de cat_f_pago, no la clave SAT. Antes se guardaba
                    // "03" como si fuera el id: al recargar el documento, el select de forma
                    // de pago del siguiente paso quedaba con la forma equivocada o vacío.
                    FPago = Convert.ToInt32(RunScalar(
                        "select id_f_pago from cat_f_pago where cve_sat = @cve_sat",
                        new Dictionary<string, object> { { "cve_sat", fc["forma-pago"].ToString() } })),
                    Mdp = fc["metodo-pago"].ToString(),
                    TipoPoceso = "pedido_" + tipo,
                    CFDI = fc["uso-cfdi"].ToString(),
                    Dto = Convert.ToDecimal(fc["descuento"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString()
                };

                // 🔹 Crear partidas (solo si hay productos)
                var partidas = new List<PartidaDocumento>();
                if (productos.Count > 0)
                {
                    foreach (var p in productos)
                    {
                        var parametersP = new Dictionary<string, object>();
                        string queryId = "select id_catproductos from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id";
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
                            // Importe = cantidad x precio menos descuento (antes iba contra
                            // costoUnitario, que el front no manda, y quedaba en 0).
                            ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"], System.Globalization.CultureInfo.InvariantCulture) : 0)
                                      * (p.ContainsKey("precio") ? decimal.Parse(p["precio"], System.Globalization.CultureInfo.InvariantCulture) : 0)
                                      * (1 - (p.ContainsKey("descuento") ? decimal.Parse(p["descuento"], System.Globalization.CultureInfo.InvariantCulture) : 0) / 100m),
                            Ud = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA",
                            IdProducto = productId,
                            TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                        });
                    }
                }

                // 🔹 Guardar documento
                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                string query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                parameters.Add("id", idEncabezadoPadre);
                RunUpdate(query, parameters);

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString()
                };

                return Json(new { success = true, message = "Documento creado exitosamente.", folio_generado = folio["folio_generado"].ToString()});
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VSPedido/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

    }
}