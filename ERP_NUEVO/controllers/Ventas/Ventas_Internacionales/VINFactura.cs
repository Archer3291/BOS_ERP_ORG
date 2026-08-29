using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using BOS_ERP.Services;
using DocumentFormat.OpenXml.InkML;
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

namespace BOS_ERP.Controllers.Ventas.Ventas_Internacionales
{
    [Authorize]
    public class VINFacturaController : FacturacionVentaINController
    {

        private readonly IConfiguration _configuration;

        public VINFacturaController(
            EmailSender _emailSender,
            IOptions<TimbradoOptions> timbradoOptions,
            IConfiguration configuration,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            XmlBuilderService xmlBuilder)
            : base(_emailSender, timbradoOptions, env, viewEngine, tempDataProvider, xmlBuilder)
        {
            _configuration = configuration;
        }

        protected override string FacturaLogTag => "VINFacturaController";

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Internacionales", Accion = "Creacion de factura")]
        public async Task<(bool Success, string Message, object Data)> Guardar_ConRemisiones(
            IFormCollection fc, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            try
            {
                // ── 1. Leer y parsear el JSON de partidas de remisiones ──────
                List<RemisionPartidaParaFacturar> partidasRemision = null;
                List<int> remisionIds = new List<int>();
                

                if (!string.IsNullOrWhiteSpace(fc["remisionesParcialesJSON"].ToString()))
                {
                    partidasRemision = JsonConvert.DeserializeObject<List<RemisionPartidaParaFacturar>>(
                        fc["remisionesParcialesJSON"].ToString());
                }
                string remisionesIds = fc["remisionesIds"].ToString();
                if (!string.IsNullOrWhiteSpace(remisionesIds))
                {
                    remisionIds = remisionesIds
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(s => int.TryParse(s.Trim(), out _))
                        .Select(s => int.Parse(s.Trim()))
                        .ToList();
                }

                bool tieneRemisiones = partidasRemision != null && partidasRemision.Count > 0;

                if (tieneRemisiones)
                {
                    // Toda remisión referenciada en el detalle debe estar en remisionIds, o su
                    // tracking nunca se inicializa.
                    remisionIds = remisionIds
                        .Union(partidasRemision.Select(p => p.RemisionId))
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();

                    // VI y VN validaban el detalle desde hace tiempo; Internacionales no, por
                    // tener su propia copia del tracking. Sin esto, una partida sin
                    // id_partida_remision se insertaba con la clave en nulo y el saldo de esa
                    // remisión quedaba descuadrado en silencio.
                    ValidarDetalleRemisiones(partidasRemision, conn, tx);
                }

                // ── 2. Validaciones básicas ──────────────────────────────────
                string tipo = fc["tipo"].ToString()?.ToLower() ?? "";

                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    throw new Exception("Debe seleccionar un cliente.");
                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    throw new Exception("Debe seleccionar una moneda.");
                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
                    throw new Exception("Debe seleccionar un vendedor.");
                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    throw new Exception("Debe seleccionar una forma de pago.");

                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
                if (tipo != "anticipo")
                {
                    if (!tieneRemisiones)
                    {
                        if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                            throw new Exception("Debe agregar al menos un producto o seleccionar remisiones.");

                        productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                        if (productos == null || productos.Count == 0)
                            throw new Exception("Debe agregar al menos un producto.");
                    }
                    else
                    {
                        productos = ConvertirPartidasRemisionAProductos(partidasRemision, conn, tx);
                    }
                }

                // ── 3. Validar fecha de pago según tipo ──────────────────────
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaPago"].ToString()))
                        throw new Exception("Debe ingresar la fecha de pago.");
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

                // ── 4. Recuperar datos del encabezado padre ──────────────────
                int idEncabezadoPadre = 0;
                int usrId0 = 0, usrId1 = 0, usrId2 = 0;
                DateTime usrFch0 = DateTime.Now, usrFch1 = DateTime.Now, usrFch2 = DateTime.Now;

                if (!string.IsNullOrWhiteSpace(fc["documentid"].ToString()) && fc["documentid"].ToString() != "0")
                {
                    var usrParameter = new Dictionary<string, object> { { "id", Convert.ToInt32(fc["documentid"].ToString()) } };
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
                        idEncabezadoPadre = Convert.ToInt32(fc["documentid"].ToString());
                    }
                }
                else if (remisionIds.Count > 0)
                {
                    idEncabezadoPadre = remisionIds.First();
                }

                // ── 5. Obtener ID y nombre del cliente ───────────────────────
                int idCliente = 0;
                string clienteNombre = "";
                var clienteParameter = new Dictionary<string, object>
        {
            { "cliente", fc["cliente"].ToString() },
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

                // ── 6. Obtener id de forma de pago ───────────────────────────
                var fPagoParameter = new Dictionary<string, object> { { "cve_sat", fc["forma-pago"].ToString() } };
                int fpId = Convert.ToInt32(
                    RunScalar("SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat", fPagoParameter));

                // ── 7. Construir encabezado ──────────────────────────────────
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 17,
                    IdTpDoc = 56,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = fc["almacen"].ToString(),
                    Fch = DateTime.Now,
                    TpMov = "VINFAC",
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
                    Dto = Convert.ToDecimal(fc["descuento"].ToString()),
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
                    FchPgEntrega = fechaPago ?? DateTime.Now,
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    Incoterm = fc["incoterm"].ToString() ?? "",
                    FPago = fpId,
                    Mdp = fc["metodo-pago"].ToString(),
                    TipoPoceso = "factura_" + tipo,
                    CFDI = fc["uso-cfdi"].ToString(),
                    CentroCostos = Convert.ToInt32(fc["CentroCostosId"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString(),
                };

                // ── 8. Construir partidas ────────────────────────────────────
                var partidas = new List<PartidaDocumento>();
                foreach (var p in productos)
                {
                    var pParam = new Dictionary<string, object>
            {
                { "cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "" },
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
                        Dto1 = Math.Round(cantP * preP * (dtoP / 100m), 2),
                        ImpPart = Math.Round(cantP * preP * (1 - dtoP / 100m), 2),
                        Ud = p.ContainsKey("unidad") ? p["unidad"] : "PZA",
                        IdProducto = productId,
                        TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                    });
                }

                // ── 9. Guardar documento principal ───────────────────────────
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                int idNuevaFactura = Convert.ToInt32(folio["IdEncabezado"]);
                string docsIds = Convert.ToString(fc["documentid"].ToString());
                // ── 10. Actualizar remisiones ─────────────────────────────────
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
                // Facturación por documentid, sin remisionesIds explícito.
                else if (!string.IsNullOrWhiteSpace(fc["documentid"].ToString()) && fc["documentid"].ToString() != "0")
                {
                    var docIds = docsIds
                        .Split(',')
                        .Where(s => int.TryParse(s.Trim(), out _))
                        .Select(s => int.Parse(s.Trim()))
                        .Where(id => id > 0)
                        .ToList();

                    // Por esta vía el documento puede no ser una remisión: en Internacionales
                    // hay 12 facturas colgadas de un AIEINV, que no descarga almacén. Sembrar
                    // sus partidas como saldo de remisión ensuciaría la tabla, así que se
                    // filtra por naturaleza. Pero el documento igual debe cerrarse: si es
                    // remisión lo cierra el tracking según sus partidas, y si no, se cierra
                    // directo a 11 como hacen VI y VN en esta misma rama.
                    foreach (int idDoc in docIds)
                    {
                        if (RegistrarOrigenSiEsRemision(idNuevaFactura, idDoc, conn, tx))
                        {
                            ActualizarEstatusRemisionSiCompleta(idDoc, conn, tx);
                        }
                        else
                        {
                            RunUpdate(
                                "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id",
                                new Dictionary<string, object> { { "id", idDoc } },
                                false, conn, tx);
                        }
                    }
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
                RegistrarErrorParaTicket(ex, "VINFactura/?");
                return (false, "No se pudo guardar la factura internacional.", null);
            }
        }


        private List<Dictionary<string, string>> ConvertirPartidasRemisionAProductos(
            List<RemisionPartidaParaFacturar> partidas, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var resultado = new List<Dictionary<string, string>>();

            var agrupado = partidas
                .GroupBy(p => p.CveProd)
                .Select(g => new
                {
                    CveProd = g.Key,
                    CantidadTotal = g.Sum(p => p.CantidadAFacturar),
                    PrecioUnitario = g.First().PrecioUnitario,
                    Descuento = g.Average(p => p.Descuento),
                    Descripcion = g.First().Descripcion,
                    Unidad = g.First().Unidad,
                    Comentario = string.Join(" | ", g.Select(p => $"Rem:{p.FolioRemision}").Distinct())
                });

            foreach (var p in agrupado)
            {
                resultado.Add(new Dictionary<string, string>
        {
            { "productoId",    p.CveProd },
            { "descripcion",   p.Descripcion },
            { "cantidad",      p.CantidadTotal.ToString("F4") },
            { "precio",        p.PrecioUnitario.ToString("F4") },
            { "descuento",     p.Descuento.ToString("F2") },
            { "unidad",        p.Unidad ?? "PZA" },
            { "comentario",    p.Comentario },
            { "costoUnitario", "0" }
        });
            }

            return resultado;
        }


        [Route("FacturacionVentaIN/Factura")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<(bool Success, string Message, object Data)> GenerarFacturaVentas(NCContext ctx, Factura factura, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                : "pruebas";
            bool borradoEjecutado = false; // 🔹 Bandera global            
            try
            {

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


                query = "SELECT regimen_fiscal FROM direcciones_facturacion where entidad_clave = @cve_cli;";
                string rScocial = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "616";


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

                AplicarAnticipos anticipoModelo = new AplicarAnticipos();
                // 🔹 Crear objeto Factura

                factura.Serie = string.IsNullOrWhiteSpace(ctx.Form["Serie"].ToString())? "VIS": ctx.Form["Serie"]!.ToString();
                factura.IdTipoPago = ctx.Form["forma-pago"].ToString();
                factura.Moneda = moneda;
                factura.CpE = GetEmisor("CpE");
                factura.LugarExpedicion = GetEmisor("CpE");
                factura.RfcEmisor = GetEmisor("Rfc");
                factura.RsoEmisor = GetEmisor("RazonSocial");
                factura.Rege = GetEmisor("Regimen");
                factura.RfcCliente = ctx.Form["rfc"].ToString();
                factura.RsoCliente = datosCliente[0]["n_cli"].ToString();
                factura.CpR = datosCliente[0]["cp"].ToString();
                factura.IdUsoCFDI = ctx.Form["uso-cfdi"].ToString();
                factura.CFDIText = usoCFDItext;
                factura.Regc = "616";
                factura.regimenEText = regimenText;
                factura.Subtotal = Convert.ToDecimal(ctx.Form["subtotal2"].ToString() ?? "0");
                factura.MontoAnticipo = Convert.ToDecimal(ctx.Form["subtotal1"].ToString() ?? "0");
                factura.IVA = Convert.ToDecimal("0");
                factura.Total = Convert.ToDecimal(ctx.Form["totalFinal"].ToString() ?? "0");
                factura.TipoCambio = Convert.ToDecimal(ctx.Form["paridad"].ToString() ?? "1.00");
                factura.metodoPagoTexto = mdp1 ?? "PPD";
                factura.MdpFactura = mdp;
                factura.TipoDeComprobante = ctx.Form["TipoDeComprobante"].ToString();
                factura.Observaciones = ctx.Form["Observaciones"].ToString();
                factura.formaPagoTexto = tp;
                factura.Fecha = DateTime.Now;
                factura.TipoFacturacion = tipo;
                factura.FechaTimbrado = fechaPago.ToString();
                factura.Flete = flete;
                factura.EncabezadoId = Convert.ToInt32(ctx.EncabezadoId);
                factura.Exportacion = "02";
                factura.IdCliente = (int)datosCliente[0]["id_cliente"];
                factura.DireccionReceptor = ctx.Form["direccion-receptor"].ToString() ?? "";

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


                //Comercio Exterior inicio
                bool aplicaCE = ctx.Form["aplicaComercioExterior"].ToString() == "1";
                factura.Exportacion = aplicaCE ? "02" : "01";

                if (aplicaCE)
                {
                    var ce = new ComercioExterior
                    {
                        Version = "2.0",
                        MotivoTraslado = ctx.Form["ce_motivoTraslado"].ToString() ?? "",
                        ClaveDePedimento = ctx.Form["ce_clavePedimento"].ToString() ?? "",
                        CertificadoOrigen = ctx.Form["ce_certificadoOrigen"].ToString() ?? "",
                        NumCertificadoOrigen = ctx.Form["ce_numCertificadoOrigen"].ToString() ?? "",
                        NumeroExportadorConfiable = ctx.Form["ce_numExportadorConfiable"].ToString() ?? "",
                        Incoterm = ctx.Form["incoterm"].ToString() ?? "",
                        TipoCambioUSD = decimal.TryParse(ctx.Form["ce_tipoCambioUSD"].ToString(), out decimal tcUsd) ? tcUsd : 0m,
                        TotalUSD = decimal.TryParse(ctx.Form["ce_totalUSD"].ToString(), out decimal tUsd) ? tUsd : 0m,
                        NumRegIdTrib = ctx.Form["ce_propietario_numRegIdTrib"].ToString() ?? "",
                    };

                    ce.DomicilioEmisor = new DomicilioComExt
                    {
                        Calle = ctx.Form["ce_emisor_calle"].ToString() ?? "",
                        NumeroExterior = ctx.Form["ce_emisor_numExterior"].ToString() ?? "",
                        Colonia = ctx.Form["ce_emisor_colonia"].ToString() ?? "",
                        Localidad = ctx.Form["ce_emisor_localidad"].ToString() ?? "",
                        Municipio = ctx.Form["ce_emisor_municipio"].ToString() ?? "",
                        Estado = ctx.Form["ce_emisor_estado"].ToString() ?? "",
                        Pais = ctx.Form["ce_emisor_pais"].ToString() ?? "",
                        CodigoPostal = ctx.Form["ce_emisor_codigoPostal"].ToString() ?? ""
                    };

                    ce.DomicilioDestinatario = new DomicilioComExt
                    {
                        Calle = ctx.Form["ce_receptor_calle"].ToString() ?? "",
                        NumeroExterior = ctx.Form["ce_receptor_numExterior"].ToString() ?? "",
                        Colonia = ctx.Form["ce_receptor_colonia"].ToString() ?? "",
                        Localidad = ctx.Form["ce_receptor_localidad"].ToString() ?? "",
                        Municipio = ctx.Form["ce_receptor_municipio"].ToString() ?? "",
                        Estado = ctx.Form["ce_receptor_estado"].ToString() ?? "",
                        Pais = ctx.Form["ce_receptor_pais"].ToString() ?? "",
                        CodigoPostal = ctx.Form["ce_receptor_codigoPostal"].ToString() ?? ""
                    };

                    // Mercancias: aquí SÍ se valida fracción arancelaria
                    ce.Mercancias = new List<MercanciaExportada>();
                    if (!string.IsNullOrWhiteSpace(ctx.Form["productosJSON"].ToString()))
                    {
                        var productosCE = JsonConvert.DeserializeObject<List<dynamic>>(ctx.Form["productosJSON"].ToString());
                        foreach (var p in productosCE)
                        {
                            var arParam = new Dictionary<string, object> { { "cve_prod", p.productoId.ToString() } };
                            string qFrac = "SELECT frac, unidad FROM frac_arancelarias fa " +
                                           "INNER JOIN fracciones_arancelarias_sat fas ON fas.fraccion_arancelaria = fa.frac " +
                                           "WHERE cve_prod = @cve_prod";
                            var frac = RunQuery(qFrac, arParam, false, conn, tx);

                            if (frac.Count == 0 || string.IsNullOrWhiteSpace(frac[0]["frac"]?.ToString()))
                                throw new Exception($"El producto {p.productoId} no tiene fracción arancelaria asignada.");

                            ce.Mercancias.Add(new MercanciaExportada
                            {
                                NoIdentificacion = (string)p.productoId,
                                FraccionArancelaria = frac[0]["frac"].ToString(),
                                Descripcion = (string)p.descripcion,
                                CantidadAduana = Convert.ToDecimal(p.cantidad),
                                UnidadAduana = frac[0]["unidad"].ToString(),
                                ValorUnitarioAduana = Convert.ToDecimal(p.precio),
                                ValorDolares = Convert.ToDecimal(p.importe),
                                Unidad = (string)p.unidad,
                                Cantidad = Convert.ToDecimal(p.cantidad),
                                Pedimentos = new List<Pedimento>
                {
                    new Pedimento { Numero = p.pedimento?.ToString() ?? "" }
                }
                            });
                        }
                    }

                    factura.ComercioExterior = ce;
                }
                else
                {
                    // Servicios: sin complemento CE, sin validar fracción arancelaria
                    factura.ComercioExterior = null;
                }
                //Comercio Exterior fin

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
                    new DataColumn("prod_id", typeof(int)),
                    new DataColumn("clave_cliente", typeof(string)),
                    new DataColumn("pedimento", typeof(string)),
                    new DataColumn("comentario", typeof(string)),
                    new DataColumn("descuento", typeof(double))   // ← NUEVO
                });

                if (productos.Count > 0)
                {
                    foreach (var prod in productos)
                    {
                        var parametersProd = new Dictionary<string, object>();
                        string product = "SELECT prod_sat, ud_sat, obj_impto from catrelacion WHERE prod_kepler = @cve";
                        parametersProd.Add("cve", (string)prod.productoId);
                        parametersProd.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        var dat = RunQuery(product, parametersProd, false, conn, tx);

                        if (dat.Count == 0)
                        {
                            throw new Exception("El producto no tiene informacion fiscal registrada " + (string)prod.productoId);
                        }


                        string queryId = "SELECT id_catproductos from catproductos WHERE cve_prod = @cve AND empresa_id = @empresa_id";
                        int productId = Convert.ToInt32(RunScalar(queryId, parametersProd, false, conn, tx));

                        // ✅ DESPUÉS - funciona con y sin CE
                        string pedimento = "";
                        if (aplicaCE && factura.ComercioExterior?.Mercancias != null)
                        {
                            var mercancia = factura.ComercioExterior.Mercancias
                                .FirstOrDefault(m => m.NoIdentificacion == (string)prod.productoId);
                            pedimento = mercancia?.Pedimentos?.FirstOrDefault()?.Numero ?? "";
                        }
                        else
                        {
                            // Sin CE: tomar el pedimento directo del JSON del producto
                            pedimento = prod.pedimento?.ToString() ?? "";
                        }

                        double descuentoPct = prod.descuento != null ? (double)prod.descuento : 0.0;
                        double importeConDescuento = (double)prod.cantidad * (double)prod.precio * (1 - descuentoPct / 100);

                        factura.Tproductos.Rows.Add(
                            (string)prod.productoId,
                            (string)dat[0]["prod_sat"],
                            (string)dat[0]["ud_sat"],
                            (string)prod.unidad,
                            (string)prod.descripcion,
                            (double)prod.cantidad,
                            (double)prod.precio,          // precio bruto sin tocar
                            (double)prod.cantidad * (double)prod.precio,  // importe bruto (o el campo que ya existía)
                            (string)prod.objetoImp ?? "02",
                            (int)productId,
                            (string)prod.claveCliente,
                            pedimento,
                            (string)prod.comentario ?? "",
                            descuentoPct                  // solo el % viaja aquí
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
                            throw new Exception($"No se encontró el anticipo con encabezado {ant.id_encabezado}.");

                        anticiposDisponibles.Add((
                            IdEncabezado: Convert.ToInt32(ant.id_encabezado),
                            UUID: dat[0]["uuid"].ToString(),
                            Saldo: Convert.ToDecimal(dat[0]["saldo"]),
                            Total: Convert.ToDecimal(dat[0]["total"]),
                            IdFactura: Convert.ToInt32(dat[0]["id"])
                        ));
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
                TimbradoResult resultadoTimbrado;

                if (factura.TipoFacturacion?.ToLower() == "anticipo")
                {
                    factura.Saldo = factura.Total;
                    parameters = new Dictionary<string, object>();
                    query = "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado";
                    parameters.Add("id_encabezado", Convert.ToInt32(ctx.EncabezadoId));

                    int enc = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);
                    if (enc > 0)
                    {
                        var poliza = GenerarDatosPoliza(enc, null, null, conn, tx);
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

                    int enc = Convert.ToInt32(RunScalar(query, parameters) ?? 0);
                    if (enc > 0)
                    {
                        List<PolizaData> poliza = GenerarDatosPoliza(enc, null, null, conn, tx);
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
                        var poliza = GenerarDatosPoliza(enc, null, null, conn, tx);
                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
                        RegistrarCarteraResult cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

                        bool carteraSaldada = false;

                        if (anticipo.Count > 0)
                        {
                            anticipoModelo.CarteraId = cartera.CarteraId;
                            anticipoModelo.UsuarioId = GetUserId(User.Identity.Name);

                            ResultadoAplicacionAnticipos resultado = AplicarAnticipos(anticipoModelo, conn, tx);

                            if (resultado != null)
                                carteraSaldada = resultado.CarteraSaldada;
                        }

                        query = "SELECT refe FROM encabezadomov WHERE id_encabezado = @id_encabezado ";
                        int cli = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);

                        query = "SELECT es_especial from catclientes WHERE id_cliente = @cli ";
                        parameters.Add("cli", cli);
                        bool es_especial = Convert.ToBoolean(RunScalar(query, parameters, false, conn, tx) ?? 0);

                        //if (!carteraSaldada && !es_especial)
                        //{
                        //    query = "SELECT cli_prov, refe, centro_costos, id_encabezado, coment1, coment2, usr_dep, usr_doc, usr0, sub, fch " +
                        //        "FROM encabezadomov " +
                        //        "WHERE id_encabezado = @i";
                        //    parameters = new Dictionary<string, object>();
                        //    parameters.Add("i", factura.EncabezadoId);
                        //    var usrId = RunQuery(query, parameters, false, conn, tx)[0];

                        //    var encabezado = new DocumentoEncabezado();
                        //    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                        //    encabezado.IdArea = 20;
                        //    encabezado.IdTpDoc = 70;
                        //    encabezado.UsrDep = GetString(usrId["usr_dep"]);
                        //    encabezado.Anio = DateTime.Now.Year;
                        //    encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                        //    encabezado.Fch = (DateTime)usrId["fch"];
                        //    encabezado.TpMov = "CXC";
                        //    encabezado.UsrDoc = GetString(usrId["usr_doc"]);
                        //    encabezado.FchCap = GetDate(usrId["fch"]);
                        //    encabezado.Usr0 = GetInt(usrId["usr0"]);
                        //    encabezado.Fch0 = GetDate(usrId["fch"]);
                        //    encabezado.Imp = cartera.MontoTotal;
                        //    encabezado.Sub = GetDecimal(usrId["sub"]);
                        //    encabezado.CliProv = usrId["cli_prov"].ToString();
                        //    encabezado.Ref = Convert.ToInt32(usrId["refe"]);
                        //    encabezado.Estatus = 11;
                        //    encabezado.TipoPoceso = "cobro_cliente";
                        //    encabezado.CentroCostos = Convert.ToInt32(usrId["centro_costos"]);
                        //    encabezado.EncabezadoPadre = factura.EncabezadoId;
                        //    encabezado.Coment1 = GetString(usrId["coment1"]);
                        //    encabezado.Coment2 = GetString(usrId["coment2"]);
                        //    encabezado.IdCartera = cartera.CarteraId;
                        //    encabezado.Ccy = "PESOS";
                        //    var partidas = new List<PartidaDocumento>();
                        //    var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                        //    query = "SELECT * FROM encabezadomov where id_encabezado = @encabezado";
                        //    parameters.Add("encabezado", Convert.ToInt32(documento["IdEncabezado"]));
                        //    var hola = RunQuery(query, parameters, false, conn, tx);


                        //    query = "INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
                        //        "values (@encabezado, 2, @sub, @imp, 1, 16, @prov, null)";
                        //    parameters.Add("sub", GetDecimal(usrId["sub"]));
                        //    parameters.Add("prov", GetString(usrId["cli_prov"]));
                        //    parameters.Add("imp", GetDecimal(usrId["sub"]) * 0.16m);
                        //    RunUpdate(query, parameters, false, conn, tx);

                        //    poliza = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), null, null, conn, tx);
                        //    resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), poliza, false, null, conn, tx);

                        //    CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), cartera.CarteraId, false, false, conn, tx);
                        //}

                    }
                    resultadoTimbrado = GenerarXml(factura);
                }

                if (!resultadoTimbrado.Success)
                {
                    if (!borradoEjecutado)
                    {
                        borradoEjecutado = true;
                    }
                    throw new Exception(resultadoTimbrado.Message);
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
                    { "isEn", true },
                    { "PdfUrlEN", Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}_EN.pdf") },
                    { "XmlUrl",Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml")},
                    { "CantidadProductos", factura.Tproductos.Rows.Count }
                };

                // Descontar material 
                var movimientos = new List<Dictionary<string, object>>();
                int userId = GetUserId(User.Identity.Name);
                int idEncabezado = Convert.ToInt32(factura.EncabezadoId);

                string queryTarima = "SELECT ct.id_tarima FROM catalmacenes c " +
                                     "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                                     "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                                     "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                                     "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                                     "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                                     "INNER JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima " +
                                     "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id " +
                                     "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Temporal'";
                parameters.Add("sucursal",Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                int destino = Convert.ToInt32(RunScalar(queryTarima, parameters));

                foreach (var p in productos)
                {
                    string prodCve = p.ContainsKey("productoId") ? p["productoId"] : "";
                    decimal cantidadSolicitada = p.ContainsKey("cantidad") ? GetDecimal(p["cantidad"]) : 0;
                    string unidadStr = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA"; // 🔹 viene del JSON

                    // 🔹 Obtener ID de unidad desde catunidades
                    int idudm = 0;
                    try
                    {
                        var paramUdm = new Dictionary<string, object> { { "cve_udm", unidadStr } };
                        string queryUdm = "SELECT id_udm FROM catunidades WHERE cve_udm = @cve_udm;";
                        idudm = Convert.ToInt32(RunScalar(queryUdm, paramUdm));
                    }
                    catch
                    {
                        Console.WriteLine($"⚠️ Unidad no encontrada para clave: {unidadStr}. Se asignará 0.");
                    }

                    // 🔹 Buscar datos del producto
                    var paramProd = new Dictionary<string, object> { { "cve_prod", prodCve }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } };
                    var prodInfo = RunQuery(@"
                                    SELECT id_catproductos AS id_producto, cve_prod AS codigo, descr_prod AS descripcion
                                    FROM catproductos
                                    WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id", paramProd);

                    if (prodInfo.Count == 0)
                    {
                        Console.WriteLine($"⚠️ Producto no encontrado: {prodCve}");
                        continue;
                    }

                    var prodData = prodInfo[0];
                    int idProducto = Convert.ToInt32(prodData["id_producto"]);
                    string codigo = prodData["codigo"].ToString();
                    string descripcion = prodData["descripcion"].ToString();

                    // 🔹 Buscar tarimas con stock disponible
                    decimal restante = cantidadSolicitada;

                    if (restante <= 0)
                        break;

                    // 🔹 Construir producto para movimiento
                    var prodMovimiento = new Dictionary<string, object>
                        {
                            { "id_producto", idProducto },
                            { "codigo", codigo },
                            { "descripcion", descripcion },
                            { "cantidad", restante },
                            { "unidad", idudm }, // 🔹 id real desde catunidades
                            { "tarima_id", destino },
                            { "tipo", "venta" },
                            { "movimiento", "salida" }
                        };

                    movimientos.Add(prodMovimiento);

                    // 🔹 Registrar movimiento de salida (por tarima)
                    //RegistrarMovimiento(
                    //    new List<Dictionary<string, object>> { prodMovimiento },
                    //    userId,
                    //    "venta",
                    //    destino,
                    //    null,
                    //    "salida",
                    //    idEncabezado,
                    //    conn,
                    //    tx
                    //);
                }

                return (true, resultadoTimbrado.Message, datosDetallados);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VINFactura/?");
                if (!borradoEjecutado)
                {
                    borradoEjecutado = true;
                }
                return (false, "Error al generar la factura: " + ex.Message, null);
            }
        }




        [Route("VINFactura/ProcesarDocumentosAsync")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<JsonResult> ProcesarDocumentosAsync(IFormCollection fc)
        {
            (bool Success, string Message, object Data) resultado1 = (false, string.Empty, null);
            var datosFactura = new Dictionary<string, object>();
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
                        // ── Guardar encabezado + partidas ────────────────────
                        resultado1 = await Guardar_ConRemisiones(fc, conn, tx);
                        if (!resultado1.Success)
                            return Json(new
                            {
                                success = false,
                                step = "GuardarFactura",
                                error = resultado1.Message
                            });

                        dynamic data = resultado1.Data;
                        int idEncabezado = data.IdEncabezado;

                        var ctx = new NCContext
                        {
                            Form = fc,
                            EncabezadoId = idEncabezado
                        };
                        //fc.Add("enc_id", idEncabezado.ToString());

                        // ── Timbrar ──────────────────────────────────────────
                        var resultado2 = await GenerarFacturaVentas(ctx, factura, conn, tx);
                        if (!resultado2.Success)
                            return Json(new
                            {
                                success = false,
                                step = "GenerarFactura",
                                error = resultado2.Message
                            });

                        // ── Actualizar documentos padre si aplica ────────────
                        string idsDocumentos = fc["documentid"].ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(idsDocumentos))
                        {
                            var ids = idsDocumentos
                                .Split(',')
                                .Select(s => s.Trim())
                                .Where(s => int.TryParse(s, out _))
                                .Select(int.Parse)
                                .Where(id => id > 0)
                                .Distinct()
                                .ToList();

                            // Solo actualizar documentos que NO sean remisiones
                            // (las remisiones las maneja ActualizarEstatusRemisionSiCompleta)
                            foreach (var id in ids)
                            {
                                var updParam = new Dictionary<string, object> { { "id", id } };
                                // Descomenta si necesitas cerrar el pedido padre:
                                // RunUpdate(@"UPDATE encabezadomov SET estatus_id = 11
                                //             WHERE id_encabezado = @id
                                //               AND nat NOT IN ('VINREM')",
                                //     updParam, false, conn, tx);
                            }
                        }

                        datosFactura = resultado2.Data as Dictionary<string, object>;
                        tx.Commit();
                        GuardarFactura(factura);

                        // El PDF DEBE esperarse: GenerarFactura es async y Rotativa/Razor
                        // dependen del HttpContext de esta request. Sin await la request
                        // termina, el contexto se libera y el PDF nunca se escribe.
                        // El CFDI ya está timbrado y commiteado, así que un fallo aquí se
                        // registra pero no invalida la respuesta (la factura existe).
                        try
                        {
                            await GenerarFactura(factura);
                        }
                        catch (Exception exPdf)
                        {
                            LogErrorHelper.RegistrarLog(
                                "VINFacturaController",
                                factura.UUID ?? idEncabezado.ToString(),
                                $"CFDI timbrado OK pero falló la generación del PDF: {exPdf.Message}",
                                nivel: "ERROR");
                        }

                        return Json(new
                        {
                            success = true,
                            message = "Todos los documentos fueron procesados y timbrados correctamente",
                            resumen = new
                            {
                                documentosProcesados = 1,
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
                                isEn = datosFactura?["isEn"],
                                pdfUrlEN = datosFactura?["PdfUrlEN"],
                                xmlUrl = datosFactura?["XmlUrl"]
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "VINFactura/?");
                        tx.Rollback();
                        LogErrorHelper.RegistrarLog(
                            "VINFacturaController",
                            "ProcesarDocumentosAsync",
                            $"Error en transacción: {ex.Message}",
                            nivel: "ERROR");
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
    }
}