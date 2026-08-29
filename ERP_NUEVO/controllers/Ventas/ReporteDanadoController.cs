using Newtonsoft.Json;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using BOS_ERP.Filters;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas
{
    [Authorize]
    public class ReporteDanadoController : Utilities
    {
        private static readonly Dictionary<string, int> ESTATUS_POR_TIPO = new Dictionary<string, int>
        {
            ["fisico"] = 34,    // "Material dañado — Daño físico"
            ["incompleto"] = 35,    // "Material dañado — Incompleto"
            ["caducado"] = 36,    // "Material dañado — Caducado"
            ["calidad"] = 37,    // "Material dañado — Problema de calidad"
            ["mojado"] = 38,    // "Material dañado — Humedad / Mojado"
            ["otro"] = 39,    // "Material dañado — Otro"
        };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerFacturasVendedor(IFormCollection fc)
        {
            try
            {
                int usuarioId = GetUserId(User.Identity.Name);
                int sucursal = HttpContext.Session.GetInt32("Sucursal") ?? 0;

                string nombre = fc["nombre"].ToString() ?? "";
                string desde = fc["desde"].ToString() ?? "";
                string hasta = fc["hasta"].ToString() ?? "";
                int page = int.TryParse(fc["page"].ToString(), out int p) ? p : 1;
                int pageSize = int.TryParse(fc["pageSize"].ToString(), out int ps) ? ps : 30;

                var parameters = new Dictionary<string, object>
                {
                    ["suc"] = sucursal,
                    ["usuario"] = usuarioId,
                    ["nombre"] = $"%{nombre}%",
                    ["desde"] = string.IsNullOrWhiteSpace(desde) ? (object)DBNull.Value : DateTime.Parse(desde),
                    ["hasta"] = string.IsNullOrWhiteSpace(hasta) ? (object)DBNull.Value : DateTime.Parse(hasta).AddDays(1),
                    ["offset"] = (page - 1) * pageSize,
                    ["pageSize"] = pageSize,
                };

                var items = RunQuery(@"
            SELECT
                e.id_encabezado,
                e.folio,
                e.cli_prov,
                c.n_cli        AS n_cli,
                c.rfc,
                e.imp,
                e.ccy,
                e.fch,
                e.estatus_id,
                est.descripcion AS estatus_txt,
                EXISTS (
                    SELECT 1
                    FROM rmd_reporte_detalle rd
                    INNER JOIN rmd_reporte r ON r.id = rd.reporte_id
                    WHERE rd.encabezado_id = e.id_encabezado
                      AND r.estatus_actual IN ('PENDIENTE','RECIBIDO')
                ) AS rmd_activo
            FROM encabezadomov e
            LEFT JOIN catclientes c      ON c.id_cliente  = e.refe
            LEFT JOIN estatus_docs est   ON est.id_estatus = e.estatus_id
            WHERE e.suc = @suc
              AND e.nat IN ('VSFAC','VIFAC','VINFAC','VNFAC')
              AND e.estatus_id NOT IN (40,41,42,43,44,45,99)
              AND e.usr3 = @usuario
              AND (@nombre = '%%' OR LOWER(e.folio)  LIKE LOWER(@nombre)
                                  OR LOWER(c.n_cli) LIKE LOWER(@nombre)
                                  OR LOWER(c.rfc)   LIKE LOWER(@nombre))
              AND (CAST(@desde AS TIMESTAMP) IS NULL OR e.fch >= CAST(@desde AS TIMESTAMP))
              AND (CAST(@hasta AS TIMESTAMP) IS NULL OR e.fch <  CAST(@hasta AS TIMESTAMP))
            ORDER BY e.fch DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    parameters);

                var totalResult = RunQuery(@"
            SELECT COUNT(*) AS total
            FROM encabezadomov e
            LEFT JOIN catclientes c ON c.id_cliente = e.refe
            WHERE e.suc = @suc
              AND e.nat IN ('VSFAC','VIFAC','VINFAC','VNFAC')
              AND e.estatus_id NOT IN (40,41,42,43,44,45,99)
              AND e.usr3 = @usuario
              AND (@nombre = '%%' OR LOWER(e.folio)  LIKE LOWER(@nombre)
                                  OR LOWER(c.n_cli) LIKE LOWER(@nombre)
                                  OR LOWER(c.rfc)   LIKE LOWER(@nombre))
              AND (CAST(@desde AS TIMESTAMP) IS NULL OR e.fch >= CAST(@desde AS TIMESTAMP))
              AND (CAST(@hasta AS TIMESTAMP) IS NULL OR e.fch <  CAST(@hasta AS TIMESTAMP))",
                    new Dictionary<string, object>
                    {
                        ["suc"] = sucursal,
                        ["usuario"] = usuarioId,
                        ["nombre"] = $"%{nombre}%",
                        ["desde"] = string.IsNullOrWhiteSpace(desde) ? (object)DBNull.Value : DateTime.Parse(desde),
                        ["hasta"] = string.IsNullOrWhiteSpace(hasta) ? (object)DBNull.Value : DateTime.Parse(hasta).AddDays(1),
                    });

                int total = totalResult?.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

                // createTable espera { data: [...], total: N }
                return Json(new { data = items, total });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "ReporteDanado/ObtenerFacturasVendedor");
                return Json(new { data = new object[0], total = 0, error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industriales", Accion = "Reporte de material dañado")]
        public JsonResult RegistrarReporte(IFormCollection fc)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        // ── Parsear inputs ────────────────────────────────────
                        string encabezadoIdsJson = fc["encabezadoIds"].ToString() ?? "[]";
                        string partidasJson = fc["partidasDanadas"].ToString() ?? "[]";

                        var encabezadoIds = JsonConvert.DeserializeObject<List<int>>(encabezadoIdsJson);
                        var partidas = JsonConvert.DeserializeObject<List<PartidaDanada>>(partidasJson);

                        if (encabezadoIds == null || encabezadoIds.Count == 0)
                            return Json(new { success = false, message = "No se recibieron facturas seleccionadas." });

                        if (partidas == null || !partidas.Any())
                            return Json(new { success = false, message = "Selecciona al menos una partida dañada." });

                        string tipoDanio = fc["tipoDanio"].ToString() ?? "";
                        string descripcion = fc["descripcion"].ToString() ?? "";
                        string urgencia = fc["urgencia"].ToString() ?? "normal";
                        string referencia = fc["referencia"].ToString() ?? "";
                        string ubicacion = fc["ubicacion"].ToString() ?? "";

                        if (string.IsNullOrWhiteSpace(tipoDanio))
                            return Json(new { success = false, message = "Selecciona el tipo de daño." });

                        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Trim().Length < 10)
                            return Json(new { success = false, message = "La descripción debe tener al menos 10 caracteres." });

                        int estatusId = ESTATUS_POR_TIPO.ContainsKey(tipoDanio)
                            ? ESTATUS_POR_TIPO[tipoDanio]
                            : 40;

                        int usuarioId = GetUserId(User.Identity.Name);
                        int sucursal = HttpContext.Session.GetInt32("Sucursal") ?? 0;
                        int empresaId = HttpContext.Session.GetInt32("Empresa") ?? 0;

                        // ── Validar que los encabezados pertenecen al vendedor ──
                        string inClause = string.Join(",", encabezadoIds);
                        var encabezadosValidos = RunQuery($@"
                            SELECT id_encabezado, folio
                            FROM encabezadomov
                            WHERE id_encabezado IN ({inClause})
                              AND suc = @suc
                              AND (usr3 = @usr)",
                            new Dictionary<string, object>
                            {
                                ["suc"] = sucursal,
                                ["usr"] = usuarioId,
                            },
                            false, conn, tx);

                        if (!encabezadosValidos.Any())
                            return Json(new { success = false, message = "No se encontraron los documentos indicados." });

                        var folios = encabezadosValidos
                            .Select(r => r["folio"]?.ToString() ?? "")
                            .Where(f => !string.IsNullOrEmpty(f))
                            .ToList();

                        // ── 1. Insertar cabecera del reporte ──────────────────
                        var rowReporte = RunQuery(@"
                            INSERT INTO rmd_reporte
                                (empresa_id, sucursal, tipo_danio, descripcion,
                                 urgencia, referencia, ubicacion_material,
                                 estatus_actual, usuario_vendedor, fecha_reporte,
                                 encabezado_ids_json, folios_txt)
                            VALUES
                                (@emp, @suc, @tipo, @desc,
                                 @urg, @ref, @ubic,
                                 'PENDIENTE', @usr, NOW(),
                                 @enc_ids::jsonb, @folios)
                            RETURNING id",
                            new Dictionary<string, object>
                            {
                                ["emp"] = empresaId,
                                ["suc"] = sucursal,
                                ["tipo"] = tipoDanio,
                                ["desc"] = descripcion.Trim(),
                                ["urg"] = urgencia,
                                ["ref"] = referencia,
                                ["ubic"] = ubicacion,
                                ["usr"] = usuarioId,
                                ["enc_ids"] = encabezadoIdsJson,
                                ["folios"] = string.Join(", ", folios),
                            },
                            false, conn, tx);

                        int reporteId = rowReporte?.Count > 0
                            ? Convert.ToInt32(rowReporte[0]["id"])
                            : 0;

                        // ── 2. Por cada encabezado seleccionado ───────────────
                        foreach (int encId in encabezadoIds)
                        {
                            // 2a. Cambiar estatus en encabezadomov
                            RunQuery(@"
                                UPDATE encabezadomov
                                   SET estatus_id = @estatus
                                 WHERE id_encabezado = @id
                                   AND suc = @suc",
                                new Dictionary<string, object>
                                {
                                    ["estatus"] = estatusId,
                                    ["id"] = encId,
                                    ["suc"] = sucursal,
                                },
                                false, conn, tx);

                            // 2b. Insertar detalle del reporte (1 fila por factura)
                            RunQuery(@"
                                INSERT INTO rmd_reporte_detalle
                                    (reporte_id, encabezado_id, estatus_detalle)
                                VALUES
                                    (@rep, @enc, 'PENDIENTE')",
                                new Dictionary<string, object>
                                {
                                    ["rep"] = reporteId,
                                    ["enc"] = encId,
                                },
                                false, conn, tx);

                            // 2c. Comentario de trazabilidad
                            RunQuery(@"
                                INSERT INTO enc_comentarios
                                    (encabezado_id, tipo_comentario, comentario,
                                     usuario_id, fecha, origen_modulo, referencia_id)
                                VALUES
                                    (@enc_id, 'MATERIAL_DANADO', @comentario,
                                     @usr, NOW(), 'ReporteDanado', @ref_id)",
                                new Dictionary<string, object>
                                {
                                    ["enc_id"] = encId,
                                    ["comentario"] = $"[{tipoDanio.ToUpper()}] {descripcion.Trim()} — Urgencia: {urgencia} — Ref: {referencia}",
                                    ["usr"] = usuarioId,
                                    ["ref_id"] = reporteId,
                                },
                                false, conn, tx);
                        }

                        // ── 3. Insertar partidas dañadas ──────────────────────
                        foreach (var p in partidas.Where(x => x.cantDanada > 0))
                        {
                            RunQuery(@"
                                INSERT INTO rmd_partida_danada
                                    (reporte_id, encabezado_id, producto_id, descripcion,
                                     cant_total_factura, cant_danada, precio_unitario, ccy)
                                VALUES
                                    (@rep, @enc, @prod, @desc,
                                     @cant_total, @cant_danada, @precio, @ccy)",
                                new Dictionary<string, object>
                                {
                                    ["rep"] = reporteId,
                                    ["enc"] = p.encabezadoId,
                                    ["prod"] = p.productoId ?? "",
                                    ["desc"] = p.descripcion ?? "",
                                    ["cant_total"] = p.cantTotal,
                                    ["cant_danada"] = p.cantDanada,
                                    ["precio"] = p.precio,
                                    ["ccy"] = p.ccy ?? "MXN",
                                },
                                false, conn, tx);
                        }

                        tx.Commit();

                        LogErrorHelper.RegistrarLog(
                            "ReporteDanado", $"RMD-{reporteId}",
                            $"Reporte dañado registrado: {encabezadoIds.Count} docs, tipo={tipoDanio}, " +
                            $"partidas={partidas.Count}, folios=[{string.Join(",", folios)}], usuario={User.Identity.Name}",
                            nivel: "INFO");

                        return Json(new
                        {
                            success = true,
                            reporteId,
                            message = $"Reporte enviado a inventario — {encabezadoIds.Count} documento(s).",
                            folios,
                            encabezadoIds,
                        });
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "ReporteDanado/?");
                        tx.Rollback();
                        LogErrorHelper.RegistrarLog(
                            "ReporteDanado", "ERROR",
                            $"Error en RegistrarReporte: {ex.Message}", nivel: "ERROR");

                        return Json(new { success = false, message = "Error al registrar: " + ex.Message });
                    }
                }
            }
        }

        [HttpGet]
        public JsonResult ObtenerHistorialVendedor(
            string estatus = "",
            int page = 1,
            int pageSize = 25)
        {
            try
            {
                int usuarioId = GetUserId(User.Identity.Name);
                int empresaId = HttpContext.Session.GetInt32("Empresa") ?? 0;

                var parameters = new Dictionary<string, object>
                {
                    ["empresa_id"] = empresaId,
                    ["usuario"] = usuarioId,
                    ["estatus"] = estatus ?? "",
                    ["offset"] = (page - 1) * pageSize,
                    ["pageSize"] = pageSize,
                };

                var items = RunQuery(@"
                    SELECT
                        r.id,
                        r.fecha_reporte,
                        r.folios_txt,
                        r.tipo_danio,
                        r.descripcion,
                        r.urgencia,
                        r.estatus_actual,
                        r.usuario_inventario,
                        r.fecha_recepcion,
                        -- Folio del documento de devolución generado
                        (
                            SELECT e2.folio
                            FROM encabezadomov e2
                            INNER JOIN rmd_reporte_detalle rd2
                                ON rd2.doc_generado_id = e2.id_encabezado
                            WHERE rd2.reporte_id = r.id
                            ORDER BY e2.id_encabezado DESC
                            LIMIT 1
                        ) AS doc_folio
                    FROM rmd_reporte r
                    WHERE r.empresa_id = @empresa_id
                      AND r.usuario_vendedor = @usuario
                      AND (@estatus = '' OR r.estatus_actual = @estatus)
                    ORDER BY r.fecha_reporte DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    parameters);

                var totalResult = RunQuery(@"
                    SELECT COUNT(*) AS total
                    FROM rmd_reporte r
                    WHERE r.empresa_id = @empresa_id
                      AND r.usuario_vendedor = @usuario
                      AND (@estatus = '' OR r.estatus_actual = @estatus)",
                    new Dictionary<string, object>
                    {
                        ["empresa_id"] = empresaId,
                        ["usuario"] = usuarioId,
                        ["estatus"] = estatus ?? "",
                    });

                int total = totalResult?.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

                return Json(new { items, total });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "ReporteDanado/ObtenerHistorialVendedor");
                return Json(new { items = new object[0], total = 0, error = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MODELOS DE DATOS
        // ══════════════════════════════════════════════════════════════════════

        private class PartidaDanada
        {
            public int encabezadoId { get; set; }
            public string folio { get; set; }
            public string productoId { get; set; }
            public string descripcion { get; set; }
            public decimal cantTotal { get; set; }
            public int cantDanada { get; set; }
            public decimal precio { get; set; }
            public string ccy { get; set; }
        }
    }
}
