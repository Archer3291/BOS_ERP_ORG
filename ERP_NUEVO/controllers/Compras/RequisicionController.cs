using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Services;
using System.Collections.Specialized;
using System.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public partial class RequisicionController : Utilities
    {
        private readonly IConfiguration _configuration;

        public RequisicionController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [Route("Requisition/Create")]
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Creacion de requisision de material")]
        public JsonResult create(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["materiales"].ToString()))
                {
                    return Json(new { success = false, message = "Faltan datos obligatorios del formulario." });
                }

                var area = GetAreaName(User.Identity.Name);

                if (area == "")
                {
                    return Json(new { success = false, message = "No se encontro un area asignada para este usuario." });
                }

                var parameters = new Dictionary<string, object>();

                string description = fc["observaciones"].ToString();

                string query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   u2.usuarioid AS managerId, u2.nombre || ' ' || u2.apellido AS nombreManager, " +
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
                int areaDoc = 0;
                if (fc["tpnom"].ToString() == "SCTZ")
                {
                    areaDoc = 2;
                }
                else
                {
                    areaDoc = 4;
                }

                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = areaDoc;
                encabezado.IdTpDoc = Convert.ToInt32(idTpDocumento);
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = fc["tpnom"].ToString();
                encabezado.ComentAut = description;
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.Usr1 = Convert.ToInt32(userResult["managerid"]);
                encabezado.Usr2 = int.Parse(fc["compradorseleccionado"].ToString());
                encabezado.CliProv = "srs";
                encabezado.Estatus = 1;
                encabezado.TipoProducto = fc["tipo_producto"].ToString();
                encabezado.EnPresupuesto = Convert.ToBoolean(fc["presupuesto"].ToString());
                encabezado.CentroCostos = GetInt(fc["centro_costos"].ToString());

                if (new List<string> { "SSRT", "SSRT", "SSR" }.Contains(fc["tpnom"].ToString()))
                {
                    encabezado.EsServicio = true;
                }

                // 📌 Parsear productos como partidas
                string productosJson = fc["materiales"].ToString();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson)
                    ?? new List<Dictionary<string, string>>();

                var partidas = productos.Select(p => new PartidaDocumento
                {
                    CveProd = GetString(p["codigo"]),
                    DescrProd = GetString(p["descripcion"]),
                    CantUd = GetDecimal(p["cantidad"], 0),
                    PvProd = GetDecimal(p["costoUnitario"], 0),
                    Dto1 = p.ContainsKey("descuento") ? GetDecimal(p["descuento"], 0) : 0,
                    CtoVtaPart = p.ContainsKey("precioVenta") && !string.IsNullOrWhiteSpace(p["precioVenta"])
                        ? GetDecimal(p["precioVenta"])
                        : null,
                    Ud = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA"
                }).ToList();

                var totales = DescuentosService.Normalizar(partidas);
                decimal total = totales.Base;

                encabezado.Sub = totales.Subtotal;
                encabezado.Dto = totales.Descuento;
                encabezado.Imp = total;

                // 📌 Guardar documento
                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                IFormFile? archivo = Request.Form.Files["archivo"];
                if (archivo != null && archivo.Length > 0)
                {
                    var nombreOriginal = archivo.FileName;
                    var extencion = Path.GetExtension(archivo.FileName);
                    var ruta = "content/archivos_solicitud/";
                    var uuid = Guid.NewGuid().ToString();

                    query = "INSERT INTO archivos_solicitud (nombre_original, path, uuid, extencion, encabezado_id) " +
                        "   VALUES(@nombreOriginal, @ruta, @uuid, @extencion, @encabezado)";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("nombreOriginal", nombreOriginal);
                    parameters.Add("ruta", ruta);
                    parameters.Add("uuid", uuid);
                    parameters.Add("extencion", extencion);
                    parameters.Add("encabezado", Convert.ToInt32(folio["IdEncabezado"]));
                    RunUpdate(query, parameters);
                    UploadFormFileToPath(ruta, archivo, uuid, extencion);
                }

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/GestionSolicitudes";
                string urlCotizacion = $"{urlBase}{path}?id={folio["folio_generado"].ToString()}";

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

                //string htmlBody = EmailSender.RenderViewToString(this.ControllerContext, "~/Views/Email/_SendCreateRequisitionNotification.cshtml", emailData);

                //CorreoHelper.EnviarCorreoNotificacion(emailData.RecipientEmail, "Nueva Requisicion Creada", htmlBody);

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
                    folio = folio["folio_generado"],
                });

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString(),
                    Observaciones = description
                };

                return Json(new { success = true, message = $"solicitud de cotizacion creada exitosamente.<br>Folio: {folio["folio_generado"].ToString()}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar los datos: " + ex.Message });

            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetRequisicionPartida(string folio)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folio))
                {
                    return Json(new { success = false, error = "Folio no proporcionado." });
                }

                string query = @"
                               SELECT 
                                   cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, fch, 
                                   cve_prod, descr_prod, cant_ud, imp_part, 
                                   imp_part * 0.16 AS iva, cto_vta_part, 
                                   imp_part * cant_ud AS importe, 
                                   '' AS observaciones
                               FROM partidasdoc
                               WHERE encabezado_id = @folio;";

                var parameters = new Dictionary<string, object>
                {
                    { "@folio", int.Parse(folio) }
                };

                var result = RunQuery(query, parameters);

                return Json(new { data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al consultar la remisión: " + ex.Message });
            }
        }

        public JsonResult Obtener(int id_encabezado)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id", id_encabezado);

            string encabezado = "SELECT em.iva, COALESCE(em.sub, em.imp) AS subtotal, COALESCE(em.dto, 0) AS descuento, " +
                "   em.imp AS total, em.nat, em.coment_aut AS observaciones, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   em.fch, u.nombre || ' ' || u.apellido AS solicitante " +
                "FROM encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "WHERE id_encabezado = @id";
            var encabezadoResult = RunQuery(encabezado, parameters);

            string partidas = "SELECT cant_ud AS cantidad, cve_prod AS codigo, descr_prod AS descripcion, " +
                "   pv_prod AS costoUnitario, imp_part AS total, ud AS unidad, COALESCE(dto1, 0) AS descuento, " +
                "   ROUND(COALESCE(imp_part, 0)::numeric * (1 - COALESCE(dto1, 0)::numeric / 100), 2) AS totaldescuento " +
                "FROM partidasdoc " +
                "WHERE encabezado_id = @id " +
                "AND variacion = (SELECT MAX(variacion) FROM partidasdoc WHERE encabezado_id = @id)";
            var partidasResult = RunQuery(partidas, parameters);

            var impuestos = "SELECT id_imp_oc, encabezado_id, impuesto_id, subtotal, importe, imp_variable " +
                "FROM imp_oc " +
                "WHERE encabezado_id = @id";
            var impuestosResult = RunQuery(impuestos, parameters);

            return Json(new { documento = encabezadoResult, materiales = partidasResult, impuestos = impuestosResult });
        }

        public JsonResult gerPdfData(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));

            var returnResult = new Dictionary<string, object>();

            // Obtener documento actual
            string query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, " +
                "   em.usr0, em.usr1, em.usr2, em.usr3, em.usr4, em.usr5, em.usr6, " +
                "   em.fch0, em.fch1, em.fch2, em.fch3, em.fch4, em.fch5, em.fch6, em.ccy, cc.n_cli, " +
                "   em.firma3 AS firmagerente, em.firma5 AS firmapresupuesto, em.firma6 AS firmadireccion, " +
                "   em.id_encabezado, em.coment_aut AS observaciones, em.refe, fac.uuid AS factura, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   u0.nombre || ' ' || u0.apellido AS solicitante, " +
                "   u1.nombre || ' ' || u1.apellido AS gerente, " +
                "   u2.nombre || ' ' || u2.apellido AS comprador, " +
                "   u3.nombre || ' ' || u3.apellido AS gerenteRevision, " +
                //"   u4.nombre || ' ' || u4.apellido AS finanzas, " +
                "   u5.nombre || ' ' || u5.apellido AS presupuestoRevision, " +
                "   u6.nombre || ' ' || u6.apellido AS direccion, " +
                "   a.nombre AS nombre_area, em.encabezados_padre, variacion, em.uuid " +
                "FROM encabezadomov em " +
                "LEFT JOIN usuarios u0 ON u0.usuarioid = em.usr0 " +
                "LEFT JOIN usuarios u1 ON u1.usuarioid = em.usr1 " +
                "LEFT JOIN usuarios u2 ON u2.usuarioid = em.usr2 " +
                "LEFT JOIN usuarios u3 ON u3.usuarioid = em.usr3 " +
                "LEFT JOIN usuarios u4 ON u4.usuarioid = em.usr4 " +
                "LEFT JOIN usuarios u5 ON u5.usuarioid = em.usr5 " +
                "LEFT JOIN usuarios u6 ON u6.usuarioid = em.usr6 " +
                "LEFT JOIN factura fac ON fac.encabezado_id = em.id_encabezado " +
                "LEFT JOIN areas a ON a.areaid = u1.areaid " +
                "LEFT JOIN catclientes cc ON cc.id_cliente = em.refe " +
                "WHERE em.id_encabezado = @id";
            var requisicion = RunQuery(query, parameters)[0];
            returnResult.Add("requisicion", requisicion);

            string logoPath = Path.Combine($"{Directory.GetCurrentDirectory()}\\wwwroot\\content\\img\\{HttpContext.Session.GetString("EmpresaFactura")}\\logo-light.png");
            string codigoQR = GenerarQRBase64ConLogo(requisicion["uuid"].ToString(), logoPath);
            returnResult.Add("codigoQR", "");

            // Obtener partidas del documento actual
            query = "WITH cantidades_por_pedimento AS ( " +
                "    SELECT  " +
                "        pp.partida_id, " +
                "        pp.pedimento_id, " +
                "        SUM(pp.cantidad) AS cantidad_pedimento " +
                "    FROM pedimentos_partidas pp " +
                "   GROUP BY pp.partida_id, pp.pedimento_id " +
                ") " +
                "SELECT  " +
                "    p.fol_doc, " +
                "    p.cant_ud AS cantidad,  " +
                "    p.pv_prod AS costoUnitario, " +
                "    p.cve_prod AS codigo, " +
                "    p.descr_prod AS descripcion, " +
                "    p.id_partidas, " +
                "    cve_vdr_cpr AS proveedor,  " +
                "    COALESCE(cant_ud * pv_prod, 0) AS total, " +
                "    COALESCE((cant_ud * pv_prod) * (1 - (dto1 / 100)), 0) AS totalDescuento, " +
                "    dto1 AS descuento, " +
                "    refe AS proveedorid, id_partidas, ud AS unidad, variacion, " +
                "    SUM(cpp.cantidad_pedimento) AS cantidad_total, " +
                "    STRING_AGG(pe.pedimento_sat || ' (' || cpp.cantidad_pedimento || ')', ', ') AS pedimentos, p.tp_doc_ant " +
                "FROM partidasdoc p " +
                "LEFT JOIN cantidades_por_pedimento cpp ON cpp.partida_id = p.id_partidas " +
                "LEFT JOIN pedimentos pe ON pe.id_pedimento = cpp.pedimento_id " +
                "WHERE p.encabezado_id = @id " +
                "  AND p.variacion = (SELECT MAX(variacion) FROM partidasdoc WHERE encabezado_id = @id) " +
                "GROUP BY  " +
                "    p.fol_doc, p.pv_prod, p.cve_prod, p.descr_prod, p.id_partidas " +
                "ORDER BY p.id_partidas;";
            var partidas = RunQuery(query, parameters);
            returnResult.Add("partidas", partidas);

            // Obtener partidas del encabezado padre
            parameters.Add("encabezados_padre", Convert.ToInt32(requisicion["encabezados_padre"]));
            query = "SELECT p.fol_doc, p.cant_ud, p.pv_prod, p.cve_prod, p.descr_prod, p.id_partidas, imp_part AS total " +
                "FROM partidasdoc p " +
                "WHERE p.encabezado_id = @encabezados_padre " +
                "AND p.variacion = (SELECT MAX(variacion) FROM partidasdoc WHERE encabezado_id = @encabezados_padre)";
            var partidasPadre = RunQuery(query, parameters);
            returnResult.Add("partidasPadre", partidasPadre);

            // Obtener cotizaciones del documento actual
            query = "SELECT id_producto_opcion, partidas_id, prov_id, encabezado_id, producto, " +
                "   precio, descripcion, cantidad, uuid, extencion, ruta, proveedor_nombre, " +
                "   nombre_original, p.cant_ud, (po.cantidad * po.precio) total, unidad " +
                "FROM productos_opciones po " +
                "INNER JOIN (SELECT cant_ud, id_partidas FROM partidasdoc) p ON p.id_partidas = po.partidas_id " +
                "WHERE encabezado_id = @id";
            var opciones = RunQuery(query, parameters);
            returnResult.Add("opciones", opciones);

            // Obtener todos los documentos vinculados con el actual
            query = "WITH RECURSIVE relacionados AS (" +
                "   SELECT em.id_encabezado, em.encabezados_padre, " +
                "       em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "       em.variacion, em.variacion_padre, ARRAY[em.id_encabezado] AS visitados " +
                "   FROM encabezadomov em " +
                "   WHERE em.id_encabezado = @id " +
                "   UNION ALL " +
                "   SELECT e.id_encabezado, e.encabezados_padre, " +
                "       e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio, " +
                "       e.variacion, e.variacion_padre, r.visitados || e.id_encabezado " +
                "   FROM encabezadomov e " +
                "   INNER JOIN relacionados r ON e.id_encabezado = r.encabezados_padre OR e.encabezados_padre = r.id_encabezado " +
                "   WHERE NOT e.id_encabezado = ANY(r.visitados) " +
                ")" +
                "SELECT DISTINCT r.id_encabezado, r.encabezados_padre, r.folio, r.variacion, r.variacion_padre, " +
                "   a.path, a.uuid, a.extencion, a.nombre_original, COUNT(p.*) polizas " +
                "FROM relacionados r " +
                "LEFT JOIN archivos_solicitud a ON a.encabezado_id = r.id_encabezado " +
                "LEFT JOIN polizas p ON p.referencia = r.id_encabezado " +
                "GROUP BY r.id_encabezado, r.encabezados_padre, r.folio, r.variacion, r.variacion_padre, a.path, a.uuid, a.extencion, a.nombre_original " +
                "ORDER BY r.id_encabezado";
            var encabezadosAnteriores = RunQuery(query, parameters);
            returnResult.Add("encabezadosAnteriores", encabezadosAnteriores);

            // Obtener todas las variaciones del documento actual
            query = "WITH RECURSIVE relacionados AS (" +
                "   SELECT em.id_encabezado, em.variacion_padre, " +
                "       em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "       em.variacion, ARRAY[em.id_encabezado] AS visitados, em.fch " +
                "   FROM encabezadomov em " +
                "   WHERE em.id_encabezado = @id " +
                "   UNION ALL " +
                "   SELECT e.id_encabezado, e.variacion_padre, " +
                "       e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio, " +
                "       e.variacion, r.visitados || e.id_encabezado, e.fch " +
                "   FROM encabezadomov e " +
                "   INNER JOIN relacionados r ON e.id_encabezado = r.variacion_padre OR e.variacion_padre = r.id_encabezado " +
                "       WHERE NOT e.id_encabezado = ANY(r.visitados) " +
                ") " +
                "SELECT DISTINCT variacion, id_encabezado, folio, fch " +
                "FROM relacionados ORDER BY id_encabezado";
            var variaciones = RunQuery(query, parameters);
            returnResult.Add("variaciones", variaciones);

            // Obtener cotizaciones de los documentos con variacion
            query = "SELECT p.fol_doc, p.cant_ud, p.pv_prod, p.cve_prod, p.descr_prod, p.id_partidas, imp_part AS total " +
                "FROM partidasdoc p " +
                "WHERE encabezado_id = @id ";
            var partidasOpcionesVariacion = RunQuery(query, parameters);
            returnResult.Add("partidas_opciones_variacion", partidasOpcionesVariacion);

            // Historial de movimientos
            query = "SELECT hdm.id_h_doc_mov, hdm.documento_id, mo.tipo_movimiento, hdm.fecha_registro, u.nombreusuario " +
                "FROM h_doc_mov hdm " +
                "inner join  movimientos_ocdi mo on mo.id_movimiento = hdm.movimiento_id " +
                "inner join usuarios u on u.usuarioid = hdm.user_id " +
                "WHERE documento_id = @id";
            var historialMovimientos = RunQuery(query, parameters);
            returnResult.Add("historialMovimientos", historialMovimientos);

            query = "SELECT io.encabezado_id, io.impuesto_id, io.subtotal, io.importe, io.orden_apl, io.imp_variable, " +
                "   ci.cve_impuesto, ci.es_retencion " +
                "FROM imp_oc io " +
                "INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
                "WHERE encabezado_id = @id";
            var impuestos = RunQuery(query, parameters);
            returnResult.Add("impuestos", impuestos);

            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            string GetEmisor(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            var datosEmpresa = new Dictionary<string, string>();
            datosEmpresa.Add("razon_social", GetEmisor("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisor("Rfc"));
            datosEmpresa.Add("direccion", GetEmisor("Direccion"));
            datosEmpresa.Add("telefono", GetEmisor("Telefono"));

            returnResult.Add("datos_empresa", datosEmpresa);

            return Json(returnResult);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult OCDirecta(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["total"].ToString()) || string.IsNullOrWhiteSpace(fc["proveedores"].ToString()))
                {
                    return Json(new { success = false, message = "Faltan datos obligatorios del formulario." });
                }

                // ===============================
                // Datos base
                // ===============================
                decimal totalFormulario = decimal.Parse(fc["total"].ToString());
                string description = fc["observaciones"].ToString();
                string usuario = User.Identity.Name;
                DateTime fecha = DateTime.Now;
                string html = "<ul>";

                // ===============================
                // Obtener usuario
                // ===============================
                var parameters = new Dictionary<string, object>();
                string idUserQuery =
                    "SELECT UsuarioId, AreaId, Email, nombre || apellido AS usuario " +
                    "FROM Usuarios WHERE NombreUsuario = @nombre_usuario";

                parameters.Add("nombre_usuario", usuario);
                var userResult = RunQuery(idUserQuery, parameters).First();

                // ===============================
                // Obtener gerente de área
                // ===============================
                parameters.Clear();
                string getAreaManagerQuery =
                    "SELECT usuarioid, email, nombreusuario " +
                    "FROM usuarios WHERE areaid = @AreaId AND rolid = 8";

                parameters.Add("AreaId", Convert.ToInt32(userResult["areaid"]));
                var areaManagerResult = RunQuery(getAreaManagerQuery, parameters);

                if (areaManagerResult == null || !areaManagerResult.Any())
                {
                    return Json(new { success = false, message = "No se encontró un gerente para esta área." });
                }

                // ===============================
                // Parsear productos
                // ===============================
                string materialesJson = fc["proveedores"].ToString();
                var proveedores = JsonConvert.DeserializeObject<List<ProveedorModel>>(materialesJson);

                if (proveedores == null || !proveedores.Any())
                    throw new Exception("No se recibieron proveedores.");

                if (proveedores.Any(p => p.Productos == null || !p.Productos.Any()))
                    throw new Exception("Hay proveedores sin productos.");

                var partidas = proveedores
                    .SelectMany(proveedor => proveedor.Productos.Select(prod => new PartidaDocumento
                    {
                        CveProd = prod.CveProd,
                        DescrProd = prod.DescrProd,
                        CantUd = prod.CantUd,
                        PvProd = prod.PvProd,
                        Ref = proveedor.Id_Prov,
                        CveVdrCpr = proveedor.N_Prov,
                        Ud = string.IsNullOrEmpty(prod.Ud) ? "PZA" : prod.Ud,
                        Dto1 = prod.Dto1,
                        CtoVtaPart = prod.CtoVtaPart,
                    }))
                    .ToList();

                DescuentosService.Normalizar(partidas);

                // ===============================
                // Agrupar por proveedor
                // ===============================
                var partidasPorProveedor = partidas
                    .GroupBy(p => p.Ref.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var foliosGenerados = new List<string>();
                var folio = new Dictionary<string, object>();

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // ===============================
                            // Crear una OC por proveedor
                            // ===============================
                            foreach (var proveedor in partidasPorProveedor)
                            {
                                int proveedorId = proveedor.Key;
                                var partidasProveedor = proveedor.Value;
                                var proveedorData = proveedores.First(p => p.Id_Prov == proveedorId);

                                var totalesProveedor = DescuentosService.Normalizar(partidasProveedor);
                                decimal bases = totalesProveedor.Base;
                                decimal total = bases;

                                var impuestosCalculados = new List<(int id, decimal tasa, decimal importe)>();

                                foreach (var imp in proveedorData.Impuestos)
                                {
                                    parameters = new Dictionary<string, object>();
                                    string query = "SELECT tasa_imp, es_retencion FROM cat_impuestos WHERE id_impuesto = @impuesto";
                                    parameters.Add("impuesto", imp.IdImpuesto);

                                    var impuesto = RunQuery(query, parameters, false, conn, tx);
                                    if (impuesto.Count == 0) continue;

                                    decimal tasa = GetDecimal(impuesto[0]["tasa_imp"], 0).Value / 100m;
                                    bool esRetencion = Convert.ToBoolean(impuesto[0]["es_retencion"]);
                                    decimal importe = DescuentosService.Redondear(bases * tasa);

                                    total += esRetencion ? -importe : importe;
                                    impuestosCalculados.Add((imp.IdImpuesto, tasa, esRetencion ? -importe : importe));
                                }

                                var encabezado = new DocumentoEncabezado();
                                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                                encabezado.IdArea = 2;
                                encabezado.IdTpDoc = 12;
                                encabezado.Anio = fecha.Year;
                                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                                encabezado.Fch = fecha;
                                encabezado.TpMov = "OCD";
                                encabezado.ComentAut = description;
                                encabezado.UsrDoc = usuario;
                                encabezado.FchCap = fecha;
                                encabezado.Usr0 = GetUserId(usuario);
                                encabezado.Fch0 = fecha;
                                encabezado.Usr1 = Convert.ToInt32(areaManagerResult[0]["usuarioid"]);
                                encabezado.Usr2 = GetInt(fc["compradorseleccionado"].ToString());
                                encabezado.Usr6 = GetInt(fc["directorSeleccionado"].ToString());
                                encabezado.Fch6 = fecha;
                                encabezado.Sub = totalesProveedor.Subtotal;
                                encabezado.Imp = DescuentosService.Redondear(total);
                                encabezado.CliProv = proveedorData.N_Prov;
                                encabezado.Estatus = 17;
                                encabezado.TipoPoceso = "orden_compra_directa";
                                encabezado.CentroCostos = Convert.ToInt32(fc["centro_costos"].ToString());
                                encabezado.Ref = proveedorId;
                                encabezado.Dto = totalesProveedor.Descuento;
                                encabezado.Ccy = "PESOS";

                                folio = GenerarDocumentoConPartidas(encabezado, partidasProveedor, conn, tx);

                                foreach (var imp in impuestosCalculados)
                                {
                                    parameters = new Dictionary<string, object>();
                                    string query = "INSERT INTO imp_oc " +
                                        "(encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom) " +
                                        "VALUES (@encabezado_id, @impuesto_id, @subtotal, @importe, 1, @imp_variable, @prov_nom)";

                                    parameters.Add("encabezado_id", folio["IdEncabezado"]);
                                    parameters.Add("impuesto_id", imp.id);
                                    parameters.Add("subtotal", bases);
                                    parameters.Add("importe", imp.importe);
                                    parameters.Add("imp_variable", (int)(imp.tasa * 100));
                                    parameters.Add("prov_nom", proveedorData.N_Prov);

                                    RunUpdate(query, parameters, false, conn, tx);
                                }


                                foliosGenerados.Add(folio["folio_generado"].ToString());
                                html += $"<li>{folio["folio_generado"].ToString()}</li>";
                            }

                            // 3️⃣ Todo bien → commit
                            tx.Commit();
                        }
                        catch
                        {
                            // 💣 Algo explotó → rollback real
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                html += "</ul>";
                return Json(new
                {
                    success = true,
                    message = $"Órdenes de compra creadas correctamente. \n{html}",
                    folios = foliosGenerados
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al procesar la orden de compra: " + ex.Message
                });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetListaIva()
        {
            string query = "SELECT id_impuesto, cve_impuesto, \"desc\", tasa_imp, tipo_imp, es_retencion " +
                "FROM cat_impuestos";
            var ivas = RunQuery(query);

            return Json(ivas);
        }

        public JsonResult GetRechazadasDirector(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "em.estatus_id = 15 AND @userId IN (em.usr0, em.usr1, em.usr6)",
                new Dictionary<string, object> { ["userId"] = GetUserId(User.Identity.Name) },
                usuarioJoin: "em.usr0");
        }

        public JsonResult GetCotizaciones()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            string query = "SELECT em.id_encabezado, em.nat, em.ccy, em.refe, em.dto, em.imp, em.sub, em.tp_mov, em.coment_aut, em.usr0, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END as folio, " +
                "   u.nombre || ' ' || u.apellido creado_por, c.cve_cli, c.n_cli " +
                "FROM encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr0 " +
                "INNER JOIN catclientes c ON c.id_cliente = em.refe " +
                "WHERE em.nat = 'VICOT' AND em.estatus_id != 11 AND em.suc = @sucursal " +
                "ORDER BY em.fch DESC";

            var cotizaciones = RunQuery(query, parameters);

            return Json(cotizaciones);
        }

        public JsonResult GetProductosCotizacion(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id_encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
            string query = "SELECT pd.nat, pd.fol_doc, pd.cant_ud, pd.ud, pd.pv_prod, pd.imp_part, pd.encabezado_id, " +
                "   pd.id_partidas, COALESCE(pd.dto1, 0) AS dto1, " +
                "   ROUND(COALESCE(pd.imp_part, 0)::numeric * (1 - COALESCE(pd.dto1, 0)::numeric / 100), 2) AS totaldescuento, " +
                "   c.id_catproductos, c.cve_prod, c.descr_prod " +
                "FROM partidasdoc pd " +
                "INNER JOIN catproductos c ON c.id_catproductos = pd.producto_id " +
                "WHERE  pd.encabezado_id = @id_encabezado";

            var partidas = RunQuery(query, parameters);

            return Json(partidas);
        }

        #region Flujo de documentos
        public JsonResult GetFlujoDocumento(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("controller", GetString(fc["controller"].ToString()));
            parameters.Add("action", GetString(fc["action"].ToString()));

            string query = "SELECT tf.id_flujo, pd.id_proceso, pd.nombre, tf.idtpdoc_origen, tf.idtpdoc_destino, " +
                "   tf.descripcion, tf.obligatorio " +
                "FROM tpdoc_flujo tf " +
                "INNER JOIN procesos_documento pd ON pd.id_proceso = tf.id_proceso " +
                "INNER JOIN  proceso_endpoints pe ON pe.proceso_id = pd.id_proceso " +
                "WHERE pe.controlador = @controller AND pe.accion = @action";
            var flujo = RunQuery(query, parameters);

            query = "SELECT t.idtpdoc, t.tpdoc, t.descr, a.areaid idarea, a.nombre area, " +
                "   a.abreviatura || '-' || TO_CHAR(CURRENT_DATE, 'YY') || '-' || t.abreviaturatpdoc || '-' || '00' AS abreviaturatpdoc " +
                "FROM tpdoc t " +
                "INNER JOIN areas a ON a.areaid = t.idarea";
            var tpdoc = RunQuery(query);

            return Json(new { flujo, tpdoc });
        }
        #endregion
    }
}