using BOS_ERP.Controllers;
using BOS_ERP.Models.Options;
using Microsoft.Extensions.Options;

namespace BOS_ERP.services.Facturacion
{
    public interface ITimbradoService
    {
        Task<RespuestaTimbrado> TimbrarAsync(string rutaXmlSinTimbrar);
    }

    public sealed class RespuestaTimbrado
    {
        public bool Exitoso { get; init; }
        public string Mensaje { get; init; } = string.Empty;
        public string Uuid { get; init; } = string.Empty;
        public string XmlTimbrado { get; init; } = string.Empty;
    }

    public sealed class TimbradoService : Utilities, ITimbradoService
    {
        private readonly TimbradoOptions _opciones;

        public TimbradoService(IOptions<TimbradoOptions> opciones)
        {
            _opciones = opciones.Value;
        }

        public async Task<RespuestaTimbrado> TimbrarAsync(string rutaXmlSinTimbrar)
        {
            if (string.IsNullOrWhiteSpace(rutaXmlSinTimbrar))
                throw new ArgumentException("La ruta del XML es obligatoria.", nameof(rutaXmlSinTimbrar));
            if (!System.IO.File.Exists(rutaXmlSinTimbrar))
                throw new FileNotFoundException("No se encontr\u00f3 el XML que se enviar\u00e1 a timbrar.", rutaXmlSinTimbrar);

            string base64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(rutaXmlSinTimbrar));

            using var client = new ServiceReference1.TimbradoServiceClient();
            client.ClientCredentials.UserName.UserName = _opciones.User;
            client.ClientCredentials.UserName.Password = _opciones.Password;

            bool esProduccion = Convert.ToBoolean(GetSetting("perfil_factura"));
            var response = esProduccion
                ? await client.TimbrarSellarBase64Async(base64)
                : await client.TimbrarSellarBase64TestAsync(base64);

            string mensaje = response.message ?? string.Empty;
            return new RespuestaTimbrado
            {
                Exitoso = mensaje.Equals("Documento timbrado exitosamente", StringComparison.OrdinalIgnoreCase),
                Mensaje = mensaje,
                Uuid = response.uuid ?? string.Empty,
                XmlTimbrado = response.xml ?? string.Empty
            };
        }
    }
}
