using Npgsql;
using BOS_ERP.Models;
using System.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public class KitsController : Utilities
    {
        public IActionResult KitsManagement()
        {
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GenerarKit(KitsModel kit)
        {
            var parameters = new Dictionary<string, object>
            {
                { "@id_kit", kit.KitId },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            string query = "SELECT COUNT(*) FROM catproductos WHERE id_catproductos = @id_kit AND empresa_id = @empresa_id";
            int qty = Convert.ToInt32(RunScalar(query, parameters));

            if (qty == 0)
            {
                return Json(new { icon = "error", title = "Ocurrió un error", html = "No se encontró un producto relacionado a este kit", showCancelButton = false });
            }

            if (kit.Materiales == null || !kit.Materiales.Any())
            {
                return Json(new { icon = "error", title = "Ocurrió un error", html = "El kit debe contener al menos un material", showCancelButton = false });
            }

            foreach (var prod in kit.Materiales)
            {
                if (prod.Cantidad <= 0)
                {
                    return Json(new { icon = "error", title = "Ocurrió un error", html = "La cantidad del material número " + prod.Orden + " debe ser mayor a cero", showCancelButton = false });
                }

                if (prod.Unidad <= 0)
                {
                    return Json(new { icon = "error", title = "Ocurrió un error", html = "La unidad del material número " + prod.Orden + " no es válida", showCancelButton = false });
                }

                string queryProd = "SELECT COUNT(*) FROM catproductos WHERE id_catproductos = @id_producto AND empresa_id = @empresa_id";
                var parametersProd = new Dictionary<string, object>
                {
                    { "@id_producto", prod.ProductoId },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };
                int qtyProd = Convert.ToInt32(RunScalar(queryProd, parametersProd));

                if (qtyProd == 0)
                {
                    return Json(new { icon = "error", title = "Ocurrió un error", html = "No se encontró el producto relacionado al material número " + prod.Orden, showCancelButton = false });
                }

                if (GeneraCiclo(kit, prod))
                {
                    return Json(new { icon = "error", title = "Ocurrió un error al registrar el kit", html = "El material número " + prod.Orden + " genera una relación circular entre kits", showCancelButton = false });
                }
            }

            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            var documento = new Dictionary<string, object>();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        parameters = new Dictionary<string, object>();
                        parameters.Add("@id_kit", kit.KitId);

                        query = "DELETE FROM kits_productos WHERE kit_id = @id_kit";
                        RunQuery(query, parameters, false, conn, tx);

                        var encabezado = new DocumentoEncabezado();
                        encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                        encabezado.IdArea = 6;
                        encabezado.IdTpDoc = 68;
                        encabezado.TpMov = "CKIT";
                        encabezado.Anio = DateTime.Now.Year;
                        encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                        encabezado.Fch = DateTime.Now;
                        encabezado.UsrDoc = User.Identity.Name;
                        encabezado.FchCap = DateTime.Now;
                        encabezado.Usr0 = GetUserId(User.Identity.Name);
                        encabezado.Fch0 = DateTime.Now;
                        encabezado.Estatus = 19;
                        encabezado.UsrDep = GetAreaName(User.Identity.Name);
                        encabezado.TipoPoceso = "ingreso_cuerantena";
                        encabezado.CentroCostos = 6;
                        encabezado.CliProv = "";

                        var partidas = new List<PartidaDocumento>();
                        var productosUsados = new List<Dictionary<string, object>>();

                        foreach (var prod in kit.Materiales)
                        {
                            parameters = new Dictionary<string, object>();
                            parameters.Add("id_kit", kit.KitId);
                            parameters.Add("id_producto", prod.ProductoId);
                            parameters.Add("unidad", prod.Unidad);

                            query = "SELECT cve_udm FROM catunidades WHERE id_udm = @unidad";
                            var unidadClave = RunScalar(query, parameters, false, conn, tx)?.ToString();
                            query = "SELECT cve_prod, descr_prod FROM catproductos WHERE id_catproductos = @id_producto";
                            var producto = RunQuery(query, parameters, false, conn, tx)[0];

                            var partida = new PartidaDocumento();
                            partida.NroPart = partidas.Count + 1;
                            partida.IdProducto = prod.ProductoId;
                            partida.CantUd = prod.Cantidad;
                            partida.Ud = unidadClave;
                            partida.CveProd = producto["cve_prod"].ToString();
                            partida.DescrProd = producto["descr_prod"].ToString();
                            partidas.Add(partida);

                            productosUsados.Add(new Dictionary<string, object>
                            {
                                { "id_producto", prod.ProductoId },
                                { "codigo", producto["cve_prod"].ToString() },
                                { "descripcion", producto["descr_prod"].ToString() },
                                { "cantidad", prod.Cantidad },
                                { "unidad", prod.Unidad }
                            });
                        }

                        documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                        query = "INSERT INTO kits_productos (kit_id, producto_id, cantidad, unidad, orden) " +
                            "VALUES (@id_kit, @id_producto, @cantidad, @unidad, @orden)";
                        foreach (var prod in kit.Materiales)
                        {
                            parameters = new Dictionary<string, object>();
                            parameters.Add("id_kit", kit.KitId);
                            parameters.Add("id_producto", prod.ProductoId);
                            parameters.Add("cantidad", prod.Cantidad / kit.Cantidad);
                            parameters.Add("unidad", prod.Unidad);
                            parameters.Add("orden", prod.Orden);

                            RunUpdate(query, parameters, false, conn, tx);
                        }

                        parameters = new Dictionary<string, object>();
                        query = "SELECT ct.id_tarima FROM catalmacenes c " +
                            "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                            "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                            "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                            "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                            "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                            "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Stock'";
                        parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                        int destino = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                        RegistrarMovimiento(productosUsados, GetUserId(User.Identity.Name), "consumo", destino, null, "Creacion de kit", Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);

                        query = "SELECT cve_prod, descr_prod FROM catproductos WHERE id_catproductos = @producto AND empresa_id = @empresa_id";
                        parameters.Add("producto", kit.KitId);
                        parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        var produ = RunQuery(query, parameters, false, conn, tx)[0];
                        var kitCreado = new List<Dictionary<string, object>>();
                        kitCreado.Add(new Dictionary<string, object>
                        {
                            { "id_producto", kit.KitId },
                            { "codigo", produ["cve_prod"].ToString() },
                            { "descripcion", produ["descr_prod"].ToString() },
                            { "cantidad", kit.Cantidad },
                            { "unidad", 11 }
                        });

                        RegistrarMovimiento(kitCreado, GetUserId(User.Identity.Name), "ingreso", null, destino, "Creacion de kit", Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);

                        // ✔ Todo bien → commit
                        tx.Commit();
                    }
                    catch
                    {
                        // 💣 Algo explotó → rollback real
                        tx.Rollback();
                        throw;
                    }
                }
            }

            return Json(new { icon = "success", title = "Kit registrado correctamente", showCancelButton = false });
        }

        #region Validaciones de kit
        private bool GeneraCiclo(KitsModel kit, MaterialModel prod)
        {
            return GeneraCicloInterno(kit.KitId, prod.ProductoId, new HashSet<int>());
        }

        private bool GeneraCicloInterno(int kitId, int productoId, HashSet<int> visitados)
        {
            if (!visitados.Add(productoId))
                return false;

            if (kitId == productoId)
                return true;

            if (!EsKit(productoId))
                return false;

            var hijos = ObtenerMaterialesDelKit(productoId);

            foreach (var hijo in hijos)
            {
                if (GeneraCicloInterno(kitId, hijo.ProductoId, visitados))
                    return true;
            }

            return false;
        }

        private bool EsKit(int productoId)
        {
            string query = "SELECT es_kit FROM catproductos WHERE id_catproductos = @id AND empresa_id = @empresa_id";
            var parameters = new Dictionary<string, object>
            {
                { "@id", productoId },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            return Convert.ToBoolean(RunScalar(query, parameters));
        }

        private List<MaterialModel> ObtenerMaterialesDelKit(int kitId)
        {
            string query = "SELECT producto_id AS productoid, cantidad AS cantidad " +
                "FROM kits_productos " +
                "WHERE kit_id = @id_kit";

            var parameters = new Dictionary<string, object>
            {
                { "@id_kit", kitId }
            };

            var rows = RunQuery(query, parameters);

            var materiales = new List<MaterialModel>();

            foreach (var row in rows)
            {
                materiales.Add(new MaterialModel
                {
                    ProductoId = Convert.ToInt32(row["productoid"]),
                    Cantidad = Convert.ToDecimal(row["cantidad"])
                });
            }

            return materiales;
        }
        #endregion

        #region Consultas de kit
        public JsonResult DatosSelect(int page = 1, int pageSize = 50, string search = "")
        {
            var result = new Dictionary<string, object>();

            // Calcular offset
            int offset = (page - 1) * pageSize;

            // Query con búsqueda y paginación
            string queryProductos = @"
                WITH ProductosConStock AS (
                    SELECT  
                        cp.id_catproductos AS id,
                        cp.descr_prod AS name,
                        cp.cve_prod as sku,
                        '📦' AS icon,
                        COALESCE(SUM(tp.cantidad), 0) AS stock,
                        cp.udm AS unit,
                        cun.id_udm,
                        cun.descripcion AS unidadnombre
                    FROM catproductos cp
                    LEFT JOIN tarima_productos tp 
                        ON tp.producto_id = cp.id_catproductos
                    LEFT JOIN catunidades cun 
                        ON cun.id_udm = tp.unidad
                    LEFT JOIN cattarimas ct 
                        ON ct.id_tarima = tp.tarima_id
                    LEFT JOIN catniveles cn 
                        ON cn.id_nivel = ct.nivel_id
                    LEFT JOIN catcolumnas cc 
                        ON cc.id_columna = cn.columna_id
                    LEFT JOIN catracks cr 
                        ON cr.id_rack = cc.rack_id
                    LEFT JOIN catalmacenes ca 
                        ON ca.id_almacen = cr.almacen_id
                        AND ca.tipo = 'Stock'
                    LEFT JOIN catsucursales csu 
                        ON csu.id_sucursal = ca.sucursal_id
                        AND csu.id_sucursal = @sucursal
                    WHERE (@search = '' OR 
                        cp.cve_prod ILIKE '%' || @search || '%' OR 
                        cp.descr_prod ILIKE '%' || @search || '%')
                    AND cp.empresa_id = @empresa_id
                    GROUP BY 
                        cp.id_catproductos,
                        cp.cve_prod,
                        cp.descr_prod,
                        cp.udm,
                        cun.id_udm,
                        cun.descripcion
                )
                SELECT * FROM (
                    SELECT *, 
                            ROW_NUMBER() OVER (ORDER BY sku) AS RowNum,
                            COUNT(*) OVER() AS TotalRecords
                    FROM ProductosConStock
                ) AS Numbered
                WHERE RowNum > @offset AND RowNum <= @offset + @pageSize
                ORDER BY sku";

            var productos = RunQuery(queryProductos, new Dictionary<string, object>
                {
                    { "@search", search },
                    { "@offset", offset },
                    { "@pageSize", pageSize },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                    { "sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                });

            int totalRecords = productos.Count > 0
                ? Convert.ToInt32(productos[0]["totalrecords"])
                : 0;

            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

            result.Add("productos", productos);
            result.Add("pagination", new Dictionary<string, object>
                {
                    { "currentPage", page },
                    { "pageSize", pageSize },
                    { "totalRecords", totalRecords },
                    { "totalPages", totalPages },
                    { "hasNextPage", page < totalPages },
                    { "hasPreviousPage", page > 1 }
                });

            return Json( new { Data = result, MaxJsonLength = int.MaxValue });
        }

        public JsonResult ObtenerKits(int page = 1, int pageSize = 50, string search = "")
        {
            var result = new Dictionary<string, object>();
            int offset = (page - 1) * pageSize;

            // Query para obtener kits con paginación
            string queryKits = @"
    WITH KitsConStock AS (
        SELECT
            cp.id_catproductos AS id,
            cp.cve_prod AS code,
            cp.descr_prod AS name,
            '📦' AS icon,
            COALESCE(SUM(tp.cantidad), 0) AS stock,
            cp.udm AS unit,
            EXISTS (
                SELECT 1
                FROM kits_productos kp
                WHERE kp.kit_id = cp.id_catproductos
            ) AS hascomponents
        FROM catproductos cp
        LEFT JOIN tarima_productos tp
            ON tp.producto_id = cp.id_catproductos
        LEFT JOIN cattarimas ct
            ON ct.id_tarima = tp.tarima_id
        LEFT JOIN catniveles cn
            ON cn.id_nivel = ct.nivel_id
        LEFT JOIN catcolumnas cc
            ON cc.id_columna = cn.columna_id
        LEFT JOIN catracks cr
            ON cr.id_rack = cc.rack_id
        LEFT JOIN catalmacenes ca
            ON ca.id_almacen = cr.almacen_id
            AND ca.tipo = 'Stock'
        LEFT JOIN catsucursales csu
            ON csu.id_sucursal = ca.sucursal_id
            AND csu.id_sucursal = 7
        WHERE cp.es_kit = true
            AND (@search = '' OR 
                 cp.cve_prod ILIKE '%' || @search || '%' OR 
                 cp.descr_prod ILIKE '%' || @search || '%')
            AND cp.empresa_id = @empresa_id
        GROUP BY
            cp.id_catproductos,
            cp.cve_prod,
            cp.descr_prod,
            cp.udm
    )
    SELECT * FROM (
        SELECT *, 
               ROW_NUMBER() OVER (ORDER BY code) AS RowNum,
               COUNT(*) OVER() AS TotalRecords
        FROM KitsConStock
    ) AS Numbered
    WHERE RowNum > @offset AND RowNum <= @offset + @pageSize
    ORDER BY code";

            var kits = RunQuery(queryKits, new Dictionary<string, object>
{
    { "@search", search },
    { "@offset", offset },
    { "@pageSize", pageSize },
    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
});

            int totalRecords = kits.Count > 0
                ? Convert.ToInt32(kits[0]["totalrecords"])
                : 0;
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

            result.Add("kits", kits);
            result.Add("pagination", new Dictionary<string, object>
{
    { "currentPage", page },
    { "pageSize", pageSize },
    { "totalRecords", totalRecords },
    { "totalPages", totalPages },
    { "hasNextPage", page < totalPages },
    { "hasPreviousPage", page > 1 }
});

            return Json(new { Data = result, MaxJsonLength = int.MaxValue});
        }

        public JsonResult ObtenerComponentesKit(int kitId)
        {
            string queryComponentes = @"
                SELECT
                    p.id_catproductos AS productid,
                    p.cve_prod AS code,
                    p.descr_prod AS name,
                    p.udm AS unit,
                    '📦' AS icon,
                    kp.cantidad AS requiredperkit,
                    kp.unidad AS unidadid,
                    cu.descripcion AS unidadnombre,
                    kp.orden,
                    COALESCE(SUM(tp.cantidad), 0) AS stock
                FROM kits_productos kp
                INNER JOIN catproductos p
                    ON p.id_catproductos = kp.producto_id
                    AND p.empresa_id = @empresa_id
                LEFT JOIN catunidades cu
                    ON cu.id_udm = kp.unidad
                LEFT JOIN tarima_productos tp
                    ON tp.producto_id = p.id_catproductos
                LEFT JOIN cattarimas ct
                    ON ct.id_tarima = tp.tarima_id
                LEFT JOIN catniveles cn
                    ON cn.id_nivel = ct.nivel_id
                LEFT JOIN catcolumnas cc
                    ON cc.id_columna = cn.columna_id
                LEFT JOIN catracks cr
                    ON cr.id_rack = cc.rack_id
                LEFT JOIN catalmacenes ca
                    ON ca.id_almacen = cr.almacen_id
                    AND ca.tipo = 'Stock'
                LEFT JOIN catsucursales csu
                    ON csu.id_sucursal = ca.sucursal_id
                    AND csu.id_sucursal = 7
                WHERE kp.kit_id = @kit_id
                GROUP BY
                    p.id_catproductos,
                    p.cve_prod,
                    p.descr_prod,
                    p.udm,
                    kp.cantidad,
                    kp.unidad,
                    cu.descripcion,
                    kp.orden
                ORDER BY kp.orden";

            var componentes = RunQuery(queryComponentes, new Dictionary<string, object>
            {
                { "@kit_id", kitId },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            });

            return Json(new
            {
                Data = new { components = componentes },
                MaxJsonLength = int.MaxValue
            });
        }
        #endregion
    }
}