using BOS_ERP.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BOS_ERP.Filters
{
    public class AuthorizeRoleAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string[] _allowedRoles;

        private static Dictionary<int, string> _rolesCache;
        private static readonly object _rolesLock = new object();

        /// <summary>
        /// Roles permitidos. Se aceptan nombres ("Gerente") o ids ("8").
        /// </summary>
        public AuthorizeRoleAttribute(params string[] roles)
        {
            _allowedRoles = roles ?? Array.Empty<string>();
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var session = context.HttpContext.Session;

            // El login escribe ambos con SetInt32 (AccountController). Leerlos con
            // GetString devolvía los 4 bytes crudos convertidos a texto: nunca era
            // null (así que el chequeo de sesión pasaba) y nunca coincidía con un
            // nombre de rol, de modo que este filtro rechazaba incluso a los
            // usuarios autorizados.
            int? usuarioId = session.GetInt32("UsuarioId");
            int? rolId = session.GetInt32("Rol");

            if (usuarioId == null)
            {
                context.Result = new RedirectToActionResult(
                    "Login",
                    "Account",
                    new { returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString }
                );
                return;
            }

            if (!EstaAutorizado(rolId))
            {
                context.Result = EsNavegacion(context.HttpContext.Request)
                    ? new ViewResult { ViewName = "~/Views/Shared/Unauthorized.cshtml" }
                    : new JsonResult(new
                    {
                        success = false,
                        message = "No tiene permisos para realizar esta acción."
                    })
                    { StatusCode = StatusCodes.Status403Forbidden };
            }
        }

        private bool EstaAutorizado(int? rolId) => TieneRol(rolId, _allowedRoles);

        /// <summary>
        /// Mismo criterio que el filtro, disponible para chequeos dentro de una acción
        /// cuando el permiso depende de lo que se está haciendo y no sólo del endpoint.
        /// </summary>
        public static bool TieneRol(ISession session, params string[] roles)
            => TieneRol(session?.GetInt32("Rol"), roles);

        private static bool TieneRol(int? rolId, string[] roles)
        {
            if (rolId == null || roles == null || roles.Length == 0)
                return false;

            string nombreRol = ObtenerNombreRol(rolId.Value);

            return roles.Any(permitido =>
                (int.TryParse(permitido, out int idPermitido) && idPermitido == rolId.Value) ||
                string.Equals(permitido, nombreRol, StringComparison.OrdinalIgnoreCase));
        }

        private static string ObtenerNombreRol(int rolId)
        {
            if (_rolesCache == null)
            {
                lock (_rolesLock)
                {
                    if (_rolesCache == null)
                    {
                        var cache = new Dictionary<int, string>();
                        try
                        {
                            var utils = new Utilities(true);
                            foreach (var fila in utils.RunQuery("SELECT rolid, nombre FROM roles"))
                            {
                                if (fila["rolid"] == null) continue;
                                cache[Convert.ToInt32(fila["rolid"])] = fila["nombre"]?.ToString() ?? "";
                            }
                        }
                        catch
                        {
                            // Si el catálogo no se puede leer se deja el cache vacío: la
                            // comparación por id sigue funcionando y el acceso se deniega
                            // por defecto en vez de abrirse.
                        }
                        _rolesCache = cache;
                    }
                }
            }

            return _rolesCache.TryGetValue(rolId, out var nombre) ? nombre : null;
        }

        /// <summary>
        /// Distingue una navegación del navegador (donde tiene sentido devolver la vista
        /// Unauthorized) de una llamada fetch/AJAX, que espera JSON y reventaría al hacer
        /// response.json() sobre una página HTML.
        /// </summary>
        private static bool EsNavegacion(HttpRequest request)
        {
            if (request.Headers["Sec-Fetch-Dest"].ToString() == "document")
                return true;

            return request.Headers["Accept"].ToString()
                .Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }
    }
}
