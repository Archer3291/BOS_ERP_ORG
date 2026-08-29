using BOS_ERP.Helpers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Nacionales
{
    /// <summary>
    /// Autorización de crédito para Ventas Nacionales.
    ///
    /// A diferencia de Industrial, aquí aprobar NO genera el pedido: solo deja registrada la
    /// autorización. El vendedor guarda el documento por el flujo normal, que es el único que
    /// sabe repartir las partidas entre Stock, Modula y Tubos. Así no se duplica esa lógica ni
    /// se pierde lo capturado en el formulario (forma de pago, uso de CFDI, productos).
    ///
    /// El desbloqueo lo resuelve <see cref="CreditoVentasHelper.ExisteAutorizacionAprobada"/>,
    /// que sube por la cadena de encabezados_padre, igual que en Industrial.
    /// </summary>
    [Authorize]
    public partial class VNPedidoController
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

                int documentoId = int.TryParse(fc["pedido"].ToString(), out var did) ? did : 0;

                // El pedido se genera AHORA, en estatus pendiente (40). El reparto entre
                // Stock, Modula y Tubos depende de las existencias del momento, así que se
                // calcula una sola vez, con lo que el vendedor tiene en pantalla; aprobar
                // solo lo activa. Si el formulario no viaja (solicitud desde remisión o
                // factura) no hay nada que generar y se sigue como antes.
                if (!string.IsNullOrWhiteSpace(fc["productosJSON"].ToString())
                    && !string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                {
                    var generado = GenerarPedidoDocumentos(
                        fc, estatusDocumentos: 40, omitirValidacionCredito: true);

                    dynamic datos = generado.Value;
                    if (datos == null || !(bool)datos.success)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "No se pudo generar el pedido pendiente: " +
                                      (datos?.message ?? "error desconocido")
                        });
                    }

                    // La solicitud se liga al primer documento generado; sus hermanos se
                    // resuelven por documentos_relacionados al aprobar.
                    documentoId = (int?)datos.id_encabezado_normal
                               ?? (int?)datos.id_encabezado_modula
                               ?? (int?)datos.id_encabezado_tubo
                               ?? documentoId;

                    folio = (string)datos.folio_generado ?? folio;
                }

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
                        { "cliente_id", clienteId  },
                        { "pedido_id",  documentoId },
                        { "limite",     limite     },
                        { "usado",      usado      },
                        { "total",      total      },
                        { "excedente",  excedente  },
                        { "usuario",    usuarioId  },
                        { "token",      token      }
                    });

                string urlAutorizacion =
                    $"{Request.Scheme}://{Request.Host}/VNPedido/Autorizar?token={token}";

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
                    $"[Autorización requerida] Venta nacional {folio}",
                    htmlBody);

                // El token vuelve al front: si el documento aún no existe, es lo único que
                // permite reconocer la autorización cuando el gerente la apruebe.
                return Json(new { success = true, token, folio_generado = folio });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNAutorizacionCredito/?");
                LogErrorHelper.RegistrarLog("VNPedidoController", "0",
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

            var sol = ObtenerSolicitudNacionalPorToken(token);
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

                var sol = ObtenerSolicitudNacionalPorToken(token);
                if (sol == null)
                    return Json(new { success = false, message = "Solicitud no encontrada o ya procesada." });

                bool aprobado = accion == "aprobar";
                string estatus = aprobado ? "aprobado" : "rechazado";

                RunQuery(@"
                    UPDATE autorizaciones_credito
                    SET estatus = @estatus, fecha_resolucion = NOW()
                    WHERE token = @token AND estatus = 'pendiente'",
                    new Dictionary<string, object>
                    {
                        { "estatus", estatus },
                        { "token",   token   }
                    });

                // Los documentos ya existen en estatus 40 (se generaron al solicitar, con el
                // reparto Stock/Modula/Tubos que vio el vendedor): aprobar los activa y
                // rechazar los cancela. Se resuelven todos los hermanos del grupo.
                int documentosAfectados = ResolverDocumentosPendientes(
                    sol.PedidoId, aprobado ? 1 : 10);

                if (aprobado && documentosAfectados > 0)
                    CerrarDocumentoPadre(sol.PedidoId);

                LogErrorHelper.RegistrarLog("VNPedidoController", sol.PedidoId.ToString(),
                    $"Solicitud de crédito {estatus}; {documentosAfectados} documento(s) " +
                    $"actualizados a estatus {(aprobado ? 1 : 10)}.",
                    nivel: "INFO");

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNAutorizacionCredito/ConfirmarAutorizacion");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Pasa a <paramref name="nuevoEstatus"/> los documentos pendientes (40) del grupo al
        /// que pertenece <paramref name="documentoId"/>. Un pedido nacional puede haberse
        /// dividido en VNPED, MODPED y TYBCOT: los tres se resuelven juntos.
        /// </summary>
        private int ResolverDocumentosPendientes(int documentoId, int nuevoEstatus)
        {
            if (documentoId <= 0) return 0;

            var param = new Dictionary<string, object>
            {
                { "doc", documentoId },
                { "est", nuevoEstatus }
            };

            // Solo se tocan los que siguen en 40: si el vendedor ya hizo algo con ellos,
            // esta resolución no los pisa.
            RunUpdate(@"
                UPDATE encabezadomov
                SET    estatus_id = @est
                WHERE  estatus_id = 40
                  AND  id_encabezado IN (
                       SELECT UNNEST(ARRAY[
                           dr.id_encabezado_normal,
                           dr.id_encabezado_modula,
                           dr.id_encabezado_tubo
                       ])
                       FROM documentos_relacionados dr
                       WHERE @doc IN (dr.id_encabezado_normal,
                                      dr.id_encabezado_modula,
                                      dr.id_encabezado_tubo)
                  )", param);

            object afectados = RunScalar(@"
                SELECT COUNT(*)
                FROM   encabezadomov
                WHERE  estatus_id = @est
                  AND  id_encabezado IN (
                       SELECT UNNEST(ARRAY[
                           dr.id_encabezado_normal,
                           dr.id_encabezado_modula,
                           dr.id_encabezado_tubo
                       ])
                       FROM documentos_relacionados dr
                       WHERE @doc IN (dr.id_encabezado_normal,
                                      dr.id_encabezado_modula,
                                      dr.id_encabezado_tubo)
                  )", param);

            return afectados != null && afectados != DBNull.Value ? Convert.ToInt32(afectados) : 0;
        }

        /// <summary>
        /// Cierra la cotización de origen (estatus 11), que al generar el pedido pendiente se
        /// dejó abierta a propósito por si el gerente rechazaba.
        /// </summary>
        private void CerrarDocumentoPadre(int documentoId)
        {
            RunUpdate(@"
                UPDATE encabezadomov
                SET    estatus_id = 11
                WHERE  id_encabezado IN (
                       SELECT dr.id_encabezado_padre
                       FROM   documentos_relacionados dr
                       WHERE  @doc IN (dr.id_encabezado_normal,
                                       dr.id_encabezado_modula,
                                       dr.id_encabezado_tubo)
                  )
                  AND estatus_id <> 11",
                new Dictionary<string, object> { { "doc", documentoId } });
        }

        private SolicitudCreditoVM ObtenerSolicitudNacionalPorToken(string token)
        {
            var dt = RunQuery(@"
                SELECT  ac.id, ac.pedido_id, ac.cliente_id,
                        cc.n_cli,
                        COALESCE(em.folio, '') AS folio,
                        ac.credito_limite, ac.credito_usado,
                        ac.monto_pedido,   ac.excedente,
                        ac.fecha_solicitud, ac.solicitado_por, ac.token
                FROM    autorizaciones_credito ac
                JOIN    catclientes cc ON cc.id_cliente = ac.cliente_id
                LEFT JOIN encabezadomov em ON em.id_encabezado = ac.pedido_id
                WHERE   ac.token   = @token
                  AND   ac.estatus = 'pendiente'
                LIMIT 1",
                new Dictionary<string, object> { { "token", token } });

            if (dt == null || dt.Count == 0) return null;

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
                Token = row["token"].ToString(),
                UrlConfirmacion = "/VNPedido/ConfirmarAutorizacion"
            };
        }
    }
}
