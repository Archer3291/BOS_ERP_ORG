using BOS_ERP.Controllers;
using BOS_ERP.Models;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BOS_ERP.Services
{
    public class ConversacionService : IConversacionService
    {
        private readonly string _connectionString;

        public ConversacionService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ERP_SRS")
                ?? throw new InvalidOperationException("Cadena de conexión no configurada");
        }

        public async Task<int> CrearConversacionAsync(int usuarioId, string? ipOrigen)
        {
            const string sql = @"
                INSERT INTO ia_conversaciones (usuarioid, ip_origen)
                VALUES (@usuarioId, @ipOrigen)
                RETURNING conversacionid";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            cmd.Parameters.AddWithValue("ipOrigen", (object?)ipOrigen ?? DBNull.Value);

            return (int)(await cmd.ExecuteScalarAsync())!;
        }

        public async Task<List<MensajeChat>> ObtenerMensajesAsync(int conversacionId, int usuarioId)
        {
            const string sql = @"
                SELECT mensajeid, conversacionid, usuarioid, rol, contenido, fechacreacion, marcado_revision
                FROM ia_mensajes
                WHERE conversacionid = @conversacionId AND usuarioid = @usuarioId
                ORDER BY fechacreacion ASC";

            var mensajes = new List<MensajeChat>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("conversacionId", conversacionId);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                mensajes.Add(new MensajeChat
                {
                    MensajeId = reader.GetInt64(0),
                    ConversacionId = reader.GetInt32(1),
                    UsuarioId = reader.GetInt32(2),
                    Rol = reader.GetString(3),
                    Contenido = reader.GetString(4),
                    FechaCreacion = reader.GetDateTime(5),
                    MarcadoRevision = reader.GetBoolean(6)
                });
            }

            return mensajes;
        }

        public async Task<long> GuardarMensajeAsync(int conversacionId, int usuarioId, string rol, string contenido, string? modelo = null, int? tiempoRespuestaMs = null)
        {
            const string sql = @"
                INSERT INTO ia_mensajes (conversacionid, usuarioid, rol, contenido, modelo, tiempo_respuesta_ms)
                VALUES (@conversacionId, @usuarioId, @rol, @contenido, @modelo, @tiempoMs)
                RETURNING mensajeid";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("conversacionId", conversacionId);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            cmd.Parameters.AddWithValue("rol", rol);
            cmd.Parameters.AddWithValue("contenido", contenido);
            cmd.Parameters.AddWithValue("modelo", (object?)modelo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("tiempoMs", (object?)tiempoRespuestaMs ?? DBNull.Value);

            return (long)(await cmd.ExecuteScalarAsync())!;
        }

        public async Task ActualizarActividadAsync(int conversacionId)
        {
            const string sql = @"
                UPDATE ia_conversaciones
                SET fechaultimaactividad = now()
                WHERE conversacionid = @conversacionId";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("conversacionId", conversacionId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<Conversacion>> ObtenerConversacionesUsuarioAsync(int usuarioId, int limite = 20)
        {
            const string sql = @"
                SELECT conversacionid, usuarioid, titulo, fechacreacion, fechaultimaactividad, activa
                FROM ia_conversaciones
                WHERE usuarioid = @usuarioId
                ORDER BY fechaultimaactividad DESC
                LIMIT @limite";

            var conversaciones = new List<Conversacion>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            cmd.Parameters.AddWithValue("limite", limite);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                conversaciones.Add(new Conversacion
                {
                    ConversacionId = reader.GetInt32(0),
                    UsuarioId = reader.GetInt32(1),
                    Titulo = reader.IsDBNull(2) ? null : reader.GetString(2),
                    FechaCreacion = reader.GetDateTime(3),
                    FechaUltimaActividad = reader.GetDateTime(4),
                    Activa = reader.GetBoolean(5)
                });
            }

            return conversaciones;
        }
    }
}
