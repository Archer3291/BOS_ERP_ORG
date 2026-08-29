using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public class DocumentosController : Controller
    {
        // ─── Vista principal ────────────────────────────────────────────────
        public IActionResult ConsultaDocumentos()
        {
            return View();
        }
        public IActionResult CancelacionDocumentos()
        {
            return View();
        }
        public IActionResult DocumentosPendientes()
        {
            return View();
        }
    }
}