using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Services;

namespace BOS_ERP.Controllers.Soporte
{
    /// <summary>
    /// Recibe lo que reporta wwwroot/Scripts/error-reporter.js (capa 3).
    ///
    /// TODO LO QUE LLEGA AQUÍ ES TEXTO QUE ESCRIBIÓ EL NAVEGADOR.
    /// Se recorta, se limita por sesión y se registra siempre con prioridad baja:
    /// no trae stack de servidor, así que sirve para detectar que algo se rompe,
    /// no para creerle nada.
    /// </summary>
    [Authorize]
    [Route("Soporte/ErrorCliente")]
    public class ErrorClienteController : Utilities
    {
        /// <summary>Tope por sesión. Las guardas del JS son bypasseables; ésta no.</summary>
        private const int MaxPorVentana = 20;
        private const int VentanaMinutos = 10;

        private const string ClaveConteo = "ErrCliente_Conteo";
        private const string ClaveInicio = "ErrCliente_Inicio";

        public sealed class ReporteCliente
        {
            public string Tipo { get; set; }
            public string Mensaje { get; set; }
            public string Origen { get; set; }
            public string Stack { get; set; }
            public string Url { get; set; }
            public string Ruta { get; set; }
            public string Metodo { get; set; }
            public int? EstadoHttp { get; set; }
            public string Modulo { get; set; }
        }

        [HttpPost("Reportar")]
        [ValidateAntiForgeryToken]
        public IActionResult Reportar([FromBody] ReporteCliente r)
        {
            // Respuesta deliberadamente muda: el navegador no necesita saber si se
            // creó ticket, si se descartó o si topó el límite. Cualquier detalle
            // aquí sería información que un reporte falso puede usar para tantear.
            if (r == null || string.IsNullOrWhiteSpace(r.Mensaje)) return Ok();

            if (RebasoElLimite()) return Ok();

            try
            {
                var e = new ErrorCapturado
                {
                    Capa = "navegador",
                    Modulo = Recortar(r.Modulo, 100) ?? "Navegador",
                    // El tipo lleva prefijo para que nunca choque con una huella de
                    // servidor: un error de JS y uno de C# jamás son el mismo problema.
                    TipoExc = "JS." + (Recortar(r.Tipo, 40) ?? "error"),
                    Mensaje = Recortar(r.Mensaje, 1000),
                    Stack = Recortar(r.Stack, 4000),
                    Origen = Recortar(r.Origen, 300),
                    Ruta = Recortar(r.Ruta ?? r.Url, 500),
                    Metodo = Recortar(r.Metodo, 10),
                    TraceId = HttpContext.TraceIdentifier,
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
                };

                // El usuario sale de la SESIÓN, nunca del cuerpo de la petición: si
                // llegara por el request, cualquiera podría atribuirle errores a otro.
                e.UsuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session);
                e.EmpresaId = HttpContext.Session.GetInt32("Empresa");
                e.SucursalId = HttpContext.Session.GetInt32("Sucursal");

                e.Extra["navegador"] = new
                {
                    pagina = Recortar(r.Url, 500),
                    estadoHttp = r.EstadoHttp,
                    tipoReporte = Recortar(r.Tipo, 40)
                };

                new ErrorTicketService().Registrar(e);
            }
            catch
            {
                // Un reporte malformado no puede tumbar nada.
            }

            return Ok();
        }

        /// <summary>
        /// Ventana deslizante por sesión. Sin esto, una pestaña con un bucle de
        /// render -o alguien curioseando el endpoint- puede agotar por sí sola el
        /// cortacircuitos global y dejar ciega a la captura de errores del servidor.
        /// </summary>
        private bool RebasoElLimite()
        {
            try
            {
                long ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long inicio = HttpContext.Session.GetInt32(ClaveInicio) ?? 0;
                int conteo = HttpContext.Session.GetInt32(ClaveConteo) ?? 0;

                if (inicio == 0 || ahora - inicio > VentanaMinutos * 60)
                {
                    // Se guarda como int para caber en Session.SetInt32; alcanza
                    // hasta 2038 y el desbordamiento no rompería nada más que la
                    // ventana, que se reiniciaría sola.
                    HttpContext.Session.SetInt32(ClaveInicio, (int)ahora);
                    HttpContext.Session.SetInt32(ClaveConteo, 1);
                    return false;
                }

                if (conteo >= MaxPorVentana) return true;

                HttpContext.Session.SetInt32(ClaveConteo, conteo + 1);
                return false;
            }
            catch
            {
                // Sin sesión no hay forma de limitar: se descarta, que es el lado
                // seguro.
                return true;
            }
        }

        private static string Recortar(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
