using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers.Soporte
{
    public class BuscarTicketsController : Utilities
    {
        [Authorize]
        public IActionResult EnviarTicket()
        {
            return View();
        }
        /// <summary>
        /// Busca un ticket del usuario autenticado por su folio.
        ///
        /// El correo del formulario ya no decide de quién es el ticket: antes bastaba con
        /// escribir el correo de otra persona para abrir sus tickets, y los mensajes de
        /// error confirmaban qué correos existían en el sistema. Se conserva el parámetro
        /// porque la vista lo sigue enviando, pero sólo se valida contra el propio usuario.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public JsonResult Buscar(string ticketId, string email)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return Json(new { success = false, error = "Ticket ID es requerido." });
            }

            try
            {
                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                if (usuarioId <= 0)
                {
                    return Json(new { success = false, error = "Tu sesión expiró. Vuelve a iniciar sesión." });
                }

                var tkt = SoporteAuthz.CargarPorFolio(ticketId);

                // Mismo mensaje para "no existe" y "no es tuyo": si se distinguieran, el
                // buscador serviría para averiguar qué folios existen.
                if (!SoporteAuthz.PuedeVer(HttpContext.Session, usuarioId, tkt))
                {
                    return Json(new { success = false, error = "Ticket no encontrado." });
                }

                // Devolver URL para redireccionar al detalle del ticket
                string url = Url.Action("Ticket", "Soporte", new { id = ticketId });
                return Json(new { success = true, redirectUrl = url });

            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/BuscarTicket", "Error al buscar el ticket");
            }
        }
        /// <summary>
        /// Resuelve un folio a la vista de gestión. La usa la tabla de administración,
        /// así que exige poder gestionar el ticket, no sólo verlo.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public JsonResult BuscarT(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return Json(new { success = false, error = "Ticket ID requerido." });
            }

            try
            {
                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                if (usuarioId <= 0)
                {
                    return Json(new { success = false, error = "Tu sesión expiró. Vuelve a iniciar sesión." });
                }

                var tkt = SoporteAuthz.CargarPorFolio(ticketId);
                if (!SoporteAuthz.PuedeGestionar(HttpContext.Session, usuarioId, tkt))
                {
                    return Json(new { success = false, error = "Ticket no encontrado." });
                }

                // Devolver URL para redireccionar al detalle del ticket
                string url = Url.Action("TicketT", "Soporte", new { id = ticketId });
                return Json(new { success = true, redirectUrl = url });

            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/BuscarTicket", "Error al buscar el ticket");
            }
        }


        //    public IActionResult Ticket(string id)
        //    {
        //        if (string.IsNullOrWhiteSpace(id))
        //            return RedirectToAction("EnviarTicket");

        //        var parameters = new Dictionary<string, object>
        //{
        //    { "folio_tkt", id }
        //};

        //        var ticketQuery = "SELECT * FROM tkts WHERE folio_tkt = @folio_tkt";
        //        var ticketResult = RunQuery(ticketQuery, parameters);

        //        if (ticketResult == null || ticketResult.Count == 0)
        //            return RedirectToAction("EnviarTicket");

        //        ViewBag.Ticket = ticketResult[0];
        //        return View("Ticket"); // Renderiza la vista Ticket.cshtml
        //    }

    }
}