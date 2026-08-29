using BOS_ERP.Models;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using static BOS_ERP.Controllers.SqlHelper;

namespace BOS_ERP.Controllers
{
    [RightAuthorize("credito_cobranza")]
    public class CreditoCobranzaController : Utilities
    {
        [RightAuthorize("gestion_clientes")]
        public IActionResult Clientes()
        {
            return View();
        }

        [RightAuthorize("zonas")]
        public IActionResult Zonas()
        {
            return View();
        }

        public IActionResult TipoClientes()
        {
            return View();
        }

        public IActionResult Incoterms()
        {
            return View();
        }

        public IActionResult Vendedores()
        {
            return View();
        }
        
        public IActionResult AgentesCobranza()
        {
            return View();
        }

        [RightAuthorize(new[] { "facturacion_especial", "factura_arrendamiento" })]
        public IActionResult FacturacionEspecial()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT \r\n    'VINFAC-' || (COALESCE(MAX(NULLIF(fol_doc, '')::integer),0) + 1) AS folio_siguiente\r\nFROM encabezadomov\r\nWHERE nat = 'VINFAC';";
            var folioFactura = RunScalar(query, parameters);

            query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'FAR' " +
                "ORDER BY folio DESC;";
            var folioFacturaPredial = RunScalar(query, parameters);

            var modelo = new FolioInfoGroup
            {
                ParcialC = new FolioInfo
                {
                    Folio = folioFactura?.ToString() ?? "VINFAC-1",
                    Sucursal = "Sucursal Sur",
                    Almacen = "A3"
                },
                ParcialB = new FolioInfo
                {
                    Folio = folioFacturaPredial?.ToString() ?? "FAC-FAR-26-1",
                    Sucursal = "Sucursal Sur",
                    Almacen = "A3"
                }

            };

            return View(modelo);
        }

        [RightAuthorize(new[] { "facturacion_especial", "facturacion_normal", "facturacion_global", "complemento_pago" })]
        public IActionResult ConsultarFacturas()
        {
            return View();
        }

        [RightAuthorize("facturacion_global")]
        public IActionResult ConsultarFactuasGlobales()
        {
            return View();
        }

        [RightAuthorize("pago_proveedor")]
        public IActionResult PagoProveedor()
        {
            return View();
        }

        [RightAuthorize("cobro_clientes")]
        public IActionResult PagoCliente()
        {
            return View();
        }

        [RightAuthorize("complemento_pago")]
        public IActionResult ComplementoPago()
        {
            return View();
        }

        public IActionResult CobranzaFactura()
        {
            return View();
        }
        public IActionResult AplicacionNotasCredito()
        {
            return View();
        }
        public IActionResult ReporteVendedor()
        {
            return View();
        }
    }
}