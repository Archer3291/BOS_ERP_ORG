using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace BOS_ERP.Filters
{
    /// <summary>
    /// Identidad del portal de clientes: nombre del esquema y claims.
    /// </summary>
    public static class PortalClientesAuth
    {
        /// <summary>Esquema de cookie propio, aislado del que usa el ERP.</summary>
        public const string Scheme = "PortalClientes";

        public const string ClaimClienteId = "portal:cliente_id";
        public const string ClaimClaveCliente = "portal:cve_cli";
        public const string ClaimCorreo = "portal:correo";
        public const string ClaimEmpresaId = "portal:empresa_id";

        /// <summary>
        /// id_cliente de la sesión del portal, o 0 si no hay sesión válida.
        /// Es la única fuente de verdad para filtrar documentos: nunca se toma el
        /// cliente de la petición, porque entonces bastaría cambiar un parámetro para
        /// ver las facturas de otro.
        /// </summary>
        public static int ClienteId(ClaimsPrincipal user)
        {
            var claim = user?.FindFirst(ClaimClienteId)?.Value;
            return int.TryParse(claim, out int id) ? id : 0;
        }

        public static string Correo(ClaimsPrincipal user) =>
            user?.FindFirst(ClaimCorreo)?.Value ?? "";

        public static string ClaveCliente(ClaimsPrincipal user) =>
            user?.FindFirst(ClaimClaveCliente)?.Value ?? "";

        public static int EmpresaId(ClaimsPrincipal user)
        {
            var claim = user?.FindFirst(ClaimEmpresaId)?.Value;
            return int.TryParse(claim, out int id) ? id : 0;
        }

        /// <summary>Cuánto vive un enlace de activación o restablecimiento.</summary>
        public static readonly TimeSpan VigenciaToken = TimeSpan.FromHours(2);

        /// <summary>
        /// Token de un solo uso, criptográficamente aleatorio y seguro para URL.
        /// Lo generan tanto el autoservicio del portal como el alta manual desde
        /// Sistemas; vive aquí para que ambos usen la misma fuerza y el mismo formato.
        /// </summary>
        public static string GenerarToken()
        {
            byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                          .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// Hash del token. Se guarda esto y no el token: si alguien leyera la tabla no
        /// podría activar cuentas con lo que encuentre ahí.
        /// </summary>
        public static string HashearToken(string token) =>
            BCrypt.Net.BCrypt.HashPassword(token);

        public static bool TokenCoincide(string token, string hash)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(hash))
                return false;

            try { return BCrypt.Net.BCrypt.Verify(token, hash); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Exige una sesión válida del portal de clientes.
    ///
    /// Hace falta un filtro propio en vez de [Authorize]: el controlador del portal
    /// lleva [AllowAnonymous] para que el SessionManagementFilter global —que exige
    /// Session["UsuarioId"] y rebota a /Account/Login— lo deje pasar, y ese mismo
    /// [AllowAnonymous] desactiva el middleware de autorización estándar. Así que la
    /// verificación se hace aquí, explícitamente y contra el esquema del portal.
    /// </summary>
    public class AutorizarClienteAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var resultado = await context.HttpContext
                .AuthenticateAsync(PortalClientesAuth.Scheme);

            bool autenticado = resultado.Succeeded
                && resultado.Principal != null
                && PortalClientesAuth.ClienteId(resultado.Principal) > 0;

            if (autenticado)
            {
                // Se publica el principal para que las acciones lean los claims con
                // User.* sin volver a autenticar.
                context.HttpContext.User = resultado.Principal;
                return;
            }

            // Las llamadas de datos esperan JSON: devolverles el HTML del login haría
            // que el front fallara con un error de formato en vez de avisar que la
            // sesión expiró.
            bool esPeticionDeDatos =
                string.Equals(context.HttpContext.Request.Headers["X-Requested-With"],
                              "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || (context.HttpContext.Request.Headers["Accept"].ToString() ?? "")
                        .Contains("application/json", StringComparison.OrdinalIgnoreCase);

            if (esPeticionDeDatos)
            {
                context.Result = new JsonResult(new
                {
                    success = false,
                    sessionExpired = true,
                    message = "Tu sesión expiró. Vuelve a entrar."
                })
                { StatusCode = StatusCodes.Status401Unauthorized };
                return;
            }

            context.Result = new RedirectToActionResult("Acceso", "PortalClientes", null);
        }
    }
}
