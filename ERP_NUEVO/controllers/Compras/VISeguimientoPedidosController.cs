using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public class VISeguimientoPedidosController : Utilities
    {
        // ─── Consulta paginada con filtros ──────────────────────────────────
        [HttpPost]
        public IActionResult ObtenerPedidos(
            string nombre = "",
            string estatus = "",
            string fechaDesde = "",
            string fechaHasta = "",
            int page = 1,
            int pageSize = 25)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                string usuario = User.Identity.Name.ToString();

                var parameters = new Dictionary<string, object>
        {
            { "suc",        sucursalId },
            { "empresa_id", empresaId  },
            { "offset",     (page - 1) * pageSize },
            { "pageSize",   pageSize },
            { "user",       usuario }
        };

                var filtrosExtra = new List<string>();

                if (!string.IsNullOrWhiteSpace(nombre))
                {
                    filtrosExtra.Add(@"(
                LOWER(g.folio_combinado) LIKE LOWER(@nombre)
             OR LOWER(g.cli_prov)        LIKE LOWER(@nombre)
             OR LOWER(cc.n_cli)          LIKE LOWER(@nombre))");
                    parameters["nombre"] = $"%{nombre}%";
                }
                if (!string.IsNullOrWhiteSpace(fechaDesde))
                {
                    filtrosExtra.Add("g.fch >= @fdesde::date");
                    parameters["fdesde"] = fechaDesde;
                }
                if (!string.IsNullOrWhiteSpace(fechaHasta))
                {
                    filtrosExtra.Add("g.fch < (@fhasta::date + INTERVAL '1 day')");
                    parameters["fhasta"] = fechaHasta;
                }

                string filtroEstatusSql = "";
                switch (estatus)
                {
                    case "sin_verificar":
                        filtroEstatusSql = "AND g.fecha_inicio_surtido IS NULL";
                        break;
                    case "en_surtido":
                        filtroEstatusSql = "AND g.fecha_inicio_surtido IS NOT NULL AND g.fecha_fin_surtido IS NULL";
                        break;
                    case "completado":
                        filtroEstatusSql = "AND g.fecha_fin_surtido IS NOT NULL AND g.tiene_cortes_pendientes = false";
                        break;
                    case "pendiente_corte":
                        filtroEstatusSql = "AND g.fecha_fin_surtido IS NOT NULL AND g.tiene_cortes_pendientes = true";
                        break;
                }

                string filtrosExtraSql = filtrosExtra.Count > 0 ? "AND " + string.Join(" AND ", filtrosExtra) : "";

                // ── Estado de surtido POR ORIGEN (para el semáforo de la columna Origen) ──
                // Documentos de stock/modula (VNPED/MODPED): se miden por verificacion_detalle.
                //   completado = todas las partidas con fecha_fin_surtido
                //   en_surtido = alguna partida con fecha_inicio_surtido pero no todas terminadas
                //   pendiente  = ninguna iniciada
                string EstadoSurtidoDoc(string idExpr) => $@"
        CASE
            WHEN {idExpr} IS NULL THEN NULL
            WHEN NOT EXISTS (SELECT 1 FROM partidasdoc p WHERE p.encabezado_id = {idExpr}) THEN NULL
            WHEN NOT EXISTS (
                    SELECT 1 FROM partidasdoc p
                    WHERE p.encabezado_id = {idExpr}
                      AND NOT EXISTS (
                          SELECT 1 FROM verificacion_detalle vd
                          WHERE vd.id_partida = p.id_partidas AND vd.fecha_fin_surtido IS NOT NULL)
                 ) THEN 'completado'
            WHEN EXISTS (
                    SELECT 1 FROM verificacion_detalle vd
                    INNER JOIN partidasdoc p ON p.id_partidas = vd.id_partida
                    WHERE p.encabezado_id = {idExpr} AND vd.fecha_inicio_surtido IS NOT NULL
                 ) THEN 'en_surtido'
            ELSE 'pendiente'
        END";

                // Tubo (TYBPED): se mide por confirmación de cortes.
                string tuboIdExpr = "COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo)";
                // Semáforo del tubo:
                //   pendiente  = pedido de tubo aún sin cortes configurados
                //   en_surtido = cortes ya configurados (documento creado), faltan confirmar
                //   completado = todos los cortes confirmados
                string estadoTuboA = $@"
        CASE
            WHEN dr.id_encabezado_tubo IS NULL THEN NULL
            WHEN NOT EXISTS (
                    SELECT 1 FROM pedido_detalle_corte c
                    INNER JOIN partidasdoc pdc ON pdc.id_partidas = c.pedido_detalle_id
                    WHERE pdc.encabezado_id = {tuboIdExpr}
                 ) THEN 'pendiente'
            WHEN NOT EXISTS (
                    SELECT 1 FROM pedido_detalle_corte c
                    INNER JOIN partidasdoc pdc ON pdc.id_partidas = c.pedido_detalle_id
                    WHERE pdc.encabezado_id = {tuboIdExpr}
                      AND NOT EXISTS (
                          SELECT 1 FROM pedido_detalle_corte_asignacion a
                          WHERE a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado')
                 ) THEN 'completado'
            ELSE 'en_surtido'
        END";

                string estadoNormalA = EstadoSurtidoDoc("dr.id_encabezado_normal");
                string estadoModulaA = EstadoSurtidoDoc("dr.id_encabezado_modula");
                string estadoNormalB = EstadoSurtidoDoc("em2.id_encabezado");

                // ── g = "grupos": UNION de (A) pedidos con split en documentos_relacionados
                //                          y (B) pedidos sueltos que nunca se dividieron ──
                string query = $@"
WITH grupos AS (

  -- (A) Pedidos divididos: normal / tubo / modula
    SELECT
        dr.id_encabezado_padre                                   AS id_grupo,
        COALESCE(en.cli_prov, et.cli_prov, em.cli_prov)          AS cli_prov,
        COALESCE(en.fch, et.fch, em.fch)                         AS fch,
        COALESCE(en.imp,0) + COALESCE(et_real.imp, et.imp, 0) + COALESCE(em.imp,0) AS imp,
        COALESCE(en.usr_doc, et.usr_doc, em.usr_doc)             AS usr_doc,
        COALESCE(en.tipo_proceso, et.tipo_proceso, em.tipo_proceso) AS tipo_proceso,

        CONCAT_WS(' + ',
            NULLIF(dr.folio_normal, ''),
            NULLIF(COALESCE(et_real.folio, dr.folio_tubo), ''),
            NULLIF(dr.folio_modula, '')
        ) AS folio_combinado,

        dr.id_encabezado_normal, dr.folio_normal,

        -- ── id/folio REAL del pedido de tubo (no la cotización) ──
        COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo) AS id_encabezado_tubo,
        COALESCE(et_real.folio, dr.folio_tubo)                 AS folio_tubo,

        dr.id_encabezado_modula, dr.folio_modula,

        -- fecha_inicio: usa id_tubo_real para buscar partidas del tubo
        (SELECT MIN(vd.fecha_inicio_surtido)
         FROM verificacion_detalle vd
         INNER JOIN partidasdoc pd ON pd.id_partidas = vd.id_partida
         WHERE pd.encabezado_id IN (
             dr.id_encabezado_normal,
             COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo),
             dr.id_encabezado_modula)
           AND vd.fecha_inicio_surtido IS NOT NULL
        ) AS fecha_inicio_surtido,

        CASE WHEN (
            (dr.id_encabezado_normal IS NULL OR EXISTS (
                SELECT 1 FROM verificacion_detalle vd2
                INNER JOIN partidasdoc pd2 ON pd2.id_partidas = vd2.id_partida
                WHERE pd2.encabezado_id = dr.id_encabezado_normal AND vd2.fecha_fin_surtido IS NOT NULL))
            AND
(dr.id_encabezado_tubo IS NULL OR NOT EXISTS (
    SELECT 1
    FROM pedido_detalle_corte c
    INNER JOIN partidasdoc pdc ON pdc.id_partidas = c.pedido_detalle_id
    WHERE pdc.encabezado_id = COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo)
      AND NOT EXISTS (
          SELECT 1 FROM pedido_detalle_corte_asignacion a
          WHERE a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado'
      )
))
            AND
            (dr.id_encabezado_modula IS NULL OR EXISTS (
                SELECT 1 FROM verificacion_detalle vd4
                INNER JOIN partidasdoc pd4 ON pd4.id_partidas = vd4.id_partida
                WHERE pd4.encabezado_id = dr.id_encabezado_modula AND vd4.fecha_fin_surtido IS NOT NULL))
        ) THEN (
            SELECT MAX(x.fmin) FROM (
                SELECT MIN(vd5.fecha_fin_surtido) AS fmin
                FROM verificacion_detalle vd5
                INNER JOIN partidasdoc pd5 ON pd5.id_partidas = vd5.id_partida
                WHERE pd5.encabezado_id IN (
                    dr.id_encabezado_normal,
                    COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo),
                    dr.id_encabezado_modula)
                  AND vd5.fecha_fin_surtido IS NOT NULL
            ) x
        ) ELSE NULL END AS fecha_fin_surtido,

        (SELECT COUNT(*) FROM partidasdoc pdx
         WHERE pdx.encabezado_id IN (
             dr.id_encabezado_normal,
             COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo),
             dr.id_encabezado_modula)
        ) AS total_partidas,

COALESCE((
    SELECT COUNT(*)
    FROM verificacion_detalle vd6
    INNER JOIN partidasdoc pd6 ON pd6.id_partidas = vd6.id_partida
    WHERE pd6.encabezado_id IN (dr.id_encabezado_normal, dr.id_encabezado_modula)
      AND vd6.cantidad_verificada > 0
), 0)
+
COALESCE((
    SELECT COUNT(DISTINCT pdc.id_partidas)
    FROM partidasdoc pdc
    WHERE pdc.encabezado_id = COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo)
      AND EXISTS (SELECT 1 FROM pedido_detalle_corte c WHERE c.pedido_detalle_id = pdc.id_partidas)
      AND NOT EXISTS (
          SELECT 1 FROM pedido_detalle_corte c
          WHERE c.pedido_detalle_id = pdc.id_partidas
            AND NOT EXISTS (
                SELECT 1 FROM pedido_detalle_corte_asignacion a
                WHERE a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado'
            )
      )
), 0) AS partidas_verificadas,

        (dr.id_encabezado_normal IS NOT NULL) AS tiene_normal,
        (dr.id_encabezado_tubo   IS NOT NULL) AS tiene_tubo,
        (dr.id_encabezado_modula IS NOT NULL) AS tiene_modula,
        dr.id_encabezado_padre                AS pedido_padre_id,

        -- ── cortes pendientes, ahora usando el id REAL del pedido de tubo ──
        CASE WHEN dr.id_encabezado_tubo IS NOT NULL THEN (
            EXISTS (
                SELECT 1
                FROM pedido_detalle_corte c
                INNER JOIN partidasdoc pdc ON pdc.id_partidas = c.pedido_detalle_id
                WHERE pdc.encabezado_id = COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM pedido_detalle_corte_asignacion a
                      WHERE a.pedido_detalle_corte_id = c.id
                        AND a.estatus = 'confirmado'
                  )
            )
        ) ELSE false END AS tiene_cortes_pendientes,

        -- ── Estado de surtido por origen (semáforo) ──
        ({estadoNormalA}) AS estado_normal,
        ({estadoTuboA})   AS estado_tubo,
        ({estadoModulaA}) AS estado_modula

    FROM documentos_relacionados dr
    LEFT JOIN encabezadomov en ON en.id_encabezado = dr.id_encabezado_normal
    LEFT JOIN encabezadomov et ON et.id_encabezado = dr.id_encabezado_tubo
    LEFT JOIN encabezadomov em ON em.id_encabezado = dr.id_encabezado_modula

    -- ── Resolver el pedido de tubo real generado a partir de la cotización TYBCOT ──
    --    VTPedido.Guardar lo crea con nat='VIPED' (sólo se vuelve 'TYBPED' al aprobar
    --    crédito), pero es el ÚNICO hijo del TYBCOT, así que se acepta cualquiera de los dos.
    LEFT JOIN LATERAL (
        SELECT eht.id_encabezado, eht.folio, eht.imp
        FROM encabezadomov eht
        WHERE eht.encabezados_padre = dr.id_encabezado_tubo
          AND eht.nat IN ('TYBPED', 'VIPED')
        ORDER BY CASE WHEN eht.nat = 'TYBPED' THEN 0 ELSE 1 END, eht.id_encabezado DESC
        LIMIT 1
    ) et_real ON dr.id_encabezado_tubo IS NOT NULL

    WHERE dr.empresa_id = @empresa_id

UNION ALL

    -- (B) Pedidos sueltos: nunca pasaron por el split (folio único)
    SELECT
        em2.id_encabezado                     AS id_grupo,
        em2.cli_prov,
        em2.fch,
        em2.imp,
        em2.usr_doc,
        em2.tipo_proceso,
        em2.folio                             AS folio_combinado,

        em2.id_encabezado                     AS id_encabezado_normal,
        em2.folio                             AS folio_normal,
        NULL::int                             AS id_encabezado_tubo,
        NULL::varchar                         AS folio_tubo,
        NULL::int                             AS id_encabezado_modula,
        NULL::varchar                         AS folio_modula,

        (SELECT MIN(vd7.fecha_inicio_surtido)
         FROM verificacion_detalle vd7
         INNER JOIN partidasdoc pd7 ON pd7.id_partidas = vd7.id_partida
         WHERE pd7.encabezado_id = em2.id_encabezado
           AND vd7.fecha_inicio_surtido IS NOT NULL
        ) AS fecha_inicio_surtido,

        (SELECT MIN(vd8.fecha_fin_surtido)
         FROM verificacion_detalle vd8
         INNER JOIN partidasdoc pd8 ON pd8.id_partidas = vd8.id_partida
         WHERE pd8.encabezado_id = em2.id_encabezado
           AND vd8.fecha_fin_surtido IS NOT NULL
        ) AS fecha_fin_surtido,

        (SELECT COUNT(*) FROM partidasdoc pd9 WHERE pd9.encabezado_id = em2.id_encabezado) AS total_partidas,

        COALESCE((
            SELECT COUNT(*)
            FROM verificacion_detalle vd9
            INNER JOIN partidasdoc pd10 ON pd10.id_partidas = vd9.id_partida
            WHERE pd10.encabezado_id = em2.id_encabezado AND vd9.cantidad_verificada > 0
        ), 0) AS partidas_verificadas,

        true  AS tiene_normal,
        false AS tiene_tubo,
        false AS tiene_modula,
        NULL::int AS pedido_padre_id,

        false AS tiene_cortes_pendientes,

        -- ── Estado de surtido por origen (semáforo). Suelto = solo 'normal' ──
        ({estadoNormalB}) AS estado_normal,
        NULL::text        AS estado_tubo,
        NULL::text        AS estado_modula

    FROM encabezadomov em2
    WHERE em2.nat IN ('VIPED', 'VNPED')
      AND em2.suc = @suc
      AND NOT EXISTS (
          SELECT 1 FROM documentos_relacionados dr2
          WHERE em2.id_encabezado IN (dr2.id_encabezado_normal, dr2.id_encabezado_tubo, dr2.id_encabezado_modula)
      )
      -- Excluir el pedido de tubo (VIPED hijo de un TYBCOT que ya vive en un grupo)
      AND NOT EXISTS (
          SELECT 1 FROM documentos_relacionados dr3
          WHERE dr3.id_encabezado_tubo = em2.encabezados_padre
      )
)
SELECT g.*, cc.n_cli
FROM grupos g
INNER JOIN catclientes cc ON cc.cve_cli = g.cli_prov AND cc.empresa_id = @empresa_id
WHERE g.usr_doc = @user
  {filtroEstatusSql}
  {filtrosExtraSql}
ORDER BY g.fch DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var items = RunQuery(query, parameters);

                // ── Total (mismo WHERE sin paginación) ──
                string queryTotal = $@"
WITH grupos AS (
    SELECT dr.id_encabezado_padre AS id_grupo,
           COALESCE(en.cli_prov, et.cli_prov, em.cli_prov) AS cli_prov,
           COALESCE(en.fch, et.fch, em.fch) AS fch,
           COALESCE(en.usr_doc, et.usr_doc, em.usr_doc) AS usr_doc,
           CONCAT_WS(' + ', NULLIF(dr.folio_normal,''), NULLIF(COALESCE(et_real.folio, dr.folio_tubo),''), NULLIF(dr.folio_modula,'')) AS folio_combinado,
           (SELECT MIN(vd.fecha_inicio_surtido) FROM verificacion_detalle vd
            INNER JOIN partidasdoc pd ON pd.id_partidas = vd.id_partida
            WHERE pd.encabezado_id IN (dr.id_encabezado_normal, COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo), dr.id_encabezado_modula)
              AND vd.fecha_inicio_surtido IS NOT NULL) AS fecha_inicio_surtido,

           CASE WHEN dr.id_encabezado_tubo IS NOT NULL THEN (
               EXISTS (
                   SELECT 1
                   FROM pedido_detalle_corte c
                   INNER JOIN partidasdoc pdc ON pdc.id_partidas = c.pedido_detalle_id
                   WHERE pdc.encabezado_id = COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo)
                     AND NOT EXISTS (
                         SELECT 1 FROM pedido_detalle_corte_asignacion a
                         WHERE a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado'
                     )
               )
           ) ELSE false END AS tiene_cortes_pendientes,

           (SELECT MAX(x.fmin) FROM (
                SELECT MIN(vd5.fecha_fin_surtido) AS fmin
                FROM verificacion_detalle vd5
                INNER JOIN partidasdoc pd5 ON pd5.id_partidas = vd5.id_partida
                WHERE pd5.encabezado_id IN (dr.id_encabezado_normal, COALESCE(et_real.id_encabezado, dr.id_encabezado_tubo), dr.id_encabezado_modula)
                  AND vd5.fecha_fin_surtido IS NOT NULL
           ) x) AS fecha_fin_surtido

    FROM documentos_relacionados dr
    LEFT JOIN encabezadomov en ON en.id_encabezado = dr.id_encabezado_normal
    LEFT JOIN encabezadomov et ON et.id_encabezado = dr.id_encabezado_tubo
    LEFT JOIN encabezadomov em ON em.id_encabezado = dr.id_encabezado_modula
    LEFT JOIN LATERAL (
        SELECT eht.id_encabezado, eht.folio
        FROM encabezadomov eht
        WHERE eht.encabezados_padre = dr.id_encabezado_tubo AND eht.nat IN ('TYBPED', 'VIPED')
        ORDER BY CASE WHEN eht.nat = 'TYBPED' THEN 0 ELSE 1 END, eht.id_encabezado DESC LIMIT 1
    ) et_real ON dr.id_encabezado_tubo IS NOT NULL
    WHERE dr.empresa_id = @empresa_id

    UNION ALL

    SELECT em2.id_encabezado, em2.cli_prov, em2.fch, em2.usr_doc, em2.folio,
           (SELECT MIN(vd7.fecha_inicio_surtido) FROM verificacion_detalle vd7
            INNER JOIN partidasdoc pd7 ON pd7.id_partidas = vd7.id_partida
            WHERE pd7.encabezado_id = em2.id_encabezado AND vd7.fecha_inicio_surtido IS NOT NULL) AS fecha_inicio_surtido,
           false AS tiene_cortes_pendientes,
           NULL::timestamp AS fecha_fin_surtido
    FROM encabezadomov em2
    WHERE em2.nat IN ('VIPED', 'VNPED') AND em2.suc = @suc
      AND NOT EXISTS (
          SELECT 1 FROM documentos_relacionados dr2
          WHERE em2.id_encabezado IN (dr2.id_encabezado_normal, dr2.id_encabezado_tubo, dr2.id_encabezado_modula))
      AND NOT EXISTS (
          SELECT 1 FROM documentos_relacionados dr3
          WHERE dr3.id_encabezado_tubo = em2.encabezados_padre)
)
SELECT COUNT(*) AS total
FROM grupos g
INNER JOIN catclientes cc ON cc.cve_cli = g.cli_prov AND cc.empresa_id = @empresa_id
WHERE g.usr_doc = @user {filtroEstatusSql} {filtrosExtraSql}";

                var totalParams = new Dictionary<string, object>
        {
            { "suc", sucursalId }, { "empresa_id", empresaId }, { "user", usuario }
        };
                if (parameters.ContainsKey("nombre")) totalParams["nombre"] = parameters["nombre"];
                if (parameters.ContainsKey("fdesde")) totalParams["fdesde"] = parameters["fdesde"];
                if (parameters.ContainsKey("fhasta")) totalParams["fhasta"] = parameters["fhasta"];

                var totalResult = RunQuery(queryTotal, totalParams);
                int total = totalResult?.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

                return Json(new { success = true, data = items, total });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Detalle completo de un pedido/verificación ─────────────────────
        [HttpGet]
        public IActionResult ObtenerDetalle(int idGrupo)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                var relResult = RunQuery(@"
            SELECT id_encabezado_normal, id_encabezado_tubo, id_encabezado_modula,
                   folio_normal, folio_tubo, folio_modula
            FROM documentos_relacionados
            WHERE id_encabezado_padre = @id AND empresa_id = @empresa_id
            ORDER BY id_relacion DESC LIMIT 1",
                    new Dictionary<string, object> { { "id", idGrupo }, { "empresa_id", empresaId } });

                int? idNormal, idTubo, idModula;

                if (relResult?.Count > 0)
                {
                    var rel = relResult[0];
                    idNormal = rel["id_encabezado_normal"] != DBNull.Value ? Convert.ToInt32(rel["id_encabezado_normal"]) : (int?)null;
                    idTubo = rel["id_encabezado_tubo"] != DBNull.Value ? Convert.ToInt32(rel["id_encabezado_tubo"]) : (int?)null;
                    idModula = rel["id_encabezado_modula"] != DBNull.Value ? Convert.ToInt32(rel["id_encabezado_modula"]) : (int?)null;
                }
                else
                {
                    idNormal = idGrupo;
                    idTubo = null;
                    idModula = null;
                }

                // ── Resolver el pedido de tubo real (hijo del TYBCOT): 'TYBPED' o 'VIPED' ──
                if (idTubo.HasValue)
                {
                    var tuboRealResult = RunQuery(@"
        SELECT id_encabezado
        FROM encabezadomov
        WHERE encabezados_padre = @idTuboCot
          AND nat IN ('TYBPED', 'VIPED')
        ORDER BY CASE WHEN nat = 'TYBPED' THEN 0 ELSE 1 END, id_encabezado DESC
        LIMIT 1",
                        new Dictionary<string, object> { { "idTuboCot", idTubo.Value } });

                    if (tuboRealResult?.Count > 0)
                        idTubo = Convert.ToInt32(tuboRealResult[0]["id_encabezado"]);
                    // Si no hay resultado, se queda con la cotización (aún no se ha generado el pedido)
                }

                var idsHijos = new[] { idNormal, idTubo, idModula }.Where(x => x.HasValue).Select(x => x.Value).ToList();
                if (idsHijos.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                // ── Encabezado de referencia (folio/cliente) del primero disponible ──
                var encResult = RunQuery(@"
    SELECT
        COALESCE(en.id_encabezado, et.id_encabezado, em.id_encabezado) AS id_encabezado,
        COALESCE(en.folio, et.folio, em.folio)                         AS folio,
        COALESCE(en.nat, et.nat, em.nat)                               AS nat,
        COALESCE(en.fch, et.fch, em.fch)                               AS fch,
        COALESCE(en.cli_prov, et.cli_prov, em.cli_prov)                AS cli_prov,
        COALESCE(en.coment1, et.coment1, em.coment1)                   AS coment1,
        COALESCE(en.coment_aut, et.coment_aut, em.coment_aut)          AS coment_aut,
        COALESCE(en.tipo_proceso, et.tipo_proceso, em.tipo_proceso)    AS tipo_proceso,
        COALESCE(en.encabezados_padre, et.encabezados_padre, em.encabezados_padre) AS encabezados_padre,
        cc.n_cli, cc.rfc,
        ce.descripcion AS estatus_desc
    FROM (SELECT 1) dummy
    LEFT JOIN encabezadomov en ON en.id_encabezado = @id_normal
    LEFT JOIN encabezadomov et ON et.id_encabezado = @id_tubo
    LEFT JOIN encabezadomov em ON em.id_encabezado = @id_modula
    INNER JOIN catclientes cc
        ON cc.cve_cli = COALESCE(en.cli_prov, et.cli_prov, em.cli_prov)
       AND cc.empresa_id = @empresa_id
    LEFT JOIN estatus_docs ce
        ON ce.id_estatus = COALESCE(en.estatus_id, et.estatus_id, em.estatus_id)",
                    new Dictionary<string, object>
                    {
        { "id_normal", (object)idNormal ?? -1 },
        { "id_tubo",   (object)idTubo   ?? -1 },
        { "id_modula", (object)idModula ?? -1 },
        { "empresa_id", empresaId }
                    });

                if (encResult == null || encResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var enc = encResult[0];

                // ── Sumar importe total del grupo (todos los hijos) ──
                var impResult = RunQuery(@"
            SELECT COALESCE(SUM(imp),0) AS imp_total
            FROM encabezadomov
            WHERE id_encabezado = ANY(@ids)",
                    new Dictionary<string, object> { { "ids", idsHijos.ToArray() } });
                decimal impTotal = impResult?.Count > 0 ? Convert.ToDecimal(impResult[0]["imp_total"]) : 0;

                // ── Partidas con origen ──
                string queryPartidas = @"
        SELECT
            pd.id_partidas, pd.nro_part, pd.cve_prod, pd.descr_prod,
            pd.cant_ud AS cantidad_pedida, pd.pv_prod AS precio, pd.dto1 AS descuento, pd.ud AS unidad,

            CASE
                WHEN pd.encabezado_id = @id_normal THEN 'normal'
                WHEN pd.encabezado_id = @id_tubo   THEN 'tubo'
                WHEN pd.encabezado_id = @id_modula THEN 'modula'
            END AS origen,

            COALESCE(
                (SELECT vd_f.cantidad_verificada FROM verificacion_detalle vd_f
                 WHERE vd_f.id_partida = pd.id_partidas AND vd_f.encabezado_verificacion_id IS NOT NULL
                 ORDER BY vd_f.id DESC LIMIT 1),
                (SELECT vd_t.cantidad_verificada FROM verificacion_detalle vd_t
                 WHERE vd_t.id_partida = pd.id_partidas AND vd_t.encabezado_verificacion_id IS NULL
                   AND vd_t.cantidad_verificada > 0
                 ORDER BY vd_t.id DESC LIMIT 1), 0
            ) AS cantidad_verificada,

            COALESCE(
                (SELECT vd_f.cve_almacen FROM verificacion_detalle vd_f
                 WHERE vd_f.id_partida = pd.id_partidas AND vd_f.encabezado_verificacion_id IS NOT NULL
                 ORDER BY vd_f.id DESC LIMIT 1),
                (SELECT vd_t.cve_almacen FROM verificacion_detalle vd_t
                 WHERE vd_t.id_partida = pd.id_partidas AND vd_t.encabezado_verificacion_id IS NULL
                 ORDER BY vd_t.id DESC LIMIT 1), ''
            ) AS cve_almacen,

            COALESCE(
                (SELECT vd_f.n_almacen FROM verificacion_detalle vd_f
                 WHERE vd_f.id_partida = pd.id_partidas AND vd_f.encabezado_verificacion_id IS NOT NULL
                 ORDER BY vd_f.id DESC LIMIT 1),
                (SELECT vd_t.n_almacen FROM verificacion_detalle vd_t
                 WHERE vd_t.id_partida = pd.id_partidas AND vd_t.encabezado_verificacion_id IS NULL
                 ORDER BY vd_t.id DESC LIMIT 1), ''
            ) AS n_almacen,

            COALESCE(
                (SELECT vd_f.lote FROM verificacion_detalle vd_f
                 WHERE vd_f.id_partida = pd.id_partidas AND vd_f.encabezado_verificacion_id IS NOT NULL
                 ORDER BY vd_f.id DESC LIMIT 1),
                (SELECT vd_t.lote FROM verificacion_detalle vd_t
                 WHERE vd_t.id_partida = pd.id_partidas AND vd_t.encabezado_verificacion_id IS NULL
                 ORDER BY vd_t.id DESC LIMIT 1), ''
            ) AS lote,

            COALESCE(
                (SELECT vd_f.ubicacion FROM verificacion_detalle vd_f
                 WHERE vd_f.id_partida = pd.id_partidas AND vd_f.encabezado_verificacion_id IS NOT NULL
                 ORDER BY vd_f.id DESC LIMIT 1),
                (SELECT vd_t.ubicacion FROM verificacion_detalle vd_t
                 WHERE vd_t.id_partida = pd.id_partidas AND vd_t.encabezado_verificacion_id IS NULL
                 ORDER BY vd_t.id DESC LIMIT 1), ''
            ) AS ubicacion,

            (SELECT MIN(vd_fi.fecha_inicio_surtido) FROM verificacion_detalle vd_fi
             WHERE vd_fi.id_partida = pd.id_partidas) AS fecha_inicio_surtido,

            (SELECT MIN(vd_fin.fecha_fin_surtido) FROM verificacion_detalle vd_fin
             WHERE vd_fin.id_partida = pd.id_partidas AND vd_fin.fecha_fin_surtido IS NOT NULL) AS fecha_fin_surtido,

            (SELECT COUNT(*) FROM pedido_detalle_corte c
             WHERE c.pedido_detalle_id = pd.id_partidas
            ) AS cortes_total,

            COALESCE((
                SELECT COUNT(*)
                FROM pedido_detalle_corte c
                INNER JOIN pedido_detalle_corte_asignacion a
                    ON a.pedido_detalle_corte_id = c.id
                WHERE c.pedido_detalle_id = pd.id_partidas
                  AND a.estatus = 'confirmado'
            ), 0) AS cortes_confirmados,

            COALESCE((
                SELECT COUNT(*)
                FROM pedido_detalle_corte c
                INNER JOIN pedido_detalle_corte_asignacion a
                    ON a.pedido_detalle_corte_id = c.id
                WHERE c.pedido_detalle_id = pd.id_partidas
            ), 0) AS cortes_asignados

        FROM partidasdoc pd
        WHERE pd.encabezado_id = ANY(@ids)
        ORDER BY
            CASE WHEN pd.encabezado_id = @id_normal THEN 1
                 WHEN pd.encabezado_id = @id_tubo   THEN 2
                 WHEN pd.encabezado_id = @id_modula THEN 3 END,
            pd.nro_part";

                var partidas = RunQuery(queryPartidas, new Dictionary<string, object>
        {
            { "ids",       idsHijos.ToArray() },
            { "id_normal", (object)idNormal ?? -1 },
            { "id_tubo",   (object)idTubo   ?? -1 },
            { "id_modula", (object)idModula ?? -1 }
        });

                // después de obtener 'partidas' de RunQuery en ObtenerDetalle
                foreach (var row in partidas)
                {
                    if (row["origen"]?.ToString() == "tubo")
                    {
                        int total = row["cortes_total"] != DBNull.Value ? Convert.ToInt32(row["cortes_total"]) : 0;
                        int conf = row["cortes_confirmados"] != DBNull.Value ? Convert.ToInt32(row["cortes_confirmados"]) : 0;
                        if (total > 0 && conf >= total)
                        {
                            row["cantidad_verificada"] = row["cantidad_pedida"]; // refleja que ya está resuelto
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    encabezado = new
                    {
                        id_grupo = idGrupo,
                        folio = enc["folio"],
                        nat = enc["nat"],
                        fch = enc["fch"],
                        cli_prov = enc["cli_prov"],
                        n_cli = enc["n_cli"],
                        rfc = enc["rfc"],
                        imp = impTotal,
                        coment1 = enc["coment1"],
                        coment_aut = enc["coment_aut"],
                        tipo_proceso = enc["tipo_proceso"],
                        estatus_desc = enc["estatus_desc"],
                        encabezado_padre = enc["encabezados_padre"],
                        tiene_normal = idNormal.HasValue,
                        tiene_tubo = idTubo.HasValue,
                        tiene_modula = idModula.HasValue
                    },
                    partidas
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── KPIs para el dashboard de resumen ──────────────────────────────
        [HttpGet]
        public IActionResult ObtenerKpis()
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

                string query = @"
    SELECT
        COUNT(*) FILTER (WHERE em.nat = 'VIPED' AND em.estatus_id = 11)   AS pedidos_aprobados,
        COUNT(*) FILTER (WHERE em.nat = 'RMP')                             AS total_verificaciones,
        COUNT(*) FILTER (WHERE em.nat = 'RMP'
            AND EXISTS (SELECT 1 FROM verificacion_detalle vd
                        WHERE vd.encabezado_verificacion_id = em.id_encabezado
                          AND vd.fecha_inicio_surtido IS NOT NULL
                          AND vd.fecha_fin_surtido IS NULL))               AS en_surtido,
        COUNT(*) FILTER (WHERE em.nat = 'RMP'
            AND EXISTS (SELECT 1 FROM verificacion_detalle vd
                        WHERE vd.encabezado_verificacion_id = em.id_encabezado
                          AND vd.fecha_fin_surtido IS NOT NULL))            AS completados,
        COALESCE(SUM(em.imp) FILTER (
            WHERE em.nat = 'VIPED' AND em.estatus_id = 11), 0)             AS importe_pendiente,
        COALESCE(SUM(em.imp) FILTER (
            WHERE em.nat = 'RMP'), 0)                                      AS importe_verificado
    FROM encabezadomov em
    WHERE em.suc = @suc
      AND em.fch >= (NOW() - INTERVAL '30 days')";

                var r = RunQuery(query,
                    new Dictionary<string, object>
                    {
                        { "suc",        sucursalId },
                        { "empresa_id", empresaId  }
                    });

                var row = r?[0] ?? new Dictionary<string, object>();
                return Json(new
                {
                    success = true,
                    pedidos_aprobados = row.ContainsKey("pedidos_aprobados") ? row["pedidos_aprobados"] : 0,
                    total_verificaciones = row.ContainsKey("total_verificaciones") ? row["total_verificaciones"] : 0,
                    en_surtido = row.ContainsKey("en_surtido") ? row["en_surtido"] : 0,
                    completados = row.ContainsKey("completados") ? row["completados"] : 0,
                    importe_pendiente = row.ContainsKey("importe_pendiente") ? row["importe_pendiente"] : 0,
                    importe_verificado = row.ContainsKey("importe_verificado") ? row["importe_verificado"] : 0
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}