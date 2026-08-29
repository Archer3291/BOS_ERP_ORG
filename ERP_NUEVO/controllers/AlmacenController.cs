using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    [Authorize]
    public class AlmacenController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult Productos()
        {
            return View();
        }
        public IActionResult Lineas()
        {
            return View();
        }
        public IActionResult Almacenes()
        {
            return View();
        }
        public IActionResult Tipos()
        {
            return View();
        }
        public IActionResult Unidades()
        {
            return View();
        }

        public IActionResult Agencies()
        {
            return View();
        }

        public IActionResult Grupos()
        {
            return View();
        }
        public IActionResult SolicitudesRecibidasAlmacen()
        {
            return View();
        }
        
        public IActionResult ReglasPrecio()
        {
            return View();
        }
        public IActionResult Modula()
        {
            return View();
        }
        public IActionResult ConsultarAlmacen()
        {
            return View();
        }
    }
}