// handlers/Refacturacion/MonedaTypeHandler.cs
using System.Data;
using System.Globalization;
using BOS_ERP.Controllers;
using BOS_ERP.Services.Refacturacion;

// Cambio de moneda y/o tipo de cambio (Art. 20 CFF; Regla 2.7.1.26 RMF).
// Es una sustitución estándar (relación 04): se timbra un CFDI nuevo con la
// Moneda/TipoCambio corregidos y se cancela el original (motivo 01).
//
// Contrato con el front (moneda.js → collectChanges):
//   moneda            → nueva moneda ISO (c_Moneda), p.ej. "USD","EUR","MXN".
//   tipoCambio        → nuevo TipoCambio (MXN por 1 unidad de la moneda). "1" para MXN.
//   factorConversion  → factor que reexpresa los importes de CADA concepto en la nueva
//                       moneda. 1 = los importes no cambian (Caso 1: misma moneda, solo
//                       se corrige el TC). !=1 = reexpresión de importes (Caso 2/3):
//                          · preservar valor en MXN  → factor = TC_viejo / TC_nuevo
//                          · capturar total corregido → factor = total_nuevo / total_actual
// El XmlCfdiBuilder recalcula Subtotal/IVA/Total desde precioUnit, así que basta con
// reexpresar precioUnit; aquí también ajustamos los campos monetarios de la fila
// (Importe/Descuento/Saldo/Flete) para que la factura persistida quede coherente.
public class MonedaTypeHandler : IRefacturacionTypeHandler
{
    private readonly Utilities _utils;
    public string TipoId => "moneda";

    public MonedaTypeHandler(Utilities utils) => _utils = utils;

    public async Task<ValidacionResultado> ValidarAsync(int encabezadoId, Dictionary<string, object> cambios)
    {
        var r = new ValidacionResultado { EsValido = true };

        string moneda = (cambios.GetValueOrDefault("moneda")?.ToString() ?? "").Trim().ToUpper();
        decimal tc = ParseDec(cambios.GetValueOrDefault("tipoCambio")?.ToString());
        decimal factor = ParseDec(cambios.GetValueOrDefault("factorConversion")?.ToString(), 1m);

        // ── Reglas de catálogo/Art. 20 CFF ──
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            r.Errores.Add("La moneda destino es obligatoria (código ISO de 3 letras del catálogo c_Moneda).");

        if (moneda == "MXN")
        {
            if (tc != 1m)
                r.Errores.Add("Para moneda MXN el tipo de cambio debe ser exactamente 1.");
        }
        else if (!string.IsNullOrWhiteSpace(moneda))
        {
            if (tc <= 0m)
                r.Errores.Add("Para moneda extranjera el tipo de cambio debe ser mayor a 0 (Art. 20 CFF, publicado en DOF).");
            else if (tc == 1m)
                r.Errores.Add("Un tipo de cambio de 1 no es válido para moneda extranjera.");
        }

        if (factor <= 0m)
            r.Errores.Add("El factor de conversión de importes debe ser mayor a 0.");

        // Validaciones comunes de la cadena documental (complementos, anticipos, notas, sustituto).
        // Cambiar moneda/importe además invalida los complementos de pago (REP) ya emitidos, por lo
        // que su presencia bloquea igual que en cualquier sustitución.
        r.Errores.AddRange(AnalizadorDocumentos.Bloqueos(_utils, encabezadoId, TipoId));

        r.EsValido = r.Errores.Count == 0;
        return r;
    }

    public async Task<ResultadoConstruccionCfdi> ConstruirXmlNuevoAsync(int encabezadoId, Dictionary<string, object> cambios,
        string motivo, string tipoRelacion, string uuidRelacionado)
    {
        // Reconstruye la Factura original desde la BD/XML timbrado y sobreescribe solo
        // moneda, tipo de cambio y (si aplica) los importes con los valores del wizard.
        var factura = FacturaBuilder.DesdeEncabezado(_utils, encabezadoId);

        string moneda = (cambios.GetValueOrDefault("moneda")?.ToString() ?? "").Trim().ToUpper();
        decimal tc = ParseDec(cambios.GetValueOrDefault("tipoCambio")?.ToString());
        decimal factor = ParseDec(cambios.GetValueOrDefault("factorConversion")?.ToString(), 1m);

        if (!string.IsNullOrWhiteSpace(moneda))
            factura.Moneda = moneda;

        // TipoCambio: MXN siempre 1; extranjera el capturado (>0).
        if (factura.Moneda == "MXN")
            factura.TipoCambio = 1m;
        else if (tc > 0m)
            factura.TipoCambio = tc;

        // OJO: la columna persistida es `tdc` (GuardarFacturaNueva mapea @tdc = factura.Tdc),
        // no factura.TipoCambio. Hay que sincronizar ambos o la BD guardaría el TC viejo.
        factura.Tdc = factura.TipoCambio;

        // ── Reexpresar importes en la nueva moneda (Caso 2/3). factor==1 → sin cambio (Caso 1). ──
        if (factor > 0m && factor != 1m)
        {
            foreach (DataRow row in factura.Tproductos.Rows)
            {
                decimal precioOld = Convert.ToDecimal(row["precioUnit"]);
                decimal precioNew = Math.Round(precioOld * factor, 6); // formato ValorUnitario "0.######"
                row["precioUnit"] = precioNew;

                // Mantener coherente la columna `importe` de la fila (el builder recalcula la suya,
                // pero dfactura/addenda leen esta). Se recompone con el precio ya convertido.
                if (factura.Tproductos.Columns.Contains("importe"))
                    row["importe"] = Math.Round(Convert.ToDecimal(row["cantidad"]) * precioNew, 2);
            }

            // Campos monetarios de encabezado que el builder NO recalcula.
            factura.Flete = Math.Round(factura.Flete * factor, 2);
            factura.Importe = Math.Round(factura.Importe * factor, 2);
            factura.Descuento = Math.Round(factura.Descuento * factor, 2);
            factura.Saldo = Math.Round(factura.Saldo * factor, 2);
        }

        // XmlCfdiBuilder recalcula Subtotal/IVA/Total desde los precioUnit ya convertidos
        // y escribe Moneda/TipoCambio (MXN→"1", extranjera→invariante 4 decimales).
        return new ResultadoConstruccionCfdi
        {
            Factura = factura,
            Xml = XmlCfdiBuilder.Construir(factura, tipoRelacion, uuidRelacionado, _utils)
        };
    }

    private static decimal ParseDec(string s, decimal def = 0m)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : def;
}
