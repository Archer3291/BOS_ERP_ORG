using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Catálogos que alimentan los Tom Selects de la pantalla:
    /// la cascada de dirección (país → estado → municipio → cp → colonia),
    /// vendedores, incoterms y catálogos fiscales del SAT.
    /// </summary>
    public partial class ClientesController : Utilities
    {
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetVendedores()
        {
            string query = "SELECT clave_vendedor, nombre FROM vendedores";
            var vendedores = RunQuery(query);

            return Json(vendedores);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetPaises()
        {
            string query = "SELECT id_pais, cve_pais, nombre, cve_iso FROM paises";
            var paises = RunQuery(query);

            return Json(paises);
        }

        /// <summary>
        /// Estados del país indicado. Para clientes nacionales se
        /// restringe siempre a México.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetEstados(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string where = "";

            if (Convert.ToBoolean(fc["internacional"].ToString()))
            {
                where = " AND pais_id = @pais ";
                parameters.Add("pais", Convert.ToInt32(fc["pais"].ToString()));
            }
            else
            {
                where = " AND cve_pais = 'MX' ";
            }

            string query = $"SELECT id_estado, nombre, cve_estado FROM estados WHERE 1 = 1 {where} ORDER BY nombre ASC";
            var estados = RunQuery(query, parameters);

            return Json(estados);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetMunicipios(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT m.id_municipio, m.nombre, m.numero_municipio " +
                "FROM municipios m " +
                "WHERE m.estado_id = @estado " +
                "ORDER BY m.nombre ASC";
            parameters.Add("estado", Convert.ToInt32(fc["estado"].ToString()));
            var municipio = RunQuery(query, parameters);

            return Json(municipio);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetCodigosPostales(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT DISTINCT c.cp " +
                "FROM colonias c " +
                "WHERE c.municipio_id = @municipio_id " +
                "ORDER BY c.cp ASC";
            parameters.Add("municipio_id", Convert.ToInt32(fc["municipio_id"].ToString()));
            var codigos = RunQuery(query, parameters);

            return Json(codigos);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetColonias(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT c.id_colonia, c.cp, c.nombre " +
                "FROM colonias c " +
                "WHERE c.cp = @cp";
            parameters.Add("cp", fc["cp"].ToString());
            var colonias = RunQuery(query, parameters);

            return Json(colonias);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetIncoterms()
        {
            string query = "SELECT id_icoterm, codigo, descripcion FROM catalogo_incoterms WHERE activo = true";
            var incoterms = RunQuery(query);

            return Json(incoterms);
        }

        /// <summary>
        /// Catálogos del SAT para la pestaña de facturación:
        /// formas de pago, regímenes fiscales y usos de CFDI.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDatosFiscales()
        {
            string query = "SELECT cve_sat, descripcion FROM cat_f_pago";
            var fpago = RunQuery(query);

            query = "SELECT descripcion, clave FROM catregimenfiscal";
            var regimen = RunQuery(query);

            query = "SELECT clave, descripcion FROM catusocfdi";
            var cfdi = RunQuery(query);

            return Json(new { fpago, regimen, cfdi });
        }

        /// <remarks>
        /// Devuelve exactamente lo mismo que <see cref="GetDatosFiscales"/> y
        /// hoy ningún cliente la invoca; se conserva por compatibilidad.
        /// </remarks>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDatosAdenda()
        {
            return GetDatosFiscales();
        }
    }
}
