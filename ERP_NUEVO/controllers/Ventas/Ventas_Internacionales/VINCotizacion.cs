using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Internacionales
{
    [Authorize]
    public class VINCotizacionController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;

        public VINCotizacionController(BOS_ERP.Services.EmailSender emailSenderService, BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Internacionales", Accion = "Creacion de cotizacion")]
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
                    else
                    {
                        throw new Exception("El cliente No exisre en la base de datos");
                    }
                }

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 17,
                    IdTpDoc = 53, //tipo de documento
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "VINCOT",
                    ComentAut = fc["comentarios"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Ref = idCliente,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Imp = Convert.ToDecimal(fc["total"].ToString()),
                    CliProv = fc["cliente"].ToString(),
                    Ccy = fc["moneda"].ToString(),
                    Estatus = 1,
                    Flete = Convert.ToDecimal(fc["flete"].ToString()), //Flete
                    VdrCpr = fc["vendedor"].ToString(),
                    Coment1 = fc["concepto"].ToString(),
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString(),
                    Mdp = fc["tipoPago"].ToString() == "credito" ? "PPD" : "PUE",
                };
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(
     fc["productosJSON"].ToString()
 );

                var partidas = new List<PartidaDocumento>();

                foreach (var p in productos)
                {
                    var parametersP = new Dictionary<string, object>();

                    string queryId = @"
        select id_catproductos 
        from catproductos 
        where cve_prod = @cve_prod  
        AND empresa_id = @empresa_id";

                    parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"]?.ToString() : "");
                    parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                    int productId = Convert.ToInt32(RunScalar(queryId, parametersP));

                    decimal cantidad = p.ContainsKey("cantidad")
                        ? Convert.ToDecimal(p["cantidad"])
                        : 0;

                    decimal precio = p.ContainsKey("precio")
                        ? Convert.ToDecimal(p["precio"])
                        : 0;

                    decimal descuento = p.ContainsKey("descuento")
                        ? Convert.ToDecimal(p["descuento"])
                        : 0;

                    decimal costoUnitario = p.ContainsKey("costoUnitario")
                        ? Convert.ToDecimal(p["costoUnitario"])
                        : 0;

                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = p.ContainsKey("productoId")
                            ? p["productoId"]?.ToString()
                            : "",

                        DescrProd = p.ContainsKey("descripcion")
                            ? p["descripcion"]?.ToString()
                            : "",

                        CantUd = cantidad,

                        PvProd = precio,

                        Dto1 = descuento,

                        ImpPart = cantidad * costoUnitario,

                        Ud = p.ContainsKey("unidad")
                            ? p["unidad"]?.ToString()
                            : "PZA",

                        IdProducto = productId,

                        TpDocAnt = p.ContainsKey("comentario")
                            ? p["comentario"]?.ToString()
                            : ""
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
                RegistrarErrorParaTicket(ex, "VINCotizacion/?");
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Nacionales", Accion = "Modificacion de cotizacion")]
        public JsonResult Modificar(IFormCollection fc)
        {
            try
            {
                int documentId = 0;
                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                    documentId = Convert.ToInt32(fc["documentid"].ToString());

                if (documentId == 0)
                    return Json(new { success = false, message = "ID de documento no válido." });

                int idCliente = 0;
                if (!string.IsNullOrEmpty(fc["cliente"].ToString()))
                {
                    var p = new Dictionary<string, object>
                    {
                        { "cliente", fc["cliente"].ToString() },
                        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                    };
                    var r = RunQuery(
                        "SELECT id_cliente FROM catclientes WHERE cve_cli = @cliente AND empresa_id = @empresa_id", p);
                    if (r.Count > 0)
                        idCliente = Convert.ToInt32(r[0]["id_cliente"]);
                }

                // Actualizar encabezado
                var encabezadoParams = new Dictionary<string, object>
                {
                    { "id_encabezado", documentId },
                    { "empresa_id",    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                    { "cli_prov",      fc["cliente"].ToString() },
                    { "ref",           idCliente },
                    { "ccy",           fc["moneda"].ToString() },
                    { "vdr_cpr",       fc["vendedor"].ToString() },
                    { "coment1",       fc["concepto"].ToString() },
                    { "coment_aut",    fc["comentarios"].ToString() },
                    { "par",           Convert.ToDecimal(fc["paridad"].ToString()) },
                    { "imp",           Convert.ToDecimal(fc["total"].ToString()) },
                    { "flete",         Convert.ToDecimal(fc["flete"].ToString()) },
                    { "nat_doc_padre_char", fc["ordenCompra"].ToString() },
                    { "fch_mod",       DateTime.Now },
                    { "usr_mod",       User.Identity.Name }
                };

                RunQuery(
                    @"UPDATE encabezadomov SET
                cli_prov            = @cli_prov,
                refe                 = @ref,
                ccy                 = @ccy,
                vdr_cpr             = @vdr_cpr,
                coment1             = @coment1,
                coment_aut          = @coment_aut,
                par                 = @par,
                imp                 = @imp,
                flete               = @flete,
                orden_compra  = @nat_doc_padre_char
              WHERE id_encabezado = @id_encabezado",
                    encabezadoParams);

                // Eliminar partidas existentes
                var deleteParams = new Dictionary<string, object>
                {
                    { "id_encabezado", documentId },
                    { "empresa_id",    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };
                RunQuery(
                    "DELETE FROM partidasdoc WHERE encabezado_id = @id_encabezado",
                    deleteParams);

                // Reinsertar partidas
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                int numParte = 1;
                foreach (var p in productos)
                {
                    var pp = new Dictionary<string, object>();
                    var qId = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id";
                    pp.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
                    pp.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    int productId = Convert.ToInt32(RunScalar(qId, pp));

                    decimal cantidad = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0;
                    decimal precio = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0;
                    decimal descuento = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0;
                    decimal importe = cantidad * precio * (1 - descuento / 100);

                    var partidaParams = new Dictionary<string, object>
                    {
                        { "id_encabezado", documentId },
                        { "empresa_id",    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                        { "num_parte",     numParte++ },
                        { "cve_prod",      p.ContainsKey("productoId")  ? p["productoId"]  : "" },
                        { "descr_prod",    p.ContainsKey("descripcion") ? p["descripcion"] : "" },
                        { "cant_ud",       cantidad },
                        { "pv_prod",       precio },
                        { "dto1",          descuento },
                        { "imp_part",      importe },
                        { "ud",            p.ContainsKey("unidad")      ? p["unidad"]      : "PZA" },
                        { "id_producto",   productId },
                        { "tp_doc_ant",    p.ContainsKey("comentario")  ? p["comentario"]  : "" },
                        { "folio",    Convert.ToInt32(RunScalar("SELECT fol_doc FROM encabezadomov WHERE id_encabezado = @id_encabezado",deleteParams)) }
                    };

                    RunQuery(
                        @"INSERT INTO partidasdoc
                    (encabezado_id, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, cve_prod, descr_prod,
                     cant_ud, pv_prod, dto1, imp_part, ud, producto_id, tp_doc_ant)
                  VALUES
                    (@id_encabezado, 'VIS', 'VINCOT', 17, 53, @folio, @cve_prod, @descr_prod,
                     @cant_ud, @pv_prod, @dto1, @imp_part, @ud, @id_producto, @tp_doc_ant)",
                        partidaParams);
                }

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = documentId,
                    Folio = RunScalar("SELECT fol_doc FROM encabezadomov WHERE id_encabezado = @id_encabezado", deleteParams).ToString()
                };

                return Json(new
                {
                    success = true,
                    message = "Cotización actualizada exitosamente.",
                    folio_generado = RunScalar("SELECT folio FROM encabezadomov WHERE id_encabezado = @id_encabezado", deleteParams).ToString()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VINCotizacion/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Nacionales", Accion = "Creacion de solicitud de traspaso")]
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
                    Estatus = 21,
                    EncabezadoPadre = Convert.ToInt32(resultDatos[0]["id_encabezado"])
                };

                var partidas = new List<PartidaDocumento>();
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
                        CantUd = p.ContainsKey("faltante") ? decimal.Parse(p["faltante"]) : 0,
                        PvProd = 0,
                        Dto1 = 0,
                        ImpPart = 0,
                        Ud = p.ContainsKey("unidad") ? p["unidad"] : "PZA",
                        IdProducto = productId
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
                RegistrarErrorParaTicket(ex, "VINCotizacion/CrearSolicitudTraspaso");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public async Task<ActionResult> EnviarCorreoTraspaso(
  string folioTraspaso,
  string folioCotizacion,
  int sucursalDestino,
  List<Dictionary<string, string>> productos,
  int empresaId)
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
                return Ok(new { success = true, message = "Correo de traspaso enviado." });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VINCotizacion/EnviarCorreoTraspaso");
                System.Diagnostics.Debug.WriteLine($"Error enviando correo de traspaso: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error enviando correo de traspaso." });
            }
        }


    }
}