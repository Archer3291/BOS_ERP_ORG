using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers
{

    [Authorize]
    public class Tubos : Utilities
    {
        // GET: Tubos
        public IActionResult Index()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT em.gen || '-' || em.nat || '-' || to_char(em.fch, 'YY') || '-' || " +
                "    LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "    CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VSPED' " +
                "ORDER BY folio DESC;";
            var folioPedido = RunScalar(query, parameters);

            query = "SELECT em.gen || '-' || em.nat || '-' || to_char(em.fch, 'YY') || '-' || " +
                "    LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "    CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VSREM' " +
                "ORDER BY folio DESC;";
            var folioRemision = RunScalar(query, parameters);

            query = "SELECT em.gen || '-' || em.nat || '-' || to_char(em.fch, 'YY') || '-' || " +
                "    LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "    CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VSFAC' " +
                "ORDER BY folio DESC;";
            var folioFactura = RunScalar(query, parameters);


            var modelo = new FolioInfoGroup
            {
                ParcialA = new FolioInfo
                {
                    Folio = folioPedido?.ToString() ?? "VSUC-VSPED-2025-0000001",
                    Sucursal = "Matriz",
                    Almacen = "A1"
                },
                ParcialB = new FolioInfo
                {
                    Folio = folioRemision?.ToString() ?? "VSUC-VSREM-2025-0000001",
                    Sucursal = "Sucursal Norte",
                    Almacen = "A2"
                },
                ParcialC = new FolioInfo
                {
                    Folio = folioFactura?.ToString() ?? "VSUC-VSFAC-2025-0000001",
                    Sucursal = "Sucursal Sur",
                    Almacen = "A3"
                }

            };
            return View(modelo);
        }


        public IActionResult CorteOperacion()
        {
            return View();
        }
        public IActionResult AdminCortes()
        {
            return View();
        }
        
    }
}
