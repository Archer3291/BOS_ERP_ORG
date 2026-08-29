using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public class ComprasInternacionalesController : Utilities
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult PlaneadorCI()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string query = "select usuarioid, nombreusuario from usuarios where rolid = 11";
            result.Add("directores",RunQuery(query));
            return View(result);
        }

        public IActionResult RequisicionMaterialInternacional()
        {
            string query = "SELECT id, fecha, dolar, euro, peso " +
                "FROM tasas_cambio " +
                "ORDER BY fecha DESC " +
                "LIMIT 1;";

            var result = RunQuery(query);

            query = "SELECT id_icoterm, codigo, descripcion, activo FROM catalogo_incoterms;";
            var incoResult = RunQuery(query);
            ViewBag.TasaUSD = Convert.ToDecimal(result[0]["dolar"]);
            ViewBag.TasaEUR = Convert.ToDecimal(result[0]["euro"]);
            ViewBag.TasaMXN = Convert.ToDecimal(result[0]["peso"]);
            ViewBag.Incoterms = incoResult;

            return View();
        }

        public IActionResult GestionSolicitudesInternacional()
        {
            return View();
        }
        public IActionResult Reclasificacion()
        {
            return View();
        }
        public IActionResult Pedimentos()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string query = "SELECT gen || '-' || nat || '-' || EXTRACT(YEAR FROM fch)::text || '-' || fol_doc AS folio, " +
                "uuid FROM encabezadomov " +
                "WHERE nat = 'OCDI'";

            result.Add("folios", RunQuery(query));
            return View(result);
        }
    }
}