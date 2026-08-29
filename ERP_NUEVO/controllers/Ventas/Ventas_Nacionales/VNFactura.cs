using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Npgsql;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Text;

namespace BOS_ERP.Controllers.Ventas.Ventas_Nacionales
{
    [Authorize]
    public class VNFacturaController : VentasFacturaBaseController
    {
        // Los servicios (_configuration, _emailSender, correoHelper, _env, _viewEngine,
        // _tempDataProvider, _xmlService) los guarda la base; antes se redeclaraban aquí y
        // correoHelper quedaba en null.
        public VNFacturaController(
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            IConfiguration configuration,
            BOS_ERP.Services.EmailSender emailSender,
            BOS_ERP.Helpers.CorreoHelper correoHelper,
            XmlBuilderService xmlService)
            : base(timbradoOptions, viewEngine, tempDataProvider, env,
                   configuration, emailSender, correoHelper, xmlService)
        {
        }

        // Tag para los logs del tracking de remisiones (ver VentasFacturaBaseController).
        protected override string FacturaLogTag => "VNFacturaController";

        // Identidad del documento. Nacional todavía usa su propio Guardar/GenerarFacturaVentas
        // (el flujo anterior, sin anticipos ni NC), pero la base ya exige declararla para poder
        // migrarlo al pipeline compartido sin tocar nada más.
        protected override int FacturaIdArea => 12;
        protected override int FacturaIdTpDoc => 44;
        protected override string FacturaTpMov => "VNFAC";
        protected override string FacturaSerie => "VN";

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Nacionales", Accion = "Creacion de cotizacion")]
        public async Task<(bool Success, string Message, object Data)> Guardar(IFormCollection fc, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string tipo = fc["tipo"].ToString()?.ToLower() ?? ""; // contado, credito, anticipo

                // 🔹 Validaciones previas
                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    throw new Exception("Debe seleccionar un cliente.");

                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    throw new Exception("Debe seleccionar una moneda.");

                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
                    throw new Exception("Debe seleccionar un vendedor.");

                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    throw new Exception("Debe seleccionar una forma de pago.");

                // 🔹 Remisiones (facturación parcial / múltiple) — tracking de saldos.
                //    productosJSON sigue mandando para el CFDI/partidas; el detalle de
                //    remisiones solo alimenta remision_partidas_facturadas / factura_remisiones_origen.
                List<RemisionPartidaParaFacturar> partidasRemision = null;
                List<int> remisionIds = new List<int>();

                string remParcialesJson = fc["remisionesParcialesJSON"].ToString();
                if (!string.IsNullOrWhiteSpace(remParcialesJson))
                {
                    try
                    {
                        partidasRemision = JsonConvert
                            .DeserializeObject<List<RemisionPartidaParaFacturar>>(remParcialesJson);
                    }
                    catch (Exception exJson)
                    {
                        throw new Exception("El detalle de remisiones enviado no es válido: " + exJson.Message);
                    }
                }

                string remIdsStr = fc["remisionesIds"].ToString();
                if (!string.IsNullOrWhiteSpace(remIdsStr))
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
                    remisionIds = remisionIds
                        .Union(partidasRemision.Select(p => p.RemisionId))
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();
                    ValidarDetalleRemisiones(partidasRemision, conn, tx);
                }

                // 🔹 Validar productos solo si NO es anticipo
                var fPagoParameter = new Dictionary<string, object>();
                fPagoParameter.Add("cve_sat", fc["forma-pago"].ToString());
                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
                if (tipo != "anticipo")
                {
                    string prodJson = fc["productosJSON"].ToString();
                    if (!string.IsNullOrWhiteSpace(prodJson))
                        productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(prodJson);

                    // Fallback: si el front no mandó productosJSON pero sí remisiones,
                    // se arma un renglón por partida de remisión (trazabilidad 1:1).
                    if ((productos == null || productos.Count == 0) && tieneRemisiones)
                        productos = ConvertirPartidasRemisionAProductos(partidasRemision, conn, tx);

                    if (productos == null || productos.Count == 0)
                        throw new Exception("Debe agregar al menos un producto o seleccionar remisiones.");

                    // 🔹 Validar precios y descuentos contra las reglas de precio
                    // Mismo criterio que ventas industriales (Helpers/ReglasPrecioHelper.cs).
                    TokenStore.LimpiarExpirados();
                    string usuarioReglas = User.Identity.Name;
                    string descuentoToken = fc["descuentoToken"].ToString() ?? "";
                    string precioToken = fc["precioToken"].ToString() ?? "";
                    int empresaReglas = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                    var reglas = this.ValidarPartidas(
                        empresaReglas,
                        this.ResolverClienteId(fc["cliente"].ToString(), empresaReglas, conn, tx),
                        productos,
                        TokenStore.Validar(precioToken, usuarioReglas, "CAMBIO DE PRECIO"),
                        TokenStore.Validar(descuentoToken, usuarioReglas, "DESCUENTO"),
                        conn, tx);

                    if (!reglas.Permitido)
                        throw new Exception(reglas.Mensaje);
                }

                // 🔹 Validar fecha de pago solo si es crédito
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaPago"].ToString()))
                        throw new Exception("Debe ingresar la fecha de pago (solo para crédito).");

                    if (!DateTime.TryParse(fc["fechaPago"].ToString(), out DateTime fch))
                        throw new Exception("Formato de fecha de pago no válido.");

                    fechaPago = fch;
                }
                else if (tipo == "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaAnticipo"].ToString()))
                        throw new Exception("Debe ingresar la fecha del anticipo.");
                    if (!DateTime.TryParse(fc["fechaAnticipo"].ToString(), out DateTime fch))
                        throw new Exception("Formato de fecha del anticipo no válido.");
                    fechaPago = fch;
                }
                // 🔹 Validar crédito — solo aplica cuando la venta es a crédito
                var credito = this.ValidarCreditoVenta(
                    fc["cliente"].ToString(),
                    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    CreditoVentasHelper.TotalDocumentoParaCredito(fc),
                    tipo,
                    int.TryParse(fc["documentid"].ToString(), out int docCredito) ? docCredito : 0,
                    null, null, fc["creditoToken"].ToString());

                if (!credito.Permitido)
                    throw new Exception(credito.Mensaje);

                int idEncabezadoPadre = GetUserId(User.Identity.Name);
                int usrId0 = 0;
                DateTime usrFch0 = DateTime.Now;
                int usrId1 = 0;
                DateTime usrFch1 = DateTime.Now;
                int usrId2 = 0;
                DateTime usrFch2 = DateTime.Now;

                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                {
                    // 🔹 Cargar datos del encabezado existente
                    var usrParameter = new Dictionary<string, object>();
                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3,  " +
                                      "       em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto " +
                                      "FROM encabezadomov em " +
                                      "WHERE em.id_encabezado = @id";

                    usrParameter.Add("id", Convert.ToInt32(fc["documentid"].ToString()));

                    var result = RunQuery(usrquery, usrParameter, false, conn, tx);

                    if (result.Count > 0)
                    {
                        var usrId = result[0];
                        usrId0 = Convert.ToInt32(usrId["usr0"]);
                        usrFch0 = Convert.ToDateTime(usrId["fch0"]);
                        usrId1 = Convert.ToInt32(usrId["usr1"]);
                        usrFch1 = Convert.ToDateTime(usrId["fch1"]);
                        usrId2 = Convert.ToInt32(usrId["usr2"]);
                        usrFch2 = Convert.ToDateTime(usrId["fch2"]);
                        idEncabezadoPadre = Convert.ToInt32(fc["documentid"].ToString());
                    }
                }
                else if (remisionIds.Count > 0)
                {
                    idEncabezadoPadre = remisionIds.First();
                }

                int idCliente = 0;
                string clienteNombre = "";
                if (!string.IsNullOrEmpty(fc["cliente"].ToString()))
                {
                    var clienteParameter = new Dictionary<string, object>();
                    string clientequery = "SELECT cl.id_cliente, cl.n_cli " +
                                          "FROM catclientes cl " +
                                          "WHERE cl.cve_cli = @cliente AND cl.empresa_id = @empresa_id";
                    clienteParameter.Add("cliente", fc["cliente"].ToString());
                    clienteParameter.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    var clienteResult = RunQuery(clientequery, clienteParameter, false, conn, tx);
                    if (clienteResult.Count > 0)
                    {
                        var cliente = clienteResult[0];
                        idCliente = Convert.ToInt32(cliente["id_cliente"]);
                        clienteNombre = cliente["n_cli"].ToString();

                    }
                }

                // 🔹 Crear encabezado
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 12,
                    IdTpDoc = 44,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = fc["almacen"].ToString(),
                    Fch = DateTime.Now,
                    TpMov = "VNFAC",
                    ComentAut = fc["comentarios"].ToString(),
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
                    Imp = Convert.ToDecimal(fc["total"].ToString()),
                    Sub = Convert.ToDecimal(fc["subtotal1"].ToString()),
                    CliProv = fc["cliente"].ToString(),
                    Ref = idCliente,
                    Ccy = fc["moneda"].ToString(),
                    Estatus = 11,
                    Flete = Convert.ToDecimal(fc["flete"].ToString()),
                    VdrCpr = fc["vendedor"].ToString(),
                    Coment1 = fc["concepto"].ToString(),
                    EncabezadoPadre = idEncabezadoPadre,
                    PlDias = string.IsNullOrWhiteSpace(fc["plazo"].ToString()) ? 0 : Convert.ToInt32(fc["plazo"].ToString()),
                    FchPgEntrega = fechaPago ?? DateTime.Now, // si no hay, usa fecha actual
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    FPago = Convert.ToInt32(RunScalar("select id_f_pago from cat_f_pago where cve_sat = @cve_sat", fPagoParameter)),
                    Mdp = fc["metodo-pago"].ToString(),
                    TipoPoceso = "factura_" + tipo,
                    CFDI = fc["uso-cfdi"].ToString(),
                    CentroCostos = Convert.ToInt32(fc["CentroCostosId"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString()
                };

                // 🔹 Crear partidas (solo si hay productos)
                var partidas = new List<PartidaDocumento>();
                if (productos.Count > 0)
                {
                    foreach (var p in productos)
                    {
                        var parametersP = new Dictionary<string, object>();
                        string queryId = "select id_catproductos from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id";
                        parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
                        parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        int productId = Convert.ToInt32(RunScalar(queryId, parametersP, false, conn, tx));

                        partidas.Add(new PartidaDocumento
                        {
                            CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
                            DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                            CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
                            PvProd = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0,
                            Dto1 = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0,
                            ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) *
                                      (p.ContainsKey("costoUnitario") ? decimal.Parse(p["costoUnitario"]) : 0),
                            Ud = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA",
                            IdProducto = productId,
                            TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                        });
                    }
                }

                // 🔹 Guardar documento
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                var fpagoParameter = new Dictionary<string, object>();
                string fpago = "select id_f_pago from cat_f_pago where cve_sat = @f_pago_id ";
                fpagoParameter.Add("f_pago_id", fc["forma-pago"].ToString());
                int fp = Convert.ToInt32(RunScalar(fpago, fpagoParameter, false, conn, tx));

                string inpuestos = "INSERT INTO imp_oc " +
                    "(encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
                    "VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);";
                var inpuestosParameter = new Dictionary<string, object>();

                inpuestosParameter.Add("encabezado_id", Convert.ToInt32(folio["IdEncabezado"]));
                inpuestosParameter.Add("impuesto_id", Convert.ToInt32(GetSetting("impuesto"))); // IVA
                inpuestosParameter.Add("subtotal", Convert.ToDecimal(fc["subtotal1"].ToString()));
                inpuestosParameter.Add("importe", Convert.ToDecimal(fc["iva"].ToString()));
                inpuestosParameter.Add("orden_apl", 1);
                inpuestosParameter.Add("imp_variable", 16);
                inpuestosParameter.Add("prov_nom", clienteNombre);
                inpuestosParameter.Add("f_pago_id", fp);

                RunQuery(inpuestos, inpuestosParameter, false, conn, tx);

                // 🔹 Tracking de saldos de remisiones facturadas (parcial o total)
                int idNuevaFactura = Convert.ToInt32(folio["IdEncabezado"]);
                if (tieneRemisiones)
                {
                    ActualizarPartidasRemisionesFacturadas(idNuevaFactura, partidasRemision, remisionIds, conn, tx);
                }
                else if (remisionIds.Count > 0)
                {
                    MarcarRemisionesComoFacturadas(remisionIds, idNuevaFactura, conn, tx);
                }

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString()
                };

                // Cerrar el documento fuente a 11 SOLO cuando la factura NO proviene del flujo
                // de remisiones (Especiales). En "Buscar Documentos" (documentid) se factura el
                // documento completo → se cierra a 11. En el flujo de remisiones, el estatus
                // (11 completa / 41 parcial) ya lo fijó ActualizarEstatusRemisionSiCompleta según
                // el tracking y NO debe pisarse.
                if (remisionIds.Count == 0 && !string.IsNullOrWhiteSpace(fc["documentid"].ToString()))
                {
                    // Igual que en Industriales: esta rama facturaba la remisión completa sin
                    // registrar origen ni sembrar saldos. Sólo aplica si el padre es remisión.
                    RegistrarOrigenSiEsRemision(idNuevaFactura, idEncabezadoPadre, conn, tx);

                    string query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                    parameters.Add("id", idEncabezadoPadre);
                    RunUpdate(query, parameters, false, conn, tx);
                }
                return (true, "Documentos guardados correctamente", new
                {
                    Folio = folio["folio_generado"].ToString(),
                    IdEncabezado = Convert.ToInt32(folio["IdEncabezado"])
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNFactura/?");
                return (false, "No se pudo guardar el documento de factura.", null);
            }
        }

        // ============================================================
        // Facturación desde remisiones (parcial / múltiple):
        //   los helpers de tracking (ValidarDetalleRemisiones, ActualizarPartidasRemisionesFacturadas,
        //   MarcarRemisionesComoFacturadas, InicializarPartidasSiFalta,
        //   ActualizarEstatusRemisionSiCompleta, ConvertirPartidasRemisionAProductos)
        //   ahora viven en VentasFacturaBaseController (compartidos entre canales).
        // ============================================================



        [Route("FacturacionVenta/Factura")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<(bool Success, string Message, object Data)> GenerarFacturaVentas(NCContext ctx, Factura factura, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            LogErrorHelper.RegistrarLog(
                    "Factura ventas industriales",
                    "SIN_FOLIO",
                    $"Ingreso a funcion de generar facturas ventas industriales",
                    nivel: "DEBUG"
            );

            string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                : "pruebas";
            bool borradoEjecutado = false; // 🔹 Bandera global
            TimbradoResult resultadoTimbrado;
            try
            {
                // Datos del emisor: se leen igual que en VIFactura, con la clave plana
                // "emisores:{perfil}:{campo}" de IConfiguration (appsettings).
                // Antes se hacía GetSection("Emisores:{perfil}")[$"{perfil}.Campo"], lo que
                // apuntaba a "Emisores:{perfil}:{perfil}.Campo" (clave inexistente) y devolvía
                // null → la factura salía sin RFC/RazónSocial/Régimen del emisor.
                string GetEmisor(string campo) =>
                    _configuration[$"emisores:{perfil}:{campo}"] ?? "";
                // 🔹 Tipo de facturación: contado, crédito, anticipo
                string tipo = ctx.Form["tipo"].ToString()?.ToLower() ?? "";

                // 🔹 Validaciones básicas
                if (string.IsNullOrWhiteSpace(ctx.Form["rfc"].ToString()))
                    throw new Exception("Debe seleccionar un cliente.");

                if (string.IsNullOrWhiteSpace(ctx.Form["moneda"].ToString()))
                    throw new Exception("Debe seleccionar una moneda.");

                if (string.IsNullOrWhiteSpace(ctx.Form["forma-pago"].ToString()))
                    throw new Exception("Debe seleccionar una forma de pago.");

                if (string.IsNullOrWhiteSpace(ctx.Form["metodo-pago"].ToString()))
                    throw new Exception("Debe seleccionar un método de pago.");

                // 🔹 Validar productos solo si NO es anticipo
                List<dynamic> productos = new List<dynamic>();
                List<dynamic> anticipo = new List<dynamic>();
                if (tipo != "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(ctx.Form["productosJSON"].ToString()))
                        throw new Exception("Debe agregar al menos un producto.");

                    productos = JsonConvert.DeserializeObject<List<dynamic>>(ctx.Form["productosJSON"].ToString());
                    if (productos == null || productos.Count == 0)
                        throw new Exception("Debe agregar al menos un producto.");
                }
                if (tipo == "contado" && !string.IsNullOrWhiteSpace(ctx.Form["anticiposJSON"].ToString()))
                {
                    anticipo = JsonConvert.DeserializeObject<List<dynamic>>(ctx.Form["anticiposJSON"].ToString());
                }

                // 🔹 Validar fecha de pago o anticipo
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (string.IsNullOrWhiteSpace(ctx.Form["FechaPago"].ToString()))
                        throw new Exception("Debe ingresar la fecha de pago (solo para crédito).");

                    if (!DateTime.TryParse(ctx.Form["FechaPago"].ToString(), out DateTime fch))
                        throw new Exception("Formato de fecha de pago no válido.");

                    fechaPago = fch;
                }
                else if (tipo == "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(ctx.Form["FechaAnticipo"].ToString()))
                        throw new Exception("Debe ingresar la fecha del anticipo.");

                    if (!DateTime.TryParse(ctx.Form["FechaAnticipo"].ToString(), out DateTime fch))
                        throw new Exception("Formato de fecha del anticipo no válido.");

                    fechaPago = fch;
                }

                // 🔹 Consultas para obtener descripciones (como en tu versión original)
                var parameters = new Dictionary<string, object>();
                string query = "";

                query = "SELECT descripcion FROM mdp WHERE cve_mdp = @cve;";
                parameters.Add("cve", ctx.Form["metodo-pago"].ToString());
                string mdp = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                query = "SELECT cve_mdp FROM mdp WHERE cve_mdp = @cve;";
                parameters.Add("cve", ctx.Form["metodo-pago"].ToString());
                string mdp1 = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                query = "SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve;";
                parameters.Add("cve", ctx.Form["forma-pago"].ToString());
                string tp = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                query = "SELECT descripcion FROM catusocfdi WHERE clave = @cve;";
                parameters.Add("cve", ctx.Form["uso-cfdi"].ToString());
                string usoCFDItext = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
                parameters.Clear();

                query = "SELECT descripcion FROM catregimenfiscal WHERE clave = @cve;";
                parameters.Add("cve", ctx.Form["RegimenFiscalReceptor"].ToString());
                string regimenText = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "General de Ley Personas Morales";
                parameters.Clear();

                query = "SELECT  id_cliente, n_cli, cp, rfc FROM catclientes where cve_cli = @cve_cli AND empresa_id = @empresa_id;";
                parameters.Add("cve_cli", ctx.Form["cliente"].ToString());
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var datosCliente = RunQuery(query, parameters, false, conn, tx);

                if (datosCliente.Count == 0)
                {
                    LogErrorHelper.RegistrarLog(
                        "Facturacion Ventas Industriales",
                        "SIN_FOLIO",
                        $"No se encontraron datos del cliente para generar la factura.",
                        nivel: "ERROR"
                    );
                    throw new Exception("No se encontraron los datos del cliente para generar la factura.");
                }

                query = "SELECT regimen_fiscal FROM direcciones_facturacion where entidad_clave = @cve_cli;";
                string rScocial = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "601";

                query = "SELECT id_correo_cli, cliente_id, correo FROM correos_cliente WHERE cliente_id = @cliente_id;";
                parameters.Add("cliente_id", datosCliente[0]["id_cliente"]);
                var datosCorreosClientes = RunQuery(query, parameters, false, conn, tx);

                string moneda = "";
                if (ctx.Form["moneda"].ToString() == "PESOS")
                {
                    moneda = "MXN";
                }
                else if (ctx.Form["moneda"].ToString() == "DLLS")
                {
                    moneda = "USD";
                }
                else if (ctx.Form["moneda"].ToString() == "EURO")
                {
                    moneda = "EUR";
                }

                decimal flete = Convert.ToDecimal(ctx.Form["flete"].ToString());

                // 🔹 Crear objeto Factura
                AplicarAnticipos anticipoModelo = new AplicarAnticipos();
                // 🔹 Crear objeto Factura

                factura.Serie = "VN";
                factura.Folio = ctx.Form["folio"].ToString();
                factura.IdTipoPago = ctx.Form["forma-pago"].ToString();
                factura.Moneda = moneda;
                factura.CpE = GetEmisor("CpE");
                factura.RfcEmisor = GetEmisor("Rfc");
                factura.RsoEmisor = GetEmisor("RazonSocial");
                factura.Rege = GetEmisor("Regimen");
                factura.RfcCliente = ctx.Form["rfc"].ToString();
                factura.RsoCliente = datosCliente[0]["n_cli"].ToString();
                factura.CpR = datosCliente[0]["cp"].ToString();
                factura.IdUsoCFDI = ctx.Form["uso-cfdi"].ToString();
                factura.CFDIText = usoCFDItext;
                factura.Regc = rScocial;
                factura.regimenEText = regimenText;
                factura.Subtotal = Convert.ToDecimal(ctx.Form["subtotal2"].ToString() ?? "0");
                factura.MontoAnticipo = Convert.ToDecimal(ctx.Form["subtotal1"].ToString() ?? "0");
                factura.TipoCambio = Convert.ToDecimal(ctx.Form["paridad"].ToString() ?? "1.00");
                factura.LugarExpedicion = GetEmisor("CpE");
                factura.metodoPagoTexto = mdp1 ?? "PPD";
                factura.MdpFactura = mdp;
                factura.TipoDeComprobante = ctx.Form["TipoDeComprobante"].ToString();
                factura.Observaciones = ctx.Form["comentarios"].ToString();
                factura.formaPagoTexto = tp;
                factura.Fecha = DateTime.Now;
                factura.TipoFacturacion = tipo;
                factura.FechaTimbrado = fechaPago.ToString();
                factura.Flete = flete;
                factura.Oc = ctx.Form["ordenCompra"].ToString();
                factura.EncabezadoId = Convert.ToInt32(ctx.EncabezadoId);
                factura.IdCliente = (int)datosCliente[0]["id_cliente"];

                parameters = new Dictionary<string, object>();
                parameters.Add("encabezado", Convert.ToInt32(ctx.EncabezadoId));

                query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio FROM encabezadomov em WHERE id_encabezado = @encabezado";
                var result = RunScalar(query, parameters, false, conn, tx);

                if (result == null)
                {
                    throw new Exception("folio no encontrado para el encabezado.");
                }

                string uuid = result.ToString();


                string folioCompleto = uuid;


                factura.Folio = folioCompleto;
                factura.FolioCorto = ctx.Form["folio"].ToString();

                // 🔹 DENTRO DE GenerarFacturaVentas, reemplaza la sección de addenda:

                Addenda addenda = null;

                var pAdd = new Dictionary<string, object>
                {
                    { "id_addenda", Convert.ToInt32(ctx.Form["idAdenda"].ToString()) }
                };

                string sqlAddenda = @"
                    SELECT
                        id_addenda,
                        nombre,
                        xml_namespace,
                        xml_schema,
                        xml_prefix,
                        usar_conceptos,
                        version,
                        data_template
                    FROM cfdi_addenda_def
                    WHERE activo = true
                      AND id_addenda = @id_addenda
                    LIMIT 1;
                ";

                var addendaDb = RunQuery(sqlAddenda, pAdd, false, conn, tx);

                if (addendaDb.Count > 0)
                {
                    var row = addendaDb[0];

                    addenda = new Addenda
                    {
                        Tipo = row["nombre"]?.ToString() ?? string.Empty,
                        Namespace = row["xml_namespace"]?.ToString() ?? string.Empty,
                        SchemaLocation = row["xml_schema"]?.ToString() ?? string.Empty,
                        Prefix = row["xml_prefix"]?.ToString() ?? "add",

                        Options = new AddendaOptions
                        {
                            UsarConceptosCFDI = row["usar_conceptos"] != DBNull.Value && Convert.ToBoolean(row["usar_conceptos"])
                        }
                    };

                    // 🔥 Parsear el template JSON y extraer datos planos
                    if (!string.IsNullOrWhiteSpace(row["data_template"]?.ToString()))
                    {
                        var templateJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                            row["data_template"].ToString()
                        );
                        templateJson["ordenCompra"] = ctx.Form["ordenCompra"].ToString();
                        addenda.DatosTemplate = AplanarJSON(templateJson);
                    }
                }

                factura.Addenda = addenda;
                // 🔹 Crear DataTable para productos
                factura.Tproductos = new System.Data.DataTable();
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
                    new DataColumn("comentario", typeof(string))
                });

                if (productos.Count > 0)
                {
                    foreach (var prod in productos)
                    {
                        var parametersProd = new Dictionary<string, object>();
                        string product = "select prod_sat, ud_sat, obj_impto from catrelacion where prod_kepler = @cve";
                        parametersProd.Add("cve", (string)prod.productoId);

                        var dat = RunQuery(product, parametersProd, false, conn, tx);

                        if (dat.Count == 0)
                        {
                            throw new Exception($"No se encontró relación SAT para el producto {prod.productoId}.");
                        }

                        var row = dat[0];

                        string prodSat = row["prod_sat"]?.ToString()?.Trim();
                        string udSat = row["ud_sat"]?.ToString()?.Trim();
                        string objImp = row["obj_impto"]?.ToString()?.Trim();

                        var camposFaltantes = new List<string>();

                        if (string.IsNullOrWhiteSpace(prodSat))
                            camposFaltantes.Add("Clave SAT (prod_sat)");

                        if (string.IsNullOrWhiteSpace(udSat))
                            camposFaltantes.Add("Unidad SAT (ud_sat)");

                        if (string.IsNullOrWhiteSpace(objImp))
                            camposFaltantes.Add("Objeto de Impuesto (obj_impto)");

                        if (camposFaltantes.Any())
                        {
                            string mensaje = $"El producto {prod.productoId} no tiene configurado correctamente: {string.Join(", ", camposFaltantes)}.";

                            LogErrorHelper.RegistrarLog(
                                "Facturacion Ventas Industriales",
                                "SIN_RELACION_SAT",
                                mensaje,
                                nivel: "ERROR"
                            );

                            throw new Exception(mensaje);
                        }

                        factura.Tproductos.Rows.Add(
                            (string)prod.productoId,
                            (string)dat[0]["prod_sat"],
                            (string)dat[0]["ud_sat"],
                            (string)prod.unidad,
                            (string)prod.descripcion,
                            (double)prod.cantidad,
                            (double)prod.precio,
                            ((double)prod.precio * (double)prod.cantidad),
                            (string)prod.objetoImp ?? "02",
                            (string)prod.comentario ?? ""
                        );
                    }
                }
                // 🔹 Crear DataTable para anticipos
                factura.TAnticipos = new System.Data.DataTable();
                factura.TAnticipos.Columns.AddRange(new[]
                {
                    new DataColumn("uuid", typeof(string)),
                    new DataColumn("fecha", typeof(string)),
                    new DataColumn("monto_aplicado", typeof(decimal)),
                    new DataColumn("saldo_antes", typeof(decimal)),
                    new DataColumn("saldo_despues", typeof(decimal))
                });

                // 🔹 Variables acumuladoras
                decimal totalAnticiposAplicados = 0;
                decimal totalFactura = Convert.ToDecimal(ctx.Form["subtotal2"].ToString() ?? "0");
                decimal ivaFactura = Convert.ToDecimal(ctx.Form["iva"].ToString() ?? "0");
                decimal totalFacturaConIVA = totalFactura + ivaFactura;

                // 🔹 Si hay anticipos, procesarlos
                if (anticipo.Count > 0)
                {
                    totalAnticiposAplicados = Convert.ToDecimal(ctx.Form["totalAnticipos"].ToString() ?? "0");

                    // 🔹 Consultar información de todos los anticipos para aplicar proporción correctamente
                    var anticiposDisponibles = new List<(int IdEncabezado, string UUID, decimal Saldo, decimal Total, int IdFactura)>();

                    foreach (var ant in anticipo)
                    {
                        var parametersAnt = new Dictionary<string, object> { { "cve", (int)ant.id_encabezado } };
                        string sqlAnt = "SELECT id, uuid, total, saldo FROM factura WHERE encabezado_id = @cve;";
                        var dat = RunQuery(sqlAnt, parametersAnt, false, conn, tx);
                        if (dat.Count == 0)
                        {
                            LogErrorHelper.RegistrarLog(
                                "Facturacion Ventas Industriales",
                                "SIN_FOLIO",
                                $"No se encontraron datos del anticipo con encabezado {ant.id_encabezado} .",
                                nivel: "ERROR"
                            );
                            throw new Exception($"No se encontró el anticipo con encabezado {ant.id_encabezado}.");
                        }

                        anticiposDisponibles.Add((
                            IdEncabezado: Convert.ToInt32(ant.id_encabezado),
                            UUID: dat[0]["uuid"].ToString(),
                            Saldo: Convert.ToDecimal(dat[0]["saldo"]),
                            Total: Convert.ToDecimal(dat[0]["total"]),
                            IdFactura: Convert.ToInt32(dat[0]["id"])
                        ));

                        anticipoModelo.Anticipos.Add(Convert.ToInt32(ant.id_encabezado));
                    }

                    // 🔹 Calcular total aplicable y exceso
                    decimal saldoTotalDisponible = anticiposDisponibles.Sum(a => a.Saldo);
                    if (saldoTotalDisponible <= 0)
                        throw new Exception("Los anticipos seleccionados no tienen saldo disponible.");

                    decimal totalAplicableAFactura = Math.Min(totalFacturaConIVA, totalAnticiposAplicados);
                    decimal exceso = totalAnticiposAplicados - totalAplicableAFactura;

                    decimal restante = totalAplicableAFactura;
                    var operacionesPendientes = new List<(int IdEncabezado, decimal NuevoSaldo, decimal MontoAplicado, int IdAnticipo, decimal SaldoAntes)>();

                    foreach (var ant in anticiposDisponibles)
                    {
                        if (restante <= 0) break;

                        decimal proporcion = ant.Saldo / saldoTotalDisponible;
                        decimal montoAplicado = Math.Round(totalAplicableAFactura * proporcion, 2);

                        if (montoAplicado > ant.Saldo)
                            montoAplicado = ant.Saldo;

                        if (montoAplicado > restante)
                            montoAplicado = restante;

                        decimal nuevoSaldo = ant.Saldo - montoAplicado;
                        restante -= montoAplicado;

                        // 🔹 Registrar en DataTable para XML / complemento
                        factura.TAnticipos.Rows.Add(
                            ant.UUID,
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            montoAplicado,
                            ant.Saldo,
                            nuevoSaldo
                        );

                        // 🔹 Registrar operación pendiente (para actualizar DB post-timbrado)
                        operacionesPendientes.Add((ant.IdEncabezado, nuevoSaldo, montoAplicado, ant.IdFactura, ant.Saldo));
                    }

                    // 🔹 Guardar operaciones para después del timbrado
                    TempData["OperacionesAnticipos"] = JsonConvert.SerializeObject(operacionesPendientes);

                    // 🔹 Guardar exceso para futuras facturas
                    TempData["ExcesoAnticipo"] = exceso;

                    // 🔹 Ajustar total de factura solo restando lo aplicado
                    decimal totalAplicadoFinal = factura.TAnticipos.AsEnumerable().Sum(r => Convert.ToDecimal(r["monto_aplicado"]));
                    factura.Total = Convert.ToDecimal(totalFacturaConIVA - totalAplicadoFinal);
                    if (factura.Total < 0) factura.Total = 0;
                }
                else
                {
                    // Sin anticipos, total normal
                    factura.Total = Convert.ToDecimal(ctx.Form["subtotal1"].ToString() ?? "0") + Convert.ToDecimal(ctx.Form["iva"].ToString() ?? "0");
                }


                // 🔹 Generar XML y timbrar dependiendo del tipo


                if (factura.TipoFacturacion?.ToLower() == "anticipo")
                {
                    factura.Saldo = factura.Total;
                    parameters = new Dictionary<string, object>();
                    query = "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado";
                    parameters.Add("id_encabezado", Convert.ToInt32(ctx.EncabezadoId));

                    int enc = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);
                    if (enc > 0)
                    {
                        var poliza = GenerarDatosPoliza(enc, ctx.Form["CuentaBancariaId"].ToString(), null, conn, tx);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
                        CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Anticipo, conn, tx);
                    }

                    resultadoTimbrado = GenerarXmlAnticipo(factura);
                }
                else if (factura.TipoFacturacion?.ToLower() == "credito")
                {


                    parameters = new Dictionary<string, object>();
                    query = "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado";
                    parameters.Add("id_encabezado", Convert.ToInt32(ctx.EncabezadoId));

                    int enc = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);
                    if (enc > 0)
                    {
                        List<PolizaData> poliza = GenerarDatosPoliza(enc, ctx.Form["CuentaBancariaId"].ToString(), null, conn, tx);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
                        RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);
                    }
                    resultadoTimbrado = GenerarXml(factura);
                }
                else
                {
                    parameters = new Dictionary<string, object>();
                    query = "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado";
                    parameters.Add("id_encabezado", Convert.ToInt32(ctx.EncabezadoId));

                    int enc = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);
                    if (enc > 0)
                    {
                        List<PolizaData> poliza = GenerarDatosPoliza(enc, ctx.Form["CuentaBancariaId"].ToString(), null, conn, tx);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
                        RegistrarCarteraResult cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

                        bool carteraSaldada = true;

                        if (anticipo.Count > 0)
                        {
                            anticipoModelo.CarteraId = cartera.CarteraId;
                            anticipoModelo.UsuarioId = GetUserId(User.Identity.Name);

                            ResultadoAplicacionAnticipos resultado = AplicarAnticipos(anticipoModelo, conn, tx);

                            if (resultado != null)
                                carteraSaldada = !resultado.CarteraSaldada;
                        }

                        //if (carteraSaldada)
                        //{
                        //    CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), cartera.CarteraId, false, false, conn, tx);
                        //}

                    }
                    resultadoTimbrado = GenerarXml(factura);
                }

                if (!resultadoTimbrado.Success)
                {
                    if (!borradoEjecutado)
                    {
                        //BorradoFacturasIncorrectas(Convert.ToInt32(fc["enc_id"].ToString()), "factura", "VI");
                        //borradoEjecutado = true;
                    }
                    throw new Exception("Error en timbrado: " + resultadoTimbrado.Message);
                }

                // 🔹 Ejecutar actualizaciones solo si el timbrado fue exitoso
                if (resultadoTimbrado.Success && TempData["OperacionesAnticipos"] != null)
                {
                    var operaciones = JsonConvert.DeserializeObject<List<(int IdEncabezado, decimal NuevoSaldo, decimal MontoAplicado, int IdAnticipo, decimal SaldoAntes)>>(
                        TempData["OperacionesAnticipos"].ToString()
                    );

                    foreach (var op in operaciones)
                    {
                        // 1️⃣ Actualizar saldo
                        var parametersUpdate = new Dictionary<string, object>();
                        string updateSaldo = @"
                                                UPDATE factura 
                                                SET saldo = @nuevoSaldo 
                                                WHERE encabezado_id = @id_encabezado;";
                        parametersUpdate.Add("nuevoSaldo", op.NuevoSaldo);
                        parametersUpdate.Add("id_encabezado", op.IdEncabezado);
                        RunUpdate(updateSaldo, parametersUpdate, false, conn, tx);

                        // 2️⃣ Insertar relación en factura_anticipos
                        var parametersInsert = new Dictionary<string, object>();
                        string insertRelacion = @"
                                                    INSERT INTO factura_anticipos
                                                    (id_factura_principal, id_factura_anticipo, monto_aplicado, fecha_aplicacion, usuario_aplica, observaciones, saldo_antes, saldo_despues)
                                                    VALUES (@idFacturaPrincipal, @idFacturaAnticipo, @montoAplicado, NOW(), @usuario, @observaciones, @saldoAntes, @saldoDespues);";

                        parametersInsert.Add("idFacturaPrincipal", Convert.ToInt32(ctx.EncabezadoId));
                        parametersInsert.Add("idFacturaAnticipo", op.IdAnticipo);
                        parametersInsert.Add("montoAplicado", op.MontoAplicado);
                        parametersInsert.Add("usuario", GetUserId(User.Identity.Name));
                        parametersInsert.Add("observaciones", "Aplicación de anticipo sobre subtotal.");
                        parametersInsert.Add("saldoAntes", op.SaldoAntes);
                        parametersInsert.Add("saldoDespues", op.NuevoSaldo);
                        RunQuery(insertRelacion, parametersInsert, false, conn, tx);
                    }
                }
                query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                parameters.Add("id", Convert.ToInt32(ctx.EncabezadoId));
                RunUpdate(query, parameters, false, conn, tx);

                var datosDetallados = new Dictionary<string, object>
                {
                    { "UUID", resultadoTimbrado.UUID },
                    { "Total", factura.Total },
                    { "Subtotal", factura.Subtotal },
                    { "IVA", factura.IVA },
                    { "RFCCliente", factura.RfcCliente },
                    { "RazonSocialCliente", factura.RsoCliente },
                    { "Fecha", factura.Fecha.ToString("dd/MM/yyyy HH:mm:ss") },
                    { "Serie", factura.Serie },
                    { "Folio", factura.Folio },
                    { "EncabezadoId", factura.EncabezadoId },
                    { "PdfUrl", Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf") },
                    { "XmlUrl",Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml")},
                    { "CantidadProductos", factura.Tproductos.Rows.Count },
                    { "CorreosCliente", datosCorreosClientes.Select(d => new {
                        id = d["id_correo_cli"],
                        correo = d["correo"].ToString()
                    }).ToList() }
                };


                return (true, resultadoTimbrado.Message, datosDetallados);
            }

            catch (Exception ex)
            {
                if (!borradoEjecutado)
                {
                    LogErrorHelper.RegistrarLog(
                        "Facturacion Ventas Industriales",
                        "SIN_FOLIO",
                        $"Ocurrio un error {ex.Message}",
                        nivel: "ERROR"
                    );
                    //BorradoFacturasIncorrectas(Convert.ToInt32(fc["enc_id"].ToString()), "factura", "VI");
                    borradoEjecutado = true;
                }

                return (false, "Error al generar la factura: " + ex.Message, null);
            }
        }
        private Dictionary<string, string> AplanarJSON(Dictionary<string, object> json, string prefijo = "")
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

        [Route("VNFactura/ProcesarDocumentosAsync")]
        public async Task<JsonResult> ProcesarDocumentosAsync(IFormCollection fc)
        {
            (bool Success, string Message, object Data) resultado1 = (false, string.Empty, null);
            var datosFactura = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            var factura = new Factura();
            int cantidadDocumentos = 0;
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        string productosJson = fc["productosJSON"].ToString();

                        List<Dictionary<string, string>> productos = null;

                        if (!string.IsNullOrWhiteSpace(productosJson))
                        {
                            productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);
                        }

                        string idsDocumentos = fc["documentid"].ToString();

                        if (!string.IsNullOrWhiteSpace(idsDocumentos))
                        {
                            // Contador de documentos procesados
                            cantidadDocumentos = idsDocumentos.Split(',').Length;
                        }
                        try
                        {
                            // 1️⃣ Guardar desde documentos base
                            resultado1 = await Guardar(fc, conn, tx);
                            if (!resultado1.Success)
                                return Json(new { success = false, step = "GuardarFacturaDesdeDocs", error = resultado1.Message });

                            dynamic data = resultado1.Data;
                            int idEncabezado = data.IdEncabezado;

                            var ctx = new NCContext
                            {
                                Form = fc,
                                EncabezadoId = idEncabezado
                            };
                            //fc.Add("enc_id", idEncabezado.ToString());
                            // 2️⃣ Crear remisiones desde pedidos
                            var resultado2 = await GenerarFacturaVentas(ctx, factura, conn, tx);
                            if (!resultado2.Success)
                                return Json(new { success = false, step = "GenerarFacturaDesdeDocs", error = resultado2.Message });



                            // En el flujo de remisiones (Especiales) el estatus de cada remisión
                            // (11 completa / 41 parcial) lo fija ActualizarEstatusRemisionSiCompleta
                            // dentro de Guardar; ahí NO se deben cerrar a ciegas. Solo en "Buscar
                            // Documentos" (documentid, sin remisiones) se cierra el documento completo.
                            bool esFlujoRemisiones =
                                !string.IsNullOrWhiteSpace(fc["remisionesParcialesJSON"].ToString())
                                || !string.IsNullOrWhiteSpace(fc["remisionesIds"].ToString());

                            if (!esFlujoRemisiones && !string.IsNullOrWhiteSpace(idsDocumentos))
                            {
                                // Sanear la lista de IDs (antes string.Join(",", idsDocumentos)
                                // recorría el string carácter por carácter) y cerrar a 11.
                                var idsParam = string.Join(",", idsDocumentos
                                    .Split(',')
                                    .Select(s => s.Trim())
                                    .Where(s => int.TryParse(s, out _)));
                                if (!string.IsNullOrWhiteSpace(idsParam))
                                {
                                    string updateHijos = $@"UPDATE encabezadomov SET estatus_id = 11
                                                            WHERE id_encabezado IN ({idsParam});";
                                    RunUpdate(updateHijos, parameters, false, conn, tx);
                                }
                            }
                            // ✅ ÉXITO - Consolidar datos detallados
                            datosFactura = resultado2.Data as Dictionary<string, object>;
                            tx.Commit();
                        }

                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                        //GuardarFactura(factura);
                        //GenerarFactura(factura);
                        // RETORNO COMPLETO Y DETALLADO
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
                                xmlUrl = datosFactura?["XmlUrl"],

                                CorreosCliente = datosFactura?["CorreosCliente"]
                            }
                        });
                    }

                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "VNFactura/?");
                        return Json(new
                        {
                            success = false,
                            message = "Error general en el proceso: " + ex.Message,
                            detalles = ex.StackTrace
                        });
                    }
                }
            }
        }
    }
}