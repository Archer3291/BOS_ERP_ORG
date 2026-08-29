using BOS_ERP.Models;

namespace BOS_ERP.services.Facturacion
{
    public interface ITimbradoWorkflow
    {
        Task<TimbradoResult> EjecutarAsync(
            Factura factura,
            string rutaXmlSinTimbrar,
            string carpetaXmlTimbrados,
            string carpetaQr,
            string rutaQrPublica);
    }

    public sealed class TimbradoWorkflow : ITimbradoWorkflow
    {
        private readonly ITimbradoService _timbrado;
        private readonly IComprobanteFiscalService _comprobante;

        public TimbradoWorkflow(ITimbradoService timbrado, IComprobanteFiscalService comprobante)
        {
            _timbrado = timbrado;
            _comprobante = comprobante;
        }

        public async Task<TimbradoResult> EjecutarAsync(
            Factura factura,
            string rutaXmlSinTimbrar,
            string carpetaXmlTimbrados,
            string carpetaQr,
            string rutaQrPublica)
        {
            ArgumentNullException.ThrowIfNull(factura);
            var respuesta = await _timbrado.TimbrarAsync(rutaXmlSinTimbrar);
            if (!respuesta.Exitoso)
                return new TimbradoResult { Success = false, Message = "Error al timbrar: " + respuesta.Mensaje };
            if (string.IsNullOrWhiteSpace(respuesta.Uuid) || string.IsNullOrWhiteSpace(respuesta.XmlTimbrado))
                return new TimbradoResult { Success = false, Message = "El PAC report\u00f3 \u00e9xito, pero no devolvi\u00f3 UUID o XML timbrado." };

            factura.UUID = respuesta.Uuid;
            factura.XmlFactura = respuesta.XmlTimbrado;
            Directory.CreateDirectory(carpetaXmlTimbrados);

            string rutaFinal = Path.Combine(carpetaXmlTimbrados, $"{respuesta.Uuid}.xml");
            string rutaTemporal = rutaFinal + ".tmp";
            await System.IO.File.WriteAllTextAsync(rutaTemporal, respuesta.XmlTimbrado);
            System.IO.File.Move(rutaTemporal, rutaFinal, true);

            _comprobante.ComplementarDesdeXml(factura, rutaFinal);
            _comprobante.GenerarQr(factura, carpetaQr);
            factura.RutaQr = rutaQrPublica.TrimEnd('/') + $"/qr_{factura.UUID}.png";

            return new TimbradoResult
            {
                Success = true,
                Message = "Timbrado con \u00e9xito",
                UUID = factura.UUID
            };
        }
    }
}
