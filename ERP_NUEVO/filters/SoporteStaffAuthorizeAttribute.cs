using BOS_ERP.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BOS_ERP.Filters
{
    /// <summary>
    /// Exige que el usuario sea staff de soporte, con el mismo criterio que
    /// <see cref="SoporteAuthz.EsStaff"/>: permiso 'sistemas'/'super_admin', rol del ERP
    /// o rol propio del módulo (tkt_usuario_rol).
    ///
    /// Existe porque las pantallas de administración usaban [AuthorizeRole], que sólo
    /// mira el rol del ERP, mientras que el menú decide con DoesUserHasRight, que mira
    /// los permisos. El resultado era que el enlace de Administración se mostraba y al
    /// entrar respondía "acceso no autorizado". Con un único criterio compartido, lo que
    /// el menú ofrece es exactamente lo que el controlador acepta.
    ///
    /// IMPORTANTE: implementa IAuthorizationFilter. Un atributo que sólo herede de
    /// Attribute con un método OnAuthorization NUNCA se ejecuta — es lo que le pasa a
    /// RoleAuthorizeAttribute, que decora endpoints sin protegerlos.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class SoporteStaffAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var http = context.HttpContext;
            var usuarioId = SoporteAuthz.UsuarioActualId(http.Session) ?? 0;

            if (usuarioId <= 0)
            {
                context.Result = new RedirectToActionResult(
                    "Login", "Account",
                    new { returnUrl = http.Request.Path + http.Request.QueryString });
                return;
            }

            if (SoporteAuthz.EsStaff(http.Session, usuarioId))
                return;

            // Una navegación espera HTML; un fetch espera JSON y reventaría al hacer
            // response.json() sobre una página. Mismo criterio que AuthorizeRoleAttribute.
            context.Result = EsNavegacion(http.Request)
                ? new ViewResult { ViewName = "~/Views/Shared/Unauthorized.cshtml" }
                : new JsonResult(new
                {
                    success = false,
                    message = "No tienes permisos para administrar tickets."
                })
                { StatusCode = StatusCodes.Status403Forbidden };
        }

        private static bool EsNavegacion(HttpRequest request)
        {
            if (request.Headers["Sec-Fetch-Dest"].ToString() == "document")
                return true;

            return request.Headers["Accept"].ToString()
                .Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }
    }
}
