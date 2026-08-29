using BOS_ERP.controllers;
using BOS_ERP.Controllers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace BOS_ERP.Filters
{
    public class AuditActionAttribute : ActionFilterAttribute
    {
        public string Modulo { get; set; }
        public string Accion { get; set; }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            try
            {
                var httpContext = context.HttpContext;
                var user = httpContext.User?.Identity?.Name ?? "Anonimo";
                var ip = httpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
                var controller = context.ActionDescriptor.RouteValues["controller"];
                var action = context.ActionDescriptor.RouteValues["action"];

                AuditDetails detalles = null;
                if (context.Controller is Controller controllerInstance && controllerInstance.ViewData["detalles"] is AuditDetails auditDetails)
                {
                    detalles = auditDetails;
                }

                var detallesJson = detalles != null
                    ? JsonSerializer.Serialize(detalles)
                    : null;

                Utilities.RegistrarAccion(
                    usuario: user,
                    accion: Accion ?? action,
                    modulo: Modulo ?? controller,
                    descripcion: $"{Modulo ?? controller} - {Accion ?? action}",
                    ip: ip,
                    userAgent: userAgent,
                    detalles: detallesJson
                );
            }
            catch
            {
                // sí, lo sigues tragando igual que antes, no aprendiste nada del pasado
            }

            base.OnActionExecuted(context);
        }
    }
}