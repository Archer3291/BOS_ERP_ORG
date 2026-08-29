using BOS_ERP.Filters;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using System.Configuration;
using System.Data.SqlClient;
using System.Text;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    public class VTPedidoController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;

        public VTPedidoController(BOS_ERP.Services.EmailSender emailSenderService, BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Creacion de pedido")]
        public JsonResult Guardar(IFormCollection fc)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;

            try
            {
                var parameters = new Dictionary<string, object>();
                string tipo = fc["tipo"].ToString()?.ToLower() ?? "";

                // 🔹 Validaciones previas (sin cambios)
                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un cliente." });
                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una moneda." });
                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un vendedor." });
                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una forma de pago." });

                // 🔹 ★ CAMBIO: DTO tipado en vez de Dictionary<string,string>, para soportar 'cortes' anidado
                List<ProductoPedidoDto> productos = new List<ProductoPedidoDto>();
                if (tipo != "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });

                    productos = JsonConvert.DeserializeObject<List<ProductoPedidoDto>>(fc["productosJSON"].ToString());
                    if (productos == null || productos.Count == 0)
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });
                }

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

                int idEncabezadoPadre = GetUserId(User.Identity.Name);
                int usrId0 = 0;
                DateTime usrFch0 = DateTime.Now;

                // ★ ABRIMOS LA CONEXIÓN/TRANSACCIÓN AQUÍ, ANTES DE TODO,
                // para que encabezado + partidas + cortes vivan en la misma unidad atómica
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                conn = new NpgsqlConnection(connStr);
                conn.Open();
                trx = conn.BeginTransaction();

                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                {
                    var usrParameter = new Dictionary<string, object>
            {
                { "id", Convert.ToInt32(fc["documentid"].ToString()) }
            };
                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3, " +
                        "em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto " +
                        "FROM encabezadomov em WHERE em.id_encabezado = @id";

                    var result = RunQuery(usrquery, usrParameter, false, conn, trx);
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
                    var clienteParameter = new Dictionary<string, object>
            {
                { "cliente", fc["cliente"].ToString() },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };
                    string clientequery = "SELECT cl.id_cliente, cl.n_cli FROM catclientes cl " +
                                           "WHERE cl.cve_cli = @cliente AND cl.empresa_id = @empresa_id";
                    var clienteResult = RunQuery(clientequery, clienteParameter, false, conn, trx);
                    if (clienteResult.Count > 0)
                    {
                        idCliente = Convert.ToInt32(clienteResult[0]["id_cliente"]);
                        clienteNombre = clienteResult[0]["n_cli"].ToString();
                    }
                }

                var fPagoParameter = new Dictionary<string, object> { { "cve_sat", fc["forma-pago"].ToString() } };

                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 26;
                encabezado.IdTpDoc = 88;
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
                encabezado.FPago = Convert.ToInt32(RunScalar("select id_f_pago from cat_f_pago where cve_sat = @cve_sat", fPagoParameter, false, conn, trx));
                encabezado.Mdp = fc["metodo-pago"].ToString();
                encabezado.TipoPoceso = "pedido_" + tipo;
                encabezado.CFDI = fc["uso-cfdi"].ToString();
                encabezado.NatDocPadreChar = fc["ordenCompra"].ToString();

                var partidas = new List<PartidaDocumento>();
                if (productos.Count > 0)
                {
                    for (int idx = 0; idx < productos.Count; idx++)
                    {
                        var p = productos[idx];
                        var parametersP = new Dictionary<string, object>
                {
                    { "cve_prod", p.ProductoId ?? "" },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };
                        string queryId = "select id_catproductos, es_tubo from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id";
                        var prodInfo = RunQuery(queryId, parametersP, false, conn, trx);
                        if (prodInfo.Count == 0)
                            throw new InvalidOperationException($"El producto \"{p.ProductoId}\" no existe en el catálogo.");

                        int productId = Convert.ToInt32(prodInfo[0]["id_catproductos"]);
                        bool esTubo = prodInfo[0]["es_tubo"] != null && Convert.ToBoolean(prodInfo[0]["es_tubo"]);

                        // Un pedido de tubo sólo es válido si su corte está configurado. Antes se
                        // dejaba pasar sin cortes y el pedido quedaba invisible para el operador
                        // (CorteOperacion hace INNER JOIN sobre las asignaciones de corte). El
                        // front ya lo valida, pero el backend no debe confiar en eso.
                        if (esTubo)
                        {
                            decimal sumaCortes = (p.Cortes ?? new List<CorteRequestDto>())
                                .Sum(c => c.Longitud * c.Cantidad);

                            if (p.Cortes == null || p.Cortes.Count == 0)
                                throw new InvalidOperationException(
                                    $"La partida {idx + 1} (\"{p.ProductoId}\") es un tubo y no tiene cortes configurados.");

                            if (Math.Abs(sumaCortes - p.Cantidad) > 0.01m)
                                throw new InvalidOperationException(
                                    $"La partida {idx + 1} (\"{p.ProductoId}\"): la suma de los cortes ({sumaCortes:0.##}) " +
                                    $"no coincide con la cantidad pedida ({p.Cantidad:0.##}).");
                        }

                        partidas.Add(new PartidaDocumento
                        {
                            CveProd = p.ProductoId ?? "",
                            DescrProd = p.Descripcion ?? "",
                            CantUd = p.Cantidad,
                            PvProd = p.Precio,
                            Dto1 = p.Descuento,
                            ImpPart = p.Cantidad * p.Precio,
                            Ud = p.Unidad ?? "PZA",
                            IdProducto = productId,
                            TpDocAnt = p.Comentario ?? ""
                        });
                    }
                }

                // ★ Usa el overload que ya existe (visto en AprobarPedido) para que corra en la misma trx
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, trx);
                int idEncabezadoNuevo = Convert.ToInt32(folio["IdEncabezado"]);

                // ★ NUEVO: obtener los ids de partida recién insertados, en el mismo orden que 'productos'
                var idsPartidas = ObtenerIdsPartidasPorEncabezado(idEncabezadoNuevo, conn, trx);

                if (idsPartidas.Count != productos.Count)
                    throw new InvalidOperationException(
                        $"No coincide el número de partidas guardadas ({idsPartidas.Count}) con los productos enviados ({productos.Count}).");

                // ★ NUEVO: guardar cortes de cada partida, EN LA MISMA TRANSACCIÓN.
                // Si algo falla aquí (ej. stock insuficiente detectado con FOR UPDATE),
                // se revierte TODO el pedido, no solo los cortes.
                var cortesPorProducto = new Dictionary<int, List<(int idPartida, List<CorteRequestDto> cortes)>>();

                for (int i = 0; i < productos.Count; i++)
                {
                    if (productos[i].Cortes == null || productos[i].Cortes.Count == 0) continue;

                    var parametersP = new Dictionary<string, object>
    {
        { "cve_prod", productos[i].ProductoId ?? "" },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    };
                    int idProductoActual = Convert.ToInt32(RunScalar(
                        "select id_catproductos from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id",
                        parametersP, false, conn, trx));

                    if (!cortesPorProducto.TryGetValue(idProductoActual, out var lista))
                        cortesPorProducto[idProductoActual] = lista = new List<(int, List<CorteRequestDto>)>();

                    lista.Add((idsPartidas[i], productos[i].Cortes));
                }

                foreach (var kvp in cortesPorProducto)
                {
                    AsignarCortesPorProducto(conn, trx, kvp.Key,
                        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")), User.Identity.Name, kvp.Value);
                }

                // 🔹 Actualizar estatus del padre (misma transacción)
                RunQuery("UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id",
                    new Dictionary<string, object> { { "id", idEncabezadoPadre } }, false, conn, trx);

                trx.Commit();

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = idEncabezadoNuevo,
                    Folio = folio["folio_generado"].ToString()
                };

                return Json(new { success = true, message = "Documento creado exitosamente.", folio_generado = folio["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VTPedido/?");
                try { trx?.Rollback(); } catch { /* ya se cerró o no aplica */ }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }

        // ★ NUEVO: obtiene los id_partidas recién generados para un encabezado,
        // en el mismo orden en que se insertaron (por nro_part / orden natural)
        private List<int> ObtenerIdsPartidasPorEncabezado(int idEncabezado, NpgsqlConnection conn, NpgsqlTransaction trx)
        {
            string query = @"
        SELECT id_partidas
        FROM partidasdoc
        WHERE encabezado_id = @id
        ORDER BY nro_part;";

            var rows = RunQuery(query, new Dictionary<string, object> { { "id", idEncabezado } }, false, conn, trx);
            return rows.Select(r => Convert.ToInt32(r["id_partidas"])).ToList();
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

                // 🔹 Guardar solicitud (ahora con token)
                InsertarSolicitudCredito(clienteId, pedidoId, limite, usado, total, excedente, usuarioId, token);

                // 🔹 Cambiar estatus del pedido
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


                await correoHelper.EnviarCorreoNotificacionAsync(
                    gerenteEmail,
                    $"[Autorización requerida] Pedido {folio}",
                    htmlBody
                );

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VTPedido/EnviarSolicitudGerente");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private void InsertarSolicitudCredito(
    int clienteId, int pedidoId, decimal limite,
    decimal usado, decimal total, decimal excedente,
    int usuarioId, string token)
        {
            string query = @"
        INSERT INTO autorizaciones_credito
            (cliente_id, pedido_id, credito_limite, credito_usado,
             monto_pedido, excedente, solicitado_por, token, estatus)
        VALUES
            (@cliente_id, @pedido_id, @limite, @usado,
             @total, @excedente, @usuario, @token, 'pendiente')";

            RunQuery(query, new Dictionary<string, object>
            {
                { "cliente_id", clienteId },
                { "pedido_id",  pedidoId  },
                { "limite",     limite    },
                { "usado",      usado     },
                { "total",      total     },
                { "excedente",  excedente },
                { "usuario",    usuarioId },
                { "token",      token     }
            });
        }

        private void MarcarPedidoPendiente(int pedidoId)
        {
            string query = @"
                            UPDATE encabezadomov
                            SET estatus_id = 40
                            WHERE id_encabezado = @id";

            RunQuery(query, new Dictionary<string, object> { { "id", pedidoId } });
        }

        [HttpPost]
        public IActionResult AprobarPedido(int idpedido, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var parameters = new Dictionary<string, object>();

            string docquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3,  " +
                                      "       em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto, coment_aut, " +
                                      "       imp, dto, cli_prov, ccy, refe, flete, vdr_cpr, coment1, pl_dias, fch_pg_entrega, par, f_pago, mdp, tipo_proceso, cfdi, orden_compra " +
                                      "FROM encabezadomov em " +
                                      "WHERE em.id_encabezado = @id";
            parameters.Add("id", idpedido);
            var doc = RunQuery(docquery, parameters, false, conn, tx);

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

            string tipoProceso = "";

            if (doc.Count > 0 && doc[0].ContainsKey("tipo_proceso") && doc[0]["tipo_proceso"] != null && doc[0]["tipo_proceso"] != DBNull.Value)
            {
                tipoProceso = doc[0]["tipo_proceso"].ToString();
            }

            string cfdi = "";

            if (doc.Count > 0 && doc[0].ContainsKey("cfdi") && doc[0]["cfdi"] != null && doc[0]["cfdi"] != DBNull.Value)
            {
                cfdi = doc[0]["cfdi"].ToString();
            }

            var encabezado = new DocumentoEncabezado();
            encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
            encabezado.IdArea = 26;
            encabezado.IdTpDoc = 88;
            encabezado.UsrDep = GetAreaName(User.Identity.Name);
            encabezado.Anio = DateTime.Now.Year;
            encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            // encabezado.//Alm = fc["almacen"].ToString();
            encabezado.Fch = DateTime.Now;
            encabezado.TpMov = "TYBPED";
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
            encabezado.Ccy = doc[0]["ccy"].ToString();
            encabezado.Estatus = 1;
            encabezado.Ref = Convert.ToInt32(doc[0]["refe"]);
            encabezado.Flete = Convert.ToInt32(doc[0]["flete"]);
            encabezado.VdrCpr = doc[0]["vdr_cpr"].ToString();
            encabezado.Coment1 = doc[0]["coment1"].ToString();
            encabezado.EncabezadoPadre = idpedido;
            encabezado.PlDias = plDias;
            encabezado.FchPgEntrega = Convert.ToDateTime(doc[0]["fch_pg_entrega"]);
            encabezado.Par = Convert.ToDecimal(doc[0]["par"]);
            encabezado.FPago = Convert.ToInt32(doc[0]["f_pago"]);
            encabezado.Mdp = mdp;
            encabezado.TipoPoceso = tipoProceso;
            encabezado.CFDI = cfdi;
            encabezado.NatDocPadreChar = doc[0]["orden_compra"].ToString();


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

            return Json(new { success = true });
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

                int estatusId = accion == "aprobar" ? 11 : 11;
                string estatus = accion == "aprobar" ? "aprobado" : "rechazado";

                ProcesarResolucionPorToken(sol.PedidoId, estatus, estatusId, token);

                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // ─── Helpers privados ────────────────────────────────────────────────────────

        private void ProcesarResolucion(int pedidoId, string estatus, int estatusId, int usuarioId)
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
                        RunQuery("UPDATE encabezadomov SET estatus_id = @est WHERE id_encabezado = @id", new Dictionary<string, object> { { "est", estatusId }, { "id", pedidoId } }, false, conn, tx);
                        RunQuery(@"
        UPDATE autorizaciones_credito
        SET estatus = @estatus, autorizado_por = @usr, fecha_resolucion = NOW()
        WHERE pedido_id = @pid AND estatus = 'pendiente'", new Dictionary<string, object> { { "estatus", estatus }, { "usr", usuarioId }, { "pid", pedidoId } }, false, conn, tx);

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
                em.folio,
                ac.credito_limite, ac.credito_usado,
                ac.monto_pedido,   ac.excedente,
                ac.fecha_solicitud, ac.solicitado_por, ac.token
        FROM    autorizaciones_credito ac
        JOIN    catclientes       cc ON cc.id_cliente    = ac.cliente_id
        JOIN    encabezadomov em ON em.id_encabezado = ac.pedido_id
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

        private void AsignarCortesPorProducto(
    NpgsqlConnection conn, NpgsqlTransaction trx,
    int idProducto, int sucursal, string usuario,
    List<(int idPartida, List<CorteRequestDto> cortes)> solicitudesPorPartida)
        {
            // Aplanar TODAS las piezas solicitadas de TODAS las partidas de este producto en
            // este pedido, de mayor a menor longitud (para no dejar sin material a un corte
            // grande por haberle repartido antes a los chicos).
            var solicitudes = new List<(int idPartida, CorteRequestDto corte, decimal longitud)>();
            foreach (var (idPartida, cortes) in solicitudesPorPartida)
                foreach (var corte in cortes ?? new List<CorteRequestDto>())
                {
                    if (corte.Longitud <= 0 || corte.Cantidad <= 0)
                        throw new InvalidOperationException(
                            "Se recibió una configuración de corte inválida (longitud o cantidad en 0). " +
                            "Revisa que todas las piezas de corte estén completamente capturadas.");
                    for (int i = 0; i < corte.Cantidad; i++)
                        solicitudes.Add((idPartida, corte, corte.Longitud));
                }

            solicitudes = solicitudes.OrderByDescending(s => s.longitud).ToList();

            // Un solo pedido_detalle_corte por corte lógico, aunque cantidad > 1
            var corteDbIdCache = new Dictionary<(int, CorteRequestDto), int>();

            foreach (var sol in solicitudes)
            {
                // ★ Clave del fix: resolver contra la BD EN VIVO en cada paso, no contra lo
                // que el front cree que existe. Así siempre ve lo que ya consumieron las
                // demás partidas del mismo pedido, incluso dentro de la misma transacción.
                var (idCorteFisico, longitudPieza, _) =
                    ResolverOFormalizarPiezaFallback(conn, trx, idProducto, sucursal, sol.longitud, usuario);

                decimal sobrante = Math.Max(0, longitudPieza - sol.longitud);

                RunQuery("UPDATE tarima_productos_cortes SET cantidad = cantidad - 1 WHERE id_corte = @id;",
                    new Dictionary<string, object> { { "id", idCorteFisico } }, false, conn, trx);

                var key = (sol.idPartida, sol.corte);
                if (!corteDbIdCache.TryGetValue(key, out int corteDbId))
                {
                    corteDbId = Convert.ToInt32(RunScalar(@"
                INSERT INTO pedido_detalle_corte (pedido_detalle_id, longitud, cantidad, comentario, precio)
                VALUES (@pedido_detalle_id, @longitud, @cantidad, @comentario, @precio) RETURNING id;",
                        new Dictionary<string, object>
                        {
                    { "pedido_detalle_id", sol.idPartida },
                    { "longitud", sol.corte.Longitud }, { "cantidad", sol.corte.Cantidad },
                    { "comentario", (object)sol.corte.Comentario ?? DBNull.Value },
                    { "precio", sol.corte.Precio }
                        }, false, conn, trx));
                    corteDbIdCache[key] = corteDbId;
                }

                int idAsignacion = Convert.ToInt32(RunScalar(@"
            INSERT INTO pedido_detalle_corte_asignacion
                (pedido_detalle_corte_id, tarima_producto_corte_id, longitud_origen, cantidad_asignada, sobrante)
            VALUES (@corteId, @idCorteFisico, @longitudOrigen, 1, @sobrante)
            RETURNING id;",
                    new Dictionary<string, object>
                    {
                { "corteId", corteDbId }, { "idCorteFisico", idCorteFisico },
                { "longitudOrigen", longitudPieza }, { "sobrante", sobrante }
                    }, false, conn, trx));

                // Enlaza esta asignación con la barra física concreta que el UPDATE de arriba
                // acaba de consumir (trg_corte_piezas_sync la pasó a 'usada' en el mismo statement).
                // cantidad_asignada siempre es 1 aquí, así que hay exactamente una pieza que
                // tomar: la de fecha_baja más reciente para este id_corte que todavía no tiene
                // dueño. No se toca estado/cantidad — eso lo sigue gobernando solo el trigger.
                EtiquetarPiezaConsumida(conn, trx, idCorteFisico, idAsignacion);

                // Mismo umbral mínimo que en AdminCortes/Crear: solo descarta ruido de
                // redondeo decimal, no retazos reales de piezas chicas.
                if (sobrante > 0.001m)
                {
                    string folioNuevo = RunScalar("SELECT fn_generar_folio_corte();",
                        new Dictionary<string, object>(), false, conn, trx).ToString();

                    RunQuery(@"
                INSERT INTO tarima_productos_cortes
                    (folio, tarima_producto_id, longitud, cantidad, cantidad_original,
                     usuario_creacion, comentario, generado_por_asignacion_id)
                SELECT @folio, tarima_producto_id, @longitud, 1, 1, @usuario,
                       'Retazo generado automáticamente', @asigId
                FROM tarima_productos_cortes WHERE id_corte = @idOrigen;",
                        new Dictionary<string, object>
                        {
                    { "folio", folioNuevo }, { "longitud", sobrante },
                    { "usuario", usuario }, { "idOrigen", idCorteFisico }, { "asigId", idAsignacion }
                        }, false, conn, trx);
                }
            }
        }

        // Ata una asignación de pedido a la barra física (srs.corte_piezas) que el trigger de
        // sincronía acaba de marcar 'usada' para ese id_corte. Si el módulo de piezas no está
        // instalado (sql/cortes_piezas.sql sin correr), la tabla no existe y esto se ignora: la
        // operación de corte sigue funcionando igual que antes, solo sin el código de barras.
        private void EtiquetarPiezaConsumida(NpgsqlConnection conn, NpgsqlTransaction trx, int idCorteFisico, int idAsignacion)
        {
            try
            {
                RunQuery(@"
            UPDATE corte_piezas
            SET asignacion_id = @idAsignacion
            WHERE id_pieza = (
                SELECT id_pieza FROM corte_piezas
                WHERE corte_id = @idCorteFisico AND estado = 'usada' AND asignacion_id IS NULL
                ORDER BY fecha_baja DESC, id_pieza ASC
                LIMIT 1
            );",
                    new Dictionary<string, object>
                    {
                        { "idAsignacion", idAsignacion }, { "idCorteFisico", idCorteFisico }
                    }, false, conn, trx);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { /* corte_piezas no existe todavía */ }
        }

        private (int idCorte, decimal longitud, decimal cantidad) ResolverOFormalizarPiezaFallback(
            NpgsqlConnection conn, NpgsqlTransaction trx, int idProducto, int sucursal, decimal longitudMinima, string usuario)
        {
            // 1. Si ya existe alguna pieza formal que alcance, úsala — pero SOLO si pertenece
            // a la misma sucursal y a un almacén tipo 'Stock' (nunca Modula, nunca otra sucursal).
            var candidatas = RunQuery(@"
        SELECT tpc.id_corte, tpc.cantidad, tpc.longitud
        FROM tarima_productos_cortes tpc
        INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id
        INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
        INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
        INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
        INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
        INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
        WHERE tp.producto_id = @idProducto
          AND tpc.longitud >= @longitud
          AND tpc.activo = true
          AND tpc.cantidad > 0
          AND cs.id_sucursal = @sucursal
          AND ca.tipo = 'Stock'
        ORDER BY tpc.longitud ASC FOR UPDATE;",
                new Dictionary<string, object>
                {
            { "idProducto", idProducto },
            { "longitud", longitudMinima },
            { "sucursal", sucursal }
                },
                false, conn, trx);

            if (candidatas.Count > 0)
            {
                var c = candidatas[0];
                return (Convert.ToInt32(c["id_corte"]), Convert.ToDecimal(c["longitud"]), Convert.ToDecimal(c["cantidad"]));
            }

            // 2. No hay pieza formal en esta sucursal/Stock: formalizar la barra virgen
            // completa como pieza nueva.
            // ★ MODELO (igual que AdminCortes.Crear): tarima_productos es la AUTORIDAD del
            //   total; la formalización NO descuenta tarima_productos — solo crea el desglose
            //   en tarima_productos_cortes. El material se descuenta de tarima_productos hasta
            //   la REMISIÓN (vía RegistrarMovimiento, que registra el documento de salida).
            // ★ Para no sobre-cortar: solo se formaliza una tarima_producto VIRGEN (sin ninguna
            //   pieza previa). Una vez formalizada, todo el material vive en sus piezas
            //   (fuentes consumidas + retazos) y los cortes siguientes salen de esos retazos.
            var filaTp = RunQuery(@"
        SELECT tp.id_tarima_producto, tp.cantidad
        FROM tarima_productos tp
        INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
        INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
        INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
        INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
        INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
        WHERE tp.producto_id = @idProducto
          AND cs.id_sucursal = @sucursal
          AND ca.tipo = 'Stock'
          AND tp.cantidad >= @longitud
          AND NOT EXISTS (
              SELECT 1 FROM tarima_productos_cortes tpc
              WHERE tpc.tarima_producto_id = tp.id_tarima_producto
          )
        ORDER BY tp.cantidad ASC
        LIMIT 1
        FOR UPDATE;",
                new Dictionary<string, object> { { "idProducto", idProducto }, { "sucursal", sucursal }, { "longitud", longitudMinima } },
                false, conn, trx);

            if (filaTp.Count == 0)
                throw new InvalidOperationException("No hay inventario físico suficiente para este producto.");

            int idTarimaProducto = Convert.ToInt32(filaTp[0]["id_tarima_producto"]);
            decimal cantidadTotal = Convert.ToDecimal(filaTp[0]["cantidad"]);

            string folio = RunScalar("SELECT fn_generar_folio_corte();",
                new Dictionary<string, object>(), false, conn, trx).ToString();

            int idCorteNuevo = Convert.ToInt32(RunScalar(@"
        INSERT INTO tarima_productos_cortes
            (folio, tarima_producto_id, longitud, cantidad, cantidad_original, usuario_creacion, comentario)
        VALUES (@folio, @tpId, @longitud, 1, 1, @usuario, 'Formalizado automáticamente al guardar corte')
        RETURNING id_corte;",
                new Dictionary<string, object>
                {
            { "folio", folio }, { "tpId", idTarimaProducto },
            { "longitud", cantidadTotal }, { "usuario", usuario }
                }, false, conn, trx));

            // ★ NO se descuenta tarima_productos aquí. El bulk permanece como total y se
            //   descuenta únicamente al remisionar.

            return (idCorteNuevo, cantidadTotal, 1);
        }

        private string ObtenerNuevoFolio(NpgsqlConnection conn, NpgsqlTransaction trx)
        {
            var cmd = new NpgsqlCommand("SELECT fn_generar_folio_corte()", conn, trx);
            return (string)cmd.ExecuteScalar();
        }

        // ─── Detalle de pedido (para modal desde Admin Cortes / Usados) ──────────────
        [HttpGet]
        public IActionResult Detalle(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "id", id },
            { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
        };

                string queryEncabezado = @"
            SELECT
                em.id_encabezado, em.folio, em.fch, em.imp, em.dto, em.flete,
                em.ccy, em.cli_prov, em.vdr_cpr, em.coment1, em.coment_aut,
                em.estatus_id, em.orden_compra,
                cl.n_cli AS cliente_nombre
            FROM encabezadomov em
            LEFT JOIN catclientes cl
                ON cl.cve_cli = em.cli_prov AND cl.empresa_id = @empresa_id
            WHERE em.id_encabezado = @id;";

                var encabezado = RunQuery(queryEncabezado, parameters);
                if (encabezado.Count == 0)
                    return Json(new { success = false, message = "No se encontró el pedido." });

                string queryPartidas = @"
            SELECT
                p.cve_prod, p.descr_prod, p.cant_ud, p.pv_prod, p.dto1, p.ud,
                (p.cant_ud * p.pv_prod) AS importe
            FROM partidasdoc p
            WHERE p.encabezado_id = @id
            ORDER BY p.nro_part;";

                var partidas = RunQuery(queryPartidas, new Dictionary<string, object> { { "id", id } });

                return Json(new { success = true, encabezado = encabezado[0], partidas });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VTPedido/Detalle");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class CorteRequestDto
        {
            public decimal Longitud { get; set; }
            public int Cantidad { get; set; }
            public string Comentario { get; set; }
            // Precio (costo) de la línea de corte. Se captura por corte y se guarda en
            // pedido_detalle_corte; la suma de todos alimenta la partida de SERVICIO DE CORTE.
            public decimal Precio { get; set; }
            public List<PiezaUsadaDto> PiezasUsadas { get; set; }
        }

        public class PiezaUsadaDto
        {
            public int IdCorte { get; set; }
            public string Folio { get; set; }
            public decimal Longitud { get; set; }   
            public decimal Cantidad { get; set; }
            public decimal Sobrante { get; set; }
        }

        public class ProductoPedidoDto
        {
            public string ProductoId { get; set; }
            public string Descripcion { get; set; }
            public decimal Cantidad { get; set; }
            public decimal Precio { get; set; }
            public decimal Descuento { get; set; }
            public string Unidad { get; set; }
            public string Comentario { get; set; }
            public List<CorteRequestDto> Cortes { get; set; } = new();
        }

        public class ConfirmarCorteDto
        {
            public int AsignacionId { get; set; }
        }

        public class ReasignarCorteDto
        {
            public int AsignacionId { get; set; }   // asignación original a liberar
            public int NuevoIdCorte { get; set; }   // id_corte de la pieza que el operador eligió en su lugar
            public string NuevoFolio { get; set; }
        }
    }
}