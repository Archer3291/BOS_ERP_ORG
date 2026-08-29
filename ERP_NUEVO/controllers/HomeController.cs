using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Filters;
using Microsoft.AspNetCore.Authorization;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers
{
    public partial class HomeController : Utilities
    {
        [Authorize]
        public IActionResult Index()
        {
            var parameters = new Dictionary<string, object>();
            string query = "select modulo, accion, fecha  " +
                "from historial_usuarios " +
                "where usuario = @usuario " +
                "order by fecha desc " +
                "limit 5;";
            parameters.Add("usuario", User.Identity.Name);
            var resultMovimientos = RunQuery(query,parameters);

            var acciones = ObtenerAccionesRapidas(GetUserId(User.Identity.Name));
            ViewBag.AccionesRapidas = acciones;

            ViewBag.Movimientos = resultMovimientos;
            return View();
        }

        [AuthorizeRole("Administrador")]
        public IActionResult AdminDashboard()
        {
            return View();
        }

        public IActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        public IActionResult Maintenance()
        {
            return View();
        }
    }
}