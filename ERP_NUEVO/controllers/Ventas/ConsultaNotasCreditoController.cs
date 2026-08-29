using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas
{
    [Authorize]
    public class ConsultaNotasCreditoController : Utilities
    {

        // ══════════════════════════════════════════════════════════════════════
        //  LISTADO PAGINADO CON FILTROS
        //  POST /ConsultaNotasCredito/BuscarNC
        //  Agrega: modo_aplicacion, monto_saldo_favor, tiene_split
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost]
        public IActionResult BuscarNC(
            string cliente = "",
            string tipo = "",
            string estatus = "",
            string fechaDesde = "",
            string fechaHasta = "",
            string folio = "",
            string modoAplicacion = "",   // NUEVO: "deuda_reducida"|"saldo_favor"|"reembolso"|"split"
            int page = 1,
            int pageSize = 50)
        {
            var p = new Dictionary<string, object>
            {
                ["suc"] = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                ["offset"] = (page - 1) * pageSize,
                ["pageSize"] = pageSize,
                ["cliente"] = $"%{cliente}%",
                ["folio"] = $"%{folio}%",
            };

            string filtroTipo = string.IsNullOrWhiteSpace(tipo) ? "" : "AND f.tipo ILIKE @tipo";
            string filtroEst = string.IsNullOrWhiteSpace(estatus) ? "" : "AND f.statusfactura = @estatus";
            string filtroFechaD = string.IsNullOrWhiteSpace(fechaDesde) ? "" : "AND f.fecha >= @fecha_desde::date";
            string filtroFechaH = string.IsNullOrWhiteSpace(fechaHasta) ? "" : "AND f.fecha <= (@fecha_hasta::date + interval '1 day')";
            // NUEVO: filtro por modo de aplicación
            string filtroModo = string.IsNullOrWhiteSpace(modoAplicacion) ? ""
                : "AND sf.modo_aplicacion = @modo_aplicacion";

            if (!string.IsNullOrWhiteSpace(tipo)) p["tipo"] = $"%{tipo}%";
            if (!string.IsNullOrWhiteSpace(estatus)) p["estatus"] = estatus;
            if (!string.IsNullOrWhiteSpace(fechaDesde)) p["fecha_desde"] = fechaDesde;
            if (!string.IsNullOrWhiteSpace(fechaHasta)) p["fecha_hasta"] = fechaHasta;
            if (!string.IsNullOrWhiteSpace(modoAplicacion)) p["modo_aplicacion"] = modoAplicacion;

            string sql = $@"
SELECT
    f.id,
    f.serie,
    f.folio,
    f.fecha,
    f.fechatimbrado,
    f.statusfactura,
    f.tipo,
    f.rfccliente,
    f.rsocliente,
    f.subtotal,
    f.descuento,
    f.iva,
    f.total,
    f.saldo,
    f.moneda,
    f.uuid::text                AS uuid,
    f.observaciones,
    f.oc                        AS ticket,
    f.encabezado_id,

    -- Facturas origen relacionadas (pipe-separado para la celda)
    COALESCE(
        (SELECT string_agg(fo.folio || ' (' || fr.tipo_relacion || ')', ', ')
           FROM factura_relacion fr
           JOIN factura fo ON fo.id = fr.factura_origen_id
          WHERE fr.factura_relacionada_id = f.id
        ), '—'
    )                           AS facturas_origen,

    -- Saldo ya aplicado en CxC
    COALESCE(f.total - f.saldo, 0) AS aplicado_cxc,

    -- ── NUEVOS CAMPOS ──────────────────────────────────────────────
    -- Modo de aplicación registrado en nc_saldo_favor
    COALESCE(sf.origen, 'deuda_reducida')  AS modo_aplicacion,
    -- Monto de saldo a favor generado (0 si la NC fue directo a deuda)
    COALESCE(sf.monto_original, 0)         AS monto_saldo_favor,
    -- Saldo a favor aún disponible
    COALESCE(sf.monto_disponible, 0)       AS saldo_favor_disponible,
    -- Estado del saldo a favor
    COALESCE(sf.estado, '—')               AS saldo_favor_estado,
    -- ¿Hubo split PPD?
    EXISTS(
        SELECT 1 FROM nc_split_ppd sp WHERE sp.nc_factura_id = f.id
    )                           AS tiene_split,
    -- ¿Tiene anticipos afectados?
    EXISTS(
        SELECT 1 FROM nc_aplicacion_anticipo nca WHERE nca.factura_nc_id = f.id
    )                           AS tiene_anticipo

FROM factura f

-- JOIN opcional con nc_saldo_favor (LEFT porque no toda NC genera saldo a favor)
LEFT JOIN nc_saldo_favor sf ON sf.nc_factura_id = f.id

WHERE (  f.tipo ILIKE '%NC%'
      OR f.tipo ILIKE '%DEVOLUCION%'
      OR f.tipo ILIKE '%DESCUENTO%'
      OR f.tipo ILIKE '%CANCELACION%'
      OR f.tipo ILIKE '%BONIFICACION%'
      OR f.tipo ILIKE '%PRECIO%'  )
  AND f.encabezado_id IN (
          SELECT em.id_encabezado FROM encabezadomov em WHERE em.suc = @suc
      )
  AND (f.rsocliente ILIKE @cliente OR f.rfccliente ILIKE @cliente)
  AND f.folio ILIKE @folio
  {filtroTipo}
  {filtroEst}
  {filtroFechaD}
  {filtroFechaH}
  {filtroModo}

ORDER BY f.fecha DESC, f.id DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";

            var data = RunQuery(sql, p);

            // Conteo para paginación
            string sqlCount = $@"
SELECT COUNT(*) AS total
FROM factura f
LEFT JOIN nc_saldo_favor sf ON sf.nc_factura_id = f.id
WHERE (  f.tipo ILIKE '%NC%'
      OR f.tipo ILIKE '%DEVOLUCION%'
      OR f.tipo ILIKE '%DESCUENTO%'
      OR f.tipo ILIKE '%CANCELACION%'
      OR f.tipo ILIKE '%BONIFICACION%'
      OR f.tipo ILIKE '%PRECIO%'  )
  AND f.encabezado_id IN (
          SELECT em.id_encabezado FROM encabezadomov em WHERE em.suc = @suc
      )
  AND (f.rsocliente ILIKE @cliente OR f.rfccliente ILIKE @cliente)
  AND f.folio ILIKE @folio
  {filtroTipo}
  {filtroEst}
  {filtroFechaD}
  {filtroFechaH}
  {filtroModo};";

            var totalRes = RunQuery(sqlCount, p);
            int total = totalRes?.Count > 0 ? Convert.ToInt32(totalRes[0]["total"]) : 0;

            return Json(new { data, total, page, pageSize });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  DETALLE COMPLETO DE UNA NC
        //  GET /ConsultaNotasCredito/DetalleNC?id=123
        //  Agrega: saldo_favor, split_ppd, estado_origen
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult DetalleNC(int id)
        {
            // ── Encabezado ────────────────────────────────────────────────────
            var enc = RunQuery(@"
SELECT
    f.id, f.serie, f.folio, f.fecha, f.fechatimbrado,
    f.statusfactura, f.tipo,
    f.rfccliente, f.rsocliente, f.cpr, f.reg_fise,
    f.rfcemisor, f.rsoemisor, f.reg_fisr,
    f.subtotal, f.descuento, f.iva, f.total, f.saldo, f.moneda,
    f.uuid::text     AS uuid,
    f.observaciones, f.oc AS ticket,
    f.sellosat, f.sellocfdi, f.cadenaoriginal,
    f.mdpfactura, f.usocfdi, f.idusocfdi,
    f.encabezado_id,
    COALESCE(f.total - f.saldo, 0) AS aplicado_cxc
FROM factura f
WHERE f.id = @id;",
                new Dictionary<string, object> { ["id"] = id });

            if (enc == null || enc.Count == 0)
                return Json(new { success = false, message = "NC no encontrada" });

            // ── Partidas ──────────────────────────────────────────────────────
            var partidas = RunQuery(@"
SELECT
    d.claveprod, d.descripcion,
    d.cantidad, d.precio, d.descuento,
    d.saldo AS importe_total, d.udm,
    d.claveprodserv, d.lote, d.pedimento,
    d.cant_ndc, d.comentario
FROM dfactura d
WHERE d.idfac = @id
ORDER BY d.idfactura;",
                new Dictionary<string, object> { ["id"] = id });

            // ── Facturas origen relacionadas ──────────────────────────────────
            var relacionadas = RunQuery(@"
SELECT
    fo.id, fo.serie, fo.folio, fo.fecha,
    fo.total, fo.saldo, fo.statusfactura,
    fo.uuid::text AS uuid,
    fr.tipo_relacion
FROM factura_relacion fr
JOIN factura fo ON fo.id = fr.factura_origen_id
WHERE fr.factura_relacionada_id = @id;",
                new Dictionary<string, object> { ["id"] = id });

            // ── Anticipos afectados ───────────────────────────────────────────
            var anticipos = RunQuery(@"
SELECT
    nca.monto_acreditado,
    nca.saldo_anticipo_antes, nca.saldo_anticipo_despues,
    nca.tipo_aplicacion, nca.fecha_aplicacion,
    fa.folio AS anticipo_folio,
    fa.total AS anticipo_total,
    fa.saldo AS anticipo_saldo_actual
FROM nc_aplicacion_anticipo nca
JOIN factura fa ON fa.id = nca.factura_ant_id
WHERE nca.factura_nc_id = @id
ORDER BY nca.fecha_aplicacion;",
                new Dictionary<string, object> { ["id"] = id });

            // ── NUEVO: Saldo a favor ──────────────────────────────────────────
            var saldoFavor = RunQuery(@"
SELECT
    sf.id,
    sf.origen,
    sf.monto_original,
    sf.monto_disponible,
    COALESCE(sf.monto_original - sf.monto_disponible, 0) AS ya_consumido,
    sf.estado,
    sf.moneda,
    sf.creado_en,
    sf.vence_en,
    sf.notas,
    -- Historial de aplicaciones de este saldo
    COALESCE(
        (SELECT json_agg(
            json_build_object(
                'tipo',             nas.tipo_aplicacion,
                'monto',            nas.monto_aplicado,
                'saldo_antes',      nas.saldo_antes,
                'saldo_despues',    nas.saldo_despues,
                'referencia',       nas.referencia,
                'fecha',            nas.fecha_aplicacion,
                'factura_destino',  fd.folio
            ) ORDER BY nas.fecha_aplicacion
         )
         FROM nc_aplicacion_saldo nas
         LEFT JOIN factura fd ON fd.id = nas.factura_destino_id
         WHERE nas.saldo_favor_id = sf.id
        ), '[]'::json
    )                   AS historial_aplicaciones
FROM nc_saldo_favor sf
WHERE sf.nc_factura_id = @id
ORDER BY sf.id;",
                new Dictionary<string, object> { ["id"] = id });

            // ── NUEVO: Split PPD ──────────────────────────────────────────────
            var splitPPD = RunQuery(@"
SELECT
    sp.saldo_pendiente,
    sp.nc_total,
    sp.monto_a_deuda,
    sp.monto_a_cartera,
    sp.confirmado_en,
    fo.folio  AS origen_folio,
    fo.total  AS origen_total
FROM nc_split_ppd sp
JOIN factura fo ON fo.id = sp.origen_factura_id
WHERE sp.nc_factura_id = @id;",
                new Dictionary<string, object> { ["id"] = id });

            // ── NUEVO: Snapshot estado factura origen ─────────────────────────
            var estadoOrigen = RunQuery(@"
SELECT
    ne.metodo_pago,
    ne.total_factura,
    ne.saldo_antes_nc,
    ne.pagos_acumulados,
    ne.nc_importe,
    ne.saldo_despues_nc,
    ne.creado_en,
    fo.folio  AS origen_folio
FROM nc_estado_factura_origen ne
JOIN factura fo ON fo.id = ne.origen_factura_id
WHERE ne.nc_factura_id = @id
ORDER BY ne.id;",
                new Dictionary<string, object> { ["id"] = id });

            // ── URLs de archivos ──────────────────────────────────────────────
            string uuidNC = enc[0]["uuid"]?.ToString() ?? "";
            string pdfUrl = string.IsNullOrWhiteSpace(uuidNC) ? null
                           : Url.Content($"~/Facturacion/facturas/{uuidNC}.pdf");
            string xmlUrl = string.IsNullOrWhiteSpace(uuidNC) ? null
                           : Url.Content($"~/Facturacion/xml_timbrados/{uuidNC}.xml");
            string qrUrl = string.IsNullOrWhiteSpace(uuidNC) ? null
                           : Url.Content($"~/content/qrcodes/qr_{uuidNC}.png");

            return Json(new
            {
                success = true,
                encabezado = enc[0],
                partidas,
                relacionadas,
                anticipos,
                saldo_favor = saldoFavor,   // NUEVO
                split_ppd = splitPPD,      // NUEVO
                estado_origen = estadoOrigen, // NUEVO
                pdfUrl,
                xmlUrl,
                qrUrl
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  KPI — ahora incluye saldo a favor real
        //  GET /ConsultaNotasCredito/ResumenKPI
        // ══════════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult ResumenKPI(string fechaDesde = "", string fechaHasta = "")
        {
            var p = new Dictionary<string, object>
            {
                ["suc"] = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                ["fecha_desde"] = string.IsNullOrWhiteSpace(fechaDesde)
                                ? DateTime.Now.AddMonths(-1).ToString("yyyy-MM-dd")
                                : fechaDesde,
                ["fecha_hasta"] = string.IsNullOrWhiteSpace(fechaHasta)
                                ? DateTime.Now.ToString("yyyy-MM-dd")
                                : fechaHasta
            };

            var res = RunQuery(@"
SELECT
    COUNT(DISTINCT f.id)                                              AS total_ncs,
    COALESCE(SUM(f.total), 0)                                         AS monto_total,
    COALESCE(SUM(CASE WHEN f.tipo ILIKE '%DEVOLUCION%'
                      THEN f.total ELSE 0 END), 0)                    AS monto_devolucion,
    COALESCE(SUM(CASE WHEN f.tipo ILIKE '%DESCUENTO%'
                      THEN f.total ELSE 0 END), 0)                    AS monto_descuento,
    COALESCE(SUM(CASE WHEN f.statusfactura = 'TIMBRADA'
                      THEN 1 ELSE 0 END), 0)                          AS ncs_timbradas,
    COALESCE(SUM(f.saldo), 0)                                         AS saldo_cxc_pendiente,
    -- NUEVOS KPIs
    COALESCE(SUM(sf.monto_disponible),0)                              AS saldo_favor_disponible,
    COUNT(DISTINCT CASE WHEN sf.estado = 'disponible'
                        THEN sf.id END)                               AS clientes_con_saldo,
    COALESCE(SUM(CASE WHEN sf.estado = 'reembolsado'
                      THEN sf.monto_original ELSE 0 END), 0)          AS total_reembolsado,
    COUNT(DISTINCT CASE WHEN sp.id IS NOT NULL
                        THEN f.id END)                                AS ncs_con_split
FROM factura f
LEFT JOIN nc_saldo_favor sf ON sf.nc_factura_id = f.id
LEFT JOIN nc_split_ppd   sp ON sp.nc_factura_id = f.id
WHERE (  f.tipo ILIKE '%NC%'
      OR f.tipo ILIKE '%DEVOLUCION%'
      OR f.tipo ILIKE '%DESCUENTO%'
      OR f.tipo ILIKE '%CANCELACION%'
      OR f.tipo ILIKE '%BONIFICACION%' )
  AND f.encabezado_id IN (
          SELECT em.id_encabezado FROM encabezadomov em
          WHERE em.suc = @suc )
  AND f.fecha >= @fecha_desde::date
  AND f.fecha <= (@fecha_hasta::date + interval '1 day');", p);

            return Json(res?.Count > 0 ? res[0] : new Dictionary<string, object>());
        }

        // ══════════════════════════════════════════════════════════════════════
        //  REENVIAR EMAIL (sin cambios funcionales)
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost]
        public async Task<IActionResult> ReenviarEmail(int facturaId, string emailDestino)
        {
            try
            {
                var row = RunQuery(@"
SELECT f.uuid::text AS uuid, f.folio, f.rsocliente,
       f.emlcliente, f.total, f.moneda, f.fecha
FROM factura f WHERE f.id = @id;",
                    new Dictionary<string, object> { ["id"] = facturaId });

                if (row == null || row.Count == 0)
                    return Json(new { success = false, message = "NC no encontrada" });

                var r = row[0];
                string uuid = r["uuid"]?.ToString() ?? "";
                string email = string.IsNullOrWhiteSpace(emailDestino)
                             ? r["emlcliente"]?.ToString() ?? ""
                             : emailDestino;

                if (string.IsNullOrWhiteSpace(email))
                    return Json(new { success = false, message = "No hay correo destino configurado" });

                string pdfPath = Path.Combine($"~/Facturacion/facturas/{uuid}.pdf");
                string xmlPath = Path.Combine($"~/Facturacion/xml_timbrados/{uuid}.xml");

                if (!System.IO.File.Exists(pdfPath))
                    return Json(new { success = false, message = "Archivo PDF no disponible en servidor" });

                using (var msg = new MailMessage())
                {
                    msg.To.Add(email);
                    msg.Subject = $"Nota de Crédito {r["folio"]} — {r["rsocliente"]}";
                    msg.Body = $"Se adjunta la Nota de Crédito {r["folio"]} "
                                + $"por {r["moneda"]} {r["total"]:N2} "
                                + $"emitida el {r["fecha"]:dd/MM/yyyy}.";
                    msg.Attachments.Add(new Attachment(pdfPath));
                    if (System.IO.File.Exists(xmlPath))
                        msg.Attachments.Add(new Attachment(xmlPath));

                    using (var smtp = new SmtpClient(
                        System.Configuration.ConfigurationManager.AppSettings["SmtpHost"],
                        Convert.ToInt32(
                            System.Configuration.ConfigurationManager.AppSettings["SmtpPort"] ?? "587")))
                    {
                        smtp.EnableSsl = true;
                        smtp.Credentials = new NetworkCredential(
                            System.Configuration.ConfigurationManager.AppSettings["SmtpUser"],
                            System.Configuration.ConfigurationManager.AppSettings["SmtpPass"]);
                        await smtp.SendMailAsync(msg);
                    }
                }

                return Json(new { success = true, message = $"Correo enviado a {email}" });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "ConsultaNotasCredito/ReenviarEmail");
                return Json(new { success = false, message = "Error al enviar: " + ex.Message });
            }
        }
    }
}
