using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    [RightAuthorize(new string[] { "inventarios" }, "ERP_SRS")]
    public partial class InventarioController : Utilities
    {
        public IActionResult TipoDeMovimiento()
        {
            return View();
        }

        public IActionResult GestionYAdministracion()
        {
            return View();
        }

        public IActionResult Recepcion()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            string query = "SELECT em.uuid, em.variacion," +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                "FROM encabezadomov em " +
                "WHERE estatus_id = 17 AND nat IN ('GTO', 'OC', 'OCD') AND em.suc = @sucursal";

            result.Add("folios", RunQuery(query, parameters));

            query = "SELECT em.uuid, em.variacion, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                "FROM encabezadomov em " +
                "WHERE estatus_id IN (17, 26) AND nat IN ('OCDI') AND em.suc = @sucursal";

            result.Add("folios_internacionales", RunQuery(query, parameters));

            return View(result);
        }

        public IActionResult Inspeccion()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));

            string query = "SELECT em.uuid, em.variacion," +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                "FROM encabezadomov em " +
                "WHERE estatus_id = 18 AND em.suc = @sucursal";

            result.Add("folios", RunQuery(query, parameters));
            return View(result);
        }

        public IActionResult Consulta()
        {
            return View();
        }

        public IActionResult Cuarentenas()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string query = "SELECT em.uuid, em.variacion," +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                "FROM encabezadomov em " +
                "WHERE estatus_id = 19";

            result.Add("folios", RunQuery(query));

            return View(result);
        }

        public IActionResult CalculadoraPesos()
        {
            return View();
        }
        public IActionResult ValidacionDiscrepancias()
        {
            return View();
        }

        public IActionResult InventarioVirtual()
        {
            return View();
        }

        public IActionResult AlmacenVirtual()
        {
            var parameters = new Dictionary<string, object>
            {
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            var sucursales = RunQuery(@"
                SELECT
                    id_sucursal AS id,
                    descripcion
                FROM catsucursales
                WHERE empresa_id = @empresa_id
                ORDER BY descripcion
            ", parameters);

            ViewBag.Sucursales = sucursales;

            return View();
        }

        public IActionResult MigrarInventario()
        {
            return View();
        }

        public IActionResult ConsultarDocumentos()
        {
            return View();
        }
    }
}