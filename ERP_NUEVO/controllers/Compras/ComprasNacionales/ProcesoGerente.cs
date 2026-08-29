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
        [RoleAuthorize(new string[] { "Gerente", "Administrador", "Super Administrador" }, "ERP_SRS")]
        public JsonResult GetRequisicionesGerente(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "(em.gen = 'CPN' OR em.gen = 'AF') AND em.estatus_id = 1 AND em.usr1 = @usuario_id",
                new Dictionary<string, object> { ["usuario_id"] = GetUserId(User.Identity.Name) });
        }

        [RoleAuthorize(new string[] { "Gerente", "Administrador", "Super Administrador" }, "ERP_SRS")]
        public JsonResult GetRequisicionesConOpcionesGerente(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "(em.gen = 'CPN' OR em.gen = 'AF') AND em.estatus_id = 3 " +
                "AND u.areaid = (SELECT areaid FROM usuarios WHERE usuarioid = @usuario_id)",
                new Dictionary<string, object> { ["usuario_id"] = GetUserId(User.Identity.Name) });
        }

        [RoleAuthorize(new string[] { "Gerente", "Administrador", "Super Administrador" }, "ERP_SRS")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetRequisicionDataAndOptions(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));
            parameters.Add("area", GetAreaName(User.Identity.Name));

            var returnResult = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "   a.nombre AS nombre_area, encabezados_padre, em.tipo_producto " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "   (em.gen = 'CPN' OR em.gen = 'AF' )AND " +
                "   em.estatus_id = 3 and em.id_encabezado = @id";

            var requisicion = RunQuery(query, parameters)[0];
            returnResult.Add("requisicion", requisicion);

            query = "SELECT p.fol_doc, p.cant_ud, p.pv_prod, p.cve_prod, p.descr_prod, p.id_partidas, p.ud, " +
                "   COALESCE(p.dto1, 0) AS descuento, p.imp_part AS total, " +
                "   ROUND(COALESCE(p.imp_part, 0)::numeric * (1 - COALESCE(p.dto1, 0)::numeric / 100), 2) AS totaldescuento " +
                "FROM partidasdoc p " +
                "WHERE p.encabezado_id = @id";
            var partidas = RunQuery(query, parameters);
            returnResult.Add("partidas", partidas);

            parameters.Add("encabezados_padre", int.Parse(requisicion["encabezados_padre"].ToString()));
            query = "SELECT p.fol_doc, p.cant_ud, p.pv_prod, p.cve_prod, p.descr_prod, p.id_partidas, p.ud, " +
                "   COALESCE(p.dto1, 0) AS descuento, p.imp_part AS total, " +
                "   ROUND(COALESCE(p.imp_part, 0)::numeric * (1 - COALESCE(p.dto1, 0)::numeric / 100), 2) AS totaldescuento " +
                "FROM partidasdoc p " +
                "WHERE p.encabezado_id = @encabezados_padre";
            var partidasPadre = RunQuery(query, parameters);
            returnResult.Add("partidasPadre", partidasPadre);

            query = "SELECT po.id_producto_opcion, po.partidas_id, po.prov_id, po.encabezado_id, po.producto, po.precio, " +
                "   po.descripcion, po.cantidad, po.uuid, po.unidad, po.extencion, po.ruta, po.proveedor_nombre, " +
                "   po.nombre_original, p.cant_ud, po.precio_venta, COALESCE(po.descuento, 0) AS descuento, " +
                "   ROUND((po.cantidad * po.precio)::numeric, 2) AS subtotal, " +
                "   ROUND((po.cantidad * po.precio)::numeric * (1 - COALESCE(po.descuento, 0)::numeric / 100), 2) AS total " +
                "FROM productos_opciones po " +
                "INNER JOIN (SELECT cant_ud, id_partidas FROM partidasdoc) p ON p.id_partidas = po.partidas_id " +
                "WHERE po.encabezado_id = @id;";
            var opciones = RunQuery(query, parameters);
            returnResult.Add("opciones", opciones);

            query = "SELECT usuarioid, nombre || ' ' || apellido AS nombre_completo " +
                "FROM usuarios " +
                "WHERE activo = true AND areaid =4;";
            var finanzas = RunQuery(query);
            returnResult.Add("finanzas", finanzas);
            return Json(returnResult);
        }
        #endregion

        #region Acciones Requisision
        [HttpPost, ValidateAntiForgeryToken]
        [RoleAuthorize(new string[] { "Gerente", "Administrador", "Super Administrador" }, "ERP_SRS")]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Aceptar requisicion de material")]
        public JsonResult AceptarRequisicion(int id_encabezado)
        {
            try
            {
                string query = "UPDATE encabezadomov SET estatus_id = 2, fch1 = @fechaAprobacion, usr1 = @usr_gerente, tipo_proceso = 'solicitud_cotizacion' WHERE id_encabezado = @id_encabezado;";
                var parameters = new Dictionary<string, object>
                {
                    { "id_encabezado", id_encabezado },
                    { "fechaAprobacion", DateTime.Now },
                    { "usr_gerente", GetUserId(User.Identity.Name) }
                };
                RunUpdate(query, parameters);

                query = "SELECT u.nombreusuario, u.email FROM usuarios u " +
                        "INNER JOIN encabezadomov em ON em.usr0 = u.usuarioid " +
                        "WHERE em.id_encabezado = @id_encabezado";
                var userResult = RunQuery(query, parameters)[0];

                query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                    "FROM encabezadomov em WHERE em.id_encabezado = @id_encabezado";
                var folio = RunScalar(query, parameters);

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/MisRequisiciones";
                string urlCotizacion = $"{urlBase}{path}?id={folio.ToString()}";

                _ = SendNotificationInterno(userResult["nombreusuario"].ToString(), userResult["email"].ToString(), new
                {
                    icon = "success",
                    title = "Solicitud de cotizacion aceptada",
                    message = $"Tu gerente de área ha aprobado la solicitud de cotizacion con el folio {folio.ToString()}.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                query = "SELECT u.nombreusuario, u.email FROM usuarios u " +
                        "INNER JOIN encabezadomov em ON em.usr2 = u.usuarioid " +
                        "WHERE em.id_encabezado = @id_encabezado";
                userResult = RunQuery(query, parameters)[0];

                urlBase = $"{Request.Scheme}://{Request.Host}";
                path = "/Compras/GestionSolicitudes";
                urlCotizacion = $"{urlBase}{path}?id={folio.ToString()}";

                _ = SendNotificationInterno(userResult["nombreusuario"].ToString(), userResult["email"].ToString(), new
                {
                    icon = "success",
                    title = "Nueva solicitud de cotizacion",
                    message = $"Se ha creado una nueva solicitud de cotizacion con el folio {folio}. Está pendiente de su revisión.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });


                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RoleAuthorize(new string[] { "Gerente", "Administrador", "Super Administrador" }, "ERP_SRS")]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Rechazar requisicion de material")]
        public JsonResult RechazarRequisicion(int id_encabezado, string comentario)
        {
            try
            {
                string query = "UPDATE encabezadomov SET estatus_id = 8, coment1 = @comentario WHERE id_encabezado=@id_encabezado";
                var parameters = new Dictionary<string, object>
                {
                    { "id_encabezado", id_encabezado },
                    { "comentario", comentario }
                };
                RunUpdate(query, parameters);

                query = "SELECT u.nombreusuario, u.email FROM usuarios u " +
                        "INNER JOIN encabezadomov em ON em.usr0 = u.usuarioid " +
                        "WHERE em.id_encabezado = @id_encabezado";
                var userResult = RunQuery(query, parameters)[0];

                query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                    "FROM encabezadomov em WHERE em.id_encabezado = @id_encabezado";
                var folio = RunScalar(query, parameters);

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/GestionSolicitudes";
                string urlCotizacion = $"{urlBase}{path}?id={folio.ToString()}";

                _ = SendNotificationInterno(userResult["nombreusuario"].ToString(), userResult["email"].ToString(), new
                {
                    icon = "error",
                    title = "Requisición rechazada",
                    message = $"Tu gerente de área ha rechazado la requisición con el folio {folio}.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        #endregion

        #region Acciones Cotizacion
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Creacion de cotizacion y seeccionar productos de opcines")]
        public JsonResult GenerarCotizacion(IFormCollection fc)
        {
            try
            {
                var usrParameter = new Dictionary<string, object>();
                string query = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, tipo_proceso, tipo_producto, variacion," +
                    "   suc, nro_gpo_doc, nro_tp_doc, fol_doc, usr_dep, en_presupuesto, em.nat, em.es_servicio, centro_costos " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                usrParameter.Add("id", Convert.ToInt32(fc["id_encabezado"].ToString()));
                var usrId = RunQuery(query, usrParameter)[0];
                // Presupuesto es obligatorio: toda cotizacion pasa por finanzas (estatus 4)
                int estatusSiguiente = 4;

                int idEncabezadoOrigen = Convert.ToInt32(fc["id_encabezado"].ToString());
                var partidas = LeerPartidasSeleccionadas(fc["partidas"].ToString());

                if (!partidas.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "Debes seleccionar al menos una opción por partida antes de guardar."
                    });
                }

                var totales = DescuentosService.Normalizar(partidas);

                var documento = new Dictionary<string, Object>();
                if (Convert.ToInt32(usrId["variacion"]) > 0)
                {
                    var _parameters = new List<Dictionary<string, object>>();

                    foreach (var partida in partidas)
                    {
                        _parameters.Add(new Dictionary<string, object>
                        {
                            ["cve_suc"] = usrId["suc"].ToString(),
                            ["gen"] = "CPN",
                            ["nat"] = "RM",
                            ["nro_gpo_mov"] = usrId["nro_gpo_doc"],
                            ["nro_tp_mov"] = usrId["nro_tp_doc"],
                            ["fol_doc"] = usrId["fol_doc"],

                            ["cve_prod"] = partida.CveProd,
                            ["descr_prod"] = partida.DescrProd,
                            ["ud"] = partida.Ud,
                            ["cant_ud"] = partida.CantUd,
                            ["pv_prod"] = partida.PvProd,
                            ["imp_part"] = partida.ImpPart,
                            ["encabezado_id"] = idEncabezadoOrigen,
                            ["refe"] = (object)partida.Ref ?? DBNull.Value,
                            ["cve_vdr_cpr"] = partida.CveVdrCpr,
                            ["dto1"] = partida.Dto1,
                            ["cto_vta_part"] = (object)partida.CtoVtaPart ?? DBNull.Value,
                            ["variacion"] = Convert.ToInt32(usrId["variacion"]) + 1
                        });
                    }

                    query = "INSERT INTO partidasdoc " +
                        "   (cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, cve_prod, descr_prod, ud, cant_ud, pv_prod, " +
                        "       imp_part, encabezado_id, refe, cve_vdr_cpr, dto1, cto_vta_part, variacion) " +
                        "VALUES " +
                        "   (@cve_suc, @gen, @nat, @nro_gpo_mov, @nro_tp_mov, @fol_doc, @cve_prod, @descr_prod, @ud, @cant_ud, " +
                        "       @pv_prod, @imp_part, @encabezado_id, @refe, @cve_vdr_cpr, @dto1, @cto_vta_part, @variacion)";
                    RunUpdate(query, _parameters);

                    documento.Add("folio_generado", GenerateFolio(idEncabezadoOrigen));

                    var encabezadoParam = new Dictionary<string, object>();
                    query = "UPDATE encabezadomov SET estatus_id = @estatus, firma1 = @firma1, sub = @sub, dto = @dto, imp = @imp " +
                        "WHERE id_encabezado = @encabezadoId";
                    encabezadoParam.Add("encabezadoId", idEncabezadoOrigen);
                    encabezadoParam.Add("firma1", fc["firma1"].ToString());
                    encabezadoParam.Add("estatus", estatusSiguiente);
                    encabezadoParam.Add("sub", totales.Subtotal);
                    encabezadoParam.Add("dto", totales.Descuento);
                    encabezadoParam.Add("imp", totales.Base);
                    RunUpdate(query, encabezadoParam);
                }
                else
                {
                    var encabezado = new DocumentoEncabezado();
                    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                    encabezado.IdArea = 2;
                    encabezado.IdTpDoc = 3;
                    encabezado.TpMov = "RM";
                    encabezado.Anio = DateTime.Now.Year;
                    encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                    encabezado.Fch = DateTime.Now;
                    encabezado.ComentAut = fc["comentario"].ToString();
                    encabezado.UsrDoc = User.Identity.Name;
                    encabezado.FchCap = DateTime.Now;
                    encabezado.UsrDep = usrId["usr_dep"].ToString();
                    encabezado.Usr0 = Convert.ToInt32(usrId["usr0"]);
                    encabezado.Fch0 = (DateTime)usrId["fch0"];
                    encabezado.Usr1 = Convert.ToInt32(usrId["usr1"]);
                    encabezado.Fch1 = (DateTime)usrId["fch1"];
                    encabezado.Usr2 = Convert.ToInt32(usrId["usr2"]);
                    encabezado.Fch2 = (DateTime)usrId["fch2"];
                    encabezado.Usr3 = Convert.ToInt32(GetUserId(User.Identity.Name));
                    encabezado.Usr4 = Convert.ToInt32(fc["usuario_finanzas_id"].ToString());
                    encabezado.Fch3 = DateTime.Now;
                    encabezado.Sub = totales.Subtotal;
                    encabezado.Dto = totales.Descuento;
                    encabezado.Imp = totales.Base;
                    encabezado.TipoPoceso = usrId["tipo_proceso"].ToString();
                    //encabezado.TipoProducto = usrId["tipo_producto"].ToString();
                    encabezado.CliProv = "srs";
                    encabezado.EncabezadoPadre = idEncabezadoOrigen;
                    encabezado.Estatus = estatusSiguiente;
                    encabezado.Firma3 = fc["firma1"].ToString();
                    //encabezado.EnPresupuesto = Convert.ToBoolean(fc["en_presupuesto"].ToString());
                    encabezado.EsServicio = Convert.ToBoolean(usrId["es_servicio"]);
                    encabezado.CentroCostos = GetInt(usrId["centro_costos"]);

                    if (new List<string> { "CTZG" }.Contains(usrId["nat"].ToString()))
                    {
                        encabezado.EsServicio = true;
                        encabezado.IdTpDoc = 77;
                        encabezado.TpMov = "RG";
                        encabezado.IdArea = 4;
                    }

                    documento = GenerarDocumentoConPartidas(encabezado, partidas);

                    var encabezadoParam = new Dictionary<string, object>();
                    query = "UPDATE encabezadomov SET estatus_id = 11  WHERE id_encabezado = @encabezadoId";
                    encabezadoParam.Add("encabezadoId", idEncabezadoOrigen);
                    RunUpdate(query, encabezadoParam);

                    encabezadoParam = new Dictionary<string, object>();

                    encabezadoParam.Add("nombreUsuario", User.Identity.Name);
                    query = "SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = @nombreUsuario";
                    var user = RunScalar(query, encabezadoParam);

                    query = "SELECT u.nombre || ' ' || u.apellido  AS usuario, u.nombreusuario, u.email " +
                        "FROM encabezadomov e " +
                        "INNER JOIN usuarios u ON u.usuarioid = e.usr2 " +
                        "WHERE e.id_encabezado = @encabezado";

                    encabezadoParam.Add("encabezado", idEncabezadoOrigen);
                    var finanzasResult = RunQuery(query, encabezadoParam)[0];

                    var urlBase = $"{Request.Scheme}://{Request.Host}";
                    string path = "/Compras/GestionSolicitudes";
                    string urlCotizacion = $"{urlBase}{path}?id={documento["folio_generado"].ToString()}";

                    _ = SendNotificationInterno(finanzasResult["nombreusuario"].ToString(), finanzasResult["email"].ToString(), new
                    {
                        icon = "info",
                        title = "Nueva cotizaccion creada",
                        message = $"El usuario {user} a creado una requisicion de material con el folio {documento["folio_generado"].ToString()} que necesita de su aprobacion.",
                        buttons = new[]
                        {
                            new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                            new { text = "Más Tarde", style = "secondary", action = (string)null }
                        },
                        timer = 0,
                    });
                }

                return Json(new { success = true, folio_generado = documento["folio_generado"].ToString() });
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

        private List<PartidaDocumento> LeerPartidasSeleccionadas(string json)
        {
            var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json)
                ?? new List<Dictionary<string, string>>();

            string Campo(Dictionary<string, string> p, string clave, string porDefecto = "")
                => p.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v) && v != "null" ? v : porDefecto;

            return productos.Select(p => new PartidaDocumento
            {
                CveProd = Campo(p, "producto_nombre"),
                DescrProd = Campo(p, "descripcion"),
                CantUd = GetDecimal(Campo(p, "cantidad", "0"), 0),
                PvProd = GetDecimal(Campo(p, "costo_unitario", "0"), 0),
                Dto1 = GetDecimal(Campo(p, "descuento", "0"), 0),
                CtoVtaPart = GetDecimal(Campo(p, "precio_venta", null)),
                Ref = GetInt(Campo(p, "proveedor_id", null)),
                CveVdrCpr = Campo(p, "proveedor_nombre"),
                Ud = Campo(p, "unidad", "PZA")
            }).ToList();
        }
        #endregion
    }
}
