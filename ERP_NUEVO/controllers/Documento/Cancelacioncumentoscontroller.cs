using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Documento
{
    public class CancelacionDocumentosController : Utilities
    {
        private const int ESTATUS_CANCELADO = 27;

        private JsonResult BuscarActivosPorNat(string nat)
        {
            var parameters = new Dictionary<string, object>
            {
                { "nat",            nat },
                { "suc",            Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id",     Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "estatus_cancel", ESTATUS_CANCELADO }
            };

            string query = @"
                SELECT
                    em.id_encabezado,
                    em.gen || '-' || em.nat || '-' ||
                        EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc
                        || CASE WHEN em.variacion > 0
                                THEN '-' || num_to_letters(em.variacion)
                                ELSE '' END  AS folio,
                    em.nat,
                    TO_CHAR(em.fch, 'DD/MM/YYYY') AS fch,
                    em.imp,
                    em.cli_prov,
                    em.incoterm,
                    u.nombreusuario AS usr0,
                    cc.n_cli,
                    em.estatus_id
                FROM encabezadomov em
                INNER JOIN catclientes cc
                    ON cc.id_cliente = em.refe
                   AND cc.empresa_id = @empresa_id
                INNER JOIN usuarios u ON u.usuarioid = em.usr0
                WHERE em.nat        = @nat
                  AND em.estatus_id = 1
                  AND em.suc        = @suc
                  AND em.estatus_id != @estatus_cancel
                ORDER BY em.fch DESC";

            var data = RunQuery(query, parameters);
            return Json(new { data, total = data.Count });
        }

        // ── Endpoints que llama la vista ────────────────────────────────

        public JsonResult BuscarActivosCot() => BuscarActivosPorNat("VICOT");
        public JsonResult BuscarActivosPed() => BuscarActivosPorNat("VIPED");
        public JsonResult BuscarActivosRem() => BuscarActivosPorNat("VIREM");

        // ── BuscarDocumento — Encabezado + Partidas (solo activos) ──────
        public IActionResult BuscarDocumento(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id",             id },
                    { "empresa_id",     Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                    { "estatus_cancel", ESTATUS_CANCELADO }
                };

                string queryValidar =
                    " SELECT COUNT(*) FROM encabezadomov " +
                    " WHERE id_encabezado = @id AND estatus_id != @estatus_cancel ";

                if (Convert.ToInt32(RunScalar(queryValidar, parameters)) == 0)
                    return Json(
                        new { success = false, message = "El documento ya está cancelado o no existe." });

                string queryEncabezado = @"
                    SELECT
                        em.gen || '-' || em.nat || '-' ||
                            EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc
                            || CASE WHEN em.variacion > 0
                                    THEN '-' || num_to_letters(em.variacion)
                                    ELSE '' END  AS folio,
                        em.id_encabezado,
                        em.suc,
                        em.gen,
                        em.nat,
                        u.nombreusuario  AS usr0,
                        em.fch0,
                        em.cli_prov,
                        em.coment1,
                        em.ccy,
                        em.incoterm,
                        em.orden_compra  AS ordenCompra,
                        em.imp,
                        em.estatus_id,
                        cc.rfc,
                        cc.n_cli,
                        cc.dir,
                        cc.id_cliente
                    FROM encabezadomov em
                    LEFT JOIN catclientes cc
                        ON cc.cve_cli = em.cli_prov
                       AND cc.empresa_id = @empresa_id
                    INNER JOIN usuarios u ON u.usuarioid = em.usr0
                    WHERE em.id_encabezado = @id
                      AND em.estatus_id   != @estatus_cancel";

                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(
                        new { success = false, message = "Documento no encontrado o ya cancelado." });

                var encabezado = encabezadoResult.First();

                string queryPartidas = @"
                    SELECT
                        pd.id_partidas  AS id,
                        pd.cve_prod     AS producto_id,
                        pd.descr_prod   AS descripcion,
                        pd.cant_ud      AS cantidad,
                        pd.pv_prod      AS precio,
                        pd.dto1         AS descuento,
                        pd.ud           AS unidad,
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
                        pd.ieps
                    FROM partidasdoc pd
                    WHERE pd.encabezado_id = @id
                    ORDER BY pd.nro_part";

                encabezado["productos"] = RunQuery(queryPartidas, parameters);

                return Json(new { success = true, data = encabezado });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── EjecutarCancelacion — Cotizaciones y Pedidos ────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EjecutarCancelacion(int id, string motivo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo))
                    return Json(new { success = false, message = "El motivo de cancelación es requerido." });

                var parameters = new Dictionary<string, object>
                {
                    { "id",             id },
                    { "estatus_cancel", ESTATUS_CANCELADO },
                    { "motivo",         motivo.Trim() },
                    { "empresa_id",     Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string queryVerificar =
                    " SELECT COUNT(*) FROM encabezadomov " +
                    " WHERE id_encabezado = @id AND estatus_id != @estatus_cancel ";

                if (Convert.ToInt32(RunScalar(queryVerificar, parameters)) == 0)
                    return Json(new { success = false, message = "El documento ya fue cancelado o no existe." });

                string queryUpdate =
                    " UPDATE encabezadomov SET " +
                    "     estatus_id = @estatus_cancel, " +
                    "     coment_aut = @motivo " +
                    " WHERE id_encabezado = @id " +
                    "   AND estatus_id   != @estatus_cancel ";

                RunQuery(queryUpdate, parameters);

                return Json(new { success = true, message = "Documento cancelado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── EjecutarCancelacionRemision — con campos extra ──────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EjecutarCancelacionRemision(
            int id,
            string motivo,
            string tipoDevolucion,
            string docRelacionado,
            string refAlmacen)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo))
                    return Json(new { success = false, message = "El motivo de cancelación es requerido." });

                var parameters = new Dictionary<string, object>
                {
                    { "id",             id },
                    { "estatus_cancel", ESTATUS_CANCELADO },
                    { "motivo",         motivo.Trim() },
                    { "empresa_id",     Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string queryVerificar =
                    " SELECT COUNT(*) FROM encabezadomov " +
                    " WHERE id_encabezado = @id AND estatus_id != @estatus_cancel ";

                if (Convert.ToInt32(RunScalar(queryVerificar, parameters)) == 0)
                    return Json(new { success = false, message = "El documento ya fue cancelado o no existe." });

                string queryUpdate =
                    " UPDATE encabezadomov SET " +
                    "     estatus_id = @estatus_cancel, " +
                    "     coment_aut = @motivo " +
                    " WHERE id_encabezado = @id " +
                    "   AND estatus_id   != @estatus_cancel ";

                RunQuery(queryUpdate, parameters);
                RevertirMovimientoInventario(id, GetUserId(User.Identity.Name));
                return Json(new { success = true, message = "Documento cancelado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}