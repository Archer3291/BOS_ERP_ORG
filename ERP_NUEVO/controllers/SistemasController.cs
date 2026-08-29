using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BOS_ERP.Controllers
{
    [RightAuthorize("sistemas")]
    [AreaAuthorize("Sistemas")]
    public class SistemasController : Controller
    {
        private readonly FacturacionDbContext db;

        public SistemasController(FacturacionDbContext context)
        {
            db = context;
        }

        public IActionResult Paridades()
        {
            return View();
        }

        public IActionResult Usuarios()
        {
            var roles = db.Roles.ToList(); // Asegúrate de tener acceso a la tabla Roles
            var empresas = db.Empresas.ToList();
            var permisos = db.Permisos.ToList();
            var areas = db.AreaId.ToList();
            ViewBag.Roles = new SelectList(roles, "RolId", "Nombre");
            ViewBag.Empresas = new SelectList(empresas, "EmpresaId", "Nombre");
            ViewBag.Permisos = new SelectList(permisos, "Id_permiso", "Descripcion");
            ViewBag.Areas = new SelectList(areas, "Id_area", "Nombre");
            ViewBag.PermisosRaw = permisos;

            return View();
        }

        public IActionResult Documentos()
        {
            return View();
        }
        public IActionResult DocumentosRelacion()
        {
            return View();
        }

        public IActionResult UserSessions()
        {
            return View();
        }
        public IActionResult UsuarioClaveModulo()
        {
            return View();
        }
    }
}