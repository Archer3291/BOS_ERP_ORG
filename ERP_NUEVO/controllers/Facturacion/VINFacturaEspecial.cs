//using BOS_ERP.Controllers.Facturacion.Productos;
//using Newtonsoft.Json;
//using Npgsql;
//using BOS_ERP.Filters;
//using BOS_ERP.Models;
//using BOS_ERP.Models.Carteras.Cliente;
//using System.Collections.Specialized;
//using System.Configuration;
//using System.Data;
//using System.Text;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Authorization;
//using BOS_ERP.Services;

//namespace BOS_ERP.Controllers.Facturacion
//{
//    [Authorize]
//    public class VINFacturaEspecialController : FacturacionVentaINController
//    {
//        public VINFacturaEspecialController(EmailSender emailSender) : base(emailSender)
//        {
//        }

//        [HttpPost, ValidateAntiForgeryToken]
//        [AuditAction(Modulo = "Ventas Internacionales", Accion = "Creacion de factura")]
//        public async Task<(bool Success, string Message, object Data)> Guardar(IFormCollection fc, NpgsqlConnection conn, NpgsqlTransaction tx)
//        {
//            try
//            {
//                string tipo = fc["tipo"].ToString() ?? ""; // contado, credito, anticipo

//                // 🔹 Validaciones previas
//                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
//                    throw new Exception("Debe seleccionar un cliente.");

//                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
//                    throw new Exception("Debe seleccionar una moneda.");

//                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
//                    throw new Exception("Debe seleccionar un vendedor.");

//                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
//                    throw new Exception("Debe seleccionar una forma de pago.");

//                // 🔹 Validar productos solo si NO es anticipo
//                var fPagoParameter = new Dictionary<string, object>();
//                fPagoParameter.Add("cve_sat", fc["forma-pago"].ToString());
//                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
//                if (tipo != "anticipo")
//                {
//                    if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
//                        throw new Exception("Debe agregar al menos un producto.");

//                    productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
//                    if (productos == null || productos.Count == 0)
//                        throw new Exception("Debe agregar al menos un producto.");
//                }

//                // 🔹 Validar fecha de pago solo si es crédito
//                DateTime? fechaPago = null;
//                if (tipo == "credito")
//                {
//                    if (string.IsNullOrWhiteSpace(fc["fechaPago"].ToString()))
//                        throw new Exception("Debe ingresar la fecha de pago (solo para crédito).");

//                    if (!DateTime.TryParse(fc["fechaPago"].ToString(), out DateTime fch))
//                        throw new Exception("Formato de fecha de pago no válido.");

//                    fechaPago = fch;
//                }
//                else if (tipo == "anticipo")
//                {
//                    if (string.IsNullOrWhiteSpace(fc["fechaAnticipo"].ToString()))
//                        throw new Exception("Debe ingresar la fecha del anticipo.");
//                    if (!DateTime.TryParse(fc["fechaAnticipo"].ToString(), out DateTime fch))
//                        throw new Exception("Formato de fecha del anticipo no válido.");
//                    fechaPago = fch;
//                }
//                int idEncabezadoPadre = 0;
//                int usrId0 = 0;
//                DateTime usrFch0 = DateTime.Now;
//                int usrId1 = 0;
//                DateTime usrFch1 = DateTime.Now;
//                int usrId2 = 0;
//                DateTime usrFch2 = DateTime.Now;

//                if (!string.IsNullOrWhiteSpace(fc["documentid"].ToString())) // 👈 Nota: ahora con '!'
//                {
//                    // 🔹 Cargar datos del encabezado existente
//                    var usrParameter = new Dictionary<string, object>();
//                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3,  " +
//                                      "       em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto " +
//                                      "FROM encabezadomov em " +
//                                      "WHERE em.id_encabezado = @id";

//                    usrParameter.Add("id", Convert.ToInt32(fc["documentid"].ToString()));

//                    var result = RunQuery(usrquery, usrParameter, false, conn, tx);

//                    if (result.Count > 0)
//                    {
//                        var usrId = result[0];
//                        usrId0 = Convert.ToInt32(usrId["usr0"]);
//                        usrFch0 = Convert.ToDateTime(usrId["fch0"]);
//                        usrId1 = Convert.ToInt32(usrId["usr1"]);
//                        usrFch1 = Convert.ToDateTime(usrId["fch1"]);
//                        usrId2 = Convert.ToInt32(usrId["usr2"]);
//                        usrFch2 = Convert.ToDateTime(usrId["fch2"]);
//                        idEncabezadoPadre = Convert.ToInt32(fc["documentid"].ToString());
//                    }
//                }

//                int idCliente = 0;
//                string clienteNombre = "";
//                if (!string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
//                {
//                    var clienteParameter = new Dictionary<string, object>();
//                    string clientequery = "SELECT cl.id_cliente, cl.n_cli " +
//                                          "FROM catclientes cl " +
//                                          "WHERE cl.cve_cli = @cliente AND cl.empresa_id = @empresa_id";
//                    clienteParameter.Add("cliente", fc["cliente"].ToString());
//                    clienteParameter.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
//                    var clienteResult = RunQuery(clientequery, clienteParameter, false, conn, tx);
//                    if (clienteResult.Count > 0)
//                    {
//                        var cliente = clienteResult[0];
//                        idCliente = Convert.ToInt32(cliente["id_cliente"]);
//                        clienteNombre = cliente["n_cli"].ToString();

//                    }
//                }

//                // 🔹 Crear encabezado
//                var encabezado = new DocumentoEncabezado();

//                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
//                encabezado.IdArea = 17;
//                encabezado.IdTpDoc = 56;
//                encabezado.UsrDep = GetAreaName(User.Identity.Name);
//                encabezado.Anio = DateTime.Now.Year;
//                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
//                encabezado.Alm = fc["almacen"].ToString();
//                encabezado.Fch = DateTime.Now;
//                encabezado.TpMov = "VINFAC";
//                encabezado.ComentAut = fc["comentarios"].ToString();
//                encabezado.UsrDoc = User.Identity.Name;
//                encabezado.FchCap = DateTime.Now;
//                encabezado.Usr0 = usrId0;
//                encabezado.Fch0 = usrFch0;
//                encabezado.Usr1 = usrId1;
//                encabezado.Fch1 = usrFch1;
//                encabezado.Usr2 = usrId2;
//                encabezado.Fch2 = usrFch2;
//                encabezado.Usr3 = GetUserId(User.Identity.Name);
//                encabezado.Fch3 = DateTime.Now;
//                encabezado.Imp = Convert.ToDecimal(fc["total"].ToString());
//                encabezado.Sub = Convert.ToDecimal(fc["subtotal1"].ToString());
//                encabezado.CliProv = fc["cliente"].ToString();
//                encabezado.Ref = idCliente;
//                encabezado.Ccy = fc["moneda"].ToString();
//                encabezado.Estatus = 1;
//                encabezado.Flete = Convert.ToDecimal(fc["flete"].ToString());
//                encabezado.VdrCpr = fc["vendedor"].ToString();
//                encabezado.Coment1 = fc["concepto"].ToString();
//                encabezado.EncabezadoPadre = idEncabezadoPadre;
//                encabezado.PlDias = string.IsNullOrWhiteSpace(fc["plazo"].ToString()) ? 0 : Convert.ToInt32(fc["plazo"].ToString());
//                encabezado.FchPgEntrega = fechaPago ?? DateTime.Now; // si no hay, usa fecha actua;
//                encabezado.Par = Convert.ToDecimal(fc["paridad"].ToString());
//                encabezado.Incoterm = fc["incoterm"].ToString();
//                encabezado.FPago = Convert.ToInt32(RunScalar("select id_f_pago from cat_f_pago where cve_sat = @cve_sat", fPagoParameter));
//                encabezado.Mdp = fc["metodo-pago"].ToString();
//                encabezado.TipoPoceso = "factura_" + tipo;
//                encabezado.CFDI = fc["uso-cfdi"].ToString();
//                encabezado.CentroCostos = Convert.ToInt32(fc["CentroCostosId"].ToString());
//                encabezado.Dto = Convert.ToDecimal(fc["descuento"].ToString());


//                // 🔹 Crear partidas (solo si hay productos)
//                var partidas = new List<PartidaDocumento>();
//                if (productos.Count > 0)
//                {
//                    foreach (var p in productos)
//                    {
//                        var parametersP = new Dictionary<string, object>();
//                        string queryId = "SELECT id_catproductos from catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id";
//                        parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
//                        parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
//                        int productId = Convert.ToInt32(RunScalar(queryId, parametersP));

//                        partidas.Add(new PartidaDocumento
//                        {
//                            CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
//                            DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
//                            CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
//                            PvProd = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0,
//                            Dto1 = (p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0) / 100 * (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) *
//                                      (p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0),
//                            ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) *
//                                      (p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0),
//                            Ud = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA",
//                            IdProducto = productId,
//                            CveCli = p.ContainsKey("claveCliente") ? p["claveCliente"].ToString() : "",
//                        });
//                    }
//                }

//                // 🔹 Guardar documento
//                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

//                //var fpagoParameter = new Dictionary<string, object>();
//                //string fpago = "select id_f_pago from cat_f_pago where cve_sat = @f_pago_id ";
//                //fpagoParameter.Add("f_pago_id", fc["forma-pago"].ToString());
//                //int fp = Convert.ToInt32(RunScalar(fpago, fpagoParameter));

//                //Comentado por no saber ek proceso de impuestos en ventas internacionales
//                //string inpuestos = "INSERT INTO imp_oc " +
//                //    "(encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
//                //    "VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);";
//                //var inpuestosParameter = new Dictionary<string, object>();

//                //inpuestosParameter.Add("encabezado_id", Convert.ToInt32(folio["IdEncabezado"]));
//                //inpuestosParameter.Add("impuesto_id", 1); // IVA
//                //inpuestosParameter.Add("subtotal", Convert.ToDecimal(fc["subtotal1"].ToString()));
//                //inpuestosParameter.Add("importe", Convert.ToDecimal(fc["iva"].ToString()));
//                //inpuestosParameter.Add("orden_apl", 1);
//                //inpuestosParameter.Add("imp_variable", 16);
//                //inpuestosParameter.Add("prov_nom", clienteNombre);
//                //inpuestosParameter.Add("f_pago_id", fp);


//                //RunQuery(inpuestos, inpuestosParameter);


//                ViewData["detalles"] = new AuditDetails
//                {
//                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
//                    Folio = folio["folio_generado"].ToString()
//                };

//                return (true, "Documentos guardados correctamente", new
//                {
//                    Folio = folio["folio_generado"].ToString(),
//                    IdEncabezado = Convert.ToInt32(folio["IdEncabezado"])
//                });
//            }
//            catch (Exception ex)
//            {
//                return (false, "Error en guardar el documento de factura: " + ex.Message, null);
//            }
//        }


//        [Route("FacturacionVentaIN/Factura")]
//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<(bool Success, string Message, object Data)> GenerarFacturaVentas(IFormCollection fc, Factura factura, NpgsqlConnection conn, NpgsqlTransaction tx)
//        {
//            string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
//                ? HttpContext.Session.GetString("EmpresaFactura")
//                : "pruebas";
//            bool borradoEjecutado = false; // 🔹 Bandera global            
//            try
//            {
//                var emisores = _configuration.GetSection($"emisores:{perfil}");
//                // 🔹 Tipo de facturación: contado, crédito, anticipo
//                string tipo = fc["tipo"].ToString() ?? "";

//                // 🔹 Validaciones básicas
//                if (string.IsNullOrWhiteSpace(fc["rfc"].ToString()))
//                    throw new Exception("Debe seleccionar un cliente.");


//                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
//                    throw new Exception("Debe seleccionar una moneda.");

//                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
//                    throw new Exception("Debe seleccionar una forma de pago.");

//                if (string.IsNullOrWhiteSpace(fc["metodo-pago"].ToString()))
//                    throw new Exception("Debe seleccionar un método de pago.");

//                // 🔹 Validar productos solo si NO es anticipo
//                List<dynamic> productos = new List<dynamic>();
//                List<dynamic> anticipo = new List<dynamic>();
//                if (tipo != "anticipo")
//                {
//                    if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
//                        throw new Exception("Debe agregar al menos un producto.");

//                    productos = JsonConvert.DeserializeObject<List<dynamic>>(fc["productosJSON"].ToString());
//                    if (productos == null || productos.Count == 0)
//                        throw new Exception("Debe agregar al menos un producto.");
//                }
//                if (tipo == "contado" && !string.IsNullOrWhiteSpace(fc["anticiposJSON"].ToString()))
//                {
//                    anticipo = JsonConvert.DeserializeObject<List<dynamic>>(fc["anticiposJSON"].ToString());
//                }

//                // 🔹 Validar fecha de pago o anticipo
//                DateTime? fechaPago = null;
//                if (tipo == "credito")
//                {
//                    if (string.IsNullOrWhiteSpace(fc["FechaPago"].ToString()))
//                        throw new Exception("Debe ingresar la fecha de pago (solo para crédito).");

//                    if (!DateTime.TryParse(fc["FechaPago"].ToString(), out DateTime fch))
//                        throw new Exception("Formato de fecha de pago no válido.");

//                    fechaPago = fch;
//                }
//                else if (tipo == "anticipo")
//                {
//                    if (string.IsNullOrWhiteSpace(fc["FechaAnticipo"].ToString()))
//                        throw new Exception("Debe ingresar la fecha del anticipo.");

//                    if (!DateTime.TryParse(fc["FechaAnticipo"].ToString(), out DateTime fch))
//                        throw new Exception("Formato de fecha del anticipo no válido.");


//                    fechaPago = fch;
//                }

//                // 🔹 Consultas para obtener descripciones (como en tu versión original)
//                var parameters = new Dictionary<string, object>();
//                string query = "";

//                query = "SELECT descripcion FROM mdp WHERE cve_mdp = @cve;";
//                parameters.Add("cve", fc["metodo-pago"].ToString());
//                string mdp = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
//                parameters.Clear();

//                query = "SELECT cve_mdp FROM mdp WHERE cve_mdp = @cve;";
//                parameters.Add("cve", fc["metodo-pago"].ToString());
//                string mdp1 = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
//                parameters.Clear();

//                query = "SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve;";
//                parameters.Add("cve", fc["forma-pago"].ToString());
//                string tp = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
//                parameters.Clear();

//                query = "SELECT descripcion FROM catusocfdi WHERE clave = @cve;";
//                parameters.Add("cve", fc["uso-cfdi"].ToString());
//                string usoCFDItext = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "";
//                parameters.Clear();

//                query = "SELECT descripcion FROM catregimenfiscal WHERE clave = @cve;";
//                parameters.Add("cve", fc["RegimenFiscalReceptor"].ToString());
//                string regimenText = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "General de Ley Personas Morales";
//                parameters.Clear();

//                query = "SELECT  id_cliente, n_cli, cp, rfc FROM catclientes where cve_cli = @cve_cli AND empresa_id = @empresa_id;";
//                parameters.Add("cve_cli", fc["cliente"].ToString());
//                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
//                var datosCliente = RunQuery(query, parameters, false, conn, tx);


//                query = "SELECT regimen_fiscal FROM direcciones_facturacion where entidad_clave = @cve_cli;";
//                string rScocial = RunScalar(query, parameters, false, conn, tx)?.ToString() ?? "616";


//                string moneda = "";
//                if (fc["moneda"].ToString() == "PESOS")
//                {
//                    moneda = "MXN";
//                }
//                else if (fc["moneda"].ToString() == "DLLS")
//                {
//                    moneda = "USD";
//                }
//                else if (fc["moneda"].ToString() == "EURO")
//                {
//                    moneda = "EUR";
//                }

//                decimal flete = Convert.ToDecimal(fc["flete"].ToString());

//                AplicarAnticipos anticipoModelo = new AplicarAnticipos();
//                // 🔹 Crear objeto Factura

//                factura.Serie = fc["Serie"].ToString() ?? "VIS";
//                factura.IdTipoPago = fc["forma-pago"].ToString();
//                factura.Moneda = moneda;
//                factura.CpE = emisores[$"{perfil}.CpE"];
//                factura.RfcEmisor = emisores[$"{perfil}.Rfc"];
//                factura.RsoEmisor = emisores[$"{perfil}.RazonSocial"];
//                factura.Rege = emisores[$"{perfil}.Regimen"];
//                factura.RfcCliente = fc["rfc"].ToString();
//                factura.RsoCliente = datosCliente[0]["n_cli"].ToString();
//                factura.CpR = datosCliente[0]["cp"].ToString();
//                factura.IdUsoCFDI = fc["uso-cfdi"].ToString();
//                factura.CFDIText = usoCFDItext;
//                factura.Regc = "616";
//                factura.regimenEText = regimenText;
//                factura.Subtotal = Convert.ToDecimal(fc["subtotal2"].ToString() ?? "0");
//                factura.MontoAnticipo = Convert.ToDecimal(fc["subtotal1"].ToString() ?? "0");
//                factura.IVA = Convert.ToDecimal("0");
//                factura.Total = Convert.ToDecimal(fc["totalFinal"].ToString() ?? "0");
//                factura.TipoCambio = Convert.ToDecimal(fc["paridad"].ToString() ?? "1.00");
//                factura.LugarExpedicion = emisores[$"{perfil}.CpE"];
//                factura.metodoPagoTexto = mdp1 ?? "PPD";
//                factura.MdpFactura = mdp;
//                factura.TipoDeComprobante = fc["TipoDeComprobante"].ToString();
//                factura.Observaciones = fc["Observaciones"].ToString();
//                factura.formaPagoTexto = tp;
//                factura.Fecha = DateTime.Now;
//                factura.TipoFacturacion = tipo;
//                factura.FechaTimbrado = fechaPago.ToString();
//                factura.Flete = flete;
//                factura.EncabezadoId = Convert.ToInt32(fc["enc_id"].ToString());
//                factura.Exportacion = "02";
//                factura.IdCliente = (int)datosCliente[0]["id_cliente"];
//                factura.DireccionReceptor = fc["direccion-receptor"].ToString() ?? "";

//                parameters = new Dictionary<string, object>();
//                parameters.Add("encabezado", Convert.ToInt32(fc["enc_id"].ToString()));

//                query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio FROM encabezadomov em WHERE id_encabezado = @encabezado";
//                var result = RunScalar(query, parameters, false, conn, tx);

//                if (result == null)
//                {
//                    throw new Exception("folio no encontrado para el encabezado.");
//                }

//                string uuid = result.ToString();


//                string folioCompleto = uuid;


//                factura.Folio = folioCompleto;
//                factura.FolioCorto = fc["folio"].ToString();


//                //Comercio Exterior inicio
//                bool aplicaCE = fc["aplicaComercioExterior"].ToString() == "1";
//                factura.Exportacion = aplicaCE ? "02" : "01";

//                if (aplicaCE)
//                {
//                    var ce = new ComercioExterior
//                    {
//                        Version = "2.0",
//                        MotivoTraslado = fc["ce_motivoTraslado"].ToString() ?? "",
//                        ClaveDePedimento = fc["ce_clavePedimento"].ToString() ?? "",
//                        CertificadoOrigen = fc["ce_certificadoOrigen"].ToString() ?? "",
//                        NumCertificadoOrigen = fc["ce_numCertificadoOrigen"].ToString() ?? "",
//                        NumeroExportadorConfiable = fc["ce_numExportadorConfiable"].ToString() ?? "",
//                        Incoterm = fc["incoterm"].ToString() ?? "",
//                        TipoCambioUSD = decimal.TryParse(fc["ce_tipoCambioUSD"].ToString(), out decimal tcUsd) ? tcUsd : 0m,
//                        TotalUSD = decimal.TryParse(fc["ce_totalUSD"].ToString(), out decimal tUsd) ? tUsd : 0m,
//                        NumRegIdTrib = fc["ce_propietario_numRegIdTrib"].ToString() ?? "",
//                    };

//                    ce.DomicilioEmisor = new DomicilioComExt
//                    {
//                        Calle = fc["ce_emisor_calle"].ToString() ?? "",
//                        NumeroExterior = fc["ce_emisor_numExterior"].ToString() ?? "",
//                        Colonia = fc["ce_emisor_colonia"].ToString() ?? "",
//                        Localidad = fc["ce_emisor_localidad"].ToString() ?? "",
//                        Municipio = fc["ce_emisor_municipio"].ToString() ?? "",
//                        Estado = fc["ce_emisor_estado"].ToString() ?? "",
//                        Pais = fc["ce_emisor_pais"].ToString() ?? "",
//                        CodigoPostal = fc["ce_emisor_codigoPostal"].ToString() ?? ""
//                    };

//                    ce.DomicilioDestinatario = new DomicilioComExt
//                    {
//                        Calle = fc["ce_receptor_calle"].ToString() ?? "",
//                        NumeroExterior = fc["ce_receptor_numExterior"].ToString() ?? "",
//                        Colonia = fc["ce_receptor_colonia"].ToString() ?? "",
//                        Localidad = fc["ce_receptor_localidad"].ToString() ?? "",
//                        Municipio = fc["ce_receptor_municipio"].ToString() ?? "",
//                        Estado = fc["ce_receptor_estado"].ToString() ?? "",
//                        Pais = fc["ce_receptor_pais"].ToString() ?? "",
//                        CodigoPostal = fc["ce_receptor_codigoPostal"].ToString() ?? ""
//                    };

//                    // Mercancias: aquí SÍ se valida fracción arancelaria
//                    ce.Mercancias = new List<MercanciaExportada>();
//                    if (!string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
//                    {
//                        var productosCE = JsonConvert.DeserializeObject<List<dynamic>>(fc["productosJSON"].ToString());
//                        foreach (var p in productosCE)
//                        {
//                            var arParam = new Dictionary<string, object> { { "cve_prod", p.productoId.ToString() } };
//                            string qFrac = "SELECT frac, unidad FROM frac_arancelarias fa " +
//                                           "INNER JOIN fracciones_arancelarias_sat fas ON fas.fraccion_arancelaria = fa.frac " +
//                                           "WHERE cve_prod = @cve_prod";
//                            var frac = RunQuery(qFrac, arParam, false, conn, tx);

//                            if (frac.Count == 0 || string.IsNullOrWhiteSpace(frac[0]["frac"]?.ToString()))
//                                throw new Exception($"El producto {p.productoId} no tiene fracción arancelaria asignada.");

//                            ce.Mercancias.Add(new MercanciaExportada
//                            {
//                                NoIdentificacion = (string)p.productoId,
//                                FraccionArancelaria = frac[0]["frac"].ToString(),
//                                Descripcion = (string)p.descripcion,
//                                CantidadAduana = Convert.ToDecimal(p.cantidad),
//                                UnidadAduana = frac[0]["unidad"].ToString(),
//                                ValorUnitarioAduana = Convert.ToDecimal(p.precio),
//                                ValorDolares = Convert.ToDecimal(p.importe),
//                                Unidad = (string)p.unidad,
//                                Cantidad = Convert.ToDecimal(p.cantidad),
//                                Pedimentos = new List<Pedimento>
//                {
//                    new Pedimento { Numero = p.pedimento?.ToString() ?? "" }
//                }
//                            });
//                        }
//                    }

//                    factura.ComercioExterior = ce;
//                }
//                else
//                {
//                    // Servicios: sin complemento CE, sin validar fracción arancelaria
//                    factura.ComercioExterior = null;
//                }
//                //Comercio Exterior fin

//                // 🔹 Crear DataTable para productos
//                factura.Tproductos = new System.Data.DataTable();
//                factura.Tproductos.Columns.AddRange(new[]
//                {
//                    new DataColumn("numero", typeof(string)),
//                    new DataColumn("claveProdServ", typeof(string)),
//                    new DataColumn("claveUnidad", typeof(string)),
//                    new DataColumn("unidad", typeof(string)),
//                    new DataColumn("descripcion", typeof(string)),
//                    new DataColumn("cantidad", typeof(double)),
//                    new DataColumn("precioUnit", typeof(double)),
//                    new DataColumn("importe", typeof(double)),
//                    new DataColumn("objetoImp", typeof(string)),
//                    new DataColumn("prod_id", typeof(int)),
//                    new DataColumn("clave_cliente", typeof(string)),
//                    new DataColumn("pedimento", typeof(string)),
//                    new DataColumn("comentario", typeof(string)),
//                    new DataColumn("descuento", typeof(double))   // ← NUEVO
//                });

//                if (productos.Count > 0)
//                {
//                    foreach (var prod in productos)
//                    {
//                        var parametersProd = new Dictionary<string, object>();
//                        string product = "SELECT prod_sat, ud_sat, obj_impto from catrelacion WHERE prod_kepler = @cve";
//                        parametersProd.Add("cve", (string)prod.productoId);
//                        parametersProd.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
//                        var dat = RunQuery(product, parametersProd, false, conn, tx);

//                        if (dat.Count == 0)
//                        {
//                            throw new Exception("El producto no tiene informacion fiscal registrada " + (string)prod.productoId);
//                        }


//                        string queryId = "SELECT id_catproductos from catproductos WHERE cve_prod = @cve AND empresa_id = @empresa_id";
//                        int productId = Convert.ToInt32(RunScalar(queryId, parametersProd, false, conn, tx));

//                        // ✅ DESPUÉS - funciona con y sin CE
//                        string pedimento = "";
//                        if (aplicaCE && factura.ComercioExterior?.Mercancias != null)
//                        {
//                            var mercancia = factura.ComercioExterior.Mercancias
//                                .FirstOrDefault(m => m.NoIdentificacion == (string)prod.productoId);
//                            pedimento = mercancia?.Pedimentos?.FirstOrDefault()?.Numero ?? "";
//                        }
//                        else
//                        {
//                            // Sin CE: tomar el pedimento directo del JSON del producto
//                            pedimento = prod.pedimento?.ToString() ?? "";
//                        }

//                        double descuentoPct = prod.descuento != null ? (double)prod.descuento : 0.0;
//                        double importeConDescuento = (double)prod.cantidad * (double)prod.precio * (1 - descuentoPct / 100);

//                        factura.Tproductos.Rows.Add(
//                            (string)prod.productoId,
//                            (string)dat[0]["prod_sat"],
//                            (string)dat[0]["ud_sat"],
//                            (string)prod.unidad,
//                            (string)prod.descripcion,
//                            (double)prod.cantidad,
//                            (double)prod.precio,          // precio bruto sin tocar
//                            (double)prod.cantidad * (double)prod.precio,  // importe bruto (o el campo que ya existía)
//                            (string)prod.objetoImp ?? "02",
//                            (int)productId,
//                            (string)prod.claveCliente,
//                            pedimento,
//                            (string)prod.comentario ?? "",
//                            descuentoPct                  // solo el % viaja aquí
//                        );
//                    }
//                }

//                // 🔹 Crear DataTable para anticipos
//                factura.TAnticipos = new System.Data.DataTable();
//                factura.TAnticipos.Columns.AddRange(new[]
//                {
//                    new DataColumn("uuid", typeof(string)),
//                    new DataColumn("fecha", typeof(string)),
//                    new DataColumn("monto_aplicado", typeof(decimal)),
//                    new DataColumn("saldo_antes", typeof(decimal)),
//                    new DataColumn("saldo_despues", typeof(decimal))
//                });

//                // 🔹 Variables acumuladoras
//                decimal totalAnticiposAplicados = 0;
//                decimal totalFactura = Convert.ToDecimal(fc["subtotal2"].ToString() ?? "0");
//                decimal ivaFactura = Convert.ToDecimal(fc["iva"].ToString() ?? "0");
//                decimal totalFacturaConIVA = totalFactura + ivaFactura;

//                // 🔹 Si hay anticipos, procesarlos
//                if (anticipo.Count > 0)
//                {
//                    totalAnticiposAplicados = Convert.ToDecimal(fc["totalAnticipos"].ToString() ?? "0");

//                    // 🔹 Consultar información de todos los anticipos para aplicar proporción correctamente
//                    var anticiposDisponibles = new List<(int IdEncabezado, string UUID, decimal Saldo, decimal Total, int IdFactura)>();

//                    foreach (var ant in anticipo)
//                    {
//                        var parametersAnt = new Dictionary<string, object> { { "cve", (int)ant.id_encabezado } };
//                        string sqlAnt = "SELECT id, uuid, total, saldo FROM factura WHERE encabezado_id = @cve;";
//                        var dat = RunQuery(sqlAnt, parametersAnt, false, conn, tx);
//                        if (dat.Count == 0)
//                            throw new Exception($"No se encontró el anticipo con encabezado {ant.id_encabezado}.");

//                        anticiposDisponibles.Add((
//                            IdEncabezado: Convert.ToInt32(ant.id_encabezado),
//                            UUID: dat[0]["uuid"].ToString(),
//                            Saldo: Convert.ToDecimal(dat[0]["saldo"]),
//                            Total: Convert.ToDecimal(dat[0]["total"]),
//                            IdFactura: Convert.ToInt32(dat[0]["id"])
//                        ));
//                    }

//                    // 🔹 Calcular total aplicable y exceso
//                    decimal saldoTotalDisponible = anticiposDisponibles.Sum(a => a.Saldo);
//                    if (saldoTotalDisponible <= 0)
//                        throw new Exception("Los anticipos seleccionados no tienen saldo disponible.");

//                    decimal totalAplicableAFactura = Math.Min(totalFacturaConIVA, totalAnticiposAplicados);
//                    decimal exceso = totalAnticiposAplicados - totalAplicableAFactura;

//                    decimal restante = totalAplicableAFactura;
//                    var operacionesPendientes = new List<(int IdEncabezado, decimal NuevoSaldo, decimal MontoAplicado, int IdAnticipo, decimal SaldoAntes)>();

//                    foreach (var ant in anticiposDisponibles)
//                    {
//                        if (restante <= 0) break;

//                        decimal proporcion = ant.Saldo / saldoTotalDisponible;
//                        decimal montoAplicado = Math.Round(totalAplicableAFactura * proporcion, 2);

//                        if (montoAplicado > ant.Saldo)
//                            montoAplicado = ant.Saldo;

//                        if (montoAplicado > restante)
//                            montoAplicado = restante;

//                        decimal nuevoSaldo = ant.Saldo - montoAplicado;
//                        restante -= montoAplicado;

//                        // 🔹 Registrar en DataTable para XML / complemento
//                        factura.TAnticipos.Rows.Add(
//                            ant.UUID,
//                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
//                            montoAplicado,
//                            ant.Saldo,
//                            nuevoSaldo
//                        );

//                        // 🔹 Registrar operación pendiente (para actualizar DB post-timbrado)
//                        operacionesPendientes.Add((ant.IdEncabezado, nuevoSaldo, montoAplicado, ant.IdFactura, ant.Saldo));
//                    }

//                    // 🔹 Guardar operaciones para después del timbrado
//                    TempData["OperacionesAnticipos"] = JsonConvert.SerializeObject(operacionesPendientes);

//                    // 🔹 Guardar exceso para futuras facturas
//                    TempData["ExcesoAnticipo"] = exceso;

//                    // 🔹 Ajustar total de factura solo restando lo aplicado
//                    decimal totalAplicadoFinal = factura.TAnticipos.AsEnumerable().Sum(r => Convert.ToDecimal(r["monto_aplicado"]));
//                    factura.Total = Convert.ToDecimal(totalFacturaConIVA - totalAplicadoFinal);
//                    if (factura.Total < 0) factura.Total = 0;
//                }
//                else
//                {
//                    // Sin anticipos, total normal
//                    factura.Total = Convert.ToDecimal(fc["subtotal1"].ToString() ?? "0") + Convert.ToDecimal(fc["iva"].ToString() ?? "0");
//                }


//                // 🔹 Generar XML y timbrar dependiendo del tipo
//                TimbradoResult resultadoTimbrado;

//                if (factura.TipoFacturacion?.ToLower() == "anticipo")
//                {
//                    factura.Saldo = factura.Total;
//                    parameters = new Dictionary<string, object>();
//                    query = "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado";
//                    parameters.Add("id_encabezado", Convert.ToInt32(fc["enc_id"].ToString()));

//                    int enc = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);
//                    if (enc > 0)
//                    {
//                        var poliza = GenerarDatosPoliza(enc, fc["CuentaBancariaId"].ToString(), null, conn, tx);
//                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
//                        CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Anticipo, conn, tx);
//                    }
//                    resultadoTimbrado = GenerarXmlAnticipo(factura);
//                }
//                else if (factura.TipoFacturacion?.ToLower() == "credito")
//                {
//                    parameters = new Dictionary<string, object>();
//                    query = "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado";
//                    parameters.Add("id_encabezado", Convert.ToInt32(fc["enc_id"].ToString()));

//                    int enc = Convert.ToInt32(RunScalar(query, parameters) ?? 0);
//                    if (enc > 0)
//                    {
//                        List<PolizaData> poliza = GenerarDatosPoliza(enc, fc["CuentaBancariaId"].ToString(), null, conn, tx);
//                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
//                        RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);
//                    }
//                    resultadoTimbrado = GenerarXml(factura);
//                }
//                else
//                {
//                    parameters = new Dictionary<string, object>();
//                    query = "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id_encabezado";
//                    parameters.Add("id_encabezado", Convert.ToInt32(fc["enc_id"].ToString()));

//                    int enc = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);
//                    if (enc > 0)
//                    {
//                        List<PolizaData> poliza = GenerarDatosPoliza(enc, fc["CuentaBancariaId"].ToString(), null, conn, tx);
//                        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), enc, poliza, false, null, conn, tx);
//                        RegistrarCarteraResult cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

//                        bool carteraSaldada = false;

//                        if (anticipo.Count > 0)
//                        {
//                            anticipoModelo.CarteraId = cartera.CarteraId;
//                            anticipoModelo.UsuarioId = GetUserId(User.Identity.Name);

//                            ResultadoAplicacionAnticipos resultado = AplicarAnticipos(anticipoModelo, conn, tx);

//                            if (resultado != null)
//                                carteraSaldada = resultado.CarteraSaldada;
//                        }

//                        query = "SELECT refe FROM encabezadomov WHERE id_encabezado = @id_encabezado ";
//                        int cli = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx) ?? 0);

//                        query = "SELECT es_especial from catclientes WHERE id_cliente = @cli ";
//                        parameters.Add("cli", cli);
//                        bool es_especial = Convert.ToBoolean(RunScalar(query, parameters, false, conn, tx) ?? 0);

//                        //if (!carteraSaldada && !es_especial)
//                        //{
//                        //    query = "SELECT cli_prov, refe, centro_costos, id_encabezado, coment1, coment2, usr_dep, usr_doc, usr0, sub, fch " +
//                        //        "FROM encabezadomov " +
//                        //        "WHERE id_encabezado = @i";
//                        //    parameters = new Dictionary<string, object>();
//                        //    parameters.Add("i", factura.EncabezadoId);
//                        //    var usrId = RunQuery(query, parameters, false, conn, tx)[0];

//                        //    var encabezado = new DocumentoEncabezado();
//                        //    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
//                        //    encabezado.IdArea = 20;
//                        //    encabezado.IdTpDoc = 70;
//                        //    encabezado.UsrDep = GetString(usrId["usr_dep"]);
//                        //    encabezado.Anio = DateTime.Now.Year;
//                        //    encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
//                        //    encabezado.Fch = (DateTime)usrId["fch"];
//                        //    encabezado.TpMov = "CXC";
//                        //    encabezado.UsrDoc = GetString(usrId["usr_doc"]);
//                        //    encabezado.FchCap = GetDate(usrId["fch"]);
//                        //    encabezado.Usr0 = GetInt(usrId["usr0"]);
//                        //    encabezado.Fch0 = GetDate(usrId["fch"]);
//                        //    encabezado.Imp = cartera.MontoTotal;
//                        //    encabezado.Sub = GetDecimal(usrId["sub"]);
//                        //    encabezado.CliProv = usrId["cli_prov"].ToString();
//                        //    encabezado.Ref = Convert.ToInt32(usrId["refe"]);
//                        //    encabezado.Estatus = 11;
//                        //    encabezado.TipoPoceso = "cobro_cliente";
//                        //    encabezado.CentroCostos = Convert.ToInt32(usrId["centro_costos"]);
//                        //    encabezado.EncabezadoPadre = factura.EncabezadoId;
//                        //    encabezado.Coment1 = GetString(usrId["coment1"]);
//                        //    encabezado.Coment2 = GetString(usrId["coment2"]);
//                        //    encabezado.IdCartera = cartera.CarteraId;
//                        //    encabezado.Ccy = "PESOS";
//                        //    var partidas = new List<PartidaDocumento>();
//                        //    var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

//                        //    query = "SELECT * FROM encabezadomov where id_encabezado = @encabezado";
//                        //    parameters.Add("encabezado", Convert.ToInt32(documento["IdEncabezado"]));
//                        //    var hola = RunQuery(query, parameters, false, conn, tx);


//                        //    query = "INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
//                        //        "values (@encabezado, 2, @sub, @imp, 1, 16, @prov, null)";
//                        //    parameters.Add("sub", GetDecimal(usrId["sub"]));
//                        //    parameters.Add("prov", GetString(usrId["cli_prov"]));
//                        //    parameters.Add("imp", GetDecimal(usrId["sub"]) * 0.16m);
//                        //    RunUpdate(query, parameters, false, conn, tx);

//                        //    poliza = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), "1-1-02-01-0002", null, conn, tx);
//                        //    resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), poliza, false, null, conn, tx);

//                        //     CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Normal, conn, tx);
//                        //}

//                    }
//                    resultadoTimbrado = GenerarXml(factura);
//                }

//                if (!resultadoTimbrado.Success)
//                {
//                    if (!borradoEjecutado)
//                    {
//                        borradoEjecutado = true;
//                    }
//                    throw new Exception(resultadoTimbrado.Message);
//                }

//                // 🔹 Ejecutar actualizaciones solo si el timbrado fue exitoso
//                if (resultadoTimbrado.Success && TempData["OperacionesAnticipos"] != null)
//                {
//                    var operaciones = JsonConvert.DeserializeObject<List<(int IdEncabezado, decimal NuevoSaldo, decimal MontoAplicado, int IdAnticipo, decimal SaldoAntes)>>(
//                        TempData["OperacionesAnticipos"].ToString()
//                    );

//                    foreach (var op in operaciones)
//                    {
//                        // 1️⃣ Actualizar saldo
//                        var parametersUpdate = new Dictionary<string, object>();
//                        string updateSaldo = @"
//                                                UPDATE factura 
//                                                SET saldo = @nuevoSaldo 
//                                                WHERE encabezado_id = @id_encabezado;";
//                        parametersUpdate.Add("nuevoSaldo", op.NuevoSaldo);
//                        parametersUpdate.Add("id_encabezado", op.IdEncabezado);
//                        RunUpdate(updateSaldo, parametersUpdate, false, conn, tx);

//                        // 2️⃣ Insertar relación en factura_anticipos
//                        var parametersInsert = new Dictionary<string, object>();
//                        string insertRelacion = @"
//                                                    INSERT INTO factura_anticipos
//                                                    (id_factura_principal, id_factura_anticipo, monto_aplicado, fecha_aplicacion, usuario_aplica, observaciones, saldo_antes, saldo_despues)
//                                                    VALUES (@idFacturaPrincipal, @idFacturaAnticipo, @montoAplicado, NOW(), @usuario, @observaciones, @saldoAntes, @saldoDespues);";

//                        parametersInsert.Add("idFacturaPrincipal", Convert.ToInt32(fc["enc_id"].ToString()));
//                        parametersInsert.Add("idFacturaAnticipo", op.IdAnticipo);
//                        parametersInsert.Add("montoAplicado", op.MontoAplicado);
//                        parametersInsert.Add("usuario", GetUserId(User.Identity.Name));
//                        parametersInsert.Add("observaciones", "Aplicación de anticipo sobre subtotal.");
//                        parametersInsert.Add("saldoAntes", op.SaldoAntes);
//                        parametersInsert.Add("saldoDespues", op.NuevoSaldo);
//                        RunQuery(insertRelacion, parametersInsert, false, conn, tx);

//                    }
//                }


//                var datosDetallados = new Dictionary<string, object>
//                {
//                    { "UUID", resultadoTimbrado.UUID },
//                    { "Total", factura.Total },
//                    { "Subtotal", factura.Subtotal },
//                    { "IVA", factura.IVA },
//                    { "RFCCliente", factura.RfcCliente },
//                    { "RazonSocialCliente", factura.RsoCliente },
//                    { "Fecha", factura.Fecha.ToString("dd/MM/yyyy HH:mm:ss") },
//                    { "Serie", factura.Serie },
//                    { "Folio", factura.Folio },
//                    { "EncabezadoId", factura.EncabezadoId },
//                    { "PdfUrl", Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf") },
//                    { "isEn", true },
//                    { "PdfUrlEN", Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}_EN.pdf") },
//                    { "XmlUrl",Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml")},
//                    { "CantidadProductos", factura.Tproductos.Rows.Count }
//                };

//                // Descontar material 
//                var movimientos = new List<Dictionary<string, object>>();
//                int userId = GetUserId(User.Identity.Name);
//                int idEncabezado = Convert.ToInt32(factura.EncabezadoId);

//                string queryTarima = "SELECT ct.id_tarima FROM catalmacenes c " +
//                                     "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
//                                     "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
//                                     "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
//                                     "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
//                                     "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
//                                     "INNER JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima " +
//                                     "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id " +
//                                     "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Temporal'";
//                parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
//                int destino = Convert.ToInt32(RunScalar(queryTarima, parameters));

//                foreach (var p in productos)
//                {
//                    string prodCve = p.ContainsKey("productoId") ? p["productoId"] : "";
//                    decimal cantidadSolicitada = p.ContainsKey("cantidad") ? GetDecimal(p["cantidad"]) : 0;
//                    string unidadStr = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA"; // 🔹 viene del JSON

//                    // 🔹 Obtener ID de unidad desde catunidades
//                    int idudm = 0;
//                    try
//                    {
//                        var paramUdm = new Dictionary<string, object> { { "cve_udm", unidadStr } };
//                        string queryUdm = "SELECT id_udm FROM catunidades WHERE cve_udm = @cve_udm;";
//                        idudm = Convert.ToInt32(RunScalar(queryUdm, paramUdm));
//                    }
//                    catch
//                    {
//                        Console.WriteLine($"⚠️ Unidad no encontrada para clave: {unidadStr}. Se asignará 0.");
//                    }

//                    // 🔹 Buscar datos del producto
//                    var paramProd = new Dictionary<string, object> { { "cve_prod", prodCve }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } };
//                    var prodInfo = RunQuery(@"
//                                    SELECT id_catproductos AS id_producto, cve_prod AS codigo, descr_prod AS descripcion
//                                    FROM catproductos
//                                    WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id", paramProd);

//                    if (prodInfo.Count == 0)
//                    {
//                        Console.WriteLine($"⚠️ Producto no encontrado: {prodCve}");
//                        continue;
//                    }

//                    var prodData = prodInfo[0];
//                    int idProducto = Convert.ToInt32(prodData["id_producto"]);
//                    string codigo = prodData["codigo"].ToString();
//                    string descripcion = prodData["descripcion"].ToString();

//                    // 🔹 Buscar tarimas con stock disponible
//                    decimal restante = cantidadSolicitada;

//                    if (restante <= 0)
//                        break;

//                    // 🔹 Construir producto para movimiento
//                    var prodMovimiento = new Dictionary<string, object>
//                        {
//                            { "id_producto", idProducto },
//                            { "codigo", codigo },
//                            { "descripcion", descripcion },
//                            { "cantidad", restante },
//                            { "unidad", idudm }, // 🔹 id real desde catunidades
//                            { "tarima_id", destino },
//                            { "tipo", "venta" },
//                            { "movimiento", "salida" }
//                        };

//                    movimientos.Add(prodMovimiento);

//                    // 🔹 Registrar movimiento de salida (por tarima)
//                    RegistrarMovimiento(
//                        new List<Dictionary<string, object>> { prodMovimiento },
//                        userId,
//                        "venta",
//                        destino,
//                        null,
//                        "salida",
//                        idEncabezado,
//                        conn,
//                        tx
//                    );
//                }

//                return (true, resultadoTimbrado.Message, datosDetallados);
//            }
//            catch (Exception ex)
//            {
//                if (!borradoEjecutado)
//                {
//                    borradoEjecutado = true;
//                }

//                return (false, "Error al generar la factura: " + ex.Message, null);
//            }
//        }
        
        
        
//        [Route("VINFacturaEspecial/ProcesarDocumentosAsync")]
//        public async Task<JsonResult> ProcesarDocumentosAsync(IFormCollection fc)
//        {
//            (bool Success, string Message, object Data) resultado1 = (false, string.Empty, null);
//            var datosFactura = new Dictionary<string, object>();
//            var parameters = new Dictionary<string, object>();
//            var utils = new Utilities(true);
//            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
//            var factura = new Factura();
//            int? cantidadDocumentos = 0;
//            using (var conn = new NpgsqlConnection(connStr))
//            {
//                conn.Open();

//                using (var tx = conn.BeginTransaction())
//                {
//                    try
//                    {
//                        string productosJson = fc["productosJSON"].ToString();
//                        var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);
//                        string idsDocumentos = fc["documentid"].ToString();

//                        if (!string.IsNullOrWhiteSpace(idsDocumentos))
//                        {   //return Json(new { success = false, message = "No se recibieron IDs de documentos." });

//                            // Contador de documentos procesados
//                            cantidadDocumentos = idsDocumentos.Split(',').Length;
//                        }
//                        try
//                        {
//                            // 1️⃣ Guardar desde documentos base
//                            resultado1 = await Guardar(fc, conn, tx);
//                            if (!resultado1.Success)
//                                return Json(new { success = false, step = "GuardarFacturaDesdeDocs", error = resultado1.Message });

//                            dynamic data = resultado1.Data;
//                            int idEncabezado = data.IdEncabezado;

//                            var formData = fc.ToDictionary(x => x.Key, x => x.Value);
//                            formData["enc_id"] = idEncabezado.ToString();
//                            fc = new FormCollection(formData);

//                            // 2️⃣ Crear remisiones desde pedidos
//                            var resultado2 = await GenerarFacturaVentas(fc, factura, conn, tx);
//                            if (!resultado2.Success)
//                                return Json(new { success = false, step = "GenerarFacturaDesdeDocs", error = resultado2.Message });

//                            if (!string.IsNullOrWhiteSpace(idsDocumentos))
//                            {
//                                // Actualizar encabezados hijo
//                                var idsParam = string.Join(",", idsDocumentos);
//                                string updateHijos = $@"UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado IN ({idsParam});";
//                                RunUpdate(updateHijos, parameters, false, conn, tx);
//                            }

//                            // ✅ ÉXITO - Consolidar datos detallados
//                            datosFactura = resultado2.Data as Dictionary<string, object>;
//                            tx.Commit();
//                        }

//                        catch
//                        {
//                            tx.Rollback();
//                            throw;
//                        }
//                        GuardarFactura(factura);
//                        GenerarFactura(factura);
//                        // RETORNO COMPLETO Y DETALLADO
//                        return Json(new
//                        {
//                            success = true,
//                            message = "Todos los documentos fueron procesados y timbrados correctamente",
//                            resumen = new
//                            {
//                                documentosProcesados = (cantidadDocumentos ?? 0) == 0 ? 1 : cantidadDocumentos.Value,
//                                tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
//                            },
//                            detalles = new
//                            {
//                                pedidoId = resultado1.Data,
//                                // Datos de facturación
//                                uuid = datosFactura?["UUID"],
//                                total = datosFactura?["Total"],
//                                subtotal = datosFactura?["Subtotal"],
//                                iva = datosFactura?["IVA"],

//                                // Datos del cliente
//                                rfcCliente = datosFactura?["RFCCliente"],
//                                razonSocial = datosFactura?["RazonSocialCliente"],

//                                // Datos del comprobante
//                                serie = datosFactura?["Serie"],
//                                folio = datosFactura?["Folio"],
//                                fecha = datosFactura?["Fecha"],
//                                cantidadProductos = datosFactura?["CantidadProductos"],

//                                // URLs de descarga
//                                pdfUrl = datosFactura?["PdfUrl"],
//                                isEn = datosFactura?["isEn"],
//                                pdfUrlEN = datosFactura?["PdfUrlEN"],
//                                xmlUrl = datosFactura?["XmlUrl"]
//                            }
//                        });
//                    }

//                    catch (Exception ex)
//                    {
//                        return Json(new
//                        {
//                            success = false,
//                            message = "Error general en el proceso: " + ex.Message,
//                            detalles = ex.StackTrace
//                        });
//                    }
//                }

//            }
//        }
//    }
//}