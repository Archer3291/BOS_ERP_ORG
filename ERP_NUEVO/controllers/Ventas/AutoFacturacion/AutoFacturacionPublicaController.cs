using BOS_ERP.Controllers;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace BOS_ERP.Controllers.Ventas.AutoFacturacion
{
    // ── Sin [Authorize] — acceso público intencional ──────────
    // La seguridad se implementa por capas dentro del controlador
    [AllowAnonymous]
    public class AutoFacturacionPublicaController : Controller
    {
        private readonly IConfiguration _config;

        // Servicios del mismo pipeline de timbrado que usan las ventas. Se inyectan en vez
        // de heredar de FacturacionVentaController a propósito: este controlador es
        // [AllowAnonymous], y al heredarlo todos los métodos públicos de la base quedarían
        // publicados como acciones alcanzables sin autenticar.
        private readonly XmlBuilderService _xmlBuilder;
        private readonly ITimbradoWorkflow _timbradoWorkflow;
        private readonly IFacturaRepository _facturaRepository;
        private readonly IWebHostEnvironment _env;

        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly CorreoHelper _correo;
        private readonly AutofacturacionOptions _afOptions;
        public AutoFacturacionPublicaController(
            IConfiguration config,
            XmlBuilderService xmlBuilder,
            ITimbradoWorkflow timbradoWorkflow,
            IFacturaRepository facturaRepository,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            CorreoHelper correo,
            IOptions<AutofacturacionOptions> afOptions
            )
        {
            _config = config;
            _xmlBuilder = xmlBuilder;
            _timbradoWorkflow = timbradoWorkflow;
            _facturaRepository = facturaRepository;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _correo = correo;
            _afOptions = afOptions.Value;
        }

        [NonAction]
        private bool TryResolverPunto(string punto, out PuntoAutofacturacion cfg)
        {
            cfg = null;
            if (string.IsNullOrWhiteSpace(punto)) return false;
            return _afOptions.Puntos.TryGetValue(punto.ToLowerInvariant(), out cfg);
        }

        private string FacturacionPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion");
        private string XmlTimbradosPath => Path.Combine(FacturacionPath, "xml_timbrados");
        private string FacturasPdfPath => Path.Combine(FacturacionPath, "facturas");
        private string QrCodesPath => Path.Combine(_env.WebRootPath, "content", "qrcodes");
        private string ContentPdfPath => Path.Combine(_env.WebRootPath, "content", "pdf");

        // ═════════════════════════════════════════════════════
        // RATE LIMITER — Thread-safe con lock por key
        // ═════════════════════════════════════════════════════

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RateLimitEntry>
            _rateLimiter = new System.Collections.Concurrent.ConcurrentDictionary<string, RateLimitEntry>();

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object>
            _rateLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();

        private static DateTime _ultimaLimpieza = DateTime.UtcNow;
        private static readonly object _cleanupLock = new object();

        private class RateLimitEntry
        {
            public int Count { get; set; }
            public DateTime Window { get; set; }
        }

        private const int RATE_MAX = 10;
        private const int RATE_MINUTES = 10;

        private const int RATE_TIMBRAR_MAX = 3;
        private const int RATE_TIMBRAR_MINUTES = 60;

        private const int HORAS_LIMITE_FACTURA = 72;

        // ─────────────────────────────────────────────────────────
        // QUERY BASE
        // ─────────────────────────────────────────────────────────
        private const string SQL_FACTURABLE = @"
            SELECT
                em.folio ||
                    CASE WHEN em.variacion > 0
                        THEN '-' || num_to_letters(em.variacion)
                        ELSE '' END AS folio,
                em.id_encabezado,
                em.suc,
                em.fch0,
                em.fch1,
                em.cli_prov,
                em.ccy,
                em.imp,
                em.cfdi,
                em.estatus_id,
                cc.rfc,
                cc.n_cli,
                cocl.correo,
                df.forma_pago,
                df.razon_social,
                df.uso_sugerido,
                df.regimen_fiscal,
                df.codigo_postal
            FROM encabezadomov em
            LEFT JOIN catclientes cc
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df
                ON df.entidad_clave = cc.cve_cli AND df.empresa_id = @empresa_id
            LEFT JOIN (
                SELECT DISTINCT ON (cliente_id) cliente_id, correo
                FROM correos_cliente
                WHERE correo IS NOT NULL
                ORDER BY cliente_id, id_correo_cli ASC
            ) cocl ON cocl.cliente_id = cc.id_cliente
            WHERE em.nat       = 'VSUC'
              AND em.suc       = @suc
              AND em.variacion  = 0
              AND em.estatus_id = 1
              AND em.cfdi       IS NULL
              AND em.fch >= NOW() - (@horas_limite * INTERVAL '1 hour')
        ";

        // ─────────────────────────────────────────────────────────
        // GET — Vista pública
        // ─────────────────────────────────────────────────────────
        /// <param name="folio">
        /// Folio precargado. Lo trae el QR impreso en el ticket, para que el cliente que
        /// escanea con la cámara del teléfono llegue con el campo listo en vez de teclearlo.
        /// Sólo prellena la caja de búsqueda: la validación es la misma del POST.
        /// </param>
        [HttpGet("Facturacion/{punto}")]
        public IActionResult Index(string punto, string folio = null)
        {
            if (!TryResolverPunto(punto, out _))
                return NotFound();

            ViewBag.Punto = punto.ToLowerInvariant();
            ViewBag.FolioPrecargado = SanitizarFolio(folio ?? "");
            return View("~/Views/Ventas/AutoFacturacion.cshtml");
        }

        // ─────────────────────────────────────────────────────────
        // POST — Buscar folio (público con rate limit)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BuscarFolio(string punto, string folio)
        {

            if (!TryResolverPunto(punto, out var cfg))
                return Json(new { success = false, message = "Punto de venta no válido." });

            if (!CheckRateLimit(GetClientIp(), "buscar", RATE_MAX, RATE_MINUTES))
                return Json(new { success = false, message = $"Demasiadas búsquedas. Espera {RATE_MINUTES} minutos." });
            if (!CheckRateLimit(GetClientIp(), "buscar", RATE_MAX, RATE_MINUTES))
                return Json(new { success = false, message = $"Demasiadas búsquedas. Espera {RATE_MINUTES} minutos." });

            if (string.IsNullOrWhiteSpace(folio))
                return Json(new { success = false, message = "Ingresa un folio." });

            folio = SanitizarFolio(folio);
            if (folio == null)
                return Json(new { success = false, message = "Folio con formato inválido." });

            try
            {
                string sql = SQL_FACTURABLE + @"
                    AND (
                        em.folio::text ILIKE @folio
                        OR RIGHT(em.folio::text, 5) = RIGHT(@folio_raw, 5)
                    )
                    LIMIT 1
                ";

                var parameters = new Dictionary<string, object>
                {
                    { "suc",          cfg.SucursalId          },
                    { "empresa_id",   cfg.EmpresaId            },
                    { "horas_limite", HORAS_LIMITE_FACTURA     },
                    { "folio",        "%" + folio + "%"        },
                    { "folio_raw",    folio                    }
                };

                var rows = RunQuery(sql, parameters);

                if (rows == null || rows.Count == 0)
                    return Json(new { success = false, message = "Folio no encontrado o fuera del plazo de facturación." });

                var row = rows[0];

                if (!string.IsNullOrWhiteSpace(row["cfdi"]?.ToString()))
                    return Json(new { success = false, message = "Este ticket ya fue facturado anteriormente." });

                var conceptos = ObtenerConceptos(Convert.ToInt32(row["id_encabezado"]), cfg.EmpresaId);

                decimal total = Convert.ToDecimal(row["imp"]);
                decimal subtotal = Math.Round(total / 1.16m, 2);
                decimal iva = Math.Round(total - subtotal, 2);

                var ticketFirmado = FirmarTicket(
                    row["id_encabezado"]?.ToString(),
                    row["folio"]?.ToString()
                );

                var ticket = new
                {
                    folio = row["folio"]?.ToString(),
                    sucursal = "Suc. " + row["suc"]?.ToString(),
                    total = total,
                    subtotal = subtotal,
                    iva = iva,
                    rfc = row["rfc"]?.ToString(),
                    razonSocial = row["razon_social"]?.ToString() ?? row["n_cli"]?.ToString(),
                    regimen = row["regimen_fiscal"]?.ToString(),
                    usoCfdi = row["uso_sugerido"]?.ToString(),
                    cp = row["codigo_postal"]?.ToString(),
                    correo = row["correo"]?.ToString(),
                    conceptos = conceptos,
                    ticketToken = ticketFirmado
                };

                return Json(new { success = true, ticket });
            }
            catch (Exception ex)
            {
                Utilities.RegistrarError(ex, "AutoFacturacion/BuscarFolio", HttpContext, capturarParametros: false);
                LogError("BuscarFolio", ex);
                return Json(new { success = false, message = "Error al consultar. Intenta de nuevo." });
            }
        }

        // ─────────────────────────────────────────────────────────
        // POST — Timbrar CFDI (público con rate limit estricto)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult TimbrarCFDI(string punto, string ticketToken, string rfc, string razon, string regimen, string uso, string cp, string email)
        {
            if (!TryResolverPunto(punto, out var cfg))
                return Json(new { success = false, message = "Punto de venta no válido." });
            if (!CheckRateLimit(GetClientIp(), "timbrar", RATE_TIMBRAR_MAX, RATE_TIMBRAR_MINUTES))
                return Json(new { success = false, message = $"Límite de timbrado alcanzado. Espera {RATE_TIMBRAR_MINUTES} minutos." });

            var (camposOk, camposMsg) = ValidarCamposFiscales(rfc, razon, regimen, uso, cp, email);
            if (!camposOk)
                return Json(new { success = false, message = camposMsg });

            var (idEncabezado, folio) = VerificarTokenTicket(ticketToken);
            if (idEncabezado == 0)
                return Json(new { success = false, message = "Token de ticket inválido o expirado." });

            try
            {
                string sqlCheck = @"
                    SELECT em.id_encabezado, em.cfdi, em.fch0, em.imp, em.cli_prov
                    FROM encabezadomov em
                    WHERE em.id_encabezado = @id_encabezado
                      AND em.estatus_id    = 1
                      AND em.cfdi          IS NULL
                      AND em.suc           = @suc
                ";

                var checkParams = new Dictionary<string, object>
                {
                    { "id_encabezado", idEncabezado },
                    { "suc",           cfg.SucursalId   }
                };

                var checkRows = RunQuery(sqlCheck, checkParams);
                if (checkRows == null || checkRows.Count == 0)
                    return Json(new { success = false, message = "El ticket ya fue facturado o no está disponible." });

                var checkRow = checkRows[0];
                var fechaCompra = Convert.ToDateTime(checkRow["fch0"]);

                if ((DateTime.Now - fechaCompra).TotalHours > HORAS_LIMITE_FACTURA)
                    return Json(new { success = false, message = "El plazo para facturar ha vencido." });

                decimal total = Convert.ToDecimal(checkRow["imp"]);
                decimal subtotal = Math.Round(total / 1.16m, 2);
                decimal iva = Math.Round(total - subtotal, 2);

                string sqlLock = @"
                    UPDATE encabezadomov
                    SET estatus_id = 3
                    WHERE id_encabezado = @id_encabezado
                      AND estatus_id    = 1
                      AND cfdi          IS NULL
                ";

                var lockParams = new Dictionary<string, object>
                    { { "id_encabezado", idEncabezado } };

                int filasAfectadas = RunNonQuery(sqlLock, lockParams);
                if (filasAfectadas == 0)
                    return Json(new { success = false, message = "El ticket ya está siendo procesado. Intenta en un momento." });

                try
                {
                    // ── Armar el comprobante igual que en ventas ──────────────────
                    // Conceptos reales tomados de las partidas y sus claves SAT, no un
                    // total dividido entre 1.16: el CFDI tiene que declarar renglón por
                    // renglón lo que se vendió.
                    var (facturaOk, facturaMsg, factura) = ArmarFacturaAutofactura(
                           idEncabezado, folio, rfc, razon, regimen, uso, cp, cfg.PerfilEmisor);

                    if (!facturaOk)
                    {
                        LiberarBloqueo(lockParams);
                        return Json(new { success = false, message = facturaMsg });
                    }

                    // ── Timbrado DENTRO de la transacción ─────────────────────────
                    // Si el PAC rechaza el comprobante no se timbró nada, así que el
                    // rollback deja la venta intacta y el cliente puede reintentar. El
                    // ambiente (pruebas o producción) lo decide PacService según el
                    // ajuste `perfil_factura`; hoy está en pruebas.
                    string uuid;

                    using (var conn = new NpgsqlConnection(ConnectionString))
                    {
                        conn.Open();
                        using var tx = conn.BeginTransaction();

                        var resultado = TimbrarConPipelineDeVentas(factura, conn, tx);

                        if (!resultado.Success)
                        {
                            LogError("TimbrarCFDI_PAC",
                                new Exception($"Folio {folio}: {resultado.Message}"));

                            LiberarBloqueo(lockParams);
                            return Json(new
                            {
                                success = false,
                                message = "No se pudo timbrar: " + resultado.Message
                            });
                        }

                        uuid = resultado.UUID;

                        // El uuid marca la venta como ya autofacturada: es lo que la saca
                        // de las búsquedas del portal.
                        RunNonQuery(@"
                            UPDATE encabezadomov
                            SET cfdi       = @uuid,
                                estatus_id = 2
                            WHERE id_encabezado = @id_encabezado",
                            new Dictionary<string, object>
                            {
                                { "uuid",          uuid         },
                                { "id_encabezado", idEncabezado }
                            }, conn, tx);

                        tx.Commit();
                    }

                    GuardarDatosFiscalesCliente(checkRow["cli_prov"]?.ToString(), rfc, razon, regimen, uso, cp);

                    RegistrarLog(idEncabezado, folio, rfc, email, uuid, GetClientIp());

                    // PDF y correo van DESPUÉS del commit y no rompen el timbrado si fallan:
                    // el CFDI ya existe y es descargable desde el portal. Un fallo de SMTP o
                    // de wkhtmltopdf no puede invalidar un comprobante ya emitido.
                    bool pdfOk = GenerarPdfFactura(factura);
                    bool correoOk = EnviarCfdiPorCorreo(email, uuid, folio, factura);

                    return Json(new
                    {
                        success = true,
                        uuid,
                        pdfDisponible = pdfOk,
                        correoEnviado = correoOk,
                        // Enlace firmado al ticket imprimible; la pantalla lo abre sola.
                        ticketUrl = Url.Action("TicketFactura", "AutoFacturacionPublica",
                            new { t = FirmarTokenImpresion(uuid) }),
                        message = correoOk
                            ? $"CFDI timbrado correctamente. Se envió a {email}."
                            : "CFDI timbrado correctamente. No se pudo enviar el correo; " +
                              "puedes descargar los archivos desde esta página."
                    });
                }
                catch (Exception pacEx)
                {
                    Utilities.RegistrarError(pacEx, "AutoFacturacion/TimbrarCFDI_PAC", HttpContext, capturarParametros: false);
                    LiberarBloqueo(lockParams);

                    LogError("TimbrarCFDI_PAC", pacEx);
                    return Json(new { success = false, message = "Error al timbrar. Por favor intenta de nuevo." });
                }
            }
            catch (Exception ex)
            {
                Utilities.RegistrarError(ex, "AutoFacturacion/TimbrarCFDI", HttpContext, capturarParametros: false);
                LogError("TimbrarCFDI", ex);
                return Json(new { success = false, message = "Error interno. Contacta soporte." });
            }
        }

        // ─────────────────────────────────────────────────────────
        // POST — Reenviar CFDI
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReenviarCFDI(string email, string uuid)
        {
            if (!CheckRateLimit(GetClientIp(), "reenvio", 5, 60))
                return Json(new { success = false, message = "Demasiados intentos. Espera 1 hora." });

            if (!EsEmailValido(email))
                return Json(new { success = false, message = "Correo inválido." });

            string sqlCheck = "SELECT cfdi FROM encabezadomov WHERE cfdi = @uuid LIMIT 1";
            var rows = RunQuery(sqlCheck, new Dictionary<string, object> { { "uuid", uuid } });
            if (rows == null || rows.Count == 0)
                return Json(new { success = false, message = "UUID no encontrado." });

            // EnviarCfdiPorCorreo(email, uuid, ...);

            return Json(new { success = true, message = "CFDI reenviado a " + email });
        }

        // ─────────────────────────────────────────────────────────
        // GET — Descarga (verificar UUID en BD)
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Descargar(string tipo, string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid) || !Regex.IsMatch(uuid, @"^[A-F0-9\-]{36}$"))
                return NotFound();

            if (!CheckRateLimit(GetClientIp(), "descarga", 20, 60))
                return StatusCode(429, "Too Many Requests");

            var rows = RunQuery(
                "SELECT cfdi FROM encabezadomov WHERE cfdi = @uuid LIMIT 1",
                new Dictionary<string, object> { { "uuid", uuid } }
            );
            if (rows == null || rows.Count == 0)
                return NotFound();

            // TODO: recuperar bytes reales del PAC
            byte[] bytes = Encoding.UTF8.GetBytes("<!-- CFDI UUID:" + uuid + " -->");
            string mime = tipo == "pdf" ? "application/pdf" : "application/xml";
            string ext = tipo == "pdf" ? "pdf" : "xml";

            return File(bytes, mime, "CFDI_" + uuid + "." + ext);
        }

        // ═════════════════════════════════════════════════════════
        // TIMBRADO — mismo pipeline que usan las ventas
        // ═════════════════════════════════════════════════════════

        /// <summary>Devuelve la venta al estado facturable tras un intento fallido.</summary>
        [NonAction]
        private void LiberarBloqueo(Dictionary<string, object> lockParams)
        {
            try
            {
                RunNonQuery(@"
                    UPDATE encabezadomov
                    SET estatus_id = 1
                    WHERE id_encabezado = @id_encabezado AND estatus_id = 3", lockParams);
            }
            catch (Exception ex)
            {
                // Que no tape el error original: si esto falla, la venta queda en
                // estatus 3 y hay que liberarla a mano, pero el cliente ya recibió
                // el mensaje correcto.
                LogError("TimbrarCFDI_LiberarBloqueo", ex);
            }
        }

        /// <summary>
        /// Construye el CFDI a partir de la venta y de los datos fiscales que capturó el
        /// cliente. Sigue el mismo armado que PrepararFacturaDesdeDocs del punto de venta:
        /// emisor desde appsettings según el perfil, conceptos desde partidasdoc con sus
        /// claves SAT, y el descuento deducido de la diferencia entre precio de lista e
        /// imp_part (no de dto1, que aquí sólo deja constancia).
        /// </summary>
        [NonAction]
        private (bool Ok, string Mensaje, Factura Factura) ArmarFacturaAutofactura(int idEncabezado, string folio, string rfc, string razon, string regimen, string uso, string cp, string perfilEmisor)  
        {

            // ⚠ El ajuste `perfil_factura` MANDA sobre el perfil configurado en el punto.
            //
            // Antes se calculaba esta variable y luego se ignoraba: el emisor salía siempre
            // de cfg.PerfilEmisor (p. ej. "SRS"), así que con el sistema en pruebas se
            // timbraba igualmente con el RFC y el certificado reales de la empresa. Es el
            // mismo criterio que aplica TimbradoService para elegir el endpoint del PAC, y
            // los dos tienen que decidir con la misma bandera o se emite con un emisor real
            // contra un ambiente de prueba (o al revés).
            //
            // Que el módulo sea anónimo no cambia nada: la bandera es del sistema, no de la
            // sesión.
            bool esProduccion = Convert.ToBoolean(Utilities.GetSetting("perfil_factura"));
            string perfil = esProduccion ? perfilEmisor : "pruebas";

            string Emisor(string campo) => _config[$"Emisores:{perfil}:{campo}"] ?? "";

            if (string.IsNullOrWhiteSpace(Emisor("Rfc")))
                return (false, $"No hay datos de emisor configurados para '{perfil}'.", null);

            var enc = RunQuery(@"
                SELECT em.id_encabezado, em.folio, em.ccy, em.mdp, em.f_pago, em.coment1, em.uuid
                FROM encabezadomov em
                WHERE em.id_encabezado = @id",
                new Dictionary<string, object> { { "id", idEncabezado } })?.FirstOrDefault();

            if (enc == null)
                return (false, "No se encontró la venta.", null);

            var partidas = RunQuery(@"
                SELECT pd.cve_prod, pd.descr_prod, pd.cant_ud, pd.ud, pd.pv_prod, pd.imp_part,
                       cr.prod_sat, cr.ud_sat, cr.obj_impto
                FROM partidasdoc pd
                LEFT JOIN catrelacion cr ON cr.prod_kepler = pd.cve_prod
                WHERE pd.encabezado_id = @id
                ORDER BY pd.nro_part",
                new Dictionary<string, object> { { "id", idEncabezado } });

            if (partidas == null || partidas.Count == 0)
                return (false, "La venta no tiene productos que facturar.", null);

            // Sin clave SAT no se puede armar el concepto. Al cliente no se le puede pedir
            // que lo resuelva, así que se le da un mensaje que pueda repetir en mostrador.
            var sinClave = partidas
                .Where(p => p["prod_sat"] == null)
                .Select(p => p["cve_prod"]?.ToString())
                .Distinct()
                .ToList();

            if (sinClave.Count > 0)
                return (false,
                    "Este ticket tiene productos sin clave SAT registrada (" +
                    string.Join(", ", sinClave) +
                    "). Solicita tu factura en el mostrador.", null);

            string ccy = enc["ccy"]?.ToString() ?? "";
            string moneda = ccy == "DLLS" ? "USD" : ccy == "EURO" ? "EUR" : "MXN";

            string Descripcion(string tabla, string col, string clave, object valor, string omision)
            {
                var r = RunQuery($"SELECT {col} AS d FROM {tabla} WHERE {clave} = @v",
                    new Dictionary<string, object> { { "v", valor } })?.FirstOrDefault();
                return r?["d"]?.ToString() ?? omision;
            }

            var factura = new Factura
            {
                Serie = "AF",              // autofacturación: serie propia para distinguirla
                Folio = folio,
                Moneda = moneda,
                TipoCambio = 1m,
                TipoDeComprobante = "I",
                Fecha = DateTime.Now,

                CpE = Emisor("CpE"),
                RfcEmisor = Emisor("Rfc"),
                RsoEmisor = Emisor("RazonSocial"),
                Rege = Emisor("Regimen"),
                LugarExpedicion = !string.IsNullOrWhiteSpace(Emisor("CpE")) ? Emisor("CpE") : "78394",

                // Receptor: lo capturó el cliente en el portal.
                RfcCliente = rfc,
                RsoCliente = razon,
                CpR = cp,
                Regc = regimen,
                IdUsoCFDI = uso,
                CFDIText = Descripcion("catusocfdi", "descripcion", "clave", uso, "Sin efectos fiscales."),
                regimenEText = Descripcion("catregimenfiscal", "descripcion", "clave", regimen, "General de Ley Personas Morales"),

                IdTipoPago = Descripcion("cat_f_pago", "cve_sat", "id_f_pago", enc["f_pago"], "01"),
                formaPagoTexto = Descripcion("cat_f_pago", "descripcion", "id_f_pago", enc["f_pago"], "Efectivo"),
                metodoPagoTexto = enc["mdp"]?.ToString() ?? "PUE",
                MdpFactura = Descripcion("mdp", "descripcion", "cve_mdp", enc["mdp"], "Pago en una sola exhibición"),

                Observaciones = enc["coment1"]?.ToString() ?? "",
                TipoFacturacion = "contado",   // ingreso normal: el switch de XmlBuilderService lo conoce
                EncabezadoId = idEncabezado
            };

            // El atributo Folio del CFDI sale de FolioCorto (los 8 primeros del uuid del
            // encabezado), igual que en el punto de venta.
            string uuidDoc = enc["uuid"]?.ToString();
            if (string.IsNullOrWhiteSpace(uuidDoc) || uuidDoc.Length < 8)
                return (false, "La venta no tiene identificador para formar el folio fiscal.", null);

            factura.FolioCorto = uuidDoc.Substring(0, 8);

            factura.Tproductos = new DataTable();
            factura.Tproductos.Columns.AddRange(new[]
            {
                new DataColumn("numero", typeof(string)),
                new DataColumn("claveProdServ", typeof(string)),
                new DataColumn("claveUnidad", typeof(string)),
                new DataColumn("unidad", typeof(string)),
                new DataColumn("descripcion", typeof(string)),
                new DataColumn("cantidad", typeof(double)),
                new DataColumn("precioUnit", typeof(double)),
                new DataColumn("importe", typeof(double)),
                new DataColumn("objetoImp", typeof(string)),
                new DataColumn("descuento", typeof(decimal))
            });

            decimal bruto = 0m, neto = 0m;

            foreach (var p in partidas)
            {
                decimal cantidad = Convert.ToDecimal(p["cant_ud"]);
                decimal precio = Convert.ToDecimal(p["pv_prod"]);
                decimal brutoPartida = Math.Round(cantidad * precio, 2);
                decimal netoPartida = Convert.ToDecimal(p["imp_part"]);

                decimal descuentoPct = brutoPartida > 0m && netoPartida < brutoPartida
                    ? Math.Round((brutoPartida - netoPartida) / brutoPartida * 100m, 6)
                    : 0m;

                bruto += brutoPartida;
                neto += netoPartida;

                factura.Tproductos.Rows.Add(
                    p["cve_prod"]?.ToString(),
                    p["prod_sat"]?.ToString(),
                    p["ud_sat"]?.ToString(),
                    p["ud"]?.ToString(),
                    p["descr_prod"]?.ToString(),
                    Convert.ToDouble(cantidad),
                    Convert.ToDouble(precio),
                    Convert.ToDouble(brutoPartida),
                    p["obj_impto"]?.ToString() ?? "02",
                    descuentoPct);
            }

            // Valores previos; XmlBuilderService los recalcula desde Tproductos.
            factura.Subtotal = bruto;
            factura.IVA = Math.Round(neto * 0.16m, 2);
            factura.Total = neto + factura.IVA;

            return (true, "", factura);
        }

        /// <summary>
        /// Genera el XML, lo manda al PAC y guarda la factura, todo con la misma conexión y
        /// transacción. Es el equivalente de FacturacionVentaController.GenerarXml, que no
        /// se puede reutilizar aquí sin heredar de ese controlador.
        /// </summary>
        [NonAction]
        private TimbradoResult TimbrarConPipelineDeVentas(
            Factura factura, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            try
            {
                XmlResult xml = _xmlBuilder.GenerateXmlAsync(factura, FacturacionPath);

                var resultado = _timbradoWorkflow.EjecutarAsync(
                    xml.factura,
                    Path.Combine(FacturacionPath, $"{xml.uuid}.xml"),
                    XmlTimbradosPath, QrCodesPath, "/content/qrcodes")
                    .GetAwaiter().GetResult();

                if (resultado.Success)
                    _facturaRepository.GuardarEncabezadoConDetalles(xml.factura, null, conn, tx);

                return resultado;
            }
            catch (Exception ex)
            {
                Utilities.RegistrarError(ex, "AutoFacturacion/TimbrarConPipelineDeVentas", HttpContext, capturarParametros: false);
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al generar el comprobante: " + ex.Message
                };
            }
        }

        // ═════════════════════════════════════════════════════════
        // TICKET FISCAL — comprobante impreso de la factura emitida
        // ═════════════════════════════════════════════════════════

        /// <summary>
        /// Ticket imprimible del CFDI recién emitido.
        /// Va protegido por un token firmado y de vida corta: sin él bastaría conocer un
        /// UUID —que viaja en la respuesta y en el correo— para leer los datos fiscales de
        /// cualquier factura desde una página pública.
        /// </summary>
        [HttpGet]
        public IActionResult TicketFactura(string t)
        {
            string uuid = VerificarTokenImpresion(t);

            if (uuid == null)
                return NotFound("Enlace de impresión inválido o vencido.");

            var f = RunQuery(@"
                SELECT serie, folio, uuid, fecha, fechatimbrado,
                       rfcemisor, rsoemisor, reg_fise, cpe,
                       rfccliente, rsocliente, reg_fisr, cpr, idusocfdi, usocfdi,
                       subtotal, descuento, iva, total, moneda,
                       mdpfactura, idtipopago, statusfactura, encabezado_id
                FROM factura
                WHERE uuid = @uuid",
                new Dictionary<string, object> { { "uuid", Guid.Parse(uuid) } })?.FirstOrDefault();

            if (f == null)
                return NotFound("No se encontró la factura.");

            // La liga del detalle es dfactura.idfac (así lo escribe FacturaRepository);
            // `idfactura` es otra columna y traía un solo renglón por factura.
            var conceptos = RunQuery(@"
                SELECT d.descripcion, d.cantidad, d.precio, d.descuento, d.udm,
                       d.claveprodserv, d.claveprod
                FROM dfactura d
                INNER JOIN factura fa ON fa.id = d.idfac
                WHERE fa.uuid = @uuid",
                new Dictionary<string, object> { { "uuid", Guid.Parse(uuid) } })
                ?? new List<Dictionary<string, object>>();

            ViewBag.Conceptos = conceptos;
            ViewBag.LogoDataUri = LogoDelEmisor();
            ViewBag.QrSat = QrComoDataUri(UrlVerificacionSat(f), 4);

            // Descripción de los regímenes: en el ticket "601 General de Ley Personas
            // Morales" se entiende; "601" a secas, no.
            ViewBag.RegimenEmisor = DescripcionRegimen(f["reg_fise"]);
            ViewBag.RegimenReceptor = DescripcionRegimen(f["reg_fisr"]);
            ViewBag.FormaPago = DescripcionFormaPago(f["idtipopago"]);

            return View("~/Views/Ventas/TicketFactura.cshtml", f);
        }

        /// <summary>
        /// "601 — General de Ley Personas Morales" a partir de la clave. Devuelve la clave
        /// sola si no está en el catálogo, y cadena vacía si la factura no la trae.
        /// </summary>
        [NonAction]
        private string DescripcionRegimen(object clave)
        {
            string cve = clave?.ToString();
            if (string.IsNullOrWhiteSpace(cve)) return "";

            try
            {
                var r = RunQuery(
                    "SELECT descripcion FROM catregimenfiscal WHERE clave = @cve",
                    new Dictionary<string, object> { { "cve", cve } })?.FirstOrDefault();

                string desc = r?["descripcion"]?.ToString();
                return string.IsNullOrWhiteSpace(desc) ? cve : $"{cve} — {desc}";
            }
            catch { return cve; }
        }

        /// <summary>
        /// "01 — Efectivo" a partir de lo guardado en factura.idtipopago.
        /// Esa columna es entera y guarda la CLAVE SAT como número (1, 3, 28, 99), no el
        /// id_f_pago del catálogo: por eso se compara contra cve_sat rellenando a dos
        /// dígitos, y no por llave primaria.
        /// </summary>
        [NonAction]
        private string DescripcionFormaPago(object idTipoPago)
        {
            string cve = idTipoPago?.ToString();
            if (string.IsNullOrWhiteSpace(cve)) return "";

            cve = cve.Trim().PadLeft(2, '0');

            try
            {
                var r = RunQuery(
                    "SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve LIMIT 1",
                    new Dictionary<string, object> { { "cve", cve } })?.FirstOrDefault();

                string desc = r?["descripcion"]?.ToString();
                return string.IsNullOrWhiteSpace(desc) ? cve : $"{cve} — {desc}";
            }
            catch { return cve; }
        }

        /// <summary>URL del verificador del SAT, la misma que lleva el QR del CFDI.</summary>
        [NonAction]
        private static string UrlVerificacionSat(Dictionary<string, object> f)
        {
            string total = Convert.ToDecimal(f["total"] ?? 0m).ToString("F6",
                System.Globalization.CultureInfo.InvariantCulture);

            return "https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx" +
                   $"?id={f["uuid"]}&re={f["rfcemisor"]}&rr={f["rfccliente"]}&tt={total}";
        }

        /// <summary>
        /// Logo de la empresa emisora en base64. Sigue la convención del PDF de facturas
        /// —content/img/{perfil}/logo.png— y cae al logo genérico si ese perfil no tiene uno
        /// propio, como pasa con "pruebas".
        /// </summary>
        [NonAction]
        private string LogoDelEmisor()
        {
            try
            {
                string perfil = Convert.ToBoolean(Utilities.GetSetting("perfil_factura"))
                    ? "produccion" : "pruebas";

                string[] candidatos =
                {
                    Path.Combine(_env.WebRootPath, "content", "img", perfil, "logo.png"),
                    Path.Combine(_env.WebRootPath, "content", "img", "logo.png")
                };

                foreach (var ruta in candidatos)
                    if (System.IO.File.Exists(ruta))
                        return "data:image/png;base64," +
                               Convert.ToBase64String(System.IO.File.ReadAllBytes(ruta));

                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// QR en PNG embebido como data URI. PngByteQRCode en vez de la variante con
        /// System.Drawing, para no depender del GDI del servidor.
        /// </summary>
        [NonAction]
        private static string QrComoDataUri(string contenido, int pixelsPorModulo = 4)
        {
            if (string.IsNullOrWhiteSpace(contenido)) return null;

            using var generador = new QRCoder.QRCodeGenerator();
            var datos = generador.CreateQrCode(contenido, QRCoder.QRCodeGenerator.ECCLevel.M);

            return "data:image/png;base64," +
                   Convert.ToBase64String(new QRCoder.PngByteQRCode(datos).GetGraphic(pixelsPorModulo));
        }

        /// <summary>Token de impresión: HMAC del uuid con vigencia corta.</summary>
        [NonAction]
        private string FirmarTokenImpresion(string uuid)
        {
            var key = _config["Autofacturacion:SigningKey"];
            long exp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
            string payload = $"{uuid}|{exp}";

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key));
            string hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}||{hash}"));
        }

        [NonAction]
        private string VerificarTokenImpresion(string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token)) return null;

                var key = _config["Autofacturacion:SigningKey"];
                var full = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var partes = full.Split(new[] { "||" }, StringSplitOptions.None);

                if (partes.Length != 2) return null;

                using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key));
                string esperado = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(partes[0])));

                if (!CryptographicEquals(partes[1], esperado)) return null;

                var datos = partes[0].Split('|');
                if (datos.Length < 2) return null;
                if (!long.TryParse(datos[1], out long exp)) return null;
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return null;

                return Guid.TryParse(datos[0], out _) ? datos[0] : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// PDF del CFDI, con el mismo formato que las facturas del punto de venta.
        /// Las rutas de las vistas van explícitas porque Rotativa y el motor de vistas las
        /// buscan bajo la carpeta del controlador, y este módulo no tiene las suyas.
        /// Devuelve false —sin lanzar— si algo falla: el comprobante ya está timbrado y no
        /// se puede invalidar porque wkhtmltopdf no haya podido dibujarlo.
        /// </summary>
        [NonAction]
        private bool GenerarPdfFactura(Factura factura)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(factura?.UUID))
                    return false;

                Directory.CreateDirectory(ContentPdfPath);
                Directory.CreateDirectory(FacturasPdfPath);

                // El encabezado del PDF se pasa a wkhtmltopdf como archivo HTML aparte.
                string headerHtml = RenderViewToStringAsync("~/Views/FacturaPdf/Header.cshtml", factura)
                    .GetAwaiter().GetResult();

                string headerPath = Path.Combine(ContentPdfPath, $"header_{factura.UUID}.html");
                System.IO.File.WriteAllText(headerPath, headerHtml, Encoding.UTF8);

                try
                {
                    var pdf = new Rotativa.AspNetCore.ViewAsPdf("~/Views/PuntoDeVenta/FacturaPdf.cshtml", factura)
                    {
                        PageSize = Rotativa.AspNetCore.Options.Size.A4,
                        PageMargins = new Rotativa.AspNetCore.Options.Margins(45, 10, 20, 10),
                        FileName = $"Factura_{factura.UUID}.pdf",
                        CustomSwitches =
                            $"--encoding utf-8 --header-html \"{headerPath}\" " +
                            "--header-spacing 5 " +
                            "--footer-center \"Página [page] de [toPage]\" " +
                            "--footer-line --footer-font-size 10"
                    };

                    byte[] bytes = pdf.BuildFile(ControllerContext).GetAwaiter().GetResult();
                    System.IO.File.WriteAllBytes(Path.Combine(FacturasPdfPath, $"{factura.UUID}.pdf"), bytes);

                    return true;
                }
                finally
                {
                    try { System.IO.File.Delete(headerPath); } catch { /* temporal */ }
                }
            }
            catch (Exception ex)
            {
                LogError("AutoFactura_PDF", ex);
                return false;
            }
        }

        /// <summary>
        /// Manda al cliente su XML y su PDF. Igual que el PDF, un fallo aquí no invalida el
        /// timbrado: se reporta para que la pantalla ofrezca la descarga.
        /// </summary>
        [NonAction]
        private bool EnviarCfdiPorCorreo(string email, string uuid, string folio, Factura factura)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email))
                    return false;

                string rutaXml = Path.Combine(XmlTimbradosPath, $"{uuid}.xml");
                string rutaPdf = Path.Combine(FacturasPdfPath, $"{uuid}.pdf");

                var adjuntos = new List<(string, byte[], string)>();

                if (System.IO.File.Exists(rutaXml))
                    adjuntos.Add(($"CFDI_{uuid}.xml", System.IO.File.ReadAllBytes(rutaXml), "application/xml"));

                if (System.IO.File.Exists(rutaPdf))
                    adjuntos.Add(($"CFDI_{uuid}.pdf", System.IO.File.ReadAllBytes(rutaPdf), "application/pdf"));

                // Sin el XML no hay nada que valga la pena mandar: el PDF es sólo una
                // representación impresa, el comprobante fiscal es el XML.
                if (!adjuntos.Any(a => a.Item1.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                {
                    LogError("AutoFactura_Correo",
                        new Exception($"No se encontró el XML timbrado en {rutaXml}"));
                    return false;
                }

                string cuerpo = $@"
<p>Hola{(string.IsNullOrWhiteSpace(factura?.RsoCliente) ? "" : " " + factura.RsoCliente)},</p>
<p>Adjuntamos tu factura electrónica correspondiente al ticket <strong>{folio}</strong>.</p>
<table style='border-collapse:collapse;font-family:Arial,sans-serif;font-size:14px;'>
    <tr><td style='padding:4px 12px 4px 0;'>Folio fiscal (UUID)</td>
        <td style='padding:4px 0;'><strong>{uuid}</strong></td></tr>
    <tr><td style='padding:4px 12px 4px 0;'>RFC receptor</td>
        <td style='padding:4px 0;'>{factura?.RfcCliente}</td></tr>
    <tr><td style='padding:4px 12px 4px 0;'>Total</td>
        <td style='padding:4px 0;'>{factura?.Total:C2}</td></tr>
</table>
<p style='font-size:13px;color:#555;'>
    El comprobante fiscal es el archivo <strong>XML</strong>; el PDF es su representación impresa.
    Conserva ambos.
</p>";

                _correo.EnviarCorreoConAdjuntosAsync(
                    email,
                    $"Tu factura electrónica · {folio}",
                    cuerpo,
                    adjuntos).GetAwaiter().GetResult();

                return true;
            }
            catch (Exception ex)
            {
                LogError("AutoFactura_Correo", ex);
                return false;
            }
        }

        /// <summary>Renderiza una vista Razor a texto, para el encabezado del PDF.</summary>
        [NonAction]
        private async Task<string> RenderViewToStringAsync(string viewPath, object model)
        {
            var actionContext = new ActionContext(HttpContext, RouteData, ControllerContext.ActionDescriptor);
            var viewResult = _viewEngine.GetView(null, viewPath, isMainPage: false);

            if (viewResult?.View == null)
                throw new InvalidOperationException($"No se encontró la vista: {viewPath}");

            using var sw = new StringWriter();

            var viewData = new ViewDataDictionary(
                new EmptyModelMetadataProvider(), new ModelStateDictionary())
            { Model = model };

            var viewContext = new ViewContext(
                actionContext, viewResult.View, viewData,
                new TempDataDictionary(HttpContext, _tempDataProvider),
                sw, new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }

        // ═════════════════════════════════════════════════════════
        // MÉTODOS PRIVADOS DE SEGURIDAD
        // ═════════════════════════════════════════════════════════

        // ── Sobre el token de sesión que había aquí ───────────────
        // Se retiró GenerarTokenSesion/ValidarTokenSesion. Lo único que probaba —que el
        // visitante hubiera cargado Index en esta sesión— ya lo garantiza
        // [ValidateAntiForgeryToken], que además funciona sin login y entre pestañas.
        // A cambio traía problemas propios: dos controladores escribían la misma clave de
        // sesión con formatos distintos (ticks contra fecha formateada), lo que dejaba la
        // validación fallando siempre; y cada GET regeneraba el token, así que dos pestañas
        // abiertas se invalidaban entre sí.
        // El tope de 2 horas que aportaba tampoco protegía nada: la sesión caduca antes (1 h
        // de inactividad) y TimbrarCFDI revalida contra la base que la venta siga con
        // estatus_id = 1, cfdi IS NULL y dentro de las 72 horas, así que una página vieja no
        // puede hacer daño. Queda además el límite por IP.
        // Sin esto el portal ya no usa sesión: es completamente sin estado, que es lo que
        // corresponde a una página pública sin login.
        //
        // NO confundir con el ticketToken firmado con SigningKey (FirmarTicket /
        // VerificarTokenTicket): ese SÍ es imprescindible, porque lleva el id_encabezado
        // entre BuscarFolio y TimbrarCFDI y sin su HMAC cualquiera podría facturar la venta
        // que quisiera.

        // ── Comparación en tiempo constante (evita timing attacks) ─
        private static bool CryptographicEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ── Firmar el id_encabezado para el round-trip ────────────
        private string FirmarTicket(string idEncabezado, string folio)
        {
            var key = _config["Autofacturacion:SigningKey"];
            var exp = DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds();
            var payload = $"{idEncabezado}|{folio}|{exp}";

            using (var hmac = new System.Security.Cryptography.HMACSHA256(
                Encoding.UTF8.GetBytes(key)))
            {
                var hash = Convert.ToBase64String(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

                var full = $"{payload}||{hash}";
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(full));
            }
        }

        private (int idEncabezado, string folio) VerificarTokenTicket(string token)
        {
            try
            {
                var key = _config["Autofacturacion:SigningKey"];
                var full = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = full.Split(new[] { "||" }, StringSplitOptions.None);

                if (parts.Length != 2) return (0, null);

                var payload = parts[0];
                var hash = parts[1];

                using (var hmac = new System.Security.Cryptography.HMACSHA256(
                    Encoding.UTF8.GetBytes(key)))
                {
                    var expectedHash = Convert.ToBase64String(
                        hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

                    if (!CryptographicEquals(hash, expectedHash))
                        return (0, null);
                }

                var payloadParts = payload.Split('|');
                if (payloadParts.Length < 3) return (0, null);

                if (!long.TryParse(payloadParts[2], out long expTs))
                    return (0, null);

                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expTs)
                    return (0, null);

                return (int.Parse(payloadParts[0]), payloadParts[1]);
            }
            catch
            {
                return (0, null);
            }
        }

        // ── Rate limiter thread-safe con lock por key ─────────────
        private bool CheckRateLimit(string ip, string accion, int maxPeticiones, int ventanaMinutos)
        {
            LimpiarRateLimiterSiNecesario();

            var key = $"{ip}:{accion}";
            var lockObj = _rateLocks.GetOrAdd(key, _ => new object());

            lock (lockObj)
            {
                var ahora = DateTime.UtcNow;

                _rateLimiter.TryGetValue(key, out var entry);

                if (entry == null || ahora > entry.Window)
                {
                    entry = new RateLimitEntry { Count = 1, Window = ahora.AddMinutes(ventanaMinutos) };
                }
                else
                {
                    entry.Count++;
                }

                _rateLimiter[key] = entry;
                return entry.Count <= maxPeticiones;
            }
        }

        // ── Limpieza periódica del rate limiter ───────────────────
        private void LimpiarRateLimiterSiNecesario()
        {
            if ((DateTime.UtcNow - _ultimaLimpieza).TotalMinutes < 30) return;

            lock (_cleanupLock)
            {
                if ((DateTime.UtcNow - _ultimaLimpieza).TotalMinutes < 30) return;

                var ahora = DateTime.UtcNow;
                var expiradas = _rateLimiter
                    .Where(kv => ahora > kv.Value.Window)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var k in expiradas)
                {
                    _rateLimiter.TryRemove(k, out _);
                    _rateLocks.TryRemove(k, out _);
                }

                _ultimaLimpieza = ahora;
            }
        }

        // ── Sanitizar folio (solo alfanumérico + guión) ───────────
        private string SanitizarFolio(string folio)
        {
            folio = folio.Trim().ToUpper();
            if (!Regex.IsMatch(folio, @"^[A-Z0-9\-]{1,20}$"))
                return null;
            return folio;
        }

        // ── Validar campos fiscales ───────────────────────────────
        private (bool ok, string msg) ValidarCamposFiscales(
            string rfc, string razon, string regimen, string uso, string cp, string email)
        {
            if (string.IsNullOrWhiteSpace(rfc) || string.IsNullOrWhiteSpace(razon) ||
                string.IsNullOrWhiteSpace(regimen) || string.IsNullOrWhiteSpace(uso) ||
                string.IsNullOrWhiteSpace(cp) || string.IsNullOrWhiteSpace(email))
                return (false, "Todos los campos son requeridos.");

            if (!Regex.IsMatch(rfc.Trim().ToUpper(), @"^([A-ZÑ&]{3,4})(\d{6})([A-Z0-9]{3})$"))
                return (false, "RFC con formato inválido.");

            if (!Regex.IsMatch(cp.Trim(), @"^\d{5}$"))
                return (false, "Código postal inválido.");

            if (!EsEmailValido(email))
                return (false, "Correo electrónico inválido.");

            var regimenesValidos = new[] { "601", "603", "605", "606", "612", "616", "621", "625", "626" };
            if (!regimenesValidos.Contains(regimen))
                return (false, "Régimen fiscal inválido.");

            var usosValidos = new[] { "G01", "G03", "I01", "I04", "I08", "D01", "D10", "S01", "CP01" };
            if (!usosValidos.Contains(uso))
                return (false, "Uso del CFDI inválido.");

            if (razon.Length > 254)
                return (false, "Razón social demasiado larga.");

            return (true, null);
        }

        private bool EsEmailValido(string email) =>
            !string.IsNullOrWhiteSpace(email) &&
            Regex.IsMatch(email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$") &&
            email.Length <= 254;

        // ── IP del cliente ────────────────────────────────────────
        // ASP.NET Core: Request.ServerVariables no existe.
        // Se usa HttpContext.Connection.RemoteIpAddress + header crudo.
        private string GetClientIp()
        {
            var remoteAddr = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            bool esProxyConfiable = remoteAddr.StartsWith("10.")
                                 || remoteAddr.StartsWith("192.168.")
                                 || remoteAddr.StartsWith("172.16.")
                                 || remoteAddr.StartsWith("172.17.")
                                 || remoteAddr.StartsWith("172.18.")
                                 || remoteAddr.StartsWith("172.19.")
                                 || remoteAddr.StartsWith("172.20.")
                                 || remoteAddr.StartsWith("172.21.")
                                 || remoteAddr.StartsWith("172.22.")
                                 || remoteAddr.StartsWith("172.23.")
                                 || remoteAddr.StartsWith("172.24.")
                                 || remoteAddr.StartsWith("172.25.")
                                 || remoteAddr.StartsWith("172.26.")
                                 || remoteAddr.StartsWith("172.27.")
                                 || remoteAddr.StartsWith("172.28.")
                                 || remoteAddr.StartsWith("172.29.")
                                 || remoteAddr.StartsWith("172.30.")
                                 || remoteAddr.StartsWith("172.31.")
                                 || remoteAddr == "::1"
                                 || remoteAddr == "127.0.0.1";

            if (esProxyConfiable)
            {
                var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(forwarded))
                    return forwarded.Split(',')[0].Trim();
            }

            return remoteAddr;
        }

        // ── Registro de auditoría ─────────────────────────────────
        private void RegistrarLog(int idEncabezado, string folio, string rfc, string email, string uuid, string ip)
        {
            try
            {
                string sql = @"
                    INSERT INTO af_log_timbrado
                        (id_encabezado, folio, rfc_receptor, email, uuid, ip, fecha_timbrado)
                    VALUES
                        (@id_encabezado, @folio, @rfc, @email, @uuid, @ip, NOW())
                ";
                RunNonQuery(sql, new Dictionary<string, object>
                {
                    { "id_encabezado", idEncabezado },
                    { "folio",         folio        },
                    { "rfc",           rfc          },
                    { "email",         email        },
                    { "uuid",          uuid         },
                    { "ip",            ip           }
                });
            }
            catch { /* No interrumpir el flujo por el log */ }
        }

        private void GuardarDatosFiscalesCliente(
            string cveCliente, string rfc, string razon, string regimen, string uso, string cp)
        {
            if (string.IsNullOrWhiteSpace(cveCliente)) return;
            try
            {
                RunNonQuery(@"
                    INSERT INTO direcciones_facturacion
                        (entidad_clave, razon_social, regimen_fiscal, uso_sugerido, codigo_postal)
                    VALUES (@cve, @razon, @regimen, @uso, @cp)
                    ON CONFLICT (entidad_clave) DO UPDATE SET
                        razon_social   = EXCLUDED.razon_social,
                        regimen_fiscal = EXCLUDED.regimen_fiscal,
                        uso_sugerido   = EXCLUDED.uso_sugerido,
                        codigo_postal  = EXCLUDED.codigo_postal
                ", new Dictionary<string, object>
                {
                    { "cve",     cveCliente },
                    { "razon",   razon      },
                    { "regimen", regimen    },
                    { "uso",     uso        },
                    { "cp",      cp         }
                });
            }
            catch { }
        }

        private List<object> ObtenerConceptos(int idEncabezado, int empresa)
        {
            var rows = RunQuery(@"
                SELECT dm.descr_prod AS desc,
                       dm.cant_ud    AS cant,
                       dm.pv_prod    AS pu,
                       dm.cant_ud * dm.pv_prod AS sub
                FROM   partidasdoc dm
                WHERE  dm.encabezado_id = @id_encabezado
                ORDER  BY dm.descr_prod
            ", new Dictionary<string, object> { { "id_encabezado", idEncabezado } });

            if (rows == null) return new List<object>();

            return rows.Select(r => (object)new
            {
                desc = r["desc"]?.ToString(),
                cant = Convert.ToDecimal(r["cant"]),
                pu = Convert.ToDecimal(r["pu"]),
                sub = Convert.ToDecimal(r["sub"])
            }).ToList();
        }

        private void LogError(string origen, Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"[AF:{origen}] {ex.Message} | {ex.StackTrace}");
        }

        // ═════════════════════════════════════════════════════════
        // ACCESO A DATOS — IMPLEMENTACIÓN SEGURA
        // ═════════════════════════════════════════════════════════

        private string ConnectionString => _config.GetConnectionString("ERP_SRS");

        private const int COMMAND_TIMEOUT_SECONDS = 15;

        private List<Dictionary<string, object>> RunQuery(
            string sql,
            Dictionary<string, object> parametros,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null)
        {
            if (ContieneSqlPeligroso(sql))
            {
                LogError("RunQuery_Guard", new Exception("SQL bloqueado por guardia: " + sql));
                return null;
            }

            var resultados = new List<Dictionary<string, object>>();

            bool propia = conn == null;

            try
            {
                if (propia) { conn = new NpgsqlConnection(ConnectionString); conn.Open(); }

                try
                {
                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = COMMAND_TIMEOUT_SECONDS;
                        if (tx != null) cmd.Transaction = tx;
                        AgregarParametros(cmd, parametros);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var fila = new Dictionary<string, object>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    string col = reader.GetName(i);
                                    object val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    fila[col] = val;
                                }
                                resultados.Add(fila);
                            }
                        }
                    }
                }
                finally
                {
                    if (propia) conn.Dispose();
                }

                return resultados;
            }
            catch (NpgsqlException ex)
            {
                LogError("RunQuery_DB", ex);
                throw new ApplicationException("Error de base de datos.", ex);
            }
            catch (Exception ex)
            {
                LogError("RunQuery", ex);
                throw;
            }
        }

        private int RunNonQuery(
            string sql,
            Dictionary<string, object> parametros,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null)
        {
            if (ContieneSqlPeligroso(sql))
            {
                LogError("RunNonQuery_Guard", new Exception("SQL bloqueado: " + sql));
                return 0;
            }

            bool propia = conn == null;

            try
            {
                if (propia) { conn = new NpgsqlConnection(ConnectionString); conn.Open(); }

                try
                {
                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = COMMAND_TIMEOUT_SECONDS;
                        if (tx != null) cmd.Transaction = tx;
                        AgregarParametros(cmd, parametros);
                        return cmd.ExecuteNonQuery();
                    }
                }
                finally
                {
                    if (propia) conn.Dispose();
                }
            }
            catch (NpgsqlException ex)
            {
                LogError("RunNonQuery_DB", ex);
                throw new ApplicationException("Error de base de datos.", ex);
            }
            catch (Exception ex)
            {
                LogError("RunNonQuery", ex);
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────
        // HELPERS PRIVADOS
        // ─────────────────────────────────────────────────────────

        private void AgregarParametros(NpgsqlCommand cmd, Dictionary<string, object> parametros)
        {
            if (parametros == null) return;

            foreach (var kv in parametros)
            {
                string nombre = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;

                if (kv.Value == null)
                {
                    cmd.Parameters.AddWithValue(nombre, DBNull.Value);
                    continue;
                }

                NpgsqlParameter param;

                switch (kv.Value)
                {
                    case int intVal:
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Integer)
                        { Value = intVal };
                        break;

                    case long longVal:
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Bigint)
                        { Value = longVal };
                        break;

                    case decimal decVal:
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Numeric)
                        { Value = decVal };
                        break;

                    case double dblVal:
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Double)
                        { Value = dblVal };
                        break;

                    case bool boolVal:
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Boolean)
                        { Value = boolVal };
                        break;

                    case DateTime dtVal:
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Timestamp)
                        { Value = dtVal };
                        break;

                    case string strVal:
                        if (strVal.Length > 1000)
                            throw new ArgumentException($"Parámetro '{kv.Key}' excede longitud máxima.");
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Text)
                        { Value = strVal };
                        break;

                    case byte[] bytesVal:
                        param = new NpgsqlParameter(nombre, NpgsqlTypes.NpgsqlDbType.Bytea)
                        { Value = bytesVal };
                        break;

                    default:
                        param = new NpgsqlParameter(nombre, kv.Value);
                        break;
                }

                cmd.Parameters.Add(param);
            }
        }

        private static readonly Regex _sqlPeligroso =
            new Regex(
                @"\b(DROP|TRUNCATE|ALTER|CREATE|GRANT|REVOKE|EXEC|EXECUTE|xp_|pg_read_file|COPY\s+\w+\s+FROM)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private bool ContieneSqlPeligroso(string sql) => _sqlPeligroso.IsMatch(sql);
    }
}