using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class GastosController : Utilities
    {
        public IActionResult SolicitudGasto()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT  em.gen || '-' || em.nat || '-' || TO_CHAR(em.fch, 'YY') || '-' || " +
                           "(COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text || " +
                           "CASE  " +
                           "        WHEN em.variacion > 0   " +
                           "        THEN '-' || num_to_letters(em.variacion)   " +
                           "        ELSE ''  " +
                           "    END AS folio " +
                           "FROM encabezadomov em   " +
                "WHERE nat = 'FSGTO' " +
                "ORDER BY (COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1) DESC;";
            var folioFactura = RunScalar(query, parameters);

            var modelo = new FolioInfo
            {
                Folio = folioFactura?.ToString() ?? "AF-FSGTO-26-1",
                Sucursal = "Sucursal Sur",
            };

            return View(modelo);

        }
        public IActionResult AprobacionGasto()
        {
            return View();
        }

        public IActionResult ConsultaSolicitudes()
        {
            return View();
        }
    }
}