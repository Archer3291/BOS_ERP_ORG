using BOS_ERP.Filters;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Ventas
{
    /// <summary>
    /// Respaldo contable de las ventas que el cliente facturó por su cuenta en el portal
    /// de autofacturación.
    ///
    /// Ese portal timbra el CFDI y marca la venta (estatus 2 + uuid en `cfdi`), pero no
    /// levanta la cadena documental —pedido, remisión, factura— ni la póliza, porque corre
    /// sin sesión y no tiene el contexto de caja. Esto se hace aquí, en el cierre, sobre
    /// todas las que quedaron sueltas: NO se vuelve a timbrar nada, el CFDI ya existe.
    /// </summary>
    public partial class PuntoDeVentaController
    {
        // Venta autofacturada por el cliente: el portal deja el uuid del CFDI en `cfdi`
        // y la pasa a este estatus.
        private const int EstatusVentaAutofacturada = 2;

        /// <summary>
        /// Ventas ya autofacturadas a las que todavía les falta su respaldo contable.
        /// Se reconocen porque no tienen encabezado_hijo: nadie les creó la factura local.
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerAutofacturadasPendientes()
        {
            try
            {
                if (!TryGetContextoSesion(out int sucursalSesion, out int empresaSesion, out string errorSesion))
                    return Json(new { success = false, sessionExpired = true, message = errorSesion });

                var filas = RunQuery(@"
                    SELECT em.id_encabezado, em.folio, em.fch, em.imp, em.cli_prov, em.cfdi,
                           cc.n_cli,
                           f.serie, f.folio AS folio_factura, f.rfccliente, f.rsocliente,
                           f.total AS total_cfdi, f.idusocfdi, f.fecha AS fecha_timbrado
                    FROM encabezadomov em
                    LEFT JOIN catclientes cc
                           ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
                    LEFT JOIN factura f ON f.uuid::text = em.cfdi
                    WHERE em.nat = 'VSUC'
                      AND em.suc = @sucursal_id
                      AND em.estatus_id = @estatus_autofacturada
                      AND em.encabezado_hijo IS NULL
                    ORDER BY em.fch;",
                    new Dictionary<string, object>
                    {
                        { "empresa_id",            empresaSesion },
                        { "sucursal_id",           sucursalSesion },
                        { "estatus_autofacturada", EstatusVentaAutofacturada }
                    });

                var ventas = filas.Select(f => new
                {
                    ventaId = GetInt(f["id_encabezado"]) ?? 0,
                    folio = f["folio"]?.ToString(),
                    fecha = Convert.ToDateTime(f["fch"]).ToString("dd/MM/yyyy HH:mm"),
                    cliente = f["n_cli"]?.ToString() ?? f["cli_prov"]?.ToString(),
                    total = DecimalDe(f["imp"]),
                    uuid = f["cfdi"]?.ToString(),
                    // Cuando no hay fila en `factura` el CFDI se timbró fuera de este
                    // sistema (o quedó de una prueba): se avisa en pantalla.
                    tieneCfdiRegistrado = f["serie"] != null,
                    rfcCliente = f["rfccliente"]?.ToString(),
                    razonSocial = f["rsocliente"]?.ToString(),
                    usoCfdi = f["idusocfdi"]?.ToString()
                }).ToList();

                return Json(new
                {
                    success = true,
                    ventas,
                    resumen = new
                    {
                        total = ventas.Count,
                        importe = ventas.Sum(v => v.total),
                        sinCfdiRegistrado = ventas.Count(v => !v.tieneCfdiRegistrado)
                    }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Autofacturadas/ObtenerAutofacturadasPendientes");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Levanta pedido, remisión, factura y póliza de las ventas autofacturadas que se
        /// indiquen. Cada venta va en su propia transacción: si una falla —por ejemplo por
        /// un producto sin cuenta contable— las demás se contabilizan igual y se reporta
        /// cuál quedó pendiente, en vez de perder el lote entero.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Contabilización de ventas autofacturadas")]
        public async Task<JsonResult> ContabilizarAutofacturadas(IFormCollection fc)
        {
            if (!TryGetContextoSesion(out int sucursalSesion, out int empresaSesion, out string errorSesion))
                return Json(new { success = false, sessionExpired = true, message = errorSesion });

            var ids = (fc["ids"].ToString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim(), out int v) ? v : 0)
                .Where(v => v > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return Json(new { success = false, message = "No se recibieron ventas para contabilizar." });

            string cuentaBanco = fc["banco"].ToString();

            var procesadas = new List<object>();
            var fallidas = new List<object>();

            foreach (int idVenta in ids)
            {
                try
                {
                    var (ok, mensaje, folioFactura) =
                        await ContabilizarUnaAutofacturada(idVenta, cuentaBanco, sucursalSesion, empresaSesion);

                    if (ok)
                        procesadas.Add(new { ventaId = idVenta, folioFactura });
                    else
                        fallidas.Add(new { ventaId = idVenta, error = mensaje });
                }
                catch (Exception ex)
                {
                    LogErrorHelper.RegistrarLog("ContabilizarAutofacturadas", idVenta.ToString(),
                        ex.ToString(), User.Identity?.Name);

                    fallidas.Add(new { ventaId = idVenta, error = ex.Message });
                }
            }

            return Json(new
            {
                success = fallidas.Count == 0,
                procesadas,
                fallidas,
                message = fallidas.Count == 0
                    ? $"Se contabilizaron {procesadas.Count} venta(s) autofacturada(s)."
                    : $"{procesadas.Count} contabilizada(s), {fallidas.Count} con error. " +
                      "Las que fallaron siguen pendientes y se pueden reintentar."
            });
        }

        /// <summary>
        /// Cadena documental + póliza de UNA venta, en una sola transacción.
        /// Es el mismo recorrido que ProcesarDocumentosAsync pero SIN timbrar: el CFDI ya
        /// lo emitió el portal.
        /// </summary>
        [NonAction]
        private async Task<(bool Ok, string Mensaje, string FolioFactura)> ContabilizarUnaAutofacturada(
            int idVenta, string cuentaBanco, int sucursalSesion, int empresaSesion)
        {
            using var conn = AbrirConexion();
            using var tx = conn.BeginTransaction();

            // Se relee dentro de la transacción y se exige que siga pendiente: evita que
            // dos cajeros contabilicen la misma venta a la vez.
            var venta = RunQuery(@"
                SELECT em.id_encabezado, em.cfdi, em.mdp, em.f_pago,
                       f.idusocfdi, f.reg_fisr
                FROM encabezadomov em
                LEFT JOIN factura f ON f.uuid::text = em.cfdi
                WHERE em.id_encabezado = @id
                  AND em.suc = @suc
                  AND em.estatus_id = @estatus
                  AND em.encabezado_hijo IS NULL
                FOR UPDATE OF em;",
                new Dictionary<string, object>
                {
                    { "id",      idVenta },
                    { "suc",     sucursalSesion },
                    { "estatus", EstatusVentaAutofacturada }
                }, false, conn, tx).FirstOrDefault();

            if (venta == null)
                return (false, "La venta ya fue contabilizada o no está disponible.", null);

            // El uso de CFDI real está en la factura timbrada. En la venta, la columna
            // `cfdi` guarda el UUID —es la marca de autofacturada—, no un uso de CFDI:
            // pasarla tal cual dejaría el uso del documento con un GUID dentro.
            string usoCfdi = venta["idusocfdi"]?.ToString();
            if (string.IsNullOrWhiteSpace(usoCfdi)) usoCfdi = "G03";

            string regimenReceptor = venta["reg_fisr"]?.ToString();
            string mdp = venta["mdp"]?.ToString() ?? "PUE";
            string fpago = venta["f_pago"]?.ToString() ?? "";

            // 1️⃣ Pedido
            var pedido = await GuardarDesdeDocumentos(
                idVenta.ToString(), conn, tx, mdp, TipoFacturacionContado, fpago, usoCfdi, regimenReceptor);

            if (!pedido.Success)
                return (false, "Pedido: " + pedido.Message, null);

            dynamic dPedido = pedido.Data;
            int idPedido = dPedido.IdEncabezado;

            // 2️⃣ Remisión
            var remision = await GuardarRemisionDesdePedidos(idPedido.ToString(), conn, tx);
            if (!remision.Success)
                return (false, "Remisión: " + remision.Message, null);

            dynamic dRemision = remision.Data;
            int idRemision = dRemision.IdEncabezado;

            // 3️⃣ Documento de factura
            var facturaDoc = await GuardarFacturaDesdeDocs(idRemision.ToString(), conn, tx);
            if (!facturaDoc.Success)
                return (false, "Factura: " + facturaDoc.Message, null);

            dynamic dFactura = facturaDoc.Data;
            int idFacturaDoc = dFactura.IdEncabezado;
            string folioFactura = dFactura.Folio;

            // 4️⃣ Re-apuntar el CFDI al documento de factura.
            //    Al timbrar desde el portal no existía todavía un documento de factura, así
            //    que factura.encabezado_id quedó apuntando a la VENTA (VSUC). Con eso el
            //    comprobante no encaja en los módulos de facturación, que trabajan sobre
            //    documentos de factura, y arrastraba un documento de venta a pantallas donde
            //    no corresponde. Ya existe el VSFAC: el CFDI se cuelga de él.
            RunUpdate(@"
                UPDATE factura
                SET encabezado_id = @id_factura
                WHERE uuid::text = @uuid;",
                new Dictionary<string, object>
                {
                    { "id_factura", idFacturaDoc },
                    { "uuid",       venta["cfdi"]?.ToString() }
                }, false, conn, tx);

            // 5️⃣ Póliza, cartera y cobro del documento de factura.
            //    Faltaban la cartera y el cobro: la venta quedaba timbrada y con póliza pero
            //    sin registro de que el cliente ya había pagado en caja, así que aparecía como
            //    saldo vivo en cartera. El cobro va con su propio documento CXC.
            var poliza = GenerarDatosPoliza(idFacturaDoc, cuentaBanco, null, conn, tx);
            var polizaRegistrada = RegistrarPolizas(
                GetUserId(User.Identity.Name), idFacturaDoc, poliza, false, null, conn, tx);

            var cartera = RegistrarCartera(
                polizaRegistrada[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

            RegistrarCobroConDocumentoCxc(idFacturaDoc, cartera.CarteraId, cuentaBanco, conn, tx);

            // 6️⃣ Ligar la venta a su factura y cerrarla. A partir de aquí deja de aparecer
            //    como pendiente de contabilizar.
            RunUpdate(@"
                UPDATE encabezadomov
                SET encabezado_hijo = @id_factura,
                    estatus_id      = 11
                WHERE id_encabezado = @id_venta;",
                new Dictionary<string, object>
                {
                    { "id_factura", idFacturaDoc },
                    { "id_venta",   idVenta }
                }, false, conn, tx);

            tx.Commit();

            return (true, "", folioFactura);
        }
    }
}
