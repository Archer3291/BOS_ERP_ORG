using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Services;
using System.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    public partial class RequisicionController : Utilities
    {
        public JsonResult GetRequisicionesUsuario(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "em.estatus_id IN (1, 17) AND em.usr_doc = @nombre_usuario",
                new Dictionary<string, object> { ["nombre_usuario"] = User.Identity.Name });
        }

        public JsonResult GetRequisicionesAceptadasGerente(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "em.estatus_id = 2 AND em.usr_doc = @nombre_usuario " +
                "AND em.variacion = (SELECT MAX(variacion) FROM encabezadomov " +
                "    WHERE gen = em.gen AND nat = em.nat AND fol_doc = em.fol_doc)",
                new Dictionary<string, object> { ["nombre_usuario"] = User.Identity.Name });
        }

        public JsonResult GetRequisicionesRechazadasGerente(IFormCollection fc)
        {
            return ConsultarDocumentos(fc, "em.estatus_id IN (8, 15) AND (em.usr0 = @usuarioId OR em.usr_doc = @nombre_usuario)",
                new Dictionary<string, object>
                {
                    ["usuarioId"] = GetUserId(User.Identity.Name),
                    ["nombre_usuario"] = User.Identity.Name
                });
        }

        public JsonResult GetRequisicionesCerradas(IFormCollection fc)
        {
            return ConsultarDocumentos(fc, "em.estatus_id = 11 AND (em.usr0 = @usuarioId OR em.usr_doc = @nombre_usuario)",
                new Dictionary<string, object>
                {
                    ["usuarioId"] = GetUserId(User.Identity.Name),
                    ["nombre_usuario"] = User.Identity.Name
                });
        }

        public JsonResult GetRequisicionesCanceladas(IFormCollection fc)
        {
            return ConsultarDocumentos(fc, "em.estatus_id = 27 AND (em.usr0 = @usuarioId OR em.usr_doc = @nombre_usuario)",
                new Dictionary<string, object>
                {
                    ["usuarioId"] = GetUserId(User.Identity.Name),
                    ["nombre_usuario"] = User.Identity.Name
                });
        }

        public JsonResult GetCatImpuestos()
        {
            string query = "SELECT id_impuesto, cve_impuesto, \"desc\", tasa_imp, es_retencion FROM cat_impuestos";
            var impuestos = RunQuery(query);

            return Json(impuestos);
        }

        #region Proceso de edicion

        // Editar un documento no lo modifica en el lugar: se clona en una variacion
        // con la informacion nueva y el original queda cancelado (27). El estatus con
        // el que nace la variacion depende de donde estaba el documento:
        //   - una orden de compra viva (13/17) vuelve con el director  -> 13
        //   - cualquier otro documento reinicia el flujo de aprobacion -> 1
        [ValidateAntiForgeryToken, HttpPost]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Editar documento de compras")]
        public JsonResult Edit(EditarRequisicionesModel req)
        {
            try
            {
                int? id_encabezado = GetInt(req.IdEncabezado);

                if (id_encabezado == null)
                {
                    return Json(new { icon = "error", title = "Faltan datos", html = "No se puede guardar el documento porque hay campos obligatorios vacios.", documentosHijos = false, showCancelButton = false });
                }

                if (!DoesUserHasRight(User.Identity.Name, "editar-requisiciones"))
                {
                    return Json(new { icon = "error", title = "Sin permiso", html = "No tienes permiso para editar documentos de compras.", documentosHijos = false, showCancelButton = false });
                }

                string observaciones = req.Observaciones ?? "";
                var materiales = JsonConvert.DeserializeObject<List<PartidaDocumento>>(req.Materiales)
                    ?? new List<PartidaDocumento>();
                var impuestos = JsonConvert.DeserializeObject<List<Impuesto>>(req.Impuestos);

                if (!materiales.Any())
                {
                    return Json(new { icon = "error", title = "Sin partidas", html = "El documento necesita al menos una partida.", documentosHijos = false, showCancelButton = false });
                }

                var parameters = new Dictionary<string, object> { ["id_encabezado"] = id_encabezado };

                string query = "SELECT refe, cli_prov, f_pago, estatus_id, nat " +
                    "FROM encabezadomov WHERE id_encabezado = @id_encabezado";
                var encabezado = RunQuery(query, parameters)[0];

                int estatusActual = GetInt(encabezado["estatus_id"], 0).Value;
                bool esOrdenDeCompra = new[] { 13, 17 }.Contains(estatusActual);
                int estatusVariacion = esOrdenDeCompra ? 13 : 1;

                // Los documentos que ya generaron hijos requieren confirmacion explicita:
                // al editarlos hay que revertir los movimientos de inventario de esos hijos.
                query = "SELECT id_encabezado FROM encabezadomov WHERE encabezados_padre = @id_encabezado";
                var documentosHijos = RunQuery(query, parameters);

                if (documentosHijos.Any() && !req.ConfirmarCancelacionHijos)
                {
                    return Json(new
                    {
                        icon = "warning",
                        title = "Este documento tiene documentos asociados",
                        html = "Para poder editarlo, primero deben cancelarse.<br><br>Deseas continuar con la cancelacion automatica?",
                        footer = "<p style='color:red;'>Esta accion no es reversible.</p>",
                        documentosHijos = true
                    });
                }

                var partidas = materiales.Select(par => new PartidaDocumento
                {
                    CveProd = GetString(par.CveProd),
                    DescrProd = GetString(par.DescrProd),
                    CveVdrCpr = GetString(par.CveVdrCpr),
                    Ref = GetInt(encabezado["refe"]),
                    Ud = GetString(par.Ud, "PZA"),
                    FolDocAnt = GetString(par.FolDocAnt),
                    CantUd = GetDecimal(par.CantUd, 0),
                    PvProd = GetDecimal(par.PvProd, 0),
                    Dto1 = GetDecimal(par.Dto1, 0),
                    FPagoId = GetInt(encabezado["f_pago"])
                }).ToList();

                var totales = DescuentosService.Normalizar(partidas);
                var poClonada = new Dictionary<string, object>();

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var doc in documentosHijos)
                            {
                                RunUpdate("SELECT * FROM revertir_movimiento_inventario(@encabezado)",
                                    new Dictionary<string, object> { ["encabezado"] = Convert.ToInt32(doc["id_encabezado"]) },
                                    false, conn, tx);
                            }

                            var impuestoService = new ImpuestoService();
                            decimal? importeImpuestos = impuestoService.RecalcularImpuestos(
                                id_encabezado.Value, totales.Base, impuestos, GetString(encabezado["cli_prov"]), conn, tx);
                            decimal precioTotal = DescuentosService.Redondear(totales.Base + (importeImpuestos ?? 0m));

                            query = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb, @p_estatus)";
                            poClonada = RunQuery(query, new Dictionary<string, object>
                            {
                                ["p_id_original"] = id_encabezado,
                                ["p_total"] = precioTotal,
                                ["p_observaciones"] = observaciones,
                                ["p_usuario"] = GetUserId(User.Identity.Name),
                                ["p_partidas"] = JsonConvert.SerializeObject(partidas),
                                ["p_estatus"] = estatusVariacion
                            }, false, conn, tx)[0];

                            int idVariacion = Convert.ToInt32(poClonada["idencabezado"]);
                            GuardarTotalesDocumento(idVariacion, totales, precioTotal, conn, tx);

                            RunUpdate("UPDATE encabezadomov " +
                                "SET fecha_edicion = @fecha, editado_por = @usuario " +
                                "WHERE id_encabezado = @id_encabezado",
                                new Dictionary<string, object>
                                {
                                    ["fecha"] = DateTime.Now,
                                    ["usuario"] = GetUserId(User.Identity.Name),
                                    ["id_encabezado"] = idVariacion
                                }, false, conn, tx);

                            RunUpdate("UPDATE encabezadomov " +
                                "SET estatus_id = 27, coment_aut = @coment_aut " +
                                "WHERE id_encabezado = @id_encabezado",
                                new Dictionary<string, object>
                                {
                                    ["coment_aut"] = $"Documento cancelado por edicion. Folio nuevo {poClonada["foliodoc"]}",
                                    ["id_encabezado"] = id_encabezado
                                }, false, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                if (esOrdenDeCompra)
                {
                    NotificarEdicionAlDirector(id_encabezado.Value, GetString(poClonada["foliodoc"]));
                }

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(poClonada["idencabezado"]),
                    Folio = GetString(poClonada["foliodoc"]),
                    Observaciones = observaciones
                };

                string destino = esOrdenDeCompra
                    ? "La orden regreso con el director para que apruebe la edicion."
                    : "El documento reinicia el flujo de aprobacion con su gerente de area.";

                return Json(new
                {
                    success = true,
                    icon = "success",
                    title = "Documento editado correctamente",
                    html = $"Se creo una variacion del documento original, por lo que el folio cambio.<br>" +
                           $"Nuevo folio: <strong>{poClonada["foliodoc"]}</strong><br><br>{destino}" +
                           (documentosHijos.Any() ? "<br><br>Los documentos generados a partir de este fueron cancelados." : ""),
                    documentosHijos = false,
                    showCancelButton = false
                });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error", html = "Error al editar el documento: " + ex.Message, documentosHijos = false, showCancelButton = false });
            }
        }

        private void NotificarEdicionAlDirector(int idOriginal, string folioNuevo)
        {
            string query = "SELECT u.nombreusuario, u.email, " +
                "   (SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = @nombreUsuario) AS editor " +
                "FROM encabezadomov e " +
                "INNER JOIN usuarios u ON u.usuarioid = e.usr6 " +
                "WHERE e.id_encabezado = @id_encabezado";

            var director = RunQuery(query, new Dictionary<string, object>
            {
                ["id_encabezado"] = idOriginal,
                ["nombreUsuario"] = User.Identity.Name
            });

            if (!director.Any()) return;

            var urlBase = $"{Request.Scheme}://{Request.Host}";
            string url = $"{urlBase}/Compras/GestionSolicitudes?id={folioNuevo}";

            _ = SendNotificationInterno(GetString(director[0]["nombreusuario"]), GetString(director[0]["email"]), new
            {
                icon = "warning",
                title = "Orden de compra editada",
                message = $"El usuario {director[0]["editor"]} edito una orden de compra. La variacion {folioNuevo} necesita tu aprobacion nuevamente.",
                buttons = new[]
                {
                    new { text = "Ver Detalles", style = "primary", action = $"window.open('{url}', '_blank')" },
                    new { text = "Más Tarde", style = "secondary", action = (string)null }
                },
                timer = 0,
            });
        }


        #endregion

        #region Fetch para datos complementarios de javascript
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult HasPermission(string permission)
        {
            var userName = User.Identity.Name;

            if (string.IsNullOrEmpty(userName))
                return Json(new { has = false });

            bool has = DoesUserHasRight(userName, permission);
            return Json(new { has });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult HasAnyPermission(string[] permissions)
        {
            var userName = User.Identity.Name;

            bool has = DoesUserHasRight(userName, permissions);
            return Json(new { has });
        }
        #endregion
    }
}