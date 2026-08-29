using System.Security.Cryptography;
using BOS_ERP.Controllers;
using Npgsql;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// El alta cruda de un ticket: folio único + INSERT en tkts.
    ///
    /// Vive aquí, y no dentro de EnviarTicketsController, porque ahora hay dos
    /// caminos que crean tickets -el formulario que llena una persona y la
    /// captura automática de errores- y el reintento por folio repetido es
    /// justamente el tipo de detalle que no debe existir por duplicado.
    ///
    /// Es deliberadamente tonto: no valida permisos, no manda correos y no genera
    /// documento del ERP. Eso lo decide cada llamador, que es donde esas reglas
    /// sí difieren (un ticket automático no lleva documento contable).
    /// </summary>
    public static class TicketFactory
    {
        /// <summary>stat_tkt: 1 = Abierto. Todo ticket nace aquí.</summary>
        public const int EstadoAbierto = 1;

        /// <summary>
        /// Inserta el ticket resolviendo colisiones de folio.
        ///
        /// El folio son 10 caracteres al azar y nada garantiza que sea único. Con
        /// el índice único sobre folio_tkt una colisión hace fallar el INSERT, así
        /// que se vuelve a intentar con otro folio en vez de propagar un error
        /// incomprensible.
        /// </summary>
        public static (int IdTicket, string Folio) InsertarConFolio(
            Utilities utils,
            NpgsqlConnection conn, NpgsqlTransaction tx,
            string titulo, string descripcion, int idPrioridad,
            int idUsuario, object idArea, int idResponsable, int idCategoria)
        {
            const int intentos = 5;

            for (int i = 1; i <= intentos; i++)
            {
                string folio = GenerarFolio();
                var parametros = new Dictionary<string, object>
                {
                    { "tit", titulo },
                    { "descr", descripcion },
                    { "id_stat_tkt", EstadoAbierto },
                    { "id_prio", idPrioridad },
                    { "id_usr", idUsuario },
                    { "id_area", idArea },
                    { "id_usr_asig", idResponsable },
                    { "id_cat", idCategoria },
                    { "folio_tkt", folio }
                };

                // La colisión de folio de aquí abajo es comportamiento ESPERADO: el
                // folio se genera al azar y el índice único es la señal para probar
                // otro. Sin suprimir, la instrumentación de la capa de datos
                // registraría cada colisión como si fuera una falla del sistema.
                using var sinRegistro = Utilities.SinRegistroDeErrores();

                try
                {
                    var filas = utils.RunQuery(
                        "INSERT INTO tkts " +
                        "(tit, descr, id_stat_tkt, id_prio, id_usr, id_area, id_usr_asig, fch_crea, id_cat, folio_tkt) " +
                        "VALUES " +
                        "(@tit, @descr, @id_stat_tkt, @id_prio, @id_usr, @id_area, @id_usr_asig, CURRENT_TIMESTAMP, @id_cat, @folio_tkt) " +
                        "RETURNING id_tkts",
                        parametros, false, conn, tx);

                    return (Convert.ToInt32(filas[0]["id_tkts"]), folio);
                }
                catch (PostgresException ex) when (ex.SqlState == "23505" && i < intentos)
                {
                    // 23505 = unique_violation: folio repetido, se prueba con otro.
                    // Cualquier otro error se propaga tal cual.
                }
            }

            throw new InvalidOperationException(
                $"No se pudo generar un folio de ticket unico tras {intentos} intentos.");
        }

        /// <summary>
        /// Folio público del ticket, con el formato AAA-BBB-CCCC.
        /// Se usa RandomNumberGenerator en lugar de new Random(): este último se
        /// creaba en cada llamada y dos altas simultáneas podían arrancar con la
        /// misma semilla.
        /// </summary>
        public static string GenerarFolio()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

            string Segmento(int largo) => new string(
                Enumerable.Range(0, largo)
                    .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
                    .ToArray());

            return $"{Segmento(3)}-{Segmento(3)}-{Segmento(4)}";
        }
    }
}
