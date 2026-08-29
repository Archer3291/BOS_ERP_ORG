using Microsoft.AspNetCore.SignalR;

namespace BOS_ERP.Hubs
{
    /// <summary>
    /// Reemplazo de Pusher para las notificaciones internas (documentos, aprobaciones,
    /// traslados). Antes el ruteo era canal = correo y evento = nombre de usuario; aquí
    /// el canal se vuelve un grupo por correo y el evento es un nombre fijo, porque el
    /// correo ya identifica al destinatario y el doble filtro solo agregaba formas de
    /// que un aviso se perdiera en silencio.
    /// </summary>
    public class NotificacionesHub : Hub
    {
        public const string Ruta = "/hubs/notificaciones";

        /// <summary>Nombre del método que escucha _Layout.cshtml.</summary>
        public const string EventoNotificacionInterna = "NotificacionInterna";

        /// <summary>
        /// El correo se normaliza en ambos extremos: el que manda lo saca de la base
        /// (emailusuario, emailmanager, …) y el que recibe de la sesión, y no siempre
        /// coinciden en mayúsculas o espacios.
        /// </summary>
        public static string GrupoUsuario(string? correo) =>
            "usuario-" + (correo ?? string.Empty).Trim().ToLowerInvariant();

        public override async Task OnConnectedAsync()
        {
            var session = Context.GetHttpContext()?.Session;

            if (session != null)
            {
                // La sesión se carga bajo demanda; en el handshake del hub hay que
                // pedirla explícitamente antes de leerla.
                await session.LoadAsync();

                var correo = session.GetString("Correo");
                if (!string.IsNullOrWhiteSpace(correo))
                    await Groups.AddToGroupAsync(Context.ConnectionId, GrupoUsuario(correo));
            }

            await base.OnConnectedAsync();
        }
    }

    /// <summary>
    /// Puente para emitir desde código que no se construye por DI. Utilities se
    /// instancia a mano en varios lugares (new Utilities(true)) y 29 controladores
    /// heredan de ella sin constructor propio, así que inyectar IHubContext ahí
    /// obligaría a tocar todos. Se llena una sola vez al arrancar y de ahí en
    /// adelante es de solo lectura.
    /// </summary>
    public static class NotificacionesHubAccessor
    {
        public static IHubContext<NotificacionesHub>? Hub { get; set; }
    }
}
