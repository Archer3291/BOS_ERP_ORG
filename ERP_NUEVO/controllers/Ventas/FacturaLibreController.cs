using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Npgsql;
using System.Data;

namespace BOS_ERP.Controllers.Ventas
{
    /// <summary>
    /// Facturación libre: emite un CFDI de ingreso sin cadena documental.
    ///
    /// A diferencia de los canales de venta, aquí no hay cotización, pedido ni remisión: se
    /// captura el cliente y los conceptos, y se timbra. Por eso NO toca inventario — mover
    /// almacén requiere una remisión, y en el momento en que la necesitara dejaría de ser
    /// libre. Sirve para servicios, fletes, refacturaciones administrativas y ajustes.
    ///
    /// Sí genera todo lo demás: documento propio (FACLIB), póliza, cartera y —cuando es de
    /// contado— su cobro con documento CXC, igual que el resto de la facturación.
    ///
    /// El orden importa: el documento se crea dentro de la transacción, el timbrado ocurre
    /// con la transacción abierta y el commit sólo llega si el PAC respondió bien. Si el
    /// timbrado falla, el rollback devuelve el folio consecutivo y no queda basura. Si el
    /// commit falla DESPUÉS de timbrar, el CFDI ya existe en el SAT y se registra como
    /// huérfano para conciliarlo a mano.
    /// </summary>
    [RightAuthorize(new[] { "facturacion_especial", "facturacion_normal" })]
    public class FacturaLibreController : FacturacionVentaController
    {
        private const string NatFacturaLibre = "FACLIB";
        private const int TpDocFacturaLibre = 93;
        private const int AreaFacturacion = 24;
        private const string LogTag = "FacturaLibreController";

        // Tasa con la que XmlBuilderService arma el CFDI de ingreso. Los totales del
        // encabezado se calculan con la misma para que documento y comprobante no se
        // desfasen ni un centavo.
        private const decimal TasaIva = 0.16m;

        // Margen tolerado entre los totales que manda el navegador y los que recalcula el
        // servidor (centavos de redondeo).
        private const decimal ToleranciaTotales = 0.05m;

        private readonly IConfiguration _config;

        public FacturaLibreController(
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            IConfiguration configuration,
            XmlBuilderService xmlService)
            : base(timbradoOptions, viewEngine, tempDataProvider, env, xmlService)
        {
            _config = configuration;
        }

        protected override string FacturaLogTag => LogTag;

        // ════════════════════════════════════════════════════════════════
        // Pantalla
        // ════════════════════════════════════════════════════════════════

        public IActionResult Index() => View("~/Views/Ventas/FacturaLibre.cshtml");

        // ════════════════════════════════════════════════════════════════
        // Catálogos de apoyo
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Busca clientes por clave, nombre o RFC. Trae ya resueltos los datos fiscales que
        /// el CFDI necesita para no obligar a capturarlos otra vez.
        /// </summary>
        [HttpGet]
        public JsonResult BuscarClientes(string term, int limite = 20)
        {
            try
            {
                var datos = RunQuery(@"
                    SELECT cc.id_cliente, cc.cve_cli, cc.n_cli, cc.rfc, cc.cp,
                           cc.pl_crd, cc.lim_crd,
                           df.codigo_postal, df.regimen_fiscal, df.uso_sugerido, df.forma_pago
                    FROM catclientes cc
                    LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
                    WHERE cc.empresa_id = @empresa_id
                      AND (LOWER(cc.cve_cli) LIKE LOWER(@term)
                        OR LOWER(cc.n_cli)   LIKE LOWER(@term)
                        OR LOWER(cc.rfc)     LIKE LOWER(@term))
                    ORDER BY cc.n_cli
                    LIMIT @limite",
                    new Dictionary<string, object>
                    {
                        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                        { "term",       $"%{term}%" },
                        { "limite",     limite }
                    });

                var clientes = datos.Select(c => new
                {
                    idCliente = GetInt(c["id_cliente"]) ?? 0,
                    clave = c["cve_cli"]?.ToString(),
                    nombre = c["n_cli"]?.ToString(),
                    rfc = c["rfc"]?.ToString(),
                    // El CP fiscal vive en direcciones_facturacion; catclientes.cp es el
                    // domicilio comercial y no siempre coinciden.
                    cp = string.IsNullOrWhiteSpace(c["codigo_postal"]?.ToString())
                            ? c["cp"]?.ToString()
                            : c["codigo_postal"]?.ToString(),
                    regimenFiscal = c["regimen_fiscal"]?.ToString(),
                    usoCfdi = c["uso_sugerido"]?.ToString(),
                    formaPago = c["forma_pago"]?.ToString(),
                    plazoCredito = GetInt(c["pl_crd"]) ?? 0
                }).ToList();

                return Json(new { success = true, clientes });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturaLibre/BuscarClientes");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Busca productos del catálogo trayendo sus claves SAT. Es sólo una comodidad de
        /// captura: el concepto que se factura sigue siendo libre y no descuenta existencias.
        /// Los productos sin fila en catrelacion se excluyen porque no podrían timbrarse.
        /// </summary>
        [HttpGet]
        public JsonResult BuscarProductos(string term, int limite = 20)
        {
            try
            {
                var datos = RunQuery(@"
                    SELECT p.cve_prod, p.descr_prod, p.udm,
                           r.prod_sat, r.ud_sat, r.obj_impto
                    FROM catproductos p
                    INNER JOIN catrelacion r ON r.prod_kepler = p.cve_prod
                    WHERE p.empresa_id = @empresa_id
                      AND (LOWER(p.cve_prod)   LIKE LOWER(@term)
                        OR LOWER(p.descr_prod) LIKE LOWER(@term))
                    ORDER BY p.descr_prod
                    LIMIT @limite",
                    new Dictionary<string, object>
                    {
                        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                        { "term",       $"%{term}%" },
                        { "limite",     limite }
                    });

                var productos = datos.Select(p => new
                {
                    cveProd = p["cve_prod"]?.ToString(),
                    descripcion = p["descr_prod"]?.ToString(),
                    unidad = p["udm"]?.ToString(),
                    claveProdServ = p["prod_sat"]?.ToString(),
                    claveUnidad = p["ud_sat"]?.ToString(),
                    objetoImp = p["obj_impto"]?.ToString()
                }).ToList();

                return Json(new { success = true, productos });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturaLibre/BuscarProductos");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>Catálogos del SAT que alimentan los selectores de la pantalla.</summary>
        [HttpGet]
        public JsonResult Catalogos()
        {
            try
            {
                return Json(new
                {
                    success = true,
                    usosCfdi = RunQuery("SELECT clave, descripcion FROM catusocfdi ORDER BY clave"),
                    formasPago = RunQuery("SELECT cve_sat AS clave, descripcion FROM cat_f_pago ORDER BY cve_sat"),
                    metodosPago = RunQuery("SELECT cve_mdp AS clave, descripcion FROM mdp ORDER BY cve_mdp"),
                    regimenes = RunQuery("SELECT clave, descripcion FROM catregimenfiscal ORDER BY clave"),
                    // La columna se llama cve_objeto_importacion, no `clave`, pese a que su
                    // contenido es el c_ObjetoImp del SAT (01, 02, 03…).
                    objetosImp = RunQuery(
                        "SELECT cve_objeto_importacion AS clave, descripcion " +
                        "FROM catobjeto_impuesto_sat ORDER BY cve_objeto_importacion"),
                    bancos = RunQuery("SELECT codigo, nombre FROM cuentas_finanzas WHERE codigo LIKE '%1-1-02%' ORDER BY codigo")
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturaLibre/Catalogos");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Emisión
        // ════════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("FacturaLibre/GenerarFacturaLibre")]
        [AuditAction(Modulo = "Ventas", Accion = "Emisión de factura libre")]
        public JsonResult GenerarFacturaLibre(IFormCollection fc)
        {
            int documentoId = 0;
            TimbradoResult timbrado = null;

            using var conn = AbrirConexion();
            using var tx = conn.BeginTransaction();

            try
            {
                // ── FASE 0: leer y validar ──────────────────────────────
                var errores = new List<string>();

                // El perfil de emisor obedece al setting global, igual que en el resto del
                // sistema: con perfil_factura en falso todo sale contra el RFC de pruebas,
                // aunque el usuario tenga una empresa real en sesión.
                string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                    ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                    : "pruebas";

                string Emisor(string campo) => _config[$"Emisores:{perfil}:{campo}"] ?? "";

                if (string.IsNullOrWhiteSpace(Emisor("Rfc")))
                    errores.Add($"No hay datos de emisor configurados para el perfil '{perfil}'.");

                int idCliente = int.TryParse(fc["idCliente"].ToString(), out int ic) ? ic : 0;
                string rfcCliente = fc["rfcCliente"].ToString().Trim().ToUpperInvariant();
                string rsoCliente = fc["rsoCliente"].ToString().Trim();
                string cpCliente = fc["cpCliente"].ToString().Trim();
                string regimenReceptor = fc["regimenReceptor"].ToString().Trim();
                string usoCfdi = fc["usoCfdi"].ToString().Trim();
                string formaPago = fc["formaPago"].ToString().Trim();
                string metodoPago = fc["metodoPago"].ToString().Trim();
                string moneda = string.IsNullOrWhiteSpace(fc["moneda"].ToString()) ? "MXN" : fc["moneda"].ToString().Trim();
                string condicion = fc["condicion"].ToString().Trim().ToLowerInvariant();
                string cuentaBanco = fc["banco"].ToString().Trim();
                string observaciones = fc["observaciones"].ToString();
                string serie = string.IsNullOrWhiteSpace(fc["serie"].ToString()) ? "FL" : fc["serie"].ToString().Trim();

                bool esContado = condicion != "credito";

                if (idCliente <= 0) errores.Add("Debe seleccionar un cliente del catálogo.");
                if (string.IsNullOrWhiteSpace(rfcCliente)) errores.Add("El RFC del receptor es obligatorio.");
                if (string.IsNullOrWhiteSpace(rsoCliente)) errores.Add("La razón social del receptor es obligatoria.");
                if (string.IsNullOrWhiteSpace(cpCliente)) errores.Add("El código postal del receptor es obligatorio.");
                if (string.IsNullOrWhiteSpace(regimenReceptor)) errores.Add("El régimen fiscal del receptor es obligatorio.");
                if (string.IsNullOrWhiteSpace(usoCfdi)) errores.Add("El uso de CFDI es obligatorio.");
                if (string.IsNullOrWhiteSpace(formaPago)) errores.Add("La forma de pago es obligatoria.");
                if (string.IsNullOrWhiteSpace(metodoPago)) errores.Add("El método de pago es obligatorio.");

                // Sólo el contado genera cobro, y el cobro necesita cuenta de destino. A
                // crédito no se pide porque no hay dinero que registrar todavía.
                if (esContado && string.IsNullOrWhiteSpace(cuentaBanco))
                    errores.Add("Seleccione la cuenta bancaria donde se registrará el cobro.");

                decimal.TryParse(fc["tipoCambio"].ToString(), out decimal tipoCambio);
                if (tipoCambio <= 0) tipoCambio = 1m;

                var conceptos = LeerConceptos(fc["conceptos"].ToString(), errores);

                if (errores.Count > 0)
                    return Json(new { success = false, step = "Validacion", error = string.Join("\n", errores) });

                // ── Totales recalculados en el servidor ─────────────────
                // Nunca se confía en los que manda la pantalla: se comparan y se avisa si
                // difieren, pero los que se guardan y se timbran son éstos.
                decimal subtotal = 0m, descuentoTotal = 0m, iva = 0m;
                foreach (var c in conceptos)
                {
                    subtotal += c.Bruto;
                    descuentoTotal += c.ImporteDescuento;
                    if (c.ObjetoImp == "02")
                        iva += Math.Round(c.Neto * TasaIva, 2);
                }
                decimal total = subtotal - descuentoTotal + iva;

                decimal.TryParse(fc["total"].ToString(), out decimal totalPantalla);
                if (totalPantalla > 0 && Math.Abs(total - totalPantalla) > ToleranciaTotales)
                    return Json(new
                    {
                        success = false,
                        step = "Validacion",
                        error = $"Los totales no coinciden. Pantalla: {totalPantalla:N2}, sistema: {total:N2}. " +
                                "Recargue la pantalla y capture nuevamente."
                    });

                // ── FASE 1: descripciones de catálogo para el CFDI ──────
                string mdpTexto = LeerCatalogo("SELECT descripcion FROM mdp WHERE cve_mdp = @cve",
                                               metodoPago, "método de pago", errores, conn, tx);
                string formaPagoTexto = LeerCatalogo("SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve",
                                               formaPago, "forma de pago", errores, conn, tx);
                string usoCfdiTexto = LeerCatalogo("SELECT descripcion FROM catusocfdi WHERE clave = @cve",
                                               usoCfdi, "uso de CFDI", errores, conn, tx);
                string regimenTexto = LeerCatalogo("SELECT descripcion FROM catregimenfiscal WHERE clave = @cve",
                                               regimenReceptor, "régimen fiscal del receptor", errores, conn, tx);

                if (errores.Count > 0)
                    return Json(new { success = false, step = "Validacion", error = string.Join("\n", errores) });

                // ── FASE 2: documento dentro de la transacción ──────────
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = AreaFacturacion,
                    IdTpDoc = TpDocFacturaLibre,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = NatFacturaLibre,
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    CliProv = rsoCliente,
                    // refe es lo que la póliza y la cartera usan para resolver el cliente:
                    // sin él, el asiento sale sin cuenta contable.
                    Ref = idCliente,
                    Ccy = moneda,
                    Par = tipoCambio,
                    Estatus = 11,
                    // `sub` va BRUTO, antes de descuento. La plantilla contable abona
                    // ingresos por `sub` y carga el descuento aparte, de modo que
                    // HABER (sub + IVA) = DEBE (total + descuento). Guardando aquí el neto
                    // la póliza quedaría descuadrada justo por el importe del descuento.
                    Sub = subtotal,
                    Imp = total,
                    Dto = descuentoTotal,
                    FPago = int.TryParse(formaPago, out int fp) ? fp : 0,
                    Mdp = metodoPago,
                    CFDI = usoCfdi,
                    Coment1 = observaciones,
                    TipoPoceso = esContado ? "factura_contado" : "factura_credito",
                    CentroCostos = AreaFacturacion
                };

                var partidas = conceptos.Select(c => new PartidaDocumento
                {
                    CveProd = string.IsNullOrWhiteSpace(c.CveProd) ? c.ClaveProdServ : c.CveProd,
                    DescrProd = c.Descripcion,
                    CantUd = c.Cantidad,
                    PvProd = c.PrecioUnit,
                    Dto1 = c.Descuento,
                    ImpPart = c.Neto,
                    Ud = string.IsNullOrWhiteSpace(c.Unidad) ? "SERVICIO" : c.Unidad
                }).ToList();

                var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                if (documento == null || !documento.ContainsKey("IdEncabezado"))
                {
                    tx.Rollback();
                    return Json(new
                    {
                        success = false,
                        step = "GuardarDocumento",
                        error = "No se pudo generar el documento ni su folio. No se timbró ningún comprobante."
                    });
                }

                documentoId = Convert.ToInt32(documento["IdEncabezado"]);
                string folioDocumento = documento["folio_generado"].ToString();

                RunUpdate(@"
                    INSERT INTO imp_oc
                        (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
                    VALUES
                        (@encabezado_id, @impuesto, @subtotal, @importe, 1, 16, @prov_nom, @f_pago_id)",
                    new Dictionary<string, object>
                    {
                        { "encabezado_id", documentoId },
                        { "impuesto",      Convert.ToInt32(GetSetting("impuesto")) },
                        { "subtotal",      subtotal - descuentoTotal },
                        { "importe",       iva },
                        { "prov_nom",      rsoCliente },
                        { "f_pago_id",     encabezado.FPago ?? 0 }
                    }, false, conn, tx);

                // ── FASE 3: contabilidad, aún dentro de la transacción ──
                var poliza = GenerarDatosPoliza(documentoId, cuentaBanco, null, conn, tx);
                var polizaRegistrada = RegistrarPolizas(
                    GetUserId(User.Identity.Name), documentoId, poliza, false, null, conn, tx);

                if (polizaRegistrada == null || polizaRegistrada.Count == 0)
                {
                    tx.Rollback();
                    return Json(new { success = false, step = "Poliza", error = "No se generó la póliza del documento." });
                }

                var cartera = RegistrarCartera(
                    polizaRegistrada[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

                // A crédito la cartera queda abierta con el plazo del cliente y el cobro se
                // aplica después desde Crédito y Cobranza. De contado se salda aquí mismo,
                // con su documento CXC propio.
                if (esContado)
                    RegistrarCobroConDocumentoCxc(documentoId, cartera.CarteraId, cuentaBanco, conn, tx);

                // ── FASE 4: armar el CFDI y timbrar ─────────────────────
                var factura = new Factura
                {
                    Serie = serie,
                    Folio = folioDocumento,
                    FolioCorto = folioDocumento,
                    EncabezadoId = documentoId,
                    IdCliente = idCliente,
                    RfcEmisor = Emisor("Rfc"),
                    RsoEmisor = Emisor("RazonSocial"),
                    Rege = Emisor("Regimen"),
                    RegFisE = Emisor("Regimen"),
                    CpE = Emisor("CpE"),
                    LugarExpedicion = Emisor("CpE"),
                    RfcCliente = rfcCliente,
                    RsoCliente = rsoCliente,
                    CpR = cpCliente,
                    Regc = regimenReceptor,
                    RegFisR = regimenReceptor,
                    regimenEText = regimenTexto,
                    IdUsoCFDI = usoCfdi,
                    CFDIText = usoCfdiTexto,
                    IdTipoPago = formaPago,
                    formaPagoTexto = formaPagoTexto,
                    metodoPagoTexto = metodoPago,
                    MdpFactura = mdpTexto,
                    Moneda = moneda,
                    TipoCambio = tipoCambio,
                    Subtotal = subtotal,
                    Descuento = descuentoTotal,
                    IVA = iva,
                    Total = total,
                    TipoDeComprobante = "I",
                    Observaciones = observaciones,
                    // Reutiliza el generador de CFDI de ingreso; no hace falta un tipo nuevo
                    // en XmlBuilderService porque el comprobante es idéntico al de una venta.
                    TipoFacturacion = esContado ? "contado" : "credito",
                    StatusFactura = "TIMBRADA",
                    Tproductos = ConstruirTablaProductos(conceptos)
                };

                timbrado = GenerarXml(factura, conn, tx);

                if (timbrado == null || !timbrado.Success)
                {
                    RegistrarRechazoPac(
                        timbrado?.Message ?? "El servicio de timbrado no respondio.",
                        "Facturacion/FacturaLibre");
                    tx.Rollback();
                    return Json(new
                    {
                        success = false,
                        step = "Timbrado",
                        error = timbrado?.Message ?? "El servicio de timbrado no respondió."
                    });
                }

                // ── FASE 5: commit ──────────────────────────────────────
                try
                {
                    tx.Commit();
                }
                catch (Exception exCommit)
                {
                    RegistrarErrorParaTicket(exCommit, "FacturaLibre/?");
                    // El CFDI ya vive en el SAT: no se puede deshacer con un rollback.
                    RegistrarCfdiHuerfano(timbrado.UUID, documentoId, exCommit.Message);

                    LogErrorHelper.RegistrarLog(LogTag, timbrado.UUID,
                        $"CRÍTICO: CFDI timbrado UUID={timbrado.UUID} pero el commit falló: {exCommit.Message}",
                        User.Identity?.Name, nivel: "CRITICAL");

                    return Json(new
                    {
                        success = false,
                        step = "Commit",
                        error = "El CFDI se timbró en el SAT pero no se pudo guardar en el sistema. " +
                                "Contacte a soporte con el UUID: " + timbrado.UUID
                    });
                }

                // Los correos van fuera de la transacción: ya se commiteó y si la consulta
                // falla no debe empañar una factura que ya está timbrada y guardada.
                var correosCliente = ObtenerCorreosDelCliente(idCliente);

                // La forma de la respuesta la dicta manejo-respuestas.js, que es quien pinta
                // el resumen, la descarga de PDF/XML y el envío por correo. Los nombres de
                // `resumen` y `detalles` son parte de ese contrato, no decoración.
                return Json(new
                {
                    success = true,
                    message = "Factura libre timbrada correctamente.",
                    resumen = new
                    {
                        documentosProcesados = 1,
                        tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                    },
                    detalles = new
                    {
                        uuid = timbrado.UUID,
                        serie = factura.Serie,
                        folio = factura.Folio,
                        fecha = factura.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                        rfcCliente = factura.RfcCliente,
                        razonSocial = factura.RsoCliente,
                        cantidadProductos = factura.Tproductos.Rows.Count,
                        subtotal = factura.Subtotal,
                        descuento = factura.Descuento,
                        iva = factura.IVA,
                        total = factura.Total,
                        condicion = esContado ? "Contado" : "Crédito",
                        // El resumen los muestra como identificadores de la cadena documental;
                        // aquí no hay pedido ni remisión, sólo el documento de la factura.
                        facturaId = documentoId,
                        carteraId = cartera?.CarteraId,
                        mensajeFinal = esContado
                            ? "Factura timbrada y cobrada. La cartera quedó saldada con su documento CXC."
                            : "Factura timbrada. La cartera queda abierta al plazo del cliente.",
                        rutaQr = factura.RutaQr,
                        pdfUrl = Url.Content($"~/Facturacion/facturas/{timbrado.UUID}.pdf"),
                        xmlUrl = Url.Content($"~/Facturacion/xml_timbrados/{timbrado.UUID}.xml"),
                        correosCliente
                    }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturaLibre/?");
                // Defensivo: si el commit ya pasó, Npgsql lanza aquí y se ignora.
                try { tx.Rollback(); } catch { }

                LogErrorHelper.RegistrarLog(LogTag, documentoId.ToString(),
                    $"Error al generar factura libre: {ex}", User.Identity?.Name);

                return Json(new { success = false, step = "Excepcion", error = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Apoyo
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Correos registrados del cliente, en la forma que espera el modal de envío de
        /// manejo-respuestas.js. Si la consulta falla no se propaga: la factura ya está
        /// timbrada y el usuario siempre puede capturar el correo a mano en el modal.
        /// </summary>
        [NonAction]
        private List<object> ObtenerCorreosDelCliente(int idCliente)
        {
            try
            {
                return RunQuery(@"
                    SELECT id_correo_cli, correo
                    FROM correos_cliente
                    WHERE cliente_id = @cliente AND correo IS NOT NULL
                    ORDER BY id_correo_cli",
                    new Dictionary<string, object> { { "cliente", idCliente } })
                    .Select(c => (object)new
                    {
                        id = c["id_correo_cli"],
                        correo = c["correo"]?.ToString()
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturaLibre/ObtenerCorreosDelCliente");
                LogErrorHelper.RegistrarLog(LogTag, idCliente.ToString(),
                    $"No se pudieron leer los correos del cliente {idCliente}: {ex.Message}",
                    User.Identity?.Name, nivel: "WARN");

                return new List<object>();
            }
        }

        /// <summary>Deserializa y valida los conceptos capturados.</summary>
        [NonAction]
        private List<ConceptoFacturaLibre> LeerConceptos(string json, List<string> errores)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                errores.Add("Debe capturar al menos un concepto.");
                return new List<ConceptoFacturaLibre>();
            }

            List<ConceptoFacturaLibre> conceptos;
            try
            {
                conceptos = JsonConvert.DeserializeObject<List<ConceptoFacturaLibre>>(json)
                            ?? new List<ConceptoFacturaLibre>();
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturaLibre/LeerConceptos");
                errores.Add("El detalle de conceptos no es válido: " + ex.Message);
                return new List<ConceptoFacturaLibre>();
            }

            if (conceptos.Count == 0)
            {
                errores.Add("Debe capturar al menos un concepto.");
                return conceptos;
            }

            for (int i = 0; i < conceptos.Count; i++)
            {
                var c = conceptos[i];
                int n = i + 1;

                if (string.IsNullOrWhiteSpace(c.Descripcion)) errores.Add($"Concepto {n}: la descripción es obligatoria.");
                if (string.IsNullOrWhiteSpace(c.ClaveProdServ)) errores.Add($"Concepto {n}: la clave de producto/servicio del SAT es obligatoria.");
                if (string.IsNullOrWhiteSpace(c.ClaveUnidad)) errores.Add($"Concepto {n}: la clave de unidad del SAT es obligatoria.");
                if (string.IsNullOrWhiteSpace(c.ObjetoImp)) errores.Add($"Concepto {n}: el objeto de impuesto es obligatorio.");
                if (c.Cantidad <= 0) errores.Add($"Concepto {n}: la cantidad debe ser mayor a cero.");
                if (c.PrecioUnit <= 0) errores.Add($"Concepto {n}: el precio unitario debe ser mayor a cero.");
                if (c.Descuento < 0 || c.Descuento >= 100) errores.Add($"Concepto {n}: el descuento debe estar entre 0 y 99.99 por ciento.");
            }

            return conceptos;
        }

        /// <summary>Lee una descripción de catálogo y acumula el error si la clave no existe.</summary>
        [NonAction]
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

        /// <summary>DataTable de conceptos con el esquema que consume XmlBuilderService.</summary>
        [NonAction]
        private static DataTable ConstruirTablaProductos(List<ConceptoFacturaLibre> conceptos)
        {
            var tabla = new DataTable();
            tabla.Columns.AddRange(new[]
            {
                new DataColumn("numero",        typeof(string)),
                new DataColumn("claveProdServ", typeof(string)),
                new DataColumn("claveUnidad",   typeof(string)),
                new DataColumn("unidad",        typeof(string)),
                new DataColumn("descripcion",   typeof(string)),
                new DataColumn("cantidad",      typeof(double)),
                new DataColumn("precioUnit",    typeof(double)),
                new DataColumn("importe",       typeof(double)),
                new DataColumn("objetoImp",     typeof(string)),
                new DataColumn("descuento",     typeof(double)),
                new DataColumn("comentario",    typeof(string))
            });

            foreach (var c in conceptos)
            {
                tabla.Rows.Add(
                    c.CveProd ?? string.Empty,
                    c.ClaveProdServ,
                    c.ClaveUnidad,
                    string.IsNullOrWhiteSpace(c.Unidad) ? "SERVICIO" : c.Unidad,
                    c.Descripcion,
                    (double)c.Cantidad,
                    (double)c.PrecioUnit,
                    (double)c.Bruto,
                    c.ObjetoImp,
                    (double)c.Descuento,
                    string.Empty
                );
            }

            return tabla;
        }

        /// <summary>
        /// Deja constancia de un CFDI timbrado que no se pudo guardar. Va por conexión
        /// propia: la transacción original ya no sirve después del fallo.
        /// </summary>
        [NonAction]
        private void RegistrarCfdiHuerfano(string uuid, int idEncabezado, string errorCommit)
        {
            try
            {
                using var conn = AbrirConexion();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO cfdi_huerfanos
                        (uuid, encabezado_id, error_commit, fecha_registro, resuelto)
                    VALUES (@uuid, @enc, @error, NOW(), false)
                    ON CONFLICT (uuid) DO NOTHING;";
                cmd.Parameters.AddWithValue("uuid", uuid);
                cmd.Parameters.AddWithValue("enc", idEncabezado);
                cmd.Parameters.AddWithValue("error", errorCommit);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, uuid,
                    $"No se pudo registrar el CFDI huérfano UUID={uuid}: {ex.Message}", nivel: "CRITICAL");
            }
        }
    }
}
