using BOS_ERP.Controllers;
using BOS_ERP.Models;
using DocumentFormat.OpenXml.Office.CoverPageProps;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using ServiceReference2;
using System.Text.Json;

namespace BOS_ERP.controllers
{
    public partial class OperacionesController : Utilities
    {
        private readonly IConfiguration _configuration;

        public OperacionesController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult ImportarDocumentosKepler()
        {
            var returnResult = new Dictionary<string, List<Dictionary<string, object>>>();
            string query = "SELECT c1 clave, c2 nombre FROM kdms ORDER BY c1 ASC";
            var sucursales = RunQuery(query, new Dictionary<string, object>(), false, null, null, "SRS");
            returnResult.Add("sucursales", sucursales);

            return View(returnResult);
        }

        public JsonResult ObtenerClientes()
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string query = @"
                    SELECT DISTINCT 
                        c.id_cliente as id,
                        c.cve_cli as codigo,
                        c.n_cli as nombre,
                        c.pl_crd as plazo,
                        c.cve_cli || ' - ' || c.n_cli AS cliente
                    FROM catclientes c
                    WHERE c.empresa_id = @empresa_id
                    ORDER BY c.cve_cli";

                var clientes = RunQuery(query, parameters);

                return Json(new
                {
                    success = true,
                    clientes = clientes
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        public JsonResult ObtenerProveedores()
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string query = @"
                    SELECT DISTINCT 
                        c.id_prov as id,
                        c.cve_prov as codigo,
                        c.n_prov as nombre,
                        c.cve_prov || ' - ' || c.n_prov AS cliente
                    FROM catproveedores c
                    WHERE c.id_empresa = @empresa_id
                    ORDER BY c.cve_prov";

                var proveedores = RunQuery(query, parameters);

                return Json(new
                {
                    success = true,
                    proveedores = proveedores
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        public JsonResult ConsultarRegistrosKepler(string nombre, string sortColumn, string sortDir, string genero, string naturaleza, string grupo, string tipo,
            string[] clientes, DateTime? fechaInicio, DateTime? fechaFin, string sucursal, string folio = null, int page = 1, int pageSize = 50)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(genero) || string.IsNullOrWhiteSpace(naturaleza) || string.IsNullOrWhiteSpace(grupo) || string.IsNullOrWhiteSpace(tipo))
                {
                    return Json(new { icon = "warning", title = "Advertencia", text = "Debe completar Género, Naturaleza, Grupo y Tipo" });
                }

                if (clientes == null || clientes.Length == 0)
                {
                    return Json(new { icon = "warning", title = "Advertencia", text = "Debe seleccionar al menos un cliente" });
                }

                if (!fechaInicio.HasValue)
                    fechaInicio = DateTime.Now.AddMonths(-3);

                if (!fechaFin.HasValue)
                    fechaFin = DateTime.Now;

                string connection = HttpContext.Session.GetString("EmpresaFactura");

                clientes = clientes?
                .SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToArray();

                var parameters = new Dictionary<string, object>
                {
                    { "genero", genero },
                    { "naturaleza", naturaleza },
                    { "grupo", Convert.ToInt32(grupo) },
                    { "tipo", Convert.ToInt32(tipo) },
                    { "clientes", clientes },
                    { "fechaInicio", fechaInicio.Value },
                    { "fechaFin", fechaFin.Value },
                    { "nombre", nombre ?? "" },
                    { "offset", (page - 1) * pageSize },
                    { "pageSize", pageSize }
                };

                bool filtrarPorFolio = !string.IsNullOrWhiteSpace(folio);
                string folioInput = "";
                if (filtrarPorFolio)
                {
                    parameters.Add("@folio", folio);
                    folioInput += " AND kd.c6 = @folio ";
                }

                if (!string.IsNullOrWhiteSpace(sucursal))
                {
                    parameters.Add("sucursal", sucursal);
                    folioInput += " AND kd.c1 = @sucursal ";
                }

                string query;

                // Query para otros documentos (sin JOIN)
                query = $"SELECT  kd.c10 as usuario, ku.c3 nombre_cliente, kd.c6 as folio, kd.c68 as fecha_captura, kd.c38 as tipo_documento, kd.c90 as forma_pago, (kd.c16 - kd.c14) as subtotal, " +
                    "  kd.c13 as descuento, kd.c7 as moneda, kd.c40 as tipo_cambio, kd.c16 as total, kd.c30 as plazo, '' as tipo_comprobante, '' as metodo_pago, " +
                    "  kd.c35 as cp_receptor, '' as rfc_emisor, '' as razon_emisor, '' as regimen_receptor, kd.c22 as rfc_receptor, kd.c32 as razon_receptor, " +
                    "  kd.c84 as uso_cfdi, 0 as iva_porcentual, 0 as iva_retenido, 0 as isr_retenido, 0 as total_impuestos_retenidos, kd.c14 as total_impuestos_traslado, " +
                    "  '' as regimen_emisor, kd.c2 || kd.c3 || kd.c4 || kd.c5 || '-' || kd.c6 as folio_kepler, kd.c43 as estatus, NULL as uuid, " +
                    "  kd.c6::text as id_kepler, 'otro' as tipo_registro, k.c5 nombre_documento, kd.c1 as sucursal " +
                    "FROM kdm1 kd " +
                    "JOIN kdmm k ON k.c1 = kd.c2 AND k.c2 = kd.c3 AND k.c3 = kd.c4 AND k.c4 = kd.c5 " +
                    "JOIN kdud ku ON ku.c2 = kd.c10 " +
                    "WHERE kd.c10 = ANY(@clientes) AND kd.c2 = @genero AND kd.c3 = @naturaleza AND kd.c4 = @grupo AND kd.c5 = @tipo " +
                    $"  AND kd.c9 >= @fechaInicio AND kd.c9 <= @fechaFin  {folioInput} " +
                    $"ORDER BY kd.c6, kd.c13 ASC " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var rows = RunQuery(query, parameters, false, null, null, connection);

                query = "SELECT COUNT(*) " +
                    "FROM kdm1 kd " +
                    "WHERE kd.c10 = ANY(@clientes) AND kd.c2 = @genero AND kd.c3 = @naturaleza AND kd.c4 = @grupo AND kd.c5 = @tipo " +
                    $"   AND kd.c9 BETWEEN @fechaInicio AND @fechaFin AND kd.c43 IN ('N', 'R') {folioInput} ";
                int total = Convert.ToInt32(RunScalar(query, parameters, false, null, null, connection));

                if (rows.Count == 0)
                {
                    return Json(new { icon = "info", title = "Sin resultados", text = "No se encontraron registros para los filtros aplicados" });
                }

                var registrosKepler = rows.Select(r =>
                {
                    RegistroKeplerModel registroKepler = new RegistroKeplerModel();
                    registroKepler.Usuario = r["usuario"]?.ToString();
                    registroKepler.Folio = r["folio"]?.ToString();
                    registroKepler.FechaCaptura = r["fecha_captura"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(r["fecha_captura"]);
                    registroKepler.TipoDocumento = r["tipo_documento"]?.ToString();
                    registroKepler.FormaPago = r["forma_pago"]?.ToString();
                    registroKepler.Subtotal = r["subtotal"] == DBNull.Value ? 0 : Convert.ToDecimal(r["subtotal"]);
                    registroKepler.Descuento = r["descuento"] == DBNull.Value ? 0 : Convert.ToDecimal(r["descuento"]);
                    registroKepler.Moneda = r["moneda"]?.ToString();
                    registroKepler.TipoCambio = r["tipo_cambio"] == DBNull.Value ? 0 : Convert.ToDecimal(r["tipo_cambio"]);
                    registroKepler.Total = r["total"] == DBNull.Value ? 0 : Convert.ToDecimal(r["total"]);
                    registroKepler.Plazo = r["plazo"]?.ToString();
                    registroKepler.TipoComprobante = r["tipo_comprobante"]?.ToString();
                    registroKepler.MetodoPago = r["metodo_pago"]?.ToString();
                    registroKepler.CPReceptor = r["cp_receptor"]?.ToString();
                    registroKepler.RFCEmisor = r["rfc_emisor"]?.ToString();
                    registroKepler.RazonEmisor = r["razon_emisor"]?.ToString();
                    registroKepler.RegimenReceptor = r["regimen_receptor"]?.ToString();
                    registroKepler.RFCReceptor = r["rfc_receptor"]?.ToString();
                    registroKepler.RazonReceptor = r["razon_receptor"]?.ToString();
                    registroKepler.UsoCFDI = r["uso_cfdi"]?.ToString();
                    registroKepler.IVAPorcentual = r["iva_porcentual"] == DBNull.Value ? 0 : Convert.ToDecimal(r["iva_porcentual"]);
                    registroKepler.IVARetenido = r["iva_retenido"] == DBNull.Value ? 0 : Convert.ToDecimal(r["iva_retenido"]);
                    registroKepler.ISRRetenido = r["isr_retenido"] == DBNull.Value ? 0 : Convert.ToDecimal(r["isr_retenido"]);
                    registroKepler.TotalImpuestosRetenidos = r["total_impuestos_retenidos"] == DBNull.Value ? 0 : Convert.ToDecimal(r["total_impuestos_retenidos"]);
                    registroKepler.TotalImpuestosTraslado = r["total_impuestos_traslado"] == DBNull.Value ? 0 : Convert.ToDecimal(r["total_impuestos_traslado"]);
                    registroKepler.FolioKepler = r["folio_kepler"]?.ToString();
                    registroKepler.Estatus = r["estatus"]?.ToString();
                    registroKepler.Uuid = r["uuid"]?.ToString();
                    registroKepler.IdKepler = r["id_kepler"]?.ToString();
                    registroKepler.TipoRegistro = r["tipo_registro"]?.ToString();
                    registroKepler.NombreDocumento = r["nombre_documento"]?.ToString();
                    registroKepler.Sucursal = r["sucursal"]?.ToString();
                    registroKepler.NombreCliente = r["nombre_cliente"]?.ToString();
                    return registroKepler;
                }).ToList();

                var registrosConEstado = VerificarRegistrosExistentes(registrosKepler);

                var resumenPorCliente = registrosConEstado
                    .GroupBy(r => r.Usuario)
                    .Select(g => new
                    {
                        Cliente = g.Key,
                        TotalRegistros = g.Count(),
                        RegistrosNuevos = g.Count(r => !r.YaExiste),
                        RegistrosExistentes = g.Count(r => r.YaExiste),
                        TotalMonto = g.Sum(r => r.Total),
                        MontoNuevo = g.Where(r => !r.YaExiste).Sum(r => r.Total)
                    }).ToList();

                HttpContext.Session.SetString("RegistrosKeplerConsultados", JsonConvert.SerializeObject(registrosConEstado));

                return Json(new { data = registrosConEstado, total, resumen = resumenPorCliente });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error en la consulta", text = ex.Message });
            }
        }


        private List<RegistroKeplerModel> VerificarRegistrosExistentes(List<RegistroKeplerModel> registros)
        {
            try
            {
                // Para otros documentos: verificar por folio_kepler en cartera_clientes
                var foliosKepler = registros
                    .Where(r => !string.IsNullOrWhiteSpace(r.FolioKepler))
                    .Select(r => r.FolioKepler)
                    .Distinct()
                    .ToList();

                if (foliosKepler.Count == 0)
                    return registros;

                var parametersFolios = new Dictionary<string, object>();
                string foliosIn = string.Join(",", foliosKepler.Select((f, i) =>
                {
                    parametersFolios.Add($"@folio{i}", f);
                    return $"@folio{i}";
                }));

                string query = "SELECT DISTINCT cc.folio_kepler " +
                    "FROM encabezadomov cc " +
                    $"WHERE cc.folio_kepler IN ({foliosIn})";

                var foliosExistentes = RunQuery(query, parametersFolios)
                    .Select(row => row["folio_kepler"]?.ToString())
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToHashSet();

                foreach (var registro in registros)
                {
                    if (!string.IsNullOrWhiteSpace(registro.FolioKepler))
                    {
                        registro.YaExiste = foliosExistentes.Contains(registro.FolioKepler);
                    }
                }

                return registros;
            }
            catch (Exception ex)
            {
                // En caso de error, devolver registros sin marcar
                return registros;
            }
        }

        /// <summary>
        /// Guarda los registros seleccionados en cartera_clientes
        /// </summary>
        public JsonResult GuardarRegistrosSeleccionados(string[] registrosIds, bool tiene_uuid, string[] registrosUuid)
        {
            try
            {
                // Recuperar registros de la sesión
                List<RegistroKeplerModel> todosLosRegistros =
                    JsonConvert.DeserializeObject<List<RegistroKeplerModel>>(HttpContext.Session.GetString("RegistrosKeplerConsultados"));

                bool esFactura = bool.Parse(HttpContext.Session.GetString("EsFacturaKepler") ?? "false");

                if (todosLosRegistros == null || todosLosRegistros.Count == 0)
                {
                    return Json(new
                    {
                        icon = "error",
                        title = "Error",
                        text = "No hay registros en sesión. Por favor, realice la consulta nuevamente."
                    });
                }

                var registrosSeleccionados = new List<RegistroKeplerModel>();
                // Filtrar solo los registros seleccionados que NO existen
                if (tiene_uuid)
                {
                    registrosSeleccionados = todosLosRegistros
                    .Where(r => registrosUuid.Contains(r.Uuid) && !r.YaExiste)
                    .ToList();
                }
                else
                {
                    registrosSeleccionados = todosLosRegistros
                    .Where(r => registrosIds.Contains(r.IdKepler) && !r.YaExiste)
                    .ToList();
                }

                string json = JsonConvert.SerializeObject(todosLosRegistros, Formatting.Indented);

                if (registrosSeleccionados.Count == 0)
                {
                    return Json(new
                    {
                        icon = "warning",
                        title = "Advertencia",
                        text = "No hay registros nuevos para guardar"
                    });
                }

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // Agrupar por cliente
                            var registrosPorCliente = registrosSeleccionados
                                .GroupBy(r => r.Usuario)
                                .ToList();

                            int totalProcesados = 0;

                            foreach (var grupoCliente in registrosPorCliente)
                            {
                                string codigoCliente = grupoCliente.Key;
                                var registrosCliente = grupoCliente.ToList();
                                var primerRegistro = registrosCliente.First();

                                // Obtener información del cliente
                                var parametersCliente = new Dictionary<string, object>
                                {
                                    { "cve_cli", codigoCliente },
                                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                                };

                                string query = @"
                                    SELECT id_cliente, pl_crd 
                                    FROM catclientes 
                                    WHERE cve_cli = @cve_cli AND empresa_id = @empresa_id";

                                var dtCliente = RunQuery(query, parametersCliente, false, conn, tx);

                                if (dtCliente.Count == 0)
                                {
                                    throw new Exception($"No se encontró el cliente con código: {codigoCliente}");
                                }

                                int clienteID = Convert.ToInt32(dtCliente[0]["id_cliente"]);
                                int plazo = Convert.ToInt32(dtCliente[0]["pl_crd"]);

                                // Obtener forma de pago
                                int formaPago = 1; // Valor por defecto
                                if (!string.IsNullOrWhiteSpace(primerRegistro.FormaPago))
                                {
                                    var parametersFormaPago = new Dictionary<string, object>
                                    {
                                        { "cve_sat", primerRegistro.FormaPago }
                                    };
                                    query = "SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat";
                                    var formaPagoResult = RunScalar(query, parametersFormaPago, false, conn, tx);

                                    if (formaPagoResult != null)
                                        formaPago = Convert.ToInt32(formaPagoResult);
                                }

                                // Calcular totales para este cliente
                                decimal totalGeneral = registrosCliente.Sum(r => r.Total);

                                // Procesar cada registro
                                foreach (var registro in registrosCliente)
                                {
                                    var encabezadoFactura = new DocumentoEncabezado();
                                    encabezadoFactura.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                                    encabezadoFactura.IdArea = 7;
                                    encabezadoFactura.IdTpDoc = 69;
                                    encabezadoFactura.Anio = DateTime.Now.Year;
                                    encabezadoFactura.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                                    encabezadoFactura.Fch = DateTime.Now;
                                    encabezadoFactura.TpMov = "RICD";
                                    encabezadoFactura.UsrDoc = User.Identity.Name;
                                    encabezadoFactura.FchCap = DateTime.Now;
                                    encabezadoFactura.Usr0 = GetUserId(User.Identity.Name);
                                    encabezadoFactura.Fch0 = DateTime.Now;
                                    encabezadoFactura.CliProv = codigoCliente;
                                    encabezadoFactura.Ref = clienteID;
                                    encabezadoFactura.Estatus = 1;
                                    encabezadoFactura.UsrDep = GetAreaName(User.Identity.Name);
                                    encabezadoFactura.TipoPoceso = "registro_carteras_cliente";
                                    encabezadoFactura.Mdp = registro.MetodoPago;
                                    encabezadoFactura.CFDI = primerRegistro.UsoCFDI;
                                    encabezadoFactura.FPago = formaPago;
                                    encabezadoFactura.Imp = registro.Total;
                                    encabezadoFactura.CentroCostos = 5;

                                    var partidas = new List<PartidaDocumento>();
                                    var documentoFactura = GenerarDocumentoConPartidas(encabezadoFactura, partidas, conn, tx);

                                    List<PolizaData> poliza = GenerarDatosPoliza(Convert.ToInt32(documentoFactura["IdEncabezado"]), null, null, conn, tx);
                                    var polizaId = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documentoFactura["IdEncabezado"]), poliza, false, null, conn, tx);

                                    if (esFactura && !string.IsNullOrWhiteSpace(registro.Uuid))
                                    {
                                        var parametersFactura = new Dictionary<string, object>();
                                        parametersFactura.Add("serie", "RICD");
                                        parametersFactura.Add("folio", documentoFactura["folio_generado"].ToString());
                                        parametersFactura.Add("idcliente", clienteID);
                                        parametersFactura.Add("idtipopago", formaPago);
                                        parametersFactura.Add("moneda", registro.Moneda ?? "MXN");
                                        parametersFactura.Add("cpe", "78394");
                                        parametersFactura.Add("rfcemisor", registro.RFCEmisor ?? "");
                                        parametersFactura.Add("rsoemisor", registro.RazonEmisor ?? "");
                                        parametersFactura.Add("reg_fise", "601");
                                        parametersFactura.Add("rfccliente", registro.RFCReceptor ?? "");
                                        parametersFactura.Add("rsocliente", registro.RazonReceptor ?? "");
                                        parametersFactura.Add("cpr", registro.CPReceptor ?? "");
                                        parametersFactura.Add("idusocfdi", registro.UsoCFDI ?? "");
                                        parametersFactura.Add("reg_fisr", registro.RegimenReceptor ?? "");
                                        parametersFactura.Add("subtotal", registro.Subtotal);
                                        parametersFactura.Add("iva", registro.TotalImpuestosTraslado);
                                        parametersFactura.Add("total", registro.Total);
                                        parametersFactura.Add("mdpfactura", registro.MetodoPago ?? "");
                                        parametersFactura.Add("fecha", registro.FechaCaptura);
                                        parametersFactura.Add("uuid", Guid.Parse(registro.Uuid));
                                        parametersFactura.Add("descuento", registro.Descuento);
                                        parametersFactura.Add("fechatimbrado", registro.FechaCaptura);
                                        parametersFactura.Add("encabezado_id", Convert.ToInt32(documentoFactura["IdEncabezado"]));
                                        parametersFactura.Add("parcialidad", registro.Plazo ?? "0");

                                        query = "INSERT INTO factura " +
                                            "   (serie, folio, idcliente, rfccliente, rsocliente, rfcemisor, rsoemisor, fecha, fechatimbrado, mdpfactura, idtipopago, " +
                                            "   \"uuid\", descuento, subtotal, iva, parcialidad, total, moneda, idusocfdi, reg_fisr, reg_fise, cpr, cpe, encabezado_id) " +
                                            "VALUES " +
                                            "   (@serie, @folio, @idcliente, @rfccliente, @rsocliente, @rfcemisor, @rsoemisor, @fecha, @fechatimbrado, @mdpfactura, " +
                                            "   @idtipopago, @uuid, @descuento, @subtotal, @iva, @parcialidad, @total, @moneda, @idusocfdi, @reg_fisr, @reg_fise, @cpr, " +
                                            "   @cpe, @encabezado_id)";

                                        RunQuery(query, parametersFactura, false, conn, tx);
                                    }

                                    // Insertar en cartera (siempre)
                                    var parametersCartera = new Dictionary<string, object>
                                    {
                                        { "cliente_id", clienteID },
                                        { "encabezado_id", Convert.ToInt32(documentoFactura["IdEncabezado"]) },
                                        { "fecha_emision", registro.FechaCaptura },
                                        { "fecha_vencimiento", registro.FechaCaptura.AddDays(plazo) },
                                        { "monto_total", registro.Total },
                                        { "saldo_pendiente", registro.Total },
                                        { "poliza_id", polizaId[0].idPoliza },
                                        { "creado_por", GetUserId(User.Identity.Name) },
                                        { "fecha_creacion", DateTime.Now },
                                        { "folio_kepler", registro.FolioKepler ?? "" },
                                        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                                    };

                                    query = "INSERT INTO cartera_clientes " +
                                        "   (cliente_id, encabezado_id, fecha_emision, fecha_vencimiento, monto_total, saldo_pendiente, " +
                                        "   estado, poliza_id, creado_por, fecha_creacion, \"uuid\", cancelada, folio_kepler, empresa_id) " +
                                        "VALUES" +
                                        "   (@cliente_id, @encabezado_id, @fecha_emision, @fecha_vencimiento, @monto_total, @saldo_pendiente,  " +
                                        "   'pendiente', @poliza_id, @creado_por, @fecha_creacion, gen_random_uuid(), false, @folio_kepler, @empresa_id);";

                                    RunQuery(query, parametersCartera, false, conn, tx);

                                    totalProcesados++;
                                }
                            }

                            // Limpiar sesión
                            HttpContext.Session.SetString("RegistrosKeplerConsultados", null);
                            HttpContext.Session.SetString("EsFacturaKepler", null);

                            tx.Commit();

                            return Json(new { icon = "success", title = "Éxito", text = $"Se procesaron correctamente {totalProcesados} registro(s) de {registrosPorCliente.Count} cliente(s)" });
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error inesperado", text = ex.Message });
            }
        }
        // Agregar este método al CarterasController.cs

        public JsonResult ObtenerDescripcionDocumento(string genero, string naturaleza, string grupo, string tipo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(genero) ||
                    string.IsNullOrWhiteSpace(naturaleza) ||
                    string.IsNullOrWhiteSpace(grupo) ||
                    string.IsNullOrWhiteSpace(tipo))
                {
                    return Json(new { success = false, descripcion = "" });
                }

                string connection = HttpContext.Session.GetString("EmpresaFactura");

                var parameters = new Dictionary<string, object>
                {
                    { "@genero", genero },
                    { "@naturaleza", naturaleza },
                    { "@grupo",  Convert.ToInt32(grupo) },
                    { "@tipo",  Convert.ToInt32(tipo) }
                };

                string query = "SELECT c5 as descripcion " +
                    "FROM sellosop.KDMM " +
                    "WHERE c1 = @genero AND c2 = @naturaleza AND c3 = @grupo AND c4 = @tipo " +
                    "LIMIT 1";

                var result = RunQuery(query, parameters, false, null, null, "SRS");

                if (result.Count > 0)
                {
                    string descripcion = result[0]["descripcion"]?.ToString() ?? "";
                    return Json(new { success = true, descripcion = descripcion });
                }

                return Json(new { success = false, descripcion = "No se encontró configuración para esta combinación" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, descripcion = "", error = ex.Message });
            }
        }

        public JsonResult GetPartidasDocumento(string genero, string naturaleza, int grupo, int tipo, string folio, string sucursal, string clientes)
        {
            var parameters = new Dictionary<string, object>
            {
                { "genero", genero },
                { "naturaleza", naturaleza },
                { "grupo", grupo },
                { "tipo", tipo },
                { "folio", folio },
                { "sucursal", sucursal },
                { "cliente", clientes }
            };

            string query = "SELECT k2.c2 nombre_sucursal, k.c2 || k.c3 || k.c4 || k.c5 || '-' || k.c6 folio_kepler, k.c8 clave, k.c9 cantidad, k.c10 descripcion, " +
                "   k.c11 unidad, k.c12 precio_unitario, k.c13 importe_total, k.c25 clave_cliente, k.c36 moneda, k.c17 iva_porcentual " +
                "FROM kdm2 k " +
                "LEFT JOIN kdms k2 ON k2.c1 = k.c1 " +
                "WHERE k.c2 = @genero AND k.c3 = @naturaleza AND k.c4 = @grupo AND k.c5 = @tipo AND k.c6 = @folio AND k.c1 = @sucursal AND k.c25 = @cliente";
            var partidas = RunQuery(query, parameters, false, null, null, "SRS");

            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            return Json(new { partidas = partidas, datos_empresa = datosEmpresa });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult ConvertKeplerDocuments(string documentosJson, string clientes)
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var documentos = System.Text.Json.JsonSerializer.Deserialize<List<KeplerDocumentDto>>(documentosJson, jsonOptions) ?? new List<KeplerDocumentDto>();

                if (documentos.Count == 0)
                {
                    return Json(new { icon = "warning", title = "Advertencia", text = "No se recibieron documentos" });
                }

                List<DocumentoEncabezado> encabezados = new List<DocumentoEncabezado>();
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using var conn = new NpgsqlConnection(connStr);
                conn.Open();
                using var tx = conn.BeginTransaction();
                try
                {
                    foreach (var documento in documentos)
                    {
                        var parameters = new Dictionary<string, object>();
                        parameters.Add("genero", documento.Genero);
                        parameters.Add("naturaleza", documento.Naturaleza);
                        parameters.Add("grupo", documento.Grupo);
                        parameters.Add("tipo", documento.Tipo);
                        parameters.Add("cliente", documento.Cliente);
                        parameters.Add("folio", documento.IdKepler);
                        parameters.Add("sucursal", documento.Sucursal);

                        string query = "SELECT idtpdoc " +
                            "FROM tpdoc_doc_kepler_rel tdk " +
                            "LEFT JOIN definicion_doc_kepler ddk ON ddk.id_doc = tdk.id_doc " +
                            "WHERE ddk.genero = @genero AND ddk.naturaleza = @naturaleza AND ddk.grupo = @grupo::text AND ddk.tipo = @tipo::text";
                        object result = RunScalar(query, parameters, false, conn, tx);

                        if (result == null || result == DBNull.Value)
                        {
                            throw new Exception("No se encontró un tipo de documento para la combinación de género, naturaleza, grupo y tipo.");
                        }

                        int tipo_documento = Convert.ToInt32(result);

                        query = "SELECT idtpdoc, abreviaturatpdoc, idarea " +
                            "FROM tpdoc " +
                            "WHERE idtpdoc = @tipo_documento";
                        parameters.Add("tipo_documento", tipo_documento);
                        var tipos = RunQuery(query, parameters, false, conn, tx);

                        string connection = HttpContext.Session.GetString("EmpresaFactura");
                        query = "SELECT  kd.c10 as usuario, ku.c3 nombre_cliente, kd.c6 as folio, kd.c68 as fecha_captura, kd.c38 as tipo_documento, kd.c90 as forma_pago, " +
                            "  (kd.c16 - kd.c14) as subtotal, kd.c13 as descuento, kd.c7 as moneda, kd.c40 as tipo_cambio, kd.c16 as total, kd.c30 as plazo, '' as tipo_comprobante, " +
                            "  '' as metodo_pago, kd.c35 as cp_receptor, '' as rfc_emisor, '' as razon_emisor, '' as regimen_receptor, kd.c22 as rfc_receptor, kd.c32 as razon_receptor, " +
                            "  kd.c84 as uso_cfdi, 0 as iva_porcentual, 0 as iva_retenido, 0 as isr_retenido, 0 as total_impuestos_retenidos, kd.c14 as total_impuestos_traslado, " +
                            "  '' as regimen_emisor, kd.c2 || kd.c3 || kd.c4 || kd.c5 || '-' || kd.c6 as folio_kepler, kd.c43 as estatus, NULL as uuid, " +
                            "  kd.c6::text as id_kepler, 'otro' as tipo_registro, k.c5 nombre_documento, kd.c1 as sucursal " +
                            "FROM kdm1 kd " +
                            "JOIN kdmm k ON k.c1 = kd.c2 AND k.c2 = kd.c3 AND k.c3 = kd.c4 AND k.c4 = kd.c5 " +
                            "JOIN kdud ku ON ku.c2 = kd.c10 " +
                            "WHERE kd.c10 = @cliente AND kd.c2 = @genero AND kd.c3 = @naturaleza AND kd.c4 = @grupo AND kd.c5 = @tipo AND kd.c6 = @folio " +
                            $"ORDER BY kd.c6, kd.c13 ASC";
                        var doc = RunQuery(query, parameters, false, null, null, connection);

                        query = "SELECT id_cliente " +
                            "FROM catclientes " +
                            "WHERE cve_cli = @cliente";
                        int refe = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                        var encabezado = new DocumentoEncabezado();
                        encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                        encabezado.IdArea = Convert.ToInt32(tipos[0]["idarea"]);
                        encabezado.IdTpDoc = Convert.ToInt32(tipos[0]["idtpdoc"]);
                        encabezado.UsrDep = GetAreaName(User.Identity.Name);
                        encabezado.Anio = DateTime.Now.Year;
                        encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                        encabezado.Fch = DateTime.Now;
                        encabezado.TpMov = tipos[0]["abreviaturatpdoc"].ToString();
                        encabezado.UsrDoc = User.Identity.Name;
                        encabezado.FchCap = DateTime.Now;
                        encabezado.Usr0 = GetUserId(User.Identity.Name);
                        encabezado.Fch0 = DateTime.Now;
                        encabezado.Imp = Convert.ToDecimal(doc[0]["total"]);
                        encabezado.CliProv = $"{documento.Cliente}";
                        encabezado.Ref = refe;
                        encabezado.Estatus = 1;
                        encabezado.Ccy = doc[0]["moneda"].ToString();

                        var listaPartidas = new List<PartidaDocumento>();
                        query = "SELECT k2.c2 nombre_sucursal, k.c2 || k.c3 || k.c4 || k.c5 || '-' || k.c6 folio_kepler, k.c8 clave, k.c9 cantidad, k.c10 descripcion, " +
                            "   k.c11 unidad, k.c12 precio_unitario, k.c13 importe_total, k.c25 clave_cliente, k.c36 moneda, k.c17 iva_porcentual " +
                            "FROM kdm2 k " +
                            "LEFT JOIN kdms k2 ON k2.c1 = k.c1 " +
                            "WHERE k.c2 = @genero AND k.c3 = @naturaleza AND k.c4 = @grupo AND k.c5 = @tipo AND k.c6 = @folio AND k.c1 = @sucursal AND k.c25 = @cliente";
                        var partidas = RunQuery(query, parameters, false, null, null, "SRS");

                        int nro = 1;
                        foreach (var partida in partidas)
                        {
                            listaPartidas.Add(new PartidaDocumento
                            {
                                NroPart = nro++,
                                CveProd = GetString(partida["clave"]),
                                DescrProd = GetString(partida["descripcion"]),
                                CantUd = GetDecimal(partida["cantidad"]),
                                PvProd = GetDecimal(partida["precio_unitario"]),
                                CtoVtaPart = GetDecimal(partida["importe_total"]),
                                ImpPart = GetDecimal(partida["importe_total"]),
                                Ud = GetString(partida["unidad"], "PZA")
                            });
                        }

                        var documentoCreado = GenerarDocumentoConPartidas(encabezado, listaPartidas, conn, tx);

                        query = "INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom) " +
                            "VALUES (@encabezado_id, @impuesto, @subtotal, @importe, 1, 16, @prov_nom)";

                        parameters = new Dictionary<string, object>();
                        parameters.Add("impuesto", Convert.ToInt32(GetSetting("impuesto")));
                        parameters.Add("encabezado_id", Convert.ToInt32(documentoCreado["IdEncabezado"]));
                        parameters.Add("subtotal", GetDecimal(doc[0]["subtotal"]));
                        parameters.Add("importe", GetDecimal(doc[0]["total_impuestos_traslado"]));
                        parameters.Add("prov_nom", GetString(doc[0]["nombre_cliente"]));
                        RunUpdate(query, parameters, false, conn, tx);

                        query = "UPDATE encabezadomov SET folio_kepler = @folio WHERE id_encabezado = @encabezado";
                        parameters.Add("folio", $"{documento.Genero}{documento.Naturaleza}{documento.Grupo}{documento.Tipo}-{documento.IdKepler}");
                        parameters.Add("encabezado", Convert.ToInt32(documentoCreado["IdEncabezado"]));
                        RunUpdate(query, parameters, false, conn, tx);

                    }
                    tx.Commit();

                    return Json(new { icon = "success", title = "Documentos guardados", text = $"Se procesaron {documentos.Count} documentos correctamente" });
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Json(new { icon = "error", title = "Datos inválidos", text = $"No se pudo interpretar la información enviada: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", text = ex.Message });
            }
        }
    }



    #region Modelos    
    public class KeplerDocumentDto
    {
        public string IdKepler { get; set; }
        public string Genero { get; set; }
        public string Naturaleza { get; set; }
        public int Grupo { get; set; }
        public int Tipo { get; set; }
        public string Sucursal { get; set; }
        public string Cliente { get; set; }
        public string NombreCliente { get; set; }
    }
    public class RegistroKeplerModel
    {
        public string Usuario { get; set; }
        public string Folio { get; set; }
        public DateTime FechaCaptura { get; set; }
        public string TipoDocumento { get; set; }
        public string FormaPago { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Descuento { get; set; }
        public string Moneda { get; set; }
        public decimal TipoCambio { get; set; }
        public decimal Total { get; set; }
        public string Plazo { get; set; }
        public string TipoComprobante { get; set; }
        public string MetodoPago { get; set; }
        public string CPReceptor { get; set; }
        public string RFCEmisor { get; set; }
        public string RazonEmisor { get; set; }
        public string RegimenReceptor { get; set; }
        public string RFCReceptor { get; set; }
        public string RazonReceptor { get; set; }
        public string UsoCFDI { get; set; }
        public decimal IVAPorcentual { get; set; }
        public decimal IVARetenido { get; set; }
        public decimal ISRRetenido { get; set; }
        public decimal TotalImpuestosRetenidos { get; set; }
        public decimal TotalImpuestosTraslado { get; set; }
        public string FolioKepler { get; set; }
        public string Estatus { get; set; }
        public string Uuid { get; set; }
        public string IdKepler { get; set; }
        public bool YaExiste { get; set; }
        public string TipoRegistro { get; set; } // 'factura' o 'otro'
        public string NombreDocumento { get; set; }
        public string Sucursal { get; set; }
        public string NombreCliente { get; set; }
    }

    #endregion
}
