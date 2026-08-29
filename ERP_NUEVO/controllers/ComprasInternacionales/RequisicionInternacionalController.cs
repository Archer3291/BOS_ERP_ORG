using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BOS_ERP.Controllers;
using BOS_ERP.Helpers;
using BOS_ERP.Services;

namespace BOS_ERP.controllers.ComprasInternacionales
{
    [Authorize]
    public class RequisicionInternacionalController : Utilities
    {
        private readonly EmailSender _emailSender;
        private readonly CorreoHelper _correoHelper;

        public RequisicionInternacionalController(EmailSender emailSender, CorreoHelper correoHelper)
        {
            _emailSender = emailSender;
            _correoHelper = correoHelper;
        }

        [HttpPost]
        public JsonResult GetDocumentos()
        {
            string query = "SELECT id_encabezado, gen || '-' || nat || '-' || EXTRACT(YEAR FROM fch) || '-' || fol_doc AS descripcion " +
                "FROM encabezadomov e " +
                "where gen = 'CPI' and nat = 'SCINT' and estatus_id != 11 " +
                "order by descripcion asc;";
            var result = RunQuery(query);

            return Json(new { success = true, message = "datos obtenidos exitosamente.", result = result });

        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Internacionales", Accion = "Creacion de Proforma Invoice")]
        public async Task<JsonResult> CreateParcialidades(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();

                string description = fc["observaciones2"].ToString();
                decimal total = decimal.Parse(fc["total"].ToString());

                string query = "SELECT u.usuarioid, u.nombre || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   u2.usuarioid AS managerId, u2.nombre || u2.apellido AS nombreManager, " +
                    "   u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";
                parameters.Add("userName", User.Identity.Name);
                var userResult = RunQuery(query, parameters)[0];

                if (userResult == null || userResult.Count() == 0)
                {
                    return Json(new { success = false, message = "No se encontro un gerente para esta area." });
                }
                query = "SELECT idtpdoc FROM tpdoc where abreviaturatpdoc = @tpmov;";
                parameters.Add("tpmov", fc["tpnom"].ToString());
                var idTpDocumento = RunScalar(query, parameters);
                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 3,
                    IdTpDoc = Convert.ToInt32(idTpDocumento),
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = fc["tpnom"].ToString(),
                    ComentAut = description,
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Usr1 = Convert.ToInt32(userResult["managerid"]),
                    Imp = total,
                    CliProv = "srs",
                    Estatus = 13,
                    Ccy = fc["divisa"].ToString(),
                    Incoterm = fc["incoterm"].ToString(),
                    TipoProducto = fc["tipo_producto"].ToString(),
                    Ref = Convert.ToInt32(fc["proveedor"].ToString())
                };

                // 📌 Parsear productos como partidas
                var partidas = new List<PartidaDocumento>();

                // 📌 Guardar documento
                var folio = new Dictionary<string, object>();

                IFormFile archivo = Request.Form.Files[0];
                if (archivo != null && archivo.Length > 0)
                {
                    folio = GenerarDocumentoConPartidas(encabezado, partidas);
                    var parametersArchivos = new Dictionary<string, object>();
                    string nombreOriginal = archivo.FileName;
                    string ruta = "content/archivos_proformas_i/";
                    string uuid = Guid.NewGuid().ToString();
                    string extencion = Path.GetExtension(nombreOriginal);

                    var result = UploadFormFileToPath(ruta, archivo, uuid, extencion);

                    query = "INSERT INTO archivos_compras_proformai " +
                            "(nombre_original, path, uuid, extencion, encabezado_id) " +
                            "VALUES(@nombre_original, @ruta, @uuid, @extencion, @encabezado_id); ";
                    parametersArchivos.Add("nombre_original", nombreOriginal);
                    parametersArchivos.Add("ruta", ruta + uuid + extencion);
                    parametersArchivos.Add("uuid", uuid);
                    parametersArchivos.Add("extencion", extencion);
                    parametersArchivos.Add("encabezado_id", Convert.ToInt32(folio["IdEncabezado"]));

                    RunUpdate(query, parametersArchivos);
                }

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string urlCotizacion = $"{urlBase}/?id={folio["folio_generado"].ToString()}";

                var emailData = new EmailSenderModel
                {
                    Folio = folio["folio_generado"].ToString(),
                    Date = DateTime.Now,
                    SenderName = userResult["nombreusuario"].ToString(),
                    SenderEmail = userResult["emailusuario"].ToString(),
                    Total = total,
                    Description = description,
                    RecipientName = userResult["nombremanager"].ToString(),
                    RecipientEmail = userResult["emailmanager"].ToString(),
                    URL = urlCotizacion
                };

                string htmlBody = await _emailSender.RenderViewToStringAsync("~/Views/Email/_SendCreateRequisitionNotification.cshtml", emailData);

                //await _correoHelper.EnviarCorreoNotificacionAsync(emailData.RecipientEmail, "Nueva Requisicion Creada", htmlBody);

                _ = SendNotificationInterno(userResult["aliasmanager"].ToString(), userResult["emailmanager"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva solicitud de cotizacion creada",
                    message = $"El usuario {userResult["nombreusuario"]} ha creado una nueva solicitud de cotizacion con el folio {folio["folio_generado"]}, la cual requiere su aprobación.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                    folio = folio 
                });

                return Json(new { success = true, message = "solicitud de cotizacion creada exitosamente.", folio_generado = folio["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar los datos: " + ex.Message });

            }
        }


        [HttpPost, ValidateAntiForgeryToken] 
        [AuditAction(Modulo = "Compras Internacionales", Accion = "Creacion de requisision Orden de compra")]
        public async Task<JsonResult> OrdenDeCompraInternacional(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["total"].ToString()))
                {
                    return Json(new { success = false, message = "Faltan datos obligatorios del formulario." });
                }

                var parameters = new Dictionary<string, object>();

                string description = fc["observaciones"].ToString();
                decimal total = decimal.Parse(fc["total"].ToString());

                string query = "SELECT u.usuarioid, u.nombre || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   u2.usuarioid AS managerId, u2.nombre || u2.apellido AS nombreManager, " +
                    "   u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";
                parameters.Add("userName", User.Identity.Name);
                var userResult = RunQuery(query, parameters)[0];

                if (userResult == null || userResult.Count() == 0)
                {
                    return Json(new { success = false, message = "No se encontro un gerente para esta area." });
                }

                var usrParameter = new Dictionary<string, object>();
                string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, em.firma1, tipo_proceso, tipo_producto " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                usrParameter.Add("id", Convert.ToInt32(fc["padre"].ToString()));
                var usrId = RunQuery(usrquery, usrParameter)[0];

                query = "SELECT idtpdoc FROM tpdoc where abreviaturatpdoc = @tpmov;";
                parameters.Add("tpmov", fc["tpnom"].ToString());
                var idTpDocumento = RunScalar(query, parameters);
                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 3,
                    IdTpDoc = Convert.ToInt32(idTpDocumento),
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = fc["tpnom"].ToString(),
                    ComentAut = description,
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(usrId["usr0"]),
                    Fch0 = (DateTime)usrId["fch0"],
                    Usr1 = Convert.ToInt32(usrId["usr1"]),
                    Fch1 = (DateTime)usrId["fch1"],
                    Firma1 = usrId["firma1"].ToString(), 
                    Usr2 = GetUserId(User.Identity.Name),
                    Fch2 = DateTime.Now,
                    Imp = total,
                    CliProv = "srs",
                    Estatus = 7,
                    Ccy = fc["divisaSeleccionado"].ToString(),
                    Incoterm = fc["incotermSeleccionado"].ToString(),
                    TipoProducto = fc["tipo_producto"].ToString(),
                    Ref = Convert.ToInt32(fc["proveedor"].ToString()),
                    EncabezadoPadre = Convert.ToInt32(fc["padre"].ToString()),
                    TipoPoceso = "oc_internacional",
                };

                // 📌 Parsear productos como partidas
                string productosJson = fc["materiales"].ToString();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);

                var partidas = new List<PartidaDocumento>();
                int nro = 1;
                foreach (var p in productos)
                {
                    var parametersPartidas = new Dictionary<string, object>();

                    var queryProducto = "SELECT descr_prod from catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id";
                    parametersPartidas.Clear();
                    parametersPartidas.Add("cve_prod", p["codigo"]);
                    parametersPartidas.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    var descProd = RunScalar(queryProducto, parametersPartidas);

                    partidas.Add(new PartidaDocumento
                    {
                        NroPart = nro++,
                        CveProd = p.ContainsKey("codigo") ? p["codigo"] : "",
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
                        PvProd = p.ContainsKey("costoUnitario") ? decimal.Parse(p["costoUnitario"]) : 0,
                        CtoVtaPart = p.ContainsKey("precioVenta") ? decimal.Parse(p["precioVenta"]) : 0,
                        ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) * (p.ContainsKey("costoUnitario") ? decimal.Parse(p["costoUnitario"]) : 0),
                        Ud = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA"
                    });
                }

                // 📌 Guardar documento
                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id_encabezado;";
                parameters.Add("id_encabezado", Convert.ToInt32(fc["padre"].ToString()));
                RunUpdate(query, parameters);


                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string urlCotizacion = $"{urlBase}/?id={folio["folio_generado"].ToString()}";

                var emailData = new EmailSenderModel
                {
                    Folio = folio["folio_generado"].ToString(),
                    Date = DateTime.Now,
                    SenderName = userResult["nombreusuario"].ToString(),
                    SenderEmail = userResult["emailusuario"].ToString(),
                    Total = total,
                    Description = description,
                    RecipientName = userResult["nombremanager"].ToString(),
                    RecipientEmail = userResult["emailmanager"].ToString(),
                    URL = urlCotizacion
                };

                string htmlBody = await _emailSender.RenderViewToStringAsync("~/Views/Email/_SendCreateRequisitionNotification.cshtml", emailData);

                //await _correoHelper.EnviarCorreoNotificacionAsync(emailData.RecipientEmail, "Nueva Requisicion Creada", htmlBody);

                _ = SendNotificationInterno(userResult["aliasmanager"].ToString(), userResult["emailmanager"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva solicitud de cotizacion creada",
                    message = $"El usuario {userResult["nombreusuario"]} ha creado una nueva solicitud de cotizacion con el folio {folio["folio_generado"]}, la cual requiere su aprobación.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                return Json(new { success = true, message = "solicitud de cotizacion creada exitosamente.", folio_generado = folio["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar los datos: " + ex.Message });

            }
        }

        public JsonResult GetDocumentosAndPartidasData(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();

            string query = "select e.ccy, p.descr_prod, e.refe, p.cant_ud, p.cve_prod, p.ud, p.pv_prod from partidasdoc p " +
                "inner join encabezadomov e  " +
                "on e.id_encabezado = p.encabezado_id " +
                "where encabezado_id = @encabezado_id";
            parameters.Add("encabezado_id", Convert.ToInt32(fc["encabezado_id"].ToString()));
            var result = RunQuery(query,parameters);

            return Json(new { success = true, message = "datos obtenidos exitosamente.", result = result });

        }
        [HttpPost]
        public JsonResult TasasDeCambio()
        {
            try
            {
                string query = "SELECT id, fecha, dolar, euro, peso " +
                           "FROM tasas_cambio " +
                           "ORDER BY fecha DESC " +
                           "LIMIT 1;";

            var result = RunQuery(query);
            return Json(new { success = true, message = "datos obtenidos exitosamente.", result = result });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar los datos: " + ex.Message });

            }
        }
    }
}