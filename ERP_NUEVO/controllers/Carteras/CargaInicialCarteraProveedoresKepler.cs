using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using System.Data;

namespace BOS_ERP.Controllers
{
    public partial class CarterasController : Utilities
    {

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
                        c.pl_crd as plazo,
                        c.cve_prov || ' - ' || c.n_prov AS ""displayText""
                    FROM catproveedores c
                    WHERE c.id_empresa = @empresa_id
                    ORDER BY c.cve_prov";

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

        public JsonResult ConsultarRegistrosProveedoresKepler(
            string genero,
            string naturaleza,
            string grupo,
            string tipo,
            string[] clientes,
            DateTime? fechaInicio,
            DateTime? fechaFin,
            bool esFactura = true)  // Nuevo parámetro
        {
            try
            {
                // Validar parámetros requeridos
                if (string.IsNullOrWhiteSpace(genero) ||
                    string.IsNullOrWhiteSpace(naturaleza) ||
                    string.IsNullOrWhiteSpace(grupo) ||
                    string.IsNullOrWhiteSpace(tipo))
                {
                    return Json(new
                    {
                        icon = "warning",
                        title = "Advertencia",
                        text = "Debe completar Género, Naturaleza, Grupo y Tipo"
                    });
                }

                if (clientes == null || clientes.Length == 0)
                {
                    return Json(new
                    {
                        icon = "warning",
                        title = "Advertencia",
                        text = "Debe seleccionar al menos un cliente"
                    });
                }

                if (!fechaInicio.HasValue)
                    fechaInicio = DateTime.Now.AddMonths(-3);

                if (!fechaFin.HasValue)
                    fechaFin = DateTime.Now;

                string connection = HttpContext.Session.GetString("EmpresaFactura");

                var parameters = new Dictionary<string, object>
                    {
                        { "@genero", genero },
                        { "@naturaleza", naturaleza },
                        { "@grupo", Convert.ToInt32(grupo) },
                        { "@tipo", Convert.ToInt32(tipo) },
                        { "@clientes", clientes },
                        { "@fechaInicio", fechaInicio.Value },
                        { "@fechaFin", fechaFin.Value }
                    };

                string query;

                if (esFactura)
                {
                    // Query para facturas (con JOIN)
                    query = @"
                            SELECT 
                                kd.c10 as usuario,
                                km.c3 as folio,
                                km.c6 as fecha_captura,
                                km.c12 as tipo_documento,
                                km.c16 as forma_pago,
                                km.c19 as subtotal,
                                km.c20 as descuento,
                                km.c21 as moneda,
                                km.c22 as tipo_cambio,
                                km.c24 as total,
                                km.c18 as plazo,
                                km.c25 as tipo_comprobante,
                                km.c26 as metodo_pago,
                                km.c27 as cp_receptor,
                                km.c30 as rfc_emisor,
                                km.c31 as razon_emisor,
                                km.c32 as regimen_receptor,
                                km.c34 as rfc_receptor,
                                km.c35 as razon_receptor,
                                km.c38 as uso_cfdi,
                                km.c40 as iva_porcentual,
                                km.c41 as iva_retenido,
                                km.c42 as isr_retenido,
                                km.c43 as total_impuestos_retenidos,
                                km.c44 as total_impuestos_traslado,
                                km.c39 as regimen_emisor,
                                kd.c2 || kd.c3 || kd.c4 || kd.c5 || '-' || km.c13 as folio_kepler,
                                kd.c43 as estatus,
                                km.c54 as uuid,
                                km.c13 as id_kepler,
                                'factura' as tipo_registro
                            FROM kdfe33m1 km
                            INNER JOIN kdm1 kd ON kd.c6 = km.c13
                            WHERE kd.c10 = ANY(@clientes)
                              AND kd.c2 = @genero
                              AND kd.c3 = @naturaleza
                              AND kd.c4 = @grupo
                              AND kd.c5 = @tipo
                              AND km.c5 = '1'
                              AND km.c63 = 0
                              --AND km.c11 = 7
                              AND km.c12 = 1
                              AND km.c6 BETWEEN @fechaInicio AND @fechaFin
                              AND kd.c43 IN ('N', 'R')
                            ORDER BY kd.c10, km.c6, km.c13";
                }
                else
                {
                    // Query para otros documentos (sin JOIN)
                    query = @"
                        SELECT 
                            kd.c10 as usuario,
                            kd.c6 as folio,
                            kd.c68 as fecha_captura,
                            kd.c38 as tipo_documento,
                            kd.c90 as forma_pago,
                            (kd.c16 - kd.c14) as subtotal,
                            kd.c13 as descuento,
                            kd.c7 as moneda,
                            kd.c40 as tipo_cambio,
                            kd.c16 as total,
                            kd.c30 as plazo,
                            '' as tipo_comprobante,
                            '' as metodo_pago,
                            kd.c35 as cp_receptor,
                            '' as rfc_emisor,
                            '' as razon_emisor,
                            '' as regimen_receptor,
                            kd.c22 as rfc_receptor,
                            kd.c32 as razon_receptor,
                            kd.c84 as uso_cfdi,
                            0 as iva_porcentual,
                            0 as iva_retenido,
                            0 as isr_retenido,
                            0 as total_impuestos_retenidos,
                            kd.c14 as total_impuestos_traslado,
                            '' as regimen_emisor,
                            kd.c9 || kd.c10 || kd.c11 || kd.c12 || '-' || kd.c13 as folio_kepler,
                            kd.c43 as estatus,
                            NULL as uuid,
                            kd.c6::text as id_kepler,  -- ← CAMBIO AQUÍ: usar folio como ID
                            'otro' as tipo_registro
                        FROM kdm1 kd
                        WHERE kd.c10 = ANY(@clientes)
                          AND kd.c2 = @genero
                          AND kd.c3 = @naturaleza
                          AND kd.c4 = @grupo
                          AND kd.c5 = @tipo
                          AND kd.c9 BETWEEN @fechaInicio AND @fechaFin
                          AND kd.c43 IN ('N', 'R')
                        ORDER BY kd.c6, kd.c13 ASC";
                }

                var rows = RunQuery(query, parameters, false, null, null, connection);

                if (rows.Count == 0)
                {
                    return Json(new
                    {
                        icon = "info",
                        title = "Sin resultados",
                        text = "No se encontraron registros para los filtros aplicados"
                    });
                }

                var registrosKepler = rows.Select(r => new RegistroKeplerModel
                {
                    Usuario = r["usuario"]?.ToString(),
                    Folio = r["folio"]?.ToString(),
                    FechaCaptura = r["fecha_captura"] == DBNull.Value
                        ? DateTime.MinValue
                        : Convert.ToDateTime(r["fecha_captura"]),
                    TipoDocumento = r["tipo_documento"]?.ToString(),
                    FormaPago = r["forma_pago"]?.ToString(),
                    Subtotal = r["subtotal"] == DBNull.Value ? 0 : Convert.ToDecimal(r["subtotal"]),
                    Descuento = r["descuento"] == DBNull.Value ? 0 : Convert.ToDecimal(r["descuento"]),
                    Moneda = r["moneda"]?.ToString(),
                    TipoCambio = r["tipo_cambio"] == DBNull.Value ? 0 : Convert.ToDecimal(r["tipo_cambio"]),
                    Total = r["total"] == DBNull.Value ? 0 : Convert.ToDecimal(r["total"]),
                    Plazo = r["plazo"]?.ToString(),
                    TipoComprobante = r["tipo_comprobante"]?.ToString(),
                    MetodoPago = r["metodo_pago"]?.ToString(),
                    CPReceptor = r["cp_receptor"]?.ToString(),
                    RFCEmisor = r["rfc_emisor"]?.ToString(),
                    RazonEmisor = r["razon_emisor"]?.ToString(),
                    RegimenReceptor = r["regimen_receptor"]?.ToString(),
                    RFCReceptor = r["rfc_receptor"]?.ToString(),
                    RazonReceptor = r["razon_receptor"]?.ToString(),
                    UsoCFDI = r["uso_cfdi"]?.ToString(),
                    IVAPorcentual = r["iva_porcentual"] == DBNull.Value ? 0 : Convert.ToDecimal(r["iva_porcentual"]),
                    IVARetenido = r["iva_retenido"] == DBNull.Value ? 0 : Convert.ToDecimal(r["iva_retenido"]),
                    ISRRetenido = r["isr_retenido"] == DBNull.Value ? 0 : Convert.ToDecimal(r["isr_retenido"]),
                    TotalImpuestosRetenidos = r["total_impuestos_retenidos"] == DBNull.Value ? 0 : Convert.ToDecimal(r["total_impuestos_retenidos"]),
                    TotalImpuestosTraslado = r["total_impuestos_traslado"] == DBNull.Value ? 0 : Convert.ToDecimal(r["total_impuestos_traslado"]),
                    FolioKepler = r["folio_kepler"]?.ToString(),
                    Estatus = r["estatus"]?.ToString(),
                    Uuid = r["uuid"]?.ToString(),
                    IdKepler = r["id_kepler"]?.ToString(),
                    TipoRegistro = r["tipo_registro"]?.ToString()
                }).ToList();

                var registrosConEstado = VerificarRegistrosExistentesProveedores(registrosKepler, esFactura);

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
                HttpContext.Session.SetString("EsFacturaKepler", esFactura.ToString());

                return Json(new
                {
                    icon = "success",
                    title = "Consulta exitosa",
                    text = $"Se encontraron {registrosKepler.Count} registro(s) de {resumenPorCliente.Count} cliente(s)",
                    registros = registrosConEstado,
                    resumen = resumenPorCliente
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    icon = "error",
                    title = "Error en la consulta",
                    text = ex.Message
                });
            }
        }


        private List<RegistroKeplerModel> VerificarRegistrosExistentesProveedores(List<RegistroKeplerModel> registros, bool esFactura)
        {
            try
            {
                if (esFactura)
                {
                    // Para facturas: verificar por UUID
                    var uuids = registros
                        .Where(r => !string.IsNullOrWhiteSpace(r.Uuid))
                        .Select(r => r.Uuid)
                        .Distinct()
                        .ToList();

                    if (uuids.Count == 0)
                        return registros;

                    var parametersUuids = new Dictionary<string, object>();
                    string uuidsIn = string.Join(",", uuids.Select((u, i) =>
                    {
                        parametersUuids.Add($"@uuid{i}", Guid.Parse(u));
                        return $"@uuid{i}";
                    }));

                    string query = $@"
                            SELECT DISTINCT f.""uuid""::text as uuid_texto
                            FROM factura f
                            WHERE f.""uuid"" IN ({uuidsIn})";

                    var uuidsExistentes = RunQuery(query, parametersUuids)
                        .Select(row => row["uuid_texto"]?.ToString()?.ToUpper())
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .ToHashSet();

                    foreach (var registro in registros)
                    {
                        if (!string.IsNullOrWhiteSpace(registro.Uuid))
                        {
                            registro.YaExiste = uuidsExistentes.Contains(registro.Uuid.ToUpper());
                        }
                    }
                }
                else
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

                    string query = $@"
                            SELECT DISTINCT cc.folio_kepler
                            FROM cartera_clientes cc
                            WHERE cc.folio_kepler IN ({foliosIn})
                              AND cc.cancelada = false";

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
        public JsonResult GuardarRegistrosSeleccionadosProveedores(string[] registrosIds)
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

                // Filtrar solo los registros seleccionados que NO existen
                var registrosSeleccionados = todosLosRegistros
                    .Where(r => registrosIds.Contains(r.IdKepler) && !r.YaExiste)
                    .ToList();

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
                string connStr = utils._configuration.GetConnectionString("DefaultConnection");

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

                                var dtCliente = RunQuery(query, parametersCliente);

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
                                    var formaPagoResult = RunScalar(query, parametersFormaPago);
                                    if (formaPagoResult != null)
                                    {
                                        formaPago = Convert.ToInt32(formaPagoResult);
                                    }
                                }

                                // Calcular totales para este cliente
                                decimal totalGeneral = registrosCliente.Sum(r => r.Total);

                                // Crear encabezado principal
                                var encabezado = new DocumentoEncabezado
                                {
                                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                                    IdArea = 7,
                                    IdTpDoc = 69,
                                    Anio = DateTime.Now.Year,
                                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                                    Fch = DateTime.Now,
                                    TpMov = "RICD",
                                    UsrDoc = User.Identity.Name,
                                    FchCap = DateTime.Now,
                                    Usr0 = GetUserId(User.Identity.Name),
                                    Fch0 = DateTime.Now,
                                    CliProv = codigoCliente,
                                    Ref = clienteID,
                                    Estatus = 1,
                                    UsrDep = GetAreaName(User.Identity.Name),
                                    TipoPoceso = "registro_carteras_proveedor",
                                    Mdp = primerRegistro.MetodoPago,
                                    CFDI = primerRegistro.UsoCFDI,
                                    FPago = formaPago,
                                    Imp = totalGeneral,
                                    CentroCostos = 5
                                };

                                var partidas = new List<PartidaDocumento>();
                                var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                                // Generar póliza
                                List<PolizaData> poliza = GenerarDatosPoliza(
                                    Convert.ToInt32(documento["IdEncabezado"]),
                                    null,
                                    null,
                                    conn,
                                    tx
                                );

                                var polizaId = RegistrarPolizas(
                                    GetUserId(User.Identity.Name),
                                    Convert.ToInt32(documento["IdEncabezado"]),
                                    poliza,
                                    false,
                                    null,
                                    conn,
                                    tx
                                );

                                // Procesar cada registro
                                foreach (var registro in registrosCliente)
                                {
                                    var encabezadoFactura = new DocumentoEncabezado
                                    {
                                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                                        IdArea = 7,
                                        IdTpDoc = 69,
                                        Anio = DateTime.Now.Year,
                                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                                        Fch = DateTime.Now,
                                        TpMov = "RICD",
                                        UsrDoc = User.Identity.Name,
                                        FchCap = DateTime.Now,
                                        Usr0 = GetUserId(User.Identity.Name),
                                        Fch0 = DateTime.Now,
                                        CliProv = codigoCliente,
                                        Ref = clienteID,
                                        Estatus = 1,
                                        UsrDep = GetAreaName(User.Identity.Name),
                                        TipoPoceso = "registro_carteras_proveedor",
                                        Mdp = registro.MetodoPago,
                                        CFDI = primerRegistro.UsoCFDI,
                                        FPago = formaPago,
                                        Imp = registro.Total,
                                        CentroCostos = 5
                                    };

                                    var documentoFactura = GenerarDocumentoConPartidas(encabezadoFactura, partidas, conn, tx);

                                    // Solo insertar en factura si esFactura = true
                                    if (esFactura && !string.IsNullOrWhiteSpace(registro.Uuid))
                                    {
                                        var parametersFactura = new Dictionary<string, object>
                                            {
                                                { "serie", "RICD" },
                                                { "folio", documento["folio_generado"].ToString() },
                                                { "idcliente", clienteID },
                                                { "idtipopago", formaPago },
                                                { "moneda", registro.Moneda ?? "MXN" },
                                                { "cpe", "78394" },
                                                { "rfcemisor", registro.RFCEmisor ?? "" },
                                                { "rsoemisor", registro.RazonEmisor ?? "" },
                                                { "reg_fise", "601" },
                                                { "rfccliente", registro.RFCReceptor ?? "" },
                                                { "rsocliente", registro.RazonReceptor ?? "" },
                                                { "cpr", registro.CPReceptor ?? "" },
                                                { "idusocfdi", registro.UsoCFDI ?? "" },
                                                { "reg_fisr", registro.RegimenReceptor ?? "" },
                                                { "subtotal", registro.Subtotal },
                                                { "iva", registro.TotalImpuestosTraslado },
                                                { "total", registro.Total },
                                                { "mdpfactura", registro.MetodoPago ?? "" },
                                                { "fecha", registro.FechaCaptura },
                                                { "uuid", Guid.Parse(registro.Uuid) },
                                                { "descuento", registro.Descuento },
                                                { "fechatimbrado", registro.FechaCaptura },
                                                { "encabezado_id", Convert.ToInt32(documentoFactura["IdEncabezado"]) },
                                                { "parcialidad", registro.Plazo ?? "0" }
                                            };

                                        RunQuery(
                                            @"INSERT INTO factura 
                                                (serie, folio, idcliente, rfccliente, rsocliente, 
                                                rfcemisor, rsoemisor, fecha, fechatimbrado, mdpfactura, idtipopago, ""uuid"", descuento, subtotal, iva, parcialidad, total, 
                                                moneda, idusocfdi, reg_fisr, reg_fise, cpr, cpe, encabezado_id) 
                                                VALUES(@serie, @folio, @idcliente, @rfccliente, @rsocliente, @rfcemisor, @rsoemisor, 
                                                @fecha, @fechatimbrado, @mdpfactura, @idtipopago, @uuid, @descuento, @subtotal, @iva, @parcialidad, 
                                                @total, @moneda, @idusocfdi, @reg_fisr, @reg_fise, @cpr, @cpe, @encabezado_id);",
                                            parametersFactura,
                                            false,
                                            conn,
                                            tx
                                        );
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
                                            { "empresa_id", HttpContext.Session.GetInt32("Empresa") }
                                        };

                                    RunQuery(
                                        @"INSERT INTO cartera_clientes 
                                            (cliente_id, encabezado_id, fecha_emision, fecha_vencimiento, monto_total, saldo_pendiente, 
                                            estado, poliza_id, creado_por, fecha_creacion, ""uuid"", cancelada, folio_kepler, empresa_id) 
                                            VALUES(@cliente_id, @encabezado_id, @fecha_emision, @fecha_vencimiento, @monto_total, @saldo_pendiente, 
                                            'pendiente', @poliza_id, @creado_por, @fecha_creacion, gen_random_uuid(), false, @folio_kepler, @empresa_id);",
                                        parametersCartera,
                                        false,
                                        conn,
                                        tx
                                    );

                                    totalProcesados++;
                                }
                            }

                            // Limpiar sesión
                            HttpContext.Session.SetString("RegistrosKeplerConsultados", null);
                            HttpContext.Session.SetString("EsFacturaKepler", null);

                            tx.Commit();

                            return Json(new
                            {
                                icon = "success",
                                title = "Éxito",
                                text = $"Se procesaron correctamente {totalProcesados} registro(s) de {registrosPorCliente.Count} cliente(s)"
                            });
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
                return Json(new
                {
                    icon = "error",
                    title = "Error inesperado",
                    text = ex.Message
                });
            }
        }
        // Agregar este método al CarterasController.cs

        public JsonResult ObtenerDescripcionDocumentoProveedores(string genero, string naturaleza, string grupo, string tipo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(genero) ||
                    string.IsNullOrWhiteSpace(naturaleza) ||
                    string.IsNullOrWhiteSpace(grupo) ||
                    string.IsNullOrWhiteSpace(tipo))
                {
                    return Json(new
                    {
                        success = false,
                        descripcion = ""
                    });
                }

                string connection = HttpContext.Session.GetString("EmpresaFactura");

                var parameters = new Dictionary<string, object>
                    {
                        { "@genero", genero },
                        { "@naturaleza", naturaleza },
                        { "@grupo",  Convert.ToInt32(grupo) },
                        { "@tipo",  Convert.ToInt32(tipo) }
                    };

                string query = @"
                SELECT c5 as descripcion 
                FROM sellosop.KDMM 
                WHERE c1 = @genero 
                  AND c2 = @naturaleza 
                  AND c3 = @grupo 
                  AND c4 = @tipo
                LIMIT 1";

                var result = RunQuery(query, parameters, false, null, null, "SRS");

                if (result.Count > 0)
                {
                    string descripcion = result[0]["descripcion"]?.ToString() ?? "";
                    return Json(new
                    {
                        success = true,
                        descripcion = descripcion
                    });
                }

                return Json(new
                {
                    success = false,
                    descripcion = "No se encontró configuración para esta combinación"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    descripcion = "",
                    error = ex.Message
                });
            }
        }
    }

}