using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Almacen
{
    public class ConsultaInventarioController : Utilities
    {
        #region Obtener datos
        // Obtiene todos los productos que tienen stock
        public JsonResult GetTraslados(string nombre, string sortColumn, string sortDir, int? id_sucursal, int? id_almacen, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("nombre", nombre ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("sucursal", id_sucursal);
            parameters.Add("almacen", id_almacen);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var allowedColumns = new HashSet<string> {
                "codigo", "descripcion",
                "almacendescripcion", "cantidad", "ulocation",
                "cve_almacen", "tarima", "costo_promedio_unitario", "costo_promedio_total"
            };

            if (!allowedColumns.Contains(sortColumn))
                sortColumn = "codigo";

            string where = "";

            if (!string.IsNullOrWhiteSpace(nombre))
                where = " AND (cp.cve_prod ILIKE '%' || @nombre || '%' " +
                    "OR cp.descr_prod ILIKE '%' || @nombre || '%' " +
                    "OR ca.descripcion ILIKE '%' || @nombre || '%' " +
                    "OR cn.ulocation ILIKE '%' || @nombre || '%' " +
                    "OR ct.codigo ILIKE '%' || @nombre || '%' " +
                    "OR ca.cve_almacen ILIKE '%' || @nombre || '%' ) ";

            if (id_almacen.HasValue)
                where += " AND ca.id_almacen = @almacen ";

            if (id_sucursal.HasValue)
                where += " AND cu.id_sucursal = @sucursal ";

            string query = "SELECT cp.id_catproductos AS id_producto, ca.id_almacen, ct.id_tarima, cp.cve_prod AS codigo, cp.descr_prod AS descripcion, " +
                "    cp.udm AS unidadproducto, ct.codigo AS tarima, tp.cantidad, cn.ulocation, cun.descripcion AS unidadnombre, " +
                "    cun.id_udm AS unidad, ca.cve_almacen, ca.descripcion AS almacendescripcion, ca.tipo AS tipoalmacen, cu.cve_sucursal AS clavesucursal, " +
                "    cu.descripcion AS descripcionsucursal, cu.id_sucursal, " +
                "    COALESCE(( " +
                "        SELECT  " +
                "            SUM(rc.cantidad_restante * rc.costo_unitario) / NULLIF(SUM(rc.cantidad_restante), 0) " +
                "        FROM registro_compras rc " +
                "        WHERE rc.producto_id = cp.id_catproductos " +
                "    ), 0) AS costo_promedio_unitario, " +
                "    COALESCE(( " +
                "        SELECT  " +
                "            (SUM(rc.cantidad_restante * rc.costo_unitario) / NULLIF(SUM(rc.cantidad_restante), 0)) * tp.cantidad " +
                "        FROM registro_compras rc " +
                "        WHERE rc.producto_id = cp.id_catproductos " +
                "    ), 0) AS costo_promedio_total " +
                "FROM tarima_productos tp " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id " +
                "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cu ON cu.id_sucursal = ca.sucursal_id " +
                "INNER JOIN catunidades cun ON cun.id_udm = tp.unidad " +
                $"WHERE tp.cantidad > 0 {where} " +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var traslados = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM tarima_productos tp " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id " +
                "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cu ON cu.id_sucursal = ca.sucursal_id " +
                "INNER JOIN catunidades cun ON cun.id_udm = tp.unidad " +
                $"WHERE tp.cantidad > 0 {where}";
            int total = Convert.ToInt32(RunScalar(query, parameters));
            return Json(new { data = traslados, total });
        }

        // Obtiene las ubicaciones como sucursales y demas
        public JsonResult GetUbicaciones()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();
            string query = "SELECT ct.id_tarima, ct.codigo, ca.cve_almacen, ca.tipo, cs.id_sucursal, cn.ulocation " +
                "FROM cattarimas ct " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id";
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            result.Add("tarimas", RunQuery(query, parameters));

            query = "SELECT id_sucursal, cve_sucursal, descripcion, empresa_id FROM catsucursales";
            result.Add("sucursales", RunQuery(query));

            query = "SELECT cata.id_almacen, cata.cve_almacen, cata.descripcion almacen_descripcion, cata.tipo, " +
                "   cats.cve_sucursal, cats.descripcion sucursal_descripcion, cats.id_sucursal " +
                "FROM catalmacenes cata " +
                "INNER JOIN catsucursales cats ON cats.id_sucursal = cata.sucursal_id";
            result.Add("almacenes", RunQuery(query));

            query = "SELECT empresaid, rfc, nombre FROM empresas";
            result.Add("empresas", RunQuery(query));

            return Json(result);
        }
        #endregion
    }
}