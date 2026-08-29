using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using BOS_ERP.Models;
using System.Globalization;
using System.ServiceModel;

namespace BOS_ERP.Controllers.Almacen
{
    public class ModulaController : Utilities
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ModulaController> _logger;

        // Si "Utilities" ya tiene un constructor que pide estas dependencias,
        // ajusta este constructor para llamar a base(...) en su lugar.
        public ModulaController(IConfiguration configuration, ILogger<ModulaController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private ServiceReference2.Login GetLogin() => new ServiceReference2.Login
        {
            User = _configuration["Modula:Usuario"],
            Pass = _configuration["Modula:Password"]
        };

        private string UsuarioActual =>
            User?.Identity?.Name ?? "Anónimo";

        // ── Helper de auditoría ─────────────────────────────────────
        private void GuardarAuditoria(ModulaAuditoria log)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = @"
                        INSERT INTO modula_auditoria
                            (fecha, usuario, tipo_accion, origen, codigo_articulo,
                             orden_numero, cantidad, payload_json, ws_status, ws_respuesta, total_registros)
                        VALUES
                            (@fecha, @usuario, @tipo, @origen, @codigo,
                             @orden, @cantidad, @payload, @status, @respuesta, @total)";

                parameters.Add("fecha", log.Fecha);
                parameters.Add("usuario", log.Usuario);
                parameters.Add("tipo", log.TipoAccion);
                parameters.Add("origen", log.Origen);
                parameters.Add("codigo", (object)log.CodigoArticulo ?? DBNull.Value);
                parameters.Add("orden", (object)log.OrdenNumero ?? DBNull.Value);
                parameters.Add("cantidad", (object)log.Cantidad ?? DBNull.Value);
                parameters.Add("payload", (object)log.PayloadJson ?? DBNull.Value);
                parameters.Add("status", (object)log.WsStatus ?? DBNull.Value);
                parameters.Add("respuesta", (object)log.WsRespuesta ?? DBNull.Value);
                parameters.Add("total", log.TotalRegistros);

                RunQuery(query, parameters);
            }
            catch (Exception ex)
            {
                // El log nunca debe romper la operación principal
                _logger.LogError(ex, "Error guardando auditoría de Modula");
            }
        }

        // ── Index ───────────────────────────────────────────────────
        public IActionResult Index() => View();

        // ── Item Master ─────────────────────────────────────────────
        [HttpPost]
        public JsonResult Enviar(TestModulaViewModel model)
        {
            var payload = new { model.Codigo, model.Descripcion, model.Unidad };
            string wsStatus = "ERROR", wsMensaje = "";
            bool exito = false;

            try
            {
                var client = new ServiceReference2.InterfaceClient();
                client.InnerChannel.OperationTimeout = TimeSpan.FromSeconds(30);

                var items = new[]
                {
                    new ServiceReference2.Item
                    {
                        ART_ARTICOLO = model.Codigo,
                        ART_DES      = model.Descripcion,
                        ART_UMI      = model.Unidad
                    }
                };

                var response = client.postItemMaster(GetLogin(), items);
                CerrarCliente(client);

                exito = response.All(r => r.Status == "OK");
                wsStatus = exito ? "OK" : "ERROR";
                wsMensaje = string.Join(" | ", response.Select(r =>
                    $"Status:{r.Status} Msg:{r.Message}"));

                return Json(new { ok = exito, mensaje = wsMensaje });
            }
            catch (Exception ex)
            {
                wsMensaje = "ERROR: " + ex.Message;
                return Json(new { ok = false, mensaje = wsMensaje });
            }
            finally
            {
                GuardarAuditoria(new ModulaAuditoria
                {
                    Fecha = DateTime.Now,
                    Usuario = UsuarioActual,
                    TipoAccion = "ITEM_MASTER",
                    Origen = "INDIVIDUAL",
                    CodigoArticulo = model.Codigo,
                    PayloadJson = JsonConvert.SerializeObject(payload),
                    WsStatus = wsStatus,
                    WsRespuesta = wsMensaje,
                    TotalRegistros = 1
                });
            }
        }

        // ── Inbound individual ──────────────────────────────────────
        [HttpPost]
        public JsonResult EnviarInbound(TestModulaViewModel model)
        {
            var payload = new
            {
                model.OrdenNumero,
                model.OrdenDescripcion,
                model.Codigo,
                model.Descripcion,
                model.Unidad,
                model.Cantidad,
                model.Lote,
                model.LineaHost
            };
            string wsStatus = "ERROR", wsMensaje = "";
            bool exito = false;

            try
            {
                var client = new ServiceReference2.InterfaceClient();
                client.InnerChannel.OperationTimeout = TimeSpan.FromSeconds(30);

                var orders = new[]
                {
                    new ServiceReference2.Order
                    {
                        ORD_ORDINE   = model.OrdenNumero,
                        ORD_DES      = model.OrdenDescripcion,
                        RIG_ARTICOLO = model.Codigo,
                        RIG_QTAR     = model.Cantidad,
                        ART_DES      = model.Descripcion,
                        ART_UMI      = model.Unidad,
                    }
                };

                var response = client.postInbound(GetLogin(), orders);
                CerrarCliente(client);

                exito = response.All(r => r.Status == "OK");
                wsStatus = exito ? "OK" : "ERROR";
                wsMensaje = string.Join(" | ", response.Select(r =>
                    $"Status:{r.Status} Msg:{r.Message} Rows:{r.Rows}"));

                return Json(new { ok = exito, mensaje = wsMensaje });
            }
            catch (Exception ex)
            {
                wsMensaje = "ERROR: " + ex.Message;
                return Json(new { ok = false, mensaje = wsMensaje });
            }
            finally
            {
                GuardarAuditoria(new ModulaAuditoria
                {
                    Fecha = DateTime.Now,
                    Usuario = UsuarioActual,
                    TipoAccion = "INBOUND",
                    Origen = "INDIVIDUAL",
                    CodigoArticulo = model.Codigo,
                    OrdenNumero = model.OrdenNumero,
                    Cantidad = Convert.ToDecimal(model.Cantidad),
                    PayloadJson = JsonConvert.SerializeObject(payload),
                    WsStatus = wsStatus,
                    WsRespuesta = wsMensaje,
                    TotalRegistros = 1
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> ParsearCsvInbound(IFormFile archivo)
        {
            try
            {
                if (archivo == null || archivo.Length == 0)
                    return Json(new { ok = false, mensaje = "No se recibió ningún archivo." });

                var registros = new List<object>();
                var errores = new List<string>();
                int fila = 0;

                using (var stream = archivo.OpenReadStream())
                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    string linea;
                    while ((linea = await reader.ReadLineAsync()) != null)
                    {
                        fila++;
                        if (fila == 1 || string.IsNullOrWhiteSpace(linea)) continue;

                        var cols = linea.Split(',');
                        // Mínimo 6 columnas obligatorias; col 7 = Lote, col 8 = LineaHost (opcionales)
                        if (cols.Length < 6)
                        {
                            errores.Add($"Fila {fila}: columnas insuficientes ({cols.Length}/6).");
                            continue;
                        }

                        var ordenNumero = cols[0].Trim();
                        var ordenDesc = cols[1].Trim();
                        var codigo = cols[2].Trim();
                        var descripcion = cols[3].Trim();
                        var unidad = cols[4].Trim().ToUpper();
                        var cantidadStr = cols[5].Trim();
                        var lote = cols.Length > 6 ? cols[6].Trim() : "";
                        var lineaHostStr = cols.Length > 7 ? cols[7].Trim() : "";

                        if (string.IsNullOrEmpty(ordenNumero)) { errores.Add($"Fila {fila}: OrdenNumero vacío."); continue; }
                        if (string.IsNullOrEmpty(codigo)) { errores.Add($"Fila {fila}: Codigo vacío."); continue; }
                        if (string.IsNullOrEmpty(descripcion)) { errores.Add($"Fila {fila}: Descripcion vacía."); continue; }
                        if (string.IsNullOrEmpty(unidad)) { errores.Add($"Fila {fila}: Unidad vacía."); continue; }

                        if (!decimal.TryParse(cantidadStr,
                                NumberStyles.Any,
                                CultureInfo.InvariantCulture,
                                out decimal cantidad) || cantidad <= 0)
                        {
                            errores.Add($"Fila {fila}: Cantidad inválida ('{cantidadStr}').");
                            continue;
                        }

                        registros.Add(new
                        {
                            OrdenNumero = ordenNumero,
                            OrdenDescripcion = ordenDesc,
                            Codigo = codigo,
                            Descripcion = descripcion,
                            Unidad = unidad,
                            Cantidad = cantidad,
                            Lote = lote,
                            LineaHost = lineaHostStr
                        });
                    }
                }

                return Json(new
                {
                    ok = true,
                    registros,
                    errores,
                    totalOk = registros.Count,
                    totalErr = errores.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error al parsear CSV: " + ex.Message });
            }
        }

        // ── Inbound masivo — paso 2: enviar al WS ──────────────────
        [HttpPost]
        public JsonResult EnviarInboundMasivo(List<TestModulaViewModel> registros)
        {
            if (registros == null || registros.Count == 0)
                return Json(new { ok = false, mensaje = "No hay registros para enviar." });

            string wsStatus = "ERROR", wsMensaje = "";
            bool exito = false;

            try
            {
                var client = new ServiceReference2.InterfaceClient();
                client.InnerChannel.OperationTimeout = TimeSpan.FromSeconds(60);

                var orders = registros.Select(m => new ServiceReference2.Order
                {
                    ORD_ORDINE = m.OrdenNumero,
                    ORD_DES = m.OrdenDescripcion,
                    RIG_ARTICOLO = m.Codigo,
                    RIG_QTAR = m.Cantidad,
                    ART_DES = m.Descripcion,
                    ART_UMI = m.Unidad,
                    RIG_SUB1 = m.Lote,
                    RIG_HOSTINF = m.LineaHost
                }).ToArray();

                var response = client.postInbound(GetLogin(), orders);
                CerrarCliente(client);

                exito = response.All(r => r.Status == "OK");
                wsStatus = exito ? "OK" : "ERROR";
                wsMensaje = string.Join(" | ", response.Select((r, i) =>
                    $"[{i + 1}] Status:{r.Status} Msg:{r.Message} Rows:{r.Rows}"));

                return Json(new { ok = exito, mensaje = wsMensaje, total = registros.Count });
            }
            catch (Exception ex)
            {
                wsMensaje = "ERROR: " + ex.Message;
                return Json(new { ok = false, mensaje = wsMensaje });
            }
            finally
            {
                foreach (var m in registros)
                {
                    GuardarAuditoria(new ModulaAuditoria
                    {
                        Fecha = DateTime.Now,
                        Usuario = UsuarioActual,
                        TipoAccion = "INBOUND",
                        Origen = "CSV",
                        CodigoArticulo = m.Codigo,
                        OrdenNumero = m.OrdenNumero,
                        Cantidad = Convert.ToDecimal(m.Cantidad),
                        PayloadJson = JsonConvert.SerializeObject(m),
                        WsStatus = wsStatus,
                        WsRespuesta = wsMensaje,
                        TotalRegistros = registros.Count
                    });
                }
            }
        }


        // ══════════════════════════════════════════════════════════════
        //  MAINTENANCE — individual
        // ══════════════════════════════════════════════════════════════
        [HttpPost]
        public JsonResult EnviarMaintenance(TestModulaViewModel model)
        {
            var payload = new
            {
                model.OrdenNumero,
                model.OrdenDescripcion,
                model.Codigo,
                model.Descripcion,
                model.Unidad,
                model.Cantidad,
                model.Lote,
                model.LineaHost,
                model.Nota,
                model.Prioridad
            };
            string wsStatus = "ERROR", wsMensaje = "";
            bool exito = false;

            try
            {
                var client = new ServiceReference2.InterfaceClient();
                client.InnerChannel.OperationTimeout = TimeSpan.FromSeconds(30);

                var orders = new[]
                {
                    new ServiceReference2.Maintenance
                    {
                        ORD_ORDINE   = model.OrdenNumero,
                        ORD_DES      = model.OrdenDescripcion,
                        RIG_ARTICOLO = model.Codigo,
                        RIG_QTAR     = model.Cantidad,
                        //ART_DES      = model.Descripcion,
                        //ART_UMI      = model.Unidad,
                        RIG_SUB1     = model.Lote,
                        RIG_HOSTINF  = model.LineaHost,
                        RIG_REQ_NOTE = model.Nota,
                        RIG_PRIO     = model.Prioridad
                    }
                };

                var response = client.postMaintenance(GetLogin(), orders);
                CerrarCliente(client);

                exito = response.All(r => r.Status == "OK");
                wsStatus = exito ? "OK" : "ERROR";
                wsMensaje = string.Join(" | ", response.Select(r =>
                    $"Status:{r.Status} Msg:{r.Message} Rows:{r.Rows}"));

                return Json(new { ok = exito, mensaje = wsMensaje });
            }
            catch (Exception ex)
            {
                wsMensaje = "ERROR: " + ex.Message;
                return Json(new { ok = false, mensaje = wsMensaje });
            }
            finally
            {
                GuardarAuditoria(new ModulaAuditoria
                {
                    Fecha = DateTime.Now,
                    Usuario = UsuarioActual,
                    TipoAccion = "MAINTENANCE",
                    Origen = "INDIVIDUAL",
                    CodigoArticulo = model.Codigo,
                    OrdenNumero = model.OrdenNumero,
                    Cantidad = Convert.ToDecimal(model.Cantidad),
                    PayloadJson = JsonConvert.SerializeObject(payload),
                    WsStatus = wsStatus,
                    WsRespuesta = wsMensaje,
                    TotalRegistros = 1
                });
            }
        }

        // ── Parsear CSV Maintenance ─────────────────────────────────
        [HttpPost]
        public async Task<JsonResult> ParsearCsvMaintenance(IFormFile archivo)
        {
            try
            {
                if (archivo == null || archivo.Length == 0)
                    return Json(new { ok = false, mensaje = "No se recibió ningún archivo." });

                var registros = new List<object>();
                var errores = new List<string>();
                int fila = 0;

                using (var stream = archivo.OpenReadStream())
                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    string linea;
                    while ((linea = await reader.ReadLineAsync()) != null)
                    {
                        fila++;
                        if (fila == 1 || string.IsNullOrWhiteSpace(linea)) continue;

                        // Columnas: OrdenNumero, OrdenDesc, Codigo, Descripcion, Unidad,
                        //           Cantidad, LineaHost, Lote, Nota, Prioridad
                        var cols = linea.Split(',');
                        if (cols.Length < 7)   // LineaHost es obligatorio
                        {
                            errores.Add($"Fila {fila}: columnas insuficientes ({cols.Length}/7).");
                            continue;
                        }

                        var ordenNumero = cols[0].Trim();
                        var ordenDesc = cols[1].Trim();
                        var codigo = cols[2].Trim();
                        var descripcion = cols[3].Trim();
                        var unidad = cols[4].Trim().ToUpper();
                        var cantidadStr = cols[5].Trim();
                        var lineaHostStr = cols[6].Trim();
                        var lote = cols.Length > 7 ? cols[7].Trim() : "";
                        var nota = cols.Length > 8 ? cols[8].Trim() : "";
                        var prioridadStr = cols.Length > 9 ? cols[9].Trim() : "";

                        if (string.IsNullOrEmpty(ordenNumero)) { errores.Add($"Fila {fila}: OrdenNumero vacío."); continue; }
                        if (string.IsNullOrEmpty(codigo)) { errores.Add($"Fila {fila}: Codigo vacío."); continue; }
                        if (string.IsNullOrEmpty(descripcion)) { errores.Add($"Fila {fila}: Descripcion vacía."); continue; }
                        if (string.IsNullOrEmpty(unidad)) { errores.Add($"Fila {fila}: Unidad vacía."); continue; }
                        if (string.IsNullOrEmpty(lineaHostStr)) { errores.Add($"Fila {fila}: LineaHost vacío."); continue; }

                        if (!decimal.TryParse(cantidadStr,
                                NumberStyles.Any,
                                CultureInfo.InvariantCulture,
                                out decimal cantidad) || cantidad <= 0)
                        {
                            errores.Add($"Fila {fila}: Cantidad inválida ('{cantidadStr}').");
                            continue;
                        }

                        registros.Add(new
                        {
                            OrdenNumero = ordenNumero,
                            OrdenDescripcion = ordenDesc,
                            Codigo = codigo,
                            Descripcion = descripcion,
                            Unidad = unidad,
                            Cantidad = cantidad,
                            LineaHost = lineaHostStr,
                            Lote = lote,
                            Nota = nota,
                            Prioridad = prioridadStr
                        });
                    }
                }

                return Json(new
                {
                    ok = true,
                    registros,
                    errores,
                    totalOk = registros.Count,
                    totalErr = errores.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error al parsear CSV: " + ex.Message });
            }
        }

        // ── Maintenance masivo ──────────────────────────────────────
        [HttpPost]
        public JsonResult EnviarMaintenanceMasivo(List<TestModulaViewModel> registros)
        {
            if (registros == null || registros.Count == 0)
                return Json(new { ok = false, mensaje = "No hay registros para enviar." });

            string wsStatus = "ERROR", wsMensaje = "";
            bool exito = false;

            try
            {
                var client = new ServiceReference2.InterfaceClient();
                client.InnerChannel.OperationTimeout = TimeSpan.FromSeconds(60);

                var orders = registros.Select(m => new ServiceReference2.Maintenance
                {
                    ORD_ORDINE = m.OrdenNumero,
                    ORD_DES = m.OrdenDescripcion,
                    RIG_ARTICOLO = m.Codigo,
                    RIG_QTAR = m.Cantidad,
                    //ART_DES = m.Descripcion,
                    //ART_UMI = m.Unidad,
                    RIG_SUB1 = m.Lote,
                    RIG_HOSTINF = m.LineaHost,
                    RIG_REQ_NOTE = m.Nota,
                    RIG_PRIO = m.Prioridad
                }).ToArray();

                var response = client.postMaintenance(GetLogin(), orders);
                CerrarCliente(client);

                exito = response.All(r => r.Status == "OK");
                wsStatus = exito ? "OK" : "ERROR";
                wsMensaje = string.Join(" | ", response.Select((r, i) =>
                    $"[{i + 1}] Status:{r.Status} Msg:{r.Message} Rows:{r.Rows}"));

                return Json(new { ok = exito, mensaje = wsMensaje, total = registros.Count });
            }
            catch (Exception ex)
            {
                wsMensaje = "ERROR: " + ex.Message;
                return Json(new { ok = false, mensaje = wsMensaje });
            }
            finally
            {
                foreach (var m in registros)
                {
                    GuardarAuditoria(new ModulaAuditoria
                    {
                        Fecha = DateTime.Now,
                        Usuario = UsuarioActual,
                        TipoAccion = "MAINTENANCE",
                        Origen = "CSV",
                        CodigoArticulo = m.Codigo,
                        OrdenNumero = m.OrdenNumero,
                        Cantidad = Convert.ToDecimal(m.Cantidad),
                        PayloadJson = JsonConvert.SerializeObject(m),
                        WsStatus = wsStatus,
                        WsRespuesta = wsMensaje,
                        TotalRegistros = registros.Count
                    });
                }
            }
        }

        // ── Historial / Auditoría ───────────────────────────────────
        public IActionResult Historial(ModulaAuditoriaFiltro filtro)
        {
            var parameters = new Dictionary<string, object>();
            var where = new List<string> { "1=1" };

            if (filtro.Desde.HasValue)
            {
                where.Add("fecha >= @desde");
                parameters.Add("desde", filtro.Desde.Value.Date);
            }

            if (filtro.Hasta.HasValue)
            {
                where.Add("fecha < @hasta");
                parameters.Add("hasta", filtro.Hasta.Value.Date.AddDays(1));
            }

            if (!string.IsNullOrWhiteSpace(filtro.Usuario))
            {
                where.Add("usuario ILIKE @usuario");
                parameters.Add("usuario", "%" + filtro.Usuario + "%");
            }

            if (!string.IsNullOrWhiteSpace(filtro.TipoAccion))
            {
                where.Add("tipo_accion = @tipo");
                parameters.Add("tipo", filtro.TipoAccion);
            }

            if (!string.IsNullOrWhiteSpace(filtro.WsStatus))
            {
                where.Add("ws_status = @status");
                parameters.Add("status", filtro.WsStatus);
            }

            string query = $@"
        SELECT id, fecha, usuario, tipo_accion, origen,
               codigo_articulo, orden_numero, cantidad,
               payload_json, ws_status, ws_respuesta, total_registros
        FROM modula_auditoria
        WHERE {string.Join(" AND ", where)}
        ORDER BY fecha DESC
        LIMIT 500";

            var result = RunQuery(query, parameters);

            foreach (var dr in result)
            {
                filtro.Registros.Add(new ModulaAuditoria
                {
                    Id = dr["id"] != null ? Convert.ToInt32(dr["id"]) : 0,
                    Fecha = dr["fecha"] != null ? Convert.ToDateTime(dr["fecha"]) : DateTime.MinValue,
                    Usuario = dr["usuario"]?.ToString(),
                    TipoAccion = dr["tipo_accion"]?.ToString(),
                    Origen = dr["origen"]?.ToString(),
                    CodigoArticulo = dr["codigo_articulo"]?.ToString(),
                    OrdenNumero = dr["orden_numero"]?.ToString(),
                    Cantidad = dr["cantidad"] != null ? (decimal?)Convert.ToDecimal(dr["cantidad"]) : null,
                    PayloadJson = dr["payload_json"]?.ToString(),
                    WsStatus = dr["ws_status"]?.ToString(),
                    WsRespuesta = dr["ws_respuesta"]?.ToString(),
                    TotalRegistros = dr["total_registros"] != null ? Convert.ToInt32(dr["total_registros"]) : 0
                });
            }
            return View(filtro);
        }

        private static void CerrarCliente(ServiceReference2.InterfaceClient client)
        {
            if (client.State == CommunicationState.Faulted)
                client.Abort();
            else
                client.Close();
        }
    }
}