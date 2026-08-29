using BOS_ERP.Models;
using System.Xml.Linq;

namespace BOS_ERP.services.Facturacion
{
    public interface IComprobanteFiscalService
    {
        void ComplementarDesdeXml(Factura factura, string xmlTimbradoPath);
        string GenerarQr(Factura factura, string carpetaQr);
    }

    public sealed class ComprobanteFiscalService : IComprobanteFiscalService
    {
        public void ComplementarDesdeXml(Factura factura, string xmlTimbradoPath)
        {
            ArgumentNullException.ThrowIfNull(factura);
            if (!File.Exists(xmlTimbradoPath))
                throw new FileNotFoundException("No se encontr\u00f3 el XML timbrado.", xmlTimbradoPath);

            XDocument xdoc = XDocument.Load(xmlTimbradoPath);
            XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";
            XNamespace tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

            var comprobante = xdoc.Descendants(cfdi + "Comprobante").FirstOrDefault()
                ?? throw new InvalidDataException("El XML no contiene un comprobante CFDI 4.0.");
            var timbre = xdoc.Descendants(tfd + "TimbreFiscalDigital").FirstOrDefault()
                ?? throw new InvalidDataException("El XML no contiene el Timbre Fiscal Digital.");

            factura.TipoDeComprobante = Atributo(comprobante, "TipoDeComprobante");
            factura.Serie = Atributo(comprobante, "Serie");
            factura.FolioCorto = Atributo(comprobante, "Folio");
            factura.LugarExpedicion = Atributo(comprobante, "LugarExpedicion");
            factura.FormaPago = Atributo(comprobante, "FormaPago");
            factura.NoCertificado = Atributo(comprobante, "NoCertificado");

            string version = Atributo(timbre, "Version");
            string selloCfd = Atributo(timbre, "SelloCFD");
            string certificadoSat = Atributo(timbre, "NoCertificadoSAT");
            factura.UUID = Atributo(timbre, "UUID");
            factura.FechaTimbrado = Atributo(timbre, "FechaTimbrado");
            factura.SelloSAT = Atributo(timbre, "SelloSAT");
            factura.SelloCFDI = selloCfd;
            factura.NoCertificadoSAT = certificadoSat;
            factura.TotalTexto = $"{decimal.Truncate(factura.Total):0} PESOS {decimal.Round(factura.Total % 1 * 100):00}/100 M.N.";
            factura.CadenaOriginal = $"||{version}|{factura.UUID}|{factura.FechaTimbrado}|{selloCfd}|{certificadoSat}||";
        }

        public string GenerarQr(Factura factura, string carpetaQr)
        {
            ArgumentNullException.ThrowIfNull(factura);
            if (string.IsNullOrWhiteSpace(factura.UUID))
                throw new InvalidOperationException("No se puede generar el QR sin UUID.");
            if (string.IsNullOrWhiteSpace(factura.SelloCFDI) || factura.SelloCFDI.Length < 8)
                throw new InvalidOperationException("El sello CFDI no contiene los ocho caracteres requeridos para el QR.");

            Directory.CreateDirectory(carpetaQr);
            string selloFinal = factura.SelloCFDI[^8..];
            string url = $"https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx?id={Uri.EscapeDataString(factura.UUID)}&re={Uri.EscapeDataString(factura.RfcEmisor ?? string.Empty)}&rr={Uri.EscapeDataString(factura.RfcCliente ?? string.Empty)}&tt={factura.Total:0.00}&fe={Uri.EscapeDataString(selloFinal)}";
            string rutaArchivo = Path.Combine(carpetaQr, $"qr_{factura.UUID}.png");

            using var qr = new QRCoder.QRCodeGenerator();
            using var datos = qr.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCoder.QRCode(datos);
            using var bitmap = qrCode.GetGraphic(20);
            bitmap.Save(rutaArchivo, System.Drawing.Imaging.ImageFormat.Png);
            return rutaArchivo;
        }

        public static string NumeroALetras(decimal numero)
        {
            string[] unidades = { "", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve" };
            string[] especiales = { "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve" };
            string[] decenas = { "", "diez", "veinte", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa" };
            string[] centenas = { "", "cien", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos" };

            if (numero == 0)
                return "cero";

            string letras = "";
            long num = Convert.ToInt64(Math.Truncate(numero));

            if (num >= 1000000)
            {
                if (num >= 2000000)
                {
                    letras += NumeroALetras(Math.Truncate(num / 1000000m)) + " millones ";
                }
                else
                {
                    letras += "un millón ";
                }
                num %= 1000000;
            }

            if (num >= 1000)
            {
                if (num >= 2000)
                {
                    letras += NumeroALetras(Math.Truncate(num / 1000m)) + " mil ";
                }
                else
                {
                    letras += "mil ";
                }
                num %= 1000;
            }

            if (num >= 100)
            {
                if (num == 100)
                {
                    letras += "cien ";
                }
                else
                {
                    letras += centenas[num / 100] + " ";
                }
                num %= 100;
            }

            if (num >= 20)
            {
                letras += decenas[num / 10];
                if ((num % 10) > 0)
                {
                    letras += " y " + unidades[num % 10];
                }
            }
            else if (num >= 10)
            {
                letras += especiales[num - 10];
            }
            else if (num > 0)
            {
                letras += unidades[num];
            }

            int decimalPart = Convert.ToInt32(Math.Round((numero - Math.Truncate(numero)) * 100));
            if (decimalPart > 0)
            {
                letras += " con " + NumeroALetras(decimalPart) + " centavos";
            }

            return letras.Trim();
        }

        private static string Atributo(XElement elemento, string nombre) =>
            (string?)elemento.Attribute(nombre) ?? string.Empty;
    }
}
