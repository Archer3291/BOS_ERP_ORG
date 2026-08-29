using System.Net;
using BOS_ERP.Controllers;
using BOS_ERP.Helpers;

namespace BOS_ERP.Services
{
    /// <summary>
    /// Manda por correo el aviso de un error automático.
    ///
    /// POR QUÉ ES UN JOB Y NO UNA LLAMADA DIRECTA
    /// ErrorTicketService corre desde middleware y desde workers de Hangfire, donde
    /// no hay un scope de DI del que sacar CorreoHelper. Encolarlo resuelve eso y
    /// además trae tres cosas gratis:
    ///
    ///   * la latencia del SMTP no se le cobra al usuario cuyo request falló;
    ///   * Hangfire reintenta solo si el correo no sale;
    ///   * el worker sí tiene scope, así que CorreoHelper se inyecta normal.
    ///
    /// Recibe sólo el id de la huella -no el texto ya armado- para que el correo
    /// refleje el estado real en el momento de enviarse y para que lo que Hangfire
    /// serializa sea un entero.
    /// </summary>
    public sealed class ErrorCorreoJob
    {
        private readonly CorreoHelper _correo;
        private readonly Utilities _utils;

        public ErrorCorreoJob(CorreoHelper correo)
        {
            _correo = correo;
            _utils = new Utilities(true);
        }

        /// <param name="idHuella">Huella del error.</param>
        /// <param name="esRegresion">
        /// true cuando el error volvió después de darse por resuelto. Cambia el
        /// asunto: una regresión es peor noticia que un error nuevo y no debe
        /// perderse entre los avisos normales.
        /// </param>
        public async Task Enviar(int idHuella, bool esRegresion)
        {
            var filas = _utils.RunQuery(
                "SELECT h.tipo_exc, h.mensaje_norm, h.origen, h.ocurrencias, h.afectados, " +
                "       t.folio_tkt, t.tit, " +
                "       u.email, COALESCE(u.nombre || ' ' || u.apellido, u.nombreusuario) AS nombre " +
                "FROM tkt_error_huella h " +
                "JOIN tkts t     ON t.id_tkts = h.id_tkt " +
                "JOIN usuarios u ON u.usuarioid = t.id_usr_asig " +
                "WHERE h.id_huella = @id",
                new Dictionary<string, object> { { "id", idHuella } });

            if (filas.Count == 0) return;

            var f = filas[0];
            string destino = f["email"]?.ToString();
            if (string.IsNullOrWhiteSpace(destino)) return;

            string folio = f["folio_tkt"]?.ToString();
            string asunto = esRegresion
                ? $"[REGRESIÓN] Ticket {folio}: un error resuelto volvió a ocurrir"
                : $"[Error automático] Ticket {folio}: {f["tipo_exc"]}";

            await _correo.EnviarCorreoNotificacionAsync(destino, asunto, Html(f, folio, esRegresion));
        }

        /// <summary>
        /// El cuerpo del correo.
        ///
        /// Todo lo que sale de la excepción se codifica: el mensaje de un error
        /// puede arrastrar texto que escribió un usuario, y aquí acabaría inyectado
        /// en el HTML del correo.
        /// </summary>
        private string Html(Dictionary<string, object> f, string folio, bool esRegresion)
        {
            string E(object v) => WebUtility.HtmlEncode(v?.ToString() ?? "");

            string color = esRegresion ? "#b3261e" : "#004080";
            string titulo = esRegresion ? "Un error resuelto volvió a ocurrir" : "Error detectado automáticamente";

            // Sin UrlBase configurada no se pone enlace: un href a medias es peor
            // que ninguno. Se configura en appsettings -> CapturaErrores:UrlBase.
            string urlBase = (_utils._configuration["CapturaErrores:UrlBase"] ?? "").TrimEnd('/');
            string enlace = string.IsNullOrWhiteSpace(urlBase)
                ? ""
                : $@"<tr><td style='padding:18px 24px 24px;'>
                        <a href='{urlBase}/Soporte/Ticket/{Uri.EscapeDataString(folio ?? "")}'
                           style='background:{color};color:#fff;text-decoration:none;
                                  padding:10px 18px;border-radius:4px;display:inline-block;
                                  font-family:Arial,sans-serif;font-size:14px;'>
                            Abrir el ticket
                        </a>
                     </td></tr>";

            return $@"
<table width='100%' cellpadding='0' cellspacing='0' style='background:#f4f4f4;padding:20px;font-family:Arial,sans-serif;'>
  <tr><td align='center'>
    <table width='620' cellpadding='0' cellspacing='0' style='background:#fff;border-radius:8px;overflow:hidden;'>
      <tr style='background:{color};color:#fff;'>
        <td style='padding:18px 24px;font-size:17px;font-weight:bold;'>{titulo}</td>
      </tr>
      <tr><td style='padding:20px 24px 4px;font-size:14px;color:#333;'>
        Se generó el ticket <strong>{E(folio)}</strong> sin intervención de ningún usuario.
      </td></tr>
      <tr><td style='padding:8px 24px;'>
        <table width='100%' cellpadding='6' cellspacing='0' style='font-size:13px;color:#333;border-collapse:collapse;'>
          <tr><td style='color:#666;width:120px;'>Excepción</td><td><code>{E(f["tipo_exc"])}</code></td></tr>
          <tr><td style='color:#666;'>Mensaje</td><td><code>{E(f["mensaje_norm"])}</code></td></tr>
          <tr><td style='color:#666;'>Origen</td><td><code>{E(f["origen"])}</code></td></tr>
          <tr><td style='color:#666;'>Ocurrencias</td><td>{E(f["ocurrencias"])}</td></tr>
          <tr><td style='color:#666;'>Usuarios</td><td>{E(f["afectados"])}</td></tr>
        </table>
      </td></tr>
      {enlace}
      <tr><td style='padding:14px 24px;background:#fafafa;color:#888;font-size:12px;'>
        Aviso automático de la captura de errores del ERP. Sólo se envía la primera vez
        que aparece cada error; las repeticiones se acumulan en el mismo ticket.
      </td></tr>
    </table>
  </td></tr>
</table>";
        }
    }
}
