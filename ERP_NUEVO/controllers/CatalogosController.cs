using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.controllers
{
    public class CatalogosController : Controller
    {
        private readonly FacturacionDbContext db;

        public CatalogosController(FacturacionDbContext context)
        {
            db = context;
        }

        [HttpGet]
        [AllowAnonymous]
        public JsonResult ObtenerSucursalesPorEmpresa(int empresaId)
        {
            var sucursales = db.SucursalId
                .Where(s => s.EmpresaId == empresaId)
                .Select(s => new
                {
                    Value = s.Id_sucursal,
                    Text = s.Id_sucursal + " - " + s.Descripcion
                })
                .ToList();

            return Json(sucursales);
        }
    }
}