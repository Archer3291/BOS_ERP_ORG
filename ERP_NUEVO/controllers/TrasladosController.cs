using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public class TrasladosController : Utilities
    {
        public IActionResult Traslados()
        {
            return View();
        }

        public IActionResult SolicitudInventario()
        {
            return View();
        }

        public IActionResult ReporteMovimientos()
        {
            return View();
        }

        public IActionResult SolicitudesRecibidas()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            var returnResult = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "   fch, em.uuid " +
                "FROM encabezadomov em " +
                "WHERE em.nat = 'SOLINV' AND em.estatus_id = 21 AND em.suc = @sucursal";
            var result = RunQuery(query, parameters);
            returnResult.Add("folios", result);

            query = "SELECT id_sucursal, cve_sucursal, descripcion FROM catsucursales WHERE empresa_id = @empresa";
            returnResult.Add("sucursales", RunQuery(query, parameters));

            return View(returnResult);
        }

        public IActionResult SolicitudesPendientes()
        {
            var returnResult = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();
            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "  fch, em.uuid " +
                "FROM encabezadomov em " +
                "WHERE em.nat = 'TRAINV' AND em.estatus_id = 22 AND suc_origen = @sucursal";
            parameters.Add("usuario", GetUserId(User.Identity.Name));
            parameters.Add("sucursal", GetInt(HttpContext.Session.GetInt32("Sucursal")));
            var result = RunQuery(query, parameters);
            returnResult.Add("folios", result);

            query = "SELECT id_sucursal, cve_sucursal, descripcion FROM catsucursales WHERE id_sucursal = @sucursal";
            returnResult.Add("sucursales", RunQuery(query, parameters));

            return View(returnResult);
        }

        public IActionResult RecepcionInventario()
        {
            var returnResult = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            parameters.Add("usuario", GetUserId(User.Identity.Name));

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "  fch, em.uuid " +
                "FROM encabezadomov em " +
                "WHERE em.nat = 'ENVINV' AND em.estatus_id = 23 and em.suc = @sucursal";
            var result = RunQuery(query, parameters);
            returnResult.Add("folios", result);

            query = "SELECT id_sucursal, cve_sucursal, descripcion FROM catsucursales WHERE id_sucursal = @sucursal";
            returnResult.Add("sucursales", RunQuery(query, parameters));

            return View(returnResult);
        }

        public IActionResult DiscrepanciaTraslado()
        {
            var returnResult = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();
            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "   (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "  fch, em.uuid " +
                "FROM encabezadomov em " +
                "WHERE em.gen = 'INV' AND em.estatus_id = 25 AND em.usr3 = @usuario";
            parameters.Add("usuario", GetUserId(User.Identity.Name));
            var result = RunQuery(query, parameters);
            returnResult.Add("folios", result);

            query = "SELECT id_sucursal, cve_sucursal, descripcion FROM catsucursales WHERE id_sucursal = @sucursal";
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            returnResult.Add("sucursales", RunQuery(query, parameters));

            return View(returnResult);
        }
    }
}