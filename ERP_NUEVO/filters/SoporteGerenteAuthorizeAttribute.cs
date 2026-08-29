using BOS_ERP.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BOS_ERP.Filters
{
    /// <summary>
    /// Cierra las pestañas de Categorías, Equipo e Informes a quien no dirige un área.
    ///
    /// Pasa Sistemas -por rol, área o permiso- y quien tenga el rol del ERP 'Gerente'.
    /// El resolvedor se queda con la pestaña de Tickets: administrar el catálogo, el
    /// equipo y las cifras del área es trabajo de quien la dirige, no de quien la
    /// atiende.
    ///
    /// VA ADEMÁS DE [SoporteStaffAuthorize], no en su lugar: aquél decide si la persona
    /// entra al módulo, éste qué parte del módulo. Los dos controladores que protege
    /// llevan los dos atributos.
    ///
    /// Se aplica a nivel de CLASE, no de acción, a propósito. Estas pantallas son
    /// CRUD y cada acción nueva heredaría el permiso sin que nadie tenga que
    /// acordarse; olvidar el atributo en un método suelto es justo como quedan los
    /// huecos.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SoporteGerenteAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var session = context.HttpContext.Session;
            int usuarioId = SoporteAuthz.UsuarioActualId(session) ?? 0;

            if (usuarioId <= 0)
            {
                context.Result = new RedirectToActionResult(
                    "Login", "Account",
                    new { returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString });
                return;
            }

            var alcance = SoporteAlcance.De(session, usuarioId);
            if (alcance.PuedeAdministrar) return;

            // Mismo criterio que AuthorizeRoleAttribute: una navegación recibe la
            // pantalla de no autorizado, una llamada de datos recibe JSON. Devolver
            // HTML a un fetch deja al front intentando parsear una página entera.
            context.Result = EsNavegacion(context.HttpContext.Request)
                ? new ViewResult { ViewName = "~/Views/Shared/Unauthorized.cshtml" }
                : new JsonResult(new
                {
                    success = false,
                    // Los dos nombres: el JS de Soporte lee "error" en unas pantallas
                    // y "message" en otras.
                    error = "Esta sección es sólo para gerentes de área y Sistemas.",
                    message = "Esta sección es sólo para gerentes de área y Sistemas."
                })
                { StatusCode = StatusCodes.Status403Forbidden };
        }

        private static bool EsNavegacion(HttpRequest request)
        {
            if (string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest",
                              StringComparison.OrdinalIgnoreCase))
                return false;

            string acepta = request.Headers.Accept.ToString();
            return string.IsNullOrEmpty(acepta)
                || acepta.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }
    }
}
