using Newtonsoft.Json;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOS_ERP.Services
{
    // ══════════════════════════════════════════════════════════════════════
    //  NCApplicationService
    //  Encapsula toda la lógica de aplicación de una Nota de Crédito:
    //
    //  Caso 1 — PPD con saldo suficiente  : reduce saldo directamente
    //  Caso 2 — PPD overflow / PUE pagada : genera saldo a favor,
    //           el usuario decidió (saldo_favor | reembolso | split)
    //  Caso 3 — Standalone                : crea saldo a favor directo
    //  Caso 4 — Anticipos                 : ya manejado en ProcesarNCAsync,
    //           este servicio sólo orquesta los otros tres casos
    //
    //  CONTRATO:
    //    Todos los métodos públicos reciben una NpgsqlConnection + Transaction
    //    abiertas. NO abren ni cierran la transacción — eso es responsabilidad
    //    del controlador que los invoca (VINotaCreditoController).
    // ══════════════════════════════════════════════════════════════════════

    public class NCApplicationService
    {
        // ── DTOs ─────────────────────────────────────────────────────────

        public class FacturaOrigenInfo
        {
            public int Id { get; set; }
            public string MetodoPago { get; set; }  // "PUE" | "PPD"
            public decimal Total { get; set; }
            public decimal Saldo { get; set; }  // saldo actual en BD
            public decimal PagosAcum { get; set; }  // suma de pagos recibidos
            public bool EstaLiquidada => Saldo <= 0;
        }

        public class NCApplicationInput
        {
            public int NcFacturaId { get; set; }
            public int ClienteId { get; set; }
            public decimal NcTotal { get; set; }
            public string Moneda { get; set; }
            public string TipoNC { get; set; }  // devolucion|descuento|…|standalone
            public int UsuarioId { get; set; }

            // Resultado de la decisión del usuario (viene del front)
            public string DecisionSaldo { get; set; }  // "reducir_deuda" | "saldo_favor" | "reembolso"

            // Para el caso split (PPD overflow):
            public decimal MontoADeuda { get; set; }  // cuánto va a reducir factura
            public decimal MontoACartera { get; set; }  // cuánto va a saldo a favor

            // Facturas origen referenciadas (puede ser más de una)
            public List<int> OrigenFacturaIds { get; set; } = new List<int>();
        }

        public class NCApplicationResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string Modo { get; set; }  // "deuda_reducida" | "saldo_favor" | "reembolso" | "split"
            public decimal MontoDeuda { get; set; }
            public decimal MontoCartera { get; set; }
            public int? SaldoFavorId { get; set; }
        }

        public class AplicarSaldoFavorInput
        {
            public int SaldoFavorId { get; set; }
            public int FacturaDestinoId { get; set; }
            public decimal MontoAAplicar { get; set; }
            public int UsuarioId { get; set; }
        }

        // ── Helper — ejecutar query sin ORM ──────────────────────────────

        private static List<Dictionary<string, object>> RunQ(
            string sql,
            Dictionary<string, object> parms,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var rows = new List<Dictionary<string, object>>();
            using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                if (parms != null)
                    foreach (var kv in parms)
                        cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < rdr.FieldCount; i++)
                            row[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
                        rows.Add(row);
                    }
                }
            }
            return rows;
        }

        private static object RunScalar(
            string sql,
            Dictionary<string, object> parms,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                if (parms != null)
                    foreach (var kv in parms)
                        cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
                var result = cmd.ExecuteScalar();
                return result == DBNull.Value ? null : result;
            }
        }

        // ── Leer estado de factura origen ─────────────────────────────────

        public static FacturaOrigenInfo LeerFacturaOrigen(
            int facturaId,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var rows = RunQ(@"
                SELECT
                    f.id,
                    f.saldo,
                    f.total,
                    COALESCE((SELECT cve_mdp FROM mdp WHERE descripcion = f.mdpfactura),'PUE') AS metodo_pago,
                    COALESCE(
                        (SELECT SUM(p.monto_aplicado)
                         FROM aplicaciones_cobro_cliente p
                         WHERE p.encabezado_id = f.encabezado_id), 0
                    )                                  AS pagos_acum
                FROM factura f
                WHERE f.id = @id
                  AND f.statusfactura != 'CANCELADA'",
                new Dictionary<string, object> { ["id"] = facturaId },
                conn, tx);

            if (rows.Count == 0)
                throw new Exception($"Factura origen {facturaId} no encontrada o cancelada.");

            var r = rows[0];
            return new FacturaOrigenInfo
            {
                Id = facturaId,
                MetodoPago = r["metodo_pago"]?.ToString()?.Trim() ?? "PUE",
                Total = Convert.ToDecimal(r["total"]),
                Saldo = Convert.ToDecimal(r["saldo"]),
                PagosAcum = Convert.ToDecimal(r["pagos_acum"])
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  MÉTODO PRINCIPAL
        //  Orquesta los 4 escenarios según la información de la factura
        //  origen y la decisión del usuario.
        // ══════════════════════════════════════════════════════════════════

        public static NCApplicationResult AplicarNC(
            NCApplicationInput input,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            // ── Caso standalone: sin factura de referencia ────────────────
            if (input.TipoNC == "standalone" || !input.OrigenFacturaIds.Any())
                return _CrearSaldoFavor(input, "standalone", conn, tx);

            var resultados = new List<NCApplicationResult>();

            // ── Iterar sobre cada factura referenciada ────────────────────
            // Por simplicidad de negocio aplicamos NC.total completo a la
            // primera factura disponible; si el importe se agota, paramos.
            // Si necesitas distribución proporcional entre varias facturas,
            // ajusta la lógica de distribución abajo.
            decimal montoPendiente = input.NcTotal;

            foreach (int origenId in input.OrigenFacturaIds)
            {
                if (montoPendiente <= 0) break;

                var origen = LeerFacturaOrigen(origenId, conn, tx);

                // ── Determinar el modo correcto ───────────────────────────
                bool esPUEPagada = origen.MetodoPago == "PUE" || origen.EstaLiquidada;

                if (esPUEPagada)
                {
                    // Factura ya pagada — todo va a cartera/reembolso según decisión
                    var r = _ManejarPUEPagada(input, origen, montoPendiente, conn, tx);
                    resultados.Add(r);
                    montoPendiente = 0;  // se consume todo en saldo a favor
                }
                else
                {
                    // PPD con saldo pendiente — verificar overflow
                    decimal saldo = origen.Saldo;

                    if (montoPendiente <= saldo)
                    {
                        // CASO 1: NC cabe en el saldo pendiente
                        var r = _ReducirSaldoPPD(input, origen, montoPendiente, conn, tx);
                        resultados.Add(r);
                        montoPendiente = 0;
                    }
                    else
                    {
                        // CASO 2: PPD overflow — split manual
                        var r = _ManejarPPDOverflow(input, origen, conn, tx);
                        resultados.Add(r);
                        montoPendiente = 0;
                    }
                }
            }

            // Consolidar resultado (en la mayoría de casos es uno solo)
            if (resultados.Count == 1) return resultados[0];

            return new NCApplicationResult
            {
                Success = resultados.All(r => r.Success),
                Message = $"NC aplicada en {resultados.Count} facturas",
                Modo = "multi",
                MontoDeuda = resultados.Sum(r => r.MontoDeuda),
                MontoCartera = resultados.Sum(r => r.MontoCartera)
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  CASO 1 — PPD con saldo suficiente: reducir deuda directamente
        // ══════════════════════════════════════════════════════════════════

        private static NCApplicationResult _ReducirSaldoPPD(
            NCApplicationInput input,
            FacturaOrigenInfo origen,
            decimal montoNC,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            decimal saldoAntes = origen.Saldo;
            decimal saldoDespues = Math.Max(0, saldoAntes - montoNC);

            // Reducir saldo en la factura origen
            RunScalar(@"
                UPDATE factura
                   SET saldo = @nuevo_saldo
                 WHERE id    = @id",
                new Dictionary<string, object>
                {
                    ["nuevo_saldo"] = saldoDespues,
                    ["id"] = origen.Id
                },
                conn, tx);

            // Registrar snapshot del estado
            RunScalar(@"
                INSERT INTO nc_estado_factura_origen
                    (nc_factura_id, origen_factura_id, metodo_pago,
                     total_factura, saldo_antes_nc, pagos_acumulados,
                     nc_importe, saldo_despues_nc)
                VALUES
                    (@nc_id, @orig_id, @mdp,
                     @total, @saldo_antes, @pagos,
                     @nc_imp, @saldo_despues)",
                new Dictionary<string, object>
                {
                    ["nc_id"] = input.NcFacturaId,
                    ["orig_id"] = origen.Id,
                    ["mdp"] = origen.MetodoPago,
                    ["total"] = origen.Total,
                    ["saldo_antes"] = saldoAntes,
                    ["pagos"] = origen.PagosAcum,
                    ["nc_imp"] = montoNC,
                    ["saldo_despues"] = saldoDespues
                },
                conn, tx);

            return new NCApplicationResult
            {
                Success = true,
                Message = $"Saldo reducido de {saldoAntes:C} a {saldoDespues:C}",
                Modo = "deuda_reducida",
                MontoDeuda = montoNC,
                MontoCartera = 0
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  CASO 2 — PUE pagada: todo a saldo a favor / reembolso
        // ══════════════════════════════════════════════════════════════════

        private static NCApplicationResult _ManejarPUEPagada(
            NCApplicationInput input,
            FacturaOrigenInfo origen,
            decimal montoNC,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            // Registrar snapshot (saldo era 0, se mantiene en 0)
            RunScalar(@"
                INSERT INTO nc_estado_factura_origen
                    (nc_factura_id, origen_factura_id, metodo_pago,
                     total_factura, saldo_antes_nc, pagos_acumulados,
                     nc_importe, saldo_despues_nc)
                VALUES
                    (@nc_id, @orig_id, @mdp,
                     @total, 0, @pagos, @nc_imp, 0)",
                new Dictionary<string, object>
                {
                    ["nc_id"] = input.NcFacturaId,
                    ["orig_id"] = origen.Id,
                    ["mdp"] = origen.MetodoPago,
                    ["total"] = origen.Total,
                    ["pagos"] = origen.PagosAcum,
                    ["nc_imp"] = montoNC
                },
                conn, tx);

            if (input.DecisionSaldo == "reembolso")
                return _RegistrarReembolso(input, montoNC, "pue_pagada", conn, tx);

            // Por defecto: saldo a favor
            return _CrearSaldoFavor(input, "pue_pagada", conn, tx);
        }

        // ══════════════════════════════════════════════════════════════════
        //  CASO 3 — PPD overflow: split definido por el usuario
        //  MontoADeuda  → reduce saldo de la factura
        //  MontoACartera → crea saldo a favor
        // ══════════════════════════════════════════════════════════════════

        private static NCApplicationResult _ManejarPPDOverflow(
            NCApplicationInput input,
            FacturaOrigenInfo origen,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            decimal montoDeuda = input.MontoADeuda;
            decimal montoCartera = input.MontoACartera;

            // Validar que el usuario no asignó más a deuda de lo que había
            if (montoDeuda > origen.Saldo)
                throw new Exception(
                    $"MontoADeuda ({montoDeuda:C}) supera el saldo pendiente ({origen.Saldo:C}). " +
                    "Ajusta el split en el formulario.");

            // Validar suma
            if (Math.Abs((montoDeuda + montoCartera) - input.NcTotal) > 0.01m)
                throw new Exception(
                    $"El split no suma al total de la NC. " +
                    $"Deuda={montoDeuda:C} + Cartera={montoCartera:C} ≠ {input.NcTotal:C}");

            // 1) Reducir saldo de la factura origen
            decimal saldoAntes = origen.Saldo;
            decimal saldoDespues = Math.Max(0, saldoAntes - montoDeuda);

            if (montoDeuda > 0)
            {
                RunScalar("UPDATE factura SET saldo = @s WHERE id = @id",
                    new Dictionary<string, object> { ["s"] = saldoDespues, ["id"] = origen.Id },
                    conn, tx);
            }

            // 2) Registrar snapshot
            RunScalar(@"
                INSERT INTO nc_estado_factura_origen
                    (nc_factura_id, origen_factura_id, metodo_pago,
                     total_factura, saldo_antes_nc, pagos_acumulados,
                     nc_importe, saldo_despues_nc)
                VALUES
                    (@nc_id, @orig_id, @mdp,
                     @total, @sa, @pagos, @nc_imp, @sd)",
                new Dictionary<string, object>
                {
                    ["nc_id"] = input.NcFacturaId,
                    ["orig_id"] = origen.Id,
                    ["mdp"] = origen.MetodoPago,
                    ["total"] = origen.Total,
                    ["sa"] = saldoAntes,
                    ["pagos"] = origen.PagosAcum,
                    ["nc_imp"] = input.NcTotal,
                    ["sd"] = saldoDespues
                },
                conn, tx);

            // 3) Registrar decisión de split
            RunScalar(@"
                INSERT INTO nc_split_ppd
                    (nc_factura_id, origen_factura_id, saldo_pendiente,
                     nc_total, monto_a_deuda, monto_a_cartera, usuario_id)
                VALUES
                    (@nc_id, @orig_id, @sp, @nc_total, @deuda, @cartera, @usr)",
                new Dictionary<string, object>
                {
                    ["nc_id"] = input.NcFacturaId,
                    ["orig_id"] = origen.Id,
                    ["sp"] = saldoAntes,
                    ["nc_total"] = input.NcTotal,
                    ["deuda"] = montoDeuda,
                    ["cartera"] = montoCartera,
                    ["usr"] = input.UsuarioId
                },
                conn, tx);

            // 4) Crear saldo a favor por el excedente
            int? sfId = null;
            if (montoCartera > 0)
            {
                var sfInput = new NCApplicationInput
                {
                    NcFacturaId = input.NcFacturaId,
                    ClienteId = input.ClienteId,
                    NcTotal = montoCartera,
                    Moneda = input.Moneda,
                    UsuarioId = input.UsuarioId
                };
                var sfResult = _CrearSaldoFavor(sfInput, "ppd_overflow", conn, tx);
                sfId = sfResult.SaldoFavorId;
            }

            return new NCApplicationResult
            {
                Success = true,
                Message = $"Split: {montoDeuda:C} a deuda, {montoCartera:C} a cartera",
                Modo = "split",
                MontoDeuda = montoDeuda,
                MontoCartera = montoCartera,
                SaldoFavorId = sfId
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPER — Crear saldo a favor en nc_saldo_favor
        // ══════════════════════════════════════════════════════════════════

        private static NCApplicationResult _CrearSaldoFavor(
            NCApplicationInput input,
            string origen,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var newId = RunScalar(@"
                INSERT INTO nc_saldo_favor
                    (nc_factura_id, cliente_id, origen,
                     monto_original, monto_disponible,
                     moneda, estado, usuario_creacion)
                VALUES
                    (@nc_id, @cli_id, @origen,
                     @monto, @monto,
                     @moneda, 'disponible', @usr)
                RETURNING id",
                new Dictionary<string, object>
                {
                    ["nc_id"] = input.NcFacturaId,
                    ["cli_id"] = input.ClienteId,
                    ["origen"] = origen,
                    ["monto"] = input.NcTotal,
                    ["moneda"] = input.Moneda ?? "MXN",
                    ["usr"] = input.UsuarioId
                },
                conn, tx);

            return new NCApplicationResult
            {
                Success = true,
                Message = $"Saldo a favor de {input.NcTotal:C} creado para cliente {input.ClienteId}",
                Modo = "saldo_favor",
                MontoDeuda = 0,
                MontoCartera = input.NcTotal,
                SaldoFavorId = newId != null ? Convert.ToInt32(newId) : (int?)null
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPER — Registrar decisión de reembolso
        // ══════════════════════════════════════════════════════════════════

        private static NCApplicationResult _RegistrarReembolso(
            NCApplicationInput input,
            decimal monto,
            string origen,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            // Crear saldo a favor con estado 'reembolsado' para trazabilidad
            var newId = RunScalar(@"
                INSERT INTO nc_saldo_favor
                    (nc_factura_id, cliente_id, origen,
                     monto_original, monto_disponible,
                     moneda, estado, usuario_creacion,
                     notas)
                VALUES
                    (@nc_id, @cli_id, @origen,
                     @monto, 0,
                     @moneda, 'reembolsado', @usr,
                     'Reembolso directo — decisión del usuario al emitir NC')
                RETURNING id",
                new Dictionary<string, object>
                {
                    ["nc_id"] = input.NcFacturaId,
                    ["cli_id"] = input.ClienteId,
                    ["origen"] = origen,
                    ["monto"] = monto,
                    ["moneda"] = input.Moneda ?? "MXN",
                    ["usr"] = input.UsuarioId
                },
                conn, tx);

            int sfId = newId != null ? Convert.ToInt32(newId) : 0;

            // Registrar la aplicación inmediata como reembolso
            if (sfId > 0)
            {
                RunScalar(@"
                    INSERT INTO nc_aplicacion_saldo
                        (saldo_favor_id, factura_destino_id, tipo_aplicacion,
                         monto_aplicado, saldo_antes, saldo_despues, usuario_id)
                    VALUES
                        (@sf_id, NULL, 'reembolso',
                         @monto, @monto, 0, @usr)",
                    new Dictionary<string, object>
                    {
                        ["sf_id"] = sfId,
                        ["monto"] = monto,
                        ["usr"] = input.UsuarioId
                    },
                    conn, tx);
            }

            return new NCApplicationResult
            {
                Success = true,
                Message = $"Reembolso de {monto:C} registrado para cliente {input.ClienteId}",
                Modo = "reembolso",
                MontoDeuda = 0,
                MontoCartera = monto,
                SaldoFavorId = sfId > 0 ? sfId : (int?)null
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  MÉTODO AUXILIAR — Calcular saldo pendiente real de una factura
        //  Útil para el front antes de mostrar el panel de split al usuario.
        // ══════════════════════════════════════════════════════════════════

        public static decimal ObtenerSaldoPendiente(
            int facturaId,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var row = RunQ(@"
                SELECT
                    f.saldo,
                    COALESCE(
                        (SELECT SUM(p.importe)
                         FROM pagos p
                         WHERE p.factura_id = f.id
                           AND p.cancelado  = false), 0
                    ) AS pagos_acum
                FROM factura f WHERE f.id = @id",
                new Dictionary<string, object> { ["id"] = facturaId },
                conn, tx);

            if (!row.Any()) return 0;
            return Convert.ToDecimal(row[0]["saldo"]);
        }

        public static NCApplicationResult AplicarSaldoFavorAFactura(
    AplicarSaldoFavorInput input,
    NpgsqlConnection conn,
    NpgsqlTransaction tx)
        {
            // 1. Leer el saldo disponible con bloqueo optimista
            var rowSaldo = RunQ(@"
        SELECT id, monto_disponible, moneda, cliente_id, nc_factura_id
        FROM nc_saldo_favor
        WHERE id = @id AND estado != 'agotado'
        FOR UPDATE",
                new Dictionary<string, object> { ["id"] = input.SaldoFavorId },
                conn, tx);

            if (!rowSaldo.Any())
                throw new Exception("Saldo a favor no encontrado o ya agotado.");

            decimal disponible = Convert.ToDecimal(rowSaldo[0]["monto_disponible"]);

            if (input.MontoAAplicar > disponible)
                throw new Exception(
                    $"Monto a aplicar ({input.MontoAAplicar:C}) supera el disponible ({disponible:C}).");

            // 2. Leer la factura destino con bloqueo
            var rowFact = RunQ(@"
        SELECT id, saldo, total, statusfactura
        FROM factura
        WHERE id = @id AND statusfactura = 'TIMBRADA'
        FOR UPDATE",
                new Dictionary<string, object> { ["id"] = input.FacturaDestinoId },
                conn, tx);

            if (!rowFact.Any())
                throw new Exception("Factura destino no encontrada o no timbrada.");

            decimal saldoFactura = Convert.ToDecimal(rowFact[0]["saldo"]);
            decimal montoReal = Math.Min(input.MontoAAplicar, saldoFactura);

            if (montoReal <= 0)
                throw new Exception("La factura destino ya está liquidada.");

            decimal saldoFacturaNuevo = Math.Max(0, saldoFactura - montoReal);
            decimal saldoFavorNuevo = disponible - montoReal;
            string nuevoEstado = saldoFavorNuevo <= 0 ? "agotado" : "parcialmente_aplicado";

            // 3. Actualizar factura destino
            RunScalar(
                "UPDATE factura SET saldo = @s WHERE id = @id",
                new Dictionary<string, object> { ["s"] = saldoFacturaNuevo, ["id"] = input.FacturaDestinoId },
                conn, tx);

            // 4. Actualizar saldo a favor
            RunScalar(@"
        UPDATE nc_saldo_favor
           SET monto_disponible = @disp, estado = @est
         WHERE id = @id",
                new Dictionary<string, object>
                {
                    ["disp"] = saldoFavorNuevo,
                    ["est"] = nuevoEstado,
                    ["id"] = input.SaldoFavorId
                },
                conn, tx);

            // 5. Registrar la aplicación
            RunScalar(@"
        INSERT INTO nc_aplicacion_saldo
            (saldo_favor_id, factura_destino_id, tipo_aplicacion,
             monto_aplicado, saldo_antes, saldo_despues, usuario_id)
        VALUES
            (@sf, @fd, 'contra_factura', @monto, @sa, @sd, @usr)",
                new Dictionary<string, object>
                {
                    ["sf"] = input.SaldoFavorId,
                    ["fd"] = input.FacturaDestinoId,
                    ["monto"] = montoReal,
                    ["sa"] = disponible,
                    ["sd"] = saldoFavorNuevo,
                    ["usr"] = input.UsuarioId
                },
                conn, tx);

            return new NCApplicationResult
            {
                Success = true,
                Message = $"Aplicados {montoReal:C} al saldo de la factura. Saldo NC restante: {saldoFavorNuevo:C}",
                Modo = "contra_factura",
                MontoDeuda = montoReal,
                MontoCartera = saldoFavorNuevo,
                SaldoFavorId = input.SaldoFavorId
            };
        }

    }
}