using BOS_ERP.Controllers;
using BOS_ERP.Hubs;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using System.Globalization;
using System.Text;

namespace BOS_ERP.controllers.Contabilidad
{
    public class MigrarInventarioKepler : Utilities
    {
        private readonly IHubContext<InventarioHub> _hub;

        public MigrarInventarioKepler(IHubContext<InventarioHub> hub)
        {
            _hub = hub;
        }

        [HttpPost]
        public async Task<JsonResult> ProcesarProductosSeleccionados(List<ProductoMigracion> productos, int? id_almacen = null)
        {
            try
            {
                if (productos == null || productos.Count == 0)
                    return Json(new { success = false, message = "No hay productos seleccionados para procesar." });

                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int sucursal = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                string userName = User.Identity?.Name ?? string.Empty;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS") ?? throw new Exception("No se encontró la conexión ERP_SRS");

                ResultadoMigracion resumen;

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            resumen = ProcesarMigracionInterna(productos, id_almacen, empresaId, sucursal, userName, conn, tx);
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                await NotificarInventarioActualizado(resumen, productos, id_almacen, empresaId, sucursal, userName);

                return Json(new { success = true, message = $"Migración ejecutada correctamente. {resumen.Descripcion()}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<JsonResult> GuardarCostosUnidad(List<ProductoCostoEdicion> productos, int? id_almacen = null)
        {
            try
            {
                if (productos == null || productos.Count == 0)
                    return Json(new { success = false, message = "No hay productos para actualizar." });

                var migracion = productos
                    .Where(x => !string.IsNullOrWhiteSpace(x.Codigo))
                    .Select(x =>
                    {
                        decimal costo = 0;
                        decimal.TryParse(x.Costo, NumberStyles.Any, CultureInfo.InvariantCulture, out costo);

                        return new ProductoMigracion
                        {
                            IdProducto = x.IdProducto,
                            Codigo = x.Codigo.Trim(),
                            Descripcion = x.Descripcion ?? string.Empty,
                            Cantidad = x.Cantidad,
                            Unidad = x.Unidad ?? string.Empty,
                            Costo = costo,
                            Pedimento = string.Empty,
                            CostoActualizado = costo > 0
                        };
                    })
                    .ToList();

                if (migracion.Count == 0)
                    return Json(new { success = false, message = "No hay productos válidos para registrar." });

                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int sucursal = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                string userName = User.Identity?.Name ?? string.Empty;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS") ?? throw new Exception("No se encontró la conexión ERP_SRS");

                ResultadoMigracion resumen;

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            resumen = ProcesarMigracionInterna(migracion, id_almacen, empresaId, sucursal, userName, conn, tx);
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                await NotificarInventarioActualizado(resumen, migracion, id_almacen, empresaId, sucursal, userName);

                return Json(new { icon = "success", title = "Costos y stock registrados", html = $"Se procesaron {migracion.Count} productos. {resumen.Descripcion()}" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error al procesar la solicitud", html = ex.Message });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SubirMasivo(IFormFile file, int? id_almacen = null)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return Json(new { success = false, message = "No se recibió un archivo válido." });

                var productos = new List<ProductoMigracion>();
                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    string? line;
                    int lineNumber = 0;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        lineNumber++;
                        if (lineNumber == 1 && line.Contains("codigo", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var partes = ParseCsvLine(line);
                        if (partes.Count == 0)
                            continue;

                        var codigo = partes[0].Trim();
                        if (string.IsNullOrWhiteSpace(codigo))
                            continue;

                        decimal cantidad = 1;
                        decimal costo = 0;
                        string descripcion = string.Empty;
                        string unidad = string.Empty;

                        if (partes.Count > 1 && decimal.TryParse(partes[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var cantidadParse))
                            cantidad = cantidadParse;

                        if (partes.Count > 2 && decimal.TryParse(partes[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var costoParse))
                            costo = costoParse;

                        if (partes.Count > 3)
                            descripcion = partes[3].Trim();

                        if (partes.Count > 4)
                            unidad = partes[4].Trim();

                        productos.Add(new ProductoMigracion
                        {
                            Codigo = codigo,
                            Descripcion = descripcion,
                            Cantidad = cantidad,
                            Unidad = unidad,
                            Costo = costo,
                            Pedimento = string.Empty,
                            CostoActualizado = costo > 0
                        });
                    }
                }

                if (productos.Count == 0)
                    return Json(new { success = false, message = "El archivo no contiene productos válidos para procesar." });

                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int sucursal = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                string userName = User.Identity?.Name ?? string.Empty;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS") ?? throw new Exception("No se encontró la conexión ERP_SRS");

                ResultadoMigracion resumen;

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            resumen = ProcesarMigracionInterna(productos, id_almacen, empresaId, sucursal, userName, conn, tx);
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                await NotificarInventarioActualizado(resumen, productos, id_almacen, empresaId, sucursal, userName);

                return Json(new { success = true, message = $"Se procesaron {productos.Count} productos desde el archivo. {resumen.Descripcion()}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Avisa por SignalR a las demás pestañas de la empresa que el inventario cambió.
        /// Se llama siempre DESPUÉS del commit: si se emitiera dentro de la transacción,
        /// el reporte volvería a consultar y leería datos que todavía no existen.
        /// Un fallo aquí no debe tumbar la migración, que ya quedó guardada.
        /// </summary>
        private async Task NotificarInventarioActualizado(ResultadoMigracion resumen, IEnumerable<ProductoMigracion> productos, int? id_almacen, int empresaId, int sucursal, string userName)
        {
            if (resumen.ConCompra == 0 && resumen.SoloStock == 0)
                return;

            try
            {
                var evento = new InventarioActualizadoEvento
                {
                    Sucursal = sucursal,
                    Almacen = id_almacen.HasValue && id_almacen.Value > 0 ? id_almacen : null,
                    ConCompra = resumen.ConCompra,
                    SoloStock = resumen.SoloStock,
                    EncabezadoCompra = resumen.EncabezadoCompra,
                    Codigos = productos
                        .Where(x => x.Cantidad > 0 && !string.IsNullOrWhiteSpace(x.Codigo))
                        .Select(x => x.Codigo.Trim())
                        .Distinct()
                        .Take(50)
                        .ToList(),
                    Usuario = userName
                };

                await _hub.Clients
                    .Group(InventarioHub.GrupoEmpresa(empresaId))
                    .SendAsync(InventarioHub.EventoInventarioActualizado, evento);
            }
            catch
            {
                // Silencioso a propósito: la migración ya se comprometió y el reporte
                // siempre puede refrescarse a mano.
            }
        }

        private ResultadoMigracion ProcesarMigracionInterna(IEnumerable<ProductoMigracion> productos, int? id_almacen, int empresaId, int sucursal, string userName, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var p = new Dictionary<string, object> { { "sucursal", sucursal } };
            string tarimaQuery = "SELECT ct.id_tarima FROM catalmacenes c " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Stock' ";

            if (id_almacen.HasValue && id_almacen.Value > 0)
            {
                tarimaQuery += "AND c.id_almacen = @id_almacen ";
                p.Add("id_almacen", id_almacen.Value);
            }

            tarimaQuery += "ORDER BY ct.id_tarima";

            // Se traen todas las tarimas válidas (no solo la primera) para dos cosas:
            // la primera sigue siendo el destino por defecto, y el conjunto sirve para
            // validar la tarima que cada producto trae desde el front.
            var tarimasValidas = RunQuery(tarimaQuery, p, false, conn, tx)
                .Select(x => Convert.ToInt32(x["id_tarima"]))
                .ToList();

            int destinoTarima = tarimasValidas.FirstOrDefault();
            var tarimasPermitidas = new HashSet<int>(tarimasValidas);

            // Un movimiento por tarima destino: registrar_movimiento recibe un solo destino.
            // productosConCompra: además del ingreso se les genera partida de compra, que es
            // lo que termina creando el renglón en registro_compras.
            var productosConCompra = new Dictionary<int, List<Dictionary<string, object>>>();
            var productosSoloStock = new Dictionary<int, List<Dictionary<string, object>>>();
            var partidasParaCompra = new List<PartidaDocumento>();
            int nroPart = 1;

            foreach (var prod in productos.Where(x => x.Cantidad > 0))
            {
                int destino = prod.IdTarima.HasValue && tarimasPermitidas.Contains(prod.IdTarima.Value)
                    ? prod.IdTarima.Value
                    : destinoTarima;

                if (destino <= 0)
                    throw new Exception("No se encontró tarima destino para la sucursal.");

                int? idProducto = prod.IdProducto;
                if (!idProducto.HasValue || idProducto.Value <= 0)
                {
                    var lookup = new Dictionary<string, object>
                    {
                        { "cve_prod", prod.Codigo.Trim() },
                        { "empresa_id", empresaId }
                    };
                    var idObj = RunScalar("SELECT id_catproductos FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id", lookup, false, conn, tx);
                    if (idObj == null)
                        continue;

                    idProducto = Convert.ToInt32(idObj);
                }

                int unidadId = 0;
                if (!string.IsNullOrWhiteSpace(prod.Unidad))
                {
                    var upar = new Dictionary<string, object> { { "unidad", prod.Unidad } };
                    var uobj = RunScalar("SELECT id_udm FROM catunidades WHERE cve_udm = @unidad", upar, false, conn, tx);
                    if (uobj != null)
                        unidadId = Convert.ToInt32(uobj);
                }

                var item = new Dictionary<string, object>();
                item["id_producto"] = idProducto.Value;
                item["codigo"] = prod.Codigo;
                item["descripcion"] = prod.Descripcion;
                item["cantidad"] = prod.Cantidad;
                item["unidad"] = unidadId > 0 ? (object)unidadId : DBNull.Value;
                item["pedimento"] = GetString(prod.Pedimento, "NOPEDIMENTO");

                var chk = new Dictionary<string, object> { { "id_producto", idProducto.Value } };
                var cntObj = RunScalar("SELECT COUNT(*) FROM registro_compras WHERE producto_id = @id_producto", chk, false, conn, tx);
                int cnt = cntObj == null ? 0 : Convert.ToInt32(cntObj);

                // Se genera compra en dos casos: el producto no tiene ninguna (si no,
                // entraría a inventario sin costo con qué valuarse), o el usuario capturó
                // un costo nuevo desde la vista, que es justo lo que hay que registrar.
                bool costoNuevo = prod.CostoActualizado && prod.Costo.HasValue && prod.Costo.Value > 0;

                if (cnt == 0 || costoNuevo)
                {
                    AgregarADestino(productosConCompra, destino, item);

                    var part = new PartidaDocumento();
                    part.NroPart = nroPart++;
                    part.CveProd = prod.Codigo.Trim();
                    part.CantUd = prod.Cantidad;
                    part.DescrProd = prod.Descripcion;
                    part.Ud = prod.Unidad ?? string.Empty;
                    part.PvProd = prod.Costo ?? 0;
                    part.Pedimento = GetString(prod.Pedimento, "NOPEDIMENTO");
                    partidasParaCompra.Add(part);
                }
                else
                {
                    AgregarADestino(productosSoloStock, destino, item);
                }
            }

            int? encabezadoCompraId = null;
            if (partidasParaCompra.Count > 0)
            {
                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = empresaId;
                encabezado.IdArea = 5;
                encabezado.IdTpDoc = 90;
                encabezado.Estatus = 11;
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = sucursal;
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "MIKEP";
                encabezado.UsrDoc = userName;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(userName);
                encabezado.Coment1 = "Migracion Kepler - registro de compras (productos nuevos y costos actualizados)";
                encabezado.CliProv = "srs";

                var documento = GenerarDocumentoConPartidas(encabezado, partidasParaCompra, conn, tx);
                encabezadoCompraId = Convert.ToInt32(documento["IdEncabezado"]);
                RegistrarCompra(encabezadoCompraId.Value, GetUserId(userName), conn, tx);
            }

            foreach (var grupo in productosSoloStock)
            {
                RegistrarMovimiento(grupo.Value, GetUserId(userName), "Ingreso", null, grupo.Key, "Migracion Kepler - aumento de stock (sin costo nuevo)", null, "Migracion Kepler: ingreso detectado desde Kepler", conn, tx);
            }

            foreach (var grupo in productosConCompra)
            {
                RegistrarMovimiento(grupo.Value, GetUserId(userName), "Ingreso", null, grupo.Key, "Migracion Kepler - ingreso asociado a compra", encabezadoCompraId, "Migracion Kepler: partida creada y asociada al documento de compra", conn, tx);
            }

            return new ResultadoMigracion
            {
                ConCompra = productosConCompra.Sum(g => g.Value.Count),
                SoloStock = productosSoloStock.Sum(g => g.Value.Count),
                EncabezadoCompra = encabezadoCompraId
            };
        }

        private static void AgregarADestino(Dictionary<int, List<Dictionary<string, object>>> destinos, int tarima, Dictionary<string, object> item)
        {
            if (!destinos.TryGetValue(tarima, out var lista))
            {
                lista = new List<Dictionary<string, object>>();
                destinos[tarima] = lista;
            }

            lista.Add(item);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            values.Add(current.ToString());
            return values;
        }

        public JsonResult GetProductosKepler(string nombre, string sortColumn, string sortDir, string esquema, string sucursal, string almacen, int page = 1, int pageSize = 50)
        {
            // El origen se elige en el modal: sin las tres piezas no hay nada que consultar.
            string conexion = ResolverEsquemaKepler(esquema);
            if (conexion == null || string.IsNullOrWhiteSpace(sucursal) || string.IsNullOrWhiteSpace(almacen))
                return Json(new { data = new List<Dictionary<string, object>>(), total = 0 });

            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre ?? "" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "sucursal", sucursal.Trim() },
                { "almacen", Convert.ToInt32(almacen) },
            };

            var allowedColumns = new HashSet<string> {
                "clave", "descripcion",
                "unidad", "unidad_sec", "moneda_compra",
                "valor_inventario", "ultimo_costo", "pedimento"

            };

            if (!allowedColumns.Contains(sortColumn))
                sortColumn = "clave";

            string where = "";

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where += " AND (p.c1 ILIKE '%' || @nombre || '%' " +
                    "   OR p.c2 ILIKE '%' || @nombre || '%') ";
            }

            // Sin prefijo de esquema: cada conexión de Kepler trae su propio Search Path,
            // así que "sellosop." solo funcionaría para una de las cuatro bases.
            // El FROM se comparte con el COUNT para que el total y las filas no se
            // desfasen (el inv agrupa también por pedimento y puede dar varias filas).
            string fromClause = "FROM kdii p " +
                "INNER JOIN ( " +
                "   SELECT k1.c3, SUM(k1.c8 - k1.c9) AS existencia, COALESCE(k5.c4, 'NOPEDIMENTO') pedimento " +
                "   FROM kdil k1 " +
                "   LEFT JOIN kdis k5 ON k1.c3 = k5.c1 AND k5.c15 = k1.c1 " +
                "   WHERE k1.c1 = @sucursal AND k1.c2 = @almacen " +
                "   GROUP BY k1.c3, k5.c4 " +
                "   HAVING SUM(k1.c8 - k1.c9) > 0 " +
                ") inv ON inv.c3 = p.c1 " +
                "LEFT JOIN ( " +
                "   SELECT DISTINCT ON (m.c8) m.c8, m.c6 AS folio, m.c7 AS partida, m.c9 AS cantidad_compra, m.c12 AS ultimo_costo, " +
                "      m.c32 AS fecha_compra " +
                "   FROM kdm2 m " +
                "   WHERE m.c1 = @sucursal AND m.c2 = 'X' AND m.c3 = 'A' AND m.c4 = '40' AND m.c5 = '1' " +
                "   ORDER BY m.c8, m.c32 DESC NULLS LAST, m.c6 DESC, m.c7 DESC " +
                ") uc ON uc.c8 = p.c1 " +
                $"WHERE 1=1 {where} ";

            string query = "SELECT p.c1 AS clave, p.c2 AS descripcion, p.c11 AS unidad, p.c12 AS unidad_sec, p.c13 AS factor, p.c31 AS moneda_compra, " +
                "   inv.existencia, uc.fecha_compra, uc.folio, uc.partida, uc.cantidad_compra, uc.ultimo_costo, " +
                "   CASE " +
                "       WHEN uc.ultimo_costo IS NULL THEN NULL " +
                "       WHEN p.c13 > 0 THEN ROUND((inv.existencia / p.c13 * uc.ultimo_costo)::numeric, 2) " +
                "       ELSE ROUND((inv.existencia * uc.ultimo_costo)::numeric, 2) " +
                "   END AS valor_inventario, " +
                "   CASE " +
                "       WHEN uc.fecha_compra IS NULL THEN NULL " +
                "       ELSE CURRENT_DATE - uc.fecha_compra::date " +
                "   END AS dias_sin_comprar, " +
                "   inv.pedimento " +
                fromClause +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters, false, null, null, conexion);

            int total = Convert.ToInt32(RunScalar("SELECT COUNT(*) " + fromClause, parameters, false, null, null, conexion));

            return Json(new { data, total });
        }

        /// <summary>
        /// Valida el origen que manda el front contra las conexiones configuradas y
        /// devuelve el nombre de conexión a usar, o null si no es un origen Kepler
        /// válido. Evita que un valor arbitrario llegue a GetConnectionString.
        /// </summary>
        private static string ResolverEsquemaKepler(string esquema)
        {
            if (string.IsNullOrWhiteSpace(esquema))
                return null;

            var nombre = esquema.Trim();

            // La base del ERP no es un origen de importación.
            if (nombre.Equals("ERP_SRS", StringComparison.OrdinalIgnoreCase))
                return null;

            var utils = new Utilities(true);
            return string.IsNullOrWhiteSpace(utils._configuration.GetConnectionString(nombre)) ? null : nombre;
        }

        public JsonResult GetProductosActuales(string nombre, string sortColumn, string sortDir, int? id_sucursal, int? id_almacen, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            int sucursal = id_sucursal.HasValue && id_sucursal.Value > 0 ? id_sucursal.Value : Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            parameters.Add("nombre", nombre ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("sucursal", sucursal);
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
            {
                where = " AND (cp.cve_prod ILIKE '%' || @nombre || '%' " +
                    "OR cp.descr_prod ILIKE '%' || @nombre || '%' " +
                    "OR ca.descripcion ILIKE '%' || @nombre || '%' " +
                    "OR cn.ulocation ILIKE '%' || @nombre || '%' " +
                    "OR ct.codigo ILIKE '%' || @nombre || '%' " +
                    "OR ca.cve_almacen ILIKE '%' || @nombre || '%' ) ";
            }

            if (id_almacen.HasValue && id_almacen.Value > 0)
            {
                where += " AND ca.id_almacen = @id_almacen ";
                parameters.Add("id_almacen", id_almacen.Value);
            }

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
                $"WHERE tp.cantidad > 0 AND cu.id_sucursal = @sucursal {where} " +
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
                $"WHERE tp.cantidad > 0 AND cu.id_sucursal = @sucursal {where}";
            int total = Convert.ToInt32(RunScalar(query, parameters));
            return Json(new { data = traslados, total });
        }

        /// <summary>
        /// Tarimas candidatas para recibir un producto, con el dato que necesita el
        /// usuario para decidir: si ya tiene ese mismo producto, si está vacía y qué
        /// tan ocupada está. Se ordenan en ese mismo orden de utilidad.
        /// </summary>
        public JsonResult GetTarimasSugeridas(string codigo, string nombre, int? id_almacen, int pageSize = 300)
        {
            var parameters = new Dictionary<string, object>
            {
                { "codigo", (codigo ?? string.Empty).Trim() },
                { "sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "pageSize", pageSize <= 0 ? 300 : pageSize }
            };

            string where = "";

            if (id_almacen.HasValue && id_almacen.Value > 0)
            {
                where += " AND ca.id_almacen = @id_almacen ";
                parameters.Add("id_almacen", id_almacen.Value);
            }

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where += " AND (ct.codigo ILIKE '%' || @nombre || '%' " +
                    "   OR cn.ulocation ILIKE '%' || @nombre || '%' " +
                    "   OR ca.descripcion ILIKE '%' || @nombre || '%' " +
                    "   OR ca.cve_almacen ILIKE '%' || @nombre || '%') ";
                parameters.Add("nombre", nombre.Trim());
            }

            string query = "SELECT ct.id_tarima, ct.codigo AS tarima, cn.ulocation, " +
                "   ca.id_almacen, ca.cve_almacen, ca.descripcion AS almacen, " +
                "   COUNT(DISTINCT tp.producto_id) AS productos_distintos, " +
                "   COALESCE(SUM(tp.cantidad), 0) AS cantidad_total, " +
                "   COALESCE(SUM(CASE WHEN cp.cve_prod = @codigo THEN tp.cantidad ELSE 0 END), 0) AS cantidad_producto, " +
                "   COALESCE(BOOL_OR(cp.cve_prod = @codigo), false) AS tiene_producto " +
                "FROM cattarimas ct " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cu ON cu.id_sucursal = ca.sucursal_id " +
                "LEFT JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima AND tp.cantidad > 0 " +
                "LEFT JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id " +
                $"WHERE ca.tipo = 'Stock' AND cu.id_sucursal = @sucursal {where} " +
                "GROUP BY ct.id_tarima, ct.codigo, cn.ulocation, ca.id_almacen, ca.cve_almacen, ca.descripcion " +
                "ORDER BY tiene_producto DESC, " +
                "   (COUNT(DISTINCT tp.producto_id) = 0) DESC, " +
                "   COUNT(DISTINCT tp.producto_id) DESC, " +
                "   ct.codigo " +
                "FETCH FIRST @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            return Json(new { data = data });
        }

        public JsonResult GetUbicaciones()
        {
            var result = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();
            string query = "SELECT ct.id_tarima, ct.codigo, ca.cve_almacen, ca.tipo, cs.id_sucursal, cn.ulocation " +
                "FROM cattarimas ct " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                "WHERE ca.tipo IN ('Stock', 'Recepcion', 'Temporal')";
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            result.Add("tarimas", RunQuery(query, parameters));

            query = "SELECT id_sucursal, cve_sucursal, descripcion, empresa_id FROM catsucursales";
            result.Add("sucursales", RunQuery(query));

            query = "SELECT cata.id_almacen, cata.cve_almacen, cata.descripcion almacen_descripcion, cata.tipo, " +
                "   cats.cve_sucursal, cats.descripcion sucursal_descripcion, cats.id_sucursal " +
                "FROM catalmacenes cata " +
                "INNER JOIN catsucursales cats ON cats.id_sucursal = cata.sucursal_id ";
            result.Add("almacenes", RunQuery(query));

            query = "SELECT empresaid, rfc, nombre FROM empresas";
            result.Add("empresas", RunQuery(query));

            result.Add("currentSucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));

            return Json(result);
        }

        #region obtener datos kepler
        public JsonResult GetSucursalesKepler(string esquema)
        {
            string conexion = ResolverEsquemaKepler(esquema);
            if (conexion == null)
                return Json(new List<Dictionary<string, object>>());

            var parameters = new Dictionary<string, object>();
            string query = "SELECT c1 clave, c2 descripcion " +
                "FROM kdms " +
                "ORDER BY c1";
            var sucursales = RunQuery(query, parameters, false, null, null, conexion);

            return Json(sucursales);
        }

        public JsonResult GetAmacenesKepler(string sucursal, string esquema)
        {
            string conexion = ResolverEsquemaKepler(esquema);
            if (conexion == null || string.IsNullOrWhiteSpace(sucursal))
                return Json(new List<Dictionary<string, object>>());

            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", sucursal.Trim());
            string query = "SELECT k.c1 cve_sucursal, k.c2 cve_almacen, k.c3 descripcion_almacem, k2.c2 nombre_sucursal " +
                "FROM kdiq k " +
                "LEFT JOIN kdms k2 ON k2.c1 = k.c1 " +
                "WHERE  k2.c1 = @sucursal " +
                "ORDER BY k.c2";
            var almacen = RunQuery(query, parameters, false, null, null, conexion);
            return Json(almacen);
        }
        #endregion

    }

    public class ProductoMigracion
    {
        public int? IdProducto { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public string Unidad { get; set; } = string.Empty;
        public decimal? Costo { get; set; }
        public string Pedimento { get; set; } = string.Empty;

        /// <summary>
        /// Tarima destino elegida en el front. Si viene vacía o no pertenece a un
        /// almacén de Stock de la sucursal, se usa la tarima por defecto.
        /// </summary>
        public int? IdTarima { get; set; }

        /// <summary>
        /// True cuando el usuario capturó el costo a mano. Obliga a generar partida de
        /// compra (y por lo tanto renglón en registro_compras) aunque el producto ya
        /// tenga compras previas. Si es false, el producto solo aumenta stock.
        /// </summary>
        public bool CostoActualizado { get; set; }
    }

    /// <summary>
    /// Qué hizo realmente la migración. Se devuelve al front para que el usuario vea si
    /// se generaron renglones de compra o solo movimientos de stock, sin tener que ir a
    /// revisar la base de datos.
    /// </summary>
    public class ResultadoMigracion
    {
        public int ConCompra { get; set; }
        public int SoloStock { get; set; }
        public int? EncabezadoCompra { get; set; }

        public string Descripcion()
        {
            if (ConCompra == 0 && SoloStock == 0)
                return "No se procesó ningún producto (revisa que las claves existan en el catálogo).";

            var partes = new List<string>();

            if (ConCompra > 0)
                partes.Add($"{ConCompra} con registro de compra (documento {EncabezadoCompra})");

            if (SoloStock > 0)
                partes.Add($"{SoloStock} solo con aumento de stock");

            return string.Join(" y ", partes) + ".";
        }
    }

    public class ProductoCostoEdicion
    {
        public int? IdProducto { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public string Unidad { get; set; } = string.Empty;
        public string Costo { get; set; } = string.Empty;
    }
}
