using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text;

namespace BOS_ERP.Controllers.Ventas.Ventas_Sucursales
{
    [Authorize]
    public class VSCotizacionController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;
        public VSCotizacionController(BOS_ERP.Services.EmailSender emailSenderService, BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Creacion de cotizacion")]
        public JsonResult Guardar(IFormCollection fc)
        {
            try
            {
                TokenStore.LimpiarExpirados();

                string usuarioActual = User.Identity.Name;
                string descuentoToken = fc["descuentoToken"].ToString() ?? "";
                string precioToken = fc["precioToken"].ToString() ?? "";

                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(
                    fc["productosJSON"].ToString() ?? "[]");

                bool descuentoAutorizado = TokenStore.Validar(descuentoToken, usuarioActual, "DESCUENTO");
                bool precioAutorizado = TokenStore.Validar(precioToken, usuarioActual, "CAMBIO DE PRECIO");

                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                // El cliente se resuelve ANTES de validar precios: la lista de precios
                // (catclientes.cod_ant) y las reglas por cliente dependen de el.
                int idCliente = 0;
                string clienteNombre = "";
                if (!string.IsNullOrEmpty(fc["cliente"].ToString()))
                {
                    var clienteParameter = new Dictionary<string, object>();
                    string clientequery = "SELECT cl.id_cliente, cl.n_cli " +
                                          "FROM catclientes cl " +
                                          "WHERE cl.cve_cli = @cliente AND cl.empresa_id = @empresa_id";
                    clienteParameter.Add("cliente", fc["cliente"].ToString());
                    clienteParameter.Add("empresa_id", empresaId);
                    var clienteResult = RunQuery(clientequery, clienteParameter);
                    if (clienteResult.Count > 0)
                    {
                        var cliente = clienteResult[0];
                        idCliente = Convert.ToInt32(cliente["id_cliente"]);
                        clienteNombre = cliente["n_cli"].ToString();

                    }
                }

                // Reglas de precio, mismo criterio que en Industriales. Hasta ahora el front
                // bloqueaba los campos pero el servidor no revisaba nada: un POST directo
                // (o la cotizacion capturada sin token) pasaba cualquier precio.
                var reglas = this.ValidarPartidas(
                    empresaId, idCliente, productos, precioAutorizado, descuentoAutorizado);

                if (!reglas.Permitido)
                    return Json(new { success = false, message = reglas.Mensaje });

                TokenStore.Invalidar(descuentoToken);
                TokenStore.Invalidar(precioToken);

                // La cotizacion solo define si la venta es de contado o a credito; la forma
                // de pago y el uso de CFDI se capturan hasta el pedido.
                string tipoPagoCot = fc["tipoPago"].ToString()?.ToLower() ?? "contado";

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 49, //tipo de documento
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "VSCOT",
                    ComentAut = fc["comentarios"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Imp = Convert.ToDecimal(fc["total"].ToString()),
                    CliProv = fc["cliente"].ToString(),
                    Ccy = fc["moneda"].ToString(),
                    Estatus = 1,
                    Ref = idCliente,
                    Flete = Convert.ToDecimal(fc["flete"].ToString()), //Flete
                    VdrCpr = fc["vendedor"].ToString(),
                    Coment1 = fc["concepto"].ToString(),
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    Dto = Convert.ToDecimal(fc["descuento"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString(),
                    Mdp = tipoPagoCot == "credito" ? "PPD" : "PUE",
                    TipoPoceso = "cotizacion_" + tipoPagoCot,
                };
                var partidas = new List<PartidaDocumento>();
                foreach (var p in productos)
                {
                    var parametersP = new Dictionary<string, object>();
                    string queryId = "select id_catproductos from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id";
                    parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
                    parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    int productId = Convert.ToInt32(RunScalar(queryId,parametersP));
                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
                        PvProd = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0,
                        Dto1 = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0,
                        // Importe = cantidad x precio menos descuento. Antes multiplicaba por
                        // costoUnitario, que el front nunca manda: todas las partidas de las
                        // cotizaciones de sucursal quedaban con imp_part = 0.
                        ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"], System.Globalization.CultureInfo.InvariantCulture) : 0)
                                  * (p.ContainsKey("precio") ? decimal.Parse(p["precio"], System.Globalization.CultureInfo.InvariantCulture) : 0)
                                  * (1 - (p.ContainsKey("descuento") ? decimal.Parse(p["descuento"], System.Globalization.CultureInfo.InvariantCulture) : 0) / 100m),
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
                RegistrarErrorParaTicket(ex, "VSCotizacion/Guardar");
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult VerificarExistencias(IFormCollection fc)
        {
            try
            {
                int sucursalUsuario = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                var productosJSON = fc["productosJSON"].ToString();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJSON);

                var faltantes = new List<object>();

                foreach (var p in productos)
                {
                    string cveProducto = p.ContainsKey("productoId") ? p["productoId"] : "";
                    decimal cantidadSolicitada = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0;

                    // Obtener existencia real en la sucursal del usuario
                    var parameters = new Dictionary<string, object>
            {
                { "cveProducto", cveProducto },
                { "sucursalId", sucursalUsuario },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

                    string query = @"
                SELECT COALESCE(SUM(tp.cantidad), 0) AS existencia
                FROM tarima_productos tp
                INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                WHERE ca.tipo = 'Stock'
                  AND cs.id_sucursal = @sucursalId
                  AND cp.cve_prod = @cveProducto
                  --AND ca.id_almacen =1
                  AND cp.empresa_id = @empresa_id";

                    var result = RunQuery(query, parameters);
                    decimal existenciaLocal = result != null && result.Count > 0
                        ? Convert.ToDecimal(result[0]["existencia"])
                        : 0;

                    if (existenciaLocal < cantidadSolicitada)
                    {
                        faltantes.Add(new
                        {
                            productoId = cveProducto,
                            descripcion = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                            cantidadSolicitada = cantidadSolicitada,
                            existenciaLocal = existenciaLocal,
                            faltante = cantidadSolicitada - existenciaLocal
                        });
                    }
                }

                return Json(new
                {
                    success = true,
                    hayFaltantes = faltantes.Count > 0,
                    faltantes = faltantes
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VSCotizacion/VerificarExistencias");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Creacion de solicitud de traspaso")]
        public JsonResult CrearSolicitudTraspaso(IFormCollection fc)
        {
            try
            {
                int sucursalDestino = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                string folioReferencia = fc["folioReferencia"].ToString();

                var parameters = new Dictionary<string, object>
                {
                    { "folio", folioReferencia }
                };

                string datosPrevios = "SELECT id_encabezado, cli_prov FROM encabezadomov WHERE folio = @folio ";

                var resultDatos = RunQuery(datosPrevios, parameters);

                // Obtener la primer sucursal que tenga stock (distinta a la del usuario)
                // O bien puedes hacer que el usuario elija en el front. Por ahora tomamos la primera con stock.
                var productosJSON = fc["productosJSON"].ToString();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJSON);
                
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = empresaId,
                    IdArea = 5,
                    IdTpDoc = 35,          // Ajusta al tipo de documento de traspaso en tu catálogo
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = sucursalDestino,
                    Fch = DateTime.Now,
                    TpMov = "VSTRP",
                    CliProv = resultDatos[0]["cli_prov"].ToString(),
                    ComentAut = $"Traspaso generado automáticamente por cotización {folioReferencia}",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Imp = 0,
                    Estatus = 1,
                    EncabezadoPadre = Convert.ToInt32(resultDatos[0]["id_encabezado"])
                };

                var partidas = new List<PartidaDocumento>();
                foreach (var p in productos)
                {
                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = p.ContainsKey("faltante") ? decimal.Parse(p["faltante"]) : 0,
                        PvProd = 0,
                        Dto1 = 0,
                        ImpPart = 0,
                        Ud = p.ContainsKey("unidad") ? p["unidad"] : "PZA",
                    });
                }

                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                // Enviar correo al gerente/encargado de almacén
                EnviarCorreoTraspaso(
                    folioTraspaso: folio["folio_generado"].ToString(),
                    folioCotizacion: folioReferencia,
                    sucursalDestino: sucursalDestino,
                    productos: productos,
                    empresaId: empresaId
                );

                return Json(new
                {
                    success = true,
                    message = "Solicitud de traspaso generada.",
                    folioTraspaso = folio["folio_generado"].ToString()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VSCotizacion/CrearSolicitudTraspaso");
                return Json(new { success = false, message = ex.Message });
            }
        }


        public async Task<ActionResult> EnviarCorreoTraspaso(string folioTraspaso,string folioCotizacion, int sucursalDestino, List<Dictionary<string, string>> productos, int empresaId)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "empresaId", empresaId }
                };

                string query = @"
            SELECT u.email, u.nombre
            FROM usuarios u
            INNER JOIN roles r ON r.rolid = u.rolid
            WHERE r.nombre IN ('Gerente', 'Encargado Almacen')
              AND u.empresaid = @empresaId
              AND u.activo = true
            LIMIT 5";

                var destinatarios = RunQuery(query, parameters);
                if (destinatarios == null || destinatarios.Count == 0)
                {
                    return Ok();
                }

                // Armar el modelo
                var emailData = new TraspasoEmailViewModel
                {
                    FolioTraspaso = folioTraspaso,
                    FolioCotizacion = folioCotizacion,
                    SucursalDestino = sucursalDestino,
                    Productos = productos.Select(p =>
                    {
                        string productoId, descripcion, faltante;

                        p.TryGetValue("productoId", out productoId);
                        p.TryGetValue("descripcion", out descripcion);
                        p.TryGetValue("faltante", out faltante);

                        return new ProductoTraspasoItem
                        {
                            ProductoId = productoId ?? "",
                            Descripcion = descripcion ?? "",
                            Faltante = faltante ?? "0"
                        };
                    }).ToList()
                };

                // Renderizar la vista parcial una sola vez (el HTML es el mismo para todos)
                string htmlBody = await emailSender.RenderViewToStringAsync(
                    "~/Views/Email/_SolicitudTraspaso.cshtml",
                    emailData
                );

                foreach (var dest in destinatarios)
                {
                    string emailDest = dest["email"]?.ToString();
                    if (string.IsNullOrEmpty(emailDest)) continue;

                    await correoHelper.EnviarCorreoNotificacionAsync(
                        emailDest,
                        $"[Traspaso requerido] Folio {folioTraspaso}",
                        htmlBody
                    );
                }
                return Ok();
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VSCotizacion/EnviarCorreoTraspaso");
                System.Diagnostics.Debug.WriteLine($"Error enviando correo de traspaso: {ex.Message}");
                return StatusCode(500, "Error enviando correo de traspaso.");
            }
        }

    }
}