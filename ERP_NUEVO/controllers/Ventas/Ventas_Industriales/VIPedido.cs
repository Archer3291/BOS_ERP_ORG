using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using System.Configuration;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    public class VIPedidoController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;

        public VIPedidoController(BOS_ERP.Services.EmailSender emailSenderService, BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Creacion de pedido")]
        public JsonResult Guardar(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string tipo = fc["tipo"].ToString()?.ToLower() ?? ""; // contado, credito, anticipo

                // 🔹 Validaciones previas
                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un cliente." });

                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una moneda." });

                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un vendedor." });

                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una forma de pago." });

                // 🔹 Validar productos solo si NO es anticipo
                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
                if (tipo != "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });

                    productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                    if (productos == null || productos.Count == 0)
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });
                }

                // 🔹 Validar precios y descuentos contra las reglas de precio
                // El front ya bloquea los campos y manda los tokens, pero hasta ahora el
                // servidor los ignoraba: capturar el pedido directo (sin cotización) saltaba
                // la regla por completo.
                TokenStore.LimpiarExpirados();
                string usuarioReglas = User.Identity.Name;
                string descuentoToken = fc["descuentoToken"].ToString() ?? "";
                string precioToken = fc["precioToken"].ToString() ?? "";
                int empresaReglas = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                var reglas = this.ValidarPartidas(
                    empresaReglas,
                    this.ResolverClienteId(fc["cliente"].ToString(), empresaReglas),
                    productos,
                    TokenStore.Validar(precioToken, usuarioReglas, "CAMBIO DE PRECIO"),
                    TokenStore.Validar(descuentoToken, usuarioReglas, "DESCUENTO"));

                if (!reglas.Permitido)
                    return Json(new { success = false, message = reglas.Mensaje });

                // Un solo uso, igual que en la cotización: el front recarga tras guardar y
                // vuelve a pedir autorización.
                TokenStore.Invalidar(descuentoToken);
                TokenStore.Invalidar(precioToken);

                // 🔹 Validar fecha de pago solo si es crédito
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaPago"].ToString()))
                        return Json(new { success = false, message = "Debe ingresar la fecha de pago (solo para crédito)." });

                    if (!DateTime.TryParse(fc["fechaPago"].ToString(), out DateTime fch))
                        return Json(new { success = false, message = "Formato de fecha de pago no válido." });

                    fechaPago = fch;
                }
                else if (tipo == "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaAnticipo"].ToString()))
                        return Json(new { success = false, message = "Debe ingresar la fecha del anticipo." });
                    if (!DateTime.TryParse(fc["fechaAnticipo"].ToString(), out DateTime fch))
                        return Json(new { success = false, message = "Formato de fecha del anticipo no válido." });
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
                    return Json(new { success = false, message = credito.Mensaje });

                int idEncabezadoPadre = GetUserId(User.Identity.Name);
                int usrId0 = 0;
                DateTime usrFch0 = DateTime.Now;

                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                {
                    // 🔹 Cargar datos del encabezado existente
                    var usrParameter = new Dictionary<string, object>();
                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3, " +
                        "em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto " +
                        "FROM encabezadomov em " +
                        "WHERE em.id_encabezado = @id";

                    usrParameter.Add("id", Convert.ToInt32(fc["documentid"].ToString()));

                    var result = RunQuery(usrquery, usrParameter);

                    if (result.Count > 0)
                    {
                        var usrId = result[0];
                        usrId0 = Convert.ToInt32(usrId["usr0"]);
                        usrFch0 = Convert.ToDateTime(usrId["fch0"]);
                        idEncabezadoPadre = Convert.ToInt32(fc["documentid"].ToString());
                    }
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
                    var clienteResult = RunQuery(clientequery, clienteParameter);
                    if (clienteResult.Count > 0)
                    {
                        var cliente = clienteResult[0];
                        idCliente = Convert.ToInt32(cliente["id_cliente"]);
                        clienteNombre = cliente["n_cli"].ToString();

                    }
                }
                var fPagoParameter = new Dictionary<string, object>();
                fPagoParameter.Add("cve_sat", fc["forma-pago"].ToString());
                // 🔹 Crear encabezado
                var encabezado = new DocumentoEncabezado();

                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 13;
                encabezado.IdTpDoc = 46;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Alm = fc["almacen"].ToString();
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "VIPED";
                encabezado.ComentAut = fc["comentarios"].ToString();
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = usrId0;
                encabezado.Fch0 = usrFch0;
                encabezado.Usr1 = GetUserId(User.Identity.Name);
                encabezado.Fch1 = DateTime.Now;
                encabezado.Imp = Convert.ToDecimal(fc["total"].ToString());
                encabezado.Dto = Convert.ToDecimal(fc["descuento"].ToString());
                encabezado.CliProv = fc["cliente"].ToString();
                encabezado.Ccy = fc["moneda"].ToString();
                encabezado.Estatus = 1;
                encabezado.Ref = idCliente;
                encabezado.Flete = Convert.ToDecimal(fc["flete"].ToString());
                encabezado.VdrCpr = fc["vendedor"].ToString();
                encabezado.Coment1 = fc["concepto"].ToString();
                encabezado.EncabezadoPadre = idEncabezadoPadre;
                encabezado.PlDias = string.IsNullOrWhiteSpace(fc["plazo"].ToString()) ? 0 : Convert.ToInt32(fc["plazo"].ToString());
                encabezado.FchPgEntrega = fechaPago ?? DateTime.Now;
                encabezado.Par = Convert.ToDecimal(fc["paridad"].ToString());
                encabezado.FPago = Convert.ToInt32(RunScalar("select id_f_pago from cat_f_pago where cve_sat = @cve_sat", fPagoParameter));
                encabezado.Mdp = fc["metodo-pago"].ToString();
                encabezado.TipoPoceso = "pedido_" + tipo;
                encabezado.CFDI = fc["uso-cfdi"].ToString();
                encabezado.NatDocPadreChar = fc["ordenCompra"].ToString();


                // 🔹 Crear partidas (solo si hay productos)
                var partidas = new List<PartidaDocumento>();
                if (productos.Count > 0)
                {
                    foreach (var p in productos)
                    {
                        var parametersP = new Dictionary<string, object>();
                        string queryId = "select id_catproductos from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id ";
                        parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
                        parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        int productId = Convert.ToInt32(RunScalar(queryId, parametersP));

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
                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                int idEncNormal = Convert.ToInt32(folio["IdEncabezado"]);
                string folioNormal = folio["folio_generado"].ToString();

                // 🔹 Registrar la relación del documento generado (VI solo genera VIPED, sin split)
                InsertarDocumentosRelacionados(idEncabezadoPadre, idEncNormal, null, null, folioNormal, null, null);

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = idEncNormal,
                    Folio = folioNormal
                };

                string query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                parameters.Add("id", idEncabezadoPadre);
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Documento creado exitosamente.", folio_generado = folioNormal });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VIPedido/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 🔹 Inserta el registro de relación entre los documentos generados (normal, tubo, modula).
        //    Acepta conn/tx para poder participar de la transacción de la aprobación de crédito.
        private void InsertarDocumentosRelacionados(
            int idEncabezadoPadre, int? idEncNormal, int? idEncTubo, int? idEncModula,
            string folioNormal, string folioTubo, string folioModula,
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            string tipoRelacion = CalcularTipoRelacion(idEncNormal, idEncTubo, idEncModula);

            var parametersRel = new Dictionary<string, object>();
            string insertQuery = @"
        INSERT INTO documentos_relacionados
            (id_encabezado_padre, id_encabezado_normal, id_encabezado_tubo, id_encabezado_modula,
             folio_normal, folio_tubo, folio_modula, tipo_relacion, empresa_id, usr_creacion, fecha_creacion)
        VALUES
            (@id_encabezado_padre, @id_encabezado_normal, @id_encabezado_tubo, @id_encabezado_modula,
             @folio_normal, @folio_tubo, @folio_modula, @tipo_relacion, @empresa_id, @usr_creacion, now())";

            parametersRel.Add("tipo_relacion", tipoRelacion);
            parametersRel.Add("id_encabezado_padre", idEncabezadoPadre);
            parametersRel.Add("id_encabezado_normal", (object)idEncNormal ?? DBNull.Value);
            parametersRel.Add("id_encabezado_tubo", (object)idEncTubo ?? DBNull.Value);
            parametersRel.Add("id_encabezado_modula", (object)idEncModula ?? DBNull.Value);
            parametersRel.Add("folio_normal", (object)folioNormal ?? DBNull.Value);
            parametersRel.Add("folio_tubo", (object)folioTubo ?? DBNull.Value);
            parametersRel.Add("folio_modula", (object)folioModula ?? DBNull.Value);
            parametersRel.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parametersRel.Add("usr_creacion", GetUserId(User.Identity.Name));

            RunUpdate(insertQuery, parametersRel, false, conn, tx);
        }

        private string CalcularTipoRelacion(int? idEncNormal, int? idEncTubo, int? idEncModula)
        {
            var partes = new List<string>();
            if (idEncNormal.HasValue) partes.Add("normal");
            if (idEncTubo.HasValue) partes.Add("tubo");
            if (idEncModula.HasValue) partes.Add("modula");
            return "pedido_split_" + string.Join("_", partes);
        }
        // Agregar en VIPedidoController.cs (o donde tengas el controlador del pedido)
        // Requiere que tu proyecto ya tenga configurado System.Net.Mail o un servicio de correo.

        [HttpPost]
        public async Task<ActionResult> EnviarSolicitudGerente(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string gerenteEmail = fc["gerenteEmail"].ToString() ?? "";
                string folio = fc["folio"].ToString() ?? "";
                string cliente = fc["cliente"].ToString() ?? "";
                string limiteStr = fc["limiteCredito"].ToString() ?? "0";
                string usadoStr = fc["creditoUsado"].ToString() ?? "0";
                string dispStr = fc["creditoDisp"].ToString() ?? "0";
                string totalStr = fc["totalPedido"].ToString() ?? "0";
                string productosJSON = fc["productosJSON"].ToString() ?? "[]";

                // El mismo flujo de autorización lo usan cotización, pedido, remisión y
                // factura; "origen" dice desde cuál se pidió. Vacío = pedido (compatibilidad).
                string origen = fc["origen"].ToString();
                if (string.IsNullOrWhiteSpace(origen)) origen = "pedido";

                int pedidoId = int.TryParse(fc["pedido"].ToString(), out var pid) ? pid : 0;
                parameters.Add("cve_cli", cliente);
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                int clienteId = int.TryParse(
                    RunScalar("SELECT id_cliente FROM catclientes WHERE cve_cli = @cve_cli AND empresa_id = @empresa_id", parameters).ToString(),
                    out var cid) ? cid : 0;

                if (string.IsNullOrWhiteSpace(gerenteEmail))
                    return Json(new { success = false, message = "Correo del gerente requerido." });

                decimal limite = decimal.TryParse(limiteStr, out var l) ? l : 0;
                decimal usado = decimal.TryParse(usadoStr, out var u) ? u : 0;
                decimal disp = decimal.TryParse(dispStr, out var d) ? d : 0;
                decimal total = decimal.TryParse(totalStr, out var t) ? t : 0;

                string usuarioSolicitante = User.Identity.Name;
                int usuarioId = Convert.ToInt32(HttpContext.Session.GetInt32("UsuarioId"));

                decimal excedente = 0;
                if (limite > 0)
                {
                    decimal post = usado + total;
                    if (post > limite) excedente = post - limite;
                }

                // 🔹 Generar token único
                string token = Guid.NewGuid().ToString("N"); // 32 chars hex, sin guiones

                // 🔹 Guardar solicitud (con token y con la configuración capturada en el
                //    formulario, que aún no está en ningún documento)
                InsertarSolicitudCredito(clienteId, pedidoId, limite, usado, total, excedente,
                                         usuarioId, token, fc["configuracionJSON"].ToString());

                // 🔹 Cambiar estatus del pedido. Solo desde el pedido: en remisión o factura
                //    el documento aún no existe (o ya está timbrado) y no debe tocarse.
                if (origen == "pedido" && pedidoId > 0)
                    MarcarPedidoPendiente(pedidoId);

                // 🔹 Construir URL para el correo
                //string baseUrl = $"{Request.Url.Scheme}://{Request.Url.Authority}";
                //string urlAutorizacion = $"{baseUrl}/VIPedido/Autorizar?token={token}";
                string baseUrl = $"{Request.Scheme}://{Request.Host}";
                string urlAutorizacion = $"{baseUrl}/VIPedido/Autorizar?token={token}";
                // 🔹 Convertir productos
                var productosList = new List<ProductoEmail>();
                try
                {
                    var productos = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(productosJSON);

                    foreach (var p in productos ?? new List<Dictionary<string, object>>())
                    {
                        productosList.Add(new ProductoEmail
                        {
                            Descripcion = p.ContainsKey("descripcion") ? p["descripcion"]?.ToString() : "-",
                            Cantidad = p.ContainsKey("cantidad") ? p["cantidad"]?.ToString() : "0",
                            Precio = p.ContainsKey("precio") ? p["precio"]?.ToString() : "0",
                            Importe = decimal.TryParse(p["importe"]?.ToString(), out var imp) ? imp : 0
                        });
                    }
                }
                catch { }

                // 🔹 Modelo — ahora incluye Token y UrlAutorizacion
                var emailData = new SolicitudCreditoEmailModel
                {
                    UsuarioSolicitante = usuarioSolicitante,
                    Cliente = cliente,
                    Folio = folio,
                    Limite = limite,
                    Usado = usado,
                    Disponible = disp,
                    Total = total,
                    Productos = productosList,
                    Fecha = DateTime.Now,
                    Token = token,           // 🔹 nuevo
                    UrlAutorizacion = urlAutorizacion  // 🔹 nuevo
                };

                string htmlBody = await emailSender.RenderViewToStringAsync(
                   "~/Views/Email/_SolicitudCredito.cshtml",
                    emailData
                );


                string etiquetaDoc = origen switch
                {
                    "cotizacion" => "Cotización",
                    "remision" => "Remisión",
                    "factura" => "Factura",
                    _ => "Pedido"
                };

                await correoHelper.EnviarCorreoNotificacionAsync(
                    gerenteEmail,
                    $"[Autorización requerida] {etiquetaDoc} {folio}",
                    htmlBody
                );

                // El token vuelve al front: si el documento aún no existe, es lo único que
                // permite reconocer la autorización cuando el gerente la apruebe.
                return Json(new { success = true, token });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VIPedido/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private void InsertarSolicitudCredito(
    int clienteId, int pedidoId, decimal limite,
    decimal usado, decimal total, decimal excedente,
    int usuarioId, string token, string configuracion = null)
        {
            // La forma de pago y el uso de CFDI se capturan hasta el pedido, y ese pedido
            // todavía no existe cuando se pide la autorización: se conservan aquí para que
            // AprobarPedido los aplique al generarlo (la cotización no los conoce).
            bool hayConfig = !string.IsNullOrWhiteSpace(configuracion);
            bool existeColumna = ExisteColumnaConfiguracionSolicitud();
            bool guardaConfig = hayConfig && existeColumna;

            if (hayConfig && !existeColumna)
            {
                LogErrorHelper.RegistrarLog("VIPedidoController", pedidoId.ToString(),
                    "Falta la columna autorizaciones_credito.configuracion_documento " +
                    "(ver sql/autorizaciones_credito_configuracion.sql): el pedido autorizado " +
                    "se generará sin la forma de pago ni el uso de CFDI capturados.",
                    nivel: "WARN");
            }

            string columnaConfig = guardaConfig ? ", configuracion_documento" : "";
            string valorConfig = guardaConfig ? ", @configuracion" : "";

            string query = $@"
        INSERT INTO autorizaciones_credito
            (cliente_id, pedido_id, credito_limite, credito_usado,
             monto_pedido, excedente, solicitado_por, token, estatus{columnaConfig})
        VALUES
            (@cliente_id, @pedido_id, @limite, @usado,
             @total, @excedente, @usuario, @token, 'pendiente'{valorConfig})";

            var param = new Dictionary<string, object>
            {
                { "cliente_id", clienteId },
                { "pedido_id",  pedidoId  },
                { "limite",     limite    },
                { "usado",      usado     },
                { "total",      total     },
                { "excedente",  excedente },
                { "usuario",    usuarioId },
                { "token",      token     }
            };

            if (guardaConfig) param.Add("configuracion", configuracion);

            RunQuery(query, param);
        }

        // Configuración que se guardó al pedir la autorización, para reconstruir el pedido
        // tal como lo capturó el vendedor. Devuelve null si no hay (o si aún no existe la
        // columna), y entonces se usan los valores del documento padre.
        private Dictionary<string, object> ObtenerConfiguracionSolicitud(
            int documentoId, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            if (documentoId <= 0 || !ExisteColumnaConfiguracionSolicitud()) return null;

            try
            {
                var filas = RunQuery(@"
                    SELECT configuracion_documento
                    FROM   autorizaciones_credito
                    WHERE  pedido_id = @pid
                      AND  configuracion_documento IS NOT NULL
                    ORDER BY id DESC
                    LIMIT 1",
                    new Dictionary<string, object> { { "pid", documentoId } }, false, conn, tx);

                if (filas == null || filas.Count == 0) return null;

                string json = filas[0]["configuracion_documento"]?.ToString();
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VIPedido/ObtenerConfiguracionSolicitud");
                LogErrorHelper.RegistrarLog("VIPedidoController", documentoId.ToString(),
                    $"No se pudo leer la configuración de la solicitud: {ex.Message}", nivel: "WARN");
                return null;
            }
        }

        private static DateTime FechaPagoPedido(string fechaConfig, object fechaPadre)
        {
            if (!string.IsNullOrWhiteSpace(fechaConfig)
                && DateTime.TryParse(fechaConfig, out DateTime fch))
                return fch;

            return fechaPadre != null && fechaPadre != DBNull.Value
                        ? Convert.ToDateTime(fechaPadre)
                        : DateTime.Now;
        }

        private static decimal ParidadPedido(string paridadConfig, object paridadPadre)
        {
            if (!string.IsNullOrWhiteSpace(paridadConfig)
                && decimal.TryParse(paridadConfig, out decimal par) && par > 0)
                return par;

            return paridadPadre != null && paridadPadre != DBNull.Value
                        ? Convert.ToDecimal(paridadPadre)
                        : 1m;
        }

        // La columna es opcional: si aún no se ha corrido el ALTER TABLE, el flujo sigue
        // funcionando como antes (el pedido hereda solo lo que tenga el documento padre).
        private bool ExisteColumnaConfiguracionSolicitud()
        {
            try
            {
                object r = RunScalar(@"
                    SELECT COUNT(*)
                    FROM   information_schema.columns
                    WHERE  table_name  = 'autorizaciones_credito'
                      AND  column_name = 'configuracion_documento'",
                    new Dictionary<string, object>());

                return r != null && Convert.ToInt32(r) > 0;
            }
            catch { return false; }
        }

        private void MarcarPedidoPendiente(int pedidoId)
        {
            string query = @"
                            UPDATE encabezadomov
                            SET estatus_id = 40
                            WHERE id_encabezado = @id";

            RunQuery(query, new Dictionary<string, object> { { "id", pedidoId } });
        }

        // Genera el pedido a partir del documento autorizado, replicando lo que hace Guardar.
        [HttpPost]
        public IActionResult AprobarPedido(int idpedido, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var parameters = new Dictionary<string, object>();

            string docquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3,  " +
                                      "       em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto, coment_aut, " +
                                      "       imp, dto, cli_prov, ccy, refe, flete, vdr_cpr, coment1, pl_dias, fch_pg_entrega, par, f_pago, mdp, tipo_proceso, cfdi, orden_compra, " +
                                      "       em.alm " +
                                      "FROM encabezadomov em " +
                                      "WHERE em.id_encabezado = @id";
            parameters.Add("id", idpedido);
            var doc = RunQuery(docquery, parameters, false, conn, tx);

            if (doc.Count == 0)
                throw new Exception($"No se encontró el documento {idpedido} para generar el pedido autorizado.");

            int plDias = 0;

            if (doc.Count > 0 && doc[0].ContainsKey("pl_dias") && doc[0]["pl_dias"] != null && doc[0]["pl_dias"] != DBNull.Value)
            {
                int.TryParse(doc[0]["pl_dias"].ToString(), out plDias);
            }

            string mdp = "";

            if (doc.Count > 0 && doc[0].ContainsKey("mdp") && doc[0]["mdp"] != null && doc[0]["mdp"] != DBNull.Value)
            {
                mdp = doc[0]["mdp"].ToString();
            }

            string cfdi = "";

            if (doc.Count > 0 && doc[0].ContainsKey("cfdi") && doc[0]["cfdi"] != null && doc[0]["cfdi"] != DBNull.Value)
            {
                cfdi = doc[0]["cfdi"].ToString();
            }

            // Configuración capturada en la pantalla de pedido al pedir la autorización.
            // La cotización no la tiene (forma de pago y uso de CFDI se definen hasta el
            // pedido), así que sin esto el pedido autorizado nacería sin esos datos.
            var config = ObtenerConfiguracionSolicitud(idpedido, conn, tx);

            string ValorConfig(string clave) =>
                config != null && config.ContainsKey(clave) ? config[clave]?.ToString() : null;

            string cfdiConfig = ValorConfig("usoCfdi");
            if (!string.IsNullOrWhiteSpace(cfdiConfig)) cfdi = cfdiConfig;

            string mdpConfig = ValorConfig("metodoPago");
            if (!string.IsNullOrWhiteSpace(mdpConfig)) mdp = mdpConfig;

            string plazoConfig = ValorConfig("plazo");
            if (!string.IsNullOrWhiteSpace(plazoConfig) && int.TryParse(plazoConfig, out int plCfg))
                plDias = plCfg;

            // Forma de pago: el formulario manda la clave SAT, el encabezado guarda el id.
            int? fPagoPedido = doc[0]["f_pago"] != null && doc[0]["f_pago"] != DBNull.Value
                                    ? Convert.ToInt32(doc[0]["f_pago"])
                                    : (int?)null;

            string formaPagoConfig = ValorConfig("formaPago");
            if (!string.IsNullOrWhiteSpace(formaPagoConfig))
            {
                object idFPago = RunScalar(
                    "SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat",
                    new Dictionary<string, object> { { "cve_sat", formaPagoConfig } }, false, conn, tx);

                if (idFPago != null && idFPago != DBNull.Value)
                    fPagoPedido = Convert.ToInt32(idFPago);
            }

            // El documento nace como pedido, así que marca su propio tipo_proceso con la
            // misma convención que Guardar ("pedido_credito" / "pedido_contado"): es de
            // donde la remisión y la factura leen la condición de pago.
            string tipoProceso = mdp.Trim().ToUpper() == "PPD" ? "pedido_credito" : "pedido_contado";

            var encabezado = new DocumentoEncabezado();
            encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
            encabezado.IdArea = 13;
            encabezado.IdTpDoc = 46;
            encabezado.UsrDep = GetAreaName(User.Identity.Name);
            encabezado.Anio = DateTime.Now.Year;
            encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            encabezado.Alm = doc[0]["alm"]?.ToString();
            encabezado.Fch = DateTime.Now;
            encabezado.TpMov = "VIPED";
            encabezado.ComentAut = doc[0]["coment_aut"].ToString();
            encabezado.UsrDoc = User.Identity.Name;
            encabezado.FchCap = DateTime.Now;
            encabezado.Usr0 = Convert.ToInt32(doc[0]["usr0"]);
            encabezado.Fch0 = Convert.ToDateTime(doc[0]["fch0"]);
            encabezado.Usr1 = GetUserId(User.Identity.Name);
            encabezado.Fch1 = DateTime.Now;
            encabezado.Imp = Convert.ToDecimal(doc[0]["imp"]);
            encabezado.Dto = Convert.ToDecimal(doc[0]["dto"]);
            encabezado.CliProv = doc[0]["cli_prov"].ToString();
            encabezado.Ccy = ValorConfig("moneda") ?? doc[0]["ccy"].ToString();
            encabezado.Estatus = 1;
            encabezado.Ref = Convert.ToInt32(doc[0]["refe"]);
            encabezado.Flete = Convert.ToInt32(doc[0]["flete"]);
            encabezado.VdrCpr = ValorConfig("vendedor") ?? doc[0]["vdr_cpr"].ToString();
            encabezado.Coment1 = ValorConfig("concepto") ?? doc[0]["coment1"].ToString();
            encabezado.EncabezadoPadre = idpedido;
            encabezado.PlDias = plDias;
            encabezado.FchPgEntrega = FechaPagoPedido(ValorConfig("fechaPago"), doc[0]["fch_pg_entrega"]);
            encabezado.Par = ParidadPedido(ValorConfig("paridad"), doc[0]["par"]);
            encabezado.FPago = fPagoPedido;
            encabezado.Mdp = mdp;
            encabezado.TipoPoceso = tipoProceso;
            encabezado.CFDI = cfdi;
            encabezado.NatDocPadreChar = ValorConfig("ordenCompra") ?? doc[0]["orden_compra"].ToString();

            string comentariosConfig = ValorConfig("comentarios");
            if (!string.IsNullOrWhiteSpace(comentariosConfig))
                encabezado.ComentAut = comentariosConfig;


            var partidas = new List<PartidaDocumento>();

            string partidasQuery = "SELECT p.cve_prod, p.descr_prod, p.cant_ud, p.pv_prod, p.dto1, p.ud, p.tp_doc_ant, c.id_catproductos " +
                                        "FROM partidasdoc p LEFT JOIN catproductos c ON c.cve_prod = p.cve_prod AND c.empresa_id = @empresa_id WHERE p.encabezado_id = @id";
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var productos = RunQuery(partidasQuery, parameters, false, conn, tx);

            if (productos.Count > 0)
            {
                foreach (var p in productos)
                {
                    var parametersP = new Dictionary<string, object>();
                    string queryId = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id";

                    parametersP.Add("cve_prod", p["cve_prod"]);
                    parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                    int productId = Convert.ToInt32(RunScalar(queryId, parametersP, false, conn, tx) ?? 0);

                    decimal cantidad = p["cant_ud"] != null ? Convert.ToDecimal(p["cant_ud"]) : 0;
                    decimal precio = p["pv_prod"] != null ? Convert.ToDecimal(p["pv_prod"]) : 0;
                    decimal descuento = p["dto1"] != null ? Convert.ToDecimal(p["dto1"]) : 0;

                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = p["cve_prod"]?.ToString() ?? "",
                        DescrProd = p["descr_prod"]?.ToString() ?? "",
                        CantUd = cantidad,
                        PvProd = precio,
                        Dto1 = descuento,
                        ImpPart = cantidad * precio,
                        Ud = p["ud"]?.ToString() ?? "PZA",
                        IdProducto = productId,
                        TpDocAnt = p["tp_doc_ant"]?.ToString() ?? ""
                    });
                }
            }

            // 🔹 Guardar documento
            var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

            int idEncNormal = Convert.ToInt32(folio["IdEncabezado"]);
            string folioNormal = folio["folio_generado"].ToString();

            // 🔹 Registrar la relación, igual que en Guardar. Sin esta fila el pedido existe
            //    pero no aparece en la verificación de existencias (BuscarPedidosParaVerificar
            //    consulta documentos_relacionados, no encabezadomov).
            InsertarDocumentosRelacionados(
                idpedido, idEncNormal, null, null, folioNormal, null, null, conn, tx);

            LogErrorHelper.RegistrarLog("VIPedidoController", idEncNormal.ToString(),
                $"Pedido {folioNormal} generado por autorización de crédito del documento {idpedido}.",
                nivel: "INFO");

            return Json(new { success = true, folio_generado = folioNormal, id_encabezado = idEncNormal });
        }



        // ─── Aprobar / Rechazar internos (fetch) ────────────────────────────────────

        [HttpPost]
        public IActionResult Aprobar(int pedidoId)
        {
            try
            {
                int usuarioId = Convert.ToInt32(HttpContext.Session.GetInt32("UsuarioId"));
                ProcesarResolucion(pedidoId, "aprobado", 11, usuarioId);
                return Json(new { success = true, message = "Pedido aprobado correctamente." });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public IActionResult Rechazar(int pedidoId)
        {
            try
            {
                int usuarioId = Convert.ToInt32(HttpContext.Session.GetInt32("UsuarioId"));
                ProcesarResolucion(pedidoId, "rechazado", 10, usuarioId);
                return Json(new { success = true, message = "Pedido rechazado." });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // ─── Vista pública por token (link del correo) ───────────────────────────────

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Autorizar(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return Content("Token inválido.");

            var sol = ObtenerSolicitudPorToken(token);
            if (sol == null) return Content("Solicitud no encontrada o ya fue procesada.");

            return View(sol); // ~/Views/Autorizacion/Autorizar.cshtml
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult ConfirmarAutorizacion(string token, string accion)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    return Json(new { success = false, message = "Token inválido." });

                var sol = ObtenerSolicitudPorToken(token);
                if (sol == null)
                    return Json(new { success = false, message = "Solicitud no encontrada o ya procesada." });

                // 11 = autorizado/cerrado · 10 = rechazado (mismos códigos que Aprobar/Rechazar)
                int estatusId = accion == "aprobar" ? 11 : 10;
                string estatus = accion == "aprobar" ? "aprobado" : "rechazado";

                ProcesarResolucionPorToken(sol.PedidoId, estatus, estatusId, token);

                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // ─── Helpers privados ────────────────────────────────────────────────────────

        private void ProcesarResolucion(int pedidoId, string estatus, int estatusId, int usuarioId)
        {
            // Este camino resuelve por pedido_id. Con 0 (documento aún no guardado) el UPDATE
            // alcanzaría a todas las solicitudes sueltas: esas se resuelven por token desde el
            // link del correo, nunca desde aquí.
            if (pedidoId <= 0)
                throw new Exception(
                    "La solicitud no está ligada a un documento; debe autorizarse desde el " +
                    "enlace del correo.");

            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        RunQuery("UPDATE encabezadomov SET estatus_id = @est WHERE id_encabezado = @id", new Dictionary<string, object> { { "est", estatusId }, { "id", pedidoId } }, false, conn, tx);
                        RunQuery(@"
        UPDATE autorizaciones_credito
        SET estatus = @estatus, autorizado_por = @usr, fecha_resolucion = NOW()
        WHERE pedido_id = @pid AND estatus = 'pendiente'", new Dictionary<string, object> { { "estatus", estatus }, { "usr", usuarioId }, { "pid", pedidoId } }, false, conn, tx);

                        // El pedido solo se genera si el gerente autorizó y hay un documento
                        // de origen del cual reconstruirlo. Cuando se capturó desde cero
                        // (sin cotización) no hay nada que replicar: la autorización queda
                        // registrada y el vendedor guarda el pedido por el flujo normal.
                        if (estatus == "aprobado" && pedidoId > 0)
                            AprobarPedido(pedidoId, conn, tx);

                        tx.Commit();
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }
        private void ProcesarResolucionPorToken(int pedidoId, string estatus, int estatusId, string token)
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

                        RunQuery(
                "UPDATE encabezadomov SET estatus_id = @est WHERE id_encabezado = @id",
                new Dictionary<string, object> { { "est", estatusId }, { "id", pedidoId } }, false, conn, tx
            );
                        RunQuery(@"
        UPDATE autorizaciones_credito
        SET estatus = @estatus, fecha_resolucion = NOW()
        WHERE token = @token AND estatus = 'pendiente'",
                            new Dictionary<string, object>
                            {
                                { "estatus", estatus },
                                { "token",   token   }
                            }, false, conn, tx);

                        // El pedido solo se genera si el gerente autorizó y hay un documento
                        // de origen del cual reconstruirlo. Cuando se capturó desde cero
                        // (sin cotización) no hay nada que replicar: la autorización queda
                        // registrada y el vendedor guarda el pedido por el flujo normal.
                        if (estatus == "aprobado" && pedidoId > 0)
                            AprobarPedido(pedidoId, conn, tx);

                        tx.Commit();
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        private SolicitudCreditoVM ObtenerSolicitudPorToken(string token)
        {
            string query = @"
        SELECT  ac.id, ac.pedido_id, ac.cliente_id,
                cc.n_cli,
                COALESCE(em.folio, '') AS folio,
                ac.credito_limite, ac.credito_usado,
                ac.monto_pedido,   ac.excedente,
                ac.fecha_solicitud, ac.solicitado_por, ac.token
        FROM    autorizaciones_credito ac
        JOIN    catclientes       cc ON cc.id_cliente    = ac.cliente_id
        -- LEFT: un pedido capturado desde cero no tiene documento de origen todavía,
        -- y con INNER la solicitud resultaba invisible desde el link del correo.
        LEFT JOIN encabezadomov em ON em.id_encabezado = ac.pedido_id
        WHERE   ac.token   = @token
          AND   ac.estatus = 'pendiente'
        LIMIT 1";

            var dt = RunQuery(query, new Dictionary<string, object> { { "token", token } });
            if (dt == null || dt.Count == 0)
                return null;

            var row = dt[0];
            return new SolicitudCreditoVM
            {
                Id = Convert.ToInt32(row["id"]),
                PedidoId = Convert.ToInt32(row["pedido_id"]),
                ClienteId = Convert.ToInt32(row["cliente_id"]),
                NombreCliente = row["n_cli"].ToString(),
                Folio = row["folio"].ToString(),
                CreditoLimite = Convert.ToDecimal(row["credito_limite"]),
                CreditoUsado = Convert.ToDecimal(row["credito_usado"]),
                MontoPedido = Convert.ToDecimal(row["monto_pedido"]),
                Excedente = Convert.ToDecimal(row["excedente"]),
                FechaSolicitud = Convert.ToDateTime(row["fecha_solicitud"]),
                SolicitadoPor = Convert.ToInt32(row["solicitado_por"]),
                Token = row["token"].ToString()
            };
        }


    }
}