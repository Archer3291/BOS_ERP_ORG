using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas
{
    /// <summary>
    /// Ticket de venta del punto de venta: el comprobante que se entrega en mostrador.
    /// No sustituye al CFDI — es el respaldo impreso de lo que se cobró y de lo que queda
    /// pendiente de entregar.
    /// </summary>
    public partial class PuntoDeVentaController
    {
        /// <summary>
        /// Ticket de una venta. Recibe el encabezado de la VENTA (VSUC), que es donde
        /// cuelgan los cobros (factura_formas_pagos) y los pendientes (ventas_pendientes);
        /// la factura se alcanza por encabezado_hijo.
        /// </summary>
        [HttpGet]
        [Route("PuntoDeVenta/Ticket/{id:int}")]
        public IActionResult Ticket(int id)
        {
            var ticket = ArmarTicket(id);

            if (ticket == null)
                return NotFound("No se encontró la venta solicitada.");

            // Dirección del portal público donde el cliente captura el folio del ticket.
            string urlPortal = _configuration["Autofacturacion:UrlPublica"];
            ViewBag.UrlAutofacturacion = urlPortal;

            // QR sólo cuando la venta aún puede facturarse: en una ya timbrada sobra.
            if (!ticket.TieneFactura)
                ViewBag.QrAutofacturacion = QrComoDataUri(ContenidoQr(ticket.Folio, urlPortal));

            return View("~/Views/Ventas/TicketVenta.cshtml", ticket);
        }

        /// <summary>
        /// Contenido del QR. Si hay portal configurado se codifica la URL con el folio: así
        /// la cámara normal del teléfono lleva al cliente directo a facturar. El lector del
        /// propio portal entiende ambas formas (URL con ?folio= o folio a secas).
        /// </summary>
        [NonAction]
        private static string ContenidoQr(string folio, string urlPortal)
        {
            if (string.IsNullOrWhiteSpace(urlPortal))
                return folio;

            string separador = urlPortal.Contains('?') ? "&" : "?";
            return $"{urlPortal.TrimEnd('/')}{separador}folio={Uri.EscapeDataString(folio)}";
        }

        /// <summary>
        /// QR en PNG embebido como data URI. Se usa PngByteQRCode —no la variante con
        /// System.Drawing— para no depender del GDI del servidor, y va incrustado en el HTML
        /// para que el ticket se imprima aunque la terminal esté sin red.
        /// </summary>
        [NonAction]
        private static string QrComoDataUri(string contenido, int pixelsPorModulo = 4)
        {
            if (string.IsNullOrWhiteSpace(contenido))
                return null;

            using var generador = new QRCoder.QRCodeGenerator();
            var datos = generador.CreateQrCode(contenido, QRCoder.QRCodeGenerator.ECCLevel.M);
            var png = new QRCoder.PngByteQRCode(datos).GetGraphic(pixelsPorModulo);

            return "data:image/png;base64," + Convert.ToBase64String(png);
        }

        /// <summary>
        /// Ventas con algo pendiente, para consultar y reimprimir su ticket. Son tres
        /// situaciones distintas y una venta puede estar en varias a la vez:
        ///   · sin facturar        — nadie le ha emitido CFDI, ni la caja ni el propio
        ///                           cliente por el portal de autofacturación;
        ///   · pago parcial        — sólo se cobró el material que se llevó;
        ///   · falta por entregar  — queda material en el almacén.
        /// No se reusa ObtenerPendientesSucursal porque ése resuelve otra pregunta: qué
        /// falta ENTREGAR. Aquí interesa además lo que falta cobrar o facturar.
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerTicketsPendientes(int dias = 90)
        {
            try
            {
                if (!TryGetContextoSesion(out int sucursalSesion, out int empresaSesion, out string errorSesion))
                    return Json(new { success = false, sessionExpired = true, message = errorSesion });

                if (dias < 1 || dias > 730) dias = 90;

                var filas = RunQuery(@"
                    SELECT
                        em.id_encabezado,
                        em.folio,
                        em.fch,
                        em.imp,
                        em.tipo_proceso,
                        (CURRENT_DATE - em.fch::date) AS dias,
                        cc.n_cli,
                        em.cli_prov,
                        -- Sin CFDI: ni facturada en caja (estatus 11) ni autofacturada
                        -- por el cliente (el portal deja el uuid en cfdi).
                        (em.estatus_id = 1 AND em.cfdi IS NULL) AS sin_facturar,
                        (em.tipo_proceso = @parcial_por_cobrar)  AS pago_parcial,
                        COALESCE(pend.articulos, 0)              AS articulos_pendientes,
                        COALESCE(pend.importe, 0)                AS importe_pendiente
                    FROM encabezadomov em
                    LEFT JOIN catclientes cc
                           ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
                    LEFT JOIN (
                        SELECT vp.encabezado_venta,
                               count(*) AS articulos,
                               SUM(ROUND(vp.cantidad_pendiente * vp.pv_prod
                                   * (1 - COALESCE(vp.dto1, 0) / 100.0) * 1.16, 2)) AS importe
                        FROM ventas_pendientes vp
                        WHERE vp.cantidad_pendiente > 0
                          AND vp.estatus <> 'cancelado'
                        GROUP BY vp.encabezado_venta
                    ) pend ON pend.encabezado_venta = em.id_encabezado
                    WHERE em.nat = 'VSUC'
                      AND em.suc = @sucursal_id
                      AND em.variacion = 0
                      AND em.fch >= NOW() - (@dias * INTERVAL '1 day')
                      AND (
                            (em.estatus_id = 1 AND em.cfdi IS NULL)
                         OR em.tipo_proceso = @parcial_por_cobrar
                         OR pend.encabezado_venta IS NOT NULL
                      )
                    ORDER BY em.fch DESC;",
                    new Dictionary<string, object>
                    {
                        { "empresa_id",         empresaSesion  },
                        { "sucursal_id",        sucursalSesion },
                        { "dias",               dias           },
                        { "parcial_por_cobrar", TipoVentaParcialPorCobrar }
                    });

                var ventas = filas.Select(f => new
                {
                    ventaId = GetInt(f["id_encabezado"]) ?? 0,
                    folio = f["folio"]?.ToString(),
                    fecha = Convert.ToDateTime(f["fch"]).ToString("dd/MM/yyyy HH:mm"),
                    dias = GetInt(f["dias"]) ?? 0,
                    cliente = f["n_cli"]?.ToString() ?? f["cli_prov"]?.ToString(),
                    total = DecimalDe(f["imp"]),
                    sinFacturar = f["sin_facturar"] is bool sf && sf,
                    pagoParcial = f["pago_parcial"] is bool pp && pp,
                    articulosPendientes = GetInt(f["articulos_pendientes"]) ?? 0,
                    importePendiente = DecimalDe(f["importe_pendiente"])
                }).ToList();

                return Json(new
                {
                    success = true,
                    ventas,
                    resumen = new
                    {
                        total = ventas.Count,
                        sinFacturar = ventas.Count(v => v.sinFacturar),
                        pagoParcial = ventas.Count(v => v.pagoParcial),
                        faltaEntregar = ventas.Count(v => v.articulosPendientes > 0),
                        dias
                    }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Ticket/ObtenerTicketsPendientes");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Reúne todo lo que va impreso. Se acota por empresa y sucursal de la sesión para
        /// que nadie imprima el ticket de otra sucursal cambiando el id en la URL.
        /// </summary>
        [NonAction]
        private TicketVenta ArmarTicket(int idVenta)
        {
            var parametros = new Dictionary<string, object>
            {
                { "id", idVenta },
                { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            var venta = RunQuery(@"
                SELECT em.id_encabezado, em.folio, em.fch, em.tipo_proceso, em.cli_prov,
                       em.sub, em.imp, em.suc, em.usr_doc, em.coment1, em.encabezado_hijo,
                       em.pl_dias,
                       cc.n_cli, cc.rfc,
                       s.descripcion AS sucursal_nombre
                FROM encabezadomov em
                LEFT JOIN catclientes cc
                       ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
                LEFT JOIN catsucursales s ON s.id_sucursal = em.suc
                WHERE em.id_encabezado = @id
                  AND em.suc = @suc;", parametros).FirstOrDefault();

            if (venta == null)
                return null;

            var partidas = RunQuery(@"
                SELECT cve_prod, descr_prod, cant_ud, ud, pv_prod, dto1, imp_part
                FROM partidasdoc
                WHERE encabezado_id = @id
                ORDER BY nro_part;", parametros);

            var pagos = RunQuery(@"
                SELECT metodo, metodo_nombre, monto
                FROM factura_formas_pagos
                WHERE encabezado_id = @id
                ORDER BY id_factura_forma_pago;", parametros);

            // Sólo lo que sigue debiendo entregarse. Lo ya entregado no va en el ticket.
            var pendientes = RunQuery(@"
                SELECT cve_prod, descr_prod, ud, cantidad_pendiente, pv_prod, dto1
                FROM ventas_pendientes
                WHERE encabezado_venta = @id
                  AND cantidad_pendiente > 0
                  AND estatus <> 'cancelado'
                ORDER BY cve_prod;", parametros);

            // La factura sale del documento hijo (VSFAC); puede no existir todavía.
            var factura = RunQuery(@"
                SELECT f.uuid, f.total, f.statusfactura, em.folio AS folio_factura
                FROM encabezadomov em
                INNER JOIN factura f ON f.encabezado_id = em.id_encabezado
                WHERE em.id_encabezado = @hijo;",
                new Dictionary<string, object>
                {
                    { "hijo", GetInt(venta["encabezado_hijo"]) ?? 0 }
                }).FirstOrDefault();

            string tipoProceso = venta["tipo_proceso"]?.ToString() ?? "";

            var ticket = new TicketVenta
            {
                IdVenta = Convert.ToInt32(venta["id_encabezado"]),
                Folio = venta["folio"]?.ToString() ?? "",
                Fecha = Convert.ToDateTime(venta["fch"]),
                Sucursal = venta["sucursal_nombre"]?.ToString() ?? "",
                Cajero = venta["usr_doc"]?.ToString() ?? "",
                ClienteClave = venta["cli_prov"]?.ToString() ?? "",
                ClienteNombre = venta["n_cli"]?.ToString() ?? "Público en general",
                ClienteRfc = venta["rfc"]?.ToString() ?? "",
                Observaciones = venta["coment1"]?.ToString() ?? "",

                // El tipo de venta define qué se cobró:
                //   venta_sucursal                    → todo entregado y pagado
                //   venta_sucursal_parcial            → se pagó completo, falta entregar
                //   venta_sucursal_parcial_por_cobrar → se pagó sólo lo entregado
                //   venta_sucursal_credito*           → nada se pagó: quedó en cartera
                PagadoCompleto = tipoProceso != TipoVentaParcialPorCobrar,
                EsCredito = tipoProceso.StartsWith(TipoVentaCreditoCompleta, StringComparison.OrdinalIgnoreCase),
                PlazoDias = GetInt(venta["pl_dias"], 0) ?? 0,
                HayPendientes = false,

                FolioFactura = factura?["folio_factura"]?.ToString() ?? "",
                UuidFactura = factura?["uuid"]?.ToString() ?? "",
                FacturaCancelada = string.Equals(
                    factura?["statusfactura"]?.ToString(), "Cancelada", StringComparison.OrdinalIgnoreCase)
            };

            foreach (var p in partidas)
            {
                decimal cantidad = DecimalDe(p["cant_ud"]);
                decimal precio = DecimalDe(p["pv_prod"]);
                decimal bruto = Math.Round(cantidad * precio, 2);
                decimal neto = DecimalDe(p["imp_part"]);

                ticket.Partidas.Add(new TicketPartida
                {
                    Clave = p["cve_prod"]?.ToString() ?? "",
                    Descripcion = p["descr_prod"]?.ToString() ?? "",
                    Cantidad = cantidad,
                    Unidad = p["ud"]?.ToString() ?? "",
                    PrecioUnitario = precio,
                    Descuento = bruto - neto,
                    Importe = neto
                });
            }

            foreach (var p in pendientes)
            {
                ticket.HayPendientes = true;
                decimal cantidad = DecimalDe(p["cantidad_pendiente"]);
                decimal precio = DecimalDe(p["pv_prod"]);
                decimal bruto = Math.Round(cantidad * precio, 2);
                decimal descuento = Math.Round(bruto * (DecimalDe(p["dto1"]) / 100m), 2);

                ticket.Pendientes.Add(new TicketPartida
                {
                    Clave = p["cve_prod"]?.ToString() ?? "",
                    Descripcion = p["descr_prod"]?.ToString() ?? "",
                    Cantidad = cantidad,
                    Unidad = p["ud"]?.ToString() ?? "",
                    PrecioUnitario = precio,
                    Descuento = descuento,
                    Importe = bruto - descuento
                });
            }

            foreach (var p in pagos)
            {
                ticket.Pagos.Add(new TicketPago
                {
                    Metodo = p["metodo"]?.ToString() ?? "",
                    Nombre = p["metodo_nombre"]?.ToString() ?? "",
                    Monto = DecimalDe(p["monto"])
                });
            }

            // Los totales se recalculan de las partidas con el mismo criterio que el CFDI
            // (IVA redondeado por renglón) para que el ticket no discrepe del comprobante.
            var totales = CalcularTotales(ticket.Partidas.Select(p => new PartidaDocumento
            {
                CantUd = p.Cantidad,
                PvProd = p.PrecioUnitario,
                ImpPart = p.Importe
            }));

            ticket.Subtotal = totales.Neto;
            ticket.Descuento = ticket.Partidas.Sum(p => p.Descuento);
            ticket.Iva = totales.Iva;
            ticket.Total = totales.Total;

            ticket.TotalPagado = ticket.Pagos.Sum(p => p.Monto);
            ticket.Cambio = Math.Max(0m, ticket.TotalPagado - ticket.Total);

            // A crédito no hay pagos ni cambio; lo que el ticket informa es la deuda.
            if (ticket.EsCredito)
            {
                ticket.PagadoCompleto = false;
                ticket.SaldoPorCobrar = ticket.Total;
                return ticket;
            }

            // Lo que quedará por cobrar cuando se entregue el material pendiente. Sólo
            // aplica cuando se cobró únicamente lo entregado.
            ticket.SaldoPorCobrar = ticket.PagadoCompleto
                ? 0m
                : ticket.Pendientes.Sum(p => Math.Round(p.Importe * 1.16m, 2));

            return ticket;
        }
    }
}
