using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class FinanzasController : Utilities
    {
        // GET: Finanzas
        public IActionResult OrdenesCompra()
        {
            return View();
        }

        public IActionResult Calendario()
        {
            return View();
        }
        public IActionResult DevolucionEfectivo()
        {
            return View();
        }
    }
}