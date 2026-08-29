using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Models.CuentasContables;
using BOS_ERP.services.Facturacion;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json;
using Npgsql;
using System.Data;

namespace BOS_ERP.Controllers.Ventas
{
    [Authorize]
    public class VINotaCreditoController : NotaCreditoController
    {
        private readonly IConfiguration _configuration;

        public VINotaCreditoController(
            IConfiguration configuration,
            EmailSender emailSender,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            XmlBuilderService xmlService)
            : base(configuration, emailSender, env, viewEngine, tempDataProvider, xmlService)
        {
            _configuration = configuration;
        }
        [HttpGet]
        [Route("VINotaCredito/ObtenerInfoSaldoFactura")]
        public JsonResult ObtenerInfoSaldoFactura(int encabezadoId, decimal ncTotal = 0)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            try
            {
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        // Buscar la factura timbrada asociada al encabezado
                        var rows = RunQuery(@"
                            SELECT f.id, cc.saldo_pendiente AS saldo, f.total, f.mdpfactura
                            FROM   factura f
                            LEFT JOIN cartera_clientes cc  ON  cc.encabezado_id = f.encabezado_id
                            WHERE  f.encabezado_id = @eid
                              AND  f.statusfactura  = 'TIMBRADA'
                            ORDER BY f.id DESC LIMIT 1",
                            new Dictionary<string, object> { ["eid"] = encabezadoId });

                        if (rows.Count == 0)
                            return Json(new { ok = false, message = "Factura no encontrada" });

                        var r = rows[0];
                        int facturaId = Convert.ToInt32(r["id"]);
                        string metodo = r["mdpfactura"]?.ToString()?.Trim() ?? "PUE";
                        decimal saldo = Convert.ToDecimal(r["saldo"]);
                        decimal total = Convert.ToDecimal(r["total"]);
                        bool liquidada = saldo <= 0;
                        bool esPUE = metodo == "PUE" || liquidada;

                        // Determinar scenario
                        string scenario;
                        bool mostrarSplit = false;
                        decimal montoSugerido_deuda = 0;
                        decimal montoSugerido_cartera = 0;

                        if (esPUE || liquidada)
                        {
                            scenario = "pue_o_liquidada";
                            montoSugerido_cartera = ncTotal;
                        }
                        else if (ncTotal > 0 && ncTotal > saldo)
                        {
                            scenario = "ppd_overflow";
                            mostrarSplit = true;
                            montoSugerido_deuda = saldo;
                            montoSugerido_cartera = ncTotal - saldo;
                        }
                        else
                        {
                            scenario = "ppd_normal";
                        }

                        tx.Rollback();  // sólo lectura

                        return Json(new
                        {
                            ok = true,
                            facturaId,
                            metodo_pago = metodo,
                            saldo_pendiente = saldo,
                            total_factura = total,
                            liquidada,
                            scenario,
                            mostrar_split = mostrarSplit,
                            sugerido_deuda = montoSugerido_deuda,
                            sugerido_cartera = montoSugerido_cartera
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentaNotaCredito/ObtenerInfoSaldoFactura");
                return Json(new { ok = false, message = ex.Message });
            }
        }


        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industriales", Accion = "Creacion de nota de credito")]
        public async Task<(bool Success, string Message, object Data)> GuardarDocumento(
            IFormCollection fc, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            try
            {
                var parameters = new Dictionary<string, object>();

                // ── Validaciones previas ──────────────────────────────────────
                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    throw new Exception("Debe seleccionar un cliente.");

                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    throw new Exception("Debe seleccionar una moneda.");

                if (string.IsNullOrWhiteSpace(fc["tipoNC"].ToString()))
                    throw new Exception("Debe indicar el tipo de nota de crédito.");

                if (string.IsNullOrWhiteSpace(fc["partidas"].ToString()))
                    throw new Exception("Debe agregar al menos una partida.");

                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["partidas"].ToString());
                if (productos == null || productos.Count == 0)
                    throw new Exception("Debe agregar al menos una partida.");

                // ── Recuperar datos del usuario / flujo de firmas ─────────────
                int idEncabezadoPadre = GetUserId(User.Identity.Name);
                int usrId0 = 0; DateTime usrFch0 = DateTime.Now;
                int usrId1 = 0; DateTime usrFch1 = DateTime.Now;
                int usrId2 = 0; DateTime usrFch2 = DateTime.Now;
                int centroCostos = 0;
                decimal dto = 0;

                if (!string.IsNullOrWhiteSpace(fc["encabezadoId"].ToString()))
                {
                    var usrParam = new Dictionary<string, object> { ["id"] = Convert.ToInt32(fc["encabezadoId"].ToString()) };
                    string usrQuery =
                        "SELECT centro_costos, dto1 ,usr0, fch0, usr1, fch1, usr2, fch2 " +
                        "FROM encabezadomov WHERE id_encabezado = @id";

                    var usrResult = RunQuery(usrQuery, usrParam, false, conn, tx);
                    if (usrResult.Count > 0)
                    {
                        var r = usrResult[0];
                        usrId0 = Convert.ToInt32(r["usr0"]);
                        dto = r["dto1"] == DBNull.Value ? 0m : Convert.ToDecimal(r["dto1"]);
                        centroCostos = Convert.ToInt32(r["centro_costos"]);
                        usrFch0 = Convert.ToDateTime(r["fch0"]);
                        usrId1 = Convert.ToInt32(r["usr1"]);
                        usrFch1 = Convert.ToDateTime(r["fch1"]);
                        usrId2 = Convert.ToInt32(r["usr2"]);
                        usrFch2 = Convert.ToDateTime(r["fch2"]);
                        idEncabezadoPadre = Convert.ToInt32(fc["encabezadoId"].ToString());
                    }
                }

                // ── Datos del cliente ─────────────────────────────────────────
                int idCliente = 0;
                string clienteNombre = "";
                if (!string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                {
                    var cliParam = new Dictionary<string, object>
                    {
                        ["cliente"] = Convert.ToInt32(fc["idCliente"].ToString()),
                        ["empresa_id"] = HttpContext.Session.GetInt32("Empresa") ?? 0
                    };
                    var cliResult = RunQuery(
                        "SELECT id_cliente, n_cli FROM catclientes " +
                        "WHERE id_cliente = @cliente AND empresa_id = @empresa_id",
                        cliParam, false, conn, tx);

                    if (cliResult.Count > 0)
                    {
                        idCliente = Convert.ToInt32(cliResult[0]["id_cliente"]);
                        clienteNombre = cliResult[0]["n_cli"].ToString();
                    }
                }

                // ── Forma de pago ─────────────────────────────────────────────
                var fPagoParam = new Dictionary<string, object> { ["cve_sat"] = fc["forma-pago"].ToString() };


                string tipoProceso = (fc["tipoNC"].ToString().ToLower()) switch
                {
                    "descuento" => "descuento_bonificacion",
                    "precio" => "descuento_bonificacion",
                    "bonificacion" => "descuento_bonificacion",
                    _ => "devolucion"
                };


                // ── Encabezado del movimiento ─────────────────────────────────
                var encabezado = new DocumentoEncabezado();
                var paridadStr = fc["paridad"].ToString();
                encabezado.EmpresaId = HttpContext.Session.GetInt32("Empresa") ?? 0; 
                encabezado.IdArea = 4;
                encabezado.IdTpDoc = 81;          // Tipo de documento para NC — ajusta según tu catálog;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = HttpContext.Session.GetInt32("Sucursal") ?? 0;
                encabezado.Alm = fc["almacen"].ToString();
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "VINC";       // Tipo de movimiento para Nota de Crédito V;
                encabezado.ComentAut = fc["comentarios"].ToString();
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = usrId0;
                encabezado.Fch0 = usrFch0;
                encabezado.Usr1 = usrId1;
                encabezado.Fch1 = usrFch1;
                encabezado.Usr2 = usrId2;
                encabezado.Fch2 = usrFch2;
                encabezado.Usr3 = GetUserId(User.Identity.Name);
                encabezado.Fch3 = DateTime.Now;
                encabezado.Imp = Convert.ToDecimal(fc["total"].ToString());
                encabezado.Sub = Convert.ToDecimal(fc["subtotal"].ToString());
                encabezado.Dto = dto;
                encabezado.CliProv = fc["cliente"].ToString();
                encabezado.Ref = idCliente;
                encabezado.Ccy = fc["moneda"].ToString();
                encabezado.Estatus = 11;
                encabezado.VdrCpr = fc["vendedor"].ToString();
                encabezado.Coment1 = fc["concepto"].ToString();
                encabezado.EncabezadoPadre = idEncabezadoPadre;
                encabezado.Par = decimal.TryParse(paridadStr, out var par) ? par : 1m;
                encabezado.FPago = Convert.ToInt32(RunScalar("SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat", fPagoParam));
                encabezado.Mdp = fc["metodo-pago"].ToString();
                encabezado.TipoPoceso = "nc_" + (tipoProceso ?? "devolucion");
                encabezado.CFDI = fc["uso-cfdi"].ToString();
                encabezado.CentroCostos = centroCostos;
                encabezado.NatDocPadreChar = fc["refs"].ToString(); // IDs de facturas relacionada;


                // ── Partidas ──────────────────────────────────────────────────
                var partidas = new List<PartidaDocumento>();
                foreach (var p in productos)
                {
                    var prodParam = new Dictionary<string, object>
                    {
                        ["cve_prod"] = p.ContainsKey("clave") ? p["clave"] : "",
                        ["empresa_id"] = HttpContext.Session.GetInt32("Empresa") ?? 0
                    };
                    int productId = Convert.ToInt32(RunScalar(
                        "SELECT id_catproductos FROM catproductos " +
                        "WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id",
                        prodParam, false, conn, tx));

                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
                        PvProd = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0,
                        Dto1 = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0,
                        ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) *
                                     (p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0),
                        Ud = p.ContainsKey("unidad") ? p["unidad"] : "PZA",
                        IdProducto = productId == 0 ? (int?)null : productId,
                        TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                    });
                }

                // ── Persistir documento ───────────────────────────────────────
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                // ── IVA en imp_oc ─────────────────────────────────────────────

                var formaPago = string.IsNullOrEmpty(fc["forma-pago"].ToString())? "99": fc["forma-pago"].ToString();
                var fpagoParam2 = new Dictionary<string, object> { ["f_pago_id"] = formaPago };
                int fp = Convert.ToInt32(RunScalar(
                    "SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @f_pago_id", fpagoParam2));

                RunQuery(
                    "INSERT INTO imp_oc " +
                    "(encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
                    "VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);",
                    new Dictionary<string, object>
                    {
                        ["encabezado_id"] = Convert.ToInt32(folio["IdEncabezado"]),
                        ["impuesto_id"] = Convert.ToInt32(GetSetting("impuesto")),  // IVA
                        ["subtotal"] = Convert.ToDecimal(fc["subtotal"].ToString()),
                        ["importe"] = Convert.ToDecimal(fc["iva"].ToString()),
                        ["orden_apl"] = 1,
                        ["imp_variable"] = 16,
                        ["prov_nom"] = clienteNombre,
                        ["f_pago_id"] = fp
                    }, false, conn, tx);

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString()
                };

                // ── Marcar encabezado padre como procesado ────────────────────
                parameters.Add("id", idEncabezadoPadre);
                //RunUpdate(
                //    "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id",
                //    parameters, false, conn, tx);

                return (true, "Documento de NC guardado correctamente", new
                {
                    Folio = folio["folio_generado"].ToString(),
                    IdEncabezado = Convert.ToInt32(folio["IdEncabezado"])
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentaNotaCredito/?");
                return (false, "Error al guardar el documento de NC: " + ex.Message, null);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PASO 2 — LLENAR EL MODELO Factura Y TIMBRAR LA NC
        //  Equivalente a VIFacturaController.GenerarFacturaVentas()
        //  Construye el objeto Factura con TipoDeComprobante = "E" y llama a
        //  GenerarXml() de NotaCreditoController para timbrar.
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost]
        [Route("VINotaCredito/GenerarNC")]
        [ValidateAntiForgeryToken]
        public async Task<(bool Success, string Message, object Data)> GenerarNotaCredito(
            NCContext ctx, Factura factura, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            LogErrorHelper.RegistrarLog(
                "Nota Credito Ventas Industriales",
                "SIN_FOLIO",
                "Ingreso a función GenerarNotaCredito",
                nivel: "DEBUG");

            string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                : "pruebas";
            TimbradoResult resultadoTimbrado;

            try
            {
                 string GetEmisor(string campo) =>
                    _configuration[$"emisores:{perfil}:{campo}"] ?? "";

                // ── Validaciones básicas ──────────────────────────────────────
                if (string.IsNullOrWhiteSpace(ctx.Form["rfc"].ToString()))
                    throw new Exception("Debe seleccionar un cliente.");

                if (string.IsNullOrWhiteSpace(ctx.Form["moneda"].ToString()))
                    throw new Exception("Debe seleccionar una moneda.");

                if (string.IsNullOrWhiteSpace(ctx.Form["tipoNC"].ToString()))
                    throw new Exception("Debe indicar el tipo de nota de crédito.");

                if (string.IsNullOrWhiteSpace(ctx.Form["partidas"].ToString()))
                    throw new Exception("Debe agregar al menos una partida.");

                var productos = JsonConvert.DeserializeObject<List<dynamic>>(ctx.Form["partidas"].ToString());
                if (productos == null || productos.Count == 0)
                    throw new Exception("Debe agregar al menos una partida.");

                // ── Catálogos ─────────────────────────────────────────────────
                var parameters = new Dictionary<string, object>();

                parameters["cve"] = ctx.Form["metodoPago"].ToString();
                string mdp1 = RunScalar(
                    "SELECT cve_mdp FROM mdp WHERE cve_mdp = @cve", parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                parameters["cve"] = ctx.Form["formaPago"].ToString();
                string tp = RunScalar(
                    "SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve", parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                parameters["cve"] = ctx.Form["usoCFDI"].ToString();
                string usoCFDItext = RunScalar(
                    "SELECT descripcion FROM catusocfdi WHERE clave = @cve", parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                parameters["cve"] = ctx.Form["regimen"].ToString();
                string regimenText = RunScalar(
                    "SELECT descripcion FROM catregimenfiscal WHERE clave = @cve",
                    parameters, false, conn, tx)?.ToString() ?? "General de Ley Personas Morales";
                parameters.Clear();

                // ── Datos del cliente ─────────────────────────────────────────
                parameters["cve_cli"] = Convert.ToInt32(ctx.Form["idCliente"].ToString());
                parameters["empresa_id"] = HttpContext.Session.GetInt32("Empresa");
                var datosCliente = RunQuery(
                    "SELECT id_cliente, cve_cli, n_cli, cp, rfc FROM catclientes " +
                    "WHERE id_cliente = @cve_cli AND empresa_id = @empresa_id",
                    parameters, false, conn, tx);

                if (datosCliente.Count == 0)
                    throw new Exception("No se encontraron los datos del cliente para generar la NC.");
                parameters.Clear();

                parameters["cve_cli"] = datosCliente[0]["cve_cli"];
                parameters["empresa_id"] = HttpContext.Session.GetInt32("Empresa");
                string rScocial = RunScalar(
                    "SELECT regimen_fiscal FROM direcciones_facturacion WHERE entidad_clave = @cve_cli AND empresa_id = @empresa_id",
                    parameters, false, conn, tx)?.ToString() ?? "601";

                parameters.Clear();

                // ── Correos del cliente ───────────────────────────────────────
                parameters["cliente_id"] = datosCliente[0]["id_cliente"];
                var datosCorreos = RunQuery(
                    "SELECT id_correo_cli, cliente_id, correo FROM correos_cliente WHERE cliente_id = @cliente_id ",
                    parameters, false, conn, tx);
                parameters.Clear();

                // ── Moneda ────────────────────────────────────────────────────
                string moneda;

                switch (ctx.Form["moneda"].ToString())
                {
                    case "PESOS":
                        moneda = "MXN";
                        break;

                    case "DLLS":
                        moneda = "USD";
                        break;

                    case "EURO":
                        moneda = "EUR";
                        break;

                    default:
                        moneda = ctx.Form["moneda"].ToString() ?? "MXN";
                        break;
                }

                // ── Folio desde encabezadomov ─────────────────────────────────
                parameters["encabezado"] = Convert.ToInt32(ctx.EncabezadoId);
                var folioResult = RunScalar(
                    "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                    "FROM encabezadomov em WHERE id_encabezado = @encabezado",
                    parameters, false, conn, tx);

                if (folioResult == null)
                    throw new Exception("Folio no encontrado para el encabezado de la NC.");

                string folioCompleto = folioResult.ToString();
                parameters.Clear();

                // ── Addenda (misma lógica que VIFacturaController) ────────────
                Addenda addenda = null;
                var pAdd = new Dictionary<string, object> { ["id_addenda"] = ctx.Form.ContainsKey("idAdenda") && int.TryParse(ctx.Form["idAdenda"].ToString(), out var temp) ? temp : 0 };

                var addendaDb = RunQuery(
                    "SELECT id_addenda, nombre, xml_namespace, xml_schema, xml_prefix, " +
                    "       usar_conceptos, version, data_template " +
                    "FROM cfdi_addenda_def " +
                    "WHERE activo = true AND id_addenda = @id_addenda LIMIT 1;",
                    pAdd, false, conn, tx);

                if (addendaDb.Count > 0)
                {
                    var rowAdd = addendaDb[0];
                    addenda = new Addenda
                    {
                        Tipo = rowAdd["nombre"]?.ToString() ?? "",
                        Namespace = rowAdd["xml_namespace"]?.ToString() ?? "",
                        SchemaLocation = rowAdd["xml_schema"]?.ToString() ?? "",
                        Prefix = rowAdd["xml_prefix"]?.ToString() ?? "add",
                        Options = new AddendaOptions
                        {
                            UsarConceptosCFDI = rowAdd["usar_conceptos"] != DBNull.Value &&
                                                Convert.ToBoolean(rowAdd["usar_conceptos"])
                        }
                    };

                    if (!string.IsNullOrWhiteSpace(rowAdd["data_template"]?.ToString()))
                    {
                        var templateJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                            rowAdd["data_template"].ToString());
                        templateJson["ordenCompra"] = ctx.Form["ordenCompra"].ToString() ?? "";
                        addenda.DatosTemplate = AplanarJSON(templateJson);
                    }
                }

                // ── Construir el objeto Factura (TipoDeComprobante = "E") ──────
                factura.Serie = ctx.Form["serie"].ToString() ?? "NC";
                factura.Folio = folioCompleto;
                factura.FolioCorto = ctx.Form["folio"].ToString();
                factura.TipoDeComprobante = "E";               // ← EGRESO = Nota de Crédito
                factura.TipoRelacion = "01";              // ← NC de documentos relacionados
                factura.Exportacion = "01";
                factura.IdTipoPago = ctx.Form["formaPago"].ToString();
                factura.Moneda = moneda;
                factura.CpE = GetEmisor("CpE");
                factura.LugarExpedicion = datosCliente[0]["cp"].ToString();
                factura.RfcEmisor = GetEmisor("Rfc");
                factura.RsoEmisor = GetEmisor("RazonSocial");
                factura.Rege = GetEmisor("Regimen");
                factura.RfcCliente = ctx.Form["rfc"].ToString();
                factura.RsoCliente = datosCliente[0]["n_cli"].ToString();
                factura.CpR = datosCliente[0]["cp"].ToString();
                factura.IdUsoCFDI = ctx.Form["usoCFDI"].ToString();
                factura.CFDIText = usoCFDItext;
                factura.Regc = rScocial;
                factura.regimenEText = regimenText;
                factura.metodoPagoTexto = mdp1 ?? "PUE";
                factura.formaPagoTexto = tp;
                factura.TipoFacturacion = "NC_" + ctx.Form["tipoNC"].ToString();      // devolucion | descuento | precio…
                factura.Observaciones = ctx.Form["concepto"].ToString();
                factura.Oc = ctx.Form["ticket"].ToString();       // ticket/caso de soporte
                factura.Fecha = DateTime.Now;
                factura.TipoCambio = Convert.ToDecimal(ctx.Form["paridad"].ToString() ?? "1");
                factura.Subtotal = Convert.ToDecimal(ctx.Form["subtotal"].ToString() ?? "0");
                factura.Descuento = Convert.ToDecimal(ctx.Form["descuentos"].ToString() ?? "0");
                factura.IVA = Convert.ToDecimal(ctx.Form["iva"].ToString() ?? "0");
                factura.IepsF = Convert.ToDecimal(ctx.Form["ieps"].ToString() ?? "0");
                factura.Total = Convert.ToDecimal(ctx.Form["total"].ToString() ?? "0");
                factura.Saldo = factura.Total;
                factura.EncabezadoId = Convert.ToInt32(ctx.EncabezadoId);
                factura.IdCliente = Convert.ToInt32(datosCliente[0]["id_cliente"]);
                factura.Addenda = addenda;

                // ── UUIDs de las facturas relacionadas ────────────────────────
                // El front envía refs=[id1,id2,...] — IDs internos de encabezado.
                string refsJson = ctx.Form["refs"].ToString() ?? "[]";
                var refIds = ParseIntArray(refsJson);
                var uuidsRel = ObtenerUUIDsDeFacturas(refIds);
                var uuidsComplementos = ObtenerUUIDsComplementosDePago(refIds, conn, tx);

                // Unir ambas listas sin duplicados
                var todosUuids = uuidsRel
                    .Concat(uuidsComplementos)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                factura.UUIDsRelacionados = string.Join(",", todosUuids);

                factura.TipoRelacion = uuidsComplementos.Any() ? "07" : "01";

                // ── Tproductos ────────────────────────────────────────────────
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
                    new DataColumn("descuento",     typeof(double)),
                    new DataColumn("iva",           typeof(double)),
                    new DataColumn("ieps",          typeof(double)),
                    new DataColumn("objetoImp",     typeof(string)),
                    new DataColumn("comentario",    typeof(string))
                });

                bool esStandalone = (ctx.Form["tipoNC"].ToString() ?? "") == "standalone";

                // Log para trazabilidad — útil al depurar rechazos del PAC
                LogErrorHelper.RegistrarLog(
                    "NC Ventas Industriales",
                    ctx.Form["folio"].ToString() ?? "SIN_FOLIO",
                    $"CfdiRelacionados → TipoRelacion={factura.TipoRelacion} | " +
                    $"Facturas={uuidsRel.Count} | Complementos={uuidsComplementos.Count} | " +
                    $"Total UUIDs={todosUuids.Count}",
                    nivel: "DEBUG");

                if (esStandalone)
                {
                    // ── STANDALONE: concepto único genérico, igual que anticipo ──────────
                    // El SAT no requiere desglose de partidas para una NC libre.
                    // Un solo concepto representa el crédito total al cliente.
                    string conceptoLibre = !string.IsNullOrWhiteSpace(ctx.Form["concepto"].ToString())
                        ? ctx.Form["concepto"].ToString()
                        : "Nota de crédito — ajuste a favor del cliente";

                    factura.Tproductos.Rows.Add(
                        "",                // numero / NoIdentificacion — vacío en NC libre
                        "84111506",        // claveProdServ SAT — mismo que anticipo
                        "ACT",             // claveUnidad SAT
                        "Actividad",       // unidad texto
                        conceptoLibre,     // descripcion — el concepto del usuario
                        1.0,               // cantidad = 1
                        (double)factura.Subtotal,   // valorUnitario = subtotal sin IVA
                        (double)factura.Subtotal,   // importe = cantidad × valorUnitario
                        0.0,               // descuento
                        16.0,              // iva
                        0.0,               // ieps
                        "02",              // objetoImp — sí objeto de impuesto
                        ""                 // comentario
                    );
                }
                else
                {
                    // ── NORMAL: buscar relación SAT por clave de producto ─────────────────
                    foreach (var prod in productos)
                    {
                        var pProd = new Dictionary<string, object> { ["cve"] = (string)prod.clave };
                        var datRel = RunQuery(
                            "SELECT prod_sat, ud_sat, obj_impto FROM catrelacion WHERE prod_kepler = @cve",
                            pProd, false, conn, tx);

                        if (datRel.Count == 0)
                            throw new Exception(
                                $"No se encontró relación SAT para el producto '{prod.productoId}'.");

                        pProd.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
                        string datudm = RunScalar(
                            "SELECT udm FROM catproductos WHERE cve_prod = @cve AND empresa_id = @empresa",
                            pProd, false, conn, tx)?.ToString() ?? "PZA";

                        var rowRel = datRel[0];
                        string prodSat = rowRel["prod_sat"]?.ToString()?.Trim();
                        string udSat = rowRel["ud_sat"]?.ToString()?.Trim();
                        string objImp = rowRel["obj_impto"]?.ToString()?.Trim();

                        var faltantes = new List<string>();
                        if (string.IsNullOrWhiteSpace(prodSat)) faltantes.Add("Clave SAT (prod_sat)");
                        if (string.IsNullOrWhiteSpace(udSat)) faltantes.Add("Unidad SAT (ud_sat)");
                        if (string.IsNullOrWhiteSpace(objImp)) faltantes.Add("Objeto de Impuesto (obj_impto)");

                        if (faltantes.Any())
                            throw new Exception(
                                $"El producto {prod.productoId} no tiene configurado: " +
                                string.Join(", ", faltantes));

                        double descPct = prod.descuento != null ? (double)prod.descuento : 0d;
                        double ivaPct = prod.iva != null ? (double)prod.iva : 16d;
                        double iepsPct = prod.ieps != null ? (double)prod.ieps : 0d;

                        factura.Tproductos.Rows.Add(
                            (string)prod.productoId,
                            prodSat,
                            udSat,
                            datudm,
                            (string)prod.descripcion,
                            (double)prod.cantidad,
                            (double)prod.precio,
                            (double)prod.precio * (double)prod.cantidad,
                            descPct,
                            ivaPct,
                            iepsPct,
                            prod.objetoImp != null ? (string)prod.objetoImp : objImp,
                            prod.comentario != null ? (string)prod.comentario : ""
                        );
                    }
                }

                List<PolizaData> poliza = GenerarDatosPoliza(Convert.ToInt32(ctx.EncabezadoId), null, null, conn, tx);
                var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(ctx.EncabezadoId), poliza, false, null, conn, tx);



                // ── Encabezado del movimiento ─────────────────────────────────
                var encabezado = new DocumentoEncabezado();

                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 7;
                encabezado.IdTpDoc = 86;          // Tipo de documento para NC — ajusta según tu catálog;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = HttpContext.Session.GetInt32("Sucursal");
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "EDC";
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.Imp = Convert.ToDecimal(ctx.Form["total"].ToString() ?? "0");
                encabezado.Sub = Convert.ToDecimal(ctx.Form["subtotal"].ToString() ?? "0");
                encabezado.Dto = Convert.ToDecimal(ctx.Form["descuentos"].ToString() ?? "0");
                encabezado.CliProv = datosCliente[0]["n_cli"].ToString();
                encabezado.Ref = Convert.ToInt32(datosCliente[0]["id_cliente"]);
                encabezado.Ccy = moneda;
                encabezado.Estatus = 11;
                encabezado.EncabezadoPadre = Convert.ToInt32(ctx.EncabezadoId);
                encabezado.Par = Convert.ToDecimal(ctx.Form["paridad"].ToString() ?? "1");
                encabezado.Mdp = mdp1 ?? "PUE";
                encabezado.TipoPoceso = "egreso_devolucion_cliente";
                encabezado.CentroCostos = poliza[0].Detalles[0].Centro;

                var partidas = new List<PartidaDocumento>();
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                poliza = GenerarDatosPoliza(Convert.ToInt32(folio["IdEncabezado"]), null, null, conn, tx);
                resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(folio["IdEncabezado"]), poliza, false, null, conn, tx);

                CobroClienteResult cobro = CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), Models.Carteras.Cliente.TipoCobroCliente.NotaCredito, conn, tx);
                string query = "SELECT id_cartera_cliente FROM cartera_clientes WHERE encabezado_id = @id";
                var r = RunScalar(query, new Dictionary<string, object> { ["id"] = Convert.ToInt32(Request.Form["encabezadoId"].ToString()) }, false, conn, tx);
                AplicarCobros aplicacion = new AplicarCobros();
                aplicacion.CarteraIds.Add(Convert.ToInt32(r));
                aplicacion.CobrosIds.Add(cobro.PagoId);
                aplicacion.UsuarioId = GetUserId(User.Identity.Name);
                aplicacion.ClienteId = Convert.ToInt32(datosCliente[0]["id_cliente"]);
                AplicarCobrosCliente(aplicacion, conn, tx);

                if (ctx.Form["decisionSaldo"].ToString() == "reembolso" ) {
                    RunUpdate("UPDATE encabezadomov SET reembolso = true WHERE id_encabezado = @id_encabezado ", new Dictionary<string, object> { ["id_encabezado"] = Convert.ToInt32(folio["IdEncabezado"]) }, false, conn, tx);
                    RunUpdate("UPDATE cobros_cliente SET referencia = @referencia WHERE id_cobro = @id_cobro", new Dictionary<string, object> { { "id_cobro" , cobro.PagoId }, { "referencia", ctx.Form["nc-ref-reembolso"].ToString() } }, false, conn, tx);          
                }

                // ── Timbrar ───────────────────────────────────────────────────
                resultadoTimbrado = GenerarXml(factura);

                ///.cshtml

                if (!resultadoTimbrado.Success)
                    throw new Exception("Error en timbrado: " + resultadoTimbrado.Message);

                // ── Actualizar estatus del encabezado ─────────────────────────
                RunUpdate(
                    "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id",
                    new Dictionary<string, object> { ["id"] = Convert.ToInt32(ctx.EncabezadoId) },
                    false, conn, tx);

                var datosDetallados = new Dictionary<string, object>
                {
                    ["UUID"] = resultadoTimbrado.UUID,
                    ["Total"] = factura.Total,
                    ["Subtotal"] = factura.Subtotal,
                    ["IVA"] = factura.IVA,
                    ["RFCCliente"] = factura.RfcCliente,
                    ["RazonSocialCliente"] = factura.RsoCliente,
                    ["Fecha"] = factura.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                    ["Serie"] = factura.Serie,
                    ["Folio"] = factura.Folio,
                    ["TipoNC"] = factura.TipoFacturacion,
                    ["EncabezadoId"] = factura.EncabezadoId,
                    ["PdfUrl"] = Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf"),
                    ["XmlUrl"] = Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml"),
                    ["CantidadPartidas"] = factura.Tproductos.Rows.Count,
                    ["CorreosCliente"] = datosCorreos.Select(d => new
                    {
                        id = d["id_correo_cli"],
                        correo = d["correo"].ToString()
                    }).ToList()
                };

                return (true, resultadoTimbrado.Message, datosDetallados);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentaNotaCredito/?");
                LogErrorHelper.RegistrarLog(
                    "NC Ventas Industriales", "SIN_FOLIO",
                    $"Error en GenerarNotaCredito: {ex.Message}", nivel: "ERROR");

                return (false, "Error al generar la nota de crédito: " + ex.Message, null);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ENDPOINT PÚBLICO — orquesta GuardarDocumento + GenerarNotaCredito
        //  Equivalente a VIFacturaController.ProcesarDocumentosAsync()
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost]
        [Route("VINotaCredito/ProcesarNCAsync")]
        public async Task<JsonResult> ProcesarNCAsync(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            var datosNC = new Dictionary<string, object>();
            var factura = new Factura();
            int cantDocs = 0;

            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        string idsDocumentos = fc["encabezadoId"].ToString();
                        if (!string.IsNullOrWhiteSpace(idsDocumentos))
                            cantDocs = idsDocumentos.Split(',').Length;

                        // ── PASO 1: Guardar documento interno ─────────────
                        var resultadoGuardar = await GuardarDocumento(fc, conn, tx);
                        if (!resultadoGuardar.Success)
                            return Json(new
                            {
                                success = false,
                                step = "GuardarDocumentoNC",
                                error = resultadoGuardar.Message
                            });

                        dynamic dataDoc = resultadoGuardar.Data;
                        int idEncabezado = dataDoc.IdEncabezado;

                        var ctx = new NCContext
                        {
                            Form = fc,
                            EncabezadoId = idEncabezado
                        };
                        //fc.Add("enc_id", idEncabezado.ToString());

                        // ── PASO 2: Generar XML y timbrar ─────────────────
                        var resultadoNC = await GenerarNotaCredito(ctx, factura, conn, tx);
                        if (!resultadoNC.Success)
                            return Json(new
                            {
                                success = false,
                                step = "GenerarNotaCredito",
                                error = resultadoNC.Message
                            });

                        datosNC = resultadoNC.Data as Dictionary<string, object>;

                        // ── PASO 3: Obtener id interno de la NC timbrada ──
                        int idFacturaNc = 0;
                        if (datosNC != null && datosNC.ContainsKey("UUID"))
                        {
                            string uuidNC = datosNC["UUID"]?.ToString();
                            var rowNC = RunQuery(
                                "SELECT id FROM factura WHERE uuid = @uuid::uuid LIMIT 1",
                                new Dictionary<string, object> { ["uuid"] = uuidNC },
                                false, conn, tx);
                            if (rowNC?.Count > 0)
                                idFacturaNc = Convert.ToInt32(rowNC[0]["id"]);
                        }

                        if (idFacturaNc == 0)
                        {
                            tx.Rollback();
                            return Json(new
                            {
                                success = false,
                                step = "RecuperarIdNC",
                                error = "No se pudo obtener el id interno de la NC timbrada"
                            });
                        }

                        // ── PASO 4: Aplicar la NC (lógica de saldo) ───────
                        var refIds = ParseIntArray(fc["refs"].ToString() ?? "[]");
                        int clienteId = Convert.ToInt32(
                            RunScalar(
                                "SELECT idcliente FROM factura WHERE id = @id",
                                new Dictionary<string, object> { ["id"] = idFacturaNc }));

                        // Leer decisión del usuario enviada desde el front
                        // (panel nuevo en paso 4 de la UI)
                        string decision = fc["decisionSaldo"].ToString() ?? "saldo_favor";
                        decimal montoDeuda = 0;
                        decimal montoCartera = 0;

                        if (decision == "split")
                        {
                            decimal.TryParse(fc["montoADeuda"].ToString(), out montoDeuda);
                            decimal.TryParse(fc["montoACartera"].ToString(), out montoCartera);
                        }

                        // Obtener facturas origen como IDs de tabla factura
                        // (refIds son encabezado_id, necesitamos factura.id)
                        var facturaOrigenIds = new List<int>();
                        foreach (int encId in refIds)
                        {
                            var rowF = RunQuery(
                                @"SELECT id FROM factura
                                  WHERE encabezado_id = @eid
                                    AND statusfactura = 'TIMBRADA'
                                  ORDER BY id DESC LIMIT 1",
                                new Dictionary<string, object> { ["eid"] = encId },
                                false, conn, tx);
                            if (rowF?.Count > 0)
                                facturaOrigenIds.Add(Convert.ToInt32(rowF[0]["id"]));
                        }

                        var appInput = new NCApplicationService.NCApplicationInput
                        {
                            NcFacturaId = idFacturaNc,
                            ClienteId = clienteId,
                            NcTotal = Convert.ToDecimal(fc["total"].ToString() ?? "0"),
                            Moneda = fc["moneda"].ToString() ?? "MXN",
                            TipoNC = fc["tipoNC"].ToString() ?? "devolucion",
                            UsuarioId = GetUserId(User.Identity.Name),
                            DecisionSaldo = decision,
                            MontoADeuda = montoDeuda,
                            MontoACartera = montoCartera,
                            OrigenFacturaIds = facturaOrigenIds
                        };

                        var appResult = NCApplicationService.AplicarNC(appInput, conn, tx);
                        if (!appResult.Success)
                        {
                            tx.Rollback();
                            return Json(new
                            {
                                success = false,
                                step = "AplicarNC",
                                error = appResult.Message
                            });
                        }

                        // ── PASO 5: Anticipos ─────────────────────────────────────────
                        string tipoNC = fc["tipoNC"].ToString() ?? "";
                        bool ncAfectaAnticipo = tipoNC == "devolucion" || tipoNC == "cancelacion";

                        if (ncAfectaAnticipo && refIds.Any())
                        {
                            string anticiposJson = fc["anticiposAplicar"].ToString() ?? "[]";
                            var anticiposAplicar = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(anticiposJson);

                            if (anticiposAplicar != null && anticiposAplicar.Count > 0)
                            {
                                foreach (var ant in anticiposAplicar)
                                {
                                    int faId = Convert.ToInt32(ant["faId"]);
                                    int anticipoId = Convert.ToInt32(ant["anticipoId"]);
                                    decimal montoApl = Convert.ToDecimal(ant["montoAplicar"]);
                                    if (montoApl <= 0) continue;

                                    // Leer saldo actual del anticipo (con bloqueo)
                                    var rowAnt = RunQuery(
                                        "SELECT saldo FROM factura WHERE id = @id FOR UPDATE",
                                        new Dictionary<string, object> { ["id"] = anticipoId },
                                        false, conn, tx);
                                    if (rowAnt == null || rowAnt.Count == 0) continue;

                                    decimal saldoAnt = Convert.ToDecimal(rowAnt[0]["saldo"]);

                                    // Lo máximo que podemos devolver es lo que se aplicó
                                    // menos lo que ya se devolvió (saldo actual ya contiene devoluciones previas)
                                    // Fórmula: el saldo actual del anticipo ya refleja el estado real,
                                    // así que devolvemos el monto solicitado sin exceder monto_aplicado original
                                    var rowFa = RunQuery(
                                        "SELECT monto_aplicado FROM factura_anticipos WHERE id = @id",
                                        new Dictionary<string, object> { ["id"] = faId },
                                        false, conn, tx);
                                    if (rowFa == null || rowFa.Count == 0) continue;

                                    decimal montoAplicadoOriginal = Convert.ToDecimal(rowFa[0]["monto_aplicado"]);
                                    decimal maxRecuperable = montoAplicadoOriginal - saldoAnt;
                                    // maxRecuperable = cuánto se aplicó que aún no se ha devuelto

                                    decimal montoReal = Math.Min(montoApl, maxRecuperable);
                                    if (montoReal <= 0) continue;

                                    decimal saldoNuevo = saldoAnt + montoReal;

                                    // Determinar encabezado de la factura principal para la traza
                                    int facturaOrigId = 0;
                                    int encIdAnt = ant.ContainsKey("encabezadoId")
                                        ? Convert.ToInt32(ant["encabezadoId"]) : 0;

                                    if (encIdAnt > 0)
                                    {
                                        var rowOrig = RunQuery(
                                            @"SELECT id FROM factura
                      WHERE encabezado_id = @eid
                        AND statusfactura = 'TIMBRADA'
                      ORDER BY id DESC LIMIT 1",
                                            new Dictionary<string, object> { ["eid"] = encIdAnt },
                                            false, conn, tx);
                                        if (rowOrig?.Count > 0)
                                            facturaOrigId = Convert.ToInt32(rowOrig[0]["id"]);
                                    }

                                    // ── REGISTRO INVERSO en factura_anticipos ──────────────────
                                    // Regla: NUNCA editar, siempre insertar movimiento compensatorio
                                    RunQuery(@"
                INSERT INTO factura_anticipos
                    (id_factura_principal, id_factura_anticipo,
                     monto_aplicado, fecha_aplicacion, usuario_aplica,
                     observaciones, saldo_antes, saldo_despues)
                VALUES
                    (@fac_principal, @fac_anticipo,
                     @monto_aplicado, NOW(), @usuario,
                     @obs, @saldo_antes, @saldo_despues)",
                                        new Dictionary<string, object>
                                        {
                                            ["fac_principal"] = facturaOrigId > 0 ? facturaOrigId
                                                                 : (object)DBNull.Value,
                                            ["fac_anticipo"] = anticipoId,
                                            // Negativo = reverso / desaplicación
                                            ["monto_aplicado"] = -montoReal,
                                            ["usuario"] = GetUserId(User.Identity.Name),
                                            ["obs"] = $"Reverso por nota de crédito {idEncabezado} — NC {fc["folio"].ToString() ?? ""}",
                                            ["saldo_antes"] = saldoAnt,
                                            ["saldo_despues"] = saldoNuevo,
                                        },
                                        false, conn, tx);

                                    // ── Actualizar saldo del anticipo en factura ────────────────
                                    RunQuery(
                                        "UPDATE factura SET saldo = @nuevo WHERE id = @id",
                                        new Dictionary<string, object>
                                        {
                                            ["nuevo"] = saldoNuevo,
                                            ["id"] = anticipoId
                                        },
                                        false, conn, tx);

                                    // ── Auditoría adicional: ligar NC con el anticipo revertido ─
                                    RunQuery(@"
                INSERT INTO nc_aplicacion_anticipo
                    (factura_nc_id, factura_ant_id, factura_final_id,
                     monto_acreditado, saldo_anticipo_antes,
                     saldo_anticipo_despues, usuario_id,
                     tipo_aplicacion, encabezado_id)
                VALUES
                    (@nc_id, @ant_id, @orig_id,
                     @monto, @sa, @sd, @usr,
                     @tipo, @enc_id)",
                                        new Dictionary<string, object>
                                        {
                                            ["nc_id"] = idFacturaNc,
                                            ["ant_id"] = anticipoId,
                                            ["orig_id"] = facturaOrigId,
                                            ["monto"] = montoReal,
                                            ["sa"] = saldoAnt,
                                            ["sd"] = saldoNuevo,
                                            ["usr"] = GetUserId(User.Identity.Name),
                                            ["tipo"] = montoReal >= montoAplicadoOriginal ? "REVERSO_TOTAL"
                                                                                             : "REVERSO_PARCIAL",
                                            ["enc_id"] = encIdAnt
                                        },
                                        false, conn, tx);
                                }
                            }
                        }

                        // ── PASO 6: Marcar documentos origen ─────────────
                        if (!string.IsNullOrWhiteSpace(idsDocumentos))
                        {
                            RunUpdate(
                                $"UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado IN ({idsDocumentos});",
                                parameters, false, conn, tx);
                        }

                        tx.Commit();

                        return Json(new
                        {
                            success = true,
                            message = "Nota de Crédito generada y aplicada correctamente",
                            resumen = new
                            {
                                documentosProcesados = cantDocs,
                                modoAplicacion = appResult.Modo,
                                montoDeuda = appResult.MontoDeuda,
                                montoCartera = appResult.MontoCartera,
                                saldoFavorId = appResult.SaldoFavorId,
                                tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                            },
                            detalles = new
                            {
                                documentoId = resultadoGuardar.Data,
                                uuid = datosNC?["UUID"],
                                total = datosNC?["Total"],
                                subtotal = datosNC?["Subtotal"],
                                iva = datosNC?["IVA"],
                                rfcCliente = datosNC?["RFCCliente"],
                                razonSocial = datosNC?["RazonSocialCliente"],
                                serie = datosNC?["Serie"],
                                folio = datosNC?["Folio"],
                                tipoNC = datosNC?["TipoNC"],
                                fecha = datosNC?["Fecha"],
                                cantidadPartidas = datosNC?["CantidadPartidas"],
                                pdfUrl = datosNC?["PdfUrl"],
                                xmlUrl = datosNC?["XmlUrl"],
                                correosCliente = datosNC?["CorreosCliente"]
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "VentaNotaCredito/?");
                        tx.Rollback();
                        return Json(new
                        {
                            success = false,
                            message = "Error general en el proceso de NC: " + ex.Message,
                            detalles = ex.StackTrace
                        });
                    }
                }
            }
        }
        // ══════════════════════════════════════════════════════════════════════
        //  VALIDAR PARTIDAS SAT — igual que VIFacturaController
        // ══════════════════════════════════════════════════════════════════════

        [HttpPost]
        [Route("VINotaCredito/ValidarPartidasSAT")]
        public async Task<JsonResult> ValidarPartidasSAT(IFormCollection fc)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                            return Json(new { success = false, message = "No hay partidas para validar.", resultados = new object[0] });

                        var productos = JsonConvert.DeserializeObject<List<dynamic>>(fc["productosJSON"].ToString());
                        if (productos == null || productos.Count == 0)
                            return Json(new { success = false, message = "No hay partidas para validar.", resultados = new object[0] });

                        var resultados = new List<object>();
                        bool todoValido = true;

                        foreach (var prod in productos)
                        {
                            string productoId = (string)prod.productoId;
                            var pProd = new Dictionary<string, object> { ["cve"] = productoId };
                            var dat = RunQuery(
                                "SELECT prod_sat, ud_sat, obj_impto FROM catrelacion WHERE prod_kepler = @cve",
                                pProd, false, conn, tx);

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

                            var row = dat[0];
                            string ps = row["prod_sat"]?.ToString()?.Trim() ?? "";
                            string us = row["ud_sat"]?.ToString()?.Trim() ?? "";
                            string oi = row["obj_impto"]?.ToString()?.Trim() ?? "";

                            var faltantes = new List<string>();
                            if (string.IsNullOrWhiteSpace(ps)) faltantes.Add("Clave SAT (prod_sat)");
                            if (string.IsNullOrWhiteSpace(us)) faltantes.Add("Unidad SAT (ud_sat)");
                            if (string.IsNullOrWhiteSpace(oi)) faltantes.Add("Objeto de Impuesto (obj_impto)");

                            bool esValido = faltantes.Count == 0;
                            if (!esValido) todoValido = false;

                            resultados.Add(new
                            {
                                productoId,
                                descripcion = (string)prod.descripcion,
                                valido = esValido,
                                camposFaltantes = faltantes,
                                prodSat = ps,
                                udSat = us,
                                objImp = oi
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
                        RegistrarErrorParaTicket(ex, "VentaNotaCredito/ValidarPartidasSAT");
                        tx.Rollback();
                        return Json(new { success = false, message = "Error al validar: " + ex.Message });
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HELPER — Aplanar JSON (mismo que VIFacturaController)
        // ══════════════════════════════════════════════════════════════════════

        private Dictionary<string, string> AplanarJSON(Dictionary<string, object> json, string prefijo = "")
        {
            var resultado = new Dictionary<string, string>();
            foreach (var kvp in json)
            {
                string key = string.IsNullOrEmpty(prefijo) ? kvp.Key : $"{prefijo}.{kvp.Key}";
                if (kvp.Value is Newtonsoft.Json.Linq.JObject jObj)
                {
                    var sub = AplanarJSON(jObj.ToObject<Dictionary<string, object>>(), key);
                    foreach (var s in sub) resultado[s.Key] = s.Value;
                }
                else
                {
                    resultado[key] = kvp.Value?.ToString() ?? "";
                }
            }
            return resultado;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HELPER — Parsear array de enteros (wrapper para el método base)
        // ══════════════════════════════════════════════════════════════════════

        private List<int> ParseIntArray(string json)
        {
            var list = new List<int>();
            try
            {
                var raw = JsonConvert.DeserializeObject<List<object>>(json);
                if (raw != null)
                    foreach (var item in raw)
                        if (int.TryParse(item?.ToString(), out int v))
                            list.Add(v);
            }
            catch { }
            return list;
        }

        [HttpPost]
        [Route("VINotaCredito/AplicarSaldoFavor")]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> AplicarSaldoFavor(IFormCollection fc)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        var input = new NCApplicationService.AplicarSaldoFavorInput
                        {
                            SaldoFavorId = Convert.ToInt32(fc["saldoFavorId"].ToString()),
                            FacturaDestinoId = Convert.ToInt32(fc["facturaDestinoId"].ToString()),
                            MontoAAplicar = Convert.ToDecimal(fc["montoAAplicar"].ToString()),
                            UsuarioId = GetUserId(User.Identity.Name)
                        };

                        var result = NCApplicationService.AplicarSaldoFavorAFactura(
                            input, conn, tx);

                        if (!result.Success)
                        {
                            tx.Rollback();
                            return Json(new { success = false, error = result.Message });
                        }

                        tx.Commit();
                        return Json(new
                        {
                            success = true,
                            message = result.Message,
                            saldoRestante = result.MontoCartera,
                            montoAplicado = result.MontoDeuda
                        });
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "VentaNotaCredito/AplicarSaldoFavor");
                        tx.Rollback();
                        return Json(new { success = false, error = ex.Message });
                    }
                }
            }
        }

        // Consulta de saldos disponibles para un cliente
        [HttpGet]
        [Route("VINotaCredito/SaldosDisponibles")]
        public JsonResult SaldosDisponibles(int clienteId)
        {
            var rows = RunQuery(@"
        SELECT
            sf.id,
            sf.monto_original,
            sf.monto_disponible,
            sf.moneda,
            sf.estado,
            sf.fecha_creacion,
            f.serie || '-' || f.folio AS nc_folio,
            f.uuid                    AS nc_uuid
        FROM nc_saldo_favor sf
        INNER JOIN factura f ON f.id = sf.nc_factura_id
        WHERE sf.cliente_id = @cli
          AND sf.estado IN ('disponible', 'parcialmente_aplicado')
          AND sf.monto_disponible > 0
        ORDER BY sf.fecha_creacion DESC",
                new Dictionary<string, object> { ["cli"] = clienteId });

            return Json(rows.Select(r => new
            {
                id = r["id"],
                montoOriginal = r["monto_original"],
                montoDisponible = r["monto_disponible"],
                moneda = r["moneda"],
                estado = r["estado"],
                ncFolio = r["nc_folio"],
                ncUuid = r["nc_uuid"],
                fechaCreacion = r["fecha_creacion"]
            }));
        }

        [HttpGet]
        [Route("VINotaCredito/ObtenerSolicitudNC")]
        public JsonResult ObtenerSolicitudNC(int encabezadoId)
        {
            try
            {
                var rows = RunQuery(@"
            SELECT
                s.id            AS solicitud_id,
                s.motivo_tipo,
                s.tipo_nc,
                s.comentario,
                s.prioridad,
                s.responsable,
                s.estatus_actual,
                s.folios_txt
            FROM nc_solicitud s
            INNER JOIN nc_solicitud_detalle sd
                ON sd.solicitud_id = s.id
            WHERE sd.encabezado_id = @eid
              AND s.estatus_actual  = 'PENDIENTE'
              AND sd.estatus_detalle = 'PENDIENTE'
            ORDER BY s.fecha_solicitud DESC
            LIMIT 1",
                    new Dictionary<string, object> { ["eid"] = encabezadoId });

                if (rows.Count == 0)
                    return Json(new { found = false });

                var r = rows[0];
                return Json(new
                {
                    found = true,
                    solicitudId = r["solicitud_id"],
                    motivoTipo = r["motivo_tipo"]?.ToString(),
                    tipoNC = r["tipo_nc"]?.ToString(),
                    comentario = r["comentario"]?.ToString(),
                    prioridad = r["prioridad"]?.ToString(),
                    responsable = r["responsable"]?.ToString(),
                    estatus = r["estatus_actual"]?.ToString(),
                    folios = r["folios_txt"]?.ToString(),
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VentaNotaCredito/ObtenerSolicitudNC");
                return Json(new { found = false, error = ex.Message });
            }
        }
    }

}
