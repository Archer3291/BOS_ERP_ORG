using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Services;
using System.Net;
using Microsoft.AspNetCore.Mvc;


namespace BOS_ERP.Controllers.Traslados
{
    public class SolicitudesController : Utilities
    {

        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;
        public SolicitudesController(BOS_ERP.Services.EmailSender emailSenderService, BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }


        #region Traslados internos de material
        #region Obtener datos
        // Obtiene todos los productos que tienen stock
        public JsonResult GetTraslados(string nombre, string sortColumn, string sortDir, int? id_sucursal, int? id_almacen, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            int sucursal = id_sucursal.HasValue && id_sucursal.Value > 0 ? id_sucursal.Value : Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            parameters.Add("nombre", nombre ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("sucursal", sucursal);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var allowedColumns = new HashSet<string> {
                "codigo", "descripcion",
                "almacendescripcion", "cantidad", "ulocation",
                "cve_almacen", "tarima", "costo_promedio_unitario", "costo_promedio_total"
            };

            if (!allowedColumns.Contains(sortColumn))
                sortColumn = "codigo";

            string where = "";

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where = " AND (cp.cve_prod ILIKE '%' || @nombre || '%' " +
                    "OR cp.descr_prod ILIKE '%' || @nombre || '%' " +
                    "OR ca.descripcion ILIKE '%' || @nombre || '%' " +
                    "OR cn.ulocation ILIKE '%' || @nombre || '%' " +
                    "OR ct.codigo ILIKE '%' || @nombre || '%' " +
                    "OR ca.cve_almacen ILIKE '%' || @nombre || '%' ) ";
            }

            if (id_almacen.HasValue && id_almacen.Value > 0)
            {
                where += " AND ca.id_almacen = @id_almacen ";
                parameters.Add("id_almacen", id_almacen.Value);
            }

            string query = "SELECT cp.id_catproductos AS id_producto, ca.id_almacen, ct.id_tarima, cp.cve_prod AS codigo, cp.descr_prod AS descripcion, " +
                "    cp.udm AS unidadproducto, ct.codigo AS tarima, tp.cantidad, cn.ulocation, cun.descripcion AS unidadnombre, " +
                "    cun.id_udm AS unidad, ca.cve_almacen, ca.descripcion AS almacendescripcion, ca.tipo AS tipoalmacen, cu.cve_sucursal AS clavesucursal, " +
                "    cu.descripcion AS descripcionsucursal, cu.id_sucursal " +
                "FROM tarima_productos tp " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id " +
                "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cu ON cu.id_sucursal = ca.sucursal_id " +
                "INNER JOIN catunidades cun ON cun.id_udm = tp.unidad " +
                $"WHERE tp.cantidad > 0 AND cu.id_sucursal = @sucursal {where} " +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var traslados = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM tarima_productos tp " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id " +
                "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cu ON cu.id_sucursal = ca.sucursal_id " +
                "INNER JOIN catunidades cun ON cun.id_udm = tp.unidad " +
                $"WHERE tp.cantidad > 0 AND cu.id_sucursal = @sucursal {where}";
            int total = Convert.ToInt32(RunScalar(query, parameters));
            return Json(new { data = traslados, total });
        }

        // Obtiene las ubicaciones como sucursales y demas
        public JsonResult GetUbicaciones()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();
            string query = "SELECT ct.id_tarima, ct.codigo, ca.cve_almacen, ca.tipo, cs.id_sucursal, cn.ulocation " +
                "FROM cattarimas ct " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                "WHERE ca.tipo IN ('Stock', 'Recepcion', 'Temporal')";
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            result.Add("tarimas", RunQuery(query, parameters));

            query = "SELECT id_sucursal, cve_sucursal, descripcion, empresa_id FROM catsucursales";
            result.Add("sucursales", RunQuery(query));

            query = "SELECT cata.id_almacen, cata.cve_almacen, cata.descripcion almacen_descripcion, cata.tipo, " +
                "   cats.cve_sucursal, cats.descripcion sucursal_descripcion, cats.id_sucursal " +
                "FROM catalmacenes cata " +
                "INNER JOIN catsucursales cats ON cats.id_sucursal = cata.sucursal_id ";
                //"WHERE cata.tipo IN ('Stock', 'Recepcion', 'Temporal')";
            result.Add("almacenes", RunQuery(query));

            query = "SELECT empresaid, rfc, nombre FROM empresas";
            result.Add("empresas", RunQuery(query));

            return Json(result);
        }
        #endregion

        #region Acciones de movimiento
        // Genera un movimiento de material interno o entre tarimas
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Traslados", Accion = "Traslado interno de material")]
        public JsonResult GenerarMovimiento(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                var productosLista = new List<Dictionary<string, object>>();
                var producto = JsonConvert.DeserializeObject<Dictionary<string, object>>(fc["producto"].ToString());
                productosLista.Add(producto);

                var movimiento = JsonConvert.DeserializeObject<Dictionary<string, object>>(fc["movimiento"].ToString());
                string query = "SELECT cantidad FROM tarima_productos WHERE producto_id = @id_producto AND tarima_id = @tarima_origen";
                parameters.Add("id_producto", Convert.ToInt32(producto["id_producto"]));
                parameters.Add("tarima_origen", Convert.ToInt32(movimiento["tarimaOrigen"]));
                var cantidadDisponible = RunScalar(query, parameters);

                if (Convert.ToInt32(movimiento["cantidad"]) <= 0)
                {
                    Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Json(new { icon = "error", title = "Cantidad incorrecta", html = "La cantidad del producto no puede ser 0" });
                }

                if (Convert.ToDecimal(cantidadDisponible) < Convert.ToDecimal(movimiento["cantidad"]))
                {
                    Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Json(new { icon = "error", title = "Cantidad insuficiente", html = "La cantidad a mover es mayor a la cantidad disponible en la tarima" });
                }

                parameters.Clear();
                int? destino = null;
                if (movimiento["tarimaDestino"] != DBNull.Value && !string.IsNullOrWhiteSpace(movimiento["tarimaDestino"]?.ToString()))
                {
                    query = "SELECT COUNT(*) FROM cattarimas WHERE id_tarima = @tarimaDestino";
                    parameters.Add("tarimaDestino", Convert.ToInt32(movimiento["tarimaDestino"]));
                    var existeTarimaDestino = RunScalar(query, parameters);

                    if (Convert.ToInt32(existeTarimaDestino) == 0)
                    {
                        Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        return Json(new { icon = "error", title = "Tarima destino no existe", html = "La tarima destino no existe, por favor verifique" });
                    }

                    destino = Convert.ToInt32(movimiento["tarimaDestino"]);
                }

                string motivo = string.IsNullOrEmpty(movimiento["motivo"]?.ToString()) ? null : movimiento["motivo"].ToString();
                RegistrarMovimiento(productosLista, GetUserId(User.Identity.Name), fc["tipoMovimiento"].ToString(), Convert.ToInt32(movimiento["tarimaOrigen"]), destino, motivo, null, fc["comentarios"].ToString());

                return Json(new { icon = "success", title = "Movimiento registrado", html = "Se registro correctamente el movimiento del material" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error inesperado", html = ex.Message });
            }
        }
        #endregion
        #endregion

        #region solicitudes de traslado
        // Genera la solicitud de traslado
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Traslados", Accion = "Generar solicitud de traslado entre sucursales")]
        public async Task<ActionResult> GenerarSolicitudTraslado(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());

                var area = GetAreaName(User.Identity.Name);

                if (area == "")
                {
                    return Json(new { success = false, message = "No se encontro un area asignada para este usuario." });
                }

                parameters = new Dictionary<string, object>();
                string query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombrecompleto, u.email, u.nombreusuario " +
                    "FROM usuarios u " +
                    "INNER JOIN areas a ON a.areaid = u.areaid " +
                    "INNER JOIN catsucursales cs ON cs.id_sucursal = u.sucursal_id " +
                    "WHERE a.nombre = @area AND u.rolid = 8 AND cs.id_sucursal = @sucursal";
                parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                parameters.Add("area", GetAreaName(User.Identity.Name));
                var result = RunQuery(query, parameters);

                if (result == null || result.Count() == 0)
                {
                    return Json(new { icon = "error", title = "No se encontro un gerente para esta area." });
                }

                var gerenteAlmacen = result[0];

                query = "SELECT  u.nombre || ' ' || u.apellido AS nombreUsuario, email " +
                    "FROM usuarios u " +
                    "WHERE u.nombreusuario = @userName";
                parameters.Add("userName", User.Identity.Name);
                var usuario = RunQuery(query, parameters)[0];


                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 5,
                    IdTpDoc = 35,
                    TpMov = "SOLINV",
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    ComentAut = fc["comentario"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Usr1 = Convert.ToInt32(gerenteAlmacen["usuarioid"]),
                    CliProv = "srs",
                    Estatus = 21,
                    TipoPoceso = fc["tpnom"].ToString(),
                };

                var partidas = new List<PartidaDocumento>();
                int nro = 1;
                foreach (var p in productos)
                {
                    var cantidad = GetDecimal(p["cantidad"]);

                    partidas.Add(new PartidaDocumento
                    {
                        NroPart = nro++,
                        CveProd = GetString(p["codigo"]),
                        DescrProd = GetString(p["descripcion"]),
                        CantUd = cantidad,
                        Ud = GetString(p["unidad"]),
                        IdProducto = GetInt(p["id_producto"])
                    });
                }

                var documento = GenerarDocumentoConPartidas(encabezado, partidas);

                var request = HttpContext.Request;
                var urlBase = $"{request.Scheme}://{request.Host}";
                //var urlBase = Request.Url.GetLeftPart(UriPartial.Authority);
                string path = "/Traslados/SolicitudesRecibidas";
                string urlCotizacion = $"{urlBase}{path}?id={documento["folio_generado"].ToString()}";

                var emailData = new EmailSenderModel
                {
                    Folio = documento["folio_generado"].ToString(),
                    Date = DateTime.Now,
                    SenderName = usuario["nombreusuario"].ToString(),
                    SenderEmail = usuario["email"].ToString(),
                    Description = fc["comentario"].ToString(),
                    RecipientName = gerenteAlmacen["nombrecompleto"].ToString(),
                    RecipientEmail = gerenteAlmacen["email"].ToString(),
                    URL = urlCotizacion
                };

                string htmlBody = await emailSender.RenderViewToStringAsync(
                    "~/Views/Emal/_SendCreateTransferRequestNotification.cshtml",
                    emailData
                );
                await correoHelper.EnviarCorreoNotificacionAsync(
                        emailData.RecipientEmail,
                       "Nueva Solicitud Creada",
                        htmlBody
                    );

                _ = SendNotificationInterno(gerenteAlmacen["nombreusuario"].ToString(), gerenteAlmacen["email"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva solicitud de cotizacion creada",
                    message = $"El usuario {usuario["nombreusuario"]} ha creado una nueva solicitud de cotizacion con el folio {documento["folio_generado"]}, la cual requiere su aprobación.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                    folio = documento["folio_generado"],
                });

                return Json(new { icon = "success", text = "La solicitud de tranferencia de inventario se creo con el siguiente folio", folio_generado = documento["folio_generado"] });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", text = $"{ex.Message}" });
            }
        }
        #endregion

        #region Solicitudes recibidas
        #region Obtener datos
        public JsonResult GetSolicitudData(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("uuid", fc["uuid"].ToString());

            var returnResult = new Dictionary<string, object>();

            // Obtener documento actual
            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, COALESCE(em.coment_aut, '') AS coment_aut, em.usr1, em.id_encabezado, " +
                "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch) || '-' || em.fol_doc || " +
                "       CASE WHEN em.variacion > 0 " +
                "       THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   fch, em.uuid, em.suc, cs.descripcion, em.variacion, " +
                "   u0.nombre || ' ' || u0.apellido AS solicitante, cs0.descripcion AS sucursalsolicitante, cs0.id_sucursal AS idsucsolicitante " +
                "FROM encabezadomov em " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = em.suc " +
                "INNER JOIN usuarios u0 ON u0.usuarioid = em.usr0 " +
                "INNER JOIN catsucursales cs0 ON cs0.id_sucursal = u0.sucursal_id " +
                "WHERE em.uuid = @uuid";
            var ordenCompra = RunQuery(query, parameters)[0];

            // Obtener partidas del documento actual
            query = "SELECT pd.cant_ud AS cantidadSolicitada, pd.cve_prod AS codigo, pd.descr_prod AS descripcion, pd.ud, udm.id_udm AS unidad, pd.variacion, " +
                "   COALESCE(stk.cantidadStock, 0) AS cantidadStock, em.folio, em.suc sucursal_solicitante, stk.id_sucursal sucursal_solicitada, pd.producto_id " +
                "FROM partidasdoc pd " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id AND em.nat IN ('SOLINV', 'TRAINV') " +
                "LEFT JOIN catunidades udm ON udm.cve_udm = pd.ud " +
                "LEFT JOIN ( " +
                "   SELECT tp.producto_id, cs.id_sucursal, SUM(tp.cantidad) AS cantidadStock, c.cve_prod " +
                "   FROM tarima_productos tp " +
                "   INNER JOIN catproductos c ON c.id_catproductos = tp.producto_id " +
                "   INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "   INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "   INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "   INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "   INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "   INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                "   WHERE ca.tipo = 'Stock' " +
                "   GROUP BY tp.producto_id, cs.id_sucursal, c.cve_prod " +
                ") stk ON stk.cve_prod = pd.cve_prod AND stk.id_sucursal = @sucursal " +
                "WHERE em.uuid = @uuid " +
                "ORDER BY pd.cve_prod DESC;";

            if (!string.IsNullOrEmpty(fc["sucursalOrigen"].ToString()))
            {
                parameters.Add("sucursal", Convert.ToInt32(fc["sucursalOrigen"].ToString()));
            }
            else
            {
                parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            }

            var partidas = RunQuery(query, parameters);
            returnResult.Add("partidas", partidas);

            query = "SELECT ct.codigo AS tarima, cn.ulocation, ct.id_tarima, csu.descripcion " +
                "FROM cattarimas ct " +
                "LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "LEFT JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "LEFT JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "LEFT JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id " +
                "WHERE ca.tipo = 'Stock' AND csu.id_sucursal = @sucursal";
            var ubicaciones = RunQuery(query, parameters);

            return Json(new { ordenCompra, partidas, ubicaciones });
        }

        public JsonResult GetInventarioDisponible(IFormCollection fc)
        {
            // Separar los códigos por coma si vienen varios
            string idInput = fc["id"].ToString() ?? "";
            var codigos = idInput.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("id", codigos); // Npgsql detecta array automáticamente
            parameters.Add("cantidad", Convert.ToInt32(fc["cantidad"].ToString()));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = "SELECT cp.cve_prod AS codigo, cp.descr_prod AS descripcion, cp.udm AS unidadproducto, ct.codigo AS tarima, " +
                "   cn.ulocation, tp.cantidad, cun.descripcion AS unidadnombre, ct.id_tarima, csu.descripcion, ct.id_tarima, ca.descripcion almacen " +
                "FROM cattarimas ct " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id " +
                "LEFT JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima " +
                "LEFT JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id " +
                "LEFT JOIN catunidades cun ON cun.id_udm = tp.unidad " +
                "WHERE ca.tipo = 'Stock' AND csu.id_sucursal = @sucursal " +
                "   AND (cp.cve_prod = ANY(@id) OR cp.cve_prod IS NULL) " +
                "   AND tp.cantidad > 0";

            var inventario = RunQuery(query, parameters);

            if (inventario.Count() == 0)
            {
                query = "SELECT cp.cve_prod AS codigo, cp.descr_prod AS descripcion, cp.udm AS unidadproducto, ct.codigo AS tarima, " +
                "   cn.ulocation, tp.cantidad, cun.descripcion AS unidadnombre, ct.id_tarima, csu.descripcion, ct.id_tarima " +
                "FROM cattarimas ct " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id " +
                "LEFT JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima " +
                "LEFT JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id " +
                "LEFT JOIN catunidades cun ON cun.id_udm = tp.unidad " +
                "WHERE ca.tipo = 'Stock' AND csu.id_sucursal = @sucursal " +
                "   AND (cp.cve_prod = ANY(@id) OR cp.cve_prod IS NULL) ";

                inventario = RunQuery(query, parameters);
            }

            return Json(new { data = inventario });
        }

        public JsonResult GetRecepcionData(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("uuid", fc["uuid"].ToString());

            var returnResult = new Dictionary<string, object>();

            // Obtener documento actual
            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, COALESCE(em.coment_aut, '') AS coment_aut, em.usr1, em.id_encabezado, " +
                "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch) || '-' || em.fol_doc || " +
                "       CASE WHEN em.variacion > 0 " +
                "       THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   fch, em.uuid, em.suc, cs.descripcion, em.variacion, " +
                "   u0.nombre || ' ' || u0.apellido AS solicitante, cs0.descripcion AS sucursalsolicitante, cs0.id_sucursal AS idsucsolicitante " +
                "FROM encabezadomov em " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = em.suc " +
                "INNER JOIN usuarios u0 ON u0.usuarioid = em.usr0 " +
                "INNER JOIN catsucursales cs0 ON cs0.id_sucursal = u0.sucursal_id " +
                "WHERE em.uuid = @uuid";
            var ordenCompra = RunQuery(query, parameters)[0];
            returnResult.Add("ordenCompra", ordenCompra);

            query = "SELECT pd.cant_ud AS cantidad, pd.cve_prod AS codigo, pd.descr_prod AS descripcion, " +
                "   pd.id_partidas, pd.producto_id, pd.ud, udm.id_udm AS unidad, pd.variacion, pd.fol_doc_ant " +
                "FROM partidasdoc pd " +
                "LEFT JOIN catunidades udm ON udm.cve_udm = pd.ud " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id " +
                "WHERE em.uuid = @uuid";
            var partidas = RunQuery(query, parameters);
            returnResult.Add("partidas", partidas);

            query = "SELECT ct.codigo AS tarima, cn.ulocation, ct.id_tarima, csu.descripcion " +
                "FROM cattarimas ct " +
                "LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "LEFT JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "LEFT JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "LEFT JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id " +
                "WHERE ca.tipo = 'Stock' AND csu.id_sucursal = @sucursal";
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            var ubicaciones = RunQuery(query, parameters);
            returnResult.Add("ubicaciones", ubicaciones);

            return Json(returnResult);
        }
        #endregion

        #region Procesos
        [AuditAction(Modulo = "Traslados", Accion = "Genera documento de traslado entre sucursales")]
        public JsonResult SurtidoCompleto(IFormCollection fc)
        {
            try
            {

                if (GetInt(fc["sucursalOrigen"].ToString()) == GetInt(fc["sucursalDestino"].ToString()))
                {
                    return Json(new { icon = "error", html = "La sucursal origen no puede ser la misma que la sucursal destino", showCancelButton = false });
                }

                var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                var noSurtido = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["noSurtido"].ToString());
                string html = "Folio de transaccion generado: <br>";

                var parameters = new Dictionary<string, object>();
                string query = "SELECT em.usr0, em.fch0, em.suc, " +
                    "   em.folio " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                parameters.Add("id", Convert.ToInt32(fc["encabezado"].ToString()));
                var usrId = RunQuery(query, parameters)[0];

                parameters = new Dictionary<string, object>();
                query = "SELECT u.usuarioid FROM usuarios u WHERE u.sucursal_id = @sucursal AND u.rolid = 8 AND u.areaid = 6";
                parameters.Add("sucursal", Convert.ToInt32(fc["sucursalOrigen"].ToString()));
                var gerenteSucursal = RunScalar(query, parameters);

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 5,
                    IdTpDoc = 33,
                    Anio = DateTime.Now.Year,
                    Suc = GetInt(usrId["suc"]),
                    SucOrigen = Convert.ToInt32(fc["sucursalOrigen"].ToString()),
                    Fch = DateTime.Now,
                    TpMov = "TRAINV",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(usrId["usr0"]),
                    Fch0 = (DateTime)usrId["fch0"],
                    Usr1 = GetUserId(User.Identity.Name),
                    Fch1 = DateTime.Now,
                    Usr2 = Convert.ToInt32(gerenteSucursal),
                    EncabezadoPadre = Convert.ToInt32(fc["encabezado"].ToString()),
                    Estatus = 22,
                    UsrDep = GetAreaName(User.Identity.Name),
                    TipoPoceso = "Traslado",
                    Coment1 = fc["observaciones"].ToString(),
                    CliProv = "",
                };

                int nro = 1;
                var partidas = new List<PartidaDocumento>();

                foreach (var prod in products)
                {
                    if (Convert.ToInt32(prod["cantidad"]) == 0)
                    {
                        Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        return Json(new { icon = "error", text = $"No hay stock suficiente para {prod["descripcion"].ToString()} en esta sucursal", showCancelButton = false });
                    }

                    PartidaDocumento partida = new PartidaDocumento();
                    partida.NroPart = nro++;
                    partida.CveProd = prod["codigo"].ToString();
                    partida.DescrProd = prod["descripcion"].ToString();
                    partida.Ud = prod["ud"].ToString();
                    partida.CantUd = GetDecimal(prod["cantidad"]);
                    partida.FolDocAnt = "traslado";
                    partida.IdProducto = GetInt(prod["id_producto"]);

                    partidas.Add(partida);
                }

                var documento = new Dictionary<string, object>();
                var gerenteAlmacen = new Dictionary<string, object>();
                var urlBase = "";
                string path = "";
                string urlCotizacion = "";

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                            html += $"<strong>{documento["folio_generado"].ToString()}</strong> <br>";

                            if (noSurtido.Count() > 0)
                            {
                                partidas = new List<PartidaDocumento>();
                                foreach (var sur in noSurtido)
                                {
                                    PartidaDocumento partida = new PartidaDocumento();
                                    partida.NroPart = nro++;
                                    partida.CveProd = sur["codigo"].ToString();
                                    partida.DescrProd = sur["descripcion"].ToString();
                                    partida.Ud = sur["ud"].ToString();
                                    partida.CantUd = GetDecimal(sur["cantidadsolicitada"]);
                                    partida.FolDocAnt = "traslado";
                                    partida.IdProducto = GetInt(sur["id_producto"]);

                                    partidas.Add(partida);
                                }

                                parameters = new Dictionary<string, object>();
                                query = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb)";
                                parameters.Add("p_id_original", Convert.ToInt32(fc["encabezado"].ToString()));
                                parameters.Add("p_total", 0);
                                parameters.Add("p_observaciones", fc["observaciones"].ToString());
                                parameters.Add("p_usuario", GetUserId(User.Identity.Name));
                                parameters.Add("p_partidas", JsonConvert.SerializeObject(partidas));
                                var poClonada = RunQuery(query, parameters, false, conn, tx)[0];

                                html += $"<br>Variaciones del documento actual: <br>Anterior: <strong>{usrId["folio"].ToString()}</strong> <br>Actual: <strong>{poClonada["foliodoc"].ToString()}</strong>";
                            }

                            parameters = new Dictionary<string, object>();
                            query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @encabezado";
                            parameters.Add("encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
                            RunUpdate(query, parameters, false, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
                var request = HttpContext.Request;
                urlBase = $"{request.Scheme}://{request.Host}";
                //urlBase = Request.Url.GetLeftPart(UriPartial.Authority);
                path = "/Traslados/SolicitudesPendientes";
                urlCotizacion = $"{urlBase}{path}?id={documento["folio_generado"].ToString()}";

                parameters = new Dictionary<string, object>();
                query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombrecompleto, u.email, u.nombreusuario " +
                    "FROM usuarios u " +
                    "INNER JOIN areas a ON a.areaid = u.areaid " +
                    "WHERE a.nombre = 'Almacen' AND u.rolid = 8 AND u.sucursal_id = @sucursal";
                parameters.Add("sucursal", Convert.ToInt32(fc["sucursalOrigen"].ToString()));
                var res = RunQuery(query, parameters);

                if (res.Count() <= 0)
                {
                    return Json(new { icon = "success", html = "El documento se creó correctamente, pero no fue posible enviar la notificación al área de almacén de la sucursal seleccionada porque no hay un correo configurado en el sistema.<br><br>Puede enviar el correo manualmente o contactar con soporte.<br>" + html, title = "Material solicitado", showCancelButton = false });
                }

                _ = SendNotificationInterno(gerenteAlmacen["nombreusuario"].ToString(), gerenteAlmacen["email"].ToString(), new
                {
                    icon = "info",
                    title = "Solicitud de inventario",
                    message = $"El usuario {gerenteAlmacen["nombrecompleto"]} está solicitando inventario en el documento con el folio {documento["folio_generado"].ToString()}.",
                    buttons = new[]
                        {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                    folio = documento["folio_generado"].ToString(),
                });

                return Json(new { icon = "success", html = html, title = "Material solicitado", showCancelButton = false });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Traslados/Solicitudes");
                return Json(new { icon = "error", html = "Ocurrio un error inesperado, vuelve a intentarlo y si el problema persiste contacta con soporte.", showCancelButton = false });
            }
        }

        [AuditAction(Modulo = "Traslados", Accion = "Genera documento de envio de material - el material tuvo que haber salido de sucursal origen")]
        public JsonResult GenerarEnvio(IFormCollection fc)
        {
            var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
            var noSurtido = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["noSurtido"].ToString());
            var parameters = new Dictionary<string, object>();
            string documentosGenerados = "<ul>";

            string query = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.suc " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
            parameters.Add("id", Convert.ToInt32(fc["encabezado"].ToString()));
            var usrId = RunQuery(query, parameters)[0];

            parameters = new Dictionary<string, object>();
            query = "SELECT ct.id_tarima FROM catalmacenes c " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                "WHERE cs.cve_sucursal = 'CEDIS' AND c.tipo = 'Transito'";
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            int transito = Convert.ToInt32(RunScalar(query, parameters));

            parameters = new Dictionary<string, object>();
            query = "SELECT u.usuarioid FROM usuarios u WHERE u.sucursal_id = @sucursal AND u.rolid = 8 AND u.areaid = 6";
            parameters.Add("sucursal", Convert.ToInt32(fc["sucursalOrigen"].ToString()));
            var gerenteSucursal = RunScalar(query, parameters);

            var encabezado = new DocumentoEncabezado
            {
                EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                IdArea = 5,
                IdTpDoc = 40,
                Anio = DateTime.Now.Year,
                Suc = GetInt(usrId["suc"]),
                SucOrigen = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                Fch = DateTime.Now,
                TpMov = "ENVINV",
                UsrDoc = User.Identity.Name,
                FchCap = DateTime.Now,
                Usr0 = Convert.ToInt32(usrId["usr0"]),
                Fch0 = (DateTime)usrId["fch0"],
                Usr1 = Convert.ToInt32(usrId["usr1"]),
                Fch1 = (DateTime)usrId["fch1"],
                Usr2 = GetUserId(User.Identity.Name),
                Fch2 = DateTime.Now,
                Firma2 = fc["firma2"].ToString(),
                Usr3 = Convert.ToInt32(gerenteSucursal),
                EncabezadoPadre = Convert.ToInt32(fc["encabezado"].ToString()),
                Estatus = 23,
                UsrDep = GetAreaName(User.Identity.Name),
                TipoPoceso = "envio",
                Coment1 = fc["observaciones"].ToString(),
                CliProv = "",
            };

            int nro = 1;
            var partidas = new List<PartidaDocumento>();

            foreach (var prod in products)
            {
                if (Convert.ToInt32(prod["cantidad"]) == 0)
                {
                    Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Json(new { icon = "error", text = $"Debes indicar la cantidad del producto {prod["descripcion"].ToString()}", showCancelButton = false });
                }

                partidas.Add(new PartidaDocumento
                {
                    NroPart = nro++,
                    CveProd = prod["codigo"].ToString(),
                    DescrProd = prod["descripcion"].ToString(),
                    Ud = prod["ud"].ToString(),
                    CantUd = Convert.ToDecimal(prod["cantidad"]),
                    FolDocAnt = "traslado",
                    IdProducto = Convert.ToInt32(prod["id_producto"]),
                });
            }

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
                        documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                        documentosGenerados += $"<li>{documento["folio_generado"]}</li>";

                        var productosConTarimas = products.SelectMany(p =>
                        {
                            var lista = new List<(Dictionary<string, object> Producto, int IdTarima, int Cantidad)>();

                            if (p["tarima"] is JObject tarimaObj)
                            {
                                var tarimas = tarimaObj.ToObject<Dictionary<string, int>>();
                                foreach (var kv in tarimas)
                                {
                                    lista.Add((p, Convert.ToInt32(kv.Key), kv.Value));
                                }
                            }

                            return lista;
                        }).ToList();

                        // Agrupar por tarima (origen)
                        var productosPorDestino = productosConTarimas
                            .GroupBy(x => x.IdTarima)
                            .ToList();

                        foreach (var grupo in productosPorDestino)
                        {
                            int origen = grupo.Key;

                            // Clonar productos pero con la cantidad correcta de esa tarima
                            var listaProductos = grupo.Select(x =>
                            {
                                var prod = new Dictionary<string, object>(x.Producto);
                                prod["cantidad"] = x.Cantidad; // cantidad de esa tarima
                                prod["tarima"] = x.IdTarima;   // tarima de origen
                                return prod;
                            }).ToList();

                            RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name),
                                "traslado", origen, transito,
                                "Traslado de inventario entre sucursales",
                                Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);
                        }

                        if (noSurtido.Count() > 0)
                        {
                            partidas = new List<PartidaDocumento>();
                            foreach (var sur in noSurtido)
                            {
                                partidas.Add(new PartidaDocumento
                                {
                                    NroPart = nro++,
                                    CveProd = sur["codigo"].ToString(),
                                    DescrProd = sur["descripcion"].ToString(),
                                    Ud = sur["ud"].ToString(),
                                    CantUd = Convert.ToDecimal(sur["cantidad_pendiente"]),
                                    FolDocAnt = "traslado",
                                    IdProducto = Convert.ToInt32(sur["id_producto"]),
                                });
                            }

                            parameters = new Dictionary<string, object>();
                            query = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb)";
                            parameters.Add("p_id_original", Convert.ToInt32(fc["encabezado"].ToString()));
                            parameters.Add("p_total", 0);
                            parameters.Add("p_observaciones", fc["observaciones"].ToString());
                            parameters.Add("p_usuario", GetUserId(User.Identity.Name));
                            parameters.Add("p_partidas", JsonConvert.SerializeObject(partidas));
                            var poClonada = RunQuery(query, parameters, false, conn, tx)[0];
                            documentosGenerados += $"<li>{poClonada["foliodoc"]}</li>";
                        }

                        parameters = new Dictionary<string, object>();
                        query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @encabezado";
                        parameters.Add("encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
                        RunUpdate(query, parameters, false, conn, tx);

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
            var request = HttpContext.Request;

            var urlBase = $"{request.Scheme}://{request.Host}";
            //var urlBase = Request.Url.GetLeftPart(UriPartial.Authority);
            string path = "/Traslados/SolicitudesPendientes";
            string urlCotizacion = $"{urlBase}{path}?id={documento["folio_generado"].ToString()}";

            parameters = new Dictionary<string, object>();
            query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombrecompleto, u.email, u.nombreusuario " +
                "FROM usuarios u " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE u.usuarioid = @id";
            parameters = new Dictionary<string, object>();
            parameters.Add("id", Convert.ToInt32(usrId["usr0"]));
            var gerenteAlmacen = RunQuery(query, parameters)[0];

            query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombrecompleto, u.email, u.nombreusuario " +
                "FROM usuarios u " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE u.usuarioid = @id";
            parameters = new Dictionary<string, object>();
            parameters.Add("id", GetUserId(User.Identity.Name));
            var usuarioActual = RunQuery(query, parameters)[0];

            _ = SendNotificationInterno(gerenteAlmacen["nombreusuario"].ToString(), gerenteAlmacen["email"].ToString(), new
            {
                icon = "info",
                title = "Material enviado",
                message = $"El usuario {usuarioActual["nombrecompleto"]} envio el material solicitado en el formato de transaccion de inventario, por favor validalo cuando llegue a tu sucursal.",
                buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                timer = 0,
                folio = documento["folio_generado"].ToString(),
            });

            return Json(new { icon = "success", html = documentosGenerados });
        }
        #endregion
        #endregion

        #region Recepcion de inventario
        #region Obtener Datos
        #endregion

        #region Procesos
        [AuditAction(Modulo = "Traslados", Accion = "Se confirma la recepcion del material y se crea el documento de recepcion")]
        public JsonResult RecepcionInventario(IFormCollection fc)
        {
            try
            {
                var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                var discrepancias = products.Where(p => p.ContainsKey("status") && !Convert.ToBoolean(p["status"])).ToList();
                var aceptados = products.Where(p => p.ContainsKey("status") && Convert.ToBoolean(p["status"])).ToList();
                var docsGenerados = new List<string>();
                string listaHtml = "";

                if (aceptados.Count > 0)
                {
                    var parameters = new Dictionary<string, object>();
                    string query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.cve_sucursal = 'CEDIS' AND c.tipo = 'Transito'";
                    parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                    int transito = Convert.ToInt32(RunScalar(query, parameters));
                    //falta obtener el stock del almacen de trancito de la sucursal origen para transferirlo a las
                    //ubicaciones seleccionadas

                    query = "SELECT fch0, fch1, fch2, firma2, " +
                         "  usr0, usr1, usr2 " +
                         "FROM encabezadomov " +
                         "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    if (string.IsNullOrEmpty(fc["firma3"].ToString()))
                    {
                        Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        return Json(new { icon = "error", title = "No se registro una firma", html = "Es necesario firmar el documento para confirmar la recepcion" });
                    }

                    var encabezado = new DocumentoEncabezado
                    {
                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        IdArea = 5,
                        IdTpDoc = 26,
                        Anio = DateTime.Now.Year,
                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                        Fch = DateTime.Now,
                        TpMov = "RINV",
                        UsrDoc = User.Identity.Name,
                        FchCap = DateTime.Now,
                        Usr0 = GetInt(usr["usr0"]),
                        Fch0 = GetDate(usr["fch0"]),
                        Usr1 = GetInt(usr["usr1"]),
                        Fch1 = GetDate(usr["fch1"]),
                        Firma2 = GetString(usr["firma2"]),
                        Usr2 = GetInt(usr["usr2"]),
                        Fch2 = GetDate(usr["fch2"]),
                        Usr3 = GetUserId(User.Identity.Name),
                        Fch3 = DateTime.Now,
                        Firma3 = fc["firma3"].ToString(),
                        CliProv = "",
                        EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString()),
                        Estatus = 11,
                        UsrDep = GetAreaName(User.Identity.Name),
                        Coment1 = GetString(fc["comentario"].ToString()),
                    };

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();

                    foreach (var disc in aceptados)
                    {
                        if (Convert.ToInt32(disc["cantidadCorrecta"]) == 0)
                        {
                            continue;
                        }

                        partidas.Add(new PartidaDocumento
                        {
                            NroPart = nro++,
                            CveProd = disc["codigo"].ToString(),
                            DescrProd = disc["descripcion"].ToString(),
                            CantUd = Convert.ToUInt32(disc["cantidadCorrecta"]),
                            Ud = disc["cve_unidad"].ToString(),
                            IdProducto = Convert.ToInt32(disc["id_producto"]),
                        });
                    }

                    var documento = GenerarDocumentoConPartidas(encabezado, partidas);
                    docsGenerados.Add(documento["folio_generado"].ToString());

                    var productosPorDestino = aceptados.Where(p => p.ContainsKey("destino")).GroupBy(p => Convert.ToInt32(p["destino"])).ToList();

                    string ubicaciones = "<ul>";
                    foreach (var grupo in productosPorDestino)
                    {
                        parameters = new Dictionary<string, object>();
                        int destino = grupo.Key;
                        var listaProductos = grupo.ToList();

                        RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name), "recepcion", transito, destino, "Recepcion de inventario entre sucursales", null, null);

                        query = "SELECT cn.ulocation " +
                            "FROM cattarimas ct " +
                            "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                            "WHERE ct.id_tarima = @destino";
                        parameters.Add("destino", destino);
                        var des = RunScalar(query, parameters);
                        ubicaciones += $"<li> {des.ToString()} </li>";
                    }

                    parameters = new Dictionary<string, object>();
                    query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @encabezado";
                    parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    RunUpdate(query, parameters);

                    listaHtml += $"Se traslado el material seleccionado desde Recepcion a la ubicacion <br> {ubicaciones} </ul>";
                }

                if (discrepancias.Count > 0)
                {
                    var parameters = new Dictionary<string, object>();
                    string query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Cuarentena'";
                    parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                    int cuarentena = Convert.ToInt32(RunScalar(query, parameters));

                    if (cuarentena == 0 || cuarentena == null)
                    {
                        return Json(new { icon = "error", title = "Ocurrio un error con la sucursla", html = "La sucursal a la que perteneces no cuenta con almacen de cuarentena, favor de contactar a soporte" });
                    }

                    query = "SELECT fch0, fch1, fch2, fch3, fch4, fch5, fch6, firma1, firma6, " +
                        "   usr0, usr1, usr2, usr3, usr4, usr5, usr6, firma2 " +
                        "FROM encabezadomov " +
                        "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    string motivo = "Recepcion parcial";
                    var encabezado = new DocumentoEncabezado
                    {
                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        IdArea = 5,
                        IdTpDoc = 36,
                        Anio = DateTime.Now.Year,
                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                        Fch = DateTime.Now,
                        TpMov = "DISINV",
                        UsrDoc = User.Identity.Name,
                        FchCap = DateTime.Now,
                        Usr0 = GetInt(usr["usr0"]),
                        Fch0 = GetDate(usr["fch0"]),
                        Usr1 = GetInt(usr["usr1"]),
                        Fch1 = GetDate(usr["fch1"]),
                        Firma2 = GetString(usr["firma2"]),
                        Usr2 = GetInt(usr["usr2"]),
                        Fch2 = GetDate(usr["fch2"]),
                        Usr3 = GetUserId(User.Identity.Name),
                        Fch3 = DateTime.Now,
                        Firma3 = fc["firma3"].ToString(),
                        CliProv = "",
                        EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString()),
                        Estatus = 25,
                        UsrDep = GetAreaName(User.Identity.Name),
                        Coment1 = GetString(fc["comentario"].ToString()),
                    };

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();

                    foreach (var disc in discrepancias)
                    {
                        if (Convert.ToInt32(disc["cantidadError"]) == 0)
                        {
                            continue;
                        }

                        motivo = string.IsNullOrEmpty(disc["motivo"]?.ToString()) ? null : disc["motivo"].ToString();
                        partidas.Add(new PartidaDocumento
                        {
                            NroPart = nro++,
                            CveProd = disc["codigo"].ToString(),
                            DescrProd = disc["descripcion"].ToString(),
                            CantUd = Convert.ToUInt32(disc["cantidadError"]),
                            Ud = disc["cve_unidad"].ToString(),
                            IdProducto = Convert.ToInt32(disc["id_producto"]),
                            FolDocAnt = GetString(disc["motivo"]),
                        });
                    }

                    var documento = GenerarDocumentoConPartidas(encabezado, partidas);
                    docsGenerados.Add(documento["folio_generado"].ToString());
                    RegistrarMovimiento(discrepancias, GetUserId(User.Identity.Name), "Ingreso", null, cuarentena, motivo, Convert.ToInt32(documento["IdEncabezado"]), null);

                    listaHtml = "Folios generados <br><ul style='text-align:left'>";
                    foreach (var folio in docsGenerados)
                    {
                        // El formato es INV-RINVD-2025-0000001
                        var partes = folio.Split('-');
                        var tipo = partes.Length > 1 ? partes[1] : "";

                        if (tipo == "RINVP")
                            listaHtml += $"<li><b>Discrepancia:</b> {folio}</li>";
                        else if (tipo == "RINV")
                            listaHtml += $"<li><b>Recepción stock:</b> {folio}</li>";
                        else if (tipo == "OC")
                            listaHtml += $"<li><b>Variacion de la orden de compra con partidas pendientes:</b> {folio}</li>";
                        else if (tipo == "GTO")
                            listaHtml += $"<li><b>Variacion de la solicitud de gasto con partidas pendientes:</b> {folio}</li>";
                        else
                            listaHtml += $"<li>{folio}</li>";
                    }
                    listaHtml += "</ul>";
                }

                return Json(new { icon = "success", title = "Material ingresado", showCancelButton = false, html = listaHtml });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Traslados/Cuarentenas");
                return Json(new { icon = "error", text = "Ocurrio un error inesperado, revise los datos que se estan mandando o intentelo de nuevo.", showCancelButton = false });
            }
        }
        #endregion
        #endregion

        #region Procesar cuarentenas
        #region Obtener Datos
        #endregion

        #region Procesos
        [AuditAction(Modulo = "Traslados", Accion = "Procesamiento de cuarentena, se reingresa a almacen o se saca como donacion")]
        public JsonResult ProcesarCuarentena(IFormCollection fc)
        {
            try
            {
                var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                var discrepancias = products.Where(p => p.ContainsKey("status") && !Convert.ToBoolean(p["status"])).ToList();
                var aceptados = products.Where(p => p.ContainsKey("status") && Convert.ToBoolean(p["status"])).ToList();
                var parameters = new Dictionary<string, object>();
                var docsGenerados = new List<string>();
                string html = "<ul>";

                if (aceptados.Count() > 0)
                {
                    var sinStock = new List<Dictionary<string, object>>();
                    string query = "";

                    foreach (var prod in aceptados)
                    {
                        int productoId = Convert.ToInt32(prod["id_producto"]);
                        int cantidadSolicitada = Convert.ToInt32(prod["cantidad"]);

                        // Traemos el stock real disponible en cuarentena
                        query = @"
                            SELECT COALESCE(SUM(tp.cantidad), 0) 
                            FROM tarima_productos tp
                            INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                            INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                            INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                            INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                            INNER JOIN catalmacenes c ON c.id_almacen = cr.almacen_id
                            INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id
                            WHERE cs.id_sucursal = @sucursal
                            AND c.tipo = 'Cuarentena'
                            AND tp.producto_id = @productoId";

                        parameters = new Dictionary<string, object>
                        {
                            { "sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                            { "productoId", productoId }
                        };

                        int stockDisponible = Convert.ToInt32(RunScalar(query, parameters));

                        if (stockDisponible < cantidadSolicitada)
                        {
                            // Añadimos la info del producto y stock disponible
                            prod.Add("stockDisponible", stockDisponible);
                            sinStock.Add(prod);
                        }
                    }

                    if (sinStock.Any())
                    {
                        return Json(new
                        {
                            icon = "error",
                            title = "Algunos productos no tienen stock suficiente",
                            productos = sinStock
                        });
                    }

                    parameters = new Dictionary<string, object>();
                    query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Cuarentena'";
                    parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                    int origen = Convert.ToInt32(RunScalar(query, parameters));

                    query = "SELECT fch0, fch1, fch2, fch3, fch4, fch5, fch6, firma1, firma6, " +
                            "   usr0, usr1, usr2, usr3, usr4, usr5, usr6, firma2, firma3 " +
                            "FROM encabezadomov " +
                            "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    string motivo = "recepcion";
                    var encabezado = new DocumentoEncabezado
                    {
                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        IdArea = 5,
                        IdTpDoc = 26,
                        Anio = DateTime.Now.Year,
                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                        Fch = DateTime.Now,
                        TpMov = "RINV",
                        UsrDoc = User.Identity.Name,
                        FchCap = DateTime.Now,
                        Usr0 = GetInt(usr["usr0"]),
                        Fch0 = GetDate(usr["fch0"]),
                        Usr1 = GetInt(usr["usr1"]),
                        Fch1 = GetDate(usr["fch1"]),
                        Firma1 = GetString(usr["firma1"]),
                        Usr2 = GetInt(usr["usr2"]),
                        Fch2 = GetDate(usr["fch2"]),
                        Firma2 = GetString(usr["firma2"]),
                        Usr3 = GetInt(usr["usr3"]),
                        Fch3 = GetDate(usr["fch3"]),
                        Firma3 = GetString(usr["firma3"]),
                        EncabezadoPadre = Convert.ToInt32(fc["encabezado"].ToString()),
                        Estatus = 11,
                        UsrDep = GetAreaName(User.Identity.Name),
                        TipoPoceso = "Recepcion",
                        Coment1 = fc["comentario"].ToString(),
                        CliProv = "srs",
                    };

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();

                    foreach (var disc in aceptados)
                    {
                        motivo = "ingreso";
                        if (Convert.ToInt32(disc["cantidad_original"]) > 0)
                        {
                            partidas.Add(new PartidaDocumento
                            {
                                NroPart = nro++,
                                CveProd = disc["codigo"].ToString(),
                                DescrProd = disc["descripcion"].ToString(),
                                CantUd = Convert.ToUInt32(disc["cantidad_original"]),
                                Ud = disc["cve_unidad"].ToString(),
                                FolDocAnt = string.IsNullOrEmpty(disc["motivo"]?.ToString()) ? null : disc["motivo"].ToString(),
                                IdProducto = GetInt(disc["id_producto"]),
                            });
                        }
                    }

                    // Revisar si la cantidad fue cambiara para crear el documento clon con las diferencias de cantidad
                    var variaciones = new List<Dictionary<string, object>>();
                    var documento = new Dictionary<string, object>();

                    foreach (var prod in aceptados)
                    {
                        int cantidadOriginal = Convert.ToInt32(prod["cantidad_original"]);
                        int cantidadRecibida = Convert.ToInt32(prod["cantidad"]);
                        int diferencia = cantidadRecibida - cantidadOriginal;

                        if (diferencia != 0) // hay exceso o faltante
                        {
                            var clon = new Dictionary<string, object>(prod);
                            clon["diferencia"] = diferencia;
                            clon["cantidad"] = cantidadRecibida; // la variación en positivo
                            clon["tipo_variacion"] = diferencia > 0 ? "EXCESO" : "FALTANTE";
                            variaciones.Add(clon);
                        }
                    }

                    // Crear el documento clon del original
                    if (variaciones.Any())
                    {
                        var partidasVariacion = new List<PartidaDocumento>();
                        nro = 1;

                        foreach (var v in variaciones)
                        {
                            parameters = new Dictionary<string, object>();
                            query = "UPDATE tarima_productos SET cantidad = cantidad + (@diferencia) WHERE producto_id = @producto AND tarima_id = @ubicacion";
                            parameters.Add("diferencia", Convert.ToDecimal(v["diferencia"]));
                            parameters.Add("producto", Convert.ToInt32(v["id_producto"]));
                            parameters.Add("ubicacion", origen);
                            RunUpdate(query, parameters);

                            partidasVariacion.Add(new PartidaDocumento
                            {
                                NroPart = nro++,
                                CveProd = v["codigo"].ToString(),
                                DescrProd = v["descripcion"].ToString(),
                                CantUd = Convert.ToDecimal(v["cantidad"]), // la diferencia
                                Ud = v["cve_unidad"].ToString(),
                                FolDocAnt = "VARIACION-" + v["tipo_variacion"],
                                IdProducto = GetInt(v["id_producto"]),
                            });
                        }

                        documento = GenerarDocumentoConPartidas(encabezado, partidas);
                        docsGenerados.Add(documento["folio_generado"].ToString());

                        //encabezado.EncabezadoPadre = Convert.ToInt32(documento["IdEncabezado"]);
                        //var documentoClon = GenerarDocumentoConPartidas(encabezado, partidasVariacion);

                        // Crear una OC con las partidas aceptadas
                        parameters = new Dictionary<string, object>();
                        query = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb)";
                        parameters.Add("p_id_original", Convert.ToInt32(documento["IdEncabezado"]));
                        parameters.Add("p_total", 0);
                        parameters.Add("p_observaciones", fc["comentario"].ToString());
                        parameters.Add("p_usuario", GetUserId(User.Identity.Name));
                        parameters.Add("p_partidas", JsonConvert.SerializeObject(partidasVariacion));
                        var documentoClon = RunQuery(query, parameters)[0];
                        docsGenerados.Add(documentoClon["foliodoc"].ToString());

                        parameters = new Dictionary<string, object>();
                        query = "UPDATE encabezadomov SET fol_doc = 'E' || fol_doc, variacion = 0, fecha_edicion = null," +
                            "   editado_por = null, variacion_padre = null " +
                            "WHERE id_encabezado = @id";
                        parameters.Add("id", Convert.ToInt32(documentoClon["idencabezado"]));
                        RunUpdate(query, parameters);
                        query = "SELECT gen || '-' || nat || '-' || fol_doc AS folio FROM encabezadomov " +
                            "WHERE id_encabezado = @id";
                        var folioClon = RunScalar(query, parameters);
                        docsGenerados.Add(folioClon.ToString());
                    }
                    else
                    {
                        documento = GenerarDocumentoConPartidas(encabezado, partidas);
                        docsGenerados.Add(documento["folio_generado"].ToString());
                    }

                    var productosPorDestino = aceptados.Where(p => p.ContainsKey("destino")).GroupBy(p => Convert.ToInt32(p["destino"])).ToList();

                    foreach (var grupo in productosPorDestino)
                    {
                        if (grupo.Any(p => Convert.ToInt32(p["cantidad"]) > 0))
                        {
                            int destino = grupo.Key;
                            var listaProductos = grupo.ToList();

                            RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name), "Movimiento", origen, destino, motivo, Convert.ToInt32(documento["IdEncabezado"]), null);
                        }
                    }

                    // Actualizar la descripcion del producto
                    foreach (var a in aceptados)
                    {
                        parameters = new Dictionary<string, object>();
                        query = "SELECT * FROM catproductos c WHERE c.cve_prod = @codigo AND empresa_id = @empresa_id";
                        parameters.Add("codigo", a["codigo"]);
                        parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        var prod = RunQuery(query, parameters)[0];

                        if (prod["descr_prod"].ToString() != a["descripcion"].ToString())
                        {
                            parameters = new Dictionary<string, object>();
                            query = "UPDATE catproductos SET descr_prod = @descripcion WHERE cve_prod = @codigo AND empresa_id = @empresa_id";
                            parameters.Add("descripcion", a["descripcion"].ToString());
                            parameters.Add("codigo", a["codigo"]);
                            RunUpdate(query, parameters);
                        }
                    }
                }

                if (discrepancias.Count() > 0)
                {
                    var sinStock = new List<Dictionary<string, object>>();
                    string query = "";

                    foreach (var prod in discrepancias)
                    {
                        int productoId = Convert.ToInt32(prod["id_producto"]);
                        int cantidadSolicitada = Convert.ToInt32(prod["cantidad"]);

                        // Traemos el stock real disponible en cuarentena
                        query = @"
                            SELECT COALESCE(SUM(tp.cantidad), 0) 
                            FROM tarima_productos tp
                            INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                            INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                            INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                            INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                            INNER JOIN catalmacenes c ON c.id_almacen = cr.almacen_id
                            INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id
                            WHERE cs.id_sucursal = @sucursal
                            AND c.tipo = 'Cuarentena'
                            AND tp.producto_id = @productoId";

                        parameters = new Dictionary<string, object>
                        {
                            { "sucursal",Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                            { "productoId", productoId }
                        };

                        int stockDisponible = Convert.ToInt32(RunScalar(query, parameters));

                        if (stockDisponible < cantidadSolicitada)
                        {
                            // Añadimos la info del producto y stock disponible
                            prod.Add("stockDisponible", stockDisponible);
                            sinStock.Add(prod);
                        }
                    }

                    if (sinStock.Any())
                    {
                        return Json(new
                        {
                            icon = "error",
                            title = "Algunos productos no tienen stock suficiente",
                            productos = sinStock
                        });
                    }

                    parameters = new Dictionary<string, object>();
                    query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Cuarentena'";
                    parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                    int cuarentena = Convert.ToInt32(RunScalar(query, parameters));

                    query = "SELECT fch0, fch1, fch2, fch3, fch4, fch5, fch6, firma1, firma6, " +
                            "   usr0, usr1, usr2, usr3, usr4, usr5, usr6, firma2, firma3 " +
                            "FROM encabezadomov " +
                            "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    string motivo = "recepcion";

                    var encabezado = new DocumentoEncabezado
                    {
                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        IdArea = 5,
                        IdTpDoc = 39,
                        Anio = DateTime.Now.Year,
                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                        Fch = DateTime.Now,
                        TpMov = "DONINV",
                        UsrDoc = User.Identity.Name,
                        FchCap = DateTime.Now,
                        Usr0 = Convert.ToInt32(usr["usr0"]),
                        Fch0 = (DateTime)usr["fch0"],
                        Usr1 = Convert.ToInt32(usr["usr1"]),
                        Fch1 = (DateTime)usr["fch1"],
                        Usr2 = Convert.ToInt32(usr["usr2"]),
                        Fch2 = (DateTime)usr["fch2"],
                        Firma2 = GetString(usr["firma2"]),
                        Usr3 = Convert.ToInt32(usr["usr3"]),
                        Fch3 = (DateTime)usr["fch3"],
                        Firma3 = GetString(usr["firma3"]),
                        EncabezadoPadre = Convert.ToInt32(fc["encabezado"].ToString()),
                        Estatus = 11,
                        UsrDep = GetAreaName(User.Identity.Name),
                        TipoPoceso = "Recepcion",
                        Coment1 = fc["comentario"].ToString(),
                        CliProv = "srs",
                    };

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();

                    foreach (var disc in discrepancias)
                    {
                        motivo = "ingreso";
                        partidas.Add(new PartidaDocumento
                        {
                            NroPart = nro++,
                            CveProd = disc["codigo"].ToString(),
                            DescrProd = disc["descripcion"].ToString(),
                            CantUd = Convert.ToUInt32(disc["cantidad"]),
                            Ud = disc["cve_unidad"].ToString(),
                            FolDocAnt = string.IsNullOrEmpty(disc["motivo"]?.ToString()) ? null : disc["motivo"].ToString(),
                            IdProducto = GetInt(disc["id_producto"])
                        });
                    }

                    var documento = GenerarDocumentoConPartidas(encabezado, partidas);
                    docsGenerados.Add(documento["folio_generado"].ToString());

                    var productosPorDestino = discrepancias.Where(p => p.ContainsKey("destino")).GroupBy(p => Convert.ToInt32(p["destino"])).ToList();

                    foreach (var grupo in productosPorDestino)
                    {
                        int destino = grupo.Key;
                        var listaProductos = grupo.ToList();

                        RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name), "Movimiento", cuarentena, null, motivo, Convert.ToInt32(documento["IdEncabezado"]), null);
                    }

                }

                foreach (var doc in docsGenerados)
                {
                    html += $"<li>{doc}</li>";
                }

                html += "</ul>";
                return Json(new { icon = "success", title = "Cuarentena procesada exitosamente", html = $"Se crearon correctamente los folios: {html}" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error al procesar la cuarentena", html = $"{ex.Message}", showCancelButton = false, stock = false });
            }
        }
        #endregion
        #endregion

        #region Sincronizar Inventario Klepler
        public JsonResult GetAmacenesKepler()
        {
            string query = "SELECT k.c1 cve_sucursal, k.c2 cve_almacen, k.c3 descripcion_almacem, k2.c2 nombre_sucursal " +
                "FROM kdiq k " +
                "LEFT JOIN kdms k2 ON k2.c1 = k.c1 " +
                "WHERE  k2.c1 = '02' AND k.c2 = '95' ";
            var almacen = RunQuery(query, new Dictionary<string, object>(), false, null, null, "SRS");
            return Json(almacen);
        }

        public JsonResult GetProductosAlmacen(string id_almacen, string nombre, string sortColumn, string sortDir, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("nombre", nombre ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("almacen", Convert.ToInt32(id_almacen));
            string where = "";

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where = " AND (k.c1 ILIKE '%' || @nombre || '%' " +
                    "OR k2.c2 ILIKE '%' || @nombre || '%' " +
                    "OR k3.c3 ILIKE '%' || @nombre || '%' " +
                    "OR k4.c1 ILIKE '%' || @nombre || '%' " +
                    "OR k4.c2 ILIKE '%' || @nombre || '%' " +
                    "OR kc.c2 ILIKE '%' || @nombre || '%' " +
                    "OR k4.c11 ILIKE '%' || @nombre || '%') ";
            }

            var allowedColumns = new HashSet<string> {
                "clave_sucursal", "sucursal",
                "clave_almacen", "almacen", "cantidad",
                "codigo", "descripcion", "unidad"
            };

            if (!allowedColumns.Contains(sortColumn))
                sortColumn = "cantidad";

            string query = "SELECT k.c1 clave_sucursal, k2.c2 sucursal, k.c2 clave_almacen, k3.c3 almacen, COALESCE(SUM(k.c8 - k.c9), 0) cantidad, " +
                "   k4.c1 codigo, k4.c2 descripcion, k4.c11 unidad, k4.c115 peso, kc.c2 fraccion " +
                "FROM kdil k " +
                "LEFT JOIN kdms k2 ON k2.c1 = k.c1 " +
                "LEFT JOIN kdiq k3 ON k3.c2 = k.c2 AND k2.c1 = k3.c1 " +
                "INNER JOIN kdii k4 ON k4.c1 = k.c3 " +
                "LEFT JOIN kdfe33cefracaraprod kc ON kc.c1 = k4.c1 " +
                $"WHERE k2.c1 = '02' AND k.c2 = @almacen {where} " +
                "GROUP BY k.c1, k.c2, k.c3, k2.c2, k3.c3, k4.c2, k4.c11, k4.c1, k4.c115, kc.c2 " +
                "HAVING COALESCE(SUM(k.c8 - k.c9), 0) > 0 " +
                $"ORDER BY {sortColumn} {sortDir}, codigo ASC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var stock = RunQuery(query, parameters, false, null, null, "SRS");

            query = "SELECT COUNT(*) " +
                "FROM ( " +
                "   SELECT 1 " +
                "   FROM kdil k " +
                "   LEFT JOIN kdms k2 ON k2.c1 = k.c1 " +
                "   LEFT JOIN kdiq k3 ON k3.c2 = k.c2 AND k2.c1 = k3.c1 " +
                "   INNER JOIN kdii k4 ON k4.c1 = k.c3 " +
                "   WHERE k2.c1 = '02' AND k.c2 = @almacen " +
                "   GROUP BY k.c1, k.c2, k.c3, k2.c2, k3.c3, k4.c2, k4.c11, k4.c1 " +
                "   HAVING COALESCE(SUM(k.c8 - k.c9), 0) > 0" +
                ") t;";
            int total = Convert.ToInt32(RunScalar(query, parameters, false, null, null, "SRS"));

            return Json(new { data = stock, total });
        }

        public JsonResult SincronizarAlmacenes(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                List<Dictionary<string, object>> productosFiltrados = new List<Dictionary<string, object>>();
                string almacen = fc["id_almacen"].ToString();
                string almacenInterno = almacen == "70" ? "Modula" : almacen == "10" ? "Stock" : "Temporal";
                string productosJson = fc["productos"].ToString();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(productosJson);
                List<PartidaDocumento> partidas = new List<PartidaDocumento>();

                string query = "SELECT ct.id_tarima " +
                    "FROM cattarimas ct " +
                    "LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                    "LEFT JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                    "LEFT JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                    "LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                    "LEFT JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id " +
                    "WHERE ca.tipo = @almacen AND csu.id_sucursal = @sucursal";
                parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                parameters.Add("almacen", almacenInterno);
                int? ubicaciones = GetInt(RunScalar(query, parameters));

                if (ubicaciones == null && ubicaciones <= 0)
                {
                    throw new Exception("No se encontro la ubicacion del almacen.");
                }

                int nro = 0;
                foreach (var p in productos)
                {
                    Dictionary<string, object> prod = new Dictionary<string, object>();
                    PartidaDocumento partida = new PartidaDocumento();

                    query = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @codigo AND empresa_id = @empresa";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("codigo", p["clave"]);
                    parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
                    int idProducto = Convert.ToInt32(RunScalar(query, parameters));

                    query = "SELECT id_udm FROM catunidades WHERE cve_udm = @udm";
                    parameters.Add("udm", p["unidad"]);
                    int idUdm = Convert.ToInt32(RunScalar(query, parameters));

                    prod.Add("id_producto", idProducto);
                    prod.Add("codigo", p["clave"]);
                    prod.Add("descripcion", p["descripcion"]);
                    prod.Add("cantidad", GetDecimal(p["cantidad"]));
                    prod.Add("unidad", idUdm);
                    productosFiltrados.Add(prod);

                    partida.NroPart = nro++;
                    partida.CveProd = p["clave"].ToString();
                    partida.DescrProd = p["descripcion"].ToString();
                    partida.CantUd = GetDecimal(p["cantidad"]);
                    partida.Ud = p["unidad"].ToString();
                    partida.FolDocAnt = "traslado_inventario";
                    partida.IdProducto = idProducto;

                    partidas.Add(partida);
                }

                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 6;
                encabezado.IdTpDoc = 33;
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "TRAINV";
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.Estatus = 11;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.TipoPoceso = "traspaso_inventario";
                encabezado.CliProv = "srs";

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
                            documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                            RegistrarMovimiento(productosFiltrados, GetUserId(User.Identity.Name), "Ingreso", null, ubicaciones, "Traspaso de almacen desde kepler", Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);

                            string connString = utils._configuration.GetConnectionString("SRS");
                            var codigos = JsonConvert.DeserializeObject<List<string>>(fc["ids"].ToString());
                            var service = new ProductoSyncService();
                            service.SincronizarEmpresa(Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")), connString, codigos, conn, tx);

                            var _parameters = new List<Dictionary<string, object>>();
                            foreach (var p in productos)
                            {
                                parameters = new Dictionary<string, object>();
                                parameters.Add("codigo", p["clave"]);
                                parameters.Add("cantidad", GetDecimal(p["cantidad"]));
                                parameters.Add("almacen", Convert.ToInt32(almacen));

                                _parameters.Add(parameters);
                            }

                            query = "UPDATE kdil SET c9 = c9 + @cantidad WHERE c2 = 95 AND c1 = '02' AND c3 = @codigo";
                            RunUpdate(query, _parameters, false, "SRS");

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Inventario movido exitosamente", html = $"El producto se movio al almacen" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error", html = ex.Message });
            }
        }
        #endregion

        #region Reporte de Movimientos Almacen
        public JsonResult GetMovimientosAlmacen(string nombre, string sortColumn, string sortDir, int? id_empresa, int? id_sucursal, int? id_almacen, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("nombre", nombre ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("id_empresa", id_empresa ?? 0);
            parameters.Add("id_sucursal", id_sucursal ?? 0);
            parameters.Add("id_almacen", id_almacen ?? 0);

            var allowedColumns = new HashSet<string> {
                "fecha", "tipo_movimiento", "cantidad", "cve_udm",
                "cve_prod", "descr_prod", "origen", "destino",
                "motivo", "usuario", "comentario", "folio"
            };

            if (!allowedColumns.Contains(sortColumn))
                sortColumn = "fecha";

            sortDir = sortDir?.ToLower() == "asc" ? "ASC" : "DESC";

            string where = "";

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where = "AND ( " +
                    "   cp.cve_prod ILIKE '%' || @nombre || '%' " +
                    "   OR cp.descr_prod ILIKE '%' || @nombre || '%' " +
                    "   OR c.cve_udm ILIKE '%' || @nombre || '%' " +
                    "   OR cto.codigo ILIKE '%' || @nombre || '%' " +
                    "   OR ctd.codigo ILIKE '%' || @nombre || '%' " +
                    "   OR tm.tipo_movimiento ILIKE '%' || @nombre || '%' " +
                    "   OR tm.motivo ILIKE '%' || @nombre || '%' " +
                    "   OR tm.comentario ILIKE '%' || @nombre || '%' " +
                    "   OR u.nombre ILIKE '%' || @nombre || '%' " +
                    "   OR u.apellido ILIKE '%' || @nombre || '%' " +
                    "   OR em.folio::text ILIKE '%' || @nombre || '%' " +
                    ")";
            }

            if (id_sucursal.HasValue && id_sucursal.Value > 0)
            {
                where += " AND (ubo.id_sucursal = @id_sucursal OR ubd.id_sucursal = @id_sucursal) ";
            }

            if (id_almacen.HasValue && id_almacen.Value > 0)
            {
                where += " AND (ubo.id_almacen = @id_almacen OR ubd.id_almacen = @id_almacen) ";
            }
            
            if (id_empresa.HasValue && id_empresa.Value > 0)
            {
                where += " AND (ubo.empresaid = @id_empresa OR ubd.empresaid = @id_empresa) ";
            }

            string query = "WITH ubicacion_tarima AS ( " +
                "   SELECT ct.id_tarima, e.nombre AS empresa, cs.id_sucursal, ca.id_almacen, empresaid, ca.cve_almacen, ca.tipo, cn.ulocation " +
                "   FROM cattarimas ct " +
                "   LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "   LEFT JOIN catcolumnas cl ON cl.id_columna = cn.columna_id " +
                "   LEFT JOIN catracks cr ON cr.id_rack = cl.rack_id " +
                "   LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "   LEFT JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                "   LEFT JOIN empresas e ON e.empresaid = cs.empresa_id " +
                ") " +
                "SELECT tm.id_tarima_mov, tm.fecha, tm.tipo_movimiento, tm.cantidad, c.cve_udm, cp.cve_prod, cp.descr_prod, tm.motivo, " +
                "   COALESCE(ubo.empresa || ' - ' || ubo.cve_almacen || ' (' || ubo.tipo || ') - ' || ubo.ulocation, '-') AS origen, " +
                "   COALESCE(ubd.empresa || ' - ' || ubd.cve_almacen || ' (' || ubd.tipo || ') - ' || ubd.ulocation, '-') AS destino, " +
                "   u.nombre || ' ' || u.apellido AS usuario, tm.comentario, em.folio " +
                "FROM tarimas_mov tm " +
                "INNER JOIN catunidades c ON c.id_udm = tm.unidad " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tm.producto_id " +
                "INNER JOIN usuarios u ON u.usuarioid = tm.usuario " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = tm.encabezado_id " +
                "LEFT JOIN cattarimas cto ON cto.id_tarima = tm.tarima_origen " +
                "LEFT JOIN cattarimas ctd ON ctd.id_tarima = tm.tarima_destino " +
                "LEFT JOIN ubicacion_tarima ubo ON ubo.id_tarima = tm.tarima_origen " +
                "LEFT JOIN ubicacion_tarima ubd ON ubd.id_tarima = tm.tarima_destino " +
                $"WHERE 1=1 {where} " +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            query = "WITH ubicacion_tarima AS ( " +
                "   SELECT ct.id_tarima, e.nombre AS empresa, cs.id_sucursal, ca.id_almacen, ca.cve_almacen, ca.tipo, cn.ulocation, empresaid " +
                "   FROM cattarimas ct " +
                "   LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "   LEFT JOIN catcolumnas cl ON cl.id_columna = cn.columna_id " +
                "   LEFT JOIN catracks cr ON cr.id_rack = cl.rack_id " +
                "   LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "   LEFT JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                "   LEFT JOIN empresas e ON e.empresaid = cs.empresa_id " +
                ") " +
                "SELECT COUNT(*) " +
                "FROM tarimas_mov tm " +
                "INNER JOIN catunidades c ON c.id_udm = tm.unidad " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tm.producto_id " +
                "INNER JOIN usuarios u ON u.usuarioid = tm.usuario " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = tm.encabezado_id " +
                "LEFT JOIN cattarimas cto ON cto.id_tarima = tm.tarima_origen " +
                "LEFT JOIN cattarimas ctd ON ctd.id_tarima = tm.tarima_destino " +
                "LEFT JOIN ubicacion_tarima ubo ON ubo.id_tarima = tm.tarima_origen " +
                "LEFT JOIN ubicacion_tarima ubd ON ubd.id_tarima = tm.tarima_destino " +
                $"WHERE 1=1 {where}";

            int total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new { data, total });
        }

        public JsonResult GetTodoMovientosAlmacen(int? id_empresa, int? id_sucursal, int? id_almacen)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id_empresa", id_empresa ?? 0);
            parameters.Add("id_sucursal", id_sucursal ?? 0);
            parameters.Add("id_almacen", id_almacen ?? 0);

            string where = "";

            if (id_sucursal.HasValue && id_sucursal.Value > 0)
            {
                where += " AND (ubo.id_sucursal = @id_sucursal OR ubd.id_sucursal = @id_sucursal) ";
            }

            if (id_almacen.HasValue && id_almacen.Value > 0)
            {
                where += " AND (ubo.id_almacen = @id_almacen OR ubd.id_almacen = @id_almacen) ";
            }

            if (id_empresa.HasValue && id_empresa.Value > 0)
            {
                where += " AND (ubo.empresaid = @id_empresa OR ubd.empresaid = @id_empresa) ";
            }

            string query = "WITH ubicacion_tarima AS (  " +
                "   SELECT ct.id_tarima, e.nombre AS empresa, cs.id_sucursal, ca.id_almacen, e.empresaid, ca.cve_almacen, ca.tipo, cn.ulocation " +
                "   FROM cattarimas ct " +
                "   LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "   LEFT JOIN catcolumnas cl ON cl.id_columna = cn.columna_id " +
                "   LEFT JOIN catracks cr ON cr.id_rack = cl.rack_id " +
                "   LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "   LEFT JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                "   LEFT JOIN empresas e ON e.empresaid = cs.empresa_id " +
                ") " +
                "SELECT tm.fecha, tm.tipo_movimiento, tm.cantidad, c.cve_udm, cp.cve_prod, cp.descr_prod, tm.motivo, " +
                "   COALESCE(ubo.empresa || ' - ' || ubo.cve_almacen || ' (' || ubo.tipo || ') - ' || ubo.ulocation, '-') AS origen, " +
                "   COALESCE(ubd.empresa || ' - ' || ubd.cve_almacen || ' (' || ubd.tipo || ') - ' || ubd.ulocation, '-') AS destino, " +
                "   u.nombre || ' ' || u.apellido AS usuario, tm.comentario, em.folio " +
                "FROM tarimas_mov tm " +
                "INNER JOIN catunidades c ON c.id_udm = tm.unidad " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tm.producto_id " +
                "INNER JOIN usuarios u ON u.usuarioid = tm.usuario " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = tm.encabezado_id " +
                "LEFT JOIN cattarimas cto ON cto.id_tarima = tm.tarima_origen " +
                "LEFT JOIN cattarimas ctd ON ctd.id_tarima = tm.tarima_destino " +
                "LEFT JOIN ubicacion_tarima ubo ON ubo.id_tarima = tm.tarima_origen " +
                "LEFT JOIN ubicacion_tarima ubd ON ubd.id_tarima = tm.tarima_destino " +
                $"WHERE 1=1 {where}";
            var data = RunQuery(query, parameters);

            return Json(new {data});
        }
        #endregion
    }
}