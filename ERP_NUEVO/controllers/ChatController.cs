using BOS_ERP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly IUsuarioService _usuarioService;
        private readonly IConversacionService _conversacionService;

        public ChatController(IUsuarioService usuarioService, IConversacionService conversacionService)
        {
            _usuarioService = usuarioService;
            _conversacionService = conversacionService;
        }

        public async Task<IActionResult> Chat()
        {
            var usuario = await _usuarioService.ObtenerPorNombreUsuarioAsync(User.Identity!.Name!);
            if (usuario == null) return Forbid();

            ViewBag.NombreUsuario = $"{usuario.Nombre} {usuario.Apellido}";

            var conversaciones = await _conversacionService.ObtenerConversacionesUsuarioAsync(usuario.UsuarioId);
            ViewBag.Conversaciones = conversaciones;

            return View();
        }

        // Controllers/AdminChatController.cs
        public class MarcarRevisionRequest
        {
            public long MensajeId { get; set; }
            public bool Marcado { get; set; }
        }

        public async Task<IActionResult> MarcarRevision([FromBody] MarcarRevisionRequest request)
        {
            //const string sql = "UPDATE ia_mensajes SET marcado_revision = @marcado WHERE mensajeid = @mensajeId";

            //await using var conn = new NpgsqlConnection(_connectionString);
            //await conn.OpenAsync();
            //await using var cmd = new NpgsqlCommand(sql, conn);
            //cmd.Parameters.AddWithValue("marcado", request.Marcado);
            //cmd.Parameters.AddWithValue("mensajeId", request.MensajeId);
            //await cmd.ExecuteNonQueryAsync();

            return View();
        }
    }
}
