using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers
{
    [AllowAnonymous]
    public class ErrorController : Controller
    {
        public IActionResult Forbidden()
        {
            Response.StatusCode = 403;
            return View();
        }

        public IActionResult NotFound()
        {
            Response.StatusCode = 404;
            return View();
        }
        
        public IActionResult ServerError()
        {
            Response.StatusCode = 500;
            return View();
        }
    }
}