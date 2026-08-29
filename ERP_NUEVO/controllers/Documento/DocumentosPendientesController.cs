using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Documento
{
    //[RightAuthorize(new[] { "ventas", "ventas_global" })]
    public class DocumentosPendientesVentasController : Utilities
    {
        private const int ESTATUS_PENDIENTE = 1;

        public IActionResult Index()
        {
            return View();
        }

        // ── VICOT — Cotizaciones Pendientes ─────────────────────────────
        public JsonResult GetVICOT(string nombre, int page = 1, int pageSize = 50)
        {
            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
                return GetDocumentosPendientes(new[] { "VICOT" }, nombre, page, pageSize);

            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_especial"))
                return GetDocumentosPendientes(new[] { "VINCOT" }, nombre, page, pageSize);

            return Json(new
            {
                success = false,
                message = "No tienes permisos para consultar este documento."
            });
        }

        // ── VIPED — Pedidos Pendientes ──────────────────────────────────
        public JsonResult GetVIPED(string nombre, int page = 1, int pageSize = 50)
        {
            return GetDocumentosPendientes(new[] { "VIPED" }, nombre, page, pageSize);
        }

        // ── VIREM — Remisiones con facturación pendiente o parcial ─────
        // Agrupa remisiones hermanas (mismo encabezados_padre) y excluye
        // las que ya están completamente facturadas.
        public JsonResult GetVIREM(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>
            {
                { "nombre",     $"%{nombre ?? ""}%" },
                { "offset",     (page - 1) * pageSize },
                { "pageSize",   pageSize },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            // ── Query principal agrupado ────────────────────────────────────
            string query = @"
WITH grupos AS (
    SELECT
        COALESCE(NULLIF(em.encabezados_padre, 0), em.id_encabezado) AS grupo_id,
        em.id_encabezado,
        em.folio,
        em.fch,
        em.fol_doc,
        em.variacion,
        em.imp,
        em.cli_prov,
        em.usr0,
        cc.n_cli,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'pendiente') AS partidas_pendientes,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'parcial')   AS partidas_parciales,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'completa')  AS partidas_completas,
        COUNT(rpf.id)                                          AS total_partidas_tracking,
        COALESCE(
    SUM(
        -- subtotal pendiente de esta partida
        (rpf.cantidad_pendiente * rpf.precio_unitario
         * (1 - rpf.descuento / 100.0))::numeric(18,2)
        /
        -- subtotal total de la remisión (em.imp menos el IVA de imp_oc)
        NULLIF(
            em.imp - (
                SELECT COALESCE(SUM(io.importe), 0)
                FROM imp_oc io
                INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id
                WHERE io.encabezado_id = em.id_encabezado
                  AND ci.es_retencion = false
            ),
        0)
        *
        em.imp   -- total con IVA → da el importe pendiente con IVA incluido
    ) FILTER (WHERE rpf.estatus IN ('pendiente','parcial')),
    0
) AS importe_pendiente
    FROM encabezadomov em
    INNER JOIN catclientes cc
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    LEFT JOIN remision_partidas_facturadas rpf
        ON rpf.encabezado_remision_id = em.id_encabezado
    WHERE em.nat        = 'VIREM'
      AND em.suc        = @suc
      AND em.estatus_id IN (1, 41)          -- ← incluye parciales
      AND (
           LOWER(em.gen || '-' || em.nat || '-' ||
                 EXTRACT(YEAR FROM em.fch)::text || '-' ||
                 em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
      )
      AND NOT EXISTS (
          SELECT 1
          FROM remision_partidas_facturadas rpf2
          WHERE rpf2.encabezado_remision_id = em.id_encabezado
            AND (
                (SELECT COUNT(*) FROM remision_partidas_facturadas rpf3
                 WHERE rpf3.encabezado_remision_id = em.id_encabezado) > 0
                AND
                (SELECT COUNT(*) FROM remision_partidas_facturadas rpf4
                 WHERE rpf4.encabezado_remision_id = em.id_encabezado
                   AND rpf4.estatus != 'completa') = 0
            )
          LIMIT 1
      )
    GROUP BY
        em.id_encabezado, em.folio, em.fch, em.fol_doc, em.variacion,
        em.imp, em.cli_prov, em.usr0, cc.n_cli, em.encabezados_padre
)
SELECT
    grupo_id,
    STRING_AGG(id_encabezado::text, ',' ORDER BY id_encabezado) AS remisiones_ids,
    COUNT(id_encabezado)         AS total_remisiones,
    MIN(folio)                   AS folio,
    cli_prov,
    n_cli,
    MIN(fch)                     AS fch,
    SUM(imp)                     AS imp,
    SUM(partidas_pendientes)     AS partidas_pendientes,
    SUM(partidas_parciales)      AS partidas_parciales,
    SUM(partidas_completas)      AS partidas_completas,
    SUM(total_partidas_tracking) AS total_partidas_tracking,
    SUM(importe_pendiente)       AS importe_pendiente,
    STRING_AGG(folio, ' | ' ORDER BY id_encabezado) AS todos_folios,
    CASE
        WHEN SUM(partidas_parciales) > 0 OR SUM(partidas_completas) > 0
        THEN 'PARCIAL'
        ELSE 'PENDIENTE'
    END AS estatus_label
FROM grupos
GROUP BY grupo_id, cli_prov, n_cli
ORDER BY MIN(fch) DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var items = RunQuery(query, parameters);

            // ── Total de grupos ────────────────────────────────────────────
            string queryTotal = @"
SELECT COUNT(*) AS total
FROM (
    SELECT COALESCE(NULLIF(em.encabezados_padre, 0), em.id_encabezado) AS grupo_id
    FROM encabezadomov em
    INNER JOIN catclientes cc
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    WHERE em.nat        = 'VIREM'
      AND em.suc        = @suc
      AND em.estatus_id IN (1, 41)          -- ← igual que el principal
      AND (
           LOWER(em.gen || '-' || em.nat || '-' ||
                 EXTRACT(YEAR FROM em.fch)::text || '-' ||
                 em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
      )
    GROUP BY COALESCE(NULLIF(em.encabezados_padre, 0), em.id_encabezado)
) t";

            // Para el total reutilizamos los mismos params (sin offset/pageSize)
            var totalParams = new Dictionary<string, object>
            {
                { "nombre",     $"%{nombre ?? ""}%" },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            var totalResult = RunQuery(queryTotal, totalParams);
            int total = totalResult?.Count > 0
                ? Convert.ToInt32(totalResult[0]["total"])
                : 0;

            return Json(new { data = items, total });
        }

        // ── Todos los tipos pendientes ──────────────────────────────────
        public JsonResult GetTodos(string nombre, int page = 1, int pageSize = 50)
        {
            string[] nats;

            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
                nats = new[] { "VICOT", "VIPED", "VIREM" };
            else if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_especial"))
                nats = new[] { "VINCOT", "VIPED", "VIREM" };
            else
                return Json(new { success = false, message = "Sin permisos." });

            return GetDocumentosPendientes(nats, nombre, page, pageSize);
        }

        // ── Método privado compartido ───────────────────────────────────
        private JsonResult GetDocumentosPendientes(string[] nats, string nombre, int page, int pageSize)
        {
            var natParams = nats
                .Select((n, i) => new { Key = $"nat{i}", Value = n })
                .ToList();

            string natIn = string.Join(", ", natParams.Select(p => $"@{p.Key}"));

            var parameters = new Dictionary<string, object>
            {
                { "nombre",           nombre ?? "" },
                { "offset",           (page - 1) * pageSize },
                { "pageSize",         pageSize },
                { "suc",              Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id",       Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "estatus_pendiente", ESTATUS_PENDIENTE }
            };

            foreach (var p in natParams)
                parameters[p.Key] = p.Value;

            string where =
                " WHERE ( " +
                "    LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER('%' || @nombre || '%') " +
                "    OR LOWER(em.cli_prov) LIKE LOWER('%' || @nombre || '%') " +
                "    OR LOWER(cc.n_cli)    LIKE LOWER('%' || @nombre || '%') " +
                " ) " +
                $" AND em.nat IN ({natIn}) " +
                " AND em.suc = @suc " +
                " AND em.estatus_id = @estatus_pendiente ";

            string query =
                " SELECT " +
                "     em.id_encabezado, " +
                "     em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc " +
                "         || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "     em.gen, " +
                "     em.nat, " +
                "     TO_CHAR(em.fch, 'DD/MM/YYYY') AS fch, " +
                "     em.imp, " +
                "     em.cli_prov, " +
                "     u.nombreusuario AS usr0, " +
                "     em.incoterm, " +
                "     cc.n_cli, " +
                "     em.estatus_id, " +
                "     'PENDIENTE' AS estatus_label " +
                " FROM encabezadomov em " +
                " INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id " +
                " INNER JOIN usuarios u ON u.usuarioid = em.usr0 " +
                where +
                " ORDER BY em.fch DESC " +
                " OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY ";

            var data = RunQuery(query, parameters);

            string queryTotal =
                " SELECT COUNT(*) " +
                " FROM encabezadomov em " +
                " INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id " +
                " INNER JOIN usuarios u ON u.usuarioid = em.usr0 " +
                where;

            var total = RunScalar(queryTotal, parameters);

            return Json(new { data, total });
        }

        // ── BuscarDocumento — Encabezado + Partidas (igual que ConsultaDocumentos) ──
        public IActionResult BuscarDocumento(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id",         id },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string queryEncabezado = @"
                    SELECT
                        em.folio ||
                            CASE WHEN em.variacion > 0
                                 THEN '-' || num_to_letters(em.variacion)
                                 ELSE '' END AS folio,
                        em.id_encabezado,
                        em.encabezados_padre,
                        em.suc,
                        em.alm,
                        em.gen,
                        em.nat,
                        u.nombreusuario AS usr0,
                        em.fch0,
                        em.cli_prov,
                        em.coment1,
                        em.coment_aut,
                        em.ccy,
                        em.vdr_cpr,
                        em.flete,
                        em.incoterm,
                        em.mdp,
                        cfp.cve_sat AS f_pago,
                        em.cfdi,
                        cc.rfc,
                        em.par,
                        cc.lim_crd,
                        cc.n_cli,
                        cc.pl_crd,
                        cc.dir,
                        cc.id_cliente,
                        f.uuid,
                        df.forma_pago,
                        df.uso_sugerido,
                        df.regimen_fiscal,
                        df.calle,
                        df.no_exterior,
                        df.no_interior,
                        df.colonia,
                        df.localidad,
                        df.municipio,
                        df.estado,
                        em.orden_compra AS ordenCompra,
                        df.pais,
                        df.codigo_postal,
                        em.estatus_id,
                        'PENDIENTE' AS estatus_label,
                        cc.dir || CHR(10) ||
                        cc.col || CHR(10) ||
                        cc.pob || CHR(10) ||
                        cc.cp AS info_cli
                    FROM encabezadomov em
                    LEFT JOIN catclientes cc
                        ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
                    LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
                    LEFT JOIN factura f ON f.encabezado_id = em.id_encabezado
                    LEFT JOIN cat_f_pago cfp ON cfp.id_f_pago = em.f_pago
                    INNER JOIN usuarios u ON u.usuarioid = em.usr0
                    WHERE em.id_encabezado = @id
                      AND em.estatus_id IN (1,41);
                ";

                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento pendiente no encontrado." });

                var encabezado = encabezadoResult.First();

                int sucursal = Convert.ToInt32(encabezado["suc"]);
                parameters.Add("sucursal", sucursal);

                string queryPartidas = @"
                    SELECT
                        pd.id_partidas         AS id,
                        pd.cve_prod            AS producto_id,
                        pd.descr_prod          AS descripcion,
                        pd.cant_ud             AS cantidad,
                        pd.pv_prod             AS precio,
                        pd.dto1                AS descuento,
                        pd.ud                  AS unidad,
                        CASE
                            WHEN COALESCE(pd.imp_part, 0) = 0
                                THEN pd.cant_ud * pd.pv_prod
                            ELSE pd.imp_part
                        END AS importe,
                        CASE
                            WHEN COALESCE(pd.iva, 0) = 0 THEN
                                (CASE
                                    WHEN COALESCE(pd.imp_part, 0) = 0
                                        THEN pd.cant_ud * pd.pv_prod
                                    ELSE pd.imp_part
                                END) * 0.16
                            ELSE pd.iva
                        END AS iva,
                        pd.ieps,
                        pd.fch                 AS fecha,
                        pd.cve_alm             AS almacen,
                        pd.cto_vta_part        AS costo,
                        pd.ccy                 AS moneda,
                        pd.pedimento,
                        pd.tp_doc_ant          AS comentario,
                        COALESCE(stk.cantidadStock, 0) AS existencia
                    FROM partidasdoc pd
                    LEFT JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
                    LEFT JOIN (
                        SELECT
                            tp.producto_id,
                            cr.almacen_id,
                            cs.id_sucursal,
                            SUM(tp.cantidad) AS cantidadStock
                        FROM tarima_productos tp
                        INNER JOIN cattarimas ct    ON ct.id_tarima   = tp.tarima_id
                        INNER JOIN catniveles cn    ON cn.id_nivel    = ct.nivel_id
                        INNER JOIN catcolumnas cc2  ON cc2.id_columna = cn.columna_id
                        INNER JOIN catracks cr      ON cr.id_rack     = cc2.rack_id
                        INNER JOIN catalmacenes ca  ON ca.id_almacen  = cr.almacen_id
                        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                        WHERE ca.tipo = 'Stock'
                        GROUP BY tp.producto_id, cr.almacen_id, cs.id_sucursal
                    ) stk
                        ON stk.producto_id = pd.producto_id
                       AND stk.id_sucursal = @sucursal
                    WHERE pd.encabezado_id = @id
                    ORDER BY pd.nro_part;
                ";

                var partidasResult = RunQuery(queryPartidas, parameters);
                encabezado["productos"] = partidasResult;

                return Json(new { success = true, data = encabezado });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}