using BOS_ERP.Helpers;
using BOS_ERP.Models;
using Npgsql;

namespace BOS_ERP.Controllers
{
    /// <summary>
    /// Tracking de remisiones facturadas y registro del origen de una factura.
    ///
    /// Vive en Utilities porque los canales que lo necesitan cuelgan de ramas distintas de la
    /// jerarquía: VI y VN de VentasFacturaBaseController, el punto de venta y la consulta
    /// global de FacturacionVentaController, y VIN de FacturacionVentaINController. El único
    /// ancestro común es éste. Antes cada rama tenía su copia y las correcciones había que
    /// aplicarlas por duplicado —fue así como VIN se quedó sin la validación de partidas.
    ///
    /// Dos tablas, con papeles distintos:
    ///   · remision_partidas_facturadas → SALDO mutable: cuánto de cada partida falta por
    ///     facturar. Clave (encabezado_remision_id, id_partida_remision).
    ///   · factura_remisiones_origen    → RASTRO histórico: qué factura consumió qué partida
    ///     y por cuánto. Sólo crece.
    ///
    /// La columna se llama `encabezado_remision_id` por historia; lo que guarda es el
    /// documento de origen de la factura. Ojo: en el punto de venta ese documento es la VSREM,
    /// que NO es la que movió el inventario —eso ocurrió en la VSUC al vender—, así que el
    /// rastro sirve para trazabilidad y saldos, no para decidir una reversión. Esa decisión la
    /// toma <see cref="ReversionInventarioPolicy"/>.
    /// </summary>
    public partial class Utilities
    {
        /// <summary>Tag para los logs; cada canal lo sobreescribe (p.ej. "VIFacturaController").</summary>
        protected virtual string FacturaLogTag => "FacturacionVentas";

        /// <summary>
        /// Documentos que amparan mercancía entregada y por tanto pueden ser origen de una
        /// factura. Deliberadamente separada de ReversionInventarioPolicy.EsRemision: aquélla
        /// responde "¿revierto almacén?" y ésta "¿de aquí puede salir una factura?". Hoy sólo
        /// difieren en VSREM —origen válido, pero cuyo movimiento vive en la VSUC—, y si se
        /// unificaran ese matiz se perdería.
        /// </summary>
        private static readonly HashSet<string> NatsDeRemisionFacturable =
            new(StringComparer.OrdinalIgnoreCase) { "VIREM", "VNREM", "VINREM", "VSREM" };

        // ─────────────────────────────────────────────────────────────────
        // Validación del detalle que llega del modal de remisiones
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Valida el detalle y resuelve id_partida_remision por cve_prod cuando falta.
        /// Sin esto, una partida sin identificador acaba insertando un renglón de saldo con
        /// id_partida_remision nulo y el saldo de esa remisión deja de cuadrar.
        /// </summary>
        protected void ValidarDetalleRemisiones(
            List<RemisionPartidaParaFacturar> partidas,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            foreach (var p in partidas)
            {
                if (p.RemisionId <= 0)
                    throw new Exception(
                        $"El detalle de remisiones trae una partida sin remisión válida " +
                        $"(producto {p.CveProd ?? "?"}).");

                if (p.CantidadAFacturar <= 0)
                    throw new Exception(
                        $"La cantidad a facturar del producto {p.CveProd ?? "?"} " +
                        $"debe ser mayor a cero.");

                if (p.IdPartidaRemision.HasValue && p.IdPartidaRemision.Value > 0)
                    continue;

                if (string.IsNullOrWhiteSpace(p.CveProd))
                    throw new Exception(
                        $"La remisión {p.RemisionId} trae una partida sin id_partida_remision " +
                        $"ni clave de producto; no es posible identificarla.");

                var resolParam = new Dictionary<string, object>
                {
                    { "enc",      p.RemisionId },
                    { "cve_prod", p.CveProd }
                };

                var candidatas = RunQuery(@"
                    SELECT pd.id_partidas
                    FROM partidasdoc pd
                    WHERE pd.encabezado_id = @enc
                      AND pd.cve_prod      = @cve_prod
                    ORDER BY pd.nro_part",
                    resolParam, false, conn, tx);

                if (candidatas.Count == 0)
                    throw new Exception(
                        $"El producto {p.CveProd} no existe en la remisión {p.RemisionId}.");

                if (candidatas.Count > 1)
                    throw new Exception(
                        $"El producto {p.CveProd} aparece {candidatas.Count} veces en la " +
                        $"remisión {p.RemisionId}; vuelve a seleccionar las partidas desde " +
                        $"el modal para que se envíe el identificador exacto de cada una.");

                p.IdPartidaRemision = Convert.ToInt32(candidatas[0]["id_partidas"]);
            }

            var duplicada = partidas
                .GroupBy(p => new { p.RemisionId, Id = p.IdPartidaRemision.Value })
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicada != null)
                throw new Exception(
                    $"La partida {duplicada.First().CveProd} de la remisión " +
                    $"{duplicada.Key.RemisionId} viene repetida en la selección. " +
                    $"Agrúpala en un solo renglón antes de facturar.");
        }

        // ─────────────────────────────────────────────────────────────────
        // Facturación PARCIAL
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Actualiza saldos por partida y registra el origen con la cantidad real.</summary>
        protected void ActualizarPartidasRemisionesFacturadas(
            int idFactura,
            List<RemisionPartidaParaFacturar> partidas,
            List<int> remisionIds,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            foreach (int remId in remisionIds)
                InicializarPartidasSiFalta(remId, conn, tx);

            var porRemision = partidas.GroupBy(p => p.RemisionId);

            foreach (var grupo in porRemision)
            {
                int remisionId = grupo.Key;

                foreach (var p in grupo)
                {
                    if (!p.IdPartidaRemision.HasValue)
                        throw new Exception(
                            $"No se pudo identificar la partida de la remisión {p.RemisionId} " +
                            $"para el producto {p.CveProd}.");

                    int idPartida = p.IdPartidaRemision.Value;

                    var selParam = new Dictionary<string, object>
                    {
                        { "enc_rem",    p.RemisionId },
                        { "id_partida", idPartida }
                    };

                    // FOR UPDATE: sin el bloqueo, dos facturaciones simultáneas de la misma
                    // partida leerían el mismo saldo y ambas lo darían por disponible.
                    var rows = RunQuery(@"
                        SELECT id, cantidad_original, cantidad_facturada, estatus
                        FROM remision_partidas_facturadas
                        WHERE encabezado_remision_id = @enc_rem
                          AND id_partida_remision    = @id_partida
                        FOR UPDATE",
                        selParam, false, conn, tx);

                    if (rows.Count == 0)
                    {
                        var insParam = new Dictionary<string, object>
                        {
                            { "enc_rem",    p.RemisionId },
                            { "id_partida", idPartida },
                            { "cve_prod",   p.CveProd },
                            { "cant_orig",  p.CantidadAFacturar },
                            { "cant_fac",   p.CantidadAFacturar },
                            { "precio",     p.PrecioUnitario },
                            { "descuento",  p.Descuento },
                            { "usuario",    User.Identity?.Name ?? "sistema" }
                        };
                        RunQuery(@"
                            INSERT INTO remision_partidas_facturadas
                                (encabezado_remision_id, id_partida_remision, cve_prod,
                                 cantidad_original, cantidad_facturada, precio_unitario,
                                 descuento, usuario_registro)
                            VALUES (@enc_rem, @id_partida, @cve_prod,
                                    @cant_orig, @cant_fac, @precio, @descuento, @usuario)
                            ON CONFLICT (encabezado_remision_id, id_partida_remision)
                            DO UPDATE SET
                                cantidad_facturada =
                                    remision_partidas_facturadas.cantidad_facturada
                                    + EXCLUDED.cantidad_facturada",
                            insParam, false, conn, tx);
                    }
                    else
                    {
                        decimal cantOriginal = Convert.ToDecimal(rows[0]["cantidad_original"]);
                        decimal cantFacActual = Convert.ToDecimal(rows[0]["cantidad_facturada"]);
                        decimal saldoDisponible = cantOriginal - cantFacActual;

                        if (p.CantidadAFacturar <= 0)
                            throw new Exception(
                                $"La cantidad a facturar para {p.CveProd} debe ser mayor a cero.");

                        if (p.CantidadAFacturar > saldoDisponible + 0.00001m)
                            throw new Exception(
                                $"La cantidad a facturar ({p.CantidadAFacturar:F4}) excede el " +
                                $"saldo disponible ({saldoDisponible:F4}) para el producto {p.CveProd} " +
                                $"en la remisión {p.RemisionId}.");

                        decimal nuevaCantFac = cantFacActual + p.CantidadAFacturar;
                        var updParam = new Dictionary<string, object>
                        {
                            { "nueva_cant", nuevaCantFac },
                            { "enc_rem",    p.RemisionId },
                            { "id_partida", idPartida }
                        };
                        RunUpdate(@"
                            UPDATE remision_partidas_facturadas
                            SET cantidad_facturada = @nueva_cant,
                                estatus = CASE
                                    WHEN @nueva_cant >= cantidad_original THEN 'completa'
                                    WHEN @nueva_cant > 0                  THEN 'parcial'
                                    ELSE                                       'pendiente'
                                END
                            WHERE encabezado_remision_id = @enc_rem
                              AND id_partida_remision    = @id_partida",
                            updParam, false, conn, tx);
                    }

                    decimal importe = p.CantidadAFacturar * p.PrecioUnitario * (1 - p.Descuento / 100m);
                    var insOrigenParam = new Dictionary<string, object>
                    {
                        { "enc_fac",    idFactura },
                        { "enc_rem",    p.RemisionId },
                        { "id_partida", idPartida },
                        { "cve_prod",   p.CveProd },
                        { "cant_fac",   p.CantidadAFacturar },
                        { "precio",     p.PrecioUnitario },
                        { "descuento",  p.Descuento },
                        { "importe",    importe },
                        { "usuario",    User.Identity?.Name ?? "sistema" }
                    };
                    RunQuery(@"
                        INSERT INTO factura_remisiones_origen
                            (encabezado_factura_id, encabezado_remision_id, id_partida_remision,
                             cve_prod, cantidad_facturada, precio_unitario, descuento,
                             importe, usuario_facturo)
                        VALUES
                            (@enc_fac, @enc_rem, @id_partida,
                             @cve_prod, @cant_fac, @precio, @descuento, @importe, @usuario)
                        ON CONFLICT (encabezado_factura_id, encabezado_remision_id, id_partida_remision)
                        DO NOTHING",
                        insOrigenParam, false, conn, tx);
                }

                ActualizarEstatusRemisionSiCompleta(remisionId, conn, tx);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Facturación TOTAL
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Consume íntegramente las remisiones indicadas: registra el origen y cierra el
        /// encabezado según el tracking.
        /// </summary>
        protected void MarcarRemisionesComoFacturadas(
            List<int> remisionIds, int idFactura, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            RegistrarOrigenFacturaTotal(idFactura, remisionIds, conn, tx);

            foreach (int remId in remisionIds)
                ActualizarEstatusRemisionSiCompleta(remId, conn, tx);
        }

        /// <summary>
        /// Registra el origen de una factura emitida contra UN documento completo, cuando ese
        /// documento es una remisión. Es el caso de "Buscar Documentos", donde no hay modal de
        /// remisiones y por tanto no llega ni remisionesIds ni el detalle de partidas.
        ///
        /// El filtro por naturaleza no es cosmético: por esa vía el padre puede ser un pedido
        /// o incluso otra factura, y sembrar remision_partidas_facturadas con sus partidas
        /// ensuciaría la tabla de saldos con documentos que nunca descargaron almacén.
        /// </summary>
        /// <returns>true si se registró; false si el documento no es una remisión.</returns>
        protected bool RegistrarOrigenSiEsRemision(
            int idFactura, int documentoOrigen, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            if (documentoOrigen <= 0) return false;

            string nat = RunScalar(
                "SELECT nat FROM encabezadomov WHERE id_encabezado = @id",
                new Dictionary<string, object> { { "id", documentoOrigen } },
                false, conn, tx)?.ToString();

            if (!NatsDeRemisionFacturable.Contains((nat ?? "").Trim()))
            {
                LogErrorHelper.RegistrarLog(FacturaLogTag, idFactura.ToString(),
                    $"El documento origen {documentoOrigen} es {nat ?? "(desconocido)"}, " +
                    "no una remisión: no se registra origen ni saldos.",
                    User.Identity?.Name, nivel: "INFO");
                return false;
            }

            RegistrarOrigenFacturaTotal(idFactura, new[] { documentoOrigen }, conn, tx);
            return true;
        }

        /// <summary>
        /// Registra el origen de una factura que consume ÍNTEGRAMENTE sus documentos fuente.
        ///
        /// No toca el estatus del encabezado origen: cada flujo cierra sus documentos a su
        /// manera y meter aquí ese cambio alteraría el comportamiento del punto de venta, que
        /// ya los cierra por su cuenta. Quien necesite además el cierre 11/41 usa
        /// <see cref="MarcarRemisionesComoFacturadas"/>.
        /// </summary>
        protected void RegistrarOrigenFacturaTotal(
            int idFactura,
            IEnumerable<int> documentosOrigen,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            if (documentosOrigen == null) return;

            foreach (int origenId in documentosOrigen.Where(id => id > 0).Distinct())
            {
                InicializarPartidasSiFalta(origenId, conn, tx);

                var param = new Dictionary<string, object> { { "enc_rem", origenId } };

                RunUpdate(@"
                    UPDATE remision_partidas_facturadas
                    SET cantidad_facturada = cantidad_original,
                        estatus            = 'completa'
                    WHERE encabezado_remision_id = @enc_rem
                      AND estatus IN ('pendiente', 'parcial')",
                    param, false, conn, tx);

                var partidas = RunQuery(@"
                    SELECT id_partida_remision, cve_prod, cantidad_original,
                           precio_unitario, descuento
                    FROM remision_partidas_facturadas
                    WHERE encabezado_remision_id = @enc_rem",
                    param, false, conn, tx);

                foreach (var p in partidas)
                {
                    decimal cant = Convert.ToDecimal(p["cantidad_original"]);
                    decimal precio = Convert.ToDecimal(p["precio_unitario"]);
                    decimal dto = Convert.ToDecimal(p["descuento"]);

                    var insParam = new Dictionary<string, object>
                    {
                        { "enc_fac",    idFactura },
                        { "enc_rem",    origenId },
                        { "id_partida", Convert.ToInt32(p["id_partida_remision"]) },
                        { "cve_prod",   p["cve_prod"]?.ToString() ?? "" },
                        { "cant_fac",   cant },
                        { "precio",     precio },
                        { "descuento",  dto },
                        { "importe",    Math.Round(cant * precio * (1 - dto / 100m), 2) },
                        { "usuario",    User.Identity?.Name ?? "sistema" }
                    };

                    RunQuery(@"
                        INSERT INTO factura_remisiones_origen
                            (encabezado_factura_id, encabezado_remision_id, id_partida_remision,
                             cve_prod, cantidad_facturada, precio_unitario, descuento,
                             importe, usuario_facturo)
                        VALUES
                            (@enc_fac, @enc_rem, @id_partida,
                             @cve_prod, @cant_fac, @precio, @descuento, @importe, @usuario)
                        ON CONFLICT (encabezado_factura_id, encabezado_remision_id, id_partida_remision)
                        DO NOTHING",
                        insParam, false, conn, tx);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Apoyo
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Siembra el tracking de un documento origen desde sus partidas reales.
        /// Idempotente: el ON CONFLICT deja pasar la segunda llamada sin tocar saldos.
        /// Lee de partidasdoc, no del formulario, que es lo que permite usarlo en flujos
        /// donde nadie captura un detalle de remisiones (punto de venta, factura global).
        /// </summary>
        protected void InicializarPartidasSiFalta(
            int documentoOrigenId, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var insParam = new Dictionary<string, object>
            {
                { "enc",     documentoOrigenId },
                { "usuario", User.Identity?.Name ?? "sistema" }
            };

            RunQuery(@"
                INSERT INTO remision_partidas_facturadas
                    (encabezado_remision_id, id_partida_remision, cve_prod,
                     cantidad_original, cantidad_facturada, precio_unitario,
                     descuento, usuario_registro)
                SELECT
                    @enc,
                    pd.id_partidas,
                    pd.cve_prod,
                    pd.cant_ud,
                    0,
                    pd.pv_prod,
                    COALESCE(pd.dto1, 0),
                    @usuario
                FROM partidasdoc pd
                WHERE pd.encabezado_id = @enc
                ON CONFLICT (encabezado_remision_id, id_partida_remision)
                DO NOTHING",
                insParam, false, conn, tx);
        }

        /// <summary>Cierra (11) o marca parcial (41) el encabezado de la remisión según sus partidas.</summary>
        protected void ActualizarEstatusRemisionSiCompleta(
            int remisionId, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var param = new Dictionary<string, object> { { "enc", remisionId } };

            int totalPartidas = Convert.ToInt32(RunScalar(
                "SELECT COUNT(*) FROM partidasdoc WHERE encabezado_id = @enc",
                param, false, conn, tx));

            if (totalPartidas == 0)
            {
                LogErrorHelper.RegistrarLog(FacturaLogTag, remisionId.ToString(),
                    $"Remisión {remisionId} sin partidas en partidasdoc.", nivel: "WARN");
                return;
            }

            int partidasConTracking = Convert.ToInt32(RunScalar(@"
                SELECT COUNT(*) FROM remision_partidas_facturadas
                WHERE encabezado_remision_id = @enc",
                param, false, conn, tx));

            if (partidasConTracking < totalPartidas)
            {
                LogErrorHelper.RegistrarLog(FacturaLogTag, remisionId.ToString(),
                    $"Remisión {remisionId}: tracking incompleto " +
                    $"({partidasConTracking}/{totalPartidas}) — no se evalúa cierre.",
                    nivel: "INFO");
                return;
            }

            int partidasCompletas = Convert.ToInt32(RunScalar(@"
                SELECT COUNT(*) FROM remision_partidas_facturadas
                WHERE encabezado_remision_id = @enc AND estatus = 'completa'",
                param, false, conn, tx));

            int partidasPendientes = Convert.ToInt32(RunScalar(@"
                SELECT COUNT(*) FROM remision_partidas_facturadas
                WHERE encabezado_remision_id = @enc AND estatus = 'pendiente'",
                param, false, conn, tx));

            if (partidasCompletas == totalPartidas)
            {
                RunUpdate("UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @enc",
                          param, false, conn, tx);
                LogErrorHelper.RegistrarLog(FacturaLogTag, remisionId.ToString(),
                    $"Remisión {remisionId} CERRADA (estatus 11). {totalPartidas} partidas completas.",
                    nivel: "INFO");
            }
            else if (partidasPendientes == totalPartidas)
            {
                LogErrorHelper.RegistrarLog(FacturaLogTag, remisionId.ToString(),
                    $"Remisión {remisionId}: sin facturación aún — estatus sin cambios.", nivel: "INFO");
            }
            else
            {
                RunUpdate("UPDATE encabezadomov SET estatus_id = 41 WHERE id_encabezado = @enc",
                          param, false, conn, tx);
                LogErrorHelper.RegistrarLog(FacturaLogTag, remisionId.ToString(),
                    $"Remisión {remisionId} PARCIAL (estatus 41). " +
                    $"{partidasCompletas}/{totalPartidas} completas.", nivel: "INFO");
            }
        }
    }
}
