using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.CuentasContables;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Npgsql;
using System.Data;

namespace BOS_ERP.Controllers.Ventas
{
    [Authorize]
    public class ComplementoPagoController : FacturacionComplementoController
    {
        private readonly TimbradoOptions _timbrado;
        private readonly IConfiguration _configuration;

        public ComplementoPagoController(
            IOptions<TimbradoOptions> timbradoOptions,
            EmailSender emailSender,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            XmlBuilderService xmlBuilder,
            IConfiguration configuration)
            : base(emailSender, timbradoOptions, env, viewEngine, tempDataProvider, xmlBuilder)
        {
            _timbrado = timbradoOptions.Value;
            _configuration = configuration;
        }
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Credito y cobranza", Accion = "Creacion de Complemento de pago")]
        public async Task<(bool Success, string Message, object Data)> Guardar(IFormCollection fc)
        {
            bool borradoEjecutado = false;
            int docfol = 0;
            var resultadoTimbrado = new TimbradoResult();
            var datosDetallados = new Dictionary<string, object>();
            var usrParameter = new Dictionary<string, object>();

            try
            {
                LogErrorHelper.RegistrarLog(
                   modulo: "COMPLEMNTO DE PAGO",
                   uuid: "SIN_FOLIO",
                   mensaje: $"Inicia proceso de complemento de pago",
                   nivel: "DEBUG"
               );

                string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                                ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                                : "pruebas";
                
                string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? ""; 

                decimal total = decimal.Parse(fc["totalComplemento"]);

                // Parsear facturas
                string productosJson = fc["facturasJSON"];
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);

                // 🔹 VALIDAR SI HAY FACTURAS MANUALES
                bool hayFacturasManual = productos.Any(p => EsFacturaManual(p));

                Dictionary<string, object> usrId = null;
                Dictionary<string, object> primeraFacturaPadre = null; // 🔹 DECLARAR AQUÍ

                // Solo buscar datos de encabezado si NO todas son manuales
                if (!hayFacturasManual || productos.Any(p => !EsFacturaManual(p)))
                {
                    var facturaSistema = productos.FirstOrDefault(p => !EsFacturaManual(p));
                    if (facturaSistema != null)
                    {
                        string usrquery = @"SELECT em.centro_costos, em.cli_prov, em.refe, em.usr0, em.fch0, 
                                   em.usr1, em.fch1, em.usr2, em.fch2, em.usr3, em.fch3, em.usr4, em.fch4, 
                                   tipo_proceso, tipo_producto 
                                   FROM encabezadomov em 
                                   WHERE em.id_encabezado = @id";
                        usrParameter.Add("id", Convert.ToInt32(facturaSistema["id_encabezado"]));
                        usrId = RunQuery(usrquery, usrParameter)[0];
                    }
                }

                // Si todas son manuales, usar datos del cliente seleccionado
                if (usrId == null)
                {
                    // Obtener datos básicos del cliente
                    var clienteId = fc["cliente"];
                    var queryCliente = @"SELECT c.cve_cli as cli_prov, c.id_cliente as refe, 1 as centro_costos 
                                FROM catclientes c 
                                WHERE c.cve_cli = @cve_cli AND c.empresa_id = @empresa_id";
                    var paramCliente = new Dictionary<string, object>
                    {
                        {"cve_cli", clienteId},
                        {"empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"))}
                    };
                    var clienteData = RunQuery(queryCliente, paramCliente)[0];

                    usrId = new Dictionary<string, object>
                    {
                        {"cli_prov", clienteData["cli_prov"]},
                        {"refe", clienteData["refe"]},
                        {"centro_costos", clienteData["centro_costos"]},
                        {"usr0", GetUserId(User.Identity.Name)},
                        {"fch0", DateTime.Now},
                        {"usr1", GetUserId(User.Identity.Name)},
                        {"fch1", DateTime.Now},
                        {"usr2", DBNull.Value},
                        {"fch2", DBNull.Value},
                        {"usr3", DBNull.Value},
                        {"fch3", DBNull.Value},
                        {"usr4", DBNull.Value},
                        {"fch4", DBNull.Value}
                    };
                    usrParameter.Add("id", clienteData["refe"]);
                }


                LogErrorHelper.RegistrarLog(
                   modulo: "COMPLEMENTO DE PAGO",
                   uuid: "SIN_FOLIO",
                   mensaje: $"Inicia proceso de creacion de documento",
                   nivel: "DEBUG"
                );
                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 20;
                encabezado.IdTpDoc = 65;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "CPFAC";
                encabezado.ComentAut = fc["concepto"];
                encabezado.Coment1 = fc["comentarios"];
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = Convert.ToInt32(usrId["usr0"]);
                encabezado.Fch0 = (DateTime)usrId["fch0"];
                encabezado.Usr1 = Convert.ToInt32(usrId["usr1"]);
                encabezado.Fch1 = Convert.ToDateTime(usrId["fch1"] ?? DateTime.Now);
                encabezado.Usr2 = Convert.ToInt32(usrId["usr2"] ?? 0);
                encabezado.Fch2 = Convert.ToDateTime(usrId["fch2"] ?? DateTime.Now);
                encabezado.Usr3 = Convert.ToInt32(usrId["usr3"] ?? 0);
                encabezado.Fch3 = Convert.ToDateTime(usrId["fch3"] ?? DateTime.Now);
                encabezado.Usr4 = usrId["usr4"] == null || usrId["usr4"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(usrId["usr4"]);
                encabezado.Fch4 = usrId.ContainsKey("fch4") && usrId["fch4"] != null && usrId["fch4"] != DBNull.Value ? (DateTime?)usrId["fch4"] : null;
                encabezado.Usr5 = GetUserId(User.Identity.Name);
                encabezado.Fch5 = DateTime.Now;
                encabezado.Imp = total;
                encabezado.CliProv = usrId["cli_prov"].ToString();
                encabezado.Ref = Convert.ToInt32(usrId["refe"]);
                encabezado.Estatus = 11;
                encabezado.TipoPoceso = "complemento_pago";
                encabezado.CentroCostos = Convert.ToInt32(usrId["centro_costos"]);


                // 🔹 Obtener el primer id_encabezado válido (no manual)
                var primerProductoConEncabezado = productos.FirstOrDefault(p => !EsFacturaManual(p));
                if (primerProductoConEncabezado != null)
                {
                    encabezado.EncabezadoPadre = Convert.ToInt32(primerProductoConEncabezado["id_encabezado"]);
                }


                encabezado.Ccy = "PESOS";

                var partidas = new List<PartidaDocumento>();

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {

                            LogErrorHelper.RegistrarLog(
                               modulo: "COMPLEMENTO DE PAGO",
                               uuid: "SIN_FOLIO",
                               mensaje: $"Inicia transaccion",
                               nivel: "DEBUG"
                            );

                            var folio = GenerarDocumentoConPartidas(encabezado, new List<PartidaDocumento>(), conn, tx);
                            docfol = Convert.ToInt32(folio["IdEncabezado"]);

                            LogErrorHelper.RegistrarLog(
                               modulo: "COMPLEMENTO DE PAGO",
                               uuid: docfol.ToString(),
                               mensaje: $"Documento creado exitosamente",
                               nivel: "DEBUG"
                            );

                            // 🔹 PROCESAR DOCUMENTOS PAGADOS (manuales y del sistema)
                            string ccy = fc["moneda"].ToString() ?? "PESOS";
                            string moneda = "MXN"; // valor por defecto

                            if (ccy == "DLLS")
                                moneda = "USD";
                            else if (ccy == "EURO")
                                moneda = "EUR";
                            else if (ccy == "PESOS")
                                moneda = "MXN";

                            DateTime fechaPago;
                            string fechaPagoStr = fc["fecha-pago-real"];

                            if (string.IsNullOrWhiteSpace(fechaPagoStr) ||
                                !DateTime.TryParse(fechaPagoStr, out fechaPago))
                            {
                                throw new Exception("La fecha de pago es requerida y debe tener un formato válido.");
                            }

                            var fPadreModel = new List<PagoComplemento>
                            {
                                new PagoComplemento
                                {
                                    FechaPago = fechaPago,        // ← Fecha del formulario
                                    FormaPago = fc["forma-pago"].ToString() ?? "99",
                                    Moneda = moneda,
                                    TipoCambio = Convert.ToDecimal(fc["paridad"]),
                                    Monto = total,
                                    Documentos = new List<DocumentoPagado>()
                                }
                            };

                            LogErrorHelper.RegistrarLog(
                               modulo: "COMPLEMENTO DE PAGO",
                               uuid: docfol.ToString(),
                               mensaje: $"Pasando a proceso de poliza",
                               nivel: "DEBUG"
                            );

                            var parameters = new Dictionary<string, object>();
                            decimal totalFactura = Convert.ToDecimal(total); // total con IVA
                            decimal pagoP = productos.Sum(p => Convert.ToDecimal(p["total"]));

                            decimal importeBase = Math.Round(totalFactura / 1.16m, 2, MidpointRounding.AwayFromZero);
                            decimal ivaTotal = totalFactura - importeBase;

                            decimal proporcion = pagoP / totalFactura;
                            decimal ivaProporcional = Math.Round(ivaTotal * proporcion, 2, MidpointRounding.AwayFromZero);
                            decimal subtotal = Math.Round(pagoP - ivaProporcional, 2, MidpointRounding.AwayFromZero);

                            string query = "INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom) " +
                                            "VALUES (@encabezado_id, @iva, @subtotal, @importe, 1, 16, @prov_nom)";

                            parameters = new Dictionary<string, object>();
                            parameters.Add("encabezado_id", Convert.ToInt32(folio["IdEncabezado"]));
                            parameters.Add("iva", Convert.ToInt32(GetSetting("impuesto")));
                            parameters.Add("subtotal", subtotal);
                            parameters.Add("importe", ivaProporcional);
                            parameters.Add("prov_nom", usrId["cli_prov"].ToString());

                            RunUpdate(query, parameters, false, conn, tx);

                            List<int> carteraIds = new List<int>();

                            foreach (var producto in productos)
                            {
                                parameters = new Dictionary<string, object>();
                                parameters.Add("encabezado_id", Convert.ToInt32(producto["id_encabezado"]));
                                query = "SELECT id_cartera_cliente FROM cartera_clientes WHERE encabezado_id = @encabezado_id";
                                var carteraId = RunScalar(query, parameters, false, conn, tx);

                                if (carteraId == null || carteraId == DBNull.Value)
                                {
                                    throw new Exception(
                                        $"No existe cartera para el encabezado {producto["id_encabezado"]}"
                                    );
                                }

                                carteraIds.Add(Convert.ToInt32(carteraId));
                            }

                            List<PolizaData> polizaData = GenerarDatosPoliza(Convert.ToInt32(folio["IdEncabezado"]), fc["banco"].ToString(), carteraIds, conn, tx);
                            var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(folio["IdEncabezado"]), polizaData, false, null, conn, tx);

                            CobroClienteResult cob = CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Complemento, conn, tx);

                            AplicarCobros cobro = new AplicarCobros();
                            cobro.UsuarioId = GetUserId(User.Identity.Name);
                            cobro.ClienteId = Convert.ToInt32(usrId["refe"]);

                            cobro.CobrosIds.Add(cob.PagoId);             

                            // Procesar cada factura (manual o del sistema)
                            foreach (var producto in productos)
                            {
                                DocumentoPagado doc;
                                if (EsFacturaManual(producto))
                                {
                                    // 🔹 FACTURA MANUAL
                                    string folio_completo = producto.ContainsKey("folio") ? producto["folio"] : "";
                                    string serie_manual = producto.ContainsKey("serie") ? producto["serie"] : "";
                                    //string[] partesFolio = folio_completo.Split('-');

                                    doc = new DocumentoPagado
                                    {
                                        IdDocumento = producto.ContainsKey("uuid") ? producto["uuid"] : "",
                                        Serie = serie_manual, // ⭐ USAR SERIE DIRECTA
                                        Folio = folio_completo,
                                        MonedaDR = producto.ContainsKey("moneda") ? producto["moneda"] : "MXN",
                                        ImpSaldoAnt = Convert.ToDecimal(producto.ContainsKey("saldoAnterior") ? producto["saldoAnterior"] : "0"),
                                        ImpPagado = Convert.ToDecimal(producto.ContainsKey("importePagado") ? producto["importePagado"] : "0"),
                                        ImpSaldoInsoluto = Convert.ToDecimal(producto.ContainsKey("saldoInsoluto") ? producto["saldoInsoluto"] : "0"),
                                        MetodoPagoDR = producto.ContainsKey("metodoPago") ? producto["metodoPago"] : "PPD"
                                    };
                                }
                                else
                                {
                                    var facParameter = new Dictionary<string, object>();
                                    string getFacturaPadre = @"
                                        SELECT f.uuid, f.serie, f.folio, f.moneda, cc.saldo_pendiente, 
                                               cc.monto_total, cc.id_cartera_cliente
                                        FROM cartera_clientes cc                                 
                                        INNER JOIN factura f ON f.encabezado_id = cc.encabezado_id
                                        WHERE f.encabezado_id = @encabezado_id";

                                    facParameter.Add("encabezado_id", Convert.ToInt32(producto["id_encabezado"]));
                                    var facturaPadre = RunQuery(getFacturaPadre, facParameter, false, conn, tx)[0];
                                    // 🔹 Guardar la primera para usarla después
                                    if (primeraFacturaPadre == null)
                                    {
                                        primeraFacturaPadre = facturaPadre;
                                    }

                                    doc = new DocumentoPagado
                                    {
                                        IdDocumento = facturaPadre["uuid"]?.ToString(),
                                        Serie = facturaPadre["serie"]?.ToString(),
                                        Folio = facturaPadre["folio"]?.ToString(),
                                        MonedaDR = facturaPadre["moneda"]?.ToString(),
                                        ImpSaldoAnt = Convert.ToDecimal(facturaPadre["saldo_pendiente"]),
                                        ImpPagado = Convert.ToDecimal(producto["importePagado"]),
                                        ImpSaldoInsoluto = Convert.ToDecimal(facturaPadre["saldo_pendiente"]) -
                                                          Convert.ToDecimal(producto["importePagado"]),
                                        MetodoPagoDR = fc["metodo_pago"].ToString() ?? "PPD"
                                    };

                                    // Registrar en cartera solo si no es manual
                                }

                                fPadreModel[0].Documentos.Add(doc);

                                parameters = new Dictionary<string, object>();
                                parameters.Add("encabezado_id", Convert.ToInt32(producto["id_encabezado"]));
                                query = "SELECT id_cartera_cliente FROM cartera_clientes WHERE encabezado_id = @encabezado_id";
                                var carteraId = RunScalar(query, parameters, false, conn, tx);

                                if (carteraId == null || carteraId == DBNull.Value)
                                {
                                    throw new Exception(
                                        $"No existe cartera para el encabezado {producto["id_encabezado"]}"
                                    );
                                }

                                cobro.CarteraIds.Add(Convert.ToInt32(carteraId));
                            }

                            AplicarCobrosCliente(cobro, conn, tx);

                            string queryCliente = "";
                            if (!hayFacturasManual || productos.Any(p => !EsFacturaManual(p)))
                            {
                                queryCliente = @"
                                SELECT em.*, c.n_cli, c.cp, c.rfc, df.uso_sugerido, df.regimen_fiscal, cr.descripcion, c2.descripcion as cfditext
                                FROM encabezadomov em
                                LEFT JOIN catclientes c ON em.cli_prov = c.cve_cli AND c.empresa_id = @empresa_id
                                INNER JOIN direcciones_facturacion df ON df.entidad_clave = c.cve_cli
                                INNER JOIN catregimenfiscal cr ON cr.clave = df.regimen_fiscal
                                INNER JOIN catusocfdi c2 ON c2.clave = df.uso_sugerido 
                                WHERE em.id_encabezado = @id;";
                            }
                            else
                            {
                                queryCliente = @"
                                SELECT c.n_cli, c.cp, c.rfc, df.uso_sugerido, df.regimen_fiscal, cr.descripcion, c2.descripcion as cfditext
                                FROM catclientes c
                                INNER JOIN direcciones_facturacion df ON df.entidad_clave = c.cve_cli
                                INNER JOIN catregimenfiscal cr ON cr.clave = df.regimen_fiscal
                                INNER JOIN catusocfdi c2 ON c2.clave = df.uso_sugerido 
                                WHERE c.id_cliente = @id AND c.empresa_id = @empresa_id;";

                            }
                            usrParameter.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                            var cliente = RunQuery(queryCliente, usrParameter, false, conn, tx);

                            // 🔹 Determinar la moneda para la factura
                            string monedaFactura = "MXN";
                            if (primeraFacturaPadre != null)
                            {
                                monedaFactura = primeraFacturaPadre["moneda"]?.ToString() ?? "MXN";
                            }
                            else if (productos.Any())
                            {
                                // Si todas son manuales, usar la moneda de la primera
                                var primeraManual = productos[0];
                                monedaFactura = primeraManual.ContainsKey("moneda") ? primeraManual["moneda"] : "MXN";
                            }

                            // 🔹 Crear objeto Factura
                            var factura = new Factura();
                            factura.Serie = "CC";
                            factura.Folio = folio["folio_generado"].ToString();
                            factura.IdTipoPago = fc["forma-pago"];
                            factura.CpE = GetEmisores("CpE");
                            factura.RfcEmisor = GetEmisores("Rfc");
                            factura.RsoEmisor = GetEmisores("RazonSocial");
                            factura.Rege = GetEmisores("Regimen");
                            factura.FormaPago = fc["forma-pago"].ToString();
                            factura.RfcCliente = cliente[0]["rfc"].ToString();
                            factura.RsoCliente = cliente[0]["n_cli"].ToString();
                            factura.CpR = cliente[0]["cp"].ToString();
                            factura.IdUsoCFDI = fc["uso-cfdi"].ToString();
                            factura.Total = Convert.ToDecimal(fc["totalComplemento"]);
                            factura.CFDIText = cliente[0]["cfditext"].ToString();
                            factura.Regc = cliente[0]["regimen_fiscal"].ToString();
                            factura.regimenEText = cliente[0]["descripcion"].ToString();
                            factura.LugarExpedicion = GetEmisores("CpE");
                            factura.metodoPagoTexto = "PUE";
                            factura.Observaciones = fc["concepto"].ToString();
                            factura.Fecha = DateTime.Now;
                            factura.TipoFacturacion = "Complemento";
                            factura.FechaTimbrado = DateTime.Now.ToString();
                            factura.EncabezadoId = Convert.ToInt32(folio["IdEncabezado"]);
                            factura.Moneda = monedaFactura;
                            factura.TipoCambio = Convert.ToDecimal(fc["paridad"].ToString() ?? "1.00");
                            factura.Pagos = new DataTable();
                            factura.Pagos.Columns.Add("FechaPago", typeof(DateTime));
                            factura.Pagos.Columns.Add("FormaPago", typeof(string));
                            factura.Pagos.Columns.Add("Moneda", typeof(string));
                            factura.Pagos.Columns.Add("Monto", typeof(decimal));
                            factura.Pagos.Columns.Add("TipoCambio", typeof(decimal));
                            factura.Pagos.Columns.Add("Documentos", typeof(string));

                            var parameters1 = new Dictionary<string, object>();
                            parameters1.Add("encabezado", Convert.ToInt32(factura.EncabezadoId));

                            query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio FROM encabezadomov em WHERE id_encabezado = @encabezado";
                            var result = RunScalar(query, parameters1, false, conn, tx);

                            if (result == null)
                            {
                                throw new Exception("folio no encontrado para el encabezado.");
                            }

                            string uuid = result.ToString();


                            string folioCompleto = uuid;


                            factura.Folio = folioCompleto;
                            //factura.FolioCorto = folio["folio"].ToString();
                            var partes = folioCompleto.Split('-');

                            if (partes.Length >= 4)
                            {
                                factura.FolioCorto = $"{partes[1]}-{partes[3]}";
                            }
                            foreach (var pago in fPadreModel)
                            {
                                var row = factura.Pagos.NewRow();
                                row["FechaPago"] = pago.FechaPago;
                                row["FormaPago"] = pago.FormaPago;
                                row["Moneda"] = pago.Moneda;
                                row["Monto"] = pago.Monto;
                                row["TipoCambio"] = pago.TipoCambio;
                                row["Documentos"] = JsonConvert.SerializeObject(pago.Documentos);
                                factura.Pagos.Rows.Add(row);
                            }

                            resultadoTimbrado = GenerarXmlComplementoPago(factura, fPadreModel);

                            if (!resultadoTimbrado.Success)
                            {
                                throw new Exception("Error en timbrado: " + resultadoTimbrado.Message);
                            }

                            // ✅ Registrar en factura_complementos_pago cada factura incluida en este complemento
                            foreach (var producto in productos)
                            {
                                decimal saldoAntes = 0;
                                decimal montoPagado = 0;

                                if (EsFacturaManual(producto))
                                {
                                    saldoAntes = Convert.ToDecimal(producto.ContainsKey("saldoAnterior") ? producto["saldoAnterior"] : "0");
                                    montoPagado = Convert.ToDecimal(producto.ContainsKey("importePagado") ? producto["importePagado"] : "0");
                                }
                                else
                                {
                                    // Reutilizar el saldo que ya consultaste de cartera_clientes
                                    var paramSaldo = new Dictionary<string, object>
                                    {
                                        { "encabezado_id", Convert.ToInt32(producto["id_encabezado"]) }
                                    };
                                    var saldoResult = RunScalar(
                                        "SELECT saldo_pendiente FROM cartera_clientes WHERE encabezado_id = @encabezado_id",
                                        paramSaldo, false, conn, tx
                                    );

                                    saldoAntes = (saldoResult != null && saldoResult != DBNull.Value)
                                                  ? Convert.ToDecimal(saldoResult) : 0;
                                    montoPagado = Convert.ToDecimal(producto["importePagado"]);
                                }

                                decimal saldoDespues = saldoAntes - montoPagado;

                                // Las facturas manuales no tienen encabezado en el sistema → NULL
                                object idEncabezadoFactura = EsFacturaManual(producto)
                                    ? (object)DBNull.Value
                                    : Convert.ToInt32(producto["id_encabezado"]);

                                var paramRegistro = new Dictionary<string, object>
                                {
                                    { "id_encabezado_complemento", Convert.ToInt32(folio["IdEncabezado"]) },
                                    { "id_encabezado_factura",     idEncabezadoFactura },
                                    { "monto_aplicado",            montoPagado },
                                    { "fecha_aplicacion",          DateTime.Now },
                                    { "usuario_aplica",            GetUserId(User.Identity.Name) },
                                    { "observaciones",             fc["concepto"].ToString() ?? "" },
                                    { "saldo_antes",               saldoAntes },
                                    { "saldo_despues",             saldoDespues }
                                };

                                RunUpdate(@"
        INSERT INTO factura_complementos_pago
            (id_encabezado_complemento, id_encabezado_factura,
             monto_aplicado, fecha_aplicacion, usuario_aplica,
             observaciones, saldo_antes, saldo_despues)
        VALUES
            (@id_encabezado_complemento, @id_encabezado_factura,
             @monto_aplicado, @fecha_aplicacion, @usuario_aplica,
             @observaciones, @saldo_antes, @saldo_despues)",
                                    paramRegistro, false, conn, tx);
                            }


                            tx.Commit();

                            string mensajeFinal = resultadoTimbrado.Message;
                            if (!resultadoTimbrado.PdfGenerado)
                            {
                                mensajeFinal += ". ADVERTENCIA: El PDF no se pudo generar, pero la factura está timbrada correctamente.\nContacta con soporte para obtener tu factura";
                            }

                            datosDetallados = new Dictionary<string, object>
                            {
                                { "UUID", resultadoTimbrado.UUID },
                                { "Total", factura.Total },
                                { "Subtotal", factura.Total },
                                { "IVA", factura.IVA },
                                { "RFCCliente", factura.RfcCliente },
                                { "RazonSocialCliente", factura.RsoCliente },
                                { "Fecha", factura.Fecha.ToString("dd/MM/yyyy HH:mm:ss") },
                                { "Serie", factura.Serie },
                                { "Folio", factura.Folio },
                                { "EncabezadoId", factura.EncabezadoId },
                                { "PdfGenerado", resultadoTimbrado.PdfGenerado },
                                { "PdfUrl", resultadoTimbrado.PdfGenerado
                                    ? Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf")
                                    : null },
                                { "XmlUrl", Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml") },
                                { "CantidadProductos", factura.Tproductos.Rows.Count },
                                { "mensajeFinal", mensajeFinal}
                            };

                            return (true, mensajeFinal, datosDetallados);
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!borradoEjecutado && !resultadoTimbrado.Success)
                {
                    //BorradoFacturasIncorrectas(docfol, "factura", "CC");
                    borradoEjecutado = true;
                }

                LogErrorHelper.RegistrarLog(
                    modulo: "COMPLEMENTO DE PAGO",
                    uuid: "SIN_FOLIO",
                    mensaje: $"Algo Fallo {ex.Message}",
                    nivel: "ERROR"
                );
                return (false, "Error Al generar la factura: " + ex.Message, null);
            }
        }

        [HttpPost]
        [Route("ComplementoPago/ProcesarDocumentosAsync")]
        public async Task<JsonResult> ProcesarDocumentosAsync(IFormCollection fc)
        {
            string productosJson = fc["facturasJSON"];
            var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);

            // Contar facturas válidas
            int cantidadDocumentos = productos.Count;

            try
            {
                var resultado1 = await Guardar(fc);
                if (!resultado1.Success)
                    return Json(new { success = false, step = "GuardarDesdeDocumentos", error = resultado1.Message });

                var datosFactura = resultado1.Data as Dictionary<string, object>;

                return Json(new
                {
                    success = true,
                    message = "Todos los documentos fueron procesados y timbrados correctamente",
                    resumen = new
                    {
                        documentosProcesados = cantidadDocumentos,
                        tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                    },
                    detalles = new
                    {
                        pedidoId = resultado1.Data,
                        mensajeFinal = resultado1.Message,
                        // Datos de facturación
                        uuid = datosFactura?["UUID"],
                        total = datosFactura?["Total"],
                        subtotal = datosFactura?["Subtotal"],
                        iva = datosFactura?["IVA"],

                        // Datos del cliente
                        rfcCliente = datosFactura?["RFCCliente"],
                        razonSocial = datosFactura?["RazonSocialCliente"],

                        // Datos del comprobante
                        serie = datosFactura?["Serie"],
                        folio = datosFactura?["Folio"],
                        fecha = datosFactura?["Fecha"],
                        cantidadProductos = datosFactura?["CantidadProductos"],

                        // URLs de descarga
                        pdfUrl = datosFactura?["PdfUrl"],
                        xmlUrl = datosFactura?["XmlUrl"]
                    }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "ComplementoPago/ProcesarDocumentosAsync");
                return Json(new
                {
                    success = false,
                    message = "Error general en el proceso: " + ex.Message,
                    detalles = ex.StackTrace
                });
            }
        }

        private bool EsFacturaManual(Dictionary<string, string> factura)
        {
            // Si no tiene id_encabezado o es nulo/vacío, es manual
            return !factura.ContainsKey("id_encabezado") ||
                   string.IsNullOrWhiteSpace(factura["id_encabezado"]);
        }
    }
}