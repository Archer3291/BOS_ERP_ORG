using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Documento
{
    //[RightAuthorize(new[] { "ventas", "ventas_global" })]
    public class ConsultaDocumentosController : Utilities
    {
        private const int ESTATUS_CANCELADO = 27;

        public IActionResult Index()
        {
            return View();
        }

        // ── VICOT — Cotizaciones ────────────────────────────────────────
        // estatus: "activos" | "cancelados" | "todos"
        public JsonResult GetVICOT(string nombre, string estatus = "activos", int page = 1, int pageSize = 50)
        {
            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
                return GetDocumentos(new[] { "VICOT" }, nombre, estatus, page, pageSize);

            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_especial"))
                return GetDocumentos(new[] { "VINCOT" }, nombre, estatus, page, pageSize);

            return Json(new
            {
                success = false,
                message = "No tienes permisos para consultar este documento."
            });
        }

        // ── VIPED — Pedidos ─────────────────────────────────────────────
        public JsonResult GetVIPED(string nombre, string estatus = "activos", int page = 1, int pageSize = 50)
        {
            return GetDocumentos(new[] { "VIPED" }, nombre, estatus, page, pageSize);
        }

        // ── VIREM — Remisiones ──────────────────────────────────────────
        public JsonResult GetVIREM(string nombre, string estatus = "activos", int page = 1, int pageSize = 50)
        {
            return GetDocumentos(new[] { "VIREM" }, nombre, estatus, page, pageSize);
        }

        // ── CANCELADOS — Todos los tipos cancelados ─────────────────────
        public JsonResult GetCancelados(string nombre, int page = 1, int pageSize = 50)
        {
            string[] nats;

            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
                nats = new[] { "VICOT", "VIPED", "VIREM" };
            else if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_especial"))
                nats = new[] { "VINCOT", "VIPED", "VIREM" };
            else
                return Json(new { success = false, message = "Sin permisos." });

            return GetDocumentos(nats, nombre, "cancelados", page, pageSize);
        }

        // ── Método privado compartido ───────────────────────────────────
        private JsonResult GetDocumentos(string[] nats, string nombre, string estatus, int page, int pageSize)
        {
            var natParams = nats
                .Select((n, i) => new { Key = $"nat{i}", Value = n })
                .ToList();

            string natIn = string.Join(", ", natParams.Select(p => $"@{p.Key}"));

            var parameters = new Dictionary<string, object>
            {
                { "nombre",          nombre ?? "" },
                { "offset",          (page - 1) * pageSize },
                { "pageSize",        pageSize },
                { "suc",             Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id",      Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "estatus_cancel",  ESTATUS_CANCELADO }
            };

            foreach (var p in natParams)
                parameters[p.Key] = p.Value;

            // Cláusula de estatus dinámica
            string estatusWhere;

            switch (estatus)
            {
                case "cancelados":
                    estatusWhere = " AND em.estatus_id = @estatus_cancel ";
                    break;

                case "activos":
                    estatusWhere = " AND em.estatus_id != @estatus_cancel ";
                    break;

                default:
                    // "todos" — sin filtro de estatus
                    estatusWhere = "";
                    break;
            }

            string where =
                " WHERE ( " +
                "    LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER('%' || @nombre || '%') " +
                "    OR LOWER(em.cli_prov) LIKE LOWER('%' || @nombre || '%') " +
                "    OR LOWER(cc.n_cli)    LIKE LOWER('%' || @nombre || '%') " +
                " ) " +
                $" AND em.nat IN ({natIn}) " +
                " AND em.suc = @suc " +
                estatusWhere;

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
                "     CASE WHEN em.estatus_id = @estatus_cancel THEN 'CANCELADO' ELSE 'ACTIVO' END AS estatus_label " +
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

        // ── BuscarDocumento — Encabezado + Partidas ─────────────────────
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
                        CASE WHEN em.estatus_id = 27 THEN 'CANCELADO' ELSE 'ACTIVO' END AS estatus_label,
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
                    WHERE em.id_encabezado = @id;
                ";

                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

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