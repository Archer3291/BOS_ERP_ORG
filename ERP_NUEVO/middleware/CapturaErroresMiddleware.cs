using BOS_ERP.Models;
using BOS_ERP.Services;

namespace BOS_ERP.Middleware
{
    /// <summary>
    /// Capa 1 de la captura de errores: todo lo que escapa del pipeline.
    ///
    /// QUÉ ATRAPA
    /// Excepciones que nadie manejó -NullReference en vistas, caídas de conexión,
    /// timeouts-. NO ve los ~300 catch que devuelven Json(success = false) con
    /// HTTP 200: para el pipeline esas peticiones fueron exitosas. Ésos los cubre
    /// el helper de Utilities (capa 2) y la red del navegador (capa 3).
    ///
    /// QUÉ HACE CON LA EXCEPCIÓN
    /// La registra y la vuelve a lanzar. Este middleware OBSERVA, no reemplaza el
    /// manejo de errores: quien pinta la página de error sigue siendo el
    /// UseExceptionHandler("/Home/Error") de Program.cs en producción y la página
    /// de desarrollador en local.
    ///
    /// La única excepción a esa regla son las peticiones que esperan JSON: ahí
    /// relanzar produce una página HTML que el front no sabe leer, así que se
    /// responde el JSON que la vista sí entiende.
    /// </summary>
    public sealed class CapturaErroresMiddleware
    {
        /// <summary>Clave con la que el folio queda disponible para lo que venga después.</summary>
        public const string ClaveFolio = "BOS_FolioTicketError";

        private readonly RequestDelegate _siguiente;

        public CapturaErroresMiddleware(RequestDelegate siguiente)
        {
            _siguiente = siguiente;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _siguiente(context);
            }
            catch (Exception ex)
            {
                string folio = Registrar(context, ex);

                // Si la respuesta ya empezó a escribirse no se puede tocar: el
                // único camino honesto es relanzar y dejar que el servidor corte
                // la conexión.
                if (context.Response.HasStarted) throw;

                if (EsPeticionJson(context))
                {
                    await ResponderJson(context, folio);
                    return;
                }

                throw;
            }
        }

        /// <summary>
        /// Arma el diagnóstico y lo manda al servicio. Nunca lanza: un fallo aquí
        /// convertiría un error en dos y taparía el original.
        /// </summary>
        private static string Registrar(HttpContext context, Exception ex)
        {
            try
            {
                // Si la capa de datos o el catch de un controlador ya la registraron,
                // esta es la MISMA falla subiendo, no una segunda. Registrarla otra vez
                // crearía una huella distinta -otro origen, otra capa- para un solo
                // incidente, y gastaría el cortacircuitos al doble.
                if (Controllers.Utilities.YaRegistrado(ex))
                    return context.Items.TryGetValue(ClaveFolio, out var f) ? f as string : null;

                var e = ErrorCapturado.Desde(ex, "middleware", Modulo(context));

                e.Ruta = context.Request.Path + context.Request.QueryString;
                e.Metodo = context.Request.Method;
                e.TraceId = context.TraceIdentifier;
                e.Ip = context.Connection.RemoteIpAddress?.ToString();
                e.UserAgent = context.Request.Headers.UserAgent.ToString();

                LeerSesion(context, e);
                LeerParametros(context, e);

                string folio = new ErrorTicketService().Registrar(e);

                // Queda disponible por si algo más adelante -una página de error, un
                // filtro- quiere mencionarlo.
                context.Items[ClaveFolio] = folio;

                return folio;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// "Ventas/PuntoDeVenta" en vez de la ruta cruda: es lo que va al título del
        /// ticket y lo que decide si el módulo cuenta como crítico.
        /// </summary>
        private static string Modulo(HttpContext context)
        {
            try
            {
                var valores = context.GetRouteData()?.Values;
                string controlador = valores?["controller"]?.ToString();
                string accion = valores?["action"]?.ToString();

                if (!string.IsNullOrWhiteSpace(controlador))
                    return string.IsNullOrWhiteSpace(accion) ? controlador : $"{controlador}/{accion}";
            }
            catch { }

            return context.Request.Path.ToString();
        }

        /// <summary>
        /// Usuario, empresa y sucursal. Este middleware va DESPUÉS de UseSession en
        /// Program.cs justamente por esto: registrado antes, la sesión no está
        /// poblada y se pierde el dato de a quién le falló.
        /// </summary>
        private static void LeerSesion(HttpContext context, ErrorCapturado e)
        {
            try
            {
                if (context.Session == null || !context.Session.IsAvailable) return;

                e.UsuarioId = context.Session.GetInt32("UsuarioId");
                e.EmpresaId = context.Session.GetInt32("Empresa");
                e.SucursalId = context.Session.GetInt32("Sucursal");
            }
            catch
            {
                // Sin sesión disponible (peticiones a la API, arranque). El error se
                // registra igual, sólo que sin dueño.
            }
        }

        /// <summary>
        /// Query string y formulario, ya sanitizados por ErrorCapturado.
        ///
        /// El cuerpo JSON no se lee a propósito: obligaría a rebobinar el stream y,
        /// en peticiones de facturación, ese cuerpo lleva datos de CSD.
        /// </summary>
        private static void LeerParametros(HttpContext context, ErrorCapturado e)
        {
            try
            {
                if (context.Request.Query.Count > 0)
                {
                    e.AgregarParametros("query", context.Request.Query
                        .Select(q => new KeyValuePair<string, string>(q.Key, q.Value.ToString())));
                }

                // HasFormContentType evita la excepción de leer .Form en una petición
                // que no es formulario.
                if (context.Request.HasFormContentType && context.Request.Form.Count > 0)
                {
                    e.AgregarParametros("form", context.Request.Form
                        .Select(f => new KeyValuePair<string, string>(f.Key, f.Value.ToString())));
                }
            }
            catch
            {
                // Un cuerpo ya consumido o mal formado no puede costar el registro.
            }
        }

        /// <summary>
        /// ¿La vista espera JSON? jQuery manda X-Requested-With en las llamadas
        /// del mismo origen, que es como está hecho todo el front de este ERP.
        /// </summary>
        private static bool EsPeticionJson(HttpContext context)
        {
            if (string.Equals(context.Request.Headers.XRequestedWith,
                              "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                return true;

            string acepta = context.Request.Headers.Accept.ToString();
            return !string.IsNullOrEmpty(acepta)
                && acepta.Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// La misma forma de respuesta que ya leen las vistas, con el mensaje que
        /// se acordó mostrarle a la gente: que el ticket existe y que Sistemas ya
        /// está en ello, sin folio.
        /// </summary>
        private static async Task ResponderJson(HttpContext context, string folio)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";

            // Sin folio no hubo ticket -cortacircuitos abierto o base caída-, así que
            // el mensaje no puede prometer que alguien lo está viendo.
            string mensaje = folio != null
                ? "Ocurrió un error en el sistema. Se levantó un ticket de soporte y Sistemas ya está trabajando en solucionarlo."
                : "Ocurrió un error inesperado. Vuelve a intentarlo; si sigue pasando, repórtalo a Sistemas.";

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                // El mismo texto en los dos nombres a propósito: el front de este ERP
                // lee "message" en unos lados y "error" en otros (281 contra 64 usos),
                // y no hay forma de saber cuál espera la vista que hizo esta llamada.
                error = mensaje,
                message = mensaje,
                // Marca de falla técnica: es lo que permite a la red del navegador
                // distinguir esto de una validación de negocio y no reportarlo dos veces.
                esError = true,
                ticketGenerado = folio != null
            });
        }
    }
}
