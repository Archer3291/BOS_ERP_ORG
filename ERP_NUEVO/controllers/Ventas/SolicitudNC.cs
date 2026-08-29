using BOS_ERP.Controllers;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas
{
    [Authorize]
    public class SolicitudNCController : Utilities
    {
        private readonly IConfiguration _configuration;
        public SolicitudNCController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private static readonly Dictionary<string, int> ESTATUS_POR_MOTIVO = new Dictionary<string, int>
        {
            ["devolucion"] = 28,   // "Solicitud NC — Devolución de Mercancía"
            ["descuento"] = 29,   // "Solicitud NC — Descuento/Error Comercial"
            ["precio"] = 30,   // "Solicitud NC — Error en Precio"
            ["cancelacion"] = 31,   // "Solicitud NC — Cancelación Parcial"
            ["bonificacion"] = 32,   // "Solicitud NC — Bonificación/Rappel"
            ["otro"] = 33,   // "Solicitud NC — Otro Motivo"
        };

        private const int ESTATUS_SNC_PENDIENTE = 20; 

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industriales", Accion = "Registro de solicitud de NC")]
        public JsonResult RegistrarSolicitud(IFormCollection fc)
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
                        // ── Parsear y validar inputs ──────────────────────────
                        string encabezadoIdsJson = fc["encabezadoIds"].ToString() ?? "[]";
                        var encabezadoIds = JsonConvert.DeserializeObject<List<int>>(encabezadoIdsJson);

                        if (encabezadoIds == null || encabezadoIds.Count == 0)
                            return Json(new { success = false, message = "No se recibieron facturas seleccionadas." });

                        string motivoTipo = fc["motivoTipo"].ToString() ?? "";
                        string tipoNC = fc["tipoNC"].ToString() ?? "standalone";
                        string comentario = fc["comentario"].ToString() ?? "";
                        string prioridad = fc["prioridad"].ToString() ?? "normal";
                        string responsable = fc["responsable"].ToString() ?? "";

                        if (string.IsNullOrWhiteSpace(motivoTipo))
                            return Json(new { success = false, message = "Debe seleccionar el motivo de la solicitud." });

                        if (string.IsNullOrWhiteSpace(comentario) || comentario.Trim().Length < 10)
                            return Json(new { success = false, message = "El comentario debe tener al menos 10 caracteres." });

                        int estatusId = ESTATUS_POR_MOTIVO.ContainsKey(motivoTipo)
                            ? ESTATUS_POR_MOTIVO[motivoTipo]
                            : ESTATUS_SNC_PENDIENTE;

                        int usuarioId = GetUserId(User.Identity.Name);
                        int empresaId = HttpContext.Session.GetInt32("Empresa") ?? 0;
                        int sucursal = HttpContext.Session.GetInt32("Sucursal") ?? 0;

                        // ── Validar que los encabezados pertenecen a la empresa ──
                        string inClause = string.Join(",", encabezadoIds);
                        var encabezadosValidos = RunQuery($@"
                            SELECT id_encabezado, folio
                            FROM encabezadomov
                            WHERE id_encabezado IN ({inClause})
                              AND suc = @suc",
                            new Dictionary<string, object> { ["suc"] = sucursal },
                            false, conn, tx);

                        if (encabezadosValidos.Count == 0)
                            return Json(new { success = false, message = "No se encontraron los documentos indicados." });

                        // ── Obtener folios para log y respuesta ───────────────
                        var folios = encabezadosValidos
                            .Select(r => r["folio"]?.ToString() ?? "")
                            .Where(f => !string.IsNullOrWhiteSpace(f))
                            .ToList();

                        // ── 1. Insertar solicitud maestra en nc_solicitud ─────
                        var rowSolicitud = RunQuery(@"
                            INSERT INTO nc_solicitud
                                (empresa_id, motivo_tipo, tipo_nc,
                                 comentario, prioridad, responsable,
                                 estatus_actual, usuario_solicitud, fecha_solicitud,
                                 encabezado_ids_json, folios_txt)
                            VALUES
                                (@emp, @motivo, @tipo_nc,
                                 @comentario, @prioridad, @responsable,
                                 'PENDIENTE', @usr, NOW(),
                                 @enc_ids::jsonb, @folios)
                            RETURNING id",
                            new Dictionary<string, object>
                            {
                                ["emp"] = empresaId,
                                ["motivo"] = motivoTipo,
                                ["tipo_nc"] = tipoNC,
                                ["comentario"] = comentario.Trim(),
                                ["prioridad"] = prioridad,
                                ["responsable"] = responsable,
                                ["usr"] = usuarioId,
                                ["enc_ids"] = encabezadoIdsJson,
                                ["folios"] = string.Join(", ", folios),
                            },
                            false, conn, tx);

                        int solicitudId = rowSolicitud != null && rowSolicitud.Count > 0
                            ? Convert.ToInt32(rowSolicitud[0]["id"])
                            : 0;

                        // ── 2. Por cada encabezado: actualizar estatus + insertar relación ──
                        foreach (int encId in encabezadoIds)
                        {
                            // 2a. Cambiar estatus_id en encabezadomov
                            RunQuery(@"
                                UPDATE encabezadomov
                                   SET estatus_id = @estatus_id
                                 WHERE id_encabezado = @id
                                   AND suc = @suc",
                                new Dictionary<string, object>
                                {
                                    ["estatus_id"] = estatusId,
                                    ["id"] = encId,
                                    ["suc"] = sucursal,
                                },
                                false, conn, tx);

                            // 2b. Insertar comentario de trazabilidad en enc_comentarios
                            //     (si tu sistema usa una tabla diferente, ajusta aquí)
                            RunQuery(@"
                                INSERT INTO enc_comentarios
                                    (encabezado_id, tipo_comentario, comentario,
                                     usuario_id, fecha, origen_modulo, referencia_id)
                                VALUES
                                    (@enc_id, 'SOLICITUD_NC', @comentario,
                                     @usr, NOW(), 'SolicitudNC', @ref_id)",
                                new Dictionary<string, object>
                                {
                                    ["enc_id"] = encId,
                                    ["comentario"] = $"[{motivoTipo.ToUpper()}] {comentario.Trim()} — Prioridad: {prioridad}",
                                    ["usr"] = usuarioId,
                                    ["ref_id"] = solicitudId,
                                },
                                false, conn, tx);

                            // 2c. Insertar en tabla de detalle de la solicitud
                            if (solicitudId > 0)
                            {
                                RunQuery(@"
                                    INSERT INTO nc_solicitud_detalle
                                        (solicitud_id, encabezado_id, estatus_detalle)
                                    VALUES
                                        (@sol_id, @enc_id, 'PENDIENTE')",
                                    new Dictionary<string, object>
                                    {
                                        ["sol_id"] = solicitudId,
                                        ["enc_id"] = encId,
                                    },
                                    false, conn, tx);
                            }
                        }

                        tx.Commit();

                        LogErrorHelper.RegistrarLog(
                            "SolicitudNC", $"SOL-{solicitudId}",
                            $"Solicitud NC registrada: {encabezadoIds.Count} docs, motivo={motivoTipo}, " +
                            $"folios=[{string.Join(",", folios)}], usuario={User.Identity.Name}",
                            nivel: "INFO");

                        return Json(new
                        {
                            success = true,
                            solicitudId,
                            message = $"Solicitud registrada para {encabezadoIds.Count} documento(s).",
                            folios,
                            tipoNC,
                            encabezadoIds,
                        });
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "SolicitudNC/?");
                        tx.Rollback();
                        LogErrorHelper.RegistrarLog(
                            "SolicitudNC", "ERROR",
                            $"Error en RegistrarSolicitud: {ex.Message}", nivel: "ERROR");

                        return Json(new { success = false, message = "Error al registrar: " + ex.Message });
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  GET /SolicitudNC/ObtenerHistorial
        //  Devuelve el historial de solicitudes NC con su estatus actual.
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        public JsonResult ObtenerHistorial(
            string query = "",
            string estatus = "",
            int page = 1,
            int pageSize = 25)
        {
            try
            {
                int empresaId = HttpContext.Session.GetInt32("Empresa") ?? 0;

                var parameters = new Dictionary<string, object>
                {
                    ["empresa_id"] = empresaId,
                    ["query"] = $"%{query}%",
                    ["estatus"] = estatus ?? "",
                    ["offset"] = (page - 1) * pageSize,
                    ["pageSize"] = pageSize,
                };

                // Query principal — une solicitud con su NC emitida si existe
                var items = RunQuery(@"
                    SELECT
                        s.id,
                        s.fecha_solicitud,
                        s.folios_txt,
                        s.motivo_tipo,
                        s.tipo_nc,
                        s.comentario,
                        s.prioridad,
                        s.responsable,
                        s.estatus_actual,
                        s.usuario_solicitud,
                        s.encabezado_ids_json,
                        -- Folio de la NC emitida si ya existe
                        (
                            SELECT f.folio
                            FROM factura f
                            INNER JOIN nc_solicitud_detalle sd
                                ON sd.encabezado_id = f.encabezado_id
                            WHERE sd.solicitud_id = s.id
                              AND f.tipo LIKE 'NC%'
                              AND f.statusfactura = 'TIMBRADA'
                            ORDER BY f.id DESC
                            LIMIT 1
                        ) AS nc_folio
                    FROM nc_solicitud s
                    WHERE s.empresa_id = @empresa_id
                      AND (@estatus = '' OR s.estatus_actual = @estatus)
                      AND (
                           @query = '%%'
                        OR LOWER(s.folios_txt)       LIKE LOWER(@query)
                        OR LOWER(s.comentario)       LIKE LOWER(@query)
                        OR LOWER(s.motivo_tipo)      LIKE LOWER(@query)
                        OR LOWER(s.usuario_solicitud) LIKE LOWER(@query)
                      )
                    ORDER BY s.fecha_solicitud DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    parameters);

                // Total para paginación
                var totalResult = RunQuery(@"
                    SELECT COUNT(*) AS total
                    FROM nc_solicitud s
                    WHERE s.empresa_id = @empresa_id
                      AND (@estatus = '' OR s.estatus_actual = @estatus)
                      AND (
                           @query = '%%'
                        OR LOWER(s.folios_txt)       LIKE LOWER(@query)
                        OR LOWER(s.comentario)       LIKE LOWER(@query)
                        OR LOWER(s.motivo_tipo)      LIKE LOWER(@query)
                        OR LOWER(s.usuario_solicitud) LIKE LOWER(@query)
                      )",
                    new Dictionary<string, object>
                    {
                        ["empresa_id"] = empresaId,
                        ["query"] = $"%{query}%",
                        ["estatus"] = estatus ?? "",
                    });

                int total = totalResult != null && totalResult.Count > 0
                    ? Convert.ToInt32(totalResult[0]["total"])
                    : 0;

                // Enriquecer cada item con los encabezado_ids deserializados
                var itemsEnriquecidos = items.Select(r => new
                {
                    id = r["id"],
                    fecha_solicitud = r["fecha_solicitud"],
                    folios = r["folios_txt"],
                    motivo_tipo = r["motivo_tipo"],
                    tipo_nc = r["tipo_nc"],
                    comentario = r["comentario"],
                    prioridad = r["prioridad"],
                    responsable = r["responsable"],
                    estatus_actual = r["estatus_actual"],
                    usuario_solicitud = r["usuario_solicitud"],
                    nc_folio = r["nc_folio"],
                    encabezado_ids = _ParseJsonArray(r["encabezado_ids_json"]?.ToString()),
                }).ToList();

                return Json(new { items = itemsEnriquecidos, total });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "SolicitudNC/ObtenerHistorial");
                return Json(new { items = new object[0], total = 0, error = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  POST /SolicitudNC/CambiarEstatus
        //  Permite cambiar el estatus de una solicitud (ej. CANCELADA, EN_PROCESO)
        //  sin generar una NC todavía.
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CambiarEstatus(int solicitudId, string nuevoEstatus, string motivo = "")
        {
            var estatusValidos = new[] { "PENDIENTE", "EN_PROCESO", "CANCELADA", "RECHAZADA" };
            if (!estatusValidos.Contains(nuevoEstatus))
                return Json(new { success = false, message = "Estatus no válido." });

            try
            {
                RunQuery(@"
                    UPDATE nc_solicitud
                       SET estatus_actual = @estatus,
                           fecha_actualizacion = NOW(),
                           usuario_actualizacion = @usr
                     WHERE id = @id",
                    new Dictionary<string, object>
                    {
                        ["estatus"] = nuevoEstatus,
                        ["usr"] = User.Identity.Name,
                        ["id"] = solicitudId,
                    });

                // Si se cancela, revertir estatus_id en encabezadomov a su valor anterior (ej. 1 = Activa)
                if (nuevoEstatus == "CANCELADA")
                {
                    var detalles = RunQuery(@"
                        SELECT encabezado_id FROM nc_solicitud_detalle
                        WHERE solicitud_id = @id AND estatus_detalle = 'PENDIENTE'",
                        new Dictionary<string, object> { ["id"] = solicitudId });

                    foreach (var row in detalles)
                    {
                        int encId = Convert.ToInt32(row["encabezado_id"]);
                        RunQuery(@"
                            UPDATE encabezadomov SET estatus_id = 1
                            WHERE id_encabezado = @id",
                            new Dictionary<string, object> { ["id"] = encId });

                        // Registrar comentario de cancelación
                        if (!string.IsNullOrWhiteSpace(motivo))
                        {
                            RunQuery(@"
                                INSERT INTO enc_comentarios
                                    (encabezado_id, tipo_comentario, comentario, usuario_id, fecha, origen_modulo, referencia_id)
                                VALUES
                                    (@enc_id, 'CANCELACION_SNC', @comentario, @usr, NOW(), 'SolicitudNC', @ref_id)",
                                new Dictionary<string, object>
                                {
                                    ["enc_id"] = encId,
                                    ["comentario"] = $"Solicitud NC cancelada. Motivo: {motivo}",
                                    ["usr"] = GetUserId(User.Identity.Name),
                                    ["ref_id"] = solicitudId,
                                });
                        }
                    }

                    // Marcar detalles como cancelados
                    RunQuery(@"
                        UPDATE nc_solicitud_detalle
                           SET estatus_detalle = 'CANCELADA'
                         WHERE solicitud_id = @id",
                        new Dictionary<string, object> { ["id"] = solicitudId });
                }

                return Json(new { success = true, message = $"Estatus actualizado a {nuevoEstatus}." });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "SolicitudNC/CambiarEstatus");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HELPER — Parsear JSON array desde string
        // ══════════════════════════════════════════════════════════════════════

        private static List<int> _ParseJsonArray(string json)
        {
            try
            {
                var list = JsonConvert.DeserializeObject<List<int>>(json ?? "[]");
                return list ?? new List<int>();
            }
            catch
            {
                return new List<int>();
            }
        }

        [HttpGet]
        public JsonResult ObtenerReportePorFactura(int encabezadoId)
        {
            try
            {
                int sucursal = HttpContext.Session.GetInt32("Sucursal") ?? 0;

                var reporte = RunQuery(@"
            SELECT
                r.id            AS reporte_id,
                r.tipo_danio,
                r.descripcion,
                r.urgencia,
                r.estatus_actual
            FROM rmd_reporte r
            INNER JOIN rmd_reporte_detalle rd ON rd.reporte_id = r.id
            WHERE rd.encabezado_id  = @enc
              AND r.sucursal        = @suc
              AND r.estatus_actual IN ('PENDIENTE','RECIBIDO','DOC_GENERADO')
            ORDER BY r.fecha_reporte DESC
            LIMIT 1",
                    new Dictionary<string, object>
                    {
                        ["enc"] = encabezadoId,
                        ["suc"] = sucursal,
                    });

                if (reporte == null || !reporte.Any())
                    return Json(new { found = false });

                return Json(new { found = true, data = reporte[0] });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "SolicitudNC/ObtenerReportePorFactura");
                return Json(new { found = false, error = ex.Message });
            }
        }

    }
}