using BOS_ERP.Controllers;

namespace BOS_ERP.Services
{
    /// <summary>
    /// Lleva las reservaciones por su ciclo de vida sin que nadie tenga que tocarlas:
    /// reservada → en_curso → finalizada, y cierra como no_asistira a quien nunca
    /// contestó la invitación. Corre en Hangfire cada minuto y también a demanda
    /// desde el calendario, porque una junta que sigue pintada como "reservada"
    /// media hora después de terminar se lee como un error de la pantalla.
    /// </summary>
    public class SalasEstadoService : Utilities
    {
        private static DateTime _ultimaCorrida = DateTime.MinValue;
        private static readonly object _candado = new object();

        public void Sincronizar()
        {
            MarcarAusencias();
            MarcarFinalizadas();
            AvisarEnCurso();
        }

        /// <summary>
        /// Versión para las lecturas del calendario: evita repetir los UPDATE en cada
        /// fetch cuando varios usuarios tienen la pantalla abierta.
        /// </summary>
        public void SincronizarConThrottle(int segundos = 20)
        {
            lock (_candado)
            {
                if ((DateTime.Now - _ultimaCorrida).TotalSeconds < segundos)
                    return;

                _ultimaCorrida = DateTime.Now;
            }

            try
            {
                Sincronizar();
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog("Salas", "SIN_FOLIO", $"Error sincronizando estados de salas: {ex.Message}", nivel: "DEBUG");
            }
        }

        private void MarcarAusencias()
        {
            string query = "UPDATE participantes_sala ps " +
                "SET estado = 'no_asistira'::estado_participante_sala, fecha_confirmacion = NOW() " +
                "FROM salas_reservadas sr " +
                "WHERE sr.id_sala_reservada = ps.sala_reservada_id " +
                "  AND ps.estado = 'pendiente'::estado_participante_sala " +
                "  AND sr.estado <> 'cancelada'::estado_sala " +
                "  AND sr.inicio_apartado <= NOW()";

            RunUpdate(query, new Dictionary<string, object>());
        }

        private void MarcarFinalizadas()
        {
            string query = "UPDATE salas_reservadas " +
                "SET estado = 'finalizada'::estado_sala " +
                "WHERE estado NOT IN ('cancelada'::estado_sala, 'finalizada'::estado_sala) " +
                "  AND fin_apartado <= NOW()";

            RunUpdate(query, new Dictionary<string, object>());
        }

        private void AvisarEnCurso()
        {
            string query = "UPDATE salas_reservadas " +
                "SET estado = 'en_curso'::estado_sala " +
                "WHERE estado IN ('reservada'::estado_sala, 'confirmada'::estado_sala) " +
                "  AND inicio_apartado <= NOW() AND fin_apartado > NOW() " +
                "RETURNING id_sala_reservada, asunto, inicio_apartado, fin_apartado";

            var arrancaron = RunQuery(query, new Dictionary<string, object>());

            if (arrancaron.Count == 0)
                return;

            var ids = arrancaron.Select(r => Convert.ToInt32(r["id_sala_reservada"])).ToArray();

            var parameters = new Dictionary<string, object> { { "ids", ids } };
            query = "SELECT ps.sala_reservada_id, us.nombreusuario, us.email, " +
                "   sr.asunto, sr.fin_apartado, cs.nombre AS sala " +
                "FROM participantes_sala ps " +
                "INNER JOIN salas_reservadas sr ON sr.id_sala_reservada = ps.sala_reservada_id " +
                "INNER JOIN catsalas cs ON cs.id_sala = sr.sala_id " +
                "INNER JOIN usuarios us ON us.usuarioid = ps.participante_id " +
                "WHERE ps.sala_reservada_id = ANY(@ids) " +
                "  AND ps.estado <> 'no_asistira'::estado_participante_sala " +
                "  AND us.email IS NOT NULL";

            foreach (var destinatario in RunQuery(query, parameters))
            {
                int reservacion = Convert.ToInt32(destinatario["sala_reservada_id"]);

                _ = SendNotificationInterno(GetString(destinatario["nombreusuario"], ""), GetString(destinatario["email"]), new
                {
                    icon = "info",
                    modulo = "salas",
                    title = "Tu reunión está comenzando",
                    message = $"\"{GetString(destinatario["asunto"])}\" arranca ahora en {GetString(destinatario["sala"])} " +
                              $"y termina a las {Convert.ToDateTime(destinatario["fin_apartado"]):HH:mm}.",
                    buttons = new[] {
                        new { text = "Ver Detalles", style = "primary", action = $"window.location.href='/RecursosHumanos/SolicitudSalas?reservacion={reservacion}'" }
                    },
                    timer = 0
                });
            }
        }
    }
}
