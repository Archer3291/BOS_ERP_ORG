// handlers/Refacturacion/MetodoPagoTypeHandler.cs
using BOS_ERP.Controllers;
using BOS_ERP.Services.Refacturacion;

// Cambio de método de pago (PUE ↔ PPD) y su forma de pago asociada.
// Es una sustitución estándar (relación 04): se timbra un CFDI nuevo con el
// MetodoPago/FormaPago corregidos y se cancela el original (motivo 01).
public class MetodoPagoTypeHandler : IRefacturacionTypeHandler
{
    private readonly Utilities _utils;
    public string TipoId => "metodo-pago";

    public MetodoPagoTypeHandler(Utilities utils) => _utils = utils;

    public async Task<ValidacionResultado> ValidarAsync(int encabezadoId, Dictionary<string, object> cambios)
    {
        var r = new ValidacionResultado { EsValido = true };

        string metodo = (cambios.GetValueOrDefault("metodoPago")?.ToString() ?? "").Trim().ToUpper();
        string forma = (cambios.GetValueOrDefault("formaPago")?.ToString() ?? "").Trim();

        // ── Reglas de catálogo SAT (c_MetodoPago ↔ c_FormaPago) ──
        if (metodo != "PUE" && metodo != "PPD")
            r.Errores.Add("El método de pago debe ser PUE o PPD.");

        if (metodo == "PPD" && forma != "99")
            r.Errores.Add("Para método PPD la forma de pago debe ser '99 - Por definir'.");

        if (metodo == "PUE" && (string.IsNullOrWhiteSpace(forma) || forma == "99"))
            r.Errores.Add("Para método PUE la forma de pago debe ser específica (no '99 - Por definir').");

        // Validaciones comunes de la cadena documental (complementos, anticipos, notas, sustituto).
        r.Errores.AddRange(AnalizadorDocumentos.Bloqueos(_utils, encabezadoId, TipoId));

        r.EsValido = r.Errores.Count == 0;
        return r;
    }

    public async Task<ResultadoConstruccionCfdi> ConstruirXmlNuevoAsync(int encabezadoId, Dictionary<string, object> cambios,
        string motivo, string tipoRelacion, string uuidRelacionado)
    {
        // Reconstruye la Factura original desde la BD/XML timbrado y sobreescribe solo
        // método y forma de pago con los valores capturados en el wizard.
        var factura = FacturaBuilder.DesdeEncabezado(_utils, encabezadoId);

        string nuevoMetodo = cambios.GetValueOrDefault("metodoPago")?.ToString();
        string nuevaForma = cambios.GetValueOrDefault("formaPago")?.ToString();

        if (!string.IsNullOrWhiteSpace(nuevoMetodo))
        {
            factura.metodoPagoTexto = nuevoMetodo; // → atributo MetodoPago del Comprobante (PUE/PPD)
            factura.MdpFactura = nuevoMetodo;       // → columna mdpfactura al persistir
        }
        if (!string.IsNullOrWhiteSpace(nuevaForma))
            factura.IdTipoPago = nuevaForma;        // → atributo FormaPago del Comprobante ('03','99',...)

        return new ResultadoConstruccionCfdi
        {
            Factura = factura,
            Xml = XmlCfdiBuilder.Construir(factura, tipoRelacion, uuidRelacionado, _utils)
        };
    }
}
