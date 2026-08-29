using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Almacen
{
    /// <summary>
    /// Consulta de productos: listados paginados de la tabla principal
    /// y detalle que alimenta el modal de edición.
    /// </summary>
    public partial class ProductosController : Utilities
    {
        /// <summary>
        /// Página del catálogo, filtrada por clave o descripción.
        /// </summary>
        [Route("Almacen/Producto/Buscar")]
        public IActionResult Buscar(string nombre, int page = 1, int pageSize = 50)
        {
            return BuscarProductos(nombre, page, pageSize, soloServicios: false);
        }

        /// <summary>
        /// Igual que <see cref="Buscar"/> pero restringido a servicios
        /// (los que tienen unidad de medida SRV).
        /// </summary>
        [Route("Almacen/Producto/BuscarServicio")]
        public IActionResult BuscarServicio(string nombre, int page = 1, int pageSize = 50)
        {
            return BuscarProductos(nombre, page, pageSize, soloServicios: true);
        }

        private IActionResult BuscarProductos(string nombre, int page, int pageSize, bool soloServicios)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string filtroServicio = soloServicios ? " AND udm = 'SRV'" : "";
            string where = $@"
                WHERE empresa_id = @empresa_id{filtroServicio}
                AND (LOWER(descr_prod) LIKE LOWER(@nombre) OR LOWER(cve_prod) LIKE LOWER(@nombre))";

            string queryData = $@"
                SELECT cve_prod AS id, descr_prod AS descripcion, lin_prod, gpo, udm, stat
                FROM catproductos
                {where}
                ORDER BY cve_prod
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            string queryCount = $@"
                SELECT COUNT(*)
                FROM catproductos
                {where}";

            var data = RunQuery(queryData, parameters);
            var total = RunScalar(queryCount, parameters);

            return Json(new { data, total });
        }

        /// <summary>
        /// Detalle completo de un producto: datos propios, relación con
        /// el catálogo del SAT y fracción arancelaria.
        /// </summary>
        [Route("Almacen/Producto/BuscarProducto")]
        [HttpGet]
        public IActionResult BuscarProducto(string idproducto)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("idproducto", idproducto);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query =
                "SELECT cp.cve_prod, cp.stat, cp.es_kit, cp.descr_prod, cp.lin_prod, cp.tp, cp.gpo, cp.fmcan, cp.udm, cp.n_img, cr.prod_sat, " +
                "   cr.ud_sat, cr.ud_sec, cr.iva_ex, cr.obj_impto, cp.pv1 , cp.pv2, cp.pv3, cp.pv4, cp.pv5, cp.pv6, cp.pv7, cp.pv8, fa.peso, " +
                "   fa.peso2, fa.peso3, fa.frac, cp.id_catproductos, cp.cve_secundaria, cp.cve_contratipo " +
                "FROM catproductos cp " +
                "LEFT JOIN catrelacion cr ON cr.prod_kepler = cp.cve_prod " +
                "LEFT JOIN frac_arancelarias fa ON fa.cve_prod = cp.cve_prod " +
                "WHERE cp.empresa_id = @empresa_id AND cp.cve_prod = @idproducto ";

            return Json(RunQuery(query, parameters));
        }
    }
}
