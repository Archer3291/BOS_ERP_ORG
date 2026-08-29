using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BOS_ERP.Controllers;

namespace BOS_ERP.Filters
{
    public class MantenimientoFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            string controllerName = filterContext.Controller.GetType().Name;

            var controladoresExcluidos = new[] { "SettingsController", "AccountController" };
            if (controladoresExcluidos.Contains(controllerName))
            {
                base.OnActionExecuting(filterContext);
                return;
            }

            bool enMantenimiento = Utilities.GetSetting("mantenimiento") == "true";

            if (enMantenimiento)
            {
                string usuario = filterContext.HttpContext.User.Identity.Name;

                bool esAdmin = Utilities.DoesUserHasArea(
                    usuario,
                    new List<string> { "Sistemas" }
                );

                if (!esAdmin)
                {
                    filterContext.Result = new ViewResult
                    {
                        ViewName = "~/Views/Shared/Mantenimiento.cshtml"
                    };

                    return;
                }
            }

            base.OnActionExecuting(filterContext);
        }

    }
}