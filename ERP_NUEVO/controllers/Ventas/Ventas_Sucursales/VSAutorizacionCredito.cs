using BOS_ERP.Helpers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Sucursales
{
    /// <summary>
    /// Autorización de crédito para Ventas Sucursales.
    ///
    /// Igual que en Nacional, aprobar NO genera el documento: solo deja registrada la
    /// autorización. El vendedor vuelve a guardar por el flujo normal y esta vez pasa, porque
    /// <see cref="CreditoVentasHelper.ExisteAutorizacionAprobada"/> encuentra la autorización
    /// subiendo por la cadena de encabezados_padre (cotización → pedido → remisión → factura).
    ///
    /// Deliberadamente no se copia el AprobarPedido de Industrial: ahí la aprobación
    /// reconstruye el pedido desde la cotización, y en sucursal eso perdería lo capturado en
    /// pantalla (existencias verificadas, traspaso solicitado, ticket).
    /// </summary>
    [Authorize]
    public partial class VSPedidoController
    {
        [HttpPost]
        public async Task<ActionResult> EnviarSolicitudGerente(IFormCollection fc)
        {
            try
            {
                string gerenteEmail = fc["gerenteEmail"].ToString() ?? "";
                if (string.IsNullOrWhiteSpace(gerenteEmail))
                    return Json(new { success = false, message = "Correo del gerente requerido." });

                string folio = fc["folio"].ToString() ?? "";
                string cliente = fc["cliente"].ToString() ?? "";
                string productosJSON = fc["productosJSON"].ToString() ?? "[]";

                // Documento de origen (la cotización o el pedido cargado). Puede ser 0 cuando
                // se captura desde cero: en ese caso la autorización solo se reconoce por token.
                int documentoId = int.TryParse(fc["pedido"].ToString(), out var did) ? did : 0;

                var parameters = new Dictionary<string, object>
                {
                    { "cve_cli",    cliente },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                object idCli = RunScalar(
                    "SELECT id_cliente FROM catclientes WHERE cve_cli = @cve_cli AND empresa_id = @empresa_id",
                    parameters);

                int clienteId = idCli != null && idCli != DBNull.Value ? Convert.ToInt32(idCli) : 0;

                decimal limite = decimal.TryParse(fc["limiteCredito"].ToString(), out var l) ? l : 0;
                decimal usado = decimal.TryParse(fc["creditoUsado"].ToString(), out var u) ? u : 0;
                decimal disp = decimal.TryParse(fc["creditoDisp"].ToString(), out var d) ? d : 0;
                decimal total = decimal.TryParse(fc["totalPedido"].ToString(), out var t) ? t : 0;

                decimal excedente = 0;
                if (limite > 0 && usado + total > limite)
                    excedente = usado + total - limite;

                int usuarioId = Convert.ToInt32(HttpContext.Session.GetInt32("UsuarioId"));
                string token = Guid.NewGuid().ToString("N");

                RunQuery(@"
                    INSERT INTO autorizaciones_credito
                        (cliente_id, pedido_id, credito_limite, credito_usado,
                         monto_pedido, excedente, solicitado_por, token, estatus)
                    VALUES
                        (@cliente_id, @pedido_id, @limite, @usado,
                         @total, @excedente, @usuario, @token, 'pendiente')",
                    new Dictionary<string, object>
                    {
                        { "cliente_id", clienteId   },
                        { "pedido_id",  documentoId },
                        { "limite",     limite      },
                        { "usado",      usado       },
                        { "total",      total       },
                        { "excedente",  excedente   },
                        { "usuario",    usuarioId   },
                        { "token",      token       }
                    });

                string urlAutorizacion =
                    $"{Request.Scheme}://{Request.Host}/VSPedido/Autorizar?token={token}";

                var productosList = new List<ProductoEmail>();
                try
                {
                    var productos = Newtonsoft.Json.JsonConvert
                        .DeserializeObject<List<Dictionary<string, object>>>(productosJSON);

                    foreach (var p in productos ?? new List<Dictionary<string, object>>())
                    {
                        productosList.Add(new ProductoEmail
                        {
                            Descripcion = p.ContainsKey("descripcion") ? p["descripcion"]?.ToString() : "-",
                            Cantidad = p.ContainsKey("cantidad") ? p["cantidad"]?.ToString() : "0",
                            Precio = p.ContainsKey("precio") ? p["precio"]?.ToString() : "0",
                            Importe = p.ContainsKey("importe")
                                        && decimal.TryParse(p["importe"]?.ToString(), out var imp) ? imp : 0
                        });
                    }
                }
                catch { }

                string origen = fc["origen"].ToString();
                string etiquetaDoc = origen switch
                {
                    "cotizacion" => "Cotización",
                    "remision" => "Remisión",
                    "factura" => "Factura",
                    _ => "Pedido"
                };

                var emailData = new SolicitudCreditoEmailModel
                {
                    UsuarioSolicitante = User.Identity.Name,
                    Cliente = cliente,
                    Folio = folio,
                    Limite = limite,
                    Usado = usado,
                    Disponible = disp,
                    Total = total,
                    Productos = productosList,
                    Fecha = DateTime.Now,
                    Token = token,
                    UrlAutorizacion = urlAutorizacion
                };

                string htmlBody = await emailSender.RenderViewToStringAsync(
                    "~/Views/Email/_SolicitudCredito.cshtml", emailData);

                await correoHelper.EnviarCorreoNotificacionAsync(
                    gerenteEmail,
                    $"[Autorización requerida] {etiquetaDoc} sucursal {folio}",
                    htmlBody);

                // El token vuelve al front: si el documento aún no existe, es lo único que
                // permite reconocer la autorización cuando el gerente la apruebe.
                return Json(new { success = true, token });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VSAutorizacionCredito/EnviarSolicitudGerente");
                LogErrorHelper.RegistrarLog("VSPedidoController", "0",
                    $"Error al enviar la solicitud de crédito: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Vista pública por token (link del correo) ────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Autorizar(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return Content("Token inválido.");

            var sol = ObtenerSolicitudSucursalPorToken(token);
            if (sol == null) return Content("Solicitud no encontrada o ya fue procesada.");

            return View("~/Views/Shared/Autorizar.cshtml", sol);
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult ConfirmarAutorizacion(string token, string accion)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    return Json(new { success = false, message = "Token inválido." });

                var sol = ObtenerSolicitudSucursalPorToken(token);
                if (sol == null)
                    return Json(new { success = false, message = "Solicitud no encontrada o ya procesada." });

                string estatus = accion == "aprobar" ? "aprobado" : "rechazado";

                RunQuery(@"
                    UPDATE autorizaciones_credito
                    SET estatus = @estatus, fecha_resolucion = NOW()
                    WHERE token = @token AND estatus = 'pendiente'",
                    new Dictionary<string, object>
                    {
                        { "estatus", estatus },
                        { "token",   token   }
                    });

                LogErrorHelper.RegistrarLog("VSPedidoController", sol.PedidoId.ToString(),
                    $"Solicitud de crédito de sucursal {estatus}.", nivel: "INFO");

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VSAutorizacionCredito/ConfirmarAutorizacion");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private SolicitudCreditoVM ObtenerSolicitudSucursalPorToken(string token)
        {
            // LEFT JOIN al encabezado: un documento capturado desde cero todavía no existe y
            // con INNER la solicitud resultaba invisible desde el link del correo.
            var filas = RunQuery(@"
                SELECT  ac.id, ac.pedido_id, ac.cliente_id,
                        cc.n_cli,
                        COALESCE(em.folio, '') AS folio,
                        ac.credito_limite, ac.credito_usado,
                        ac.monto_pedido,   ac.excedente,
                        ac.fecha_solicitud, ac.solicitado_por, ac.token
                FROM    autorizaciones_credito ac
                JOIN    catclientes    cc ON cc.id_cliente    = ac.cliente_id
                LEFT JOIN encabezadomov em ON em.id_encabezado = ac.pedido_id
                WHERE   ac.token   = @token
                  AND   ac.estatus = 'pendiente'
                LIMIT 1",
                new Dictionary<string, object> { { "token", token } });

            if (filas == null || filas.Count == 0) return null;

            var row = filas[0];
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
                Token = row["token"].ToString(),
                UrlConfirmacion = "/VSPedido/ConfirmarAutorizacion"
            };
        }
    }
}
