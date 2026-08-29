using BOS_ERP.Controllers;
using BOS_ERP.Models;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BOS_ERP.Services
{
    public class UsuarioService : IUsuarioService
    {
        private readonly string _connectionString;

        public UsuarioService(IConfiguration configuration)
        {
            // Ajusta el nombre de la cadena de conexión a como la tengas configurada
            _connectionString = configuration.GetConnectionString("ERP_SRS")
                ?? throw new InvalidOperationException("Cadena de conexión no configurada");
        }

        public async Task<UsuarioActual?> ObtenerPorNombreUsuarioAsync(string nombreUsuario)
        {
            const string sql = @"
                SELECT usuarioid, nombre, apellido, nombreusuario, rolid
                FROM usuarios
                WHERE nombreusuario = @nombreUsuario AND activo = true";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("nombreUsuario", nombreUsuario);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UsuarioActual
                {
                    UsuarioId = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    Apellido = reader.GetString(2),
                    NombreUsuario = reader.GetString(3),
                    RolId = reader.GetInt32(4)
                };
            }

            return null;
        }
    }
}
