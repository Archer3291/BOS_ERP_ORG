using DocumentFormat.OpenXml.Drawing.Charts;
using Npgsql;
using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;

public class PolizaConfigFactory : Utilities
{
    public List<PolizaData> Generar(DocumentoEncabezado encabezado, List<Impuesto> impuestos, decimal? costoVenta, CuentasBancoPoliza cuentasBanco, List<DocumentoEncabezado> facturas_pagadas, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        LogErrorHelper.RegistrarLog(
            modulo: "POLIZAS",
            uuid: encabezado.Folio?.ToString(),
            mensaje: $"Inicia generación póliza DocId={encabezado.IdDoc}, Nat={encabezado.Nat}",
            nivel: "INFO"
        );

        List<TipoPolizaModel> tiposPoliza = ObtenerTiposPoliza(encabezado, conn, tx);

        if (tiposPoliza.Count == 0)
        {
            LogErrorHelper.RegistrarLog(
                "POLIZAS",
                encabezado.Folio?.ToString(),
                $"No hay pólizas configuradas para este documento: {encabezado.Folio?.ToString()}",
                nivel: "ERROR"
            );
            throw new Exception($"No hay pólizas configuradas para este documento: {encabezado.Folio?.ToString()}");
        }

        var resultado = new List<PolizaData>();

        foreach (var tipo in tiposPoliza)
        {
            var poliza = new PolizaData
            {
                Tipo = encabezado.TipoPoceso == "factura_anticipo" ? 1 : tipo.Clasificacion,
                Estado = "cerrada",
                Descripcion = $"Descripcion: {tipo.Descripcion}\nTipo: {encabezado.TipoPoceso.Replace("_", " ").ToUpper()}"
                    + (string.IsNullOrWhiteSpace(encabezado.CliProv) ? "" : $"\nTercero: {encabezado.CliProv}"),
                IdEncabezado = encabezado.IdDoc,
                Categoria = tipo.IdCategoria,
            };

            poliza.Detalles = GenerarDetallesDesdePlantilla(tipo.Id, encabezado, impuestos, costoVenta, cuentasBanco, facturas_pagadas, conn, tx);

            resultado.Add(poliza);
        }

        return resultado;
    }

    private List<TipoPolizaModel> ObtenerTiposPoliza(DocumentoEncabezado doc, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var parameters = new Dictionary<string, object>
        {
            { "nat", doc.Nat },
            { "empresa", doc.EmpresaId },
            { "tipo_proceso", doc.TipoPoceso }
        };

        if (string.IsNullOrWhiteSpace(doc.TipoPoceso))
        {
            LogErrorHelper.RegistrarLog(
                "POLIZAS",
                doc.Folio,
                $"Tipo de proceso no definido para este documento en poliza o encabezado: {doc.Folio?.ToString()}",
                nivel: "ERROR"
            );
            throw new Exception($"No hay un tipo de proceso configurado para este documento: {doc.Folio?.ToString()}");
        }

        string query = "SELECT idtpdoc FROM tpdoc WHERE abreviaturatpdoc = @nat ";
        int tpDoc = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

        parameters.Add("idtpdoc", tpDoc);

        query = "SELECT pt.id_tipo_poliza, pt.nombre, pt.descripcion, cp.id_clasificacion_poliza clasificacion, pt.activo, pt.clasificacion_id, pt.id_categoria " +
            "FROM poliza_documento pd " +
            "JOIN poliza_tipo pt ON pt.id_tipo_poliza = pd.id_tipo_poliza " +
            "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = pt.clasificacion_id " +
            "WHERE pd.idtpdoc = @idtpdoc AND pd.activo = true AND pt.empresa_id = @empresa AND tipo_proceso = @tipo_proceso " +
            "ORDER BY pd.orden";

        var rows = RunQuery(query, parameters, false, conn, tx);

        var lista = new List<TipoPolizaModel>();

        foreach (var row in rows)
        {
            lista.Add(new TipoPolizaModel
            {
                Id = Convert.ToInt32(row["id_tipo_poliza"]),
                Nombre = row["nombre"].ToString(),
                Descripcion = row["descripcion"]?.ToString(),
                EmpresaId = doc.EmpresaId,
                Clasificacion = GetInt(row["clasificacion"]),
                Activo = (bool)row["activo"],
                IdCategoria = Convert.ToInt32(row["id_categoria"]),
            });
        }

        return lista;
    }

    private List<PolizaDetalle> GenerarDetallesDesdePlantilla(int idTipoPoliza, DocumentoEncabezado doc, List<Impuesto> impuestos, decimal? costoVenta, CuentasBancoPoliza cuentasBanco, List<DocumentoEncabezado> facturas_pagadas, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var parameters = new Dictionary<string, object>
        {
            { "idTipo", idTipoPoliza }
        };

        string sql = "SELECT orden, tipo_cuenta, id_cuenta_contable, lado, origen_monto, factor, descripcion_template, impuesto_id " +
            "FROM poliza_partida " +
            "WHERE id_tipo_poliza = @idTipo " +
            "ORDER BY orden";

        var rows = RunQuery(sql, parameters, false, conn, tx);
        var detalles = new List<PolizaDetalle>();

        // Las cuentas de banco se abren todas de golpe en la primera partida BANCO que
        // aparece. Si la plantilla trae más de una partida de ese tipo, las siguientes se
        // ignoran: si no, cada una volvería a recorrer el arreglo y la póliza terminaría con
        // el dinero contado dos veces.
        bool bancoResuelto = false;

        foreach (var row in rows)
        {
            try
            {
                LogErrorHelper.RegistrarLog(
                    "POLIZAS",
                    doc.Folio?.ToString(),
                    $"Procesando partida orden={row["orden"]}, tipo={row["tipo_cuenta"]}",
                    nivel: "DEBUG"
                );

                string tipoCuenta = row["tipo_cuenta"].ToString();
                string lado = row["lado"].ToString();
                string origen = row["origen_monto"].ToString();
                decimal factor = Convert.ToDecimal(row["factor"] ?? 1);

                if (lado != "DEBE" && lado != "HABER")
                {
                    LogErrorHelper.RegistrarLog(
                        "POLIZAS",
                        doc.Folio?.ToString(),
                        $"Lado inválido detectado: {lado}",
                        nivel: "ERROR"
                    );

                    throw new Exception($"Lado inválido en póliza: {lado}");
                }

                // --- CASO IMPUESTOS ---
                if (tipoCuenta == "IMPUESTO")
                {
                    int? impuestoConfig = row["impuesto_id"] as int?;

                    foreach (var imp in impuestos)
                    {
                        // Filtro por impuesto específico
                        if (impuestoConfig.HasValue && impuestoConfig.Value != imp.IdImpuesto)
                            continue;

                        var monto = imp.Importe * factor;
                        if (monto == 0)
                            continue;

                        detalles.Add(new PolizaDetalle
                        {
                            Cuenta = Convert.ToInt32(row["id_cuenta_contable"]),
                            Debe = lado == "DEBE" ? Math.Abs(monto) : 0,
                            Haber = lado == "HABER" ? Math.Abs(monto) : 0,
                            Centro = doc.CentroCostos,
                            Descripcion = row["descripcion_template"].ToString()?.Replace("{IMPUESTO}", imp.Clave)?.Replace("{FOLIO}", doc.Folio.ToString())
                        });
                    }

                    continue;
                }

                if (tipoCuenta == "DESCUENTO")
                {
                    if (doc.Dto == 0m)
                        continue;

                    detalles.Add(new PolizaDetalle
                    {
                        Cuenta = Convert.ToInt32(row["id_cuenta_contable"]),
                        Debe = lado == "DEBE" ? doc.Dto : 0m,
                        Haber = lado == "HABER" ? doc.Dto : 0m,
                        Centro = doc.CentroCostos,
                        Descripcion = row["descripcion_template"].ToString()
                    });

                    continue;
                }

                // --- CASO BANCO: un asiento por cada cuenta que trae el documento ---
                if (tipoCuenta == "BANCO")
                {
                    if (bancoResuelto)
                    {
                        LogErrorHelper.RegistrarLog(
                            "POLIZAS",
                            doc.Folio?.ToString(),
                            $"Partida orden={row["orden"]} de tipo BANCO omitida: las cuentas de banco ya se generaron en una partida anterior de esta póliza.",
                            nivel: "WARN"
                        );

                        continue;
                    }

                    detalles.AddRange(GenerarAsientosBanco(row, doc, lado, origen, factor, costoVenta, cuentasBanco, conn, tx));
                    bancoResuelto = true;

                    continue;
                }

                if (tipoCuenta == "DESGLOSE_FACTURA_PAGO")
                {
                    if (facturas_pagadas.Count > 0)
                    {
                        foreach (DocumentoEncabezado fac in facturas_pagadas)
                            detalles.Add(new PolizaDetalle
                            {
                                Cuenta = ResolverCuenta(tipoCuenta, row, doc, null, conn, tx),
                                Debe = lado == "DEBE" ? fac.Imp : 0m,
                                Haber = lado == "HABER" ? fac.Imp : 0m,
                                Centro = doc.CentroCostos,
                                Descripcion = row["descripcion_template"].ToString().Replace("{FOLIO}", fac.Folio.ToString())
                            });

                        continue;
                    }
                }


                // --- CUENTAS NORMALES ---
                int cuenta = ResolverCuenta(tipoCuenta, row, doc, null, conn, tx);
                decimal? montoBase = ResolverMonto(origen, doc, costoVenta);
                decimal? montoFinal = montoBase * factor;

                if (montoFinal == 0)
                    continue;

                detalles.Add(new PolizaDetalle
                {
                    Cuenta = cuenta,
                    Debe = lado == "DEBE" ? montoFinal : 0,
                    Haber = lado == "HABER" ? montoFinal : 0,
                    Centro = doc.CentroCostos,
                    Descripcion = tipoCuenta == "DESGLOSE_FACTURA_PAGO" ? "Abono a cuenta de cliente" : row["descripcion_template"]?.ToString()?.Replace("{FOLIO}", doc.Folio.ToString())
                });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(
                    "POLIZAS",
                    doc.Folio?.ToString(),
                    $"Error procesando partida orden={row["orden"]}, tipo={row["tipo_cuenta"]}: {ex.Message}",
                    nivel: "ERROR"
                );
                throw;
            }
        }

        return detalles;
    }

    /// <summary>
    /// Convierte una partida BANCO de la plantilla en un asiento por cada cuenta que trae el
    /// documento. Un cobro repartido entre dos cuentas —efectivo y terminal, por ejemplo— deja
    /// dos renglones de banco en la misma póliza.
    ///
    /// El importe sale de cada cuenta, no de la plantilla: la plantilla solo sabe del total del
    /// documento y usarlo N veces dejaría la póliza descuadrada. Se admite importe nulo cuando
    /// viene una sola cuenta, que es el caso de siempre y toma el monto de origen_monto.
    /// </summary>
    private List<PolizaDetalle> GenerarAsientosBanco(Dictionary<string, object> row, DocumentoEncabezado doc, string lado, string origen, decimal factor, decimal? costoVenta, CuentasBancoPoliza cuentasBanco, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var asientos = new List<PolizaDetalle>();

        // Documento sin cuentas: se conserva el camino viejo, la cuenta fija de la partida si
        // la tiene. Si tampoco la tiene, ObtenerCuentaBanco revienta con el mismo mensaje de
        // antes, que es donde el error se entiende.
        var cuentas = cuentasBanco != null && cuentasBanco.Count > 0
            ? (IList<CuentaBancoPoliza>)cuentasBanco
            : new List<CuentaBancoPoliza> { new CuentaBancoPoliza(null) };

        if (cuentas.Count > 1 && cuentas.Any(c => !c.Importe.HasValue))
        {
            LogErrorHelper.RegistrarLog(
                "POLIZAS",
                doc.Folio?.ToString(),
                $"Se recibieron {cuentas.Count} cuentas de banco y al menos una viene sin importe; no hay forma de repartir el monto de la plantilla sin descuadrar la póliza.",
                nivel: "ERROR"
            );

            throw new Exception(
                "Cuando el documento se cobra o se paga con más de una cuenta de banco, cada " +
                "cuenta debe traer su importe; de lo contrario la póliza no cuadra.");
        }

        decimal? montoPlantilla = ResolverMonto(origen, doc, costoVenta) * factor;
        string descripcion = row["descripcion_template"]?.ToString()?.Replace("{FOLIO}", doc.Folio.ToString());

        foreach (var cuentaBanco in cuentas)
        {
            decimal? monto = cuentaBanco.Importe.HasValue
                ? cuentaBanco.Importe * factor
                : montoPlantilla;

            if (monto == 0)
                continue;

            asientos.Add(new PolizaDetalle
            {
                Cuenta = ResolverCuenta("BANCO", row, doc, cuentaBanco.Cuenta, conn, tx),
                Debe = lado == "DEBE" ? monto : 0,
                Haber = lado == "HABER" ? monto : 0,
                Centro = doc.CentroCostos,
                Descripcion = descripcion
            });
        }

        LogErrorHelper.RegistrarLog(
            "POLIZAS",
            doc.Folio?.ToString(),
            $"Partida BANCO orden={row["orden"]} generó {asientos.Count} asiento(s) sobre {cuentas.Count} cuenta(s).",
            nivel: "DEBUG"
        );

        return asientos;
    }

    private int ResolverCuenta(string tipoCuenta, Dictionary<string, object> row, DocumentoEncabezado doc, string cuentaBanco, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        switch (tipoCuenta)
        {
            case "FIJA":
                return Convert.ToInt32(row["id_cuenta_contable"]);

            case "CLIENTE":
            case "DESGLOSE_FACTURA_PAGO":
                return ObtenerCuentaCliente(doc.Ref, conn, tx);

            case "PROVEEDOR":
                return ObtenerCuentaProveedor(doc.Ref, conn, tx);

            case "BANCO":
                return GetCuentaSiExiste(row)
                    ?? ObtenerCuentaBanco(cuentaBanco, conn, tx);

            default:
                LogErrorHelper.RegistrarLog(
                    "POLIZAS",
                    doc.Folio?.ToString(),
                    $"Tipo de cuenta no soportado: {tipoCuenta}",
                    nivel: "ERROR"
                );

                throw new Exception("Tipo de cuenta no soportado: " + tipoCuenta);
        }
    }

    private decimal? ResolverMonto(string origen, DocumentoEncabezado doc, decimal? costoVenta)
    {
        switch (origen)
        {
            case "SUBTOTAL":
                return GetDecimal(doc.Sub, 0);

            case "TOTAL":
                return GetDecimal(doc.Imp, 0);

            case "DESCUENTO":
                return GetDecimal(doc.Dto, 0);

            case "COSTO":
                return GetDecimal(costoVenta, 0);

            default:
                return 0;
        }
    }

    private int ObtenerCuentaImpuesto(Impuesto imp, int id_cuenta, int empresaId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var parameters = new Dictionary<string, object>
        {
            { "imp", imp.Clave },
            { "empresa", empresaId },
            { "id_cuenta", id_cuenta },
        };

        string query = "SELECT codigo FROM cuentas_finanzas WHERE id_cuenta_contable = @id_cuenta";
        string cuenta = RunScalar(query, parameters).ToString();
        parameters.Add("codigo", cuenta);

        string sql = "SELECT cf.id_cuenta_contable FROM cuentas_finanzas AS cf " +
            "WHERE codigo LIKE @codigo || '%' AND LOWER(nombre) LIKE LOWER('%' || @imp || '%') AND empresa_id  = @empresa";

        var rows = RunQuery(sql, parameters, false, conn, tx);

        if (rows.Count == 0)
            throw new Exception("No hay cuenta contable configurada para el impuesto");

        return Convert.ToInt32(rows[0]["id_cuenta_contable"]);
    }

    private int ObtenerCuentaCliente(int? cliente_id, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var parameters = new Dictionary<string, object>
        {
            { "ref", cliente_id }
        };

        string sql = "SELECT id_cuenta_contable FROM cuentas_finanzas WHERE cliente_id = @ref";
        var rows = RunQuery(sql, parameters, false, conn, tx);

        if (rows.Count == 0)
        {
            LogErrorHelper.RegistrarLog(
                "POLIZAS",
                "cliente",
                $"Error al obtener el cliente",
                nivel: "ERROR"
            );

            throw new Exception("No se encontró la cuenta contable del cliente");
        }

        return Convert.ToInt32(rows[0]["id_cuenta_contable"]);
    }

    private int ObtenerCuentaProveedor(int? proveedor_id, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var parameters = new Dictionary<string, object>
        {
            { "ref", proveedor_id }
        };

        string sql = "SELECT id_cuenta_contable FROM cuentas_finanzas WHERE proveedor_id = @ref";
        var rows = RunQuery(sql, parameters, false, conn, tx);

        if (rows.Count == 0)
        {
            LogErrorHelper.RegistrarLog(
                "POLIZAS",
                "proveedor",
                $"Error al obtener el proveedor",
                nivel: "ERROR"
            );
            throw new Exception("No se encontró la cuenta contable del proveedor");
        }

        return Convert.ToInt32(rows[0]["id_cuenta_contable"]);
    }

    private int ObtenerCuentaBanco(string cuentaBanco, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var parameters = new Dictionary<string, object>
        {
            { "cuenta", cuentaBanco }
        };

        string sql = "SELECT id_cuenta_contable FROM cuentas_finanzas WHERE codigo = @cuenta";
        var rows = RunQuery(sql, parameters, false, conn, tx);

        if (rows.Count == 0)
        {
            LogErrorHelper.RegistrarLog(
                "POLIZAS",
                "banco",
                $"Error al obtener la cuenta de banco",
                nivel: "ERROR"
            );

            throw new Exception("No se encontró la cuenta contable del banco");
        }

        return Convert.ToInt32(rows[0]["id_cuenta_contable"]);
    }

    private int? GetCuentaSiExiste(Dictionary<string, object> row)
    {
        if (row.ContainsKey("id_cuenta_contable") &&
            row["id_cuenta_contable"] != DBNull.Value &&
            row["id_cuenta_contable"] != null)
        {
            return Convert.ToInt32(row["id_cuenta_contable"]);
        }

        return null;
    }

    //private GetPartidasComoFactura(DocumentoEncabezado doc, Dictionary<string, object> row)
    //{

    //}

}



//⢀⡴⠑⡄⠀⠀⠀⠀⠀⠀⠀⣀⣀⣤⣤⣤⣀⡀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀ 
//⠸⡇⠀⠿⡀⠀⠀⠀⣀⡴⢿⣿⣿⣿⣿⣿⣿⣿⣷⣦⡀⠀⠀⠀⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⠑⢄⣠⠾⠁⣀⣄⡈⠙⣿⣿⣿⣿⣿⣿⣿⣿⣆⠀⠀⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⢀⡀⠁⠀⠀⠈⠙⠛⠂⠈⣿⣿⣿⣿⣿⠿⡿⢿⣆⠀⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⢀⡾⣁⣀⠀⠴⠂⠙⣗⡀⠀⢻⣿⣿⠭⢤⣴⣦⣤⣹⠀⠀⠀⢀⢴⣶⣆ 
//⠀⠀⢀⣾⣿⣿⣿⣷⣮⣽⣾⣿⣥⣴⣿⣿⡿⢂⠔⢚⡿⢿⣿⣦⣴⣾⠁⠸⣼⡿ 
//⠀⢀⡞⠁⠙⠻⠿⠟⠉⠀⠛⢹⣿⣿⣿⣿⣿⣌⢤⣼⣿⣾⣿⡟⠉⠀⠀⠀⠀⠀ 
//⠀⣾⣷⣶⠇⠀⠀⣤⣄⣀⡀⠈⠻⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⡇⠀⠀⠀⠀⠀⠀ 
//⠀⠉⠈⠉⠀⠀⢦⡈⢻⣿⣿⣿⣶⣶⣶⣶⣤⣽⡹⣿⣿⣿⣿⡇⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⠀⠀⠀⠉⠲⣽⡻⢿⣿⣿⣿⣿⣿⣿⣷⣜⣿⣿⣿⡇⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⠀⠀⠀⠀⢸⣿⣿⣷⣶⣮⣭⣽⣿⣿⣿⣿⣿⣿⣿⠀⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⠀⠀⣀⣀⣈⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⠇⠀⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⠀⠀⢿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⠃⠀⠀⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⠀⠀⠀⠹⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⡿⠟⠁⠀⠀⠀⠀⠀⠀⠀⠀⠀ 
//⠀⠀⠀⠀⠀⠀⠀⠀⠀⠉⠛⠻⠿⠿⠿⠿⠛⠉