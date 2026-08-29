// Controllers/AdminChatController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace TuApp.Controllers
{
    [Authorize(Roles = "1")] // ajusta al rolid de administrador en tu tabla roles
    public class AdminChatController : Controller
    {
        private readonly string _connectionString;

        public AdminChatController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        // Vista con todos los mensajes, filtrable por usuario y fecha
        public async Task<IActionResult> Index(int? usuarioId, DateTime? desde, DateTime? hasta, bool soloRevision = false)
        {
            var sql = @"
                SELECT m.mensajeid, m.conversacionid, u.nombre, u.apellido, m.rol, m.contenido, m.fechacreacion, m.marcado_revision
                FROM ia_mensajes m
                JOIN usuarios u ON u.usuarioid = m.usuarioid
                WHERE 1=1";

            if (usuarioId.HasValue) sql += " AND m.usuarioid = @usuarioId";
            if (desde.HasValue) sql += " AND m.fechacreacion >= @desde";
            if (hasta.HasValue) sql += " AND m.fechacreacion <= @hasta";
            if (soloRevision) sql += " AND m.marcado_revision = true";

            sql += " ORDER BY m.fechacreacion DESC LIMIT 500";

            var resultados = new List<dynamic>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);

            if (usuarioId.HasValue) cmd.Parameters.AddWithValue("usuarioId", usuarioId.Value);
            if (desde.HasValue) cmd.Parameters.AddWithValue("desde", desde.Value);
            if (hasta.HasValue) cmd.Parameters.AddWithValue("hasta", hasta.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                resultados.Add(new
                {
                    MensajeId = reader.GetInt64(0),
                    ConversacionId = reader.GetInt32(1),
                    Usuario = $"{reader.GetString(2)} {reader.GetString(3)}",
                    Rol = reader.GetString(4),
                    Contenido = reader.GetString(5),
                    Fecha = reader.GetDateTime(6),
                    MarcadoRevision = reader.GetBoolean(7)
                });
            }

            return View(resultados);
        }

        [HttpPost]
        public async Task<IActionResult> MarcarRevision(long mensajeId, bool marcado)
        {
            const string sql = "UPDATE ia_mensajes SET marcado_revision = @marcado WHERE mensajeid = @mensajeId";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("marcado", marcado);
            cmd.Parameters.AddWithValue("mensajeId", mensajeId);
            await cmd.ExecuteNonQueryAsync();

            return Ok();
        }
    }
}