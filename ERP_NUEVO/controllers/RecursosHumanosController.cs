using BOS_ERP.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.controllers
{
    public partial class RecursosHumanosController : Utilities
    {
        public IActionResult SolicitudSalas()
        {
            return View();
        }
    }
}
