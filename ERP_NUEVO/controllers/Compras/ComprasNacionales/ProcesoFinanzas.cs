using BOS_ERP.Filters;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Compras
{
    public partial class RequisicionController : Utilities
    {
        #region Paso 5 - Presupuesto (estatus 4 -> 5, o 4 -> 10)

        // Cotizaciones esperando el visto bueno de presupuesto
        public JsonResult GetCotizacionesPresupuesto(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "(em.gen = 'CPN' OR em.gen = 'AF') AND em.estatus_id = 4",
                columnaFecha: "em.fch3");
        }

        // Rechazadas por presupuesto: quedan retenidas hasta que finanzas decida
        // si se libera (vuelve al flujo) o se cancela en definitiva.
        public JsonResult GetCotizacionesRetenidas(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "(em.gen = 'CPN' OR em.gen = 'AF') AND em.estatus_id = 10",
                columnaFecha: "em.fch4");
        }

        // Detalle de la cotizacion que presupuesto tiene que autorizar.
        // En estatus 4 y 10 el documento aun no tiene impuestos ni archivos de
        // proveedor: esos se capturan hasta el paso 6, por eso aqui solo viajan
        // el encabezado, las partidas elegidas y las opciones que se descartaron.
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetCotizacionPresupuestoData(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    ["id"] = Convert.ToInt32(fc["id"].ToString())
                };

                string query = "SELECT em.id_encabezado, em.estatus_id, em.encabezados_padre, em.variacion, " +
                    "   em.coment_aut AS observaciones, em.coment1 AS motivo, em.usr_dep AS area_solicitante, " +
                    "   COALESCE(em.sub, em.imp) AS subtotal, COALESCE(em.dto, 0) AS descuento, em.imp AS total, " +
                    "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                    "   em.fch3 AS fecha_cotizacion, " +
                    "   ac.nombre AS centro_costos, " +
                    "   u0.nombre || ' ' || u0.apellido AS solicitante, " +
                    "   u1.nombre || ' ' || u1.apellido AS gerente, " +
                    "   u2.nombre || ' ' || u2.apellido AS comprador " +
                    "FROM encabezadomov em " +
                    "LEFT JOIN areas ac ON ac.areaid = em.centro_costos " +
                    "LEFT JOIN usuarios u0 ON u0.usuarioid = em.usr0 " +
                    "LEFT JOIN usuarios u1 ON u1.usuarioid = em.usr1 " +
                    "LEFT JOIN usuarios u2 ON u2.usuarioid = em.usr2 " +
                    "WHERE em.id_encabezado = @id AND em.estatus_id IN (4, 10)";

                var encabezado = RunQuery(query, parameters);

                if (!encabezado.Any())
                {
                    return Json(new
                    {
                        success = false,
                        error = "El documento ya no esta pendiente de presupuesto. Actualiza la tabla."
                    });
                }

                query = "SELECT p.id_partidas, p.cve_prod, p.descr_prod, p.ud, p.cant_ud, p.pv_prod, " +
                    "   p.refe, p.cve_vdr_cpr, COALESCE(p.dto1, 0) AS descuento, p.imp_part, " +
                    "   ROUND(COALESCE(p.imp_part, 0)::numeric * COALESCE(p.dto1, 0)::numeric / 100, 2) AS importeDescuento, " +
                    "   ROUND(COALESCE(p.imp_part, 0)::numeric * (1 - COALESCE(p.dto1, 0)::numeric / 100), 2) AS totalDescuento " +
                    "FROM partidasdoc p " +
                    "WHERE p.encabezado_id = @id " +
                    "  AND p.variacion = (SELECT MAX(variacion) FROM partidasdoc WHERE encabezado_id = @id) " +
                    "ORDER BY p.id_partidas";
                var partidas = RunQuery(query, parameters);

                // Las opciones descartadas viven en el documento padre (la cotizacion de compras)
                parameters["padre"] = GetInt(encabezado[0]["encabezados_padre"]);
                query = "SELECT po.partidas_id, po.proveedor_nombre, po.producto, po.descripcion, po.unidad, " +
                    "   po.cantidad, po.precio, COALESCE(po.descuento, 0) AS descuento, " +
                    "   ROUND((po.cantidad * po.precio)::numeric * (1 - COALESCE(po.descuento, 0)::numeric / 100), 2) AS total, " +
                    "   po.ruta, po.uuid, po.extencion, po.nombre_original " +
                    "FROM productos_opciones po " +
                    "WHERE po.encabezado_id IN (@id, @padre) " +
                    "ORDER BY po.partidas_id, po.precio";
                var opciones = RunQuery(query, parameters);

                return Json(new
                {
                    success = true,
                    returnResult = new
                    {
                        requisicion = encabezado[0],
                        partidas,
                        opciones
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al consultar la cotizacion: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AreaAuthorize(new string[] { "Administración y Finanzas" }, "ERP_SRS")]
        [AuditAction(Modulo = "Administración y Finanzas", Accion = "Aprobar presupuesto de cotizacion")]
        public JsonResult AprobarPresupuesto(IFormCollection fc)
        {
            return ResolverPresupuesto(fc, estatusDestino: 5,
                titulo: "Presupuesto aprobado",
                mensaje: "La cotizacion con el folio {0} fue aprobada por presupuesto y pasa a Compras para definir el metodo de pago.",
                icono: "success");
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AreaAuthorize(new string[] { "Administración y Finanzas" }, "ERP_SRS")]
        [AuditAction(Modulo = "Administración y Finanzas", Accion = "Rechazar presupuesto de cotizacion")]
        public JsonResult RechazarPresupuesto(IFormCollection fc)
        {
            return ResolverPresupuesto(fc, estatusDestino: 10,
                titulo: "Cotizacion retenida por presupuesto",
                mensaje: "La cotizacion con el folio {0} fue retenida por presupuesto.",
                icono: "warning");
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AreaAuthorize(new string[] { "Administración y Finanzas" }, "ERP_SRS")]
        [AuditAction(Modulo = "Administración y Finanzas", Accion = "Cancelar cotizacion retenida")]
        public JsonResult CancelarPorPresupuesto(IFormCollection fc)
        {
            return ResolverPresupuesto(fc, estatusDestino: 27,
                titulo: "Cotizacion cancelada",
                mensaje: "La cotizacion con el folio {0} fue cancelada en definitiva por presupuesto.",
                icono: "error");
        }

        // Las tres acciones de presupuesto solo cambian el estatus, dejan la firma
        // de finanzas y notifican a los involucrados: lo unico que varia es el destino.
        private JsonResult ResolverPresupuesto(IFormCollection fc, int estatusDestino,
            string titulo, string mensaje, string icono)
        {
            try
            {
                int idEncabezado = Convert.ToInt32(fc["id_encabezado"].ToString());
                string comentario = GetString(fc["comentario"].ToString());

                var parameters = new Dictionary<string, object>
                {
                    ["id_encabezado"] = idEncabezado,
                    ["estatus"] = estatusDestino,
                    ["usuario"] = GetUserId(User.Identity.Name),
                    ["fecha"] = DateTime.Now,
                    ["comentario"] = comentario
                };

                string query = "UPDATE encabezadomov " +
                    "SET estatus_id = @estatus, usr4 = @usuario, fch4 = @fecha, " +
                    "    coment1 = COALESCE(NULLIF(@comentario, ''), coment1) " +
                    "WHERE id_encabezado = @id_encabezado";
                RunUpdate(query, parameters);

                query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                    "   u0.nombreusuario AS alias_solicitante, u0.email AS email_solicitante, " +
                    "   u2.nombreusuario AS alias_comprador, u2.email AS email_comprador " +
                    "FROM encabezadomov em " +
                    "LEFT JOIN usuarios u0 ON u0.usuarioid = em.usr0 " +
                    "LEFT JOIN usuarios u2 ON u2.usuarioid = em.usr2 " +
                    "WHERE em.id_encabezado = @id_encabezado";
                var doc = RunQuery(query, new Dictionary<string, object> { ["id_encabezado"] = idEncabezado })[0];

                string folio = GetString(doc["folio"]);
                string texto = string.Format(mensaje, folio);

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string url = $"{urlBase}/Compras/GestionSolicitudes?id={folio}";

                foreach (var destinatario in new[] { ("alias_comprador", "email_comprador"), ("alias_solicitante", "email_solicitante") })
                {
                    string alias = GetString(doc[destinatario.Item1]);
                    string correo = GetString(doc[destinatario.Item2]);
                    if (string.IsNullOrWhiteSpace(alias)) continue;

                    _ = SendNotificationInterno(alias, correo, new
                    {
                        icon = icono,
                        title = titulo,
                        message = texto + (string.IsNullOrWhiteSpace(comentario) ? "" : $"<br><br>Motivo: {comentario}"),
                        buttons = new[]
                        {
                            new { text = "Ver Detalles", style = "primary", action = $"window.open('{url}', '_blank')" },
                            new { text = "Más Tarde", style = "secondary", action = (string)null }
                        },
                        timer = 0,
                    });
                }

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = idEncabezado,
                    Folio = folio,
                    Observaciones = comentario
                };

                return Json(new { success = true, icon = icono, title = titulo, html = texto });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, icon = "error", title = "Ocurrio un error", html = ex.Message });
            }
        }

        #endregion
    }
}
