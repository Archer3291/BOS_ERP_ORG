using BOS_ERP.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BOS_ERP.Filters
{
    public class SessionManagementFilter : ActionFilterAttribute
    {
        private readonly FacturacionDbContext _db;

        public SessionManagementFilter(FacturacionDbContext dbContext)
        {
            _db = dbContext;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;
            var path = httpContext.Request.Path.Value ?? "";

            // 🚫 Ignorar API
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                base.OnActionExecuting(context);
                return;
            }

            // 🚫 AllowAnonymous (forma moderna)
            var endpoint = httpContext.GetEndpoint();
            var hasAllowAnonymous =
                endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>() != null;

            if (hasAllowAnonymous)
            {
                base.OnActionExecuting(context);
                return;
            }

            // 🚨 AJAX check (manual en Core)
            // Sólo se detectaba X-Requested-With, que jQuery manda pero fetch() no.
            // Todo el punto de venta usa fetch, así que con la sesión expirada recibía
            // un 302 al login, seguía el redirect, obtenía HTML y reventaba en
            // response.json() con "Unexpected token '<'" en vez de avisar al cajero.
            var isAjax = httpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                         || !EsNavegacion(httpContext.Request);

            var usuarioId = httpContext.Session.GetInt32("UsuarioId");

            if (isAjax && !usuarioId.HasValue)
            {
                context.Result = new JsonResult(new
                {
                    success = false,
                    sessionExpired = true,
                    message = "Tu sesión expiró. Vuelve a iniciar sesión para continuar."
                })
                { StatusCode = StatusCodes.Status401Unauthorized };
                return;
            }

            // 🚫 Rutas públicas
            var controller = context.ActionDescriptor.RouteValues["controller"];
            var action = context.ActionDescriptor.RouteValues["action"];

            var allowedActions = new[] { "Login", "Register", "ForgotPassword", "ResetPassword", "Logout" };

            if (controller == "Account" && allowedActions.Contains(action))
            {
                base.OnActionExecuting(context);
                return;
            }

            // 🔒 Validación de sesión
            if (!usuarioId.HasValue)
            {
                ClearAndRedirect(context);
                return;
            }

            int userId = usuarioId.Value;
            var deviceIdentifier = httpContext.Request.Cookies["DeviceIdentifier"];

            if (string.IsNullOrEmpty(deviceIdentifier))
            {
                ClearAndRedirect(context);
                return;
            }

            var session = _db.UserSessions.FirstOrDefault(s =>
                s.UserId == userId &&
                s.DeviceIdentifier == deviceIdentifier);
            var now = DateTime.Now;

            if (session == null || !session.IsActive || session.ExpiryTime < DateTime.Now)
            {
                if (session != null && session.ExpiryTime < DateTime.Now)
                {
                    session.IsActive = false;
                    _db.SaveChanges();
                }

                ClearAndRedirect(context);
                return;
            }

            // renovar sesión
            session.ExpiryTime = DateTime.Now.AddHours(1);
            httpContext.Session.SetString("FechaExpiracion", session.ExpiryTime.ToString("O"));
            _db.SaveChanges();

            base.OnActionExecuting(context);
        }

        /// <summary>
        /// Distingue una navegación del navegador (donde corresponde redirigir al login)
        /// de una llamada fetch/AJAX, que espera JSON.
        /// </summary>
        private static bool EsNavegacion(HttpRequest request)
        {
            if (request.Headers["Sec-Fetch-Dest"].ToString() == "document")
                return true;

            return request.Headers["Accept"].ToString()
                .Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }

        private static void ClearAndRedirect(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;
            httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            httpContext.Session.Clear();

            context.Result = new RedirectToActionResult(
                "Login",
                "Account",
                null
            );
        }
    }
}