using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Npgsql;
using System.Data;

namespace BOS_ERP.Controllers.Facturacion.Productos
{
    /// <summary>
    /// Pipeline completo de facturación de Ventas, compartido por los canales que lo usan
    /// (hoy Industriales y Sucursales).
    ///
    /// Antes vivía entero dentro de VIFacturaController y Sucursales tenía su propia versión
    /// —más vieja— sin facturación parcial ni aplicación de anticipos. Lo único que cambia
    /// entre canales es la identidad del documento (área, tipo de documento, nat y serie),
    /// así que eso son las cuatro propiedades abstractas de abajo y el resto es idéntico.
    ///
    /// El orden del proceso importa y es el mismo para todos:
    ///   1A. Guardar encabezado + partidas         (BD, dentro de la transacción)
    ///   1B. Preparar datos del CFDI y anticipos   (BD)
    ///   2a. Timbrar la factura                    (PAC — si falla, rollback)
    ///   2b. Timbrar la NC por anticipos aplicados (PAC)
    ///   3.  Commit inmediato tras el timbrado
    ///   4.  Respuesta al front, ya fuera de la transacción
    /// </summary>
    public abstract class VentasFacturaBaseController : FacturacionVentaController
    {
        protected readonly IConfiguration _configuration;
        protected readonly BOS_ERP.Services.EmailSender _emailSender;
        protected readonly BOS_ERP.Helpers.CorreoHelper correoHelper;
        protected readonly IWebHostEnvironment _env;
        protected readonly IRazorViewEngine _viewEngine;
        protected readonly ITempDataProvider _tempDataProvider;
        protected readonly XmlBuilderService _xmlService;

        protected VentasFacturaBaseController(
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            IConfiguration configuration,
            BOS_ERP.Services.EmailSender emailSender,
            BOS_ERP.Helpers.CorreoHelper correoHelperService,
            XmlBuilderService xmlService)
            : base(timbradoOptions, viewEngine, tempDataProvider, env, xmlService)
        {
            _configuration = configuration;
            _emailSender = emailSender;
            // Antes se declaraba el campo pero nunca se asignaba: EnviarAlertaSAT reventaba
            // con NullReferenceException en cuanto alguien pulsaba "Validar SAT".
            correoHelper = correoHelperService;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _env = env;
            _xmlService = xmlService;
        }

        // ──────────────────────────────────────────────────────────────
        // Identidad del documento por canal
        // ──────────────────────────────────────────────────────────────

        /// <summary>Área del documento (encabezadomov.idarea).</summary>
        protected abstract int FacturaIdArea { get; }

        /// <summary>Tipo de documento (cat_tp_doc) con el que se numera el folio.</summary>
        protected abstract int FacturaIdTpDoc { get; }

        /// <summary>nat del encabezado: VIFAC, VSFAC…</summary>
        protected abstract string FacturaTpMov { get; }

        /// <summary>Serie del CFDI cuando el front no manda una.</summary>
        protected abstract string FacturaSerie { get; }

        // ============================================================
        // PUNTO DE ENTRADA PRINCIPAL
        // Orquesta: BD → Timbrado → Commit inmediato
        // ============================================================
        protected async Task<JsonResult> ProcesarFacturaAsync(IFormCollection IFormCollection)
        {
            // IFormCollection es readonly → trabajamos con un diccionario mutable
            var fc = IFormCollection.ToDictionary(x => x.Key, x => x.Value.ToString());

            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            var factura = new Factura();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        // ═══════════════════════════════════════════════
                        // FASE 1A — Guardar encabezado + partidas en BD
                        // ═══════════════════════════════════════════════
                        var resultado1 = await Guardar_ConRemisiones(fc, conn, tx);
                        if (!resultado1.Success)
                            return Json(new
                            {
                                success = false,
                                step = "GuardarFactura",
                                error = resultado1.Message
                            });

                        dynamic data = resultado1.Data;
                        int idEncabezado = data.IdEncabezado;
                        fc["enc_id"] = idEncabezado.ToString();

                        // ═══════════════════════════════════════════════
                        // FASE 1B — Preparar todos los datos para timbrar
                        // ═══════════════════════════════════════════════
                        var prepResult = await PrepararDatosParaTimbrar(fc, factura, conn, tx);
                        if (!prepResult.Success)
                            return Json(new { success = false, step = "PrepararDatos", error = prepResult.Message });

                        OperacionesAnticiposContext anticiposCtx = prepResult.AnticiposCtx;

                        // ═══════════════════════════════════════════════
                        // FASE 2a — Timbrar FACTURA
                        // ═══════════════════════════════════════════════
                        TimbradoResult resultadoTimbrado = EjecutarTimbrado(factura);

                        if (!resultadoTimbrado.Success)
                        {
                            RegistrarRechazoPac(resultadoTimbrado.Message, "Facturacion/Timbrado");
                            tx.Rollback();
                            return Json(new { success = false, step = "Timbrado", error = resultadoTimbrado.Message });
                        }

                        // ═══════════════════════════════════════════════
                        // FASE 2b — Timbrar NOTA DE CRÉDITO por anticipos
                        // ═══════════════════════════════════════════════
                        TimbradoResult resultadoNC = new TimbradoResult { Success = true };

                        if (anticiposCtx?.NotaCreditoData != null)
                        {
                            var ncFactura = ConstruirFacturaParaNC(
                                anticiposCtx.NotaCreditoData,
                                factura,
                                resultadoTimbrado.UUID,
                                fc,
                                conn,
                                tx);

                            var ncController = new NotaCreditoController(
                                                _configuration,
                                                _emailSender,
                                                _env,
                                                _viewEngine,
                                                _tempDataProvider,
                                                _xmlService);
                            ncController.ControllerContext = this.ControllerContext;

                            resultadoNC = ncController.GenerarXml(ncFactura);

                            if (!resultadoNC.Success)
                            {
                                RegistrarCFDIHuerfano(resultadoTimbrado.UUID, idEncabezado,
                                    $"NC de anticipo falló: {resultadoNC.Message}");

                                LogErrorHelper.RegistrarLog(FacturaLogTag, idEncabezado.ToString(),
                                    $"CRÍTICO: Factura timbrada UUID={resultadoTimbrado.UUID} " +
                                    $"pero NC de anticipo falló: {resultadoNC.Message}",
                                    nivel: "CRITICAL");

                                tx.Rollback();
                                return Json(new
                                {
                                    success = false,
                                    step = "TimbradoNC",
                                    error = "La factura fue timbrada pero no se pudo generar la Nota de Crédito " +
                                            "del anticipo. Contacte a soporte con el UUID de la factura: " +
                                            resultadoTimbrado.UUID
                                });
                            }
                        }

                        // ═══════════════════════════════════════════════
                        // FASE 3 — COMMIT INMEDIATO tras timbrado exitoso
                        // ═══════════════════════════════════════════════
                        try
                        {
                            if (resultadoNC.UUID != null)
                                fc["uuid_nc_anticipo"] = resultadoNC.UUID;

                            if (anticiposCtx?.Operaciones != null && anticiposCtx.Operaciones.Count > 0)
                                AplicarSaldosAnticipos(anticiposCtx, fc, conn, tx);

                            MarcarDocumentosPadre(fc, conn, tx);

                            tx.Commit();
                        }
                        catch (Exception exCommit)
                        {
                            RegistrarErrorParaTicket(exCommit, "VentasFacturaBase/ProcesarFacturaAsync");
                            RegistrarCFDIHuerfano(resultadoTimbrado.UUID, idEncabezado, exCommit.Message);

                            LogErrorHelper.RegistrarLog(FacturaLogTag, idEncabezado.ToString(),
                                $"CRÍTICO: CFDI timbrado UUID={resultadoTimbrado.UUID} " +
                                $"pero commit falló: {exCommit.Message}",
                                nivel: "CRITICAL");

                            return Json(new
                            {
                                success = false,
                                step = "Commit",
                                error = "El CFDI fue timbrado en el SAT pero no se pudo guardar en el " +
                                        "sistema. Contacte a soporte con el UUID: " + resultadoTimbrado.UUID
                            });
                        }

                        // ═══════════════════════════════════════════════
                        // FASE 4 — Post-procesado FUERA de transacción
                        // ═══════════════════════════════════════════════
                        var datosFactura = ConstruirRespuestaFactura(resultadoTimbrado, factura, fc);

                        return Json(new
                        {
                            success = true,
                            message = "Factura procesada y timbrada correctamente",
                            resumen = new
                            {
                                tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                            },
                            detalles = new
                            {
                                pedidoId = resultado1.Data,
                                uuid = datosFactura["UUID"],
                                uuidNotaCredito = resultadoNC?.UUID,
                                total = datosFactura["Total"],
                                subtotal = datosFactura["Subtotal"],
                                iva = datosFactura["IVA"],
                                rfcCliente = datosFactura["RFCCliente"],
                                razonSocial = datosFactura["RazonSocialCliente"],
                                serie = datosFactura["Serie"],
                                folio = datosFactura["Folio"],
                                fecha = datosFactura["Fecha"],
                                cantidadProductos = datosFactura["CantidadProductos"],
                                pdfUrl = datosFactura["PdfUrl"],
                                xmlUrl = datosFactura["XmlUrl"],
                                correosCliente = datosFactura["CorreosCliente"]
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "VentasFacturaBase/?");
                        tx.Rollback();
                        LogErrorHelper.RegistrarLog(FacturaLogTag, "ProcesarDocumentosAsync",
                            $"Error en transacción: {ex.Message}", nivel: "ERROR");
                        return Json(new
                        {
                            success = false,
                            message = "Error en el proceso: " + ex.Message,
                            detalles = ex.StackTrace
                        });
                    }
                }
            }
        }
        // ============================================================
        // FASE 1A — Guardar encabezado + partidas (con soporte remisiones)
        // ============================================================
        protected async Task<(bool Success, string Message, object Data)> Guardar_ConRemisiones(
            Dictionary<string, string> fc, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            try
            {
                // ── 1. Leer y parsear el JSON de partidas de remisiones ──────
                List<RemisionPartidaParaFacturar> partidasRemision = null;
                List<int> remisionIds = new List<int>();

                if (fc.TryGetValue("remisionesParcialesJSON", out string remParcialesJson)
                    && !string.IsNullOrWhiteSpace(remParcialesJson))
                {
                    try
                    {
                        partidasRemision = JsonConvert
                            .DeserializeObject<List<RemisionPartidaParaFacturar>>(remParcialesJson);
                    }
                    catch (Exception exJson)
                    {
                        throw new Exception(
                            "El detalle de remisiones enviado no es válido: " + exJson.Message);
                    }
                }

                if (fc.TryGetValue("remisionesIds", out string remIdsStr)
                    && !string.IsNullOrWhiteSpace(remIdsStr))
                {
                    remisionIds = remIdsStr
                        .Split(',')
                        .Where(s => int.TryParse(s.Trim(), out _))
                        .Select(s => int.Parse(s.Trim()))
                        .ToList();
                }

                bool tieneRemisiones = partidasRemision != null && partidasRemision.Count > 0;

                if (tieneRemisiones)
                {
                    // Toda remisión referenciada en el detalle debe estar en remisionesIds,
                    // de lo contrario su tracking nunca se inicializa.
                    remisionIds = remisionIds
                        .Union(partidasRemision.Select(p => p.RemisionId))
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();

                    ValidarDetalleRemisiones(partidasRemision, conn, tx);
                }

                // ── 2. Validaciones básicas ──────────────────────────────────
                string tipo = fc.GetValueOrDefault("tipo", "").ToLower();

                if (!fc.TryGetValue("cliente", out string cliente) || string.IsNullOrWhiteSpace(cliente))
                    throw new Exception("Debe seleccionar un cliente.");
                if (!fc.TryGetValue("moneda", out string moneda) || string.IsNullOrWhiteSpace(moneda))
                    throw new Exception("Debe seleccionar una moneda.");
                if (!fc.TryGetValue("vendedor", out string vendedor) || string.IsNullOrWhiteSpace(vendedor))
                    throw new Exception("Debe seleccionar un vendedor.");
                if (!fc.TryGetValue("forma-pago", out string formaPago) || string.IsNullOrWhiteSpace(formaPago))
                    throw new Exception("Debe seleccionar una forma de pago.");

                // ── 3. Construir lista de productos ──────────────────────────
                // IMPORTANTE: el timbrado (PrepararDatosParaTimbrar) siempre arma el CFDI
                // desde productosJSON. Si aquí guardáramos una lista distinta, partidasdoc
                // y el CFDI quedarían desalineados. Por eso productosJSON manda siempre y
                // el detalle de remisiones solo se usa para el tracking de saldos.
                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
                if (tipo != "anticipo")
                {
                    fc.TryGetValue("productosJSON", out string prodJson);

                    if (!string.IsNullOrWhiteSpace(prodJson))
                        productos = DeserializarProductosJSON(prodJson);

                    if ((productos == null || productos.Count == 0) && tieneRemisiones)
                        productos = ConvertirPartidasRemisionAProductos(partidasRemision, conn, tx);

                    if (productos == null || productos.Count == 0)
                        throw new Exception("Debe agregar al menos un producto o seleccionar remisiones.");

                    // Reglas de precio. La factura no tiene overlay con contraseña sobre los
                    // campos, pero sí manda los tokens de permiso directo del usuario: quien
                    // esté autorizado puede facturar bajo el piso y el resto no. Sin esto la
                    // factura podía emitirse por debajo del mínimo que la remisión sí respetó.
                    TokenStore.LimpiarExpirados();
                    string usuarioReglas = User.Identity.Name;
                    fc.TryGetValue("descuentoToken", out string descuentoToken);
                    fc.TryGetValue("precioToken", out string precioToken);
                    int empresaReglas = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                    var reglas = this.ValidarPartidas(
                        empresaReglas,
                        this.ResolverClienteId(cliente, empresaReglas, conn, tx),
                        productos,
                        TokenStore.Validar(precioToken ?? "", usuarioReglas, "CAMBIO DE PRECIO"),
                        TokenStore.Validar(descuentoToken ?? "", usuarioReglas, "DESCUENTO"),
                        conn, tx);

                    if (!reglas.Permitido)
                        throw new Exception(reglas.Mensaje);
                }

                // ── 4. Validar fecha de pago según tipo ──────────────────────
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (!fc.TryGetValue("fechaPago", out string fechaPagoStr) || string.IsNullOrWhiteSpace(fechaPagoStr))
                        throw new Exception("Debe ingresar la fecha de pago.");
                    if (!DateTime.TryParse(fechaPagoStr, out DateTime fch))
                        throw new Exception("Formato de fecha de pago no válido.");
                    fechaPago = fch;
                }
                else if (tipo == "anticipo")
                {
                    if (!fc.TryGetValue("fecha-anticipo", out string fechaAntStr) || string.IsNullOrWhiteSpace(fechaAntStr))
                        throw new Exception("Debe ingresar la fecha del anticipo.");
                    if (!DateTime.TryParse(fechaAntStr, out DateTime fch))
                        throw new Exception("Formato de fecha del anticipo no válido.");
                    fechaPago = fch;
                }

                // ── 4.1 Validar crédito — solo aplica cuando la venta es a crédito ──
                int docIdCredito = fc.TryGetValue("documentid", out string docIdCreditoStr)
                                   && int.TryParse(docIdCreditoStr, out int parsedDocCredito)
                                        ? parsedDocCredito : 0;

                fc.TryGetValue("creditoToken", out string creditoTokenFac);

                var credito = this.ValidarCreditoVenta(
                    cliente,
                    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    CreditoVentasHelper.TotalDocumentoParaCredito(fc),
                    tipo,
                    docIdCredito,
                    conn, tx, creditoTokenFac);

                if (!credito.Permitido)
                    throw new Exception(credito.Mensaje);

                // ── 5. Recuperar datos del encabezado padre (si aplica) ──────
                int idEncabezadoPadre = 0;
                int usrId0 = 0, usrId1 = 0, usrId2 = 0;
                DateTime usrFch0 = DateTime.Now, usrFch1 = DateTime.Now, usrFch2 = DateTime.Now;

                if (fc.TryGetValue("documentid", out string documentIdStr)
                    && !string.IsNullOrWhiteSpace(documentIdStr)
                    && documentIdStr != "0")
                {
                    var usrParameter = new Dictionary<string, object> { { "id", Convert.ToInt32(documentIdStr) } };
                    string usrquery = @"SELECT usr0, fch0, usr1, fch1, usr2, fch2
                                FROM encabezadomov WHERE id_encabezado = @id";
                    var result = RunQuery(usrquery, usrParameter, false, conn, tx);
                    if (result.Count > 0)
                    {
                        usrId0 = Convert.ToInt32(result[0]["usr0"]);
                        usrFch0 = Convert.ToDateTime(result[0]["fch0"]);
                        usrId1 = Convert.ToInt32(result[0]["usr1"]);
                        usrFch1 = Convert.ToDateTime(result[0]["fch1"]);
                        usrId2 = Convert.ToInt32(result[0]["usr2"]);
                        usrFch2 = Convert.ToDateTime(result[0]["fch2"]);
                        idEncabezadoPadre = Convert.ToInt32(documentIdStr);
                    }
                }
                else if (remisionIds.Count > 0)
                {
                    idEncabezadoPadre = remisionIds.First();
                }

                // ── 6. Obtener ID y nombre del cliente ───────────────────────
                int idCliente = 0;
                string clienteNombre = "";
                var clienteParameter = new Dictionary<string, object>
                {
                    { "cliente",    fc.GetValueOrDefault("cliente", "") },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };
                string clientequery = @"SELECT id_cliente, n_cli FROM catclientes
                                WHERE cve_cli = @cliente AND empresa_id = @empresa_id";
                var clienteResult = RunQuery(clientequery, clienteParameter, false, conn, tx);
                if (clienteResult.Count > 0)
                {
                    idCliente = Convert.ToInt32(clienteResult[0]["id_cliente"]);
                    clienteNombre = clienteResult[0]["n_cli"].ToString();
                }

                // ── 7. Obtener id de forma de pago ───────────────────────────
                var fPagoParameter = new Dictionary<string, object> { { "cve_sat", fc.GetValueOrDefault("forma-pago", "") } };
                int fpId = Convert.ToInt32(
                    RunScalar("SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat", fPagoParameter));

                // ── 8. Construir encabezado ──────────────────────────────────
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = FacturaIdArea,
                    IdTpDoc = FacturaIdTpDoc,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = fc.GetValueOrDefault("almacen", ""),
                    Fch = DateTime.Now,
                    TpMov = FacturaTpMov,
                    ComentAut = fc.GetValueOrDefault("comentarios", ""),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = usrId0,
                    Fch0 = usrFch0,
                    Usr1 = usrId1,
                    Fch1 = usrFch1,
                    Usr2 = usrId2,
                    Fch2 = usrFch2,
                    Usr3 = GetUserId(User.Identity.Name),
                    Fch3 = DateTime.Now,
                    Imp = Convert.ToDecimal(fc.GetValueOrDefault("total", "0")),
                    Dto = Convert.ToDecimal(fc.GetValueOrDefault("descuento", "0")),
                    Sub = Convert.ToDecimal(fc.GetValueOrDefault("subtotal1", "0")),
                    CliProv = fc.GetValueOrDefault("cliente", ""),
                    Ref = idCliente,
                    Ccy = fc.GetValueOrDefault("moneda", ""),
                    Estatus = 11,
                    Flete = Convert.ToDecimal(fc.GetValueOrDefault("flete", "0")),
                    VdrCpr = fc.GetValueOrDefault("vendedor", ""),
                    Coment1 = fc.GetValueOrDefault("concepto", ""),
                    EncabezadoPadre = idEncabezadoPadre,
                    PlDias = string.IsNullOrWhiteSpace(fc.GetValueOrDefault("plazo", ""))
                                        ? 0
                                        : Convert.ToInt32(fc["plazo"].ToString()),
                    FchPgEntrega = fechaPago ?? DateTime.Now,
                    Par = Convert.ToDecimal(fc.GetValueOrDefault("paridad", "1")),
                    FPago = fpId,
                    Mdp = fc.GetValueOrDefault("metodo-pago", ""),
                    TipoPoceso = "factura_" + tipo,
                    CFDI = fc.GetValueOrDefault("uso-cfdi", ""),
                    CentroCostos = Convert.ToInt32(fc.GetValueOrDefault("CentroCostosId", "0")),
                    NatDocPadreChar = fc.GetValueOrDefault("ordenCompra", ""),
                };

                // ── 9. Construir partidas ────────────────────────────────────
                var partidas = new List<PartidaDocumento>();
                foreach (var p in productos)
                {
                    var pParam = new Dictionary<string, object>
                    {
                        { "cve_prod",   p.ContainsKey("productoId") ? p["productoId"] : "" },
                        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                    };
                    int productId = Convert.ToInt32(
                        RunScalar("SELECT id_catproductos FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id",
                                  pParam, false, conn, tx));

                    decimal cantP = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0;
                    decimal preP = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0;
                    decimal dtoP = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0;

                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = cantP,
                        PvProd = preP,
                        Dto1 = dtoP,
                        ImpPart = Math.Round(cantP * preP * (1 - dtoP / 100m), 2),
                        Ud = p.ContainsKey("unidad") ? p["unidad"] : "PZA",
                        IdProducto = productId,
                        TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                    });
                }

                // ── 10. Guardar documento principal ──────────────────────────
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                int idNuevaFactura = Convert.ToInt32(folio["IdEncabezado"]);

                // ── 11. Registrar impuestos ───────────────────────────────────
                string insImpuestos = @"
                    INSERT INTO imp_oc
                        (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
                    VALUES
                        (@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id)";
                var impParam = new Dictionary<string, object>
                {
                    { "encabezado_id", idNuevaFactura },
                    { "impuesto_id",   Convert.ToInt32(GetSetting("impuesto")) },
                    { "subtotal",      Convert.ToDecimal(fc.GetValueOrDefault("subtotal1", "0")) },
                    { "importe",       Convert.ToDecimal(fc.GetValueOrDefault("iva", "0")) },
                    { "orden_apl",     1 },
                    { "imp_variable",  16 },
                    { "prov_nom",      clienteNombre },
                    { "f_pago_id",     fpId }
                };
                RunQuery(insImpuestos, impParam, false, conn, tx);

                // ── 12. Actualizar cantidades facturadas en remisiones ────────
                if (tieneRemisiones)
                {
                    ActualizarPartidasRemisionesFacturadas(
                        idNuevaFactura, partidasRemision, remisionIds, conn, tx);
                }
                else if (remisionIds.Count > 0)
                {
                    LogErrorHelper.RegistrarLog(FacturaLogTag, idNuevaFactura.ToString(),
                        $"Facturación completa desde modal Documentos para remisiones: {string.Join(",", remisionIds)}",
                        nivel: "INFO");
                    MarcarRemisionesComoFacturadas(remisionIds, idNuevaFactura, conn, tx);
                }

                // Cerrar el documento fuente a 11 SOLO cuando la factura NO proviene del flujo de
                // remisiones (Especiales). En "Buscar Documentos" (documentid) se factura el documento
                // completo → se cierra a 11. En el flujo de remisiones, el estatus (11 completa /
                // 41 parcial) ya lo fijó ActualizarEstatusRemisionSiCompleta según el tracking.
                if (remisionIds.Count == 0 && idEncabezadoPadre > 0)
                {
                    // Esta rama tampoco dejaba rastro del origen: 42 facturas quedaron sin
                    // saber de qué remisión salieron y, peor, sin sembrar los saldos, de modo
                    // que la misma remisión podía volver a facturarse desde el modal como si
                    // estuviera intacta. Sólo aplica si el padre es una remisión.
                    RegistrarOrigenSiEsRemision(idNuevaFactura, idEncabezadoPadre, conn, tx);

                    var cerrarPadreParam = new Dictionary<string, object> { { "id", idEncabezadoPadre } };
                    RunUpdate("UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id",
                              cerrarPadreParam, false, conn, tx);
                }

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = idNuevaFactura,
                    Folio = folio["folio_generado"].ToString()
                };

                return (true, "Documento guardado correctamente", new
                {
                    Folio = folio["folio_generado"].ToString(),
                    IdEncabezado = idNuevaFactura
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentasFacturaBase/?");
                return (false, "No se pudo guardar la factura.", null);
            }
        }
        // ============================================================
        // FASE 1B — Preparar todos los datos para timbrar
        // ============================================================
        private async Task<(bool Success, string Message, OperacionesAnticiposContext AnticiposCtx)>
            PrepararDatosParaTimbrar(
                Dictionary<string, string> fc,
                Factura factura,
                NpgsqlConnection conn,
                NpgsqlTransaction tx)
        {
            var anticiposCtx = new OperacionesAnticiposContext();

            try
            {
                // ── Leer perfil de emisor desde IConfiguration ────────────────
                // En appsettings.json: "emisores": { "produccion": { "Rfc": "...", ... } }
                string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                    ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                    : "pruebas";

                string GetEmisor(string campo) =>
                    _configuration[$"emisores:{perfil}:{campo}"] ?? "";

                string tipo = fc.GetValueOrDefault("tipo", "").ToLower();

                LogErrorHelper.RegistrarLog(FacturaLogTag, "SIN_FOLIO",
                    "Ingreso a PrepararDatosParaTimbrar", nivel: "DEBUG");

                // ── Validaciones básicas ──────────────────────────────────────
                if (!fc.TryGetValue("rfc", out string rfcVal) || string.IsNullOrWhiteSpace(rfcVal))
                    throw new Exception("Debe seleccionar un cliente.");
                if (!fc.TryGetValue("moneda", out string _) || string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    throw new Exception("Debe seleccionar una moneda.");
                if (!fc.TryGetValue("forma-pago", out string _) || string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    throw new Exception("Debe seleccionar una forma de pago.");
                if (!fc.TryGetValue("metodo-pago", out string _) || string.IsNullOrWhiteSpace(fc["metodo-pago"].ToString()))
                    throw new Exception("Debe seleccionar un método de pago.");

                // ── Productos y anticipos ─────────────────────────────────────
                List<dynamic> productos = new List<dynamic>();
                List<dynamic> anticipo = new List<dynamic>();

                if (tipo != "anticipo")
                {
                    if (!fc.TryGetValue("productosJSON", out string prodJson) || string.IsNullOrWhiteSpace(prodJson))
                        throw new Exception("Debe agregar al menos un producto.");

                    productos = JsonConvert.DeserializeObject<List<dynamic>>(prodJson);
                    if (productos == null || productos.Count == 0)
                        throw new Exception("Debe agregar al menos un producto.");
                }

                if (tipo == "contado"
                    && fc.TryGetValue("anticiposJSON", out string antJson)
                    && !string.IsNullOrWhiteSpace(antJson))
                {
                    anticipo = JsonConvert.DeserializeObject<List<dynamic>>(antJson);
                }

                // ── Validar fecha de pago / anticipo ──────────────────────────
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (!fc.TryGetValue("fechaPago", out string fpStr) || string.IsNullOrWhiteSpace(fpStr))
                        throw new Exception("Debe ingresar la fecha de pago (solo para crédito).");
                    if (!DateTime.TryParse(fpStr, out DateTime fch))
                        throw new Exception("Formato de fecha de pago no válido.");
                    fechaPago = fch;
                }
                else if (tipo == "anticipo")
                {
                    if (!fc.TryGetValue("fecha-anticipo", out string faStr) || string.IsNullOrWhiteSpace(faStr))
                        throw new Exception("Debe ingresar la fecha del anticipo.");
                    if (!DateTime.TryParse(faStr, out DateTime fch))
                        throw new Exception("Formato de fecha del anticipo no válido.");
                    fechaPago = fch;
                }

                // ── Consultas de catálogos ────────────────────────────────────
                var parameters = new Dictionary<string, object>();

                parameters["cve"] = fc.GetValueOrDefault("metodo-pago", "");
                string mdp = RunScalar("SELECT descripcion FROM mdp WHERE cve_mdp = @cve;",
                                        parameters, false, conn, tx)?.ToString() ?? "";

                string mdp1 = RunScalar("SELECT cve_mdp FROM mdp WHERE cve_mdp = @cve;",
                                        parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                parameters["cve"] = fc.GetValueOrDefault("forma-pago", "");
                string tp = RunScalar("SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve;",
                                      parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                parameters["cve"] = fc.GetValueOrDefault("uso-cfdi", "");
                string usoCFDItext = RunScalar("SELECT descripcion FROM catusocfdi WHERE clave = @cve;",
                                               parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                parameters["cve"] = fc.GetValueOrDefault("RegimenFiscalReceptor", "");
                string regimenText = RunScalar("SELECT descripcion FROM catregimenfiscal WHERE clave = @cve;",
                                               parameters, false, conn, tx)?.ToString()
                                               ?? "General de Ley Personas Morales";
                parameters.Clear();

                parameters["cve_cli"] = fc.GetValueOrDefault("cliente", "");
                parameters["empresa_id"] = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                var datosCliente = RunQuery(
                    "SELECT id_cliente, n_cli, cp, rfc FROM catclientes WHERE cve_cli = @cve_cli AND empresa_id = @empresa_id;",
                    parameters, false, conn, tx);

                if (datosCliente.Count == 0)
                {
                    LogErrorHelper.RegistrarLog(FacturaLogTag, "SIN_FOLIO",
                        "No se encontraron datos del cliente para generar la factura.", nivel: "ERROR");
                    throw new Exception("No se encontraron los datos del cliente para generar la factura.");
                }

                string rScocial = RunScalar(
                    "SELECT regimen_fiscal FROM direcciones_facturacion WHERE entidad_clave = @cve_cli;",
                    parameters, false, conn, tx)?.ToString() ?? "601";

                parameters["cliente_id"] = datosCliente[0]["id_cliente"];
                var datosCorreosClientes = RunQuery(
                    "SELECT id_correo_cli, cliente_id, correo FROM correos_cliente WHERE cliente_id = @cliente_id;",
                    parameters, false, conn, tx);

                // ── Moneda ────────────────────────────────────────────────────
                string monedaSAT = fc.GetValueOrDefault("moneda", "") switch
                {
                    "PESOS" => "MXN",
                    "DLLS" => "USD",
                    "EURO" => "EUR",
                    var m => m
                };

                decimal flete = Convert.ToDecimal(fc.GetValueOrDefault("flete", "0"));

                // ── Folio del encabezado ──────────────────────────────────────
                var encParam = new Dictionary<string, object>
                {
                    { "encabezado", Convert.ToInt32(fc.GetValueOrDefault("enc_id", "0")) }
                };
                var folioResult = RunScalar(
                    @"SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio
                      FROM encabezadomov em WHERE id_encabezado = @encabezado",
                    encParam, false, conn, tx);
                if (folioResult == null)
                    throw new Exception("Folio no encontrado para el encabezado.");

                // ── Addenda ───────────────────────────────────────────────────
                Addenda addenda = null;
                var pAdd = new Dictionary<string, object>
                {
                    { "id_addenda", Convert.ToInt32(fc.GetValueOrDefault("idAdenda", "0")) }
                };
                string sqlAddenda = @"
                    SELECT id_addenda, nombre, xml_namespace, xml_schema, xml_prefix,
                           usar_conceptos, version, data_template
                    FROM cfdi_addenda_def
                    WHERE activo = true AND id_addenda = @id_addenda
                    LIMIT 1;";
                var addendaDb = RunQuery(sqlAddenda, pAdd, false, conn, tx);
                if (addendaDb.Count > 0)
                {
                    var rowAdd = addendaDb[0];
                    addenda = new Addenda
                    {
                        Tipo = rowAdd["nombre"]?.ToString() ?? string.Empty,
                        Namespace = rowAdd["xml_namespace"]?.ToString() ?? string.Empty,
                        SchemaLocation = rowAdd["xml_schema"]?.ToString() ?? string.Empty,
                        Prefix = rowAdd["xml_prefix"]?.ToString() ?? "add",
                        Options = new AddendaOptions
                        {
                            UsarConceptosCFDI = rowAdd["usar_conceptos"] != DBNull.Value
                                                && Convert.ToBoolean(rowAdd["usar_conceptos"])
                        }
                    };
                    if (!string.IsNullOrWhiteSpace(rowAdd["data_template"]?.ToString()))
                    {
                        var templateJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                            rowAdd["data_template"].ToString());
                        templateJson["ordenCompra"] = fc.GetValueOrDefault("ordenCompra", "");
                        addenda.DatosTemplate = AplanarJSON(templateJson);
                    }
                }

                // ── Llenar objeto Factura ─────────────────────────────────────
                factura.Serie = fc.GetValueOrDefault("Serie", FacturaSerie);
                factura.Folio = folioResult.ToString();
                factura.FolioCorto = fc.GetValueOrDefault("folio", "");
                factura.IdTipoPago = fc.GetValueOrDefault("forma-pago", "");
                factura.Moneda = monedaSAT;
                factura.CpE = GetEmisor("CpE");
                factura.LugarExpedicion = GetEmisor("CpE");
                factura.RfcEmisor = GetEmisor("Rfc");
                factura.RsoEmisor = GetEmisor("RazonSocial");
                factura.Rege = GetEmisor("Regimen");
                factura.RfcCliente = fc.GetValueOrDefault("rfc", "");
                factura.RsoCliente = datosCliente[0]["n_cli"].ToString();
                factura.CpR = datosCliente[0]["cp"].ToString();
                factura.IdUsoCFDI = fc.GetValueOrDefault("uso-cfdi", "");
                factura.CFDIText = usoCFDItext;
                factura.Regc = rScocial;
                factura.regimenEText = regimenText;
                factura.Subtotal = Convert.ToDecimal(fc.GetValueOrDefault("subtotal2", "0"));
                factura.MontoAnticipo = Convert.ToDecimal(fc.GetValueOrDefault("subtotal1", "0"));
                factura.TipoCambio = Convert.ToDecimal(fc.GetValueOrDefault("paridad", "1.00"));
                factura.metodoPagoTexto = mdp1.Length > 0 ? mdp1 : "PPD";
                factura.MdpFactura = mdp;
                factura.TipoDeComprobante = fc.GetValueOrDefault("TipoDeComprobante", "");
                factura.Observaciones = fc.GetValueOrDefault("comentarios", "");
                factura.formaPagoTexto = tp;
                factura.Fecha = DateTime.Now;
                factura.TipoFacturacion = tipo;
                factura.FechaTimbrado = fechaPago.ToString();
                factura.Flete = flete;
                factura.Oc = fc.GetValueOrDefault("ordenCompra", "");
                factura.EncabezadoId = Convert.ToInt32(fc.GetValueOrDefault("enc_id", "0"));
                factura.IdCliente = (int)datosCliente[0]["id_cliente"];
                factura.Addenda = addenda;

                anticiposCtx.CorreosCliente = datosCorreosClientes;

                // ── Tabla de productos ────────────────────────────────────────
                factura.Tproductos = new DataTable();
                factura.Tproductos.Columns.AddRange(new[]
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
                    new DataColumn("comentario",    typeof(string)),
                    new DataColumn("pedimentos",    typeof(string)),
                    new DataColumn("descuento",     typeof(double))
                });

                foreach (var prod in productos)
                {
                    var paramsProd = new Dictionary<string, object> { { "cve", (string)prod.productoId } };
                    var dat = RunQuery(
                        "SELECT prod_sat, ud_sat, obj_impto FROM catrelacion WHERE prod_kepler = @cve",
                        paramsProd, false, conn, tx);

                    if (dat.Count == 0)
                        throw new Exception($"No se encontró relación SAT para el producto {prod.productoId}.");

                    var rowProd = dat[0];
                    string prodSat = rowProd["prod_sat"]?.ToString()?.Trim();
                    string udSat = rowProd["ud_sat"]?.ToString()?.Trim();
                    string objImp = rowProd["obj_impto"]?.ToString()?.Trim();

                    var camposFaltantes = new List<string>();
                    if (string.IsNullOrWhiteSpace(prodSat)) camposFaltantes.Add("Clave SAT (prod_sat)");
                    if (string.IsNullOrWhiteSpace(udSat)) camposFaltantes.Add("Unidad SAT (ud_sat)");
                    if (string.IsNullOrWhiteSpace(objImp)) camposFaltantes.Add("Objeto de Impuesto (obj_impto)");
                    if (camposFaltantes.Any())
                    {
                        string msg = $"El producto {prod.productoId} no tiene configurado: {string.Join(", ", camposFaltantes)}.";
                        LogErrorHelper.RegistrarLog(FacturaLogTag, "SIN_RELACION_SAT", msg, nivel: "ERROR");
                        throw new Exception(msg);
                    }

                    // Normalizar pedimentos
                    string pedimentosJson = "[]";
                    try
                    {
                        var pedimentosRaw = prod.pedimentos;
                        if (pedimentosRaw != null)
                        {
                            string rawStr = pedimentosRaw.ToString();
                            if (!string.IsNullOrWhiteSpace(rawStr) && rawStr != "[]" && rawStr != "null")
                            {
                                if (rawStr.TrimStart().StartsWith("["))
                                {
                                    var parsed = JsonConvert.DeserializeObject<List<object>>(rawStr);
                                    pedimentosJson = JsonConvert.SerializeObject(parsed);
                                }
                                else if (rawStr.TrimStart().StartsWith("\""))
                                {
                                    string innerStr = JsonConvert.DeserializeObject<string>(rawStr);
                                    if (!string.IsNullOrWhiteSpace(innerStr) && innerStr.TrimStart().StartsWith("["))
                                    {
                                        var parsed = JsonConvert.DeserializeObject<List<object>>(innerStr);
                                        pedimentosJson = JsonConvert.SerializeObject(parsed);
                                    }
                                }
                            }
                        }
                    }
                    catch { pedimentosJson = "[]"; }

                    double cantProd = (double)prod.cantidad;
                    double precioProd = (double)prod.precio;
                    double descuentoProd = (double)(prod.descuento ?? 0);
                    double importeProd = Math.Round(cantProd * precioProd * (1 - descuentoProd / 100.0), 2);

                    factura.Tproductos.Rows.Add(
                        (string)prod.productoId,
                        prodSat,
                        udSat,
                        (string)prod.unidad,
                        (string)prod.descripcion,
                        cantProd,
                        precioProd,
                        importeProd,
                        (string)(prod.objetoImp ?? "02"),
                        (string)(prod.comentario ?? ""),
                        pedimentosJson,
                        descuentoProd
                    );
                }

                // ── Tabla de anticipos ────────────────────────────────────────
                factura.TAnticipos = new DataTable();
                factura.TAnticipos.Columns.AddRange(new[]
                {
                    new DataColumn("uuid",           typeof(string)),
                    new DataColumn("fecha",          typeof(string)),
                    new DataColumn("monto_aplicado", typeof(decimal)),
                    new DataColumn("saldo_antes",    typeof(decimal)),
                    new DataColumn("saldo_despues",  typeof(decimal))
                });

                decimal totalFactura = Convert.ToDecimal(fc.GetValueOrDefault("subtotal2", "0"));
                decimal ivaFactura = Convert.ToDecimal(fc.GetValueOrDefault("iva", "0"));
                decimal totalFacturaConIVA = totalFactura + ivaFactura;

                var encParamCheck = new Dictionary<string, object>
                {
                    { "id_encabezado", Convert.ToInt32(fc.GetValueOrDefault("enc_id", "0")) }
                };
                int enc = Convert.ToInt32(
                    RunScalar("SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado",
                              encParamCheck, false, conn, tx) ?? 0);

                AplicarAnticipos anticipoModelo = new AplicarAnticipos();

                // ── Procesar anticipos ────────────────────────────────────────
                if (anticipo.Count > 0)
                {
                    decimal totalAnticiposAplicados = Convert.ToDecimal(fc.GetValueOrDefault("totalAnticipos", "0"));
                    var anticiposDisponibles = new List<(int IdEncabezado, string UUID, decimal Saldo, decimal Total, int IdFactura)>();

                    foreach (var ant in anticipo)
                    {
                        var paramsAnt = new Dictionary<string, object> { { "cve", (int)ant.id_encabezado } };
                        var datAnt = RunQuery("SELECT id, uuid, total, saldo FROM factura WHERE encabezado_id = @cve;",
                                              paramsAnt, false, conn, tx);
                        if (datAnt.Count == 0)
                            throw new Exception($"No se encontró el anticipo con encabezado {ant.id_encabezado}.");

                        anticiposDisponibles.Add((
                            IdEncabezado: Convert.ToInt32(ant.id_encabezado),
                            UUID: datAnt[0]["uuid"].ToString(),
                            Saldo: Convert.ToDecimal(datAnt[0]["saldo"]),
                            Total: Convert.ToDecimal(datAnt[0]["total"]),
                            IdFactura: Convert.ToInt32(datAnt[0]["id"])
                        ));
                        anticipoModelo.Anticipos.Add(Convert.ToInt32(ant.id_encabezado));
                    }

                    decimal saldoTotalDisponible = anticiposDisponibles.Sum(a => a.Saldo);
                    if (saldoTotalDisponible <= 0)
                        throw new Exception("Los anticipos seleccionados no tienen saldo disponible.");

                    decimal totalAplicableAFactura = Math.Min(totalFacturaConIVA, totalAnticiposAplicados);
                    decimal restante = totalAplicableAFactura;

                    foreach (var ant in anticiposDisponibles)
                    {
                        if (restante <= 0) break;

                        decimal proporcion = ant.Saldo / saldoTotalDisponible;
                        decimal montoAplicado = Math.Round(totalAplicableAFactura * proporcion, 2);
                        if (montoAplicado > ant.Saldo) montoAplicado = ant.Saldo;
                        if (montoAplicado > restante) montoAplicado = restante;

                        decimal nuevoSaldo = ant.Saldo - montoAplicado;
                        restante -= montoAplicado;

                        factura.TAnticipos.Rows.Add(
                            ant.UUID,
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            montoAplicado,
                            ant.Saldo,
                            nuevoSaldo
                        );

                        anticiposCtx.Operaciones.Add(
                            (ant.IdEncabezado, nuevoSaldo, montoAplicado, ant.IdFactura, ant.Saldo));
                    }

                    decimal totalAplicadoFinal = factura.TAnticipos.AsEnumerable()
                        .Sum(r => Convert.ToDecimal(r["monto_aplicado"]));
                    factura.Total = Math.Max(0, totalFacturaConIVA - totalAplicadoFinal);
                }
                else
                {
                    factura.Total = totalFacturaConIVA;
                }

                if (enc > 0)
                {
                    if (tipo == "anticipo")
                    {
                        factura.Saldo = totalFacturaConIVA;
                        var poliza = GenerarDatosPoliza(enc, null, null, conn, tx);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
                        CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Anticipo, conn, tx);
                    }
                    else if (tipo == "credito")
                    {
                        var poliza = GenerarDatosPoliza(enc, null, null, conn, tx);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
                        RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);
                    }
                    else // contado
                    {
                        var poliza = GenerarDatosPoliza(enc, null, null, conn, tx);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
                        RegistrarCarteraResult cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

                        if (anticipo.Count > 0)
                        {
                            anticipoModelo.CarteraId = cartera.CarteraId;
                            anticipoModelo.UsuarioId = GetUserId(User.Identity.Name);
                            AplicarAnticipos(anticipoModelo, conn, tx);
                        }

                        if (anticipo.Count > 0)
                        {
                            decimal montoTotalAplicado = factura.TAnticipos.AsEnumerable()
                                .Sum(r => Convert.ToDecimal(r["monto_aplicado"]));

                            decimal subtotalNC = Math.Round(montoTotalAplicado / 1.16m, 2);
                            decimal ivaNC = Math.Round(montoTotalAplicado - subtotalNC, 2);

                            string uuidsRelacionados = string.Join(",",
                                factura.TAnticipos.AsEnumerable()
                                    .Select(r => r["uuid"].ToString()));

                            anticiposCtx.NotaCreditoData = new NotaCreditoAnticipoData
                            {
                                UUIDsAnticiposRelacionados = uuidsRelacionados,
                                MontoTotalAplicado = montoTotalAplicado,
                                SubtotalNotaCredito = subtotalNC,
                                IVANotaCredito = ivaNC
                            };
                        }
                    }
                }

                LogErrorHelper.RegistrarLog(FacturaLogTag, "SIN_FOLIO",
                    $"PrepararDatosParaTimbrar OK → subtotal2={fc.GetValueOrDefault("subtotal2", "")} " +
                    $"iva={fc.GetValueOrDefault("iva", "")} " +
                    $"factura.Total={factura.Total} factura.Subtotal={factura.Subtotal}",
                    nivel: "DEBUG");

                return (true, "Datos preparados correctamente", anticiposCtx);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentasFacturaBase/?");
                return (false, ex.Message, null);
            }
        }

        // ============================================================
        // FASE 2 — Solo timbrado, SIN tocar BD
        // ============================================================
        private TimbradoResult EjecutarTimbrado(Factura factura)
        {
            return factura.TipoFacturacion?.ToLower() == "anticipo"
                ? GenerarXmlAnticipo(factura)
                : GenerarXml(factura);
        }

        // ============================================================
        // Aplicar saldos de anticipos DENTRO de la TX (antes del commit)
        // ============================================================
        private void AplicarSaldosAnticipos(
            OperacionesAnticiposContext ctx,
            Dictionary<string, string> fc,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var parametersInsert = new Dictionary<string, object>
            {
                { "idFacturaPrincipal", Convert.ToInt32(fc.GetValueOrDefault("enc_id", "0")) }
            };
            string query = "SELECT id FROM factura WHERE encabezado_id = @idFacturaPrincipal";
            int idFactura = Convert.ToInt32(RunScalar(query, parametersInsert, false, conn, tx));

            foreach (var op in ctx.Operaciones)
            {
                var updParam = new Dictionary<string, object>
                {
                    { "nuevoSaldo",    op.NuevoSaldo },
                    { "id_encabezado", op.IdEncabezado }
                };
                RunUpdate("UPDATE factura SET saldo = @nuevoSaldo WHERE encabezado_id = @id_encabezado;",
                          updParam, false, conn, tx);

                var insParam = new Dictionary<string, object>
                {
                    { "idFactura",        idFactura },
                    { "idFacturaAnticipo", op.IdAnticipo },
                    { "montoAplicado",    op.MontoAplicado },
                    { "usuario",          GetUserId(User.Identity.Name) },
                    { "observaciones",    "Aplicación de anticipo sobre subtotal." },
                    { "saldoAntes",       op.SaldoAntes },
                    { "saldoDespues",     op.NuevoSaldo }
                };
                RunQuery(@"
                    INSERT INTO factura_anticipos
                        (id_factura_principal, id_factura_anticipo, monto_aplicado,
                         fecha_aplicacion, usuario_aplica, observaciones,
                         saldo_antes, saldo_despues)
                    VALUES
                        (@idFactura, @idFacturaAnticipo, @montoAplicado,
                         NOW(), @usuario, @observaciones,
                         @saldoAntes, @saldoDespues);",
                    insParam, false, conn, tx);
            }

            if (fc.TryGetValue("uuid_nc_anticipo", out string uuidNC)
                && !string.IsNullOrWhiteSpace(uuidNC))
            {
                var updNcParam = new Dictionary<string, object>
                {
                    { "uuid_nc",   uuidNC },
                    { "id_factura", idFactura }
                };
                RunUpdate(
                    "UPDATE factura SET observaciones = observaciones || @uuid_nc WHERE id = @id_factura",
                    updNcParam, false, conn, tx);
            }
        }

        // ============================================================
        // Marcar documentos padre como procesados (dentro de la TX)
        // ============================================================
        private void MarcarDocumentosPadre(
            Dictionary<string, string> fc,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            if (!fc.TryGetValue("documentid", out string idsDocumentos)
                || string.IsNullOrWhiteSpace(idsDocumentos)) return;

            var ids = idsDocumentos
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            foreach (int id in ids)
            {
                // var updParam = new Dictionary<string, object> { { "id", id } };
                // RunUpdate(@"UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id AND nat NOT IN ('VIREM', 'VNREM')", updParam, false, conn, tx);
            }
        }

        // ============================================================
        // Construir respuesta final (fuera de TX)
        // ============================================================
        private Dictionary<string, object> ConstruirRespuestaFactura(
            TimbradoResult resultadoTimbrado,
            Factura factura,
            Dictionary<string, string> fc)
        {
            return new Dictionary<string, object>
            {
                { "UUID",               resultadoTimbrado.UUID },
                { "Total",              factura.Total },
                { "Subtotal",           factura.Subtotal },
                { "IVA",                factura.IVA },
                { "RFCCliente",         factura.RfcCliente },
                { "RazonSocialCliente", factura.RsoCliente },
                { "Fecha",              factura.Fecha.ToString("dd/MM/yyyy HH:mm:ss") },
                { "Serie",              factura.Serie },
                { "Folio",              factura.Folio },
                { "EncabezadoId",       factura.EncabezadoId },
                { "CantidadProductos",  factura.Tproductos.Rows.Count },
                { "PdfUrl",             Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf") },
                { "XmlUrl",             Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml") },
                { "CorreosCliente",     new List<object>() }
            };
        }

        // ============================================================
        // Registrar CFDI huérfano con conexión INDEPENDIENTE
        // ============================================================
        private void RegistrarCFDIHuerfano(string uuid, int idEncabezado, string errorCommit)
        {
            // Usa la misma IConfiguration inyectada — no hay ConfigurationManager en .NET 8
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
                LogErrorHelper.RegistrarLog(FacturaLogTag, idEncabezado.ToString(),
                    $"No se pudo registrar CFDI huérfano UUID={uuid}: {ex.Message}",
                    nivel: "CRITICAL");
            }
        }
        // ============================================================
        // Deserializa productosJSON a Dictionary<string, string>.
        //
        // No se puede usar DeserializeObject<List<Dictionary<string,string>>>
        // directamente: campos como "pedimentos" viajan como arreglo u objeto
        // y Newtonsoft revienta con "Unexpected character encountered while
        // parsing value: [". Aquí los valores complejos se conservan como su
        // JSON original (que es lo que espera el resto del flujo).
        // ============================================================
        private List<Dictionary<string, string>> DeserializarProductosJSON(string prodJson)
        {
            var crudos = JsonConvert.DeserializeObject<List<Newtonsoft.Json.Linq.JObject>>(prodJson);
            var resultado = new List<Dictionary<string, string>>();

            if (crudos == null) return resultado;

            foreach (var obj in crudos)
            {
                var fila = new Dictionary<string, string>();

                foreach (var prop in obj.Properties())
                {
                    var valor = prop.Value;

                    if (valor == null || valor.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                        fila[prop.Name] = "";
                    else if (valor.Type == Newtonsoft.Json.Linq.JTokenType.Array
                          || valor.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                        fila[prop.Name] = valor.ToString(Formatting.None);
                    else
                        fila[prop.Name] = valor.ToString();
                }

                resultado.Add(fila);
            }

            return resultado;
        }

        // Fallback: solo se usa si el front no mandó productosJSON.
        // Genera un renglón por partida de remisión (sin agrupar) para que la
        // factura guardada conserve la trazabilidad 1:1 con las remisiones y
        // coincida con lo que se timbra.

        // ============================================================
        // Aplanar JSON anidado a Dictionary<string, string>
        // ============================================================
        private Dictionary<string, string> AplanarJSON(
            Dictionary<string, object> json, string prefijo = "")
        {
            var resultado = new Dictionary<string, string>();
            foreach (var kvp in json)
            {
                string key = string.IsNullOrEmpty(prefijo) ? kvp.Key : $"{prefijo}.{kvp.Key}";
                if (kvp.Value is Newtonsoft.Json.Linq.JObject jObj)
                {
                    var subDict = jObj.ToObject<Dictionary<string, object>>();
                    var subResultado = AplanarJSON(subDict, key);
                    foreach (var sub in subResultado)
                        resultado[sub.Key] = sub.Value;
                }
                else
                {
                    resultado[key] = kvp.Value?.ToString() ?? "";
                }
            }
            return resultado;
        }
        // ============================================================
        // Validar relaciones SAT antes de timbrar
        // ============================================================
        protected async Task<JsonResult> ValidarPartidasSATCore(IFormCollection IFormCollection)
        {
            var fc = IFormCollection.ToDictionary(x => x.Key, x => x.Value.ToString());
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            using var conn = new NpgsqlConnection(connStr);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                if (!fc.TryGetValue("productosJSON", out string prodJson)
                    || string.IsNullOrWhiteSpace(prodJson))
                    return Json(new { success = false, message = "No hay productos para validar.", resultados = new object[0] });

                var productos = JsonConvert.DeserializeObject<List<dynamic>>(prodJson);
                if (productos == null || productos.Count == 0)
                    return Json(new { success = false, message = "No hay productos para validar.", resultados = new object[0] });

                var resultados = new List<object>();
                bool todoValido = true;

                foreach (var prod in productos)
                {
                    string productoId = (string)prod.productoId;
                    var paramsProd = new Dictionary<string, object> { { "cve", productoId } };
                    var dat = RunQuery(
                        "SELECT prod_sat, ud_sat, obj_impto FROM catrelacion WHERE prod_kepler = @cve",
                        paramsProd, false, conn, tx);

                    if (dat.Count == 0)
                    {
                        todoValido = false;
                        resultados.Add(new
                        {
                            productoId,
                            descripcion = (string)prod.descripcion,
                            valido = false,
                            camposFaltantes = new[] { "Sin relación SAT registrada" },
                            prodSat = "",
                            udSat = "",
                            objImp = ""
                        });
                        continue;
                    }

                    var rowV = dat[0];
                    string pSat = rowV["prod_sat"]?.ToString()?.Trim() ?? "";
                    string uSat = rowV["ud_sat"]?.ToString()?.Trim() ?? "";
                    string oImp = rowV["obj_impto"]?.ToString()?.Trim() ?? "";

                    var camposFaltantes = new List<string>();
                    if (string.IsNullOrWhiteSpace(pSat)) camposFaltantes.Add("Clave SAT (prod_sat)");
                    if (string.IsNullOrWhiteSpace(uSat)) camposFaltantes.Add("Unidad SAT (ud_sat)");
                    if (string.IsNullOrWhiteSpace(oImp)) camposFaltantes.Add("Objeto de Impuesto (obj_impto)");

                    bool esValido = camposFaltantes.Count == 0;
                    if (!esValido) todoValido = false;

                    resultados.Add(new
                    {
                        productoId,
                        descripcion = (string)prod.descripcion,
                        valido = esValido,
                        camposFaltantes,
                        prodSat = pSat,
                        udSat = uSat,
                        objImp = oImp
                    });
                }

                tx.Commit();
                return Json(new
                {
                    success = true,
                    todoValido,
                    totalProductos = resultados.Count,
                    productosValidos = resultados.Count(r => ((dynamic)r).valido),
                    productosInvalidos = resultados.Count(r => !((dynamic)r).valido),
                    resultados
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentasFacturaBase/ValidarPartidasSATCore");
                tx.Rollback();
                return Json(new { success = false, message = "No se pudo validar la factura." });
            }
        }
        // ============================================================
        // Enviar alerta por correo cuando hay productos sin config SAT
        // ============================================================
        protected async Task<ActionResult> EnviarAlertaSATCore()
        {
            try
            {
                string emailsString = Request.Form["emails"].ToString();
                string resultadosJson = Request.Form["resultadosJson"].ToString();
                string folio = Request.Form["folio"].ToString() ?? "Sin folio";
                string clienteNom = Request.Form["cliente"].ToString() ?? "Sin cliente";

                if (string.IsNullOrWhiteSpace(emailsString))
                    return Json(new { success = false, message = "Debe ingresar al menos un correo destinatario." });
                if (string.IsNullOrWhiteSpace(resultadosJson))
                    return Json(new { success = false, message = "No hay resultados de validación para enviar." });

                var resultados = JsonConvert.DeserializeObject<List<dynamic>>(resultadosJson);
                var invalidos = resultados?.Where(r => !(bool)r.valido).ToList();

                if (invalidos == null || invalidos.Count == 0)
                    return Json(new { success = false, message = "No hay productos con errores SAT para reportar." });

                var emails = emailsString
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct()
                    .ToList();

                if (!emails.Any())
                    return Json(new { success = false, message = "No se proporcionaron correos válidos." });

                var emailsInvalidos = emails.Where(e =>
                {
                    try { new System.Net.Mail.MailAddress(e); return false; }
                    catch { return true; }
                }).ToList();

                if (emailsInvalidos.Any())
                    return Json(new { success = false, message = $"Correos con formato inválido: {string.Join(", ", emailsInvalidos)}" });

                var emailData = new AlertaSATEmailModel
                {
                    Folio = folio,
                    Cliente = clienteNom,
                    UsuarioQueValido = User.Identity.Name ?? "Sistema",
                    FechaValidacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    ProductosInvalidos = invalidos.Select(r => new ProductoSATInvalido
                    {
                        ProductoId = r.productoId?.ToString() ?? "-",
                        Descripcion = r.descripcion?.ToString() ?? "-",
                        CamposFaltantes = ((Newtonsoft.Json.Linq.JArray)r.camposFaltantes)
                                            .Select(f => f.ToString()).ToList()
                    }).ToList()
                };

                string htmlBody = await _emailSender.RenderViewToStringAsync(
                    "~/Views/Email/_AlertaSATEmail.cshtml",
                    emailData);

                int enviosExitosos = 0;
                var errores = new List<string>();

                foreach (var email in emails)
                {
                    try
                    {
                        await correoHelper.EnviarCorreoNotificacionAsync(
                            email,
                            $"⚠ Alerta SAT — Productos sin configuración | Folio {folio}",
                            htmlBody
                    );
                        enviosExitosos++;
                        
                    }
                    catch (Exception exEmail)
                    {
                        errores.Add($"{email}: {exEmail.Message}");
                    }
                }

                if (enviosExitosos == emails.Count)
                    return Json(new { success = true, message = $"Alerta enviada correctamente a {enviosExitosos} destinatario(s)." });

                if (enviosExitosos > 0)
                    return Json(new { success = true, message = $"Alerta enviada a {enviosExitosos} de {emails.Count} destinatario(s).", errores, parcial = true });

                return Json(new { success = false, message = "No se pudo enviar el correo a ningún destinatario.", errores });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentasFacturaBase/EnviarAlertaSATCore");
                return Json(new { success = false, message = "No se pudo enviar la alerta al SAT." });
            }
        }

        // ============================================================
        // Construir la Factura para la Nota de Crédito de anticipo
        // ============================================================
        private Factura ConstruirFacturaParaNC(
            NotaCreditoAnticipoData ncData,
            Factura facturaOriginal,
            string uuidFacturaPrincipal,
            Dictionary<string, string> fc,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                : "pruebas";

            string GetEmisor(string campo) =>
                _configuration[$"emisores:{perfil}:{campo}"] ?? "";

            // ── Calcular IVA proporcional ─────────────────────────────────────
            decimal totalFacturaForm = Convert.ToDecimal(Request.Form["total"].ToString());
            decimal pago = ncData.MontoTotalAplicado;

            decimal importeBase = Math.Round(totalFacturaForm / 1.16m, 2, MidpointRounding.AwayFromZero);
            decimal ivaTotal = totalFacturaForm - importeBase;

            decimal proporcion = pago / totalFacturaForm;
            decimal ivaProporcional = Math.Round(ivaTotal * proporcion, 2, MidpointRounding.AwayFromZero);
            decimal subtotal = Math.Round(pago - ivaProporcional, 2, MidpointRounding.AwayFromZero);

            var nc = new Factura
            {
                Serie = "NC",
                FolioCorto = fc.GetValueOrDefault("folio", ""),
                Folio = fc.GetValueOrDefault("folio", ""),
                TipoDeComprobante = "E",
                TipoRelacion = "07",
                UUIDsRelacionados = ncData.UUIDsAnticiposRelacionados,
                TipoFacturacion = "APLICACION_ANTICIPO",
                RfcEmisor = GetEmisor("Rfc"),
                RsoEmisor = GetEmisor("RazonSocial"),
                Rege = GetEmisor("Regimen"),
                CpE = GetEmisor("CpE"),
                RfcCliente = facturaOriginal.RfcCliente,
                RsoCliente = facturaOriginal.RsoCliente,
                CpR = facturaOriginal.CpR,
                Regc = facturaOriginal.Regc,
                IdUsoCFDI = "CP01",
                Moneda = facturaOriginal.Moneda,
                TipoCambio = facturaOriginal.TipoCambio,
                Subtotal = ncData.SubtotalNotaCredito,
                IVA = ncData.IVANotaCredito,
                Total = ncData.MontoTotalAplicado,
                Saldo = ncData.MontoTotalAplicado,
                IdTipoPago = facturaOriginal.IdTipoPago,
                metodoPagoTexto = "PUE",
                MdpFactura = facturaOriginal.MdpFactura,
                Fecha = DateTime.Now,
                LugarExpedicion = facturaOriginal.LugarExpedicion,
                // OJO: EncabezadoId se asigna MÁS ABAJO, ya con el encabezado propio de la nota.
                // Aquí no se puede: ese encabezado todavía no existe (se crea con
                // GenerarDocumentoConPartidas). Antes se dejaba el de la factura original, lo que
                // guardaba la nota en `factura.encabezado_id` colgada del documento equivocado.
                IdCliente = facturaOriginal.IdCliente,
                Observaciones = $"Nota de crédito por aplicación de anticipos a factura UUID: {uuidFacturaPrincipal}",
            };

            nc.Tproductos = new DataTable();
            nc.Tproductos.Columns.AddRange(new[]
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
                new DataColumn("iva",           typeof(double)),
                new DataColumn("ieps",          typeof(double)),
            });

            nc.Tproductos.Rows.Add(
                "ANTICIPO",
                "84111506",
                "ACT",
                "Actividad",
                $"Aplicación de anticipo a factura {uuidFacturaPrincipal}",
                1.0,
                (double)ncData.SubtotalNotaCredito,
                (double)ncData.SubtotalNotaCredito,
                "02",
                0.0,
                16.0,
                0.0
            );

            // ── Forma de pago ────────────────────────────────────────────────
            // Se resuelve ANTES de crear el encabezado para poder heredarla también en
            // encabezadomov.f_pago. Antes solo se calculaba más abajo para imp_oc.f_pago_id,
            // por lo que el encabezado de la nota quedaba con f_pago en NULL.
            // Se hereda la forma de pago de la FACTURA (misma que ya lleva el CFDI de la nota
            // vía nc.IdTipoPago = facturaOriginal.IdTipoPago) para que encabezado, imp_oc y CFDI
            // sean consistentes entre sí.
            int fpId = 0;
            try
            {
                var fPagoParameter = new Dictionary<string, object>
                {
                    { "cve_sat", Request.Form["forma-pago"].ToString() }
                };
                var fpRaw = RunScalar("SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat",
                                      fPagoParameter, false, conn, tx);
                if (fpRaw != null && fpRaw != DBNull.Value)
                    fpId = Convert.ToInt32(fpRaw);
            }
            catch { fpId = 0; }

            // ── Encabezado del documento interno ─────────────────────────────
            var encabezado = new DocumentoEncabezado
            {
                EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                IdArea = 4,
                IdTpDoc = 81,
                UsrDep = GetAreaName(User.Identity.Name),
                Anio = DateTime.Now.Year,
                Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                Fch = DateTime.Now,
                TpMov = "NT",
                UsrDoc = User.Identity.Name,
                FchCap = DateTime.Now,
                Usr3 = GetUserId(User.Identity.Name),
                Fch3 = DateTime.Now,
                Imp = ncData.MontoTotalAplicado,
                Dto = 0m,
                Sub = ncData.SubtotalNotaCredito,
                CliProv = facturaOriginal.RsoCliente,
                Ref = facturaOriginal.EncabezadoId,
                Ccy = facturaOriginal.Moneda,
                Estatus = 11,
                Flete = 0m,
                Coment1 = $"Nota de crédito por anticipo. Factura: {uuidFacturaPrincipal}",
                EncabezadoPadre = facturaOriginal.EncabezadoId,
                PlDias = 0,
                FchPgEntrega = DateTime.Now,
                Par = facturaOriginal.TipoCambio,
                Mdp = "PUE",
                TipoPoceso = "aplicacion_anticipo",
                CentroCostos = Convert.ToInt32(Request.Form["CentroCostosId"].ToString()),
            };

            // Solo se asigna si se resolvió: f_pago es FK a cat_f_pago, un 0 la violaría.
            if (fpId > 0)
                encabezado.FPago = fpId;

            var partidasNC = new List<PartidaDocumento>
            {
                new PartidaDocumento
                {
                    CveProd   = "ANTICIPO",
                    DescrProd = $"Aplicación de anticipo a factura {uuidFacturaPrincipal}",
                    CantUd    = 1m,
                    PvProd    = ncData.SubtotalNotaCredito,
                    Dto1      = 0m,
                    ImpPart   = ncData.SubtotalNotaCredito,
                    Ud        = "ACT",
                    TpDocAnt  = "NC_ANTICIPO"
                }
            };

            var folio = GenerarDocumentoConPartidas(encabezado, partidasNC, conn, tx);
            int idNuevaFactura = Convert.ToInt32(folio["IdEncabezado"]);

            // La nota de crédito debe persistirse con SU PROPIO encabezado (el recién creado con
            // TpMov='NT'), no con el de la factura original: si no, en la tabla `factura` quedan dos
            // filas colgadas del mismo encabezado y cualquier lectura tipo
            // "SELECT ... WHERE encabezado_id = X ORDER BY id DESC LIMIT 1" devuelve la nota
            // en lugar de la factura.
            nc.EncabezadoId = idNuevaFactura;

            // (fpId ya se resolvió arriba, antes de crear el encabezado)
            string insImpuestos = @"
                INSERT INTO imp_oc
                    (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
                VALUES
                    (@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id)";

            var impParam = new Dictionary<string, object>
            {
                { "encabezado_id", idNuevaFactura },
                { "impuesto_id",   Convert.ToInt32(GetSetting("impuesto")) },
                { "subtotal",      subtotal },
                { "importe",       ivaProporcional },
                { "orden_apl",     1 },
                { "imp_variable",  16 },
                { "prov_nom",      facturaOriginal.RsoCliente },
                { "f_pago_id",     fpId }
            };
            RunQuery(insImpuestos, impParam, false, conn, tx);

            var poliza = GenerarDatosPoliza(idNuevaFactura, null, null, conn, tx);
            RegistrarPolizas(GetUserId(User.Identity.Name), idNuevaFactura,
                             poliza, false, null, conn, tx);

            return nc;
        }

        // Fallback: solo si el front no mandó productosJSON. Un renglón por partida.
        protected List<Dictionary<string, string>> ConvertirPartidasRemisionAProductos(
            List<RemisionPartidaParaFacturar> partidas, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var resultado = new List<Dictionary<string, string>>();

            foreach (var p in partidas)
            {
                string descripcion = p.Descripcion;
                string unidad = p.Unidad;

                if (string.IsNullOrWhiteSpace(descripcion) || string.IsNullOrWhiteSpace(unidad))
                {
                    var datosParam = new Dictionary<string, object>
                    {
                        { "enc",      p.RemisionId },
                        { "cve_prod", p.CveProd ?? "" }
                    };
                    var origen = RunQuery(@"
                        SELECT descr_prod, ud
                        FROM partidasdoc
                        WHERE encabezado_id = @enc AND cve_prod = @cve_prod
                        LIMIT 1",
                        datosParam, false, conn, tx);

                    if (origen.Count > 0)
                    {
                        if (string.IsNullOrWhiteSpace(descripcion))
                            descripcion = origen[0]["descr_prod"]?.ToString();
                        if (string.IsNullOrWhiteSpace(unidad))
                            unidad = origen[0]["ud"]?.ToString();
                    }
                }

                resultado.Add(new Dictionary<string, string>
                {
                    { "productoId",    p.CveProd },
                    { "descripcion",   descripcion ?? "" },
                    { "cantidad",      p.CantidadAFacturar.ToString("F4") },
                    { "precio",        p.PrecioUnitario.ToString("F4") },
                    { "descuento",     p.Descuento.ToString("F2") },
                    { "unidad",        string.IsNullOrWhiteSpace(unidad) ? "PZA" : unidad },
                    { "comentario",    string.IsNullOrWhiteSpace(p.FolioRemision)
                                            ? ""
                                            : $"Remisión: {p.FolioRemision}" },
                    { "costoUnitario", "0" }
                });
            }

            return resultado;
        }
    }

    // ================================================================
    // Clases auxiliares del pipeline de anticipos
    // ================================================================
    public class OperacionesAnticiposContext
    {
        public List<(int IdEncabezado, decimal NuevoSaldo, decimal MontoAplicado, int IdAnticipo, decimal SaldoAntes)>
            Operaciones
        { get; set; } = new();

        public List<Dictionary<string, object>> CorreosCliente { get; set; } = new();

        public NotaCreditoAnticipoData NotaCreditoData { get; set; }
    }

    public class NotaCreditoAnticipoData
    {
        public string UUIDsAnticiposRelacionados { get; set; }
        public decimal MontoTotalAplicado { get; set; }
        public decimal IVANotaCredito { get; set; }
        public decimal SubtotalNotaCredito { get; set; }
    }
}
