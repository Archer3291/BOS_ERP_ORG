using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers.Soporte
{
    public class EstatusController : Utilities
    {
        /// <summary>stat_tkt: 4 es "Resuelto", el estado que dispara el aviso por correo.</summary>
        private const int EstadoResuelto = 4;

        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;
        public EstatusController(BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            correoHelper = correoHelperService;
        }

        /// <summary>
        /// Aviso interno (la campana / toast que sirve SignalR) al creador del ticket
        /// cuando le mueven el estado.
        ///
        /// Se salta cuando el cambio lo hizo el propio creador: no tiene caso avisarle de
        /// lo que acaba de pulsar. Todo va en try/catch porque el estado ya se guardo:
        /// un aviso perdido no puede convertir la operacion en un error.
        /// </summary>
        private void NotificarCambioEstadoAlCreador(TicketPermisos tkt, int estadoNuevo, int autorId)
        {
            try
            {
                if (tkt == null || tkt.IdCreador <= 0 || tkt.IdCreador == autorId)
                    return;

                var datos = RunQuery(
                    "SELECT u.nombreusuario, u.email, t.tit, " +
                    "       (SELECT n FROM stat_tkt WHERE id_stat_tkt = @estado) AS estado " +
                    "FROM tkts t " +
                    "INNER JOIN usuarios u ON u.usuarioid = t.id_usr " +
                    "WHERE t.id_tkts = @id_tkt",
                    new Dictionary<string, object>
                    {
                        { "estado", estadoNuevo },
                        { "id_tkt", tkt.IdTkt }
                    });

                if (datos.Count == 0)
                    return;

                string correo = datos[0]["email"]?.ToString();
                string usuario = datos[0]["nombreusuario"]?.ToString();
                string titulo = datos[0]["tit"]?.ToString();
                string estado = datos[0]["estado"]?.ToString();

                // El hub agrupa por correo: sin el no hay a quien mandarle nada.
                if (string.IsNullOrWhiteSpace(correo))
                    return;

                string urlTicket = $"{Request.Scheme}://{Request.Host}/Soporte/Ticket/{tkt.Folio}";
                bool esResuelto = estadoNuevo == EstadoResuelto;

                // Mismo contrato que consume _Layout ('NotificacionInterna'). En "action"
                // no entra texto del usuario: el cliente lo evalua con new Function(...).
                _ = SendNotificationInterno(usuario, correo, new
                {
                    icon = esResuelto ? "success" : "info",
                    title = esResuelto ? "Tu ticket fue resuelto" : "Tu ticket cambio de estado",
                    message = esResuelto
                        ? $"El ticket {tkt.Folio} ({titulo}) se marco como resuelto."
                        : $"El ticket {tkt.Folio} ({titulo}) paso a estado: {estado}.",
                    folio = tkt.Folio,
                    buttons = new[]
                    {
                        new { text = "Ver ticket", style = "primary", action = $"window.open('{urlTicket}', '_blank')" },
                        new { text = "Mas Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Soporte] No se pudo avisar del cambio de estado del ticket {tkt?.Folio}: {ex.Message}");
            }
        }

        /// <summary>
        /// Preámbulo común de todas las acciones que mueven un ticket: resuelve el usuario
        /// de la sesión, comprueba que el ticket exista y que el usuario tenga permiso.
        ///
        /// Antes ninguna de estas acciones validaba nada más allá de "hay sesión": con el
        /// id de un ticket ajeno cualquier empleado podía cerrarlo o reasignarlo.
        /// </summary>
        private (bool Ok, JsonResult Error, int UsuarioId, TicketPermisos Tkt) Autorizar(
            int idTkt, bool requiereGestion = true)
        {
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (usuarioId <= 0)
            {
                return (false, Rechazo("Tu sesión expiró. Vuelve a iniciar sesión.", 401), 0, null);
            }

            var tkt = SoporteAuthz.CargarPorId(idTkt);
            if (!tkt.Existe)
            {
                return (false, Rechazo("El ticket no existe.", 404), usuarioId, null);
            }

            bool permitido = requiereGestion
                ? SoporteAuthz.PuedeGestionar(HttpContext.Session, usuarioId, tkt)
                : SoporteAuthz.PuedeVer(HttpContext.Session, usuarioId, tkt);

            if (!permitido)
            {
                return (false, Rechazo("No tienes permisos sobre este ticket.", 403), usuarioId, tkt);
            }

            return (true, null, usuarioId, tkt);
        }

        /// <summary>
        /// Correos de las personas a las que les interesa el ticket: quien lo creo y quien
        /// lo tiene asignado. Devuelve cada direccion una sola vez y omite las vacias.
        /// </summary>
        private List<string> DestinatariosAviso(TicketPermisos tkt)
        {
            var ids = new List<int>();
            if (tkt.IdCreador > 0) ids.Add(tkt.IdCreador);
            if (tkt.IdAsignado > 0 && tkt.IdAsignado != tkt.IdCreador) ids.Add(tkt.IdAsignado);

            if (ids.Count == 0) return new List<string>();

            var filas = RunQuery(
                "SELECT email FROM usuarios WHERE usuarioid = ANY(@ids) AND email IS NOT NULL AND email <> ''",
                new Dictionary<string, object> { { "ids", ids.ToArray() } });

            return filas
                .Select(f => f["email"]?.ToString())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private JsonResult Rechazo(string mensaje, int statusCode)
        {
            var json = Json(new { success = false, message = mensaje });
            json.StatusCode = statusCode;
            return json;
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> CambiarEstadoTicket(int idTkt, int? nuevoEstado = null)
        {
            if (idTkt <= 0)
            {
                return Json(new { success = false, message = "ID de ticket inválido." });
            }

            int estadoFinal = nuevoEstado ?? EstadoResuelto; // Si no se especifica, se resuelve

            var parameters = new Dictionary<string, object>
            {
                { "id_tkts", idTkt },
                { "nuevoEstado", estadoFinal }
            };

            string actualizarEstado = "UPDATE tkts SET id_stat_tkt = @nuevoEstado WHERE id_tkts = @id_tkts";

            // Basta con poder ver el ticket: la vista del usuario final ofrece los botones
            // "Marcar como Resuelto" y "Reabrir", y exigir permiso de gestión le daría 403
            // al creador sobre su propio ticket. Reclasificar (prioridad, categoría,
            // responsable) sí queda reservado al staff.
            var auth = Autorizar(idTkt, requiereGestion: false);
            if (!auth.Ok) return auth.Error;

            try
            {
                RunUpdate(actualizarEstado, parameters);

                // El estado anterior sale del ticket tal como estaba antes del UPDATE.
                // Antes se pasaba estadoFinal en los dos parametros, asi que
                // hst_est.id_stat_tkt_ant guardaba el estado nuevo y el historial nunca
                // pudo mostrar una transicion real.
                RegistrarCambioEstado(idTkt, auth.Tkt.IdEstado, estadoFinal, auth.UsuarioId, null, null);

                // Aviso interno al creador en CUALQUIER cambio de estado; el correo solo
                // cuando queda resuelto (mas abajo).
                NotificarCambioEstadoAlCreador(auth.Tkt, estadoFinal, auth.UsuarioId);

                if (estadoFinal == EstadoResuelto)
                {
                    var request = HttpContext.Request;

                    string baseUrl = $"{request.Scheme}://{request.Host}";
                    //string baseUrl = $"{Request.Url.Scheme}://{Request.Url.Authority}";
                    string mensajeHtml = $@"
        <table width='100%' cellpadding='0' cellspacing='0' style='font-family: Arial, sans-serif; background-color: #f4f4f4; padding: 20px;'>
            <tr>
                <td align='center'>
                    <table width='600' cellpadding='0' cellspacing='0' style='background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,0.1);'>
                        <tr style='background-color: #004080; color: #ffffff;'>
                            <td style='padding: 20px; text-align: center;'>
                                <span style='font-size: 22px;'>✅ Ticket resuelto</span>
                            </td>
                        </tr>
                        <tr>
                            <td style='padding: 30px;'>
                                <p style='font-size: 16px; color: #333;'>Hola,</p>
                                <p style='font-size: 16px; color: #333;'>El ticket con folio <strong>#{auth.Tkt.Folio}</strong> ha sido marcado como <strong>resuelto</strong>.</p>
                                <p style='font-size: 15px; color: #555;'>Si consideras que el problema no ha sido completamente solucionado, puedes reabrir el ticket desde el sistema o contactar al área de soporte.</p>

                                <table cellpadding='0' cellspacing='0' border='0' align='center' style='margin: 30px auto 0 auto;'>
                                    <tr bgcolor='#004080'>
                                        <td style='background-color: #004080; border-radius: 5px; text-align: center;'>
                                            <a href='{baseUrl}/Soporte/Ticket/{auth.Tkt.Folio}'
                                               style='display: inline-block; padding: 12px 24px; color: #ffffff; font-size: 16px;
                                                      text-decoration: none; font-weight: bold; font-family: Arial, sans-serif;'>
                                                🔎 Ver ticket
                                            </a>
                                        </td>
                                    </tr>
                                </table>

                                <p style='margin-top: 30px; font-size: 14px; color: #888;'>Este correo es una notificación automática. No respondas a este mensaje.</p>
                            </td>
                        </tr>
                        <tr style='background-color: #f0f0f0;'>
                            <td style='padding: 20px; text-align: center; font-size: 12px; color: #999;'>
                                © {DateTime.Now.Year} Sellos y Retenes. Todos los derechos reservados.
                            </td>
                        </tr>
                    </table>
                </td>
            </tr>
        </table>";

                    // El aviso va a quien le importa el ticket: quien lo creo y quien lo
                    // tiene asignado. Antes salia siempre al mismo buzon fijo, asi que el
                    // creador nunca se enteraba de que su ticket se habia resuelto.
                    foreach (var destinatario in DestinatariosAviso(auth.Tkt))
                    {
                        try
                        {
                            await correoHelper.EnviarCorreoNotificacionAsync(
                                destinatario,
                                $"Ticket {auth.Tkt.Folio} resuelto - Sellos y Retenes",
                                mensajeHtml
                            );
                        }
                        catch (Exception exCorreo)
                        {
                            // El ticket ya se resolvio: un fallo de correo se registra pero
                            // no convierte la operacion en un error para el usuario.
                            Console.WriteLine($"[Soporte] No se pudo avisar a {destinatario}: {exCorreo.Message}");
                        }
                    }
                }

                return Json(new { success = true, message = "Estado del ticket actualizado correctamente." });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Estatus", "Error al actualizar el estado");
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ReabrirTicket(int idTkt)
        {
            if (idTkt <= 0)
            {
                return Json(new { success = false, message = "ID de ticket inválido." });
            }

            // 1 = Abierto. Antes ponia 5, que el comentario del codigo llamaba
            // "Cancelado / Cerrado sin resolucion" pero en el catalogo stat_tkt es
            // "En progreso": el boton decia reabrir, el mensaje decia cancelar y el
            // ticket acababa en un tercer estado distinto de los dos.
            const int EstadoAbierto = 1;
            int estadoFinal = EstadoAbierto;

            var parameters = new Dictionary<string, object>
    {
        { "id_tkts", idTkt },
        { "nuevoEstado", estadoFinal }
    };

            string actualizarEstado = "UPDATE tkts SET id_stat_tkt = @nuevoEstado WHERE id_tkts = @id_tkts";

            // El creador puede reabrir su propio ticket, así que aquí basta con poder verlo.
            var auth = Autorizar(idTkt, requiereGestion: false);
            if (!auth.Ok) return auth.Error;

            try
            {
                RunUpdate(actualizarEstado, parameters);

                // Registrar cambio sin enviar correo
                RegistrarCambioEstado(idTkt, auth.Tkt.IdEstado, estadoFinal, auth.UsuarioId, null, null);

                // Reabrir tambien mueve el estado: si lo reabrio otro (soporte), al creador
                // le interesa enterarse. Si lo reabrio el mismo, el aviso se descarta solo.
                NotificarCambioEstadoAlCreador(auth.Tkt, estadoFinal, auth.UsuarioId);

                return Json(new { success = true, message = "El ticket fue reabierto correctamente." });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Estatus", "Error al reabrir el ticket");
            }
        }


        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CambiarCategoriaTicket(int idTkt, int nuevaCategoria)
        {
            if (idTkt <= 0 || nuevaCategoria <= 0)
            {
                return Json(new { success = false, message = "ID de ticket o categoría inválidos." });
            }

            var parameters = new Dictionary<string, object>
            {
                { "id_tkts", idTkt },
                { "nuevaCategoria", nuevaCategoria }
            };

            string actualizarCategoria = "UPDATE tkts SET id_cat = @nuevaCategoria WHERE id_tkts = @id_tkts";

            var auth = Autorizar(idTkt);
            if (!auth.Ok) return auth.Error;

            // Mover un ticket de categoría lo saca del alcance de quien lo estaba
            // viendo y lo mete en el de otra área: es la única acción del ticket que
            // cruza esa frontera, y basta para hacerlo desaparecer del tablero de un
            // gerente. Sólo Sistemas.
            if (!SoporteAlcance.VeTodo(HttpContext.Session, auth.UsuarioId))
                return Rechazo("Sólo Sistemas puede cambiar un ticket de categoría.", 403);

            try
            {
                RunUpdate(actualizarCategoria, parameters);
                RegistrarCambioEstado(idTkt, null, null, auth.UsuarioId, nuevaCategoria, null);
                return Json(new { success = true, message = "Categoría del ticket actualizada correctamente." });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Estatus", "Error al actualizar la categoría");
            }
        }
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CambiarPrioridadTicket(int idTkt, int nuevaPrioridad)
        {
            if (idTkt <= 0 || nuevaPrioridad <= 0)
            {
                return Json(new { success = false, message = "ID de ticket o prioridad inválidos." });
            }

            var parameters = new Dictionary<string, object>
            {
                { "id_tkts", idTkt },
                { "nuevaPrioridad", nuevaPrioridad }
            };

            string actualizarPrioridad = "UPDATE tkts SET id_prio = @nuevaPrioridad WHERE id_tkts = @id_tkts";

            var auth = Autorizar(idTkt);
            if (!auth.Ok) return auth.Error;

            try
            {
                RunUpdate(actualizarPrioridad, parameters);
                RegistrarCambioEstado(idTkt, null, null, auth.UsuarioId, null, nuevaPrioridad);
                return Json(new { success = true, message = "Prioridad del ticket actualizada correctamente." });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Estatus", "Error al actualizar la prioridad");
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CambiarResponsable(int idTkt, int nuevoResponsable)
        {
            if (idTkt <= 0 || nuevoResponsable <= 0)
            {
                return Json(new { success = false, message = "ID de ticket o responsable inválidos." });
            }

            // Reasignar es acción de staff: el responsable actual no puede pasarle el
            // ticket a otro por su cuenta.
            var auth = Autorizar(idTkt);
            if (!auth.Ok) return auth.Error;

            if (!SoporteAuthz.PuedeAsignar(HttpContext.Session, auth.UsuarioId))
                return Rechazo("No tienes permisos para reasignar tickets.", 403);

            // Y dentro del staff, sólo la categoría propia: el asignador de Almacén no
            // reparte los tickets de Contabilidad, y el destinatario tiene que ser
            // alguien que de verdad atienda esa categoría. Sin la segunda mitad, el
            // combo se podía saltar mandando el id a mano.
            int idCat = SoporteAlcance.CategoriaDelTicket(idTkt);

            if (!SoporteAlcance.PuedeAsignarEn(HttpContext.Session, auth.UsuarioId, idCat))
                return Rechazo("Este ticket no pertenece a una categoría que tú atiendas.", 403);

            if (!SoporteAlcance.AtiendeCategoria(nuevoResponsable, idCat))
                return Rechazo("Esa persona no atiende la categoría de este ticket.", 400);

            // El UPDATE de tkts y el registro en tkt_asig van juntos dentro de
            // AsignarTicket: antes el primero se aplicaba y el segundo fallaba siempre,
            // dejando la pantalla en error con el responsable ya cambiado.
            var error = AsignarConHistorial(idTkt, nuevoResponsable, auth.UsuarioId);
            if (error != null) return error;

            return Json(new { success = true, message = "Responsable del ticket actualizado correctamente." });
        }


        /// <summary>
        /// Envoltura de <see cref="Utilities.AsignarTicketSoporte"/> que traduce el fallo
        /// a la respuesta JSON que espera el front.
        /// </summary>
        /// <returns>null si todo fue bien; el JsonResult de error si no.</returns>
        private JsonResult AsignarConHistorial(int idTkt, int nuevoResponsable, int asignadorId)
        {
            try
            {
                AsignarTicketSoporte(idTkt, nuevoResponsable, asignadorId);
                return null;
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Soporte/Estatus");
                return Rechazo("Error al asignar responsable.", 500);
            }
        }
    }
}