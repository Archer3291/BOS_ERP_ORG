// Services/Refacturacion/AnalizadorDocumentos.cs
using BOS_ERP.Controllers;

namespace BOS_ERP.Services.Refacturacion
{
    /// <summary>
    /// Radiografía de la cadena documental colgada de una factura (complementos de pago,
    /// anticipos aplicados, cobros, notas de crédito/débito, sustitutos vigentes) + saldo.
    /// La produce <see cref="AnalizadorDocumentos.Analizar"/>.
    /// </summary>
    public class AnalisisDocumentos
    {
        public int EncabezadoId { get; set; }
        public int FacturaId { get; set; }
        public decimal Total { get; set; }
        public decimal Saldo { get; set; }
        public string MetodoPago { get; set; } = "";

        public int ComplementosVigentes { get; set; }
        public decimal TotalComplementos { get; set; }
        public decimal MontoAnticiposAplicado { get; set; }   // neto (aplicaciones - reversos)
        public decimal MontoCobrosAplicados { get; set; }     // cobros directos no cancelados
        public int NotasCredito { get; set; }
        public int NotasDebito { get; set; }
        public int NotasAplicacionAnticipo { get; set; }   // encabezadomov nat='NT' (aplicación de anticipo)
        public int SustitutosVigentes { get; set; }

        public bool TieneComplementos => ComplementosVigentes > 0;
        public bool TieneAnticipos => MontoAnticiposAplicado > 0;
        public bool TieneCobros => MontoCobrosAplicados > 0;
        public bool EstaPagada => Saldo <= 0m && Total > 0m;
        // "Tiene pagos" = complementos, anticipos, cobros aplicados o saldo ya abonado.
        public bool TienePagos =>
            TieneComplementos || TieneAnticipos || TieneCobros || (Total > 0m && Saldo < Total);
    }

    /// <summary>
    /// Veredicto de si una factura puede refacturarse dado su árbol de documentos:
    /// bloqueos (impiden avanzar), advertencias (informativas) y una posible sugerencia
    /// de usar Nota de Crédito en vez de sustituir.
    /// </summary>
    public class VerdictoDocumentos
    {
        public List<string> Bloqueos { get; set; } = new();
        public List<string> Advertencias { get; set; } = new();
        public string SugerenciaNotaCredito { get; set; }   // null si no aplica
        public bool PuedeRefacturar => Bloqueos.Count == 0;
    }

    /// <summary>
    /// Análisis y evaluación —única fuente de verdad— de los documentos relacionados a una
    /// factura, usada tanto por los handlers (bloqueo real de la ejecución) como por el wizard
    /// (endpoint que muestra el checklist y bloquea el botón "Continuar").
    ///
    /// Fuentes de datos (tablas dedicadas, NO se infiere por encabezados_padre/tp_mov):
    ///   · complementos de pago → factura_complementos_pago (id_encabezado_factura)
    ///   · anticipos aplicados  → factura_anticipos (id_factura_principal → factura.id)
    ///   · cobros aplicados     → aplicaciones_cobro_cliente (encabezado_id, no cancelados)
    ///   · saldo/deuda          → cartera_clientes (encabezado_id)
    ///   · notas NC/ND y sustitutos → encabezadomov.encabezados_padre (sí los liga ahí)
    /// </summary>
    public static class AnalizadorDocumentos
    {
        // Tipos de refacturación que ALTERAN importes (subtotal/impuestos/total). Para estos, si
        // la factura ya tiene pagos, se sugiere Nota de Crédito en lugar de cancelar-sustituir.
        private static readonly HashSet<string> TiposCambianImportes =
            new(StringComparer.OrdinalIgnoreCase)
            { "moneda", "conceptos", "impuestos", "devolucion", "parcial", "anticipo" };

        // Núcleo: factura, saldo (cartera_clientes), notas NC/ND y sustitutos (encabezados_padre).
        // Usa solo tablas siempre presentes; las relaciones "de pago" van en queries aparte.
        private const string SqlNucleo = @"
SELECT
    fa.id        AS factura_id,
    fa.total     AS total,
    fa.mdpfactura AS mdpfactura,
    COALESCE((SELECT cc.saldo_pendiente FROM cartera_clientes cc
        WHERE cc.encabezado_id = fa.encabezado_id AND COALESCE(cc.cancelada, false) = false
        ORDER BY cc.id_cartera_cliente DESC LIMIT 1), fa.saldo, fa.total) AS saldo,
    COALESCE((SELECT COUNT(*) FROM encabezadomov emn
        INNER JOIN factura fn ON fn.encabezado_id = emn.id_encabezado
        WHERE emn.encabezados_padre = fa.encabezado_id AND fn.serie = 'NC'
          AND COALESCE(emn.nat,'') <> 'NT'   -- las NT son notas por aplicación de anticipo (aparte)
          AND fn.statusfactura NOT IN ('Cancelada','Error al Cancelar')), 0) AS notas_credito,
    -- Notas de crédito por APLICACIÓN DE ANTICIPO: encabezadomov nat='NT' colgado de la factura.
    -- Se identifica por el ENCABEZADO (estatus_id 27 = cancelado), sin exigir que la fila de
    -- `factura` cuelgue de ese encabezado: ConstruirFacturaParaNC deja nc.EncabezadoId apuntando
    -- al encabezado de la FACTURA, así que el INNER JOIN podría no encontrarla.
    COALESCE((SELECT COUNT(*) FROM encabezadomov emt
        WHERE emt.encabezados_padre = fa.encabezado_id AND emt.nat = 'NT'
          AND COALESCE(emt.estatus_id, 0) <> 27), 0) AS notas_anticipo,
    COALESCE((SELECT COUNT(*) FROM encabezadomov emd
        INNER JOIN factura fd ON fd.encabezado_id = emd.id_encabezado
        WHERE emd.encabezados_padre = fa.encabezado_id AND fd.serie = 'ND'
          AND fd.statusfactura NOT IN ('Cancelada','Error al Cancelar')), 0) AS notas_debito,
    COALESCE((SELECT COUNT(*) FROM encabezadomov ems
        INNER JOIN factura fs ON fs.encabezado_id = ems.id_encabezado
        WHERE ems.encabezados_padre = fa.encabezado_id AND fs.statusfactura = 'Timbrada'
          AND COALESCE(ems.tp_mov,'') <> 'CPFAC'
          AND COALESCE(fs.serie,'') NOT IN ('NC','ND')), 0) AS sustitutos_vigentes
FROM factura fa
WHERE fa.encabezado_id = @encId
ORDER BY fa.id DESC
LIMIT 1";

        public static AnalisisDocumentos Analizar(Utilities utils, int encabezadoId)
        {
            var a = new AnalisisDocumentos { EncabezadoId = encabezadoId };
            var p = new Dictionary<string, object> { { "encId", encabezadoId } };

            var rows = utils.RunQuery(SqlNucleo, p);
            if (rows.Count > 0)
            {
                var r = rows[0];
                a.FacturaId = ToInt(r, "factura_id");
                a.Total = ToDec(r, "total");
                a.Saldo = ToDec(r, "saldo");
                a.MetodoPago = r.TryGetValue("mdpfactura", out var mp) ? mp?.ToString() ?? "" : "";
                a.NotasCredito = ToInt(r, "notas_credito");
                a.NotasDebito = ToInt(r, "notas_debito");
                a.NotasAplicacionAnticipo = ToInt(r, "notas_anticipo");
                a.SustitutosVigentes = ToInt(r, "sustitutos_vigentes");
            }

            // Complementos de pago: tabla dedicada. Se cuentan los complementos DISTINTOS cuyo CFDI
            // (por su encabezado) sigue vigente (no cancelado). Aquí estaba el bug: antes se buscaban
            // por encabezados_padre/tp_mov='CPFAC', que NO es como se registran.
            try
            {
                var rc = utils.RunQuery(
                    @"SELECT COUNT(DISTINCT fcp.id_encabezado_complemento) AS n,
                             COALESCE(SUM(fcp.monto_aplicado), 0)          AS monto
                      FROM factura_complementos_pago fcp
                      INNER JOIN factura fc ON fc.encabezado_id = fcp.id_encabezado_complemento
                      WHERE fcp.id_encabezado_factura = @encId
                        AND fc.statusfactura NOT IN ('Cancelada','Error al Cancelar')", p);
                if (rc.Count > 0)
                {
                    a.ComplementosVigentes = ToInt(rc[0], "n");
                    a.TotalComplementos = ToDec(rc[0], "monto");
                }
            }
            catch { /* tabla ausente en el entorno: no bloquea por complementos */ }

            // Anticipos aplicados (neto de reversos): factura_anticipos.id_factura_principal → factura.id.
            try
            {
                var ant = utils.RunScalar(
                    @"SELECT COALESCE(SUM(fan.monto_aplicado), 0)
                      FROM factura_anticipos fan
                      INNER JOIN factura fa ON fa.id = fan.id_factura_principal
                      WHERE fa.encabezado_id = @encId", p);
                a.MontoAnticiposAplicado = ToDecScalar(ant);
            }
            catch { a.MontoAnticiposAplicado = 0m; }

            // Cobros directos aplicados (no cancelados): aplicaciones_cobro_cliente.
            try
            {
                var pag = utils.RunScalar(
                    @"SELECT COALESCE(SUM(monto_aplicado), 0)
                      FROM aplicaciones_cobro_cliente
                      WHERE encabezado_id = @encId AND COALESCE(cancelado, false) = false", p);
                a.MontoCobrosAplicados = ToDecScalar(pag);
            }
            catch { a.MontoCobrosAplicados = 0m; }

            return a;
        }

        public static VerdictoDocumentos Evaluar(AnalisisDocumentos a, string tipoId)
        {
            var v = new VerdictoDocumentos();

            // ── Bloqueos: primero se resuelven los hijos, luego el padre ──
            if (a.SustitutosVigentes > 0)
                v.Bloqueos.Add("Ya existe un CFDI sustituto vigente para esta factura; resuélvelo antes de refacturar de nuevo.");

            if (a.ComplementosVigentes > 0)
                v.Bloqueos.Add($"La factura tiene {a.ComplementosVigentes} complemento(s) de pago vigente(s). " +
                    "Cancélalos primero (del más reciente al más antiguo) y luego la factura; no se puede sustituir mientras existan CFDI de pago que la referencian.");

            if (a.NotasDebito > 0)
                v.Bloqueos.Add($"La factura tiene {a.NotasDebito} nota(s) de débito relacionada(s), que impiden una sustitución simple.");

            // ── Advertencias: no bloquean, pero requieren revisión ──

            // Notas de crédito por aplicación de anticipo (nat='NT'): el orquestador las cancela
            // antes de sustituir la factura y re-emite una nueva contra el CFDI sustituto.
            if (a.NotasAplicacionAnticipo > 0)
                v.Advertencias.Add($"La factura tiene {a.NotasAplicacionAnticipo} nota(s) de crédito por aplicación de anticipo" +
                    (a.MontoAnticiposAplicado > 0 ? $" por ${a.MontoAnticiposAplicado:N2}" : "") + ". " +
                    "Se cancelarán automáticamente y se volverá a emitir una nueva contra el CFDI sustituto; verifica el resultado.");

            if (a.NotasCredito > 0)
                v.Advertencias.Add($"La factura tiene {a.NotasCredito} nota(s) de crédito relacionada(s). La sustitución no las cancela; verifica que la trazabilidad fiscal siga siendo correcta.");

            if (a.MontoCobrosAplicados > 0)
                v.Advertencias.Add($"La factura tiene cobros aplicados por ${a.MontoCobrosAplicados:N2}. Al cancelar y sustituir, revisa que los cobros/cartera queden correctamente reflejados en el nuevo CFDI.");

            // ── Sugerencia de Nota de Crédito (no se implementa aquí; vive en su módulo aparte) ──
            if (TiposCambianImportes.Contains(tipoId ?? "") && a.TienePagos)
                v.SugerenciaNotaCredito =
                    "Este cambio afecta importes y la factura ya tiene pagos/complementos/anticipos. " +
                    "En estos casos suele ser preferible una NOTA DE CRÉDITO (ajuste económico) en lugar de cancelar y sustituir. " +
                    "La nota de crédito se gestiona en su módulo aparte.";

            return v;
        }

        // Atajo para los handlers: analiza + evalúa y devuelve solo los bloqueos.
        public static List<string> Bloqueos(Utilities utils, int encabezadoId, string tipoId)
            => Evaluar(Analizar(utils, encabezadoId), tipoId).Bloqueos;

        private static int ToInt(Dictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToInt32(v) : 0;

        private static decimal ToDec(Dictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToDecimal(v) : 0m;

        private static decimal ToDecScalar(object v)
            => (v != null && v != DBNull.Value) ? Convert.ToDecimal(v) : 0m;
    }
}
