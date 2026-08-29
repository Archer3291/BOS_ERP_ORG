using Newtonsoft.Json;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Filters;
using ClosedXML.Excel;
using System.Text;
using System.Globalization;
using BOS_ERP.Services;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers.ComprasInternacionales.Procesos
{
    public class ProcesoDirectorInternacionalController : Utilities
    {
        private readonly EmailSender emailSender;
        private readonly CorreoHelper correoHelper;

        public ProcesoDirectorInternacionalController(EmailSender emailSenderService, CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }

        #region Obtener datos generales
        public JsonResult GetRequisicionesDirector()
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "   (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = em.usr_doc) AS responsable, " +
                "   a.nombre AS nombre_area, " +
                "   (SELECT COUNT(*) FROM partidasdoc pd WHERE pd.encabezado_id = em.id_encabezado) AS total_partidas " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "   em.gen = 'CPI' AND " +
                "   em.nat = 'PI' AND " +
                "   em.estatus_id != 11 AND em.estatus_id = 13";

            parameters.Clear();
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }
        public JsonResult GetOrdenesDeCompraDirector()
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "   a.nombre AS nombre_area, em.reclasificacion " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "   em.gen = 'CPI' AND " +
                "   em.nat = 'OCDI' AND " +
                "   em.estatus_id != 11 AND em.estatus_id = 7";
            parameters.Clear();
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }

        public JsonResult GetPlaneadorCIDirector()
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, coment1, " +
                "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "   a.nombre AS nombre_area, em.reclasificacion " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr0 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "   em.gen = 'CPI' AND " +
                "   em.nat = 'PLCI' AND " +
                "   em.estatus_id != 11";
            parameters.Clear();
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }
        #endregion

        #region Acciones Orden de compras
        [HttpPost]
        public JsonResult ProcesarExcelDesdeBD()
        {
            try
            {
                int id_encabezado = Convert.ToInt32(Request.Form["id_encabezado"].ToString());

                var parameters = new Dictionary<string, object>();
                string query = "SELECT path, nombre_original, uuid FROM archivos_planeador_ci WHERE encabezado_id = @encabezado_id";
                parameters.Add("encabezado_id", id_encabezado);
                var result = RunQuery(query, parameters);

                if (result.Count == 0)
                {
                    return Json(new { success = false, message = "No se encontró archivo asociado." });
                }

                string pathArchivo = result[0]["path"].ToString();
                string rutaFisica = Path.Combine("~/" + pathArchivo);

                if (!System.IO.File.Exists(rutaFisica))
                {
                    return Json(new { success = false, message = "El archivo no existe en el servidor." });
                }

                var listaProductos = new List<InventarioProducto>();

                using (var stream = System.IO.File.OpenRead(rutaFisica))
                using (var workbook = new XLWorkbook(stream))
                {
                    var hoja = workbook.Worksheet(1);
                    var headerRow = hoja.FirstRowUsed();

                    // Función para encontrar índice por nombre aproximado
                    var columnasUsadas = new HashSet<int>();

                    System.Func<string[], int> FindColMulti = (keywords) =>
                    {
                        var c = headerRow.CellsUsed()
                            .FirstOrDefault(cell =>
                            {
                                var texto = cell.GetString();
                                return keywords.All(k =>
                                    texto.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                            });

                        if (c != null && !columnasUsadas.Contains(c.Address.ColumnNumber))
                        {
                            columnasUsadas.Add(c.Address.ColumnNumber);
                            return c.Address.ColumnNumber;
                        }
                        return -1;
                    };
                    int cNoParte = FindColMulti(new[] { "parte" });
                    int cDescripcion = FindColMulti(new[] { "descripción" });
                    int cUnidad = FindColMulti(new[] { "unidad" });
                    int cTotalAlmacenes = FindColMulti(new[] { "total", "almacenes" });
                    int cConsumoMes = FindColMulti(new[] { "con.", "mes" });
                    int cPorCubrir = FindColMulti(new[] { "por", "cubrir" });
                    int cEnFabListo = FindColMulti(new[] { "en fabricación", "listo" });
                    int cEnFabricacion = headerRow.CellsUsed()
    .FirstOrDefault(cell =>
    {
        var texto = cell.GetString();
        return texto.IndexOf("en fabricación", StringComparison.OrdinalIgnoreCase) >= 0
            && texto.IndexOf("listo", StringComparison.OrdinalIgnoreCase) < 0;
    })?.Address.ColumnNumber ?? -1;
                    int cInvMax = FindColMulti(new[] { "maximo" });
                    int cSugeridoCompra = FindColMulti(new[] { "sugerido" });
                    int cPeso = FindColMulti(new[] { "peso" });
                    int cPesoTotal = FindColMulti(new[] { "peso", "total" });

                    // Función para lectura segura de double
                    double GetDoubleSafe(IXLCell cell)
                    {
                        if (cell == null || cell.IsEmpty()) return 0;
                        if (cell.TryGetValue<double>(out var val)) return val;
                        if (double.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                            return val;
                        if (double.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.CurrentCulture, out val))
                            return val;
                        return 0;
                    }

                    // Recorrer filas (saltando encabezado)
                    foreach (var row in hoja.RowsUsed().Skip(1))
                    {
                        var producto = new InventarioProducto
                        {
                            NoParteSRS = row.Cell(cNoParte).GetString().Trim(),
                            DescripcionProducto = row.Cell(cDescripcion).GetString().Trim(),
                            UnidadMedida = row.Cell(cUnidad).GetString().Trim(),
                            TotalAlmacenes = GetDoubleSafe(row.Cell(cTotalAlmacenes)),
                            ConsumoMes = GetDoubleSafe(row.Cell(cConsumoMes)),
                            PorCubrir = GetDoubleSafe(row.Cell(cPorCubrir)),
                            EnFabricacionListo = GetDoubleSafe(row.Cell(cEnFabListo)),
                            EnFabricacion = GetDoubleSafe(row.Cell(cEnFabricacion)),
                            InventarioMaximo = GetDoubleSafe(row.Cell(cInvMax)),
                            Compra = GetDoubleSafe(row.Cell(cSugeridoCompra)),
                            Peso = GetDoubleSafe(row.Cell(cPeso)),
                            PesoTotal = GetDoubleSafe(row.Cell(cPesoTotal)),
                        };

                        listaProductos.Add(producto);
                    }
                }

                TempData["ProductosPlaneador"] = listaProductos;
                TempData["IdEncabezado"] = id_encabezado;

                string redirectUrl = Url.Action("VerInventarioPlaneador", "ProcesoDirectorInternacional");
                return Json(new { success = true, redirectUrl });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult VerInventarioPlaneador()
        {
            var productos = TempData["ProductosPlaneador"] as List<InventarioProducto>;
            var idEncabezado = TempData["IdEncabezado"];

            if (productos == null)
                return Content("No hay datos para mostrar.");

            ViewBag.IdEncabezado = idEncabezado; // Pasar el ID a la vista
            return View("/Views/ComprasInternacionales/InventarioListado.cshtml", productos);
        }


        public async Task<JsonResult> CrearODCI(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["id_encabezado"].ToString()))
                {
                    return Json(new { success = false, message = "Documento no encontrado." });
                }

                var parameters = new Dictionary<string, object>();
                parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                string queryPartidas = "SELECT cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, nro_part,  " +
                    "cve_prod, cant_ud, descr_prod, ud, pv_prod, imp_part, dto1, iva, ieps, f_pago_id,  " +
                    "gpo_doc_ant, tp_doc_ant, fol_doc_ant, part_doc_ant, saldo_ud_part, cve_cli, cto_vta_part,  " +
                    "cve_vdr_cpr, refe, cve_alm, fch, mt_cto_, exis_prev_u, exis_prev_peso, ccy, cto_ccy,  " +
                    "vta_ccy, ot, conc, encabezado_id, id_partidas " +
                    "FROM partidasdoc " +
                    "WHERE encabezado_id = @id_encabezado;";


                var partidas = RunQuery(queryPartidas, parameters);


                decimal total = 0;

                foreach (var row in partidas)
                {
                    if (row["imp_part"] != null && decimal.TryParse(row["imp_part"].ToString(), out decimal importe))
                    {
                        total += importe;
                    }
                }

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
                string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, tipo_proceso, tipo_producto " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                usrParameter.Add("id", Convert.ToInt32(fc["id_encabezado"].ToString()));
                var usrId = RunQuery(usrquery, usrParameter)[0];
                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 3,
                    IdTpDoc = 13,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "OCDI",
                    ComentAut = fc["comentario"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(usrId["usr0"]),
                    Fch0 = (DateTime)usrId["fch0"],
                    Usr1 = Convert.ToInt32(GetUserId(User.Identity.Name)),
                    Fch1 = DateTime.Now,
                    Imp = total,
                    CliProv = "srs",
                    Estatus = 7,
                    EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString()),
                    TipoPoceso = "oc_internacional",
                };

                // 📌 Parsear productos como partidas
                var partidasODCI = new List<PartidaDocumento>();
                int nro = 1;
                foreach (var row in RunQuery(queryPartidas, parameters))
                {
                    partidasODCI.Add(new PartidaDocumento
                    {
                        NroPart = nro++,
                        CveProd = row["cve_prod"]?.ToString() ?? "",
                        Ref = row["refe"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["refe"]),
                        CveVdrCpr = row["cve_vdr_cpr"]?.ToString() ?? "",
                        DescrProd = row["descr_prod"]?.ToString() ?? "",
                        CantUd = row["cant_ud"] != null ? Convert.ToDecimal(row["cant_ud"]) : 0,
                        PvProd = row["pv_prod"] != null ? Convert.ToDecimal(row["pv_prod"]) : 0,
                        CtoVtaPart = row["cto_vta_part"] != null ? Convert.ToDecimal(row["cto_vta_part"]) : 0,
                        ImpPart = row["imp_part"] != null ? Convert.ToDecimal(row["imp_part"]) : 0,
                        Ud = row["ud"]?.ToString() ?? "PZA"
                    });
                }

                // 📌 Guardar documento
                var folio = GenerarDocumentoConPartidas(encabezado, partidasODCI);

                query = "UPDATE encabezadomov SET estatus_id = 11  WHERE id_encabezado = @id_encabezado";
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
                    RecipientName = userResult["nombremanager"].ToString(),
                    RecipientEmail = userResult["emailmanager"].ToString(),
                    URL = urlCotizacion
                };

                string htmlBody = await emailSender.RenderViewToStringAsync("~/Views/Email/_SendCreateRequisitionNotification.cshtml", emailData);

                await correoHelper.EnviarCorreoNotificacionAsync(emailData.RecipientEmail, "Nueva Requisicion Creada", htmlBody);

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
                return Json(new
                {
                    success = false,
                    message = "Error al procesar los datos: " + ex.Message
                });

            }

        }
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult AprobarODCI(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                parameters.Add("firma", fc["firma"].ToString());

                return Json(new { success = true, message = "Aprobación de ODCI" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al aprobar ODCI: " + ex.Message });

            }
        }

        //[HttpPost]
        //public JsonResult Reclasificar(IFormCollection fc)
        //{
        //    if (YaEstaReclasificado(Convert.ToInt32(fc["id_encabezado"].ToString())))
        //    {
        //        return Json(new { success = false, message = "Este documento ya ha sido reclasificado previamente." });
        //    }

        //    try
        //    {
        //        var parameters = new Dictionary<string, object>();

        //        //string description = fc["observaciones2"].ToString();
        //        decimal total = decimal.Parse(fc["precio"].ToString());

        //        string query = "SELECT u.usuarioid, u.nombre || u.apellido AS nombreUsuario, " +
        //            "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
        //            "   u2.usuarioid AS managerId, u2.nombre || u2.apellido AS nombreManager, " +
        //            "   u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
        //            "FROM usuarios u " +
        //            "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = u.areaid " +
        //            "WHERE u.nombreusuario = @userName";
        //        parameters.Add("userName", User.Identity.Name);
        //        var userResult = RunQuery(query, parameters)[0];

        //        if (userResult == null || userResult.Count() == 0)
        //        {
        //            return Json(new { success = false, message = "No se encontro un gerente para esta area." });
        //        }

        //        var usrParameter = new Dictionary<string, object>();
        //        string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, tipo_proceso, tipo_producto " +
        //            "FROM encabezadomov em " +
        //            "WHERE em.id_encabezado = @id";
        //        usrParameter.Add("id", Convert.ToInt32(fc["id_encabezado"].ToString()));
        //        var usrId = RunQuery(usrquery, usrParameter)[0];
        //        // 📌 Crear encabezado del documento
        //        var encabezado = new DocumentoEncabezado
        //        {
        //            IdArea = 3,
        //            IdTpDoc = 21,
        //            UsrDep = GetAreaName(User.Identity.Name),
        //            Anio = DateTime.Now.Year,
        //            Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
        //            Fch = DateTime.Now,
        //            TpMov = "RCL",
        //            UsrDoc = User.Identity.Name,
        //            FchCap = DateTime.Now,
        //            Usr0 = usrId["usr0"] != DBNull.Value ? Convert.ToInt32(usrId["usr0"]) : (int?)null,
        //            Fch0 = usrId["fch0"] != DBNull.Value ? (DateTime?)usrId["fch0"] : null,
        //            Usr1 = usrId["usr1"] != DBNull.Value ? Convert.ToInt32(usrId["usr1"]) : (int?)null,
        //            Fch1 = usrId["fch1"] != DBNull.Value ? (DateTime?)usrId["fch1"] : null,
        //            Usr2 = Convert.ToInt32(GetUserId(User.Identity.Name)),
        //            Fch2 = DateTime.Now,
        //            Iva = 0,
        //            Imp = total,
        //            CliProv = "srs",
        //            Estatus = 1,
        //            EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString()),
        //        };

        //        // 📌 Parsear productos como partidas
        //        var partidas = new List<PartidaDocumento>();
        //        var folio = GenerarDocumentoConPartidas(encabezado, partidas);

        //        query = "update encabezadomov set reclasificacion = true  where id_encabezado = @id_encabezado";
        //        parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
        //        RunUpdate(query, parameters);
        //        return Json(new { success = true, message = "Documento de reclasificación creado.", folio_generado = folio["folio_generado"].ToString() });

        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { success = false, message = "Error: " + ex.Message });
        //    }
        //}

        //public bool YaEstaReclasificado(int id_encabezado)
        //{
        //    var parameters = new Dictionary<string, object>
        //    {
        //        { "id_encabezado", id_encabezado }
        //    };
        //    string query = "select reclasificacion from encabezadomov where id_encabezado = @id_encabezado";
        //    var result = RunScalar(query, parameters);

        //    if (result == null || result == DBNull.Value)
        //        return false;

        //    return Convert.ToBoolean(result);
        //}

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Internacionales", Accion = "Creacion de solicitud de cotizacion internacional")]
        public JsonResult ProcesarSeleccionados(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["id_encabezado"].ToString()))
                {
                    return Json(new { success = false, message = "Documento no encontrado." });
                }

                var parameters = new Dictionary<string, object>();

                string query = "SELECT u.usuarioid, u.nombre || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   u2.usuarioid AS managerId, u2.nombre || u2.apellido AS nombreManager, " +
                    "   u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";

                var usrParameter = new Dictionary<string, object>();
                string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, tipo_proceso, tipo_producto " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                usrParameter.Add("id", Convert.ToInt32(fc["id_encabezado"].ToString()));
                var usrId = RunQuery(usrquery, usrParameter)[0];

                // 📌 Obtener información del archivo ANTES de procesar
                string fileQuery = "SELECT path, uuid FROM archivos_planeador_ci WHERE encabezado_id = @encabezado_id";
                var fileParameters = new Dictionary<string, object>();
                fileParameters.Add("encabezado_id", Convert.ToInt32(fc["id_encabezado"].ToString()));
                var fileResult = RunQuery(fileQuery, fileParameters);

                string filePath = "";
                string fileUuid = "";
                string rutaFisica = "";
                if (fileResult.Count > 0)
                {
                    filePath = fileResult[0]["path"].ToString();
                    rutaFisica = Path.Combine("~/" + filePath);
                    fileUuid = fileResult[0]["uuid"].ToString();
                }

                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 3,
                    IdTpDoc = 23,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "SCINT",
                    //ComentAut = fc["comentario"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(usrId["usr0"]),
                    Fch0 = (DateTime)usrId["fch0"],
                    Usr1 = Convert.ToInt32(GetUserId(User.Identity.Name)),
                    Fch1 = DateTime.Now,
                    Firma1 = fc["firma1"].ToString(),
                    CliProv = "srs",
                    Estatus = 7,
                    EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString()),
                };

                string jsonProductos = fc["productosSeleccionados"].ToString();
                string cantidadStr = fc["cantidad"].ToString();

                int cantidad = 0;
                int.TryParse(cantidadStr, out cantidad);

                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonProductos);

                // 📌 Parsear productos como partidas
                var partidas = new List<PartidaDocumento>();
                int nro = 1;
                foreach (var p in productos)
                {
                    partidas.Add(new PartidaDocumento
                    {
                        NroPart = nro++,
                        CveProd = p["noParteSRS"]?.ToString(),
                        DescrProd = p["descripcion"]?.ToString(),
                        CantUd = Convert.ToDecimal(p["compra"]?.ToString()),
                        Ud = p["unidadMedida"]?.ToString(),
                    });
                }

                // 📌 Guardar documento
                var documento = GenerarDocumentoConPartidas(encabezado, partidas);

                var encabezadoParam = new Dictionary<string, object>();
                query = "UPDATE encabezadomov SET estatus_id = 11  WHERE id_encabezado = @encabezadoId";
                encabezadoParam.Add("encabezadoId", Convert.ToInt32(fc["id_encabezado"].ToString()));
                RunUpdate(query, encabezadoParam);

                string getComprasQuery = "SELECT usuarioid, email, nombreusuario " +
                             "FROM usuarios WHERE usuarioid = @usrCompras";
                encabezadoParam.Clear();
                encabezadoParam.Add("usrCompras", Convert.ToInt32(usrId["usr0"]));

                var comprasResult = RunQuery(getComprasQuery, encabezadoParam)[0];

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/GestionSolicitudes";
                string urlCotizacion = $"{urlBase}{path}?id={documento["folio_generado"].ToString()}";

                _ = SendNotificationInterno(comprasResult["nombreusuario"].ToString(), comprasResult["email"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva solicitud dde cotizaccion creada",
                    message = $"El usuario {User.Identity.Name} a creado una nueva cotizacion que necesita de su aprobacion.",
                    buttons = new[]
                    {
                new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                new { text = "Más Tarde", style = "secondary", action = (string)null }
            },
                    timer = 0,
                    folio = documento["folio_generado"].ToString(),
                });

                // 📌 BORRAR EL ARCHIVO DESPUÉS DE QUE TODO SEA EXITOSO
                if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(fileUuid))
                {
                    var deleteResult = DeleteFileFromPath(Path.GetDirectoryName(rutaFisica),fileUuid,".*", true);

                    // Opcional: Log del resultado del borrado
                    if (!deleteResult.result)
                    {
                        // Log warning pero no fallar la operación principal
                        // Logger.Warning($"No se pudo borrar el archivo: {deleteResult.message}");
                    }
                }

                return Json(new { success = true, folio_generado = documento["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al procesar los datos: " + ex.Message
                });

            }
        }
        #endregion

    }
}