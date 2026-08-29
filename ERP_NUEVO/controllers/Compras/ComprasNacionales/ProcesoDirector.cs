using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    public partial class RequisicionController : Utilities
    {
        #region Obtener datos generales
        public JsonResult GetCotizacionDirector(IFormCollection fc)
        {
            return ConsultarDocumentos(fc, "(em.gen = 'CPN' OR em.gen = 'AF') AND em.estatus_id = 6");
        }

        public JsonResult GetImpuestos(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));

            string query = "SELECT id_imp_oc, encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom " +
                           "FROM imp_oc WHERE encabezado_id = @id ; ";

            var result = RunQuery(query, parameters);
            return Json(new { data = result });
        }

        public JsonResult GetCotizacionEditadaDirector(IFormCollection fc)
        {
            return ConsultarDocumentos(fc, "em.gen = 'CPN' AND em.estatus_id = 13");
        }

        [RoleAuthorize(new string[] { "Director", "Administrador", "Super Administrador" }, "ERP_SRS")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetCotizacionPendienteDataDirector(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));
                parameters.Add("area", GetAreaName(User.Identity.Name));

                var returnResult = new Dictionary<string, object>();

                string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                    "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                    "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS nuevo_codigo, " +
                    "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                    "   a.nombre AS nombre_area, encabezados_padre, variacion " +
                    "FROM " +
                    "   encabezadomov em " +
                    "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                    "INNER JOIN areas a ON a.areaid = u.areaid " +
                    "WHERE " +
                    "   em.estatus_id = 6 and em.id_encabezado = @id";

                var requisicion = RunQuery(query, parameters)[0];
                returnResult.Add("requisicion", requisicion);

                parameters.Add("encabezados_padre", int.Parse(requisicion["encabezados_padre"].ToString()));
                query = "SELECT p.fol_doc, p.cve_prod, p.descr_prod, p.id_partidas, p.pv_prod, p.imp_part, " +
                    "   p.cant_ud, p.iva, p.f_pago_id, p.refe, p.cve_vdr_cpr, p.ud, COALESCE(p.dto1, 0) AS descuento, " +
                    "   ROUND(COALESCE(p.imp_part, 0)::numeric * COALESCE(p.dto1, 0)::numeric / 100, 2) AS importeDescuento, " +
                    "   ROUND(COALESCE(p.imp_part, 0)::numeric * (1 - COALESCE(p.dto1, 0)::numeric / 100), 2) AS totalDescuento " +
                    "FROM partidasdoc p " +
                    "WHERE p.encabezado_id = @id " +
                    "AND variacion = (SELECT MAX(variacion) FROM partidasdoc WHERE encabezado_id = @id)";
                var partidas = RunQuery(query, parameters);
                returnResult.Add("partidas", partidas);

                query = "SELECT DISTINCT ac.nombre_original, ac.path, ac.extencion, ac.proveedor_nombre, uuid " +
                    "FROM archivos_compras_oc ac " +
                    "WHERE ac.encabezado_id = @id " +
                    "GROUP BY uuid, nombre_original, path, extencion , proveedor_nombre";
                var archivos = RunQuery(query, parameters);
                returnResult.Add("archivos", archivos);

                query = "SELECT io.id_imp_oc, io.encabezado_id, io.impuesto_id, io.subtotal, io.importe, " +
                    "   io.orden_apl, io.imp_variable, io.prov_nom, io.f_pago_id, cp.descripcion, ci.cve_impuesto, ci.desc, " +
                    "   ci.es_retencion " +
                    "FROM imp_oc io " +
                    "INNER JOIN cat_f_pago cp ON cp.id_f_pago = io.f_pago_id " +
                    "INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
                    "WHERE encabezado_id = @id";
                var impuestos = RunQuery(query, parameters);
                returnResult.Add("impuestos", impuestos);

                return Json(new { success = true, returnResult });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al obtener remisiones: " + ex.Message });

            }
        }
        #endregion

        #region Acciones Orden de compras
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Creacion de orden de compra o solicitud de gasto")]
        public JsonResult GenerarOC(IFormCollection fc)
        {
            // la Fimra 5 no se esta guardando cuando se genera la orden de compra 

            try
            {
                var encabezado = new DocumentoEncabezado();
                string query = "";

                var usrParameter = new Dictionary<string, object>();
                string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma3, em.fch1, em.usr2, em.fch2, em.usr3, " +
                    "   em.fch3, em.usr4, em.fch4, em.usr5, em.firma5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto, em.nat, em.es_servicio, centro_costos " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                usrParameter.Add("id", Convert.ToInt32(fc["id_encabezado"].ToString()));
                var usrId = RunQuery(usrquery, usrParameter)[0];
                string tpMov = "OC";
                int idTpDoc = 11;
                int areaDoc = 2;
                encabezado.Estatus = 17;
                if (usrId["tipo_proceso"].ToString() == "gasto")
                {
                    tpMov = "GTO";
                    idTpDoc = 14;
                    areaDoc = 4;
                    encabezado.Estatus = 11;
                }

                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = areaDoc;
                encabezado.IdTpDoc = idTpDoc;
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = tpMov;
                encabezado.UsrDep = usrId["usr_dep"].ToString();
                encabezado.ComentAut = fc["comentario"].ToString();
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = Convert.ToInt32(usrId["usr0"]);
                encabezado.Fch0 = (DateTime)usrId["fch0"];
                encabezado.Usr1 = Convert.ToInt32(usrId["usr1"]);
                encabezado.Firma3 = usrId["firma3"].ToString();
                encabezado.Fch1 = (DateTime)usrId["fch1"];
                encabezado.Usr2 = Convert.ToInt32(usrId["usr2"]);
                encabezado.Fch2 = (DateTime)usrId["fch2"];
                encabezado.Usr3 = Convert.ToInt32(usrId["usr3"]);
                encabezado.Fch3 = (DateTime)usrId["fch3"];
                encabezado.Usr4 = Convert.ToInt32(usrId["usr4"]);
                encabezado.Fch4 = usrId["fch4"] as DateTime?;
                encabezado.Usr5 = Convert.ToInt32(usrId["usr5"]);
                encabezado.Fch5 = (DateTime)usrId["fch5"];
                encabezado.Firma5 = GetString(usrId["firma5"]);
                encabezado.Usr6 = Convert.ToInt32(GetUserId(User.Identity.Name));
                encabezado.Firma6 = fc["firma6"].ToString();
                encabezado.Fch6 = DateTime.Now;
                encabezado.Imp = Convert.ToDecimal(fc["total"].ToString());
                encabezado.TipoPoceso = usrId["tipo_proceso"].ToString();
                encabezado.CliProv = "srs";
                encabezado.EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString());
                encabezado.EnPresupuesto = Convert.ToBoolean(usrId["en_presupuesto"]);
                encabezado.EsServicio = Convert.ToBoolean(usrId["es_servicio"]);
                encabezado.CentroCostos = GetInt(usrId["centro_costos"]);

                if (new List<string> { "SSRT", "SSRT", "SSR" }.Contains(usrId["nat"].ToString()))
                {
                    encabezado.EsServicio = true;
                }

                string productosJson = fc["partidas"].ToString();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);

                //Agregar productos que no existen a la base de datos
                foreach (var p in productos)
                {
                    var prodParameter = new Dictionary<string, object>();
                    bool esActivo = false;
                    prodParameter.Add("cve_prod", p["cve_prod"]);
                    prodParameter.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    if (usrId["tipo_proceso"].ToString() == "gasto")
                    {
                        esActivo = true;
                    }
                    string queryProductos = "SELECT COUNT(*) FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id";
                    int result = Convert.ToInt32(RunScalar(queryProductos, prodParameter));
                    if (result == 0)
                    {
                        prodParameter.Add("descr_prod", p["descr_prod"]);
                        prodParameter.Add("es_activo", esActivo);
                        prodParameter.Add("udm", p["ud"]);

                        queryProductos = "INSERT INTO catproductos (cve_prod, descr_prod, udm, empresa_id, es_activo) VALUES (@cve_prod, @descr_prod, @udm, @empresa_id, @es_activo)";

                        RunQuery(queryProductos, prodParameter);
                    }
                }

                // 🔴 Validación: que haya al menos una partida seleccionada
                if (productos == null || !productos.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "Debes seleccionar al menos una opción por partida antes de guardar."
                    });
                }
                var gruposPorProveedor = productos
                    .Where(p => p.ContainsKey("cve_vdr_cpr"))
                    .GroupBy(p => p["cve_vdr_cpr"]);

                // Lista para guardar los folios generados
                List<string> foliosGenerados = new List<string>();

                foreach (var grupo in gruposPorProveedor)
                {
                    string proveedor = grupo.Key;
                    int refe = Convert.ToInt32(grupo.First()["refe"]);

                    usrParameter = new Dictionary<string, object>();
                    query = "SELECT f_pago_id FROM imp_oc " +
                        "WHERE encabezado_id= @encabezado AND prov_nom = @prov_nom;";
                    usrParameter.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    usrParameter.Add("prov_nom", proveedor);
                    var fpago = RunScalar(query, usrParameter);

                    // Crear partidas para este proveedor
                    var partidasProv = grupo.Select(p => new PartidaDocumento
                    {
                        CveProd = GetString(p["cve_prod"]),
                        DescrProd = GetString(p["descr_prod"]),
                        CantUd = GetDecimal(p["cant_ud"], 0),
                        PvProd = GetDecimal(p["pv_prod"], 0),
                        FPagoId = GetInt(p["f_pago_id"]),
                        Iva = GetDecimal(p["iva"]),
                        CveVdrCpr = proveedor,
                        Ref = refe,
                        Ud = GetString(p["ud"], "PZA"),
                        Dto1 = GetDecimal(p["descuento"], 0)
                    }).ToList();

                    var parameters = new Dictionary<string, object>();
                    query = "SELECT io.encabezado_id, io.subtotal, io.importe, io.orden_apl, io.imp_variable, " +
                        "   io.prov_nom, io.f_pago_id, ci.es_retencion " +
                        "FROM imp_oc io " +
                        "INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
                        "WHERE io.prov_nom = @proveedor AND io.encabezado_id = @encabezado";
                    parameters.Add("proveedor", proveedor);
                    parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));

                    var impuestos = RunQuery(query, parameters);

                    var totalesProv = DescuentosService.Normalizar(partidasProv);
                    decimal subtotalProv = totalesProv.Base;
                    decimal totalImpuestos = 0;
                    decimal totalRetenciones = 0;

                    foreach (var imp in impuestos)
                    {
                        decimal impImporte = Convert.ToDecimal(imp["importe"]);
                        bool esRetencion = Convert.ToBoolean(imp["es_retencion"]);

                        if (esRetencion)
                            totalRetenciones += impImporte;
                        else
                            totalImpuestos += impImporte;
                    }

                    decimal totalProveedor = subtotalProv + totalImpuestos - totalRetenciones;

                    // Clonar encabezado original y asignar proveedor
                    var encabezadoProv = new DocumentoEncabezado();
                    encabezadoProv.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                    encabezadoProv.IdArea = encabezado.IdArea;
                    encabezadoProv.IdTpDoc = encabezado.IdTpDoc;
                    encabezadoProv.Anio = encabezado.Anio;
                    encabezadoProv.Suc = encabezado.Suc;
                    encabezadoProv.Fch = encabezado.Fch;
                    encabezadoProv.TpMov = encabezado.TpMov;
                    encabezadoProv.UsrDep = encabezado.UsrDep;
                    encabezadoProv.ComentAut = encabezado.ComentAut;
                    encabezadoProv.UsrDoc = encabezado.UsrDoc;
                    encabezadoProv.FchCap = encabezado.FchCap;
                    encabezadoProv.Usr0 = encabezado.Usr0;
                    encabezadoProv.Fch0 = encabezado.Fch0;
                    encabezadoProv.Usr1 = encabezado.Usr1;
                    encabezadoProv.Firma3 = encabezado.Firma3;
                    encabezadoProv.Firma5 = encabezado.Firma5;
                    encabezadoProv.Fch1 = encabezado.Fch1;
                    encabezadoProv.Usr2 = encabezado.Usr2;
                    encabezadoProv.Fch2 = encabezado.Fch2;
                    encabezadoProv.Usr3 = encabezado.Usr3;
                    encabezadoProv.Fch3 = encabezado.Fch3;
                    encabezadoProv.Usr4 = encabezado.Usr4;
                    encabezadoProv.Fch4 = encabezado.Fch4;
                    encabezadoProv.Usr5 = encabezado.Usr5;
                    encabezadoProv.Fch5 = encabezado.Fch5;
                    encabezadoProv.Usr6 = encabezado.Usr6;
                    encabezadoProv.Firma6 = encabezado.Firma6;
                    encabezadoProv.Fch6 = encabezado.Fch6;
                    encabezadoProv.Imp = totalProveedor; // Puedes calcularlo por proveedor si desea;
                    encabezadoProv.TipoPoceso = encabezado.TipoPoceso;
                    encabezadoProv.TipoProducto = encabezado.TipoProducto;
                    encabezadoProv.CliProv = proveedor; // Este es el único que cambi;
                    encabezadoProv.Ref = refe;
                    encabezadoProv.EncabezadoPadre = encabezado.EncabezadoPadre;
                    encabezadoProv.Estatus = encabezado.Estatus;
                    encabezadoProv.EnPresupuesto = encabezado.EnPresupuesto;
                    encabezadoProv.FPago = Convert.ToInt32(fpago);
                    encabezadoProv.EsServicio = encabezado.EsServicio;
                    encabezadoProv.Sub = totalesProv.Subtotal;
                    encabezadoProv.Dto = totalesProv.Descuento;
                    encabezadoProv.CentroCostos = encabezado.CentroCostos;

                    // Guardar documento por proveedor
                    var documento = GenerarDocumentoConPartidas(encabezadoProv, partidasProv);
                    foliosGenerados.Add(documento["folio_generado"].ToString());

                    query = "SELECT nat FROM encabezadomov WHERE id_encabezado = @enca_creado";
                    parameters.Add("enca_creado", Convert.ToInt32(documento["IdEncabezado"]));
                    var naturaleza = GetString(RunScalar(query, parameters));


                    // Solo si es un gasto se registra en la cartera desde aqui
                    if (naturaleza == "GTO")
                    {
                        List<PolizaData> poliza = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), null);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), poliza, false, null);
                        RegistrarCompra(Convert.ToInt32(documento["IdEncabezado"]), GetUserId(User.Identity.Name));
                        RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name));
                    }

                    var encabezadoParam = new Dictionary<string, object>();
                    query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @encabezadoId";
                    encabezadoParam.Add("encabezadoId", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    RunUpdate(query, encabezadoParam);

                    query = "UPDATE imp_oc " +
                        "SET encabezado_id = @encabezado_id_new " +
                        "WHERE encabezado_id= @encabezadoId AND prov_nom = @prov_nom;";
                    encabezadoParam.Add("encabezado_id_new", documento["IdEncabezado"]);
                    encabezadoParam.Add("prov_nom", proveedor);
                    RunUpdate(query, encabezadoParam);

                    query = "SELECT u.nombre || ' ' || u.apellido  AS usuario, u.nombreusuario, u.email " +
                    "FROM encabezadomov e " +
                    "INNER JOIN usuarios u ON u.usuarioid = e.usr5 " +
                    "WHERE e.id_encabezado = @encabezado";
                    encabezadoParam.Clear();
                    encabezadoParam.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    var finanzasResult = RunQuery(query, encabezadoParam)[0];

                    encabezadoParam.Clear();
                    encabezadoParam.Add("nombre", User.Identity.Name);
                    query = "SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = @nombre";
                    var user = RunScalar(query, encabezadoParam);

                    var urlBase = $"{Request.Scheme}://{Request.Host}";
                    string path = "/Compras/GestionSolicitudes";
                    string urlCotizacion = $"{urlBase}{path}?id={documento["folio_generado"].ToString()}";

                    _ = SendNotificationInterno(finanzasResult["nombreusuario"].ToString(), finanzasResult["email"].ToString(), new
                    {
                        icon = "info",
                        title = "Nueva orden de compra aprobada",
                        message = $"El usuario {user} a creado una orden de compra con el folio {documento["folio_generado"].ToString()} que necesita de su aprobacion.",
                        buttons = new[]
                        {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                        timer = 0,
                    });
                }

                // 📌 Guardar documento
                //var documento = GenerarDocumentoConPartidas(encabezado, partidas);

                return Json(new { success = true, folios_generados = foliosGenerados });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al procesar las opciones.",
                    error = ex.Message
                });
            }
        }
        #endregion

        #region Acciones Requisiciones
        [ValidateAntiForgeryToken]
        [RoleAuthorize(new string[] { "Director", "Administrador", "Super Administrador" }, "ERP_SRS")]
        [AreaAuthorize(new string[] { "Direccion", "Administrador", "Super Administrador" }, "ERP_SRS")]
        public JsonResult getCatalogoRechazos()
        {
            string query = "SELECT id, nombre FROM catalogo_rechazos " +
                "ORDER BY nombre";

            var rechazos = RunQuery(query);

            return Json(new { rechazos });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RoleAuthorize(new string[] { "Director", "Administrador", "Super Administrador" }, "ERP_SRS")]
        [AreaAuthorize(new string[] { "Direccion", "Administrador", "Super Administrador" }, "ERP_SRS")]
        public JsonResult rechazarRequisicionDirector(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE encabezadomov SET motivo_rechazo = @motivo_rechazo, " +
                    "   fch6 = @fchRechazo, usr6 = @userName, estatus_id = 15 " +
                    "WHERE id_encabezado = @idEncabezado";

                parameters.Add("motivo_rechazo", Convert.ToInt32(fc["motivo_rechazo"].ToString()));
                parameters.Add("fchRechazo", DateTime.Now);
                parameters.Add("userName", GetUserId(User.Identity.Name));
                parameters.Add("idEncabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));

                RunUpdate(query, parameters);

                query = "SELECT d.nombre || ' ' || d.apellido AS nombreDirector, " +
                    "   u0.email AS emailUsuario, u0.nombreusuario AS aliasUsuario, " +
                    "   u1.email AS emailGerente, u1.nombreusuario AS aliasGerente, " +
                    "   u2.email AS emailComprador, u2.nombreusuario AS aliasComprador, " +
                    "   e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio " +
                    "FROM encabezadomov e " +
                    "INNER JOIN usuarios u0 ON u0.usuarioid = e.usr0 " +
                    "INNER JOIN usuarios u1 ON u1.usuarioid = e.usr1 " +
                    "INNER JOIN usuarios u2 ON u2.usuarioid = e.usr2 " +
                    "INNER JOIN usuarios d ON d.nombreusuario = @nombreDirector " +
                    "WHERE e.id_encabezado = @idEncabezado";

                parameters.Add("nombreDirector", User.Identity.Name);
                var userResult = RunQuery(query, parameters)[0];

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/GestionSolicitudes";
                string urlCotizacion = $"{urlBase}{path}?id={userResult["folio"].ToString()}";

                _ = SendNotificationInterno(userResult["aliasusuario"].ToString(), userResult["emailusuario"].ToString(), new
                {
                    icon = "error",
                    title = "Tu requisición fue rechazada",
                    message = $"El usuario {userResult["nombredirector"]} ha rechazado la requisición que creaste con el folio {userResult["folio"]}.",
                    buttons = new[]
                     {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                _ = SendNotificationInterno(userResult["aliasgerente"].ToString(), userResult["emailgerente"].ToString(), new
                {
                    icon = "error",
                    title = "Requisición rechazada",
                    message = $"La requisición que aprobaste con el folio {userResult["folio"]} fue rechazada por {userResult["nombredirector"]}.",
                    buttons = new[]
                     {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                _ = SendNotificationInterno(userResult["aliascomprador"].ToString(), userResult["emailcomprador"].ToString(), new
                {
                    icon = "error",
                    title = "Requisición rechazada",
                    message = $"La requisición que cotizaste con el folio {userResult["folio"]} fue rechazada por {userResult["nombredirector"]}.",
                    buttons = new[]
                     {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                return Json(new { success = true, message = "Requisición rechazada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al rechazar la requisición: " + ex.Message });
            }
        }

        public JsonResult ReAbrirRequisicion(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE encabezadomov SET estatus_id = 6 WHERE id_encabezado = @id_encabezado";
                parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Requisicion abierta correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ocurro un error al abrir la requisicion" });
            }
        }

        public JsonResult aceptarEdicion(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                // La edicion queda aprobada: la orden vuelve a esperar ingreso a almacen
                string query = "UPDATE encabezadomov SET estatus_id = 17 WHERE id_encabezado = @id_encabezado";
                parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                RunUpdate(query, parameters);

                query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio, " +
                    "   (SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = @nombreUsuario) AS comprador " +
                    "FROM encabezadomov e " +
                    "INNER JOIN usuarios u ON u.usuarioid = e.usr2 " +
                    "WHERE e.id_encabezado = @id_encabezado";
                parameters.Add("nombreUsuario", User.Identity.Name);
                var userResult = RunQuery(query, parameters)[0];

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/GestionSolicitudes";
                string urlCotizacion = $"{urlBase}{path}?id={userResult["folio"].ToString()}";

                _ = SendNotificationInterno(userResult["aliasusuario"].ToString(), userResult["emailusuario"].ToString(), new
                {
                    icon = "info",
                    title = "Edicion de orden de compra aprobada",
                    message = $"El director aprobo la edicion de la orden de compra con el folio {userResult["folio"]}. Sigue pendiente de ingreso a almacen.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                return Json(new { success = true, message = "Edicion aprobada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ocurrio un error al aprobar la edicion" });
            }
        }
        #endregion
    }
}