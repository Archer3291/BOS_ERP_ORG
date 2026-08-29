using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    [Authorize]
    public partial class CarterasController : Utilities
    {
        public IActionResult CarteraProveedores()
        {
            return View();
        }

        public IActionResult CarteraClientes()
        {
            return View();
        }

        public IActionResult CargaInicial()
        {
            return View();
        }
        public IActionResult CargaInicialKepler()
        {
            return View();
        }
        public IActionResult ReporteCarteraCliente()
        {
            return View();
        }
    }
}