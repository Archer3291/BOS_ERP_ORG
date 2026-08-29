using BOS_ERP.Controllers.Facturacion.Servicios;
using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Npgsql;
using System.Data;

namespace BOS_ERP.Controllers.Facturacion
{
    public class FacturacionPredialController : PredialController
    {
        private EmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;

        // Tasa de IVA con la que XmlBuilderService.GenerarXmlArrendamiento arma el CFDI.
        // Los totales del encabezado se calculan con la misma tasa para que documento y
        // comprobante nunca queden desfasados.
        private const decimal TasaIva = 0.16m;

        // Margen permitido entre los totales que manda el navegador y los que
        // recalcula el servidor (centavos de redondeo).
        private const decimal ToleranciaTotales = 0.05m;

        private const string LogTag = "FacturacionPredialController";

        public FacturacionPredialController(
             EmailSender emailSender,
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            IConfiguration configuration,
            XmlBuilderService xmlBuilderService)
            : base(emailSender, timbradoOptions, viewEngine, tempDataProvider, env, xmlBuilderService)
        {
            _configuration = configuration;
            _emailSender = emailSender;
            xmlBuilderService = xmlBuilderService;
        }

        // ============================================================
        // PUNTO DE ENTRADA PRINCIPAL
        // Orquesta: Validación → Documento (TX) → Timbrado → Commit → PDF
        //
        // Todo lo que toca la BD (documento + folio consecutivo + factura)
        // vive dentro de una sola transacción que sólo se confirma cuando el
        // CFDI ya fue timbrado. Si el timbrado falla, el rollback devuelve el
        // consecutivo del folio y no queda basura en el sistema.
        // ============================================================
        [Route("CreditoCobranza/FacturacionPredial/FacturaPredial")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerarFacturaPredial()
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            var factura = new Factura();
            int documentoId = 0;
            int facturaId = 0;
            string folioDocumento = string.Empty;
            TimbradoResult resultadoTimbrado;

            using var conn = new NpgsqlConnection(connStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // ═══════════════════════════════════════════════
                // FASE 0 — Leer y validar el formulario
                // ═══════════════════════════════════════════════
                var errores = new List<string>();

                string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                    ? HttpContext.Session.GetString("EmpresaFactura")
                    : "pruebas";

                string GetEmisor(string campo) =>
                    _configuration[$"emisores:{perfil}:{campo}"] ?? "";

                var serie = Request.Form["Serie"].ToString();
                var idTipoPago = Request.Form["IdTipoPago"].ToString();
                var moneda = Request.Form["Moneda"].ToString();
                var rfcCliente = Request.Form["RfcCliente"].ToString();
                var rsoCliente = Request.Form["RsoCliente"].ToString();
                var cpR = Request.Form["CpR"].ToString();
                var idUsoCFDI = Request.Form["IdUsoCFDI"].ToString();
                var regimenFiscalReceptor = Request.Form["RegimenFiscalReceptor"].ToString();
                var metodoPago = Request.Form["MetodoPago"].ToString();
                var tipoDeComprobante = Request.Form["TipoDeComprobante"].ToString();
                var observaciones = Request.Form["Observaciones"].ToString();
                var TproductosJson = Request.Form["Tproductos"].ToString();

                if (string.IsNullOrWhiteSpace(rfcCliente)) errores.Add("El RFC del receptor es obligatorio.");
                if (string.IsNullOrWhiteSpace(rsoCliente)) errores.Add("La razón social del receptor es obligatoria.");
                if (string.IsNullOrWhiteSpace(cpR)) errores.Add("El domicilio fiscal (CP) del receptor es obligatorio.");
                if (string.IsNullOrWhiteSpace(moneda)) errores.Add("La moneda es obligatoria.");
                if (string.IsNullOrWhiteSpace(idTipoPago)) errores.Add("La forma de pago es obligatoria.");
                if (string.IsNullOrWhiteSpace(metodoPago)) errores.Add("El método de pago es obligatorio.");
                if (string.IsNullOrWhiteSpace(idUsoCFDI)) errores.Add("El uso de CFDI es obligatorio.");
                if (string.IsNullOrWhiteSpace(regimenFiscalReceptor)) errores.Add("El régimen fiscal del receptor es obligatorio.");
                if (string.IsNullOrWhiteSpace(GetEmisor("Rfc"))) errores.Add($"No hay datos de emisor configurados para el perfil '{perfil}'.");

                if (!decimal.TryParse(Request.Form["TipoCambio"].ToString(), out decimal tipoCambio) || tipoCambio <= 0)
                    tipoCambio = 1m;

                decimal.TryParse(Request.Form["Subtotal"].ToString(), out decimal subtotalCliente);
                decimal.TryParse(Request.Form["IVA"].ToString(), out decimal ivaCliente);
                decimal.TryParse(Request.Form["Total"].ToString(), out decimal totalCliente);

                // ── Conceptos ───────────────────────────────────────────
                List<ConceptoPredial> conceptos = null;
                if (string.IsNullOrWhiteSpace(TproductosJson))
                {
                    errores.Add("Debe capturar al menos un concepto.");
                }
                else
                {
                    try
                    {
                        conceptos = JsonConvert.DeserializeObject<List<ConceptoPredial>>(TproductosJson);
                    }
                    catch (Exception exJson)
                    {
                        errores.Add("El detalle de conceptos enviado no es válido: " + exJson.Message);
                    }
                }

                if (conceptos == null || conceptos.Count == 0)
                {
                    errores.Add("Debe capturar al menos un concepto.");
                }
                else
                {
                    for (int i = 0; i < conceptos.Count; i++)
                    {
                        var c = conceptos[i];
                        int num = i + 1;

                        if (string.IsNullOrWhiteSpace(c.ClaveProdServ)) errores.Add($"Concepto {num}: la clave de producto/servicio es obligatoria.");
                        if (string.IsNullOrWhiteSpace(c.ClaveUnidad)) errores.Add($"Concepto {num}: la clave de unidad es obligatoria.");
                        if (string.IsNullOrWhiteSpace(c.Descripcion)) errores.Add($"Concepto {num}: la descripción es obligatoria.");
                        if (c.Cantidad <= 0) errores.Add($"Concepto {num}: la cantidad debe ser mayor a 0.");
                        if (c.PrecioUnit <= 0) errores.Add($"Concepto {num}: el importe debe ser mayor a 0.");
                        if (string.IsNullOrWhiteSpace(c.ObjetoImp)) errores.Add($"Concepto {num}: el objeto de impuesto es obligatorio.");
                    }
                }

                if (errores.Count > 0)
                    return Json(new
                    {
                        success = false,
                        step = "Validacion",
                        error = string.Join("\n", errores)
                    });

                // ── Totales recalculados en servidor ────────────────────
                // Se calculan igual que en GenerarXmlArrendamiento para que
                // encabezado, partidas y CFDI cuadren al centavo.
                decimal subtotal = 0m, iva = 0m;
                foreach (var c in conceptos)
                {
                    decimal importe = Math.Round(c.Cantidad * c.PrecioUnit, 2);
                    subtotal += importe;
                    iva += Math.Round(importe * TasaIva, 2);
                }
                decimal total = subtotal + iva;

                if (Math.Abs(total - totalCliente) > ToleranciaTotales ||
                    Math.Abs(subtotal - subtotalCliente) > ToleranciaTotales ||
                    Math.Abs(iva - ivaCliente) > ToleranciaTotales)
                {
                    return Json(new
                    {
                        success = false,
                        step = "Validacion",
                        error = "Los totales enviados no coinciden con los calculados por el sistema. " +
                                $"Pantalla: subtotal {subtotalCliente:N2}, IVA {ivaCliente:N2}, total {totalCliente:N2}. " +
                                $"Sistema: subtotal {subtotal:N2}, IVA {iva:N2}, total {total:N2}. " +
                                "Recargue la pantalla y capture nuevamente."
                    });
                }

                // ═══════════════════════════════════════════════
                // FASE 1 — Catálogos SAT (descripciones del CFDI)
                // ═══════════════════════════════════════════════
                string mdp = LeerCatalogo("SELECT descripcion FROM mdp WHERE cve_mdp = @cve;",
                                          metodoPago, "método de pago", errores, conn, tx);
                string tp = LeerCatalogo("SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve;",
                                          idTipoPago, "forma de pago", errores, conn, tx);
                string receptorCFDI = LeerCatalogo("SELECT descripcion FROM catusocfdi WHERE clave = @cve;",
                                          idUsoCFDI, "uso de CFDI", errores, conn, tx);
                string receptorRege = LeerCatalogo("SELECT descripcion FROM catregimenfiscal WHERE clave = @cve;",
                                          regimenFiscalReceptor, "régimen fiscal del receptor", errores, conn, tx);

                if (errores.Count > 0)
                    return Json(new
                    {
                        success = false,
                        step = "Validacion",
                        error = string.Join("\n", errores)
                    });

                // ═══════════════════════════════════════════════
                // FASE 2 — Documento + partidas DENTRO de la TX
                // (aquí se consume el folio consecutivo)
                // ═══════════════════════════════════════════════
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 24,
                    IdTpDoc = 73,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "FAR",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Imp = total,
                    Sub = subtotal,
                    CliProv = rsoCliente,
                    Ref = 1,
                    Ccy = moneda,
                    Estatus = 1,
                    FPago = Convert.ToInt32(idTipoPago),
                    Mdp = metodoPago,
                    TipoPoceso = "factura_arrendamiento",
                    CFDI = idUsoCFDI
                };

                var partidas = conceptos.Select(c => new PartidaDocumento
                {
                    CveProd = c.ClaveProdServ,
                    DescrProd = c.Descripcion,
                    CantUd = c.Cantidad,
                    PvProd = c.PrecioUnit,
                    Dto1 = 0,
                    ImpPart = Math.Round(c.Cantidad * c.PrecioUnit, 2),
                    Ud = "SRV"
                }).ToList();

                var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                if (documento == null ||
                    !documento.ContainsKey("IdEncabezado") ||
                    !documento.ContainsKey("folio_generado"))
                {
                    tx.Rollback();
                    return Json(new
                    {
                        success = false,
                        step = "GuardarDocumento",
                        error = "No se pudo generar el documento base ni su folio. No se timbró ningún comprobante."
                    });
                }

                documentoId = Convert.ToInt32(documento["IdEncabezado"]);
                folioDocumento = documento["folio_generado"].ToString();

                // ═══════════════════════════════════════════════
                // FASE 3 — Armar la factura y TIMBRAR (sin tocar BD)
                // ═══════════════════════════════════════════════
                factura.Serie = serie;
                factura.Folio = folioDocumento;
                factura.IdTipoPago = idTipoPago;
                factura.Moneda = moneda;
                factura.CpE = GetEmisor("CpE");
                factura.LugarExpedicion = GetEmisor("CpE");
                factura.RfcEmisor = GetEmisor("Rfc");
                factura.RsoEmisor = GetEmisor("RazonSocial");
                factura.Rege = GetEmisor("Regimen");
                factura.RfcCliente = rfcCliente;
                factura.RsoCliente = rsoCliente;
                factura.CpR = cpR;
                factura.IdUsoCFDI = idUsoCFDI;
                factura.CFDIText = receptorCFDI;
                factura.Regc = regimenFiscalReceptor;
                factura.regimenEText = receptorRege;
                factura.Subtotal = subtotal;
                factura.IVA = iva;
                factura.Total = total;
                factura.TipoCambio = tipoCambio;
                factura.metodoPagoTexto = metodoPago;
                factura.MdpFactura = mdp;
                factura.TipoDeComprobante = string.IsNullOrWhiteSpace(tipoDeComprobante) ? "I" : tipoDeComprobante;
                factura.Observaciones = observaciones;
                factura.formaPagoTexto = tp;
                factura.EncabezadoId = documentoId;
                factura.TipoFacturacion = "ARRENDAMIENTO";
                factura.Tproductos = ConstruirTablaProductos(conceptos);

                resultadoTimbrado = GenerarXml(factura);

                if (!resultadoTimbrado.Success)
                {
                    RegistrarRechazoPac(resultadoTimbrado.Message, "Facturacion/Predial");
                    // El rollback devuelve el folio consecutivo: nada quedó capturado.
                    tx.Rollback();
                    return Json(new
                    {
                        success = false,
                        step = "Timbrado",
                        error = resultadoTimbrado.Message
                    });
                }

                // ═══════════════════════════════════════════════
                // FASE 4 — Guardar la factura y COMMIT
                // A partir de aquí el CFDI ya existe en el SAT: si algo
                // falla se registra como huérfano para conciliarlo después.
                // ═══════════════════════════════════════════════
                try
                {
                    facturaId = GuardarFactura(factura, conn, tx);
                    tx.Commit();
                }
                catch (Exception exCommit)
                {
                    RegistrarErrorParaTicket(exCommit, "FacturacionPredial/?");
                    RegistrarCFDIHuerfano(resultadoTimbrado.UUID, documentoId, exCommit.Message);

                    LogErrorHelper.RegistrarLog(LogTag, resultadoTimbrado.UUID,
                        $"CRÍTICO: CFDI timbrado UUID={resultadoTimbrado.UUID} " +
                        $"pero el guardado falló: {exCommit.Message}",
                        nivel: "CRITICAL");

                    return Json(new
                    {
                        success = false,
                        step = "Commit",
                        error = "El CFDI fue timbrado en el SAT pero no se pudo guardar en el sistema. " +
                                "Contacte a soporte con el UUID: " + resultadoTimbrado.UUID
                    });
                }

                // ═══════════════════════════════════════════════
                // FASE 5 — PDF FUERA de la transacción
                // Si falla, el CFDI sigue siendo válido: sólo se avisa.
                // ═══════════════════════════════════════════════
                bool pdfGenerado = true;
                string mensajeFinal = "Comprobante timbrado y registrado correctamente";

                try
                {
                    await GenerarFactura(factura);
                }
                catch (Exception exPdf)
                {
                    pdfGenerado = false;
                    mensajeFinal = "El CFDI se timbró y guardó correctamente, pero no se pudo generar el PDF. " +
                                   "Descargue el XML y solicite la reimpresión.";

                    LogErrorHelper.RegistrarLog(LogTag, resultadoTimbrado.UUID,
                        $"CFDI timbrado UUID={resultadoTimbrado.UUID} pero falló la generación del PDF: {exPdf.Message}",
                        nivel: "WARNING");
                }

                return Json(new
                {
                    success = true,
                    message = "Factura de arrendamiento/predial timbrada correctamente",
                    resumen = new
                    {
                        documentosProcesados = 1,
                        tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                    },
                    detalles = new
                    {
                        uuid = resultadoTimbrado.UUID,
                        serie = factura.Serie,
                        folio = factura.Folio,
                        total = factura.Total,
                        subtotal = factura.Subtotal,
                        iva = factura.IVA,
                        rfcCliente = factura.RfcCliente,
                        razonSocial = factura.RsoCliente,
                        fecha = factura.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                        cantidadProductos = factura.Tproductos.Rows.Count,
                        pedidoId = documentoId,
                        facturaId = facturaId,
                        mensajeFinal = mensajeFinal,
                        rutaQr = factura.RutaQr,
                        pdfUrl = pdfGenerado
                            ? Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf")
                            : null,
                        xmlUrl = Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml"),
                        correosCliente = new List<object>()
                    }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionPredial/?");
                // Rollback defensivo: si el commit ya ocurrió, Npgsql lanza y lo ignoramos.
                try { tx.Rollback(); } catch { }

                LogErrorHelper.RegistrarLog(LogTag, documentoId.ToString(),
                    $"Error en el proceso de facturación predial: {ex.Message}", nivel: "ERROR");

                return Json(new
                {
                    success = false,
                    message = "Error al generar la factura: " + ex.Message,
                    detalles = ex.StackTrace
                });
            }
        }

        // ============================================================
        // Lee una descripción de catálogo y acumula el error si no existe
        // ============================================================
        private string LeerCatalogo(string query, string clave, string nombreCatalogo,
            List<string> errores, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var valor = RunScalar(query, new Dictionary<string, object> { { "cve", clave } }, false, conn, tx);

            if (valor == null || valor == DBNull.Value)
            {
                errores.Add($"La clave '{clave}' no existe en el catálogo de {nombreCatalogo}.");
                return string.Empty;
            }

            return valor.ToString();
        }

        // ============================================================
        // DataTable de conceptos que consume XmlBuilderService
        // ============================================================
        private static DataTable ConstruirTablaProductos(List<ConceptoPredial> conceptos)
        {
            var tabla = new DataTable();
            tabla.Columns.AddRange(new[]
            {
                new DataColumn("numero", typeof(string)),
                new DataColumn("claveProdServ", typeof(string)),
                new DataColumn("claveUnidad", typeof(string)),
                new DataColumn("descripcion", typeof(string)),
                new DataColumn("cantidad", typeof(double)),
                new DataColumn("precioUnit", typeof(double)),
                new DataColumn("importe", typeof(double)),
                new DataColumn("objetoImp", typeof(string)),
                new DataColumn("cuentaPredial", typeof(string))
            });

            foreach (var c in conceptos)
            {
                tabla.Rows.Add(
                    c.Numero ?? string.Empty,
                    c.ClaveProdServ,
                    c.ClaveUnidad,
                    c.Descripcion,
                    (double)c.Cantidad,
                    (double)c.PrecioUnit,
                    (double)Math.Round(c.Cantidad * c.PrecioUnit, 2),
                    c.ObjetoImp,
                    c.CuentaPredial ?? string.Empty
                );
            }

            return tabla;
        }

        // ============================================================
        // Registrar CFDI huérfano con conexión INDEPENDIENTE
        // (la transacción original ya no sirve tras el fallo)
        // ============================================================
        private void RegistrarCFDIHuerfano(string uuid, int idEncabezado, string errorCommit)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            try
            {
                using var conn2 = new NpgsqlConnection(connStr);
                conn2.Open();
                using var cmd = conn2.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO cfdi_huerfanos
                        (uuid, encabezado_id, error_commit, fecha_registro, resuelto)
                    VALUES
                        (@uuid, @enc, @error, NOW(), false)
                    ON CONFLICT (uuid) DO NOTHING;";
                cmd.Parameters.AddWithValue("uuid", uuid);
                cmd.Parameters.AddWithValue("enc", idEncabezado);
                cmd.Parameters.AddWithValue("error", errorCommit);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, uuid,
                    $"No se pudo registrar CFDI huérfano UUID={uuid}: {ex.Message}",
                    nivel: "CRITICAL");
            }
        }

        [Route("CreditoCobranza/FacturacionPredial/GetClientes")]
        public JsonResult GetClientes()
        {

            string query = "select id_cliente, n_cli from catclientes_especiales";
            var result = RunQuery(query);

            return Json(new { data = result });
        }
        [Route("CreditoCobranza/FacturacionPredial/GetCliente")]
        public JsonResult GetCliente(int id_cliente)
        {
            var parameters = new Dictionary<string, object>();
            string query = "select id_cliente, n_cli, rfc, cp, observaciones from catclientes_especiales where id_cliente = @id_cliente";
            parameters.Add("id_cliente", id_cliente);
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }

        // ============================================================
        // Concepto tal como lo manda getPredialTableProducts() en _Predial
        // ============================================================
        private sealed class ConceptoPredial
        {
            public string Numero { get; set; } = string.Empty;
            public string ClaveProdServ { get; set; } = string.Empty;
            public string ClaveUnidad { get; set; } = string.Empty;
            public string Descripcion { get; set; } = string.Empty;
            public decimal Cantidad { get; set; }
            public decimal PrecioUnit { get; set; }
            public decimal Importe { get; set; }
            public string ObjetoImp { get; set; } = string.Empty;
            public string CuentaPredial { get; set; } = string.Empty;
        }
    }
}
