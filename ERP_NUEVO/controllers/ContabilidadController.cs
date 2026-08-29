using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers
{
    [RightAuthorize("contabilidad"), Authorize]
    public partial class ContabilidadController : Utilities
    {
        [RightAuthorize("conceptos")]
        public IActionResult Conceptos()
        {
            return View();
        }

        [RightAuthorize("departamentos")]
        public IActionResult Departamentos()
        {
            return View();
        }

        [RightAuthorize("polizas")]
        public IActionResult PolizasEspeciales()
        {
            var returnResult = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();

            string query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, em.iva, em.imp, em.coment_aut, em.usr1, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END folio, " +
                "   (SELECT nombre || ' ' || apellido AS responsable " +
                "       FROM usuarios " +
                "       WHERE nombreusuario = em.usr_doc) AS responsable," +
                "   em.fch, em.uuid " +
                "FROM encabezadomov em " +
                "WHERE em.estatus_id = 11 AND em.nat IN ('OC', 'GTO', 'VNFAC', 'OCD') AND em.suc = @sucursal" +
                "   AND NOT EXISTS (" +
                "       SELECT 1 FROM polizas p WHERE p.referencia = em.id_encabezado" +
                "   )";

            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            var result = RunQuery(query, parameters);
            returnResult.Add("folios", result);

            return View(returnResult);
        }

        [RightAuthorize("polizas")]
        public IActionResult ConsultarPolizas()
        {
            return View();
        }

        [RightAuthorize("carga_inicial")]
        public IActionResult Index()
        {
            return View();
        }

        [RightAuthorize("configuracion_polizas")]
        public IActionResult ConfiguracionPolizas()
        {
            return View();
        }

        public IActionResult HistorialFacturas()
        {
            return View();
        }

        public IActionResult BalanzaComprobacion()
        {
            return View();
        }

        public IActionResult MayorAuxiliar()
        {
            return View();
        }
        
        public IActionResult EstadoResultados()
        {
            return View();
        }

        public IActionResult CostoInventarioReporte()
        {
            return View();
        }

        public IActionResult Bancos()
        {
            return View();
        }
        
        public IActionResult BalanzaGeneral()
        {
            return View();
        }

        public IActionResult EstadoResultadosPersonalizado()
        {
            return View();
        }

        [RightAuthorize("cuentas_finanzas")]
        public IActionResult CuentasFinanzas()
        {
            return View();
        }

        public IActionResult MigrarKepler()
        {
            return View();
        }
    }
}