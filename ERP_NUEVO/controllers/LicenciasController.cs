using BOS_ERP.Models;
using System.Data.Entity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BOS_ERP.Controllers
{
    [RightAuthorize("sistemas")]
    [AreaAuthorize("Sistemas")]
    public class LicenciasController : Utilities
    {
        private readonly FacturacionDbContext db;

        public LicenciasController(FacturacionDbContext context)
        {
            db = context;
        }

        // GET: Licencias
        public IActionResult Licencias()
        {
            return View();
        }

        public JsonResult GetLicencias()
        {
            var licencias = db.Licencias
                .Include(l => l.Empresa)
                .ToList()
                .Select(l => new {
                    l.LicenciaId,
                    l.LicenciaCodigo,
                    FechaInicio = l.FechaInicio.ToString("yyyy-MM-ddTHH:mm:ss"),
                    FechaExpiracion = l.FechaExpiracion.ToString("yyyy-MM-ddTHH:mm:ss"),
                    l.Activa,
                    l.Regimen,
                    Empresa = new
                    {
                        Nombre = l.Empresa.Nombre,
                        RFC = l.Empresa.RFC
                    }
                });

            return Json(new { data = licencias });
        }


        [HttpGet]
        public IActionResult Create()
        {
            var empresas = db.Empresas
                .Select(e => new { e.EmpresaId, e.Nombre })
                .ToList();

            ViewBag.EmpresaId = new SelectList(empresas, "EmpresaId", "Nombre");
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Licencia licencia)
        {
            licencia.LicenciaCodigo = GenerarCodigoLicencia();
            if (ModelState.IsValid)
            {
                
                licencia.Activa = false;
                licencia.MaximoFacturasPermitidas = 100;
                licencia.FacturasUsadas = 0;
                licencia.UsuarioId = GetUserId(User.Identity.Name);

                db.Licencias.Add(licencia);
                db.SaveChanges();

                return RedirectToAction("Index");
            }

            var empresas = db.Empresas
                .Select(e => new { e.EmpresaId, e.Nombre })
                .ToList();
            ViewBag.EmpresaId = new SelectList(empresas, "EmpresaId", "Nombre", licencia.EmpresaId);

            return View(licencia);
        }


        [HttpGet]
        public IActionResult Edit(int? id)
        {
            if (id == null)
                return BadRequest();

            // Buscar la licencia por ID
            Licencia licencia = db.Licencias.Find(id);
            if (licencia == null)
                return NotFound();

            // Asegúrate de que la empresa seleccionada esté bien pasando
            var empresas = db.Empresas.ToList();
            // Se pasa la lista de empresas al ViewBag con el valor seleccionado (empresa.Id)
            ViewBag.EmpresaId = new SelectList(empresas, "EmpresaId", "Nombre", licencia.EmpresaId);

            // Pasamos el modelo de la licencia a la vista
            return View(licencia);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Licencia licencia)
        {
            if (ModelState.IsValid)
            {
                var licenciaExistente = db.Licencias.Find(licencia.LicenciaId);
                if (licenciaExistente == null)
                    return NotFound();

                licenciaExistente.TipoLicencia = licencia.TipoLicencia;
                licenciaExistente.FechaInicio = licencia.FechaInicio;
                licenciaExistente.FechaExpiracion = licencia.FechaExpiracion;
                licenciaExistente.RFC = licencia.RFC;
                licenciaExistente.RazonSocial = licencia.RazonSocial;
                licenciaExistente.DireccionFiscal = licencia.DireccionFiscal;
                licenciaExistente.Pais = licencia.Pais;
                licenciaExistente.Estado = licencia.Estado;
                licenciaExistente.Ciudad = licencia.Ciudad;
                licenciaExistente.Regimen = licencia.Regimen;
                licenciaExistente.EmpresaId = licencia.EmpresaId;

                db.SaveChanges();
                return RedirectToAction("Index");
            }

            //  Aquí se vuelve a llenar ViewBag pero ModelState puede tener el valor anterior
            ModelState.Remove("EmpresaId"); //  Esto es clave
            var empresas = db.Empresas.ToList();
            ViewBag.EmpresaId = new SelectList(empresas, "EmpresaId", "Nombre", licencia.EmpresaId);

            return View(licencia);
        }





        // Método para activar/inactivar licencia
        public IActionResult CambiarEstado(int id)
        {
            var licencia = db.Licencias.Find(id);
            if (licencia != null)
            {
                licencia.Activa = !licencia.Activa;
                db.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        private string GenerarCodigoLicencia()
        {
            var random = new Random();
            string letras = new string(Enumerable.Repeat("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 4)
                                    .Select(s => s[random.Next(s.Length)]).ToArray());
            string numeros = random.Next(1000, 9999).ToString();
            string año = DateTime.Now.Year.ToString();
            return $"LIC-{año}-{letras}-{numeros}";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}
