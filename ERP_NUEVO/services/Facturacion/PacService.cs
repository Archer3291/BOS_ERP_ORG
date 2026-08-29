// Services/Facturacion/PacService.cs
using BOS_ERP.Controllers;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.Extensions.Options;
using System.Text;

namespace BOS_ERP.Services.Facturacion
{
    public class PacService : IPacService
    {
        private readonly TimbradoOptions _timbrado;
        private readonly IWebHostEnvironment _env;

        public PacService(IOptions<TimbradoOptions> timbradoOptions, IWebHostEnvironment env)
        {
            _timbrado = timbradoOptions.Value;
            _env = env;
        }

        private string XmlTimbradosPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados");
        private string QrCodesPath => Path.Combine(_env.WebRootPath, "content", "qrcodes");

        public async Task<TimbradoPacResult> TimbrarAsync(string xmlSinTimbrar, string uuidTemporal)
        {
            try
            {
                string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(xmlSinTimbrar));

                using var client = new ServiceReference1.TimbradoServiceClient();
                client.ClientCredentials.UserName.UserName = _timbrado.User;
                client.ClientCredentials.UserName.Password = _timbrado.Password;
                client.Open();

                var esProduccion = Convert.ToBoolean(Utilities.GetSetting("perfil_factura"));
                var response = esProduccion
                    ? client.TimbrarSellarBase64(base64)
                    : client.TimbrarSellarBase64Test(base64);

                if (response.message != "Documento timbrado exitosamente")
                {
                    return new TimbradoPacResult { Success = false, Message = "Error al timbrar: " + response.message };
                }

                Directory.CreateDirectory(XmlTimbradosPath);
                Directory.CreateDirectory(QrCodesPath);

                string rutaXml = Path.Combine(XmlTimbradosPath, $"{response.uuid}.xml");
                await File.WriteAllTextAsync(rutaXml, response.xml);

                return new TimbradoPacResult
                {
                    Success = true,
                    Message = "Timbrado con éxito",
                    UUID = response.uuid,
                    XmlTimbrado = response.xml,
                    RutaXmlLocal = rutaXml
                };
            }
            catch (Exception ex)
            {
                return new TimbradoPacResult { Success = false, Message = "Error al conectar con el PAC: " + ex.Message };
            }
        }

        public async Task<CancelacionPacResult> CancelarAsync(string rfcEmisor, string uuid, string motivo, string folioSustitucion = "")
        {
            try
            {
                string timbradoUser = _timbrado.User;
                string timbradoPass = _timbrado.Password;

                using var client = new ServiceReference1.TimbradoServiceClient();
                client.ClientCredentials.UserName.UserName = timbradoUser;
                client.ClientCredentials.UserName.Password = timbradoPass;
                client.Open();

                var response = client.CancelarTest(rfcEmisor, uuid, motivo, folioSustitucion);

                if (response == null)
                    return new CancelacionPacResult { Success = false, Message = "No se recibió respuesta del servicio de cancelación." };

                string mensaje = response.message ?? "";
                bool ok = mensaje.ToLower().Contains("satisfactoriamente")
                       || mensaje.ToLower().Contains("ya se encuentra cancelado")
                       || mensaje.ToLower().Contains("previously cancelled");

                if (!ok)
                    return new CancelacionPacResult { Success = false, Message = "El PAC devolvió un error: " + mensaje };

                string carpeta = Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "Cancelaciones");
                Directory.CreateDirectory(carpeta);
                string rutaArchivo = Path.Combine(carpeta, $"{uuid}_AcuseCancelacion.xml");
                await File.WriteAllTextAsync(rutaArchivo, response.acuse ?? "");

                return new CancelacionPacResult
                {
                    Success = true,
                    Message = "Cancelación exitosa.",
                    AcuseXml = response.acuse,
                    RutaAcuse = rutaArchivo
                };
            }
            catch (Exception ex)
            {
                return new CancelacionPacResult { Success = false, Message = "No se pudo contactar al PAC: " + ex.Message };
            }
        }
    }
}