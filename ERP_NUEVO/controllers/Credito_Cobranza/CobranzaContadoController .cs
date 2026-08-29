using BOS_ERP.Controllers;
using BOS_ERP.Models.Carteras.Cliente;
using Microsoft.AspNetCore.Mvc;

[RightAuthorize(new[] { "cobranza_contado" })]
public class CobranzaContadoController : Utilities
{
    private string GetFiltroTipoFactura()
    {
        var tipos = new List<string>();

        if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_especial_internacional"))
            tipos.Add("'VIS'");
        if (Utilities.DoesUserHasRight(User.Identity.Name, "factura_arrendamiento"))
            tipos.Add("'FAR'");
        if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
            tipos.Add("'VI', 'VS'");
        if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_global"))
            tipos.Add("'G'");

        // cobranza_contado siempre ve VI y VS (PUE)
        if (!tipos.Any())
            tipos.Add("'VI', 'VS'");

        return $"AND fa.serie IN ({string.Join(",", tipos)})";
    }

    private string GetFiltroSucursal() =>
        "AND em.suc = @suc";

    // ============================================================
    //  2. ACCIÓN: listado de facturas PUE sin saldar
    // ============================================================

    [HttpPost]
    public IActionResult ObtenerFacturasPendientesPUE(string nombre = "", int page = 1, int pageSize = 50)
    {
        int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
        string filtroTipo = GetFiltroTipoFactura();
        string filtroSuc = GetFiltroSucursal();

        var parameters = new Dictionary<string, object>
        {
            { "nombre",   nombre ?? "" },
            { "offset",   (page - 1) * pageSize },
            { "pageSize", pageSize },
            { "suc",      suc }
        };

        // Solo facturas PUE (contado) con saldo pendiente en cartera
        string query = $@"
            SELECT
                fa.id,
                fa.serie,
                fa.folio                                        AS fac_folio,
                fa.uuid,
                fa.rfccliente,
                fa.rsocliente,
                fa.fecha,
                fa.fechatimbrado,
                fa.total,
                fa.subtotal,
                fa.iva,
                fa.descuento,
                fa.moneda,
                fa.statusfactura,
                fa.mdpfactura,
                fa.idvendedor,
                fa.encabezado_id,
                fa.tipo,
                em.usr_doc,
                COALESCE(cc.saldo_pendiente, 0)                 AS saldo,
                em.folio ||
                    CASE WHEN em.variacion > 0
                         THEN '-' || num_to_letters(em.variacion)
                         ELSE ''
                    END                                         AS folio
            FROM factura fa
            INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
            INNER JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id
            WHERE em.variacion = 0
              AND fa.statusfactura NOT IN ('Cancelada', 'Error al Cancelar')
              -- Solo PUE (contado)
              AND (
                  UPPER(fa.mdpfactura) = 'PUE'
                  OR UPPER(fa.mdpfactura) LIKE '%UNA SOLA%'
                  OR UPPER(fa.mdpfactura) LIKE '%CONTADO%'
              )
              -- Saldo pendiente real
              AND COALESCE(cc.saldo_pendiente, 0) > 0
              -- Búsqueda
              AND (
                  LOWER(fa.serie)      LIKE LOWER('%' || @nombre || '%')
               OR LOWER(fa.rfccliente) LIKE LOWER('%' || @nombre || '%')
               OR LOWER(fa.rsocliente) LIKE LOWER('%' || @nombre || '%')
               OR LOWER(em.folio::text) LIKE LOWER('%' || @nombre || '%')
              )
              {filtroSuc}
              {filtroTipo}
            ORDER BY fa.fecha ASC, cc.saldo_pendiente DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

        var data = RunQuery(query, parameters);

        // Conteo total
        string queryCount = $@"
            SELECT COUNT(*) FROM (
                SELECT DISTINCT fa.id
                FROM factura fa
                INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
                INNER JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id
                WHERE em.variacion = 0
                  AND fa.statusfactura NOT IN ('Cancelada', 'Error al Cancelar')
                  AND (
                      UPPER(fa.mdpfactura) = 'PUE'
                      OR UPPER(fa.mdpfactura) LIKE '%UNA SOLA%'
                      OR UPPER(fa.mdpfactura) LIKE '%CONTADO%'
                  )
                  AND COALESCE(cc.saldo_pendiente, 0) > 0
                  AND (
                      LOWER(fa.serie)      LIKE LOWER('%' || @nombre || '%')
                   OR LOWER(fa.rfccliente) LIKE LOWER('%' || @nombre || '%')
                   OR LOWER(fa.rsocliente) LIKE LOWER('%' || @nombre || '%')
                   OR LOWER(em.folio::text) LIKE LOWER('%' || @nombre || '%')
                  )
                  {filtroSuc}
                  {filtroTipo}
            ) sub";

        var total = RunScalar(queryCount, parameters);

        // Resumen financiero del lote visible
        string queryResumen = $@"
            SELECT
                COUNT(*)                              AS total_facturas,
                COALESCE(SUM(fa.total), 0)            AS suma_total,
                COALESCE(SUM(cc.saldo_pendiente), 0)  AS suma_saldo
            FROM factura fa
            INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
            INNER JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id
            WHERE em.variacion = 0
              AND fa.statusfactura NOT IN ('Cancelada', 'Error al Cancelar')
              AND (
                  UPPER(fa.mdpfactura) = 'PUE'
                  OR UPPER(fa.mdpfactura) LIKE '%UNA SOLA%'
                  OR UPPER(fa.mdpfactura) LIKE '%CONTADO%'
              )
              AND COALESCE(cc.saldo_pendiente, 0) > 0
              {filtroSuc}
              {filtroTipo}";

        var resumen = RunQuery(queryResumen, new Dictionary<string, object> { { "suc", suc } });

        return Json(new { data, total, resumen });
    }

    // ============================================================
    //  3. ACCIÓN: registrar pago de factura PUE
    //     ⚠️  Lógica de pago intencionalmente vacía –
    //         implementar según reglas de negocio del ERP.
    // ============================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult RegistrarPagoPUE(
        string uuid,
        decimal montoPagado,
        string formaPago,
        string referencia = "",
        string observaciones = "")
    {
        try
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            string filtroTipo = GetFiltroTipoFactura();

            // ── 1. Validar ownership: la factura existe, es PUE, tiene saldo y pertenece a la sucursal ──
            string queryVerifica = $@"
                SELECT
                    fa.id           AS factura_id,
                    fa.total,
                    fa.rfccliente,
                    fa.rsocliente,
                    fa.encabezado_id,
                    COALESCE(cc.saldo_pendiente, 0) AS saldo_actual
                FROM factura fa
                INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
                INNER JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id
                WHERE fa.uuid = @uuid
                  AND em.suc  = @suc
                  AND fa.statusfactura NOT IN ('Cancelada', 'Error al Cancelar')
                  AND (
                      UPPER(fa.mdpfactura) = 'PUE'
                      OR UPPER(fa.mdpfactura) LIKE '%UNA SOLA%'
                      OR UPPER(fa.mdpfactura) LIKE '%CONTADO%'
                  )
                  AND COALESCE(cc.saldo_pendiente, 0) > 0
                  {filtroTipo}
                LIMIT 1";

            var factura = RunQuery(queryVerifica, new Dictionary<string, object>
            {
                { "uuid", Guid.Parse(uuid) },
                { "suc",  suc }
            });

            if (factura.Count == 0)
                return Json(new { success = false, message = "Factura no encontrada, sin saldo pendiente o sin permisos." });

            decimal saldoActual = Convert.ToDecimal(factura[0]["saldo_actual"]);
            int facturaId = Convert.ToInt32(factura[0]["factura_id"]);
            int encabezadoId = Convert.ToInt32(factura[0]["encabezado_id"]);

            var parameters = new Dictionary<string, object>();
            string query = "SELECT id_poliza FROM polizas WHERE referencia = @Encabezado_id";
            parameters.Add("Encabezado_id", Convert.ToInt32(factura[0]["encabezado_id"]));
            int idpoliza = Convert.ToInt32(RunScalar(query, parameters));

            query = "SELECT id_cartera_cliente FROM cartera_clientes WHERE encabezado_id = @Encabezado_id ";
            int idcartera = Convert.ToInt32(RunScalar(query, parameters));

            CrearCobroCliente(idpoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Normal);

            // ── 3. Auditoría ──────────────────────────────────────────────

            RegistrarAuditoria(
                "PAGO_CONTADO", uuid, User.Identity.Name, suc,
                $"Monto: {montoPagado:F2} | Forma: {formaPago} | Ref: {referencia}"
            );

            return Json(new
            {
                success = true,
                message = "Pago registrado correctamente.",
                uuid,
                montoPagado,
                saldoAnterior = saldoActual,
                saldoNuevo = saldoActual - montoPagado
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error al registrar el pago: " + ex.Message });
        }
    }

    // ── Auditoría (igual que en FacturaConsultaController) ────────────
    private void RegistrarAuditoria(string accion, string referencia, string usuario, int sucursal, string detalle = "")
    {
        try
        {
            RunUpdate(
                @"INSERT INTO auditoria_facturas (accion, referencia, usuario, sucursal, detalle, fecha)
                  VALUES (@accion, @referencia, @usuario, @sucursal, @detalle, NOW())",
                new Dictionary<string, object>
                {
                    { "accion",     accion     },
                    { "referencia", referencia },
                    { "usuario",    usuario    },
                    { "sucursal",   sucursal   },
                    { "detalle",    detalle ?? "" }
                });
        }
        catch { /* No interrumpir flujo principal */ }
    }
}