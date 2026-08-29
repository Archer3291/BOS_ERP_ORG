using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using System.Data;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers.Facturacion.Productos;
using Microsoft.Extensions.Options;
using BOS_ERP.Models.Options;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using BOS_ERP.services.Facturacion;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    [RightAuthorize("facturacion_global")]
    public class FacturaGlobalConsultaController : FacturacionVentaController
    {
        // Destino de la refacturación, tal como lo manda la vista en el campo "tipo".
        private const string TipoRefacturacionCliente = "factura_cliente";

        // tipo_proceso del documento que se va a timbrar. No es una etiqueta: junto con el
        // tipo de documento es la llave con la que PolizaConfigFactory busca la plantilla
        // contable en poliza_documento. Si no hay fila para esa combinación, la emisión
        // falla con "No hay pólizas configuradas para este documento".
        // Antes iba fijo en "factura_sucursal", que no tiene plantilla configurada.
        private const string TipoProcesoFacturaContado = "factura_contado";
        private const string TipoProcesoFacturaGlobal = "factura_global";

        public FacturaGlobalConsultaController(IOptions<TimbradoOptions> timbradoOptions, IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider, IWebHostEnvironment env, XmlBuilderService xmlService) : base(timbradoOptions, viewEngine, tempDataProvider, env, xmlService)
        {
        }

        // Naturalezas que puede tener una factura global. GLFAC es el tipo propio que se
        // creó para ella (tpdoc 91); VSFAC queda por las que se emitieron antes de que
        // existiera, cuando compartía documento con la factura nominativa de sucursal.
        private static readonly string[] NatsFacturaGlobal = { "GLFAC", "VSFAC" };

        // Un solo predicado para el listado y para el conteo: si se separan, la paginación
        // acaba mostrando un total que no corresponde a las filas devueltas.
        private const string FiltroFacturaGlobal = @"
            WHERE em.tipo_proceso = 'factura_global'
              AND em.nat = ANY(@nats)
              -- encabezadomov no guarda empresa: se acota por la del cliente receptor.
              AND EXISTS (
                    SELECT 1 FROM catclientes cc
                    WHERE cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id)
              AND (@nombre = ''
                   OR LOWER(fa.serie)      LIKE LOWER('%' || @nombre || '%')
                   OR LOWER(fa.rfccliente) LIKE LOWER('%' || @nombre || '%')
                   OR LOWER(fa.rsocliente) LIKE LOWER('%' || @nombre || '%')
                   OR LOWER(fa.uuid::text) LIKE LOWER('%' || @nombre || '%')
                   OR LOWER(em.folio)      LIKE LOWER('%' || @nombre || '%'))";

        public IActionResult ObtenerFacturas(string nombre, int page = 1, int pageSize = 50)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 200) pageSize = 50;

            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre ?? "" },
                { "nats", NatsFacturaGlobal },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
            };

            // El folio se toma de la columna generada de encabezadomov (VSUC-GLFAC-26-1) en
            // vez de rearmarlo a mano: la versión anterior usaba el año a cuatro dígitos y
            // no coincidía con el folio que se ve en el resto del sistema.
            string query = $@"
            SELECT
                fa.id, fa.serie, fa.folio AS fac_folio, fa.idtipofactura, fa.idcliente,
                fa.rfccliente, fa.rsocliente, fa.emlcliente, fa.idemisor, fa.rfcemisor,
                fa.rsoemisor, fa.idexpedicion, fa.idusuario, fa.fecha, fa.fechatimbrado,
                fa.statusfactura, fa.mdpfactura, fa.textfactura, fa.idlugarexp,
                fa.idtipopago, fa.uuid, fa.importe, fa.descuento, fa.subtotal, fa.iva,
                fa.total, fa.saldo, fa.idpedido, fa.retisr, fa.retiva, fa.moneda,
                fa.observaciones, fa.idvendedor, fa.usocfdi, fa.idusocfdi, fa.cbb,
                fa.parcialidad, fa.sellosat, fa.sellocfdi, fa.cadenaoriginal, fa.oc,
                fa.tdc, fa.anticipo, fa.reg_fisr, fa.reg_fise, fa.cpr, fa.cpe, fa.tipo,
                fa.encabezado_id,
                em.refe, em.suc, em.nat, em.fch, em.variacion,
                em.folio || CASE WHEN em.variacion > 0
                                 THEN '-' || num_to_letters(em.variacion)
                                 ELSE '' END AS folio
            FROM factura fa
            INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
            {FiltroFacturaGlobal}
            ORDER BY fa.id DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            string queryTotal = $@"
            SELECT COUNT(*)
            FROM factura fa
            INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
            {FiltroFacturaGlobal}";

            var total = RunScalar(queryTotal, parameters);

            return Json(new { data, total });
        }

        /// <summary>
        /// Documentos de venta que quedaron consolidados en una factura. Los origina el
        /// punto de venta marcándolos con encabezado_hijo = id de la factura.
        /// </summary>
        /// <param name="procesoId">
        /// Cuando se envía, se excluyen los documentos que ya fueron refacturados en ese
        /// proceso. El asistente lo usa para ofrecer sólo los pendientes: los documentos
        /// origen conservan su encabezado_hijo apuntando a la global cancelada, así que sin
        /// este filtro el que ya se le facturó al cliente volvía a aparecer en la lista y
        /// podía terminar incluido otra vez en la global de reemplazo.
        /// </param>
        public IActionResult FacturaDetail(int id, int? procesoId = null)
        {
            var yaRefacturados = ObtenerOrigenesRefacturados(procesoId);

            // COALESCE en los dos json_agg: sin ellos un documento sin partidas devolvía
            // 'partidas': null y el modal reventaba al recorrerlas, y una factura sin
            // documentos ligados devolvía NULL en vez de una lista vacía, que la vista
            // presentaba como "error al cargar" en lugar de "sin documentos".
            // El self-join con encabezadomov sobraba: basta filtrar por encabezado_hijo.
            string query = @"
                SELECT COALESCE(json_agg(d ORDER BY d.id_hijo), '[]'::json) AS documento
                FROM (
                    SELECT
                        em.id_encabezado AS id_hijo,
                        em.gen,
                        em.nat,
                        em.fol_doc  AS folio,
                        em.folio    AS folio_completo,
                        em.fch      AS fecha,
                        em.cli_prov AS cliente,
                        COALESCE((
                            SELECT json_agg(json_build_object(
                                'id_partidas', p.id_partidas,
                                'nro_part',    p.nro_part,
                                'cve_prod',    p.cve_prod,
                                'descr_prod',  p.descr_prod,
                                'cant_ud',     p.cant_ud,
                                'ud',          p.ud,
                                'pv_prod',     p.pv_prod,
                                'dto1',        p.dto1,
                                'imp_part',    p.imp_part,
                                'iva',         p.iva,
                                'ieps',        p.ieps,
                                'ccy',         p.ccy
                            ) ORDER BY p.nro_part)
                            FROM partidasdoc p
                            WHERE p.encabezado_id = em.id_encabezado
                        ), '[]'::json) AS partidas
                    FROM encabezadomov em
                    WHERE em.encabezado_hijo = @id
                      AND NOT (em.id_encabezado = ANY(@refacturados))
                ) d;";

            var parameters = new Dictionary<string, object>
            {
                { "id", id },
                { "refacturados", yaRefacturados }
            };
            var result = RunQuery(query, parameters);

            // Se informa cuántos documentos quedaron fuera por tener ya su nominativa: el
            // asistente lo usa para saber si está en la selección inicial (donde hay que
            // apartar el documento del cliente) o continuando un proceso ya arrancado
            // (donde todo lo que queda va a la global).
            foreach (var fila in result)
                fila["ya_refacturados"] = yaRefacturados.Length;

            return Json(result);
        }

        /// <summary>
        /// Documentos origen que ya tienen un CFDI nominativo emitido dentro del proceso.
        /// Salen de la bitácora: cada variación timbrada guarda de qué documento partió.
        /// </summary>
        private int[] ObtenerOrigenesRefacturados(int? procesoId)
        {
            if (!procesoId.HasValue || procesoId.Value <= 0)
                return Array.Empty<int>();

            var fila = RunQuery(
                @"SELECT COALESCE(
                      array_agg(DISTINCT (v ->> 'id_documento_origen')::int)
                          FILTER (WHERE v ->> 'estado' = 'TIMBRADO'
                                    AND v ->> 'id_documento_origen' IS NOT NULL),
                      '{}') AS origenes
                  FROM control_proceso_cancelacion cpc
                  CROSS JOIN LATERAL jsonb_array_elements(
                      COALESCE(cpc.variaciones_creadas, '[]'::jsonb)) AS v
                  WHERE cpc.id = @procesoId;",
                new Dictionary<string, object> { { "procesoId", procesoId.Value } })
                .FirstOrDefault();

            return fila?["origenes"] as int[] ?? Array.Empty<int>();
        }


        [HttpPost]
        public async Task<IActionResult> EnviarEmail()
        {
            try
            {
                // Obtener valores del FormData
                string facturaId = Request.Form["facturaId"].ToString();
                string email = Request.Form["email"].ToString();
                string asunto = Request.Form["asunto"].ToString();
                string mensaje = Request.Form["mensaje"].ToString();

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(asunto))
                {
                    return Json(new { success = false, message = "Asunto y destinatario son obligatorios." });
                }

                // Rutas base separadas para XML y PDF
                string xmlBasePath = Path.Combine("~/Facturacion/xml_timbrados/");
                string pdfBasePath = Path.Combine("~/Facturacion/facturas/");

                string xmlPath = Path.Combine(xmlBasePath, $"{facturaId}.xml");
                string pdfPath = Path.Combine(pdfBasePath, $"{facturaId}.pdf");

                // Crear el correo
                MailMessage mail = new MailMessage();
                mail.From = new MailAddress("BOS@sellosyretenes.com", "Sistema de Validación");
                mail.To.Add(email);
                mail.Subject = asunto;
                mail.Body = string.IsNullOrWhiteSpace(mensaje)
                    ? "Se adjuntan los archivos correspondientes a la factura."
                    : mensaje;
                mail.IsBodyHtml = false;

                // Adjuntar archivos de forma segura
                if (System.IO.File.Exists(xmlPath))
                {
                    var xmlStream = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    mail.Attachments.Add(new Attachment(xmlStream, Path.GetFileName(xmlPath)));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ XML no encontrado: " + xmlPath);
                }

                if (System.IO.File.Exists(pdfPath))
                {
                    var pdfStream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    mail.Attachments.Add(new Attachment(pdfStream, Path.GetFileName(pdfPath)));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ PDF no encontrado: " + pdfPath);
                }

                // Configurar SMTP (usa la configuración de web.config)
                using (var smtp = new SmtpClient())
                {
                    await smtp.SendMailAsync(mail);
                }

                // Liberar streams
                foreach (var att in mail.Attachments)
                {
                    att.ContentStream.Dispose();
                }

                return Json(new { success = true, message = "Correo enviado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al enviar el correo.",
                    error = ex.Message
                });
            }
        }


        [HttpPost]
        public IActionResult CancelarFacturaAjax(string rfcEmisor, string uuid, string motivo, string folioSustitucion = "")
        {
            try
            {
                var resultado = CancelarFactura(rfcEmisor, uuid, motivo, folioSustitucion);
                return Json(new { success = true, message = resultado });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult VerificarEstadoProceso(string uuid, int encabezadoId)
        {
            try
            {
                var controlHelper = new ControlProcesoHelper(RunQueryWrapper, RunUpdateWrapper);

                // Verificar si existe un proceso
                var estadoExistente = controlHelper.ObtenerEstadoProceso(uuid, encabezadoId);

                if (estadoExistente != null)
                {
                    int procesoId = Convert.ToInt32(estadoExistente["id"]);
                    var verificacion = controlHelper.VerificarEstadoReanudacion(procesoId);
                    var resumen = controlHelper.ObtenerResumenProceso(procesoId);

                    // ⚡ VALIDACIÓN CRÍTICA: SI EL PROCESO YA ESTÁ COMPLETADO
                    if (resumen["estadoProceso"].ToString() == "COMPLETADO")
                    {
                        return Json(new
                        {
                            success = false,
                            yaCompletado = true,
                            bloqueado = true,
                            message = "Esta factura ya fue cancelada y refacturada completamente. No se puede volver a procesar.",
                            procesoId = procesoId,
                            detalles = new
                            {
                                fechaCompletado = estadoExistente["fecha_completado"],
                                facturaGlobalId = resumen.ContainsKey("facturaGlobalId") ? resumen["facturaGlobalId"] : null,
                                facturaGlobalUUID = resumen.ContainsKey("facturaGlobalUUID") ? resumen["facturaGlobalUUID"] : null,
                                totalRefacturaciones = resumen.ContainsKey("totalRefacturaciones") ? resumen["totalRefacturaciones"] : 0,
                                refacturacionesExitosas = resumen.ContainsKey("refacturacionesExitosas") ? resumen["refacturacionesExitosas"] : 0
                            },
                            resumen = resumen
                        });
                    }

                    var yaCanceladoSAT = Convert.ToBoolean(resumen["canceladoSAT"]);

                    // ⚡ SI YA SE CANCELÓ EN EL SAT
                    if (yaCanceladoSAT)
                    {
                        return Json(new
                        {
                            success = true,
                            yaCancelado = true,
                            puedeReanudar = verificacion.PuedeReanudar,
                            message = verificacion.Mensaje,
                            acusePath = estadoExistente["acuse_cancelacion_path"]?.ToString(),
                            procesoId = procesoId,
                            estadoActual = resumen["estadoProceso"],
                            pasoActual = resumen["pasoActual"],
                            pasoReanudar = verificacion.PasoReanudar,
                            resumen = resumen
                        });
                    }

                    // ⚡ Proceso iniciado pero no cancelado en SAT
                    return Json(new
                    {
                        success = true,
                        procesoExistente = true,
                        canceladoSAT = false,
                        message = "Proceso encontrado, se puede continuar con la cancelación",
                        procesoId = procesoId,
                        estadoActual = resumen["estadoProceso"],
                        pasoActual = resumen["pasoActual"],
                        resumen = resumen
                    });
                }

                // ⚡ No existe proceso previo
                return Json(new
                {
                    success = true,
                    procesoExistente = false,
                    canceladoSAT = false,
                    message = "No existe proceso previo, se puede iniciar cancelación",
                    procesoId = (int?)null
                });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog("VerificarEstadoProceso", uuid, ex.ToString(), User.Identity.Name);

                return Json(new
                {
                    success = false,
                    message = "Error al verificar el estado: " + ex.Message
                });
            }
        }

        [HttpPost]
        public JsonResult CancelarFactura(string rfcEmisor, string uuid, string motivo, string folioSustitucion = "", int? encabezadoId = null, int? procesoId = null)
        {
            var controlHelper = new ControlProcesoHelper(RunQueryWrapper, RunUpdateWrapper);
            int currentProcesoId = procesoId ?? 0;

            try
            {
                if (!encabezadoId.HasValue)
                {
                    return Json(new { success = false, message = "Se requiere el ID del encabezado." });
                }

                // ============================================
                // INICIAR O RECUPERAR PROCESO
                // ============================================
                if (currentProcesoId == 0)
                {
                    // Crear nuevo proceso
                    currentProcesoId = controlHelper.IniciarProceso(uuid, encabezadoId.Value, rfcEmisor, User.Identity.Name);
                }

                // ============================================
                // VERIFICAR SI YA SE CANCELÓ EN SAT (por seguridad)
                // ============================================
                if (controlHelper.YaCanceladoEnSAT(uuid, encabezadoId.Value))
                {
                    var estado = controlHelper.ObtenerEstadoProceso(uuid, encabezadoId.Value);
                    return Json(new
                    {
                        success = true,
                        yaCancelado = true,
                        message = "Esta factura ya fue cancelada en el SAT anteriormente",
                        procesoId = currentProcesoId,
                        acusePath = estado["acuse_cancelacion_path"]?.ToString()
                    });
                }

                // ============================================
                // CANCELAR EN EL SAT
                // ============================================
                controlHelper.ActualizarEstado(currentProcesoId,
                    ControlProcesoHelper.EstadoProceso.INICIADO,
                    1,
                    "Iniciando cancelación en SAT");

                // Estaban escritas en el código: al cambiar de PAC o de contraseña había que
                // recompilar, y quedaban versionadas en el repositorio.
                var (timbradoUser, timbradoPass) = ObtenerCredencialesPac();

                using (var client = new ServiceReference1.TimbradoServiceClient())
                {
                    client.ClientCredentials.UserName.UserName = timbradoUser;
                    client.ClientCredentials.UserName.Password = timbradoPass;
                    client.Open();

                    var response = client.CancelarTest(rfcEmisor, uuid, motivo, folioSustitucion);

                    if (response == null)
                    {
                        controlHelper.RegistrarError(currentProcesoId, "No se recibió respuesta del servicio de cancelación", 1);
                        return Json(new
                        {
                            success = false,
                            message = "No se recibió respuesta del servicio de cancelación.",
                            procesoId = currentProcesoId
                        });
                    }

                    bool cancelacionExitosa = response.message?.ToLower().Contains("satisfactoriamente") == true;

                    if (!cancelacionExitosa)
                    {
                        string errorMsg = $"PAC Error: {response.message}";
                        LogErrorHelper.RegistrarLog("CancelacionFactura", uuid, errorMsg, User.Identity.Name);
                        controlHelper.RegistrarError(currentProcesoId, errorMsg, 1);

                        return Json(new
                        {
                            success = false,
                            message = "El PAC devolvió un error: " + response.message,
                            procesoId = currentProcesoId,
                            errorPAC = true
                        });
                    }

                    // ✅ CANCELACIÓN EXITOSA EN EL SAT
                    try
                    {
                        string carpeta = Path.Combine("~/Facturacion/Cancelaciones/");
                        if (!Directory.Exists(carpeta))
                            Directory.CreateDirectory(carpeta);

                        string rutaArchivo = Path.Combine(carpeta, $"{uuid}_AcuseCancelacion.xml");
                        System.IO.File.WriteAllText(rutaArchivo, response.acuse);
                        string rutaWeb = Url.Content($"~/Facturacion/Cancelaciones/{uuid}_AcuseCancelacion.xml");

                        controlHelper.RegistrarCancelacionSAT(currentProcesoId, rutaWeb, motivo, folioSustitucion);

                        // Actualizar estado en BD
                        string query = "UPDATE factura SET statusfactura='Cancelada' WHERE uuid = @uuid";
                        RunUpdate(query, new Dictionary<string, object> { { "uuid", Guid.Parse(uuid) } });

                        query = "UPDATE polizas SET cancelada=true WHERE referencia = (SELECT encabezado_id FROM factura WHERE uuid = @uuid)";
                        RunUpdate(query, new Dictionary<string, object> { { "uuid", Guid.Parse(uuid) } });

                        query = "UPDATE cartera_clientes SET cancelada=true WHERE factura = (SELECT encabezado_id FROM factura WHERE uuid = @uuid)";
                        RunUpdate(query, new Dictionary<string, object> { { "uuid", Guid.Parse(uuid) } });

                    }
                    catch (Exception exLocal)
                    {
                        string errorMsg = $"Cancelada ante SAT, pero error al guardar acuse: {exLocal.Message}";
                        LogErrorHelper.RegistrarLog("CancelacionFactura", uuid, exLocal.ToString(), User.Identity.Name);
                        controlHelper.RegistrarError(currentProcesoId, errorMsg, 1);

                        return Json(new
                        {
                            success = true,
                            warning = true,
                            message = errorMsg,
                            procesoId = currentProcesoId,
                            uuid,
                            acusePath = ""
                        });
                    }
                }

                // ✅ Proceso completado exitosamente
                var estadoActualizado = controlHelper.ObtenerEstadoProceso(uuid, encabezadoId.Value);
                var resumenFinal = controlHelper.ObtenerResumenProceso(currentProcesoId);

                return Json(new
                {
                    success = true,
                    message = "Cancelación ante SAT exitosa. Puede continuar con la refacturación.",
                    uuid,
                    procesoId = currentProcesoId,
                    acusePath = estadoActualizado["acuse_cancelacion_path"]?.ToString(),
                    yaCancelado = false, // 🔹 Es la primera vez que se cancela
                    pasoActual = Convert.ToInt32(estadoActualizado["paso_actual"]),
                    resumen = resumenFinal
                });
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error general al cancelar la factura: {ex.Message}";
                LogErrorHelper.RegistrarLog("CancelacionFactura", uuid, ex.ToString(), User.Identity.Name);

                if (currentProcesoId > 0)
                {
                    controlHelper.RegistrarError(currentProcesoId, errorMsg, 1);
                }

                return Json(new
                {
                    success = false,
                    message = errorMsg,
                    procesoId = currentProcesoId,
                    stackTrace = ex.StackTrace
                });
            }
        }
        // Paso interno del proceso de refacturación: es público sólo para que lo llame
        // ProcesarDocumentosAsync. Sin [NonAction] MVC lo publicaba como endpoint y
        // cualquiera podía clonar documentos saltándose el asistente y la cancelación.
        [NonAction]
        public async Task<(bool Success, string Message, object Data, object Data2, int ProcesoId)> GuardarDesdeDocumentos(
    string ids,
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    string tipo = null,
    string global = null,
    int? procesoId = null)
        {
            // ControlProcesoHelper escribe por su propia conexión, a propósito: es la
            // bitácora que permite reanudar el proceso, y debe sobrevivir al rollback de
            // los documentos. Lo que se deshace es el dato, no la traza.
            var controlHelper = new ControlProcesoHelper(RunQueryWrapper, RunUpdateWrapper);
            var variacionesCreadas = new List<int>(); // IDs de documentos creados en ESTA ejecución
            var variacion = new Dictionary<string, object>();
            int currentProcesoId = procesoId ?? 0;

            try
            {
                // ============================================
                // PASO 1: Validar y procesar IDs
                // ============================================
                var idsOrigen = ids.Split(',')
                    .Select(id => int.TryParse(id.Trim(), out int val) ? val : 0)
                    .Where(val => val > 0)
                    .ToList();

                if (!idsOrigen.Any())
                    return (false, "No se recibieron IDs válidos", null, null, currentProcesoId);

                // Los dos destinos de la refacturación: la nominativa que pide el cliente
                // (arranca del documento de venta) y la global de reemplazo (arranca de la
                // factura global que se acaba de cancelar).
                bool esFacturaCliente = tipo == TipoRefacturacionCliente;

                int idEncabezadoOrigen = esFacturaCliente
                    ? idsOrigen.First()
                    : Convert.ToInt32(global);


                if (currentProcesoId > 0)
                {
                    controlHelper.ActualizarEstado(currentProcesoId,
                        ControlProcesoHelper.EstadoProceso.CANCELADO_SAT,
                        2,
                        "Procesando documentos para refacturación");
                }




                // ============================================
                // PASO 2: Obtener encabezado y cliente
                // ============================================
                string queryEnc = "SELECT * FROM encabezadomov WHERE id_encabezado = @id;";
                var encabezadoData = RunQuery(queryEnc,
                    new Dictionary<string, object> { { "@id", idEncabezadoOrigen } }, false, conn, tx).FirstOrDefault();

                // empresa_id existe en catclientes Y en direcciones_facturacion: sin calificar,
                // Postgres responde 42702 «la referencia a la columna empresa_id es ambigua».
                // Se acota por la empresa en las dos tablas para no cruzar la dirección de
                // facturación de una empresa con el cliente de otra.
                var cli = RunQuery(
                    @"SELECT * FROM catclientes cc
              INNER JOIN direcciones_facturacion df
                 ON df.entidad_clave = cc.cve_cli
                AND df.empresa_id = cc.empresa_id
              WHERE cc.cve_cli = @cve_cli AND cc.empresa_id = @empresa_id",
                    new Dictionary<string, object> { { "@cve_cli", encabezadoData["cli_prov"] }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } }
                , false, conn, tx).FirstOrDefault();

                // Más abajo se lee cli["forma_pago"] sin verificar: un cliente sin dirección
                // de facturación provocaba un NullReference en medio de la refacturación,
                // con la factura global ya cancelada en el SAT.
                if (cli == null)
                    return (false,
                        $"El cliente {encabezadoData["cli_prov"]} no tiene dirección de facturación registrada en esta empresa.",
                        null, null, currentProcesoId);

                // ============================================
                // PASO 3: Verificar si hay variaciones previas
                // ============================================
                List<int> variacionesPrevias = new List<int>();
                if (currentProcesoId > 0)
                {
                    variacionesPrevias = controlHelper.ObtenerVariacionesActivas(currentProcesoId);

                    if (variacionesPrevias.Any())
                    {
                        // Ya existen variaciones válidas, reutilizarlas
                        string idsVariacionExistentes = string.Join(",", variacionesPrevias);

                        // Si es factura_cliente, obtener el último documento para timbrado
                        if (esFacturaCliente)
                        {
                            int ultimoDoc = variacionesPrevias.Last();
                            return (true, "Variaciones recuperadas", idsVariacionExistentes, ultimoDoc, currentProcesoId);
                        }
                        else
                        {
                            return (true, "Variaciones recuperadas", idsVariacionExistentes, null, currentProcesoId);
                        }
                    }
                }

                // ============================================
                // PASO 3: Clonar documentos individuales
                // ============================================
                if (currentProcesoId > 0)
                {
                    controlHelper.ActualizarEstado(currentProcesoId,
                        ControlProcesoHelper.EstadoProceso.CANCELADO_SAT,
                        3,
                        $"Clonando {idsOrigen.Count} documentos");
                }

                foreach (var idDoc in idsOrigen)
                {
                    var padreData = RunQuery(
                        "SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id",
                        new Dictionary<string, object> { { "@id", idDoc } }
                    , false, conn, tx).FirstOrDefault();

                    int idPadre = Convert.ToInt32(padreData["id_encabezado"]);

                    var queryPartidasDoc = @"
                SELECT cve_prod, descr_prod, cant_ud, ud, pv_prod, imp_part, dto1, iva, ieps, producto_id
                FROM partidasdoc
                WHERE encabezado_id = @id_enc";

                    var partidasResultado = RunQuery(queryPartidasDoc,
                        new Dictionary<string, object> { { "@id_enc", idDoc } }, false, conn, tx);

                    var partidasParaClone = new List<object>();
                    decimal totalDoc = 0;

                    foreach (var fila in partidasResultado)
                    {
                        decimal impPart = Convert.ToDecimal(fila["imp_part"]);
                        totalDoc += impPart;

                        partidasParaClone.Add(new
                        {
                            CveProd = fila["cve_prod"].ToString(),
                            DescrProd = fila["descr_prod"].ToString(),
                            CantUd = Convert.ToDecimal(fila["cant_ud"]),
                            Ud = fila["ud"].ToString(),
                            PvProd = Convert.ToDecimal(fila["pv_prod"]),
                            ImpPart = impPart,
                            Dto1 = Convert.ToDecimal(fila["dto1"]),
                            IdProducto = Convert.ToInt32(fila["producto_id"])
                        });
                    }

                    // Ejecutar SP clone_documento
                    var parametrosClone = new Dictionary<string, object>
            {
                { "p_id_original", idPadre },
                { "p_total", totalDoc },
                { "p_observaciones", "" },
                { "p_usuario", GetUserId(User.Identity.Name) },
                { "p_partidas", JsonConvert.SerializeObject(partidasParaClone) }
            };

                    var queryClone = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb)";
                    var clonado = RunQuery(queryClone, parametrosClone, false, conn, tx)[0];

                    int idVariacionCreada = Convert.ToInt32(clonado["idencabezado"]);
                    variacionesCreadas.Add(idVariacionCreada);

                    // 🔹 REGISTRAR en el helper INMEDIATAMENTE después de crear
                    if (currentProcesoId > 0)
                    {
                        string folioOrigen = $"{encabezadoData["gen"]}-{encabezadoData["nat"]}-{encabezadoData["fol_doc"]}";
                        controlHelper.RegistrarVariacion(
                            currentProcesoId,
                            idVariacionCreada,
                            idDoc,
                            folioOrigen,
                            ControlProcesoHelper.EstadoDocumento.CREADO
                        );
                    }
                }

                // ============================================
                // PASO 4: Si es factura_cliente, crear documento final
                // ============================================
                if (esFacturaCliente)
                {
                    if (currentProcesoId > 0)
                    {
                        controlHelper.ActualizarEstado(currentProcesoId,
                            ControlProcesoHelper.EstadoProceso.DOCUMENTOS_PREPARADOS,
                            4,
                            "Generando documento final");
                    }

                    // Obtener partidas combinadas
                    string queryPartidasAll = @"
                SELECT cve_prod, descr_prod, cant_ud, ud, pv_prod, imp_part, dto1, iva, ieps, producto_id
                FROM partidasdoc
                WHERE encabezado_id = ANY(@ids)
                ORDER BY producto_id;";

                    var partidasResult = RunQuery(queryPartidasAll,
                        new Dictionary<string, object> { { "ids", idsOrigen.ToArray() } }, false, conn, tx);
                    var partidasCombinadas = new List<PartidaDocumento>();
                    decimal totalFinal = 0;
                    decimal subtotalDocumento = 0;
                    decimal iva = 0;

                    foreach (var row in partidasResult)
                    {
                        decimal imp = Convert.ToDecimal(row["imp_part"]);
                        totalFinal += imp;

                        decimal cantidad = Convert.ToDecimal(row["cant_ud"]);
                        decimal precio = Convert.ToDecimal(row["pv_prod"]);
                        decimal impPart = Convert.ToDecimal(row["imp_part"]);

                        partidasCombinadas.Add(new PartidaDocumento
                        {
                            CveProd = row["cve_prod"].ToString(),
                            DescrProd = row["descr_prod"].ToString(),
                            CantUd = cantidad,
                            PvProd = precio,
                            Dto1 = Convert.ToDecimal(row["dto1"]),
                            Ud = row["ud"].ToString(),
                            ImpPart = imp,
                            IdProducto = Convert.ToInt32(row["producto_id"])
                        });

                        subtotalDocumento += Math.Round((cantidad * precio), 2);
                        iva += Math.Round(impPart * .16m, 2);
                    }

                    var encabezado = new DocumentoEncabezado();
                    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                    encabezado.IdArea = 14;
                    encabezado.IdTpDoc = 52;
                    encabezado.UsrDep = encabezadoData["usr_dep"].ToString();
                    encabezado.Anio = DateTime.Now.Year;
                    encabezado.Suc = Convert.ToInt32(encabezadoData["suc"]);
                    encabezado.Alm = encabezadoData["alm"]?.ToString() ?? "";
                    encabezado.Fch = DateTime.Now;
                    encabezado.TpMov = "VSPED";
                    encabezado.ComentAut = encabezadoData["coment_aut"]?.ToString() ?? "";
                    encabezado.UsrDoc = User.Identity.Name;
                    encabezado.FchCap = DateTime.Now;
                    encabezado.Usr0 = Convert.ToInt32(encabezadoData["usr0"]);
                    encabezado.Fch0 = Convert.ToDateTime(encabezadoData["fch0"]);
                    encabezado.Usr1 = Convert.ToInt32(encabezadoData["usr1"]);
                    encabezado.Fch1 = Convert.ToDateTime(encabezadoData["fch1"]);
                    encabezado.Usr2 = GetUserId(User.Identity.Name);
                    encabezado.Fch2 = DateTime.Now;
                    encabezado.CliProv = encabezadoData["cli_prov"].ToString();
                    encabezado.Ref = cli == null || cli["id_cliente"] == null || cli["id_cliente"] is DBNull ? (int?)null : Convert.ToInt32(cli["id_cliente"]);
                    encabezado.Ccy = encabezadoData["ccy"]?.ToString() ?? "PESOS";
                    encabezado.Estatus = 1;
                    encabezado.Flete = Convert.ToDecimal(encabezadoData["flete"]);
                    encabezado.VdrCpr = encabezadoData["vdr_cpr"]?.ToString()?? "";
                    encabezado.Coment1 = encabezadoData["coment1"]?.ToString() ?? "";
                    encabezado.EncabezadoPadre = idEncabezadoOrigen;
                    encabezado.PlDias = Convert.ToInt32(encabezadoData["pl_dias"]);
                    encabezado.FchPgEntrega = DateTime.Now;
                    encabezado.Par = Convert.ToDecimal(encabezadoData["par"]);
                    encabezado.FPago = !string.IsNullOrWhiteSpace(encabezadoData["f_pago"]?.ToString())
                        ? Convert.ToInt32(encabezadoData["f_pago"])
                        : (cli["forma_pago"] != null ? Convert.ToInt32(cli["forma_pago"]) : 0);
                    encabezado.Mdp = encabezadoData["mdp"]?.ToString() ?? "PUE";
                    encabezado.CFDI =
                        !string.IsNullOrWhiteSpace(encabezadoData["cfdi"]?.ToString())
                            ? encabezadoData["cfdi"].ToString()
                            : cli?["uso_sugerido"] is string uso && !string.IsNullOrWhiteSpace(uso)
                                ? uso
                                : "G03";
                    encabezado.TipoPoceso = esFacturaCliente
                        ? TipoProcesoFacturaContado
                        : TipoProcesoFacturaGlobal;
                    encabezado.CentroCostos = 14;
                    encabezado.Sub = totalFinal;
                    encabezado.Imp = totalFinal + (totalFinal * .16m);


                    var folio = GenerarDocumentoConPartidas(encabezado, partidasCombinadas, conn, tx);
                    int idDocumentoFinal = Convert.ToInt32(folio["IdEncabezado"]);

                    // Rastro del origen de la nominativa que se le emite al cliente: de qué
                    // ventas salió. Sin esto, cancelarla después dejaba a la consulta sin
                    // manera de saber qué documentos amparaba.
                    RegistrarOrigenFacturaTotal(idDocumentoFinal, idsOrigen, conn, tx);

                    // 🔹 REGISTRAR documento final como variación
                    if (currentProcesoId > 0)
                    {
                        controlHelper.RegistrarVariacion(
                            currentProcesoId,
                            idDocumentoFinal,
                            idEncabezadoOrigen,
                            $"FINAL-{folio["folio_generado"]}",
                            ControlProcesoHelper.EstadoDocumento.VALIDADO
                        );
                    }

                    //variacionesCreadas.Add(idDocumentoFinal);

                    // Insertar impuesto
                    string insertImpuesto = @"
                INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
                VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);";

                    var impParams = new Dictionary<string, object>
            {
                { "encabezado_id", idDocumentoFinal },
                { "impuesto_id", Convert.ToInt32(GetSetting("impuesto")) },
                { "subtotal", subtotalDocumento },
                { "importe", iva },
                { "orden_apl", 1 },
                { "imp_variable", 16 },
                { "prov_nom", encabezado.CliProv },
                { "f_pago_id", encabezado.FPago }
            };
                    RunQuery(insertImpuesto, impParams, false, conn, tx);

                    string idsVariacionStr = string.Join(",", variacionesCreadas);
                    return (true, "Documentos procesados correctamente", idsVariacionStr, idDocumentoFinal, currentProcesoId);
                }

                // ============================================
                // PASO 5: Crear variación global (si no es factura_cliente)
                // ============================================
                if (currentProcesoId > 0)
                {
                    controlHelper.ActualizarEstado(currentProcesoId,
                        ControlProcesoHelper.EstadoProceso.DOCUMENTOS_PREPARADOS,
                        5,
                        "Creando variación global");
                }

                // Obtener partidas combinadas para variación
                string queryPartidasVar = @"
            SELECT cve_prod, descr_prod, cant_ud, ud, pv_prod, imp_part, dto1, iva, ieps, producto_id
            FROM partidasdoc
            WHERE encabezado_id = ANY(@ids)
            ORDER BY producto_id;";

                var partidasVar = RunQuery(queryPartidasVar,
                    new Dictionary<string, object> { { "ids", idsOrigen.ToArray() } }, false, conn, tx);
                var partidasCombinadasVar = new List<PartidaDocumento>();
                decimal totalVar = 0;

                decimal subtotalVar = 0;
                decimal ivaVar = 0;

                foreach (var row in partidasVar)
                {
                    decimal cantidad = Convert.ToDecimal(row["cant_ud"]);
                    decimal precio = Convert.ToDecimal(row["pv_prod"]);
                    decimal impPart = Convert.ToDecimal(row["imp_part"]);

                    subtotalVar += Math.Round(cantidad * precio, 2);
                    ivaVar += Math.Round(impPart * .16m, 2);
                }




                foreach (var row in partidasVar)
                {
                    decimal imp = Convert.ToDecimal(row["imp_part"]);
                    totalVar += imp;

                    partidasCombinadasVar.Add(new PartidaDocumento
                    {
                        CveProd = row["cve_prod"].ToString(),
                        DescrProd = row["descr_prod"].ToString(),
                        CantUd = Convert.ToDecimal(row["cant_ud"]),
                        PvProd = Convert.ToDecimal(row["pv_prod"]),
                        Dto1 = Convert.ToDecimal(row["dto1"]),
                        Ud = row["ud"].ToString(),
                        ImpPart = imp,
                        IdProducto = Convert.ToInt32(row["producto_id"])
                    });
                }

                var parametrosVariacion = new Dictionary<string, object>
        {
            { "p_id_original", Convert.ToInt32(global) },
            { "p_total", totalVar },
            { "p_observaciones", "Variación generada automáticamente" },
            { "p_usuario", GetUserId(User.Identity.Name) },
            { "p_partidas", JsonConvert.SerializeObject(partidasCombinadasVar) }
        };

                var queryVariacion = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb)";
                variacion = RunQuery(queryVariacion, parametrosVariacion, false, conn, tx)[0];
                int idVariacionGlobal = Convert.ToInt32(variacion["idencabezado"]);

                //variacionesCreadas.Add(idVariacionGlobal);

                // 🔹 REGISTRAR variación global
                if (currentProcesoId > 0)
                {
                    controlHelper.RegistrarVariacion(
                        currentProcesoId,
                        idVariacionGlobal,
                        Convert.ToInt32(global),
                        "VARIACION-GLOBAL",
                        ControlProcesoHelper.EstadoDocumento.VALIDADO
                    );
                }

                // INSERTAR IMPUESTO PARA VARIACIÓN GLOBAL
                string insertImpuestoVar = @"
INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);";

                var impParamsVar = new Dictionary<string, object>
{
    { "encabezado_id", idVariacionGlobal },
    { "impuesto_id", Convert.ToInt32(GetSetting("impuesto")) },
    { "subtotal", subtotalVar },
    { "importe", ivaVar },
    { "orden_apl", 1 },
    { "imp_variable", 16 },
    { "prov_nom", encabezadoData["cli_prov"].ToString() },
    { "f_pago_id",
        !string.IsNullOrWhiteSpace(encabezadoData["f_pago"]?.ToString())
            ? Convert.ToInt32(encabezadoData["f_pago"])
            : (cli["forma_pago"] != null ? Convert.ToInt32(cli["forma_pago"]) : 1)
    }
};

                RunQuery(insertImpuestoVar, impParamsVar, false, conn, tx);
                string idsVariacionFinal = string.Join(",", variacionesCreadas);
                return (true, "Variación creada correctamente", idsVariacionFinal, idVariacionGlobal, currentProcesoId);
            }
            catch (Exception ex)
            {
                // Los documentos creados aquí desaparecen con el rollback de la transacción
                // del llamador; ya no hay que borrarlos a mano con BorradoFacturasIncorrectas.
                // Ese borrado compensatorio corría en otra conexión y podía fallar a medias
                // —dejando clones huérfanos— o borrar de más si el id se repetía.
                //
                // La bitácora sí se actualiza, porque vive fuera de la transacción: si no se
                // marcaran como ELIMINADO, al reanudar el proceso ObtenerVariacionesActivas
                // devolvería ids de documentos que el rollback ya borró.
                if (currentProcesoId > 0)
                {
                    controlHelper.RegistrarError(currentProcesoId, ex.Message, 3);

                    foreach (var idVar in variacionesCreadas)
                    {
                        try
                        {
                            controlHelper.ActualizarEstadoVariacion(
                                currentProcesoId,
                                idVar,
                                ControlProcesoHelper.EstadoDocumento.ELIMINADO,
                                "Revertido por rollback de la transacción"
                            );
                        }
                        catch (Exception exBitacora)
                        {
                            LogErrorHelper.RegistrarLog("GuardarDesdeDocumentos", "Bitacora",
                                $"No se pudo marcar la variación {idVar} como eliminada: {exBitacora.Message}",
                                User.Identity.Name);
                        }
                    }
                }

                return (false, "Error en GuardarDesdeDocumentos: " + ex.Message, null, null, currentProcesoId);
            }
        }

        // AGREGAR ESTA CLASE AL INICIO DEL ARCHIVO (junto con las otras clases del modelo)
        public class DatosFiscalesPersonalizados
        {
            public string Rfc { get; set; }
            public string RazonSocial { get; set; }
            public string Cp { get; set; }
            public string RegimenFiscal { get; set; }
            public string UsoCFDI { get; set; }
            public string FormaPago { get; set; }
        }

        /// <summary>
        /// Arma el objeto Factura y registra su póliza, todo dentro de la transacción del
        /// llamador. NO timbra: el timbrado es una llamada al PAC que ningún rollback puede
        /// deshacer, así que lo ejecuta ProcesarDocumentosAsync una vez commiteado. Antes
        /// iba aquí adentro y, al fallar un paso posterior, se borraban los documentos
        /// locales dejando vivo en el SAT un CFDI sin respaldo en el ERP.
        /// </summary>
        [NonAction]
        public async Task<(bool Success, string Message, Factura Factura, int ProcesoId)> PrepararFacturaDesdeDocs(
            string ids,
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            int? procesoId = null,
            bool usarDatosPersonalizados = false,
            DatosFiscalesPersonalizados datosFiscales = null,
            string cuentaBanco = null)
        {
            var controlHelper = new ControlProcesoHelper(RunQueryWrapper, RunUpdateWrapper);
            int currentProcesoId = procesoId ?? 0;
            int idDocumentoTimbrado = 0;

            try
            {
                var idsOrigen = ids
                    .Split(',')
                    .Select(x => int.TryParse(x.Trim(), out int id) ? id : 0)
                    .Where(x => x > 0)
                    .ToList();

                if (!idsOrigen.Any())
                    return (false, "No se recibieron IDs válidos", null, currentProcesoId);

                idDocumentoTimbrado = idsOrigen.Last();

                if (currentProcesoId > 0)
                {
                    controlHelper.ActualizarEstado(currentProcesoId,
                        ControlProcesoHelper.EstadoProceso.DOCUMENTOS_PREPARADOS,
                        4,
                        $"Generando factura desde documentos (ID: {idDocumentoTimbrado})");
                }

                var queryValidacion = "SELECT id_encabezado, estatus_id FROM encabezadomov WHERE id_encabezado = @id;";
                var docValidacion = RunQuery(queryValidacion,
                    new Dictionary<string, object> { { "id", idDocumentoTimbrado } }, false, conn, tx).FirstOrDefault();

                if (docValidacion == null)
                {
                    throw new Exception($"El documento {idDocumentoTimbrado} no existe o fue eliminado");
                }

                var queryEncabezado = @"
            SELECT em.*, c.n_cli, c.cp, c.rfc 
            FROM encabezadomov em
            LEFT JOIN catclientes c ON em.cli_prov = c.cve_cli AND c.empresa_id = @empresa_id
            WHERE em.id_encabezado = @id;";

                var parameters = new Dictionary<string, object> { { "id", idsOrigen.First() }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } };
                var enc = RunQuery(queryEncabezado, parameters, false, conn, tx).FirstOrDefault();

                parameters.Add("cve_cli", enc["cli_prov"]);
                var queryCliente = @"
            SELECT * FROM catclientes cc
            INNER JOIN direcciones_facturacion df
               ON df.entidad_clave = cc.cve_cli
              AND df.empresa_id = cc.empresa_id
            WHERE cc.cve_cli = @cve_cli AND cc.empresa_id = @empresa_id";
                var cli = RunQuery(queryCliente, parameters, false, conn, tx).FirstOrDefault();

                string queryPartidas = @"
            SELECT pd.cve_prod, pd.descr_prod, pd.cant_ud, pd.ud, pd.pv_prod, pd.imp_part, pd.producto_id
            FROM partidasdoc pd
            WHERE pd.encabezado_id = ANY(@ids);";
                var partidas = RunQuery(queryPartidas,
                    new Dictionary<string, object> { { "ids", idsOrigen.ToArray() } }, false, conn, tx);

                decimal subtotal = 0, total = 0, iva = 0;
                foreach (var p in partidas)
                {
                    decimal cantidad = Convert.ToDecimal(p["cant_ud"]);
                    decimal precio = Convert.ToDecimal(p["pv_prod"]);
                    decimal imp = Convert.ToDecimal(p["imp_part"]);
                    subtotal += cantidad * precio;
                    total += imp;
                }
                iva = total - subtotal;

                string ccy = enc["ccy"]?.ToString() ?? "";
                string moneda = ccy == "DLLS" ? "USD" : ccy == "EURO" ? "EUR" : "MXN";

                // Emisor desde appsettings, igual que el resto de la facturación. Estaba
                // fijo con el RFC de pruebas del SAT (EKU9003173C9 / ESCUELA KEMPER URGATE),
                // así que toda refacturación se timbraba a nombre del emisor equivocado.
                string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                    ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                    : "pruebas";
                string GetEmisor(string campo) => _configuration[$"Emisores:{perfil}:{campo}"] ?? "";

                if (string.IsNullOrWhiteSpace(GetEmisor("Rfc")))
                    return (false, $"No hay datos de emisor configurados para el perfil '{perfil}' (Emisores:{perfil} en appsettings).", null, currentProcesoId);

                // "factura_sucursal" es el tipo de proceso del documento (lo usa el handler
                // de pólizas), no un tipo de comprobante: el switch de XmlBuilderService no
                // lo conoce y lanzaba "Tipo de facturación no soportado". La refacturación
                // de una global debe volver a salir como global —con su nodo
                // InformacionGlobal— y la individual como ingreso normal.
                bool esGlobal = (enc["tipo_proceso"]?.ToString() ?? "")
                    .EndsWith("_global", StringComparison.OrdinalIgnoreCase);

                // ⚡ MODIFICACIÓN CLAVE: Usar datos personalizados si están disponibles
                string rfcCliente, razonSocialCliente, cpCliente, regimenFiscalCliente, usoCFDICliente, formaPagoId;
                string usoCFDItext, regimenText, tp;

                if (usarDatosPersonalizados && datosFiscales != null)
                {
                    // Usar datos personalizados
                    rfcCliente = datosFiscales.Rfc;
                    razonSocialCliente = datosFiscales.RazonSocial;
                    cpCliente = datosFiscales.Cp;
                    regimenFiscalCliente = datosFiscales.RegimenFiscal;
                    usoCFDICliente = datosFiscales.UsoCFDI;
                    formaPagoId = datosFiscales.FormaPago;

                    // Obtener descripciones desde catálogos
                    usoCFDItext = RunScalar("SELECT descripcion FROM catusocfdi WHERE clave = @cve;",
                        new Dictionary<string, object> { { "cve", usoCFDICliente } }, false, conn, tx)?.ToString() ?? "Sin efectos fiscales.";

                    regimenText = RunScalar("SELECT descripcion FROM catregimenfiscal WHERE clave = @cve;",
                        new Dictionary<string, object> { { "cve", regimenFiscalCliente } }, false, conn, tx)?.ToString() ?? "Sin obligaciones fiscales";

                    tp = RunScalar("SELECT descripcion FROM cat_f_pago WHERE cve_sat = @cve;",
                        new Dictionary<string, object> { { "cve", formaPagoId } }, false, conn, tx)?.ToString() ?? "Por definir";
                }
                else
                {
                    // Sin dirección de facturación no hay receptor que poner en el CFDI.
                    // Antes reventaba con NullReference aquí, ya cancelada la global en el SAT.
                    if (cli == null)
                        throw new Exception(
                            $"El cliente {enc["cli_prov"]} no tiene dirección de facturación registrada en esta empresa. " +
                            "Regístrala o usa la opción de datos fiscales personalizados.");

                    // Usar datos del cliente original
                    rfcCliente = cli["rfc"].ToString();
                    razonSocialCliente = cli["n_cli"].ToString();
                    cpCliente = enc["cp"].ToString();
                    regimenFiscalCliente = cli["regimen_fiscal"].ToString() ?? "616";
                    usoCFDICliente = cli["uso_sugerido"].ToString();
                    formaPagoId = Convert.ToInt32(cli["forma_pago"]).ToString();

                    usoCFDItext = RunScalar("SELECT descripcion FROM catusocfdi WHERE clave = @cve;",
                        new Dictionary<string, object> { { "cve", usoCFDICliente } }, false, conn, tx)?.ToString() ?? "Sin efectos fiscales.";

                    regimenText = RunScalar("SELECT descripcion FROM catregimenfiscal WHERE clave = @cve;",
                        new Dictionary<string, object> { { "cve", regimenFiscalCliente } }, false, conn, tx)?.ToString() ?? "General de Ley Personas Morales";

                    tp = RunScalar("SELECT descripcion FROM cat_f_pago WHERE id_f_pago = @cve;",
                        new Dictionary<string, object> { { "cve", Convert.ToInt32(cli["forma_pago"]) } }, false, conn, tx) as string ?? "1";
                }

                string mdp = RunScalar("SELECT descripcion FROM mdp WHERE cve_mdp = @cve;",
                    new Dictionary<string, object> { { "cve", enc["mdp"] } }, false, conn, tx)?.ToString() ?? "PUE";

                // Crear objeto Factura con los datos correspondientes
                var factura = new Factura
                {
                    Serie = "VS",
                    // Con conn/tx: el documento se acaba de crear en esta transacción y
                    // desde otra conexión no existe todavía.
                    Folio = GetDocumentFolio(idsOrigen.First(), "ERP_SRS", conn, tx),
                    IdTipoPago = RunScalar("SELECT cve_sat FROM cat_f_pago WHERE id_f_pago = @cve;",
                        new Dictionary<string, object> { { "cve", enc["f_pago"] } }, false, conn, tx)?.ToString() ?? "99",
                    Moneda = moneda,
                    CpE = GetEmisor("CpE"),
                    RfcEmisor = GetEmisor("Rfc"),
                    RsoEmisor = GetEmisor("RazonSocial"),
                    Rege = GetEmisor("Regimen"),
                    RfcCliente = rfcCliente,
                    RsoCliente = razonSocialCliente,
                    CpR = cpCliente,
                    IdUsoCFDI = usoCFDICliente,
                    CFDIText = usoCFDItext,
                    Regc = regimenFiscalCliente,
                    regimenEText = regimenText,
                    Subtotal = subtotal,
                    IVA = iva,
                    Total = total,
                    TipoCambio = 1m,
                    // El lugar de expedición es el CP del emisor, no una constante.
                    LugarExpedicion = !string.IsNullOrWhiteSpace(GetEmisor("CpE"))
                        ? GetEmisor("CpE")
                        : "78394",
                    metodoPagoTexto = enc["mdp"].ToString(),
                    MdpFactura = mdp,
                    TipoDeComprobante = "I",
                    Observaciones = enc["coment1"]?.ToString() ?? "",
                    formaPagoTexto = tp,
                    Fecha = DateTime.Now,
                    TipoFacturacion = esGlobal ? "global" : "contado",
                    EncabezadoId = idsOrigen.First()
                };

                // (antes se guardaba el id en `enca` para poder borrar el documento a mano
                //  si algo fallaba; ahora de eso se encarga el rollback)

                // El atributo Folio del CFDI sale de FolioCorto —los primeros 8 caracteres
                // del uuid del encabezado—, NO de factura.Folio. Aquí nunca se asignaba, así
                // que el comprobante salía con Folio="" y el PAC lo rechazaba con
                // «The 'Folio' attribute is invalid ... Pattern constraint failed».
                // Es la misma convención que usa el punto de venta.
                var uuidDoc = RunScalar(
                    "SELECT uuid FROM encabezadomov WHERE id_encabezado = @encabezado",
                    new Dictionary<string, object> { { "encabezado", factura.EncabezadoId } },
                    false, conn, tx)?.ToString();

                if (string.IsNullOrWhiteSpace(uuidDoc) || uuidDoc.Length < 8)
                    throw new Exception(
                        $"El documento {factura.EncabezadoId} no tiene uuid: no se puede formar el folio del CFDI.");

                factura.FolioCorto = uuidDoc.Substring(0, 8);

                // Crear DataTable de productos
                factura.Tproductos = new DataTable();
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
            new DataColumn("objetoImp", typeof(string))
        });

                var sinClaveSat = new List<string>();

                foreach (var prod in partidas)
                {
                    var prodData = RunQuery(
                        "SELECT prod_sat, ud_sat, obj_impto FROM catrelacion WHERE prod_kepler = @cve",
                        new Dictionary<string, object> { { "cve", prod["cve_prod"].ToString() } }, false, conn, tx);

                    // Sin fila en catrelacion no hay ClaveProdServ, ClaveUnidad ni ObjetoImp
                    // y el concepto del CFDI no se puede armar. Antes reventaba con
                    // IndexOutOfRange sin decir qué producto faltaba.
                    if (prodData.Count == 0)
                    {
                        sinClaveSat.Add(prod["cve_prod"].ToString());
                        continue;
                    }

                    factura.Tproductos.Rows.Add(
                        prod["cve_prod"].ToString(),
                        prodData[0]["prod_sat"].ToString(),
                        prodData[0]["ud_sat"].ToString(),
                        prod["ud"].ToString(),
                        prod["descr_prod"].ToString(),
                        Convert.ToDouble(prod["cant_ud"]),
                        Convert.ToDouble(prod["pv_prod"]),
                        Convert.ToDouble(prod["cant_ud"]) * Convert.ToDouble(prod["pv_prod"]),
                        prodData[0]["obj_impto"].ToString()
                    );
                }

                if (sinClaveSat.Count > 0)
                    throw new Exception(
                        "No se puede timbrar: falta la clave SAT (catrelacion) de " +
                        string.Join(", ", sinClaveSat.Distinct()) +
                        ". Da de alta la relación del producto y vuelve a intentar.");

                if (factura.Tproductos.Rows.Count == 0)
                    throw new Exception("La factura no tiene conceptos que timbrar.");

                if (currentProcesoId > 0)
                {
                    controlHelper.ActualizarEstado(currentProcesoId,
                        ControlProcesoHelper.EstadoProceso.REFACTURACION_INDIVIDUAL,
                        5,
                        "Preparando factura para timbrado");
                }

                // Póliza dentro de la transacción: si algo falla después, se va con el
                // rollback en vez de quedar registrada contra un documento inexistente.
                int encId = Convert.ToInt32(factura.EncabezadoId);
                var poliza = GenerarDatosPoliza(encId, cuentaBanco, null, conn, tx);
                var polizaRegistrada = RegistrarPolizas(
                    GetUserId(User.Identity.Name), encId, poliza, false, null, conn, tx);

                // Cartera y cobro: faltaban por completo. Tanto la nominativa que se le emite
                // al cliente como la global de reemplazo generan un cargo que ya está pagado
                // —el dinero entró en la venta original—, así que hay que dejar la cartera
                // saldada. El cobro va con su propio documento CXC.
                var cartera = RegistrarCartera(
                    polizaRegistrada[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

                RegistrarCobroConDocumentoCxc(encId, cartera.CarteraId, cuentaBanco, conn, tx);

                // El timbrado lo hace el llamador después del commit.
                return (true, "Factura preparada correctamente", factura, currentProcesoId);
            }
            catch (Exception ex)
            {
                // Sin BorradoFacturasIncorrectas: el rollback de la transacción deshace
                // documentos y póliza de golpe. Sólo queda anotar el fallo en la bitácora,
                // que corre por su propia conexión y sobrevive al rollback.
                if (currentProcesoId > 0)
                {
                    controlHelper.RegistrarError(currentProcesoId, ex.Message, 5);

                    if (idDocumentoTimbrado > 0)
                    {
                        controlHelper.ActualizarEstadoVariacion(
                            currentProcesoId,
                            idDocumentoTimbrado,
                            ControlProcesoHelper.EstadoDocumento.FALLIDO,
                            $"Error: {ex.Message}"
                        );
                    }
                }

                return (false, "Error en PrepararFacturaDesdeDocs: " + ex.Message, null, currentProcesoId);
            }
        }
        // ASP.NET Core recorta el sufijo "Async" del nombre de la acción
        // (SuppressAsyncSuffixInActionNames vale true por omisión), así que por ruteo
        // convencional esto quedaba publicado como /FacturaGlobalConsulta/ProcesarDocumentos
        // y la vista, que llama a .../ProcesarDocumentosAsync, recibía un 404 aunque el
        // método existiera. La ruta explícita restituye el nombre completo, igual que en
        // VSFactura, VNFactura, VINFactura, VIFactura, ComplementoPago y PuntoDeVenta.
        [HttpPost]
        [Route("FacturaGlobalConsulta/ProcesarDocumentosAsync")]
        public async Task<JsonResult> ProcesarDocumentosAsync(IFormCollection fc)
        {
            string idsDocumentos = fc["ids"].ToString();
            string tipo = fc["tipo"].ToString();
            string global = fc["global"].ToString();
            // Cuenta de destino del cobro. La manda la vista igual que el cierre de caja;
            // sin ella la póliza del CXC no sabe a qué banco cargar el ingreso.
            string cuentaBanco = fc["banco"].ToString();
            int? procesoIdExistente = null;

            // ⚡ NUEVO: Recibir datos fiscales personalizados
            bool usarDatosPersonalizados = fc["usarDatosPersonalizados"].ToString() == "true";
            DatosFiscalesPersonalizados datosFiscales = null;

            if (usarDatosPersonalizados)
            {
                try
                {
                    string datosFiscalesJson = fc["datosFiscalesJson"].ToString();
                    if (!string.IsNullOrWhiteSpace(datosFiscalesJson))
                    {
                        datosFiscales = JsonConvert.DeserializeObject<DatosFiscalesPersonalizados>(datosFiscalesJson);
                    }
                }
                catch (Exception ex)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Error al procesar los datos fiscales: " + ex.Message
                    });
                }
            }

            if (int.TryParse(fc["procesoId"].ToString(), out int pid))
            {
                procesoIdExistente = pid;
            }

            if (string.IsNullOrWhiteSpace(idsDocumentos))
                return Json(new { success = false, message = "No se recibieron IDs de documentos." });

            int cantidadDocumentos = idsDocumentos.Split(',').Length;
            var controlHelper = new ControlProcesoHelper(RunQueryWrapper, RunUpdateWrapper);
            int procesoId = procesoIdExistente ?? 0;

            // Estado compartido entre la reanudación y el proceso normal: ambos terminan
            // preparando y timbrando el mismo documento, así que se resuelve quién es y se
            // sigue por un solo camino.
            bool reanudando = false;
            int idDocumentoAFacturar = 0;
            int[] idsHijos = Array.Empty<int>();

            // El paso 6 emite una nominativa por cada documento que el cliente pidió; el
            // paso 7 emite la global de reemplazo. Sólo esta última cierra el proceso.
            bool esFacturaCliente = tipo == TipoRefacturacionCliente;

            try
            {
                // ============================================
                // VERIFICAR SI HAY UN PROCESO EXISTENTE
                // ============================================
                Dictionary<string, object> estadoPrevio = null;

                if (procesoId > 0)
                {
                    var query = "SELECT * FROM control_proceso_cancelacion WHERE id = @id;";
                    var resultado = RunQuery(query, new Dictionary<string, object> { { "id", procesoId } });
                    estadoPrevio = resultado.FirstOrDefault();
                }

                if (estadoPrevio != null)
                {
                    var verificacion = controlHelper.VerificarEstadoReanudacion(procesoId);
                    var resumen = controlHelper.ObtenerResumenProceso(procesoId);

                    // Si está completado, no procesar de nuevo
                    if (!verificacion.PuedeReanudar && resumen["estadoProceso"].ToString() == "COMPLETADO")
                    {
                        return Json(new
                        {
                            success = false,
                            yaCompletado = true,
                            message = "Este proceso ya fue completado anteriormente",
                            procesoId,
                            resumen
                        });
                    }

                    var estadoActual = estadoPrevio["estado_proceso"].ToString();
                    var pasoActual = Convert.ToInt32(estadoPrevio["paso_actual"]);
                    var yaCanceladoSAT = Convert.ToBoolean(estadoPrevio["cancelado_sat"]);

                    // Verificar qué pasos ya se completaron
                    bool documentosGuardados = estadoPrevio["variaciones_creadas"] != null &&
                                              !string.IsNullOrWhiteSpace(estadoPrevio["variaciones_creadas"].ToString());

                    bool facturaGenerada = estadoPrevio["id_factura_global"] != null &&
                                          Convert.ToInt32(estadoPrevio["id_factura_global"]) > 0;

                    if (facturaGenerada)
                    {
                        return Json(new
                        {
                            success = true,
                            message = "El proceso ya fue completado anteriormente",
                            procesoId,
                            yaCompletado = true,
                            resumen
                        });
                    }

                    // Reanudación: los documentos ya existen de un intento anterior y sólo
                    // falta timbrar. Se resuelve qué documento tocaba y se sigue por el mismo
                    // camino que el proceso normal, en vez de duplicarlo.
                    if (documentosGuardados && yaCanceladoSAT)
                    {
                        var variacionesActivas = controlHelper.ObtenerVariacionesActivas(procesoId);

                        if (variacionesActivas.Any())
                        {
                            // ids_variacion puede venir vacío (proceso reiniciado a mano, o
                            // interrumpido antes de escribirlo): .ToString() sobre null
                            // lanzaba NullReference y tumbaba el paso justo al arrancar.
                            // Con la lista de variaciones pendientes basta.
                            var ultimaGuardada = (estadoPrevio["ids_variacion"]?.ToString() ?? "")
                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(x => int.TryParse(x.Trim(), out int v) ? v : 0)
                                .LastOrDefault(v => v > 0);

                            reanudando = true;
                            idsHijos = variacionesActivas.ToArray();
                            idDocumentoAFacturar = esFacturaCliente || ultimaGuardada == 0
                                ? variacionesActivas.Last()
                                : ultimaGuardada;
                        }
                    }
                }

                // ════════════════════════════════════════════════════════════════
                // Documentos y póliza: todo o nada, en una sola transacción.
                // Antes cada paso abría su propia conexión y, al fallar uno, había que
                // deshacer los anteriores a mano con BorradoFacturasIncorrectas: un
                // borrado compensatorio que podía quedarse a medias y dejar clones
                // huérfanos, o borrar de más.
                // ════════════════════════════════════════════════════════════════
                Factura facturaPorTimbrar;
                TimbradoResult resultadoTimbrado;

                using (var conn = AbrirConexion())
                using (var tx = conn.BeginTransaction())
                {
                    if (!reanudando)
                    {
                        var resultado1 = await GuardarDesdeDocumentos(
                            idsDocumentos, conn, tx, tipo, global, procesoId);

                        procesoId = resultado1.ProcesoId;

                        if (!resultado1.Success)
                            return Json(new
                            {
                                success = false,
                                step = "GuardarDesdeDocumentos",
                                error = resultado1.Message,
                                procesoId
                            });

                        idDocumentoAFacturar = Convert.ToInt32(resultado1.Data2);

                        idsHijos = (resultado1.Data?.ToString() ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => int.TryParse(x.Trim(), out int v) ? v : 0)
                            .Where(v => v > 0)
                            .ToArray();
                    }

                    var preparacion = await PrepararFacturaDesdeDocs(
                        idDocumentoAFacturar.ToString(), conn, tx,
                        procesoId, usarDatosPersonalizados, datosFiscales, cuentaBanco);

                    if (!preparacion.Success)
                        return Json(new
                        {
                            success = false,
                            step = "PrepararFacturaDesdeDocs",
                            error = preparacion.Message,
                            procesoId
                        });

                    facturaPorTimbrar = preparacion.Factura;

                    // Ligar los documentos origen a la nueva factura DENTRO de la misma
                    // transacción: si el commit falla, no quedan apuntando a un hijo que
                    // nunca existió.
                    if (idsHijos.Length > 0)
                        RunUpdate(
                            "UPDATE encabezadomov SET encabezado_hijo = @id WHERE id_encabezado = ANY(@ids);",
                            new Dictionary<string, object>
                            {
                                { "@id", idDocumentoAFacturar },
                                { "ids", idsHijos }
                            },
                            false, conn, tx);

                    // ── Timbrado, DENTRO de la transacción ───────────────────────
                    // El PAC valida el comprobante y rechaza (folio vacío, RFC mal, clave
                    // SAT ausente…). En ese caso no se timbró nada, así que lo correcto es
                    // que el rollback se lleve documentos y póliza: nada debe quedar si el
                    // proceso no terminó bien.
                    // Se puede hacer porque `factura` no tiene llaves foráneas y
                    // GuardarFactura acepta la misma conn/tx: la fila de la factura entra
                    // en la transacción junto con todo lo demás.
                    resultadoTimbrado = GenerarXml(facturaPorTimbrar, conn, tx);

                    if (!resultadoTimbrado.Success)
                    {
                        // Sin commit: al salir del using, Npgsql revierte la transacción y
                        // los encabezados creados en este intento desaparecen.
                        if (procesoId > 0)
                        {
                            controlHelper.RegistrarError(procesoId,
                                $"Error en timbrado: {resultadoTimbrado.Message}", 5);

                            controlHelper.ActualizarEstadoVariacion(procesoId, idDocumentoAFacturar,
                                ControlProcesoHelper.EstadoDocumento.ELIMINADO,
                                $"Revertido: el PAC rechazó el comprobante. {resultadoTimbrado.Message}");
                        }

                        RegistrarRechazoPac(resultadoTimbrado.Message, "Facturacion/Global");

                        return Json(new
                        {
                            success = false,
                            step = "Timbrado",
                            error = "Error al timbrar: " + resultadoTimbrado.Message,
                            procesoId
                        });
                    }

                    // El PAC ya timbró: a partir de aquí el commit debe completarse. Si
                    // fallara, existiría un CFDI en el SAT sin respaldo local, así que se
                    // registra el UUID en el log para poder recuperarlo o cancelarlo.
                    try
                    {
                        tx.Commit();
                    }
                    catch (Exception exCommit)
                    {
                        LogErrorHelper.RegistrarLog("FacturaGlobalConsulta/ProcesarDocumentos",
                            resultadoTimbrado.UUID,
                            "TIMBRADO OK PERO FALLÓ EL COMMIT. Hay un CFDI vivo en el SAT sin " +
                            $"documentos en el ERP. UUID: {resultadoTimbrado.UUID}. {exCommit}",
                            User.Identity?.Name);
                        throw;
                    }
                }

                // ── Bitácora, ya con UUID ───────────────────────────────────────
                if (procesoId > 0)
                {
                    controlHelper.ActualizarEstadoVariacion(procesoId, idDocumentoAFacturar,
                        ControlProcesoHelper.EstadoDocumento.TIMBRADO,
                        $"Timbrado exitoso - UUID: {resultadoTimbrado.UUID}");

                    // Los clones que alimentaron esta factura también quedan consumidos.
                    // Si sólo se marcaba el documento final, los clones seguían figurando
                    // como pendientes y el siguiente paso —la global— creía que había un
                    // proceso a medias que reanudar.
                    foreach (var idClon in idsHijos)
                        controlHelper.ActualizarEstadoVariacion(procesoId, idClon,
                            ControlProcesoHelper.EstadoDocumento.TIMBRADO,
                            $"Consumido por el CFDI {resultadoTimbrado.UUID}");

                    controlHelper.RegistrarRefacturacionIndividual(procesoId,
                        $"{facturaPorTimbrar.Serie}-{facturaPorTimbrar.Folio}",
                        resultadoTimbrado.UUID, true, "Factura generada exitosamente",
                        idDocumentoAFacturar);

                    // Sólo la GLOBAL de reemplazo cierra el proceso. Marcarlo al timbrar una
                    // nominativa dejaba el proceso en COMPLETADO —y la factura del cliente
                    // registrada como si fuera la global— antes de que el paso 7 llegara a
                    // emitirla; el asistente respondía luego "ya fue refacturada
                    // completamente" y la global nueva nunca se generaba.
                    if (!esFacturaCliente)
                    {
                        controlHelper.RegistrarFacturaGlobal(procesoId,
                            facturaPorTimbrar.EncabezadoId, resultadoTimbrado.UUID);

                        controlHelper.CompletarProceso(procesoId);
                    }
                }

                return Json(new
                {
                    success = true,
                    message = reanudando
                        ? "Proceso reanudado y completado exitosamente"
                        : "Todos los documentos fueron procesados y timbrados correctamente",
                    reanudado = reanudando,
                    procesoId,
                    // La vista lee result.uuid al nivel superior; sólo venía dentro de
                    // "detalles", así que el diálogo de éxito nunca mostraba el folio fiscal.
                    uuid = resultadoTimbrado.UUID,
                    resumen = new
                    {
                        documentosProcesados = cantidadDocumentos,
                        tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                    },
                    detalles = new
                    {
                        pedidoId = facturaPorTimbrar.EncabezadoId,
                        uuid = resultadoTimbrado.UUID,
                        total = facturaPorTimbrar.Total,
                        subtotal = facturaPorTimbrar.Subtotal,
                        iva = facturaPorTimbrar.IVA,
                        rfcCliente = facturaPorTimbrar.RfcCliente,
                        razonSocial = facturaPorTimbrar.RsoCliente,
                        serie = facturaPorTimbrar.Serie,
                        folio = facturaPorTimbrar.Folio,
                        fecha = facturaPorTimbrar.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                        cantidadProductos = facturaPorTimbrar.Tproductos.Rows.Count,
                        pdfUrl = Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf"),
                        xmlUrl = Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml")
                    }
                });
            }
            catch (Exception ex)
            {
                // Registrar error en el control si tenemos procesoId
                if (procesoId > 0)
                {
                    controlHelper.RegistrarError(procesoId, ex.Message, 0);
                }

                return Json(new
                {
                    success = false,
                    message = "Error general en el proceso: " + ex.Message,
                    procesoId,
                    detalles = ex.StackTrace
                });
            }
        }
    }
}