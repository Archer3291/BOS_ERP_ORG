using BOS_ERP.Filters;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    public partial class AdminCortesController : Utilities
    {
        [HttpGet]
        public IActionResult Index() => View();

        // ─── Disponibles: piezas con stock, con buscador amplio ──────────────────
        [HttpGet]
        public IActionResult ListarDisponibles(string busqueda, decimal? longitudMin, decimal? longitudMax,
            string productoId, int page = 1, int pageSize = 50)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "offset", (page - 1) * pageSize },
            { "limit", pageSize },
            { "sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
            { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
        };

                string filtros = "";
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    // ★ buscador amplio: folio, clave de producto, descripción, o la longitud escrita como texto
                    filtros += @" AND (tpc.folio ILIKE @busqueda OR cp.descr_prod ILIKE @busqueda
                          OR cp.cve_prod ILIKE @busqueda OR tpc.longitud::text ILIKE @busqueda) ";
                    parameters.Add("busqueda", $"%{busqueda}%");
                }
                if (!string.IsNullOrWhiteSpace(productoId))
                {
                    filtros += " AND cp.cve_prod = @productoId ";
                    parameters.Add("productoId", productoId);
                }
                if (longitudMin.HasValue)
                {
                    filtros += " AND tpc.longitud >= @longitudMin ";
                    parameters.Add("longitudMin", longitudMin.Value);
                }
                if (longitudMax.HasValue)
                {
                    filtros += " AND tpc.longitud <= @longitudMax ";
                    parameters.Add("longitudMax", longitudMax.Value);
                }

                // Se restringe a las piezas de la sucursal en sesión y de almacenes tipo 'Stock'
                // (mismo criterio que la asignación de cortes), para que el administrador no vea
                // piezas de otras sucursales o de Modula que el pedido nunca podría usar.
                string joinsAlmacen = @"
            INNER JOIN cattarimas ct   ON ct.id_tarima   = tp.tarima_id
            INNER JOIN catniveles cn   ON cn.id_nivel    = ct.nivel_id
            INNER JOIN catcolumnas col ON col.id_columna = cn.columna_id
            INNER JOIN catracks cr     ON cr.id_rack     = col.rack_id
            INNER JOIN catalmacenes ca ON ca.id_almacen  = cr.almacen_id
            INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id";

                string filtrosSucursal = " AND cs.id_sucursal = @sucursal AND ca.tipo = 'Stock' ";

                string query = $@"
            SELECT
                tpc.id_corte, tpc.folio, tpc.longitud, tpc.cantidad, tpc.cantidad_original,
                tpc.activo, tpc.fecha_creacion, tpc.usuario_creacion, tpc.comentario, tpc.es_sobrante,
                cp.cve_prod AS producto_id, cp.descr_prod AS producto_descripcion,
                COALESCE(cp.es_tubo, false) AS es_tubo,
                (SELECT COUNT(*) FROM corte_piezas pz
                  WHERE pz.corte_id = tpc.id_corte AND pz.estado = 'disponible') AS piezas_etiquetadas
            FROM tarima_productos_cortes tpc
            INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id
            {joinsAlmacen}
            INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id
            WHERE tpc.activo = true AND tpc.cantidad > 0 {filtrosSucursal} {filtros}
            ORDER BY cp.cve_prod, tpc.longitud DESC
            OFFSET @offset LIMIT @limit;";

                var items = RunQuery(query, parameters);

                string queryTotal = $@"
            SELECT COUNT(*) AS total
            FROM tarima_productos_cortes tpc
            INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id
            {joinsAlmacen}
            INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id
            WHERE tpc.activo = true AND tpc.cantidad > 0 {filtrosSucursal} {filtros};";

                var total = Convert.ToInt32(RunQuery(queryTotal, parameters).First()["total"]);
                return Json(new { items, total });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes/ListarDisponibles");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Usados: de dónde salió cada corte confirmado, con su pedido/partida ──
        [HttpGet]
        public IActionResult ListarUsados(string busqueda, string productoId, string estatus,
            DateTime? fechaDesde, DateTime? fechaHasta, int page = 1, int pageSize = 50)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "offset", (page - 1) * pageSize },
            { "limit", pageSize }
        };

                string filtros = "";
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    filtros += @" AND (tpc.folio ILIKE @busqueda OR cp.descr_prod ILIKE @busqueda
                          OR cp.cve_prod ILIKE @busqueda OR em.folio ILIKE @busqueda OR em.cli_prov ILIKE @busqueda) ";
                    parameters.Add("busqueda", $"%{busqueda}%");
                }
                if (!string.IsNullOrWhiteSpace(productoId))
                {
                    filtros += " AND cp.cve_prod = @productoId ";
                    parameters.Add("productoId", productoId);
                }
                if (!string.IsNullOrWhiteSpace(estatus))
                {
                    filtros += " AND a.estatus = @estatus ";
                    parameters.Add("estatus", estatus);
                }
                if (fechaDesde.HasValue)
                {
                    filtros += " AND a.fecha_confirmacion >= @fechaDesde ";
                    parameters.Add("fechaDesde", fechaDesde.Value);
                }
                if (fechaHasta.HasValue)
                {
                    filtros += " AND a.fecha_confirmacion <= @fechaHasta ";
                    parameters.Add("fechaHasta", fechaHasta.Value.AddDays(1));
                }

                string query = $@"
            SELECT
                a.id AS asignacion_id,
                tpc.folio AS folio_pieza,
                a.longitud_origen,
                a.cantidad_asignada,
                a.sobrante,
                a.estatus,
                a.fecha_confirmacion,
                a.usuario_confirmacion,
                cp.cve_prod AS producto_id,
                cp.descr_prod AS producto_descripcion,
                c.longitud AS longitud_solicitada,
                pd.nro_part,
                em.folio AS folio_pedido,
                em.cli_prov,
                em.id_encabezado
            FROM pedido_detalle_corte_asignacion a
            INNER JOIN tarima_productos_cortes tpc ON tpc.id_corte = a.tarima_producto_corte_id
            INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id
            INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
            INNER JOIN pedido_detalle_corte c ON c.id = a.pedido_detalle_corte_id
            INNER JOIN partidasdoc pd ON pd.id_partidas = c.pedido_detalle_id
            INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
            WHERE 1=1 {filtros}
            ORDER BY a.fecha_confirmacion DESC NULLS LAST, a.id DESC
            OFFSET @offset LIMIT @limit;";

                var items = RunQuery(query, parameters);

                string queryTotal = $@"
            SELECT COUNT(*) AS total
            FROM pedido_detalle_corte_asignacion a
            INNER JOIN tarima_productos_cortes tpc ON tpc.id_corte = a.tarima_producto_corte_id
            INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id
            INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
            INNER JOIN pedido_detalle_corte c ON c.id = a.pedido_detalle_corte_id
            INNER JOIN partidasdoc pd ON pd.id_partidas = c.pedido_detalle_id
            INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
            WHERE 1=1 {filtros};";

                var total = Convert.ToInt32(RunQuery(queryTotal, parameters).First()["total"]);
                return Json(new { items, total });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes/ListarUsados");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Ver desglose completo de un producto (para elegir la tarima base) ───
        // La sucursal se toma SIEMPRE de la sesión, nunca de lo que mande el
        // cliente: dejar que el front la eligiera permitía crear una pieza en
        // una sucursal y no volver a verla en Disponibles (que sí filtra por sesión).
        [HttpGet]
        public IActionResult ObtenerDesgloseProducto(string productoId)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "productoId", productoId },
                    { "sucursal", SucursalActual() },
                    { "empresa_id", EmpresaActual() }
                };

                string query = @"
                    SELECT
                        tp.id_tarima_producto,
                        tp.cantidad AS existencia_total,
                        COALESCE((
                            SELECT SUM(tpc.longitud * tpc.cantidad)
                            FROM tarima_productos_cortes tpc
                            WHERE tpc.tarima_producto_id = tp.id_tarima_producto AND tpc.activo = true
                        ), 0) AS metros_ya_configurados
                    FROM tarima_productos tp
                    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                    INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                    INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                    INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                    INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                    INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                    INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                    WHERE cp.cve_prod = @productoId AND cp.empresa_id = @empresa_id
                      AND cs.id_sucursal = @sucursal AND ca.tipo = 'Stock';";

                var filaTp = RunQuery(query, parameters);
                if (filaTp.Count == 0)
                    return Json(new { success = false, message = "No se encontró inventario para este producto en la sucursal indicada." });

                string queryPiezas = @"
                    SELECT id_corte, folio, longitud, cantidad, activo, comentario, es_sobrante
                    FROM tarima_productos_cortes
                    WHERE tarima_producto_id = @idTp
                    ORDER BY longitud DESC;";

                var piezas = RunQuery(queryPiezas, new Dictionary<string, object> { { "idTp", filaTp[0]["id_tarima_producto"] } });

                return Json(new
                {
                    success = true,
                    idTarimaProducto = filaTp[0]["id_tarima_producto"],
                    existenciaTotal = filaTp[0]["existencia_total"],
                    metrosYaConfigurados = filaTp[0]["metros_ya_configurados"],
                    piezas
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes/ObtenerDesgloseProducto");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Crear una nueva pieza física (desglose manual) ──────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Alta de pieza de corte")]
        public IActionResult Crear(IFormCollection fc)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;
            try
            {
                string productoId = fc["productoId"].ToString();
                int sucursal = SucursalActual();
                decimal longitud = Convert.ToDecimal(fc["longitud"].ToString());
                decimal cantidad = Convert.ToDecimal(fc["cantidad"].ToString());
                string comentario = fc["comentario"].ToString();

                if (string.IsNullOrWhiteSpace(productoId))
                    return Json(new { success = false, message = "Debe indicar el producto." });
                if (longitud <= 0 || cantidad <= 0)
                    return Json(new { success = false, message = "Longitud y cantidad deben ser mayores a 0." });

                var utils = new Utilities(true);
                conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS"));
                conn.Open();
                trx = conn.BeginTransaction();

                // Bloquear la tarima base para evitar que otra alta simultánea
                // exceda la existencia total del producto
                var filaTp = RunQuery(@"
                    SELECT tp.id_tarima_producto, tp.cantidad
                    FROM tarima_productos tp
                    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                    INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                    INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                    INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                    INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                    INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                    INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                    WHERE cp.cve_prod = @productoId AND cp.empresa_id = @empresaId
                      AND cs.id_sucursal = @sucursal AND ca.tipo = 'Stock'
                    FOR UPDATE;",
                    new Dictionary<string, object>
                    {
                        { "productoId", productoId },
                        { "empresaId", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                        { "sucursal", sucursal }
                    }, false, conn, trx);

                if (filaTp.Count == 0)
                    throw new InvalidOperationException("No se encontró inventario de este producto en la sucursal indicada.");

                int idTarimaProducto = Convert.ToInt32(filaTp[0]["id_tarima_producto"]);
                decimal existenciaTotal = Convert.ToDecimal(filaTp[0]["cantidad"]);

                // Se bloquean todas las piezas activas: las "normales" para saber
                // cuánto está realmente comprometido, y el sobrante (si lo hay)
                // para poder consumirlo y reemplazarlo por uno del tamaño correcto.
                var piezasActuales = RunQuery(@"
                    SELECT id_corte, longitud, cantidad, es_sobrante FROM tarima_productos_cortes
                    WHERE tarima_producto_id = @idTp AND activo = true
                    FOR UPDATE;",
                    new Dictionary<string, object> { { "idTp", idTarimaProducto } }, false, conn, trx);

                // El sobrante NO cuenta como "ya usado": es la existencia que
                // todavía no se ha declarado en ninguna pieza. Contarlo aquí
                // rechazaría una alta válida (ver sql/sobrante_cortes.sql).
                decimal metrosYaConfigurados = piezasActuales
                    .Where(p => !Convert.ToBoolean(p["es_sobrante"]))
                    .Sum(p => Convert.ToDecimal(p["longitud"]) * Convert.ToDecimal(p["cantidad"]));
                decimal metrosNuevos = longitud * cantidad;

                if (metrosYaConfigurados + metrosNuevos > existenciaTotal + 0.001m)
                    throw new InvalidOperationException(
                        $"La configuración excede la existencia total del producto. " +
                        $"Existencia: {existenciaTotal}m, ya configurado: {metrosYaConfigurados}m, " +
                        $"intentas agregar: {metrosNuevos}m.");

                // El sobrante anterior (si existe) se reemplaza siempre por uno
                // nuevo del tamaño correcto: igual que DividirPieza, nunca se
                // edita in-place (así conserva su propio folio/código mientras
                // fue real, y el reemplazo saca uno nuevo).
                var filaSobranteAnterior = piezasActuales.FirstOrDefault(p => Convert.ToBoolean(p["es_sobrante"]));
                if (filaSobranteAnterior != null)
                {
                    RunQuery("UPDATE tarima_productos_cortes SET cantidad = 0 WHERE id_corte = @id;",
                        new Dictionary<string, object> { { "id", filaSobranteAnterior["id_corte"] } }, false, conn, trx);
                }

                string folio = RunScalar("SELECT fn_generar_folio_corte();",
                    new Dictionary<string, object>(), false, conn, trx).ToString();

                RunQuery(@"
                    INSERT INTO tarima_productos_cortes
                        (folio, tarima_producto_id, longitud, cantidad, cantidad_original, usuario_creacion, comentario)
                    VALUES (@folio, @idTp, @longitud, @cantidad, @cantidad, @usuario, @comentario);",
                    new Dictionary<string, object>
                    {
                        { "folio", folio }, { "idTp", idTarimaProducto },
                        { "longitud", longitud }, { "cantidad", cantidad },
                        { "usuario", User.Identity.Name },
                        { "comentario", (object)comentario ?? DBNull.Value }
                    }, false, conn, trx);

                // El remanente también queda como un corte real —con folio y
                // código de barras propio, vía el mismo trigger que ya
                // sincroniza corte_piezas— porque es exactamente lo que se
                // cortó y se quedó sin desglosar, no una cifra abstracta.
                string folioSobrante = null;
                decimal nuevoSobrante = existenciaTotal - metrosYaConfigurados - metrosNuevos;
                // Umbral mínimo para no crear una pieza por puro ruido de redondeo
                // decimal; cualquier sobrante real (aunque sea de centímetros) se
                // rastrea, no solo los mayores a 0.5m — así piezas chicas (o-rings,
                // empaques) no pierden su remanente silenciosamente.
                if (nuevoSobrante > 0.001m)
                {
                    folioSobrante = RunScalar("SELECT fn_generar_folio_corte();",
                        new Dictionary<string, object>(), false, conn, trx).ToString();

                    RunQuery(@"
                        INSERT INTO tarima_productos_cortes
                            (folio, tarima_producto_id, longitud, cantidad, cantidad_original, usuario_creacion, comentario, es_sobrante)
                        VALUES (@folio, @idTp, @longitud, 1, 1, @usuario, @comentario, true);",
                        new Dictionary<string, object>
                        {
                            { "folio", folioSobrante }, { "idTp", idTarimaProducto },
                            { "longitud", nuevoSobrante }, { "usuario", User.Identity.Name },
                            { "comentario", "Sobrante sin desglosar" }
                        }, false, conn, trx);
                }

                trx.Commit();
                return Json(new
                {
                    success = true, folio, folioSobrante,
                    sobrante = nuevoSobrante > 0.001m ? nuevoSobrante : (decimal?)null,
                    message = "Pieza registrada correctamente."
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes/?");
                try { trx?.Rollback(); } catch { }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }

        // ─── Editar cantidad/comentario de una pieza existente ───────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Edicion de pieza de corte")]
        public IActionResult Editar(IFormCollection fc)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;
            try
            {
                int idCorte = Convert.ToInt32(fc["idCorte"].ToString());
                decimal nuevaCantidad = Convert.ToDecimal(fc["cantidad"].ToString());
                string comentario = fc["comentario"].ToString();

                if (nuevaCantidad < 0)
                    return Json(new { success = false, message = "La cantidad no puede ser negativa." });

                var utils = new Utilities(true);
                conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS"));
                conn.Open();
                trx = conn.BeginTransaction();

                var fila = RunQuery(@"
                    SELECT id_corte, longitud, tarima_producto_id
                    FROM tarima_productos_cortes WHERE id_corte = @id FOR UPDATE;",
                    new Dictionary<string, object> { { "id", idCorte } }, false, conn, trx);

                if (fila.Count == 0)
                    throw new InvalidOperationException("La pieza no existe.");

                decimal longitud = Convert.ToDecimal(fila[0]["longitud"]);
                int idTarimaProducto = Convert.ToInt32(fila[0]["tarima_producto_id"]);

                // Validar que el nuevo total configurado siga sin exceder la existencia física
                var filaTp = RunQuery(@"
                    SELECT cantidad FROM tarima_productos WHERE id_tarima_producto = @idTp FOR UPDATE;",
                    new Dictionary<string, object> { { "idTp", idTarimaProducto } }, false, conn, trx);
                decimal existenciaTotal = Convert.ToDecimal(filaTp[0]["cantidad"]);

                var otrasPiezas = RunQuery(@"
                    SELECT longitud, cantidad FROM tarima_productos_cortes
                    WHERE tarima_producto_id = @idTp AND activo = true AND id_corte != @idActual
                    FOR UPDATE;",
                    new Dictionary<string, object> { { "idTp", idTarimaProducto }, { "idActual", idCorte } },
                    false, conn, trx);

                decimal metrosOtras = otrasPiezas.Sum(p => Convert.ToDecimal(p["longitud"]) * Convert.ToDecimal(p["cantidad"]));
                decimal metrosNuevoTotal = metrosOtras + (longitud * nuevaCantidad);

                if (metrosNuevoTotal > existenciaTotal + 0.001m)
                    throw new InvalidOperationException(
                        $"El cambio excede la existencia total del producto ({existenciaTotal}m).");

                RunQuery(@"
                    UPDATE tarima_productos_cortes
                    SET cantidad = @cantidad, comentario = @comentario
                    WHERE id_corte = @id;",
                    new Dictionary<string, object>
                    {
                        { "cantidad", nuevaCantidad },
                        { "comentario", (object)comentario ?? DBNull.Value },
                        { "id", idCorte }
                    }, false, conn, trx);

                trx.Commit();
                return Json(new { success = true, message = "Pieza actualizada correctamente." });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes/Editar");
                try { trx?.Rollback(); } catch { }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }

        // ─── Desactivar una pieza (baja lógica) ──────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Baja de pieza de corte")]
        public IActionResult Desactivar(IFormCollection fc)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;
            try
            {
                int idCorte = Convert.ToInt32(fc["idCorte"].ToString());

                var utils = new Utilities(true);
                conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS"));
                conn.Open();
                trx = conn.BeginTransaction();

                var fila = RunQuery(@"
                    SELECT id_corte, cantidad, cantidad_original
                    FROM tarima_productos_cortes WHERE id_corte = @id FOR UPDATE;",
                    new Dictionary<string, object> { { "id", idCorte } }, false, conn, trx);

                if (fila.Count == 0)
                    throw new InvalidOperationException("La pieza no existe.");

                decimal cantidadActual = Convert.ToDecimal(fila[0]["cantidad"]);
                decimal cantidadOriginal = Convert.ToDecimal(fila[0]["cantidad_original"]);

                if (cantidadActual < cantidadOriginal)
                    throw new InvalidOperationException(
                        "No se puede dar de baja: parte de esta pieza ya fue usada en algún pedido. " +
                        "Considera ajustar la cantidad en vez de desactivarla.");

                RunQuery(@"
                    UPDATE tarima_productos_cortes SET activo = false, cantidad = 0 WHERE id_corte = @id;",
                    new Dictionary<string, object> { { "id", idCorte } }, false, conn, trx);

                trx.Commit();
                return Json(new { success = true, message = "Pieza desactivada correctamente." });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes/Desactivar");
                try { trx?.Rollback(); } catch { }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Division de pieza de corte")]
        public IActionResult DividirPieza(IFormCollection fc)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;
            try
            {
                int idCorte = Convert.ToInt32(fc["idCorte"].ToString());
                var subpiezas = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SubpiezaDto>>(
                    fc["subpiezasJSON"].ToString() ?? "[]");

                if (subpiezas == null || subpiezas.Count == 0)
                    return Json(new { success = false, message = "Debe indicar al menos una subpieza." });

                foreach (var s in subpiezas)
                    if (s.Longitud <= 0 || s.Cantidad <= 0)
                        return Json(new { success = false, message = "Todas las subpiezas deben tener longitud y cantidad mayores a 0." });

                var utils = new Utilities(true);
                conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS"));
                conn.Open();
                trx = conn.BeginTransaction();

                var fila = RunQuery(@"
            SELECT id_corte, folio, tarima_producto_id, longitud, cantidad
            FROM tarima_productos_cortes
            WHERE id_corte = @id AND activo = true FOR UPDATE;",
                    new Dictionary<string, object> { { "id", idCorte } }, false, conn, trx);

                if (fila.Count == 0)
                    throw new InvalidOperationException("La pieza no existe o ya no está activa.");

                decimal cantidadDisponible = Convert.ToDecimal(fila[0]["cantidad"]);
                decimal longitudBase = Convert.ToDecimal(fila[0]["longitud"]);
                int idTarimaProducto = Convert.ToInt32(fila[0]["tarima_producto_id"]);
                string folioOrigen = fila[0]["folio"].ToString();

                if (cantidadDisponible < 1)
                    throw new InvalidOperationException("No hay unidades disponibles de esta pieza para dividir.");

                decimal sumaSubpiezas = subpiezas.Sum(s => s.Longitud * s.Cantidad);
                if (sumaSubpiezas > longitudBase + 0.001m)
                    throw new InvalidOperationException(
                        $"La suma de las subpiezas ({sumaSubpiezas}m) excede la longitud de la pieza base ({longitudBase}m).");

                // Consumir UNA unidad física de la pieza original
                RunQuery("UPDATE tarima_productos_cortes SET cantidad = cantidad - 1 WHERE id_corte = @id;",
                    new Dictionary<string, object> { { "id", idCorte } }, false, conn, trx);

                var foliosGenerados = new List<string>();
                foreach (var s in subpiezas)
                {
                    string folioNuevo = RunScalar("SELECT fn_generar_folio_corte();",
                        new Dictionary<string, object>(), false, conn, trx).ToString();

                    RunQuery(@"
                INSERT INTO tarima_productos_cortes
                    (folio, tarima_producto_id, longitud, cantidad, cantidad_original, usuario_creacion, comentario)
                VALUES (@folio, @idTp, @longitud, @cantidad, @cantidad, @usuario, @comentario);",
                        new Dictionary<string, object>
                        {
                    { "folio", folioNuevo }, { "idTp", idTarimaProducto },
                    { "longitud", s.Longitud }, { "cantidad", s.Cantidad },
                    { "usuario", User.Identity.Name },
                    { "comentario", (object)($"Subdividido de {folioOrigen}. " + (s.Comentario ?? "")).Trim() }
                        }, false, conn, trx);

                    foliosGenerados.Add(folioNuevo);
                }

                decimal sobrante = longitudBase - sumaSubpiezas;
                // Mismo umbral mínimo que en Crear: solo descarta ruido de
                // redondeo, no sobrantes reales de piezas chicas.
                if (sobrante > 0.001m)
                {
                    string folioSobrante = RunScalar("SELECT fn_generar_folio_corte();",
                        new Dictionary<string, object>(), false, conn, trx).ToString();

                    RunQuery(@"
                INSERT INTO tarima_productos_cortes
                    (folio, tarima_producto_id, longitud, cantidad, cantidad_original, usuario_creacion, comentario)
                VALUES (@folio, @idTp, @longitud, 1, 1, @usuario, @comentario);",
                        new Dictionary<string, object>
                        {
                    { "folio", folioSobrante }, { "idTp", idTarimaProducto },
                    { "longitud", sobrante }, { "usuario", User.Identity.Name },
                    { "comentario", $"Sobrante de subdivisión de {folioOrigen}" }
                        }, false, conn, trx);

                    foliosGenerados.Add(folioSobrante);
                }

                trx.Commit();
                return Json(new { success = true, folios = foliosGenerados, message = "Pieza dividida correctamente." });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes/DividirPieza");
                try { trx?.Rollback(); } catch { }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }

        public class SubpiezaDto
        {
            public decimal Longitud { get; set; }
            public decimal Cantidad { get; set; }
            public string Comentario { get; set; }
        }
    }
}