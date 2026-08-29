using BOS_ERP.Controllers;
using Npgsql;
using System;

public static class LogErrorHelper
{
    public static void RegistrarLog(string modulo, string uuid, string mensaje, string usuario = null, string nivel = "ERROR")
    {
        try
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                string sql = @"
                    INSERT INTO log_sistema (modulo, uuid, mensaje, usuario, nivel, ip_origen)
                    VALUES (@modulo, @uuid, @mensaje, @usuario, @nivel, @ip_origen);";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@modulo", (object)modulo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@uuid", (object)uuid ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@mensaje", (object)mensaje ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usuario", (object)(usuario ?? Environment.UserName) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@nivel", (object)(nivel ?? "INFO"));
                    cmd.Parameters.AddWithValue("@ip_origen", (object)ObtenerIP() ?? DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }
        catch
        {
            // Si incluso el log falla, no queremos que rompa la app.
            // Opcionalmente, podrías escribir a un archivo de texto como fallback.
        }
    }

    private static string ObtenerIP()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch { }
        return "Desconocida";
    }
}