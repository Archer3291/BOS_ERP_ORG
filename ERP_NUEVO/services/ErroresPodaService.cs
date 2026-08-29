using BOS_ERP.Controllers;
using Npgsql;

namespace BOS_ERP.Services
{
    /// <summary>
    /// Poda de tkt_error_detalle.
    ///
    /// POR QUÉ EXISTE DESDE EL DÍA UNO
    /// Cada ocurrencia guarda un stack completo en jsonb: entre 3 y 5 KB por fila.
    /// A 500 errores diarios son ~700 MB al año, creciendo sin techo. Y montar la
    /// poda después, sobre millones de filas, obliga a borrar por lotes, aguantar el
    /// bloat y correr un VACUUM; montarla con la tabla vacía es gratis.
    ///
    /// QUÉ SE CONSERVA
    /// La HUELLA nunca se borra: son cuatro campos y es lo que sostiene la
    /// deduplicación, el contador histórico y el enlace con el ticket. Lo que se
    /// poda es el detalle, que es el que pesa.
    /// </summary>
    public sealed class ErroresPodaService
    {
        /// <summary>
        /// Ocurrencias que se conservan por huella. Con 50 se diagnostica cualquier
        /// cosa: si hace falta la número 300 para entender un error, el problema no
        /// es la retención.
        /// </summary>
        private const int PorHuella = 50;

        /// <summary>
        /// Huellas sin actividad en este plazo: se borra su detalle completo. El
        /// contador y el enlace al ticket se quedan.
        /// </summary>
        private const int DiasInactividad = 90;

        private readonly Utilities _utils;
        private readonly IConfiguration _cfg;

        public ErroresPodaService()
        {
            _utils = new Utilities(true);
            _cfg = _utils._configuration;
        }

        public void Podar()
        {
            try
            {
                using var conn = new NpgsqlConnection(_cfg.GetConnectionString("ERP_SRS"));
                conn.Open();

                // 1) Detalle de huellas inactivas. Va primero porque suele ser el
                //    borrado grande y deja menos trabajo al de abajo.
                int inactivas = Borrar(conn,
                    "DELETE FROM tkt_error_detalle d " +
                    "USING tkt_error_huella h " +
                    "WHERE h.id_huella = d.id_huella " +
                    "  AND h.ultima_vez < now() - make_interval(days => @dias)",
                    new Dictionary<string, object> { { "dias", DiasInactividad } });

                // 2) De cada huella viva, sólo las últimas N.
                //
                //    ROW_NUMBER y no "id_detalle NOT IN (SELECT ... LIMIT n)": esa
                //    forma sólo mira una huella a la vez y habría que recorrerlas en
                //    un bucle desde C#, con una consulta por huella.
                int excedente = Borrar(conn,
                    "DELETE FROM tkt_error_detalle " +
                    "WHERE id_detalle IN ( " +
                    "    SELECT id_detalle FROM ( " +
                    "        SELECT id_detalle, " +
                    "               ROW_NUMBER() OVER (PARTITION BY id_huella ORDER BY fch DESC) AS n " +
                    "        FROM tkt_error_detalle " +
                    "    ) t WHERE t.n > @porHuella " +
                    ")",
                    new Dictionary<string, object> { { "porHuella", PorHuella } });

                Console.WriteLine(
                    $"[CapturaErrores] Poda: {inactivas} filas de huellas inactivas, " +
                    $"{excedente} excedentes por huella.");
            }
            catch (Exception ex)
            {
                // Un job de mantenimiento que falla no puede tumbar al worker ni
                // generar su propio ticket: sería ruido sobre ruido.
                Console.WriteLine($"[CapturaErrores] Falló la poda: {ex.Message}");
            }
        }

        private int Borrar(NpgsqlConnection conn, string sql, Dictionary<string, object> parametros)
        {
            using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var p in parametros)
                cmd.Parameters.AddWithValue(p.Key, p.Value);

            return cmd.ExecuteNonQuery();
        }
    }
}
