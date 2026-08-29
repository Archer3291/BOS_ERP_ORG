using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Globalization;
using System.Text;

namespace BOS_ERP.Helpers
{
    public class CorreoHelper
    {
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPassword;
        private readonly bool _useSsl;
        private readonly string _fromAddress;
        private readonly string _fromName;

        public CorreoHelper(IConfiguration configuration)
        {
            _smtpHost = configuration["Smtp:Host"]
                ?? throw new InvalidOperationException("Falta configurar Smtp:Host en appsettings.json");

            _smtpPort = int.Parse(configuration["Smtp:Port"] ?? "587");

            _smtpUser = configuration["Smtp:User"]
                ?? throw new InvalidOperationException("Falta configurar Smtp:User en appsettings.json");

            _smtpPassword = configuration["Smtp:Password"]
                ?? throw new InvalidOperationException("Falta configurar Smtp:Password en appsettings.json");

            _useSsl = bool.Parse(configuration["Smtp:UseSsl"] ?? "true");

            _fromAddress = configuration["Smtp:FromAddress"] ?? "BOS@sellosyretenes.com";
            _fromName = configuration["Smtp:FromName"] ?? "Sistema de Validación";
        }

        public async Task EnviarCorreoNotificacionAsync(string destinatario,string asunto,string mensajeHtml)
        {
            var mensaje = new MimeMessage();

            mensaje.From.Add(new MailboxAddress(_fromName, _fromAddress));
            mensaje.To.Add(MailboxAddress.Parse(destinatario));
            mensaje.Subject = asunto;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = mensajeHtml,
                TextBody = StripHtml(mensajeHtml)
            };

            mensaje.Body = bodyBuilder.ToMessageBody();

            await EnviarAsync(mensaje);
        }

        public async Task EnviarCorreoReservacionAsync(string destinatario, string asunto, string mensajeHtml, string uid, DateTime inicio, DateTime fin, string titulo, string descripcion, string sala, string organizador, string organizadorEmail, string tipo, int sequence = 0)
        {
            var mensaje = new MimeMessage();

            mensaje.From.Add(new MailboxAddress(_fromName, _fromAddress));
            mensaje.To.Add(MailboxAddress.Parse(destinatario));
            mensaje.Subject = asunto;

            var ics = GenerarIcs(uid, inicio, fin, titulo, descripcion, sala, organizador, organizadorEmail, destinatario, tipo, sequence);

            var textPart = new TextPart("plain")
            {
                Text = StripHtml(mensajeHtml)
            };

            var htmlPart = new TextPart("html")
            {
                Text = mensajeHtml
            };

            var calendarPart = new TextPart("calendar")
            {
                Text = ics
            };

            calendarPart.ContentType.Parameters["method"] =
                tipo.Equals("cancelacion", StringComparison.OrdinalIgnoreCase)
                    ? "CANCEL"
                    : "REQUEST";

            calendarPart.ContentType.Parameters["charset"] = "utf-8";

            var multipart = new Multipart("alternative");

            multipart.Add(textPart);
            multipart.Add(htmlPart);
            multipart.Add(calendarPart);

            mensaje.Body = multipart;

            await EnviarAsync(mensaje);
        }

        private string GenerarIcs(string uid, DateTime inicio, DateTime fin, string titulo, string descripcion, string sala, string organizador, string organizadorEmail, string destinatario, string tipo, int sequence)
        {
            // iCalendar trabaja mejor con UTC.
            DateTime inicioUtc = inicio.ToUniversalTime();
            DateTime finUtc = fin.ToUniversalTime();

            string dtStamp = DateTime.UtcNow.ToString(
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture
            );

            string dtStart = inicioUtc.ToString(
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture
            );

            string dtEnd = finUtc.ToString(
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture
            );

            bool cancelado = tipo.Equals(
                "cancelacion",
                StringComparison.OrdinalIgnoreCase
            );

            var sb = new StringBuilder();

            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("PRODID:-//BOS ERP//Reservaciones de Sala//ES");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine($"METHOD:{(cancelado ? "CANCEL" : "REQUEST")}");
            sb.AppendLine("CALSCALE:GREGORIAN");

            sb.AppendLine("BEGIN:VEVENT");

            sb.AppendLine($"UID:{EscapeIcs(uid)}");
            sb.AppendLine($"DTSTAMP:{dtStamp}");
            sb.AppendLine($"DTSTART:{dtStart}");
            sb.AppendLine($"DTEND:{dtEnd}");
            sb.AppendLine($"SEQUENCE:{sequence}");

            sb.AppendLine($"SUMMARY:{EscapeIcs(titulo)}");

            if (!string.IsNullOrWhiteSpace(descripcion))
                sb.AppendLine($"DESCRIPTION:{EscapeIcs(descripcion)}");

            if (!string.IsNullOrWhiteSpace(sala))
                sb.AppendLine($"LOCATION:{EscapeIcs(sala)}");

            sb.AppendLine(
                $"ORGANIZER;CN={EscapeIcsParam(organizador)}:mailto:{organizadorEmail}"
            );

            sb.AppendLine(
                $"ATTENDEE;CN={EscapeIcsParam(destinatario)};RSVP=TRUE:mailto:{destinatario}"
            );

            sb.AppendLine(
                $"STATUS:{(cancelado ? "CANCELLED" : "CONFIRMED")}"
            );

            if (!cancelado)
                sb.AppendLine("TRANSP:OPAQUE");

            sb.AppendLine("END:VEVENT");
            sb.AppendLine("END:VCALENDAR");

            return sb.ToString();
        }

        private static string EscapeIcs(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n");
        }

        private static string EscapeIcsParam(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\r", "")
                .Replace("\n", " ");
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            return System.Text.RegularExpressions.Regex
                .Replace(html, "<.*?>", string.Empty)
                .Trim();
        }

        private async Task EnviarAsync(MimeMessage mensaje)
        {
            using var smtp = new SmtpClient();

            await smtp.ConnectAsync(
                _smtpHost,
                _smtpPort,
                _useSsl
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.None
            );

            await smtp.AuthenticateAsync(
                _smtpUser,
                _smtpPassword
            );

            await smtp.SendAsync(mensaje);

            await smtp.DisconnectAsync(true);
        }

        /// <summary>
        /// Correo con archivos adjuntos. Lo usa la autofacturación para mandarle al cliente
        /// el XML y el PDF de su CFDI.
        /// Los adjuntos se pasan ya leídos en memoria y no como rutas: así el llamador
        /// decide qué hacer si un archivo falta, en vez de que el correo salga incompleto
        /// sin que nadie se entere.
        /// </summary>
        public async Task EnviarCorreoConAdjuntosAsync(
            string destinatario,
            string asunto,
            string mensajeHtml,
            IEnumerable<(string NombreArchivo, byte[] Contenido, string TipoMime)> adjuntos)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress(_fromName, _fromAddress));
            mensaje.To.Add(MailboxAddress.Parse(destinatario));
            mensaje.Subject = asunto;

            var bodyBuilder = new BodyBuilder { HtmlBody = mensajeHtml };

            foreach (var (nombre, contenido, mime) in adjuntos ?? Enumerable.Empty<(string, byte[], string)>())
            {
                if (contenido == null || contenido.Length == 0)
                    continue;

                bodyBuilder.Attachments.Add(nombre, contenido, ContentType.Parse(mime));
            }

            mensaje.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient();

            await smtp.ConnectAsync(
                _smtpHost,
                _smtpPort,
                _useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

            await smtp.AuthenticateAsync(_smtpUser, _smtpPassword);
            await smtp.SendAsync(mensaje);
            await smtp.DisconnectAsync(quit: true);
        }
    }
}