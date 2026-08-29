using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers.Soporte
{
    public class RespuestaController : Utilities
    {
        private readonly IWebHostEnvironment _env;

        public RespuestaController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> CrearRespuesta(SeguimientoTicket respuesta, List<IFormFile> archivos)
        {
            // Validaciones básicas
            if (respuesta == null)
            {
                return Json(new { success = false, message = "Datos inválidos." });
            }

            if (respuesta.IdTkt <= 0)
            {
                return Json(new { success = false, message = "ID del ticket inválido." });
            }

            if (string.IsNullOrWhiteSpace(respuesta.Coment))
            {
                return Json(new { success = false, message = "El comentario no puede estar vacío." });
            }

            // El autor sale de la sesión, nunca del formulario: antes se usaba
            // respuesta.IdUsr tal cual llegaba del cliente, así que bastaba con editar el
            // FormData para publicar una respuesta a nombre de otra persona.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (usuarioId <= 0)
            {
                return Json(new { success = false, message = "Tu sesión expiró. Vuelve a iniciar sesión." });
            }

            var tkt = SoporteAuthz.CargarPorId(respuesta.IdTkt);
            if (!tkt.Existe)
            {
                return Json(new { success = false, message = "El ticket no existe." });
            }

            if (!SoporteAuthz.PuedeVer(HttpContext.Session, usuarioId, tkt))
            {
                return Json(new { success = false, message = "No tienes permisos sobre este ticket." });
            }

            // Los adjuntos del hilo se validan antes de insertar la respuesta, para no
            // dejar un comentario guardado y los archivos rechazados.
            string errorAdjuntos = SoporteAdjuntos.Validar(archivos);
            if (errorAdjuntos != null)
            {
                return Json(new { success = false, message = errorAdjuntos });
            }

            // Crear parámetros
            var parameters = new Dictionary<string, object>
            {
                { "id_tkt", respuesta.IdTkt },
                { "id_usr", usuarioId },
                { "coment", respuesta.Coment }
            };

            // RETURNING para conocer el id del seguimiento recién creado: es lo que liga
            // cada adjunto con su respuesta del hilo (tkts_adj.id_seg_tkts).
            string insertarRespuesta =
                "INSERT INTO seg_tkts (id_tkt, id_usr, coment) " +
                "VALUES (@id_tkt, @id_usr, @coment) " +
                "RETURNING id_seg_tkts";

            try
            {
                using var conn = AbrirConexion();
                using var tx = conn.BeginTransaction();

                try
                {
                    var filas = RunQuery(insertarRespuesta, parameters, false, conn, tx);
                    if (filas.Count == 0)
                    {
                        tx.Rollback();
                        return Json(new { success = false, message = "No se pudo guardar la respuesta." });
                    }

                    int idSeguimiento = Convert.ToInt32(filas[0]["id_seg_tkts"]);

                    // Antes el formulario permitía adjuntar archivos y el JS los mandaba,
                    // pero la acción no tenía parámetro para recibirlos: se descartaban en
                    // silencio y el usuario veía "Respuesta enviada correctamente".
                    await SoporteAdjuntos.GuardarAsync(
                        this, _env.ContentRootPath, archivos, respuesta.IdTkt, idSeguimiento, conn, tx);

                    tx.Commit();
                    return Json(new { success = true, message = "Respuesta creada correctamente." });
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Manejo de errores
                return ErrorConTicket(ex, "Soporte/Respuesta", "Error al guardar la respuesta");
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult EditarRespuesta(SeguimientoTicket respuesta)
        {
            // Validaciones
            if (respuesta == null)
            {
                return Json(new { success = false, message = "Datos inválidos." });
            }

            if (respuesta.IdSegTkts <= 0)
            {
                return Json(new { success = false, message = "ID de seguimiento inválido." });
            }

            if (string.IsNullOrWhiteSpace(respuesta.Coment))
            {
                return Json(new { success = false, message = "El comentario no puede estar vacío." });
            }

            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (usuarioId <= 0)
            {
                return Json(new { success = false, message = "Tu sesión expiró. Vuelve a iniciar sesión." });
            }

            // Crear parámetros
            var parameters = new Dictionary<string, object>
            {
                { "id_seg_tkts", respuesta.IdSegTkts },
                { "coment", respuesta.Coment },
                { "id_usr", usuarioId }
            };

            // El id_usr del WHERE es lo que impide reescribir el comentario de otra
            // persona: antes el UPDATE sólo filtraba por id_seg_tkts, así que cualquier
            // usuario con sesión podía editar cualquier respuesta del sistema.
            // RETURNING permite saber si el UPDATE encontró fila: RunUpdate no devuelve
            // el número de filas afectadas (RunUpdateWrapper retorna 1 siempre).
            string actualizarRespuesta =
                "UPDATE seg_tkts SET coment = @coment " +
                "WHERE id_seg_tkts = @id_seg_tkts AND id_usr = @id_usr " +
                "RETURNING id_seg_tkts";

            try
            {
                // Si el WHERE no encaja (respuesta ajena o inexistente) no hay filas
                // afectadas: hay que avisar en vez de reportar un éxito que no ocurrió.
                var filas = RunQuery(actualizarRespuesta, parameters);
                if (filas.Count == 0)
                {
                    return Json(new { success = false, message = "No puedes editar esta respuesta." });
                }

                return Json(new { success = true, message = "Respuesta actualizada correctamente." });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Respuesta", "Error al actualizar la respuesta");
            }
        }

    }
}