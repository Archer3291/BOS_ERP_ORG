// Services/Refacturacion/Handlers/DatosFiscalesTypeHandler.cs
using BOS_ERP.Controllers;
using BOS_ERP.Services.Refacturacion;

public class DatosFiscalesTypeHandler : IRefacturacionTypeHandler
{
    private readonly Utilities _utils;
    public string TipoId => "datos-fiscales";

    public DatosFiscalesTypeHandler(Utilities utils) => _utils = utils;

    public async Task<ValidacionResultado> ValidarAsync(int encabezadoId, Dictionary<string, object> cambios)
    {
        var r = new ValidacionResultado { EsValido = true };

        string rfc = cambios.GetValueOrDefault("rfc")?.ToString() ?? "";
        if (!ValidadorFiscal.EsRfcValido(rfc))
            r.Errores.Add("El RFC no tiene un formato válido.");

        // Validaciones comunes de la cadena documental (complementos, anticipos, notas, sustituto).
        r.Errores.AddRange(AnalizadorDocumentos.Bloqueos(_utils, encabezadoId, TipoId));

        r.EsValido = r.Errores.Count == 0;
        return r;
    }

    public async Task<ResultadoConstruccionCfdi> ConstruirXmlNuevoAsync(int encabezadoId, Dictionary<string, object> cambios,
        string motivo, string tipoRelacion, string uuidRelacionado)
    {
        // Reconstruye el Factura original desde la BD, aplica los cambios fiscales capturados
        // en el wizard (rfc, razonSocial, regimen, cp, usoCfdi) y arma el XML relacionándolo
        // al CFDI original vía <CfdiRelacionados TipoRelacion="04"><CfdiRelacionado UUID=.../>.
        var factura = FacturaBuilder.DesdeEncabezado(_utils, encabezadoId);
        factura.RfcCliente = cambios.GetValueOrDefault("rfc")?.ToString() ?? factura.RfcCliente;
        factura.RsoCliente = cambios.GetValueOrDefault("razonSocial")?.ToString() ?? factura.RsoCliente;
        factura.Regc = cambios.GetValueOrDefault("regimen")?.ToString() ?? factura.Regc;
        factura.CpR = cambios.GetValueOrDefault("cp")?.ToString() ?? factura.CpR;
        factura.IdUsoCFDI = cambios.GetValueOrDefault("usoCfdi")?.ToString() ?? factura.IdUsoCFDI;

        return new ResultadoConstruccionCfdi
        {
            Factura = factura,
            Xml = XmlCfdiBuilder.Construir(factura, tipoRelacion, uuidRelacionado, _utils)
        };
    }
}