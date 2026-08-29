using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Almacen
{
    /// <summary>
    /// Catálogos que alimentan el formulario de producto: líneas, tipos,
    /// grupos, unidades, proveedores y los catálogos del SAT.
    /// </summary>
    public partial class ProductosController : Utilities
    {
        /// <summary>
        /// Naturaleza del producto. Es un catálogo fijo del sistema,
        /// no vive en base de datos.
        /// </summary>
        private static readonly Dictionary<string, string> NaturalezaProducto = new()
        {
            { "F", "Fabricacion" },
            { "M", "Maquila" },
            { "C", "Compra" },
            { "A", "F y C" },
            { "N", "No inventario" }
        };

        /// <summary>
        /// Devuelve de una sola vez todos los catálogos del formulario.
        /// El front lo pide una vez y lo reutiliza para alta y edición.
        /// </summary>
        [Route("Almacen/Producto/Datos")]
        [HttpGet]
        public IActionResult Datos()
        {
            var returnResult = new Dictionary<string, List<Dictionary<string, object>>>();

            returnResult.Add("marcas", RunQuery("SELECT cve_grupo AS c1, descripcion AS c2 FROM catgrupo"));
            returnResult.Add("tipos", RunQuery("SELECT cve_tipo AS c1, descripcion AS c2 FROM cattipo_prd"));
            returnResult.Add("lineas", RunQuery("SELECT cve_linea AS c1, descripcion AS c2 FROM catlineas"));
            returnResult.Add("udm", RunQuery("SELECT cve_udm AS c1, descripcion AS c2 FROM catunidades"));

            returnResult.Add("naturalezaProducto", NaturalezaProducto
                .Select(kv => new Dictionary<string, object> { { "key", kv.Key }, { "value", kv.Value } })
                .ToList());

            var parameters = new Dictionary<string, object>();
            parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            returnResult.Add("proveedores", RunQuery(
                "SELECT id_prov, cve_prov AS clave, n_prov AS nombre FROM catproveedores WHERE id_empresa = @id_empresa",
                parameters));

            returnResult.Add("unidadsat", RunQuery("SELECT cve_unidad_sat, descripcion FROM catunidades_sat;"));
            returnResult.Add("objetoimpuestosat", RunQuery("SELECT cve_objeto_importacion, nombre FROM catobjeto_impuesto_sat;"));

            return Json(returnResult);
        }

        /// <summary>
        /// Búsqueda incremental en el catálogo de productos del SAT.
        /// Es demasiado grande para mandarlo completo al navegador.
        /// </summary>
        [Route("Almacen/Producto/BuscarProductosSat")]
        public IActionResult BuscarProductosSat(string q)
        {
            string query = @"
                SELECT cve_producto, descripcion
                FROM catproductos_sat
                WHERE descripcion ILIKE @q OR cve_producto ILIKE @q
                LIMIT 50;";

            var parametros = new Dictionary<string, object>
            {
                { "@q", "%" + (q ?? "") + "%" }
            };

            return Json(RunQuery(query, parametros));
        }
    }
}
