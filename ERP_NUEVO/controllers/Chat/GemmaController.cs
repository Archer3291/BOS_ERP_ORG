using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using System.Text;
using System.Text.Json;

namespace BOS_ERP.controllers.Chat
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class GemmaController : ControllerBase
    {
        private readonly IGemmaService _gemmaService;
        private readonly IUsuarioService _usuarioService;
        private readonly IConversacionService _conversacionService;
        private readonly ILogger<GemmaController> _logger;

        public GemmaController(
            IGemmaService gemmaService,
            IUsuarioService usuarioService,
            IConversacionService conversacionService,
            ILogger<GemmaController> logger)
        {
            _gemmaService = gemmaService;
            _usuarioService = usuarioService;
            _conversacionService = conversacionService;
            _logger = logger;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ConsultaRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Pregunta))
                return BadRequest(new { error = "La pregunta no puede estar vacía" });

            var usuario = await _usuarioService.ObtenerPorNombreUsuarioAsync(User.Identity!.Name!);
            if (usuario == null)
                return Unauthorized(new { error = "Usuario no encontrado o inactivo" });

            // Crear conversación si no existe una
            var conversacionId = request.ConversacionId
                ?? await _conversacionService.CrearConversacionAsync(usuario.UsuarioId, HttpContext.Connection.RemoteIpAddress?.ToString());

            // Guardar el mensaje del usuario ANTES de llamar al modelo (auditoría, incluso si el modelo falla)
            await _conversacionService.GuardarMensajeAsync(conversacionId, usuario.UsuarioId, "user", request.Pregunta);

            try
            {
                // Reconstruir historial desde BD para mandarlo al modelo (fuente de verdad = BD, no el cliente)
                var historialBd = await _conversacionService.ObtenerMensajesAsync(conversacionId, usuario.UsuarioId);
                var mensajesOllama = historialBd
                    .Select(m => new OllamaMessage { Role = m.Rol, Content = m.Contenido })
                    .ToList();

                var sw = Stopwatch.StartNew();
                var respuesta = await _gemmaService.ConsultarConHistorialAsync(mensajesOllama);
                sw.Stop();

                await _conversacionService.GuardarMensajeAsync(
                    conversacionId, usuario.UsuarioId, "assistant", respuesta,
                    modelo: "gemma4:12b", tiempoRespuestaMs: (int)sw.ElapsedMilliseconds);

                await _conversacionService.ActualizarActividadAsync(conversacionId);

                return Ok(new { respuesta, conversacionId });
            }
            catch (ApplicationException ex)
            {
                _logger.LogError(ex, "Error consultando a Gemma para usuario {UsuarioId}", usuario.UsuarioId);
                return StatusCode(503, new { error = ex.Message, conversacionId });
            }
        }

        [HttpGet("historial/{conversacionId:int}")]
        public async Task<IActionResult> Historial(int conversacionId)
        {
            var usuario = await _usuarioService.ObtenerPorNombreUsuarioAsync(User.Identity!.Name!);
            if (usuario == null)
                return Unauthorized();

            var mensajes = await _conversacionService.ObtenerMensajesAsync(conversacionId, usuario.UsuarioId);
            return Ok(mensajes.Where(m => m.Rol != "system"));
        }

        [HttpGet("conversaciones")]
        public async Task<IActionResult> MisConversaciones()
        {
            var usuario = await _usuarioService.ObtenerPorNombreUsuarioAsync(User.Identity!.Name!);
            if (usuario == null)
                return Unauthorized();

            var conversaciones = await _conversacionService.ObtenerConversacionesUsuarioAsync(usuario.UsuarioId);
            return Ok(conversaciones);
        }

        [HttpGet("health")]
        public async Task<IActionResult> Health()
        {
            var online = await _gemmaService.VerificarEstadoAsync();
            return Ok(new { online });
        }

        [HttpPost("chat/stream")]
        public async Task Stream([FromBody] ConsultaRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Pregunta))
            {
                Response.StatusCode = 400;
                return;
            }

            var usuario = await _usuarioService.ObtenerPorNombreUsuarioAsync(User.Identity!.Name!);
            if (usuario == null)
            {
                Response.StatusCode = 401;
                return;
            }

            var conversacionId = request.ConversacionId
                ?? await _conversacionService.CrearConversacionAsync(
                    usuario.UsuarioId, HttpContext.Connection.RemoteIpAddress?.ToString());

            await _conversacionService.GuardarMensajeAsync(
                conversacionId, usuario.UsuarioId, "user", request.Pregunta);

            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("X-Accel-Buffering", "no");

            await Response.WriteAsync($"event: conversacion\ndata: {conversacionId}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            var historialBd = await _conversacionService.ObtenerMensajesAsync(conversacionId, usuario.UsuarioId);
            var mensajesOllama = historialBd
                .Select(m => new OllamaMessage { Role = m.Rol, Content = m.Contenido })
                .ToList();

            var respuestaCompleta = new StringBuilder();
            var sw = Stopwatch.StartNew();

            try
            {
                await foreach (var fragmento in _gemmaService.ConsultarConHistorialStreamAsync(mensajesOllama, ct))
                {
                    respuestaCompleta.Append(fragmento);
                    var payload = JsonSerializer.Serialize(new { texto = fragmento });
                    await Response.WriteAsync($"data: {payload}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }

                sw.Stop();

                await _conversacionService.GuardarMensajeAsync(
                    conversacionId, usuario.UsuarioId, "assistant", respuestaCompleta.ToString(),
                    modelo: "gemma4:12b", tiempoRespuestaMs: (int)sw.ElapsedMilliseconds);

                await _conversacionService.ActualizarActividadAsync(conversacionId);

                await Response.WriteAsync("event: done\ndata: {}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (respuestaCompleta.Length > 0)
                {
                    await _conversacionService.GuardarMensajeAsync(
                        conversacionId, usuario.UsuarioId, "assistant",
                        respuestaCompleta.ToString() + " [interrumpido]",
                        modelo: "gemma4:12b", tiempoRespuestaMs: (int)sw.ElapsedMilliseconds);
                }
            }
            catch (ApplicationException ex)
            {
                _logger.LogError(ex, "Error en stream de Gemma para usuario {UsuarioId}", usuario.UsuarioId);
                var payload = JsonSerializer.Serialize(new { error = ex.Message });
                await Response.WriteAsync($"event: error\ndata: {payload}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
    }
}
