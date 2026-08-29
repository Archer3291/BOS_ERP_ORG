using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public partial class DatosGeneralesController : Utilities
    {


        public IActionResult BuscarVSD(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.folio ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            cc.n_cli
        FROM encabezadomov em 
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.folio) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VSCOT'
        AND em.suc =  @suc
        AND em.estatus_id = 1
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.folio) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VSCOT' AND em.suc =  @suc AND em.estatus_id = 1 ";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }


        public IActionResult BuscarDVSped(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            em.incoterm,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VSPED'
        AND em.suc =  @suc
        AND em.estatus_id = 1
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VSPED' AND em.suc =  @suc AND em.estatus_id = 1";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarDVSetiquetaRem(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.folio ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            em.incoterm,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VSREM'
        AND em.suc =  @suc
        AND em.estatus_id = 11
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VSPED' AND em.suc =  @suc AND em.estatus_id = 1";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        //public IActionResult BuscarDocumento(int id)
        //{
        //    var parameters = new Dictionary<string, object> { { "id", id } };

        //    string query = @"
        //SELECT 
        //    em.id_encabezado,
        //    em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc AS folio,
        //    em.cli_prov,
        //    em.rfc,
        //    em.imp,
        //    em.fch AS fecha,
        //    em.suc,
        //    em.usr0,
        //    json_agg(
        //        json_build_object(
        //            'producto_id', pd.cve_prod,
        //            'id', pd.cve_prod,
        //            'descripcion', pd.descr_prod,
        //            'cantidad', pd.cant_ud,
        //            'unidad', pd.ud,
        //            'precio', pd.pv_prod,
        //            'descuento', pd.dto1,
        //            'importe', pd.imp_part
        //        )
        //    ) AS productos
        //FROM encabezadomov em
        //LEFT JOIN partidasdoc pd ON pd.encabezado_id = em.id_encabezado
        //WHERE em.id_encabezado = @id
        //GROUP BY em.id_encabezado, em.gen, em.nat, em.fch, em.fol_doc, 
        //         em.cli_prov, em.rfc, em.imp, em.suc, em.usr0";

        //    var result = RunQuery(query, parameters);

        //    // Parsear el JSON de productos
        //    if (result != null && result.Count > 0 && result[0].ContainsKey("productos"))
        //    {
        //        var productosJson = result[0]["productos"]?.ToString();
        //        if (!string.IsNullOrEmpty(productosJson))
        //        {
        //            result[0]["productos"] = Newtonsoft.Json.JsonConvert.DeserializeObject(productosJson);
        //        }
        //    }

        //    return Json(result);
        //}

        public IActionResult BuscarDVSrem(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.folio ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            em.incoterm,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VSREM'
        AND em.suc =  @suc
        AND em.estatus_id != 11
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VSREM' AND em.suc =  @suc AND em.estatus_id = 1";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        // Remisiones de sucursal con saldo por facturar: alimenta el modal de facturación
        // especial (parcial / múltiple). Reutiliza el mismo helper que Industriales para que
        // el criterio de "tiene saldo" sea idéntico en los dos canales.
        public IActionResult BuscarDVSremFacturables(string nombre = "", int page = 1,
            int pageSize = 25, string cliente = "", string modo = "documento")
            => BuscarRemisionesFacturablesJson("VSREM", nombre, page, pageSize, cliente, modo);

        public IActionResult BuscarExistenciasPorAlmacen(string productoId)
        {
            try
            {
                // 1. Obtener ID real del producto
                var parameters = new Dictionary<string, object>
                {
                    { "productoId", productoId },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string query = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @productoId and empresa_id = @empresa_id ";

                var resultProduct = RunQuery(query, parameters);

                if (resultProduct.Count == 0)
                {
                    return Json(new { success = false, message = "Producto no encontrado" });
                }

                // Obtener el ID
                int idProducto = Convert.ToInt32(resultProduct[0]["id_catproductos"]);

                // 2. Usar el ID en la siguiente consulta
                var parameters2 = new Dictionary<string, object>
                {
                    { "productoId", idProducto }
                };

                query = @"
                        SELECT
                            ca.id_almacen,
                            ca.cve_almacen AS almacen,
                            SUM(tp.cantidad) AS existencia
                        FROM tarima_productos tp
                        INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                        INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                        INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                        INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                        INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                        WHERE ca.tipo = 'Stock'
                          AND tp.producto_id = @productoId
                        GROUP BY ca.id_almacen, ca.cve_almacen
                        ORDER BY ca.cve_almacen;";

                var result = RunQuery(query, parameters2);

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesVS/BuscarExistenciasPorAlmacen");
                return Json(new { success = false, message = ex.Message });
            }
        }


    }
}