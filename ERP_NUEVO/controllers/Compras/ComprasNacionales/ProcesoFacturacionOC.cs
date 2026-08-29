using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    public partial class RequisicionController : Utilities
    {
        #region Paso 8 - Facturacion de la orden de compra

        private const string RutaArchivosOC = "content/archivos_oc_compras/";

        // Ordenes vivas (pendientes de ingreso o ya recibidas) que aun no tienen
        // factura registrada. Salen de la tabla en cuanto se les sube el CFDI.
        public JsonResult GetOrdenesPorFacturar(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "em.nat IN ('OC', 'OCD', 'GTO') AND em.estatus_id IN (11, 17) " +
                "AND NOT EXISTS (SELECT 1 FROM archivos_compras_oc a " +
                "                WHERE a.encabezado_id = em.id_encabezado AND a.extencion = '.xml')",
                columnaFecha: "em.fch6",
                columnasExtra: ", em.cli_prov AS proveedor, em.tipo_proceso AS modalidad, " +
                    "  em.ccy AS moneda, em.refe AS proveedor_id");
        }

        // Ordenes que ya tienen CFDI y por lo tanto cartera generada
        public JsonResult GetOrdenesFacturadas(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "em.nat IN ('OC', 'OCD', 'GTO') " +
                "AND EXISTS (SELECT 1 FROM archivos_compras_oc a " +
                "            WHERE a.encabezado_id = em.id_encabezado AND a.extencion = '.xml')",
                columnaFecha: "em.fch6",
                columnasExtra: ", em.cli_prov AS proveedor, em.tipo_proceso AS modalidad, " +
                    "  (SELECT f.uuid FROM factura_proveedor_oc f " +
                    "   WHERE f.encabezado_id = em.id_encabezado ORDER BY f.id_factura_oc DESC LIMIT 1) AS uuid_factura, " +
                    "  (SELECT f.folio FROM factura_proveedor_oc f " +
                    "   WHERE f.encabezado_id = em.id_encabezado ORDER BY f.id_factura_oc DESC LIMIT 1) AS folio_factura");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDetalleOrdenPorFacturar(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    ["id"] = Convert.ToInt32(fc["id"].ToString())
                };

                var orden = RunQuery(QueryTotalesOrden, parameters);

                if (!orden.Any())
                {
                    return Json(new { success = false, error = "No se encontro la orden de compra." });
                }

                string query = "SELECT p.cve_prod, p.descr_prod, p.ud, p.cant_ud, p.pv_prod, " +
                    "   COALESCE(p.dto1, 0) AS descuento, p.imp_part, " +
                    "   ROUND(COALESCE(p.imp_part, 0)::numeric * (1 - COALESCE(p.dto1, 0)::numeric / 100), 2) AS totaldescuento " +
                    "FROM partidasdoc p " +
                    "WHERE p.encabezado_id = @id " +
                    "  AND p.variacion = (SELECT MAX(variacion) FROM partidasdoc WHERE encabezado_id = @id) " +
                    "ORDER BY p.id_partidas";
                var partidas = RunQuery(query, parameters);

                query = "SELECT io.impuesto_id, io.importe, io.imp_variable, ci.cve_impuesto, ci.es_retencion " +
                    "FROM imp_oc io " +
                    "INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
                    "WHERE io.encabezado_id = @id";
                var impuestos = RunQuery(query, parameters);

                query = "SELECT nombre_original, path, uuid, extencion " +
                    "FROM archivos_compras_oc WHERE encabezado_id = @id ORDER BY id_archivo_compras";
                var archivos = RunQuery(query, parameters);

                return Json(new { success = true, orden = orden[0], partidas, impuestos, archivos });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al consultar la orden: " + ex.Message });
            }
        }

        // Totales de la orden tal como quedaron tras definir el metodo de pago.
        // Los impuestos viven en imp_oc, no en el encabezado.
        private const string QueryTotalesOrden =
            "SELECT em.id_encabezado, em.nat, em.estatus_id, em.tipo_proceso, em.ccy AS moneda, " +
            "   em.cli_prov AS proveedor, em.refe AS proveedor_id, cp.rfc AS rfc_proveedor, " +
            "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
            "   em.fch, em.coment_aut AS observaciones, " +
            "   COALESCE(em.sub, 0) AS subtotal, COALESCE(em.dto, 0) AS descuento, COALESCE(em.imp, 0) AS total, " +
            "   COALESCE((SELECT SUM(io.importe) FROM imp_oc io " +
            "             INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
            "             WHERE io.encabezado_id = em.id_encabezado AND ci.es_retencion = false), 0) AS traslados, " +
            "   COALESCE((SELECT SUM(ABS(io.importe)) FROM imp_oc io " +
            "             INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
            "             WHERE io.encabezado_id = em.id_encabezado AND ci.es_retencion = true), 0) AS retenciones " +
            "FROM encabezadomov em " +
            "LEFT JOIN catproveedores cp ON cp.id_prov = em.refe " +
            "WHERE em.id_encabezado = @id";

        /// <summary>
        /// Registra la factura del proveedor: guarda PDF y XML, marca la modalidad
        /// de pago y genera la poliza y la cartera. Antes de escribir nada confronta
        /// el CFDI contra la orden; si hay diferencias devuelve el reporte y espera
        /// la confirmacion explicita del usuario.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Registrar factura de orden de compra")]
        public JsonResult RegistrarFacturaOC(IFormCollection fc)
        {
            try
            {
                int idEncabezado = Convert.ToInt32(fc["id_encabezado"].ToString());
                string modalidad = GetString(fc["modalidad"].ToString());
                bool confirmarDiferencias = fc["confirmarDiferencias"].ToString() == "true";

                if (modalidad != "contado" && modalidad != "credito")
                {
                    return Json(new { success = false, message = "Selecciona si la orden es de contado o de credito." });
                }

                IFormFile xmlFile = Request.Form.Files["xml"];
                IFormFile pdfFile = Request.Form.Files["factura"];

                if (xmlFile == null || xmlFile.Length == 0)
                {
                    return Json(new { success = false, message = "Falta el XML de la factura." });
                }

                // ── 1. Leer el CFDI ──────────────────────────────────────────
                ComprobanteProveedor cfdi;
                try
                {
                    using var lector = new StreamReader(xmlFile.OpenReadStream());
                    cfdi = CfdiProveedorService.Leer(lector.ReadToEnd());
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "No se pudo leer el XML: " + ex.Message });
                }

                // ── 2. Confrontar contra la orden ────────────────────────────
                var parameters = new Dictionary<string, object> { ["id"] = idEncabezado };
                var orden = RunQuery(QueryTotalesOrden, parameters);

                if (!orden.Any())
                {
                    return Json(new { success = false, message = "No se encontro la orden de compra." });
                }

                var o = orden[0];
                var diferencias = CfdiProveedorService.Comparar(
                    cfdi,
                    ocSubtotal: GetDecimal(o["subtotal"], 0).Value,
                    ocDescuento: GetDecimal(o["descuento"], 0).Value,
                    ocTraslados: GetDecimal(o["traslados"], 0).Value,
                    ocRetenciones: GetDecimal(o["retenciones"], 0).Value,
                    ocTotal: GetDecimal(o["total"], 0).Value,
                    ocProveedor: GetString(o["proveedor"]),
                    ocRfcProveedor: GetString(o["rfc_proveedor"]),
                    ocMoneda: GetString(o["moneda"]));

                // El mismo CFDI no se puede aplicar a dos ordenes
                if (!string.IsNullOrWhiteSpace(cfdi.Uuid))
                {
                    var repetido = RunQuery(
                        "SELECT encabezado_id FROM factura_proveedor_oc WHERE uuid = @uuid",
                        new Dictionary<string, object> { ["uuid"] = cfdi.Uuid });

                    if (repetido.Any())
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Este CFDI ya esta registrado en otra orden de compra (documento {repetido[0]["encabezado_id"]})."
                        });
                    }
                }

                if (diferencias.Any() && !confirmarDiferencias)
                {
                    return Json(new
                    {
                        success = false,
                        requiereConfirmacion = true,
                        comprobante = cfdi,
                        diferencias
                    });
                }

                // ── 3. Guardar ──────────────────────────────────────────────
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                decimal montoCartera = 0;

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // Bloquea la orden: si llegan dos peticiones a la vez, la
                            // segunda encuentra la factura ya registrada y se detiene.
                            RunScalar("SELECT id_encabezado FROM encabezadomov WHERE id_encabezado = @id FOR UPDATE",
                                parameters, false, conn, tx);

                            int yaFacturada = Convert.ToInt32(RunScalar(
                                "SELECT COUNT(*) FROM archivos_compras_oc WHERE encabezado_id = @id AND extencion = '.xml'",
                                parameters, false, conn, tx));

                            if (yaFacturada > 0)
                            {
                                tx.Rollback();
                                return Json(new
                                {
                                    success = false,
                                    yaProcesado = true,
                                    message = "Esta orden de compra ya tiene una factura registrada."
                                });
                            }

                            string proveedor = GetString(o["proveedor"]);
                            GuardarArchivoOC(pdfFile, "factura", idEncabezado, proveedor, conn, tx);
                            GuardarArchivoOC(xmlFile, "xml", idEncabezado, proveedor, conn, tx);

                            // La modalidad de pago se refleja en el tipo de proceso
                            RunUpdate("UPDATE encabezadomov SET tipo_producto = @tipo_proceso, mdp = @mdp " +
                                "WHERE id_encabezado = @id_encabezado",
                                new Dictionary<string, object>
                                {
                                    ["tipo_proceso"] = modalidad == "credito" ? "OC_credito" : "OC_contado",
                                    ["mdp"] = cfdi.MetodoPago,
                                    ["id_encabezado"] = idEncabezado
                                }, false, conn, tx);

                            RunUpdate(
                                "INSERT INTO factura_proveedor_oc " +
                                "   (encabezado_id, uuid, serie, folio, rfc_emisor, nombre_emisor, fecha_emision, " +
                                "    moneda, metodo_pago, forma_pago, subtotal, descuento, impuestos, retenciones, " +
                                "    total, diferencias, registrado_por) " +
                                "VALUES (@encabezado_id, @uuid, @serie, @folio, @rfc_emisor, @nombre_emisor, @fecha_emision, " +
                                "    @moneda, @metodo_pago, @forma_pago, @subtotal, @descuento, @impuestos, @retenciones, " +
                                "    @total, @diferencias::jsonb, @registrado_por)",
                                new Dictionary<string, object>
                                {
                                    ["encabezado_id"] = idEncabezado,
                                    ["uuid"] = (object)cfdi.Uuid ?? DBNull.Value,
                                    ["serie"] = (object)cfdi.Serie ?? DBNull.Value,
                                    ["folio"] = (object)cfdi.Folio ?? DBNull.Value,
                                    ["rfc_emisor"] = (object)cfdi.RfcEmisor ?? DBNull.Value,
                                    ["nombre_emisor"] = (object)cfdi.NombreEmisor ?? DBNull.Value,
                                    ["fecha_emision"] = (object)cfdi.Fecha ?? DBNull.Value,
                                    ["moneda"] = (object)cfdi.Moneda ?? DBNull.Value,
                                    ["metodo_pago"] = (object)cfdi.MetodoPago ?? DBNull.Value,
                                    ["forma_pago"] = (object)cfdi.FormaPago ?? DBNull.Value,
                                    ["subtotal"] = cfdi.SubTotal,
                                    ["descuento"] = cfdi.Descuento,
                                    ["impuestos"] = cfdi.Traslados,
                                    ["retenciones"] = cfdi.Retenciones,
                                    ["total"] = cfdi.Total,
                                    ["diferencias"] = diferencias.Any() ? JsonConvert.SerializeObject(diferencias) : (object)DBNull.Value,
                                    ["registrado_por"] = GetUserId(User.Identity.Name)
                                }, false, conn, tx);

                            // La poliza y la cartera nacen aqui, no en la recepcion de almacen
                            var poliza = GenerarDatosPoliza(idEncabezado, null, null, conn, tx);
                            var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), idEncabezado, poliza, false, null, conn, tx);
                            var cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);
                            montoCartera = cartera?.MontoTotal ?? cfdi.Total;

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = idEncabezado,
                    Folio = GetString(o["folio"]),
                    Observaciones = $"CFDI {cfdi.FolioCompleto} ({cfdi.Uuid}) - {modalidad}" +
                                    (diferencias.Any() ? $" - {diferencias.Count} diferencia(s) aceptada(s)" : "")
                };

                return Json(new
                {
                    success = true,
                    icon = diferencias.Any() ? "warning" : "success",
                    title = "Factura registrada",
                    html = $"Se registro el CFDI <strong>{cfdi.FolioCompleto}</strong> en la orden {o["folio"]}.<br>" +
                        $"Se genero la cartera por <strong>{montoCartera:C2}</strong> como <strong>{modalidad}</strong>." +
                        (diferencias.Any()
                            ? $"<br><br>Se registraron {diferencias.Count} diferencia(s) contra la orden para su seguimiento."
                            : ""),
                    diferencias
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al registrar la factura: " + ex.Message });
            }
        }

        private void GuardarArchivoOC(IFormFile archivo, string tipo, int idEncabezado, string proveedor,
            NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            if (archivo == null || archivo.Length == 0) return;

            string extension = Path.GetExtension(archivo.FileName);
            string uuid = Guid.NewGuid().ToString();

            UploadFormFileToPath(RutaArchivosOC, archivo, uuid, extension);

            RunUpdate(
                "INSERT INTO archivos_compras_oc (nombre_original, path, uuid, extencion, encabezado_id, proveedor_nombre) " +
                "VALUES (@nombreOriginal, @ruta, @uuid, @extension, @encabezado_id, @proveedor_nombre)",
                new Dictionary<string, object>
                {
                    ["nombreOriginal"] = archivo.FileName,
                    ["ruta"] = RutaArchivosOC + uuid + extension,
                    ["uuid"] = uuid,
                    ["extension"] = extension,
                    ["encabezado_id"] = idEncabezado,
                    ["proveedor_nombre"] = proveedor
                }, false, conn, tx);
        }

        #endregion
    }
}
