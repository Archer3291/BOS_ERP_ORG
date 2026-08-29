//using Microsoft.AspNetCore.Mvc;
//using BOS_ERP.Models;
//using System.Xml;
//using System.Data;
//using System.Text;
//using System.Xml.Linq;

//namespace BOS_ERP.Controllers
//{
//    public class FacturacionController : Utilities
//    {
//        public class TimbradoResult
//        {
//            public bool Success { get; set; }
//            public string Message { get; set; }
//            public string UUID { get; set; }
//        }

//        // Acción para generar el XML
//        [HttpPost]
//        public TimbradoResult GenerarXml(Factura factura)
//        {
//            try
//            {
//                Guid myuuid = Guid.NewGuid();
//                string myuuidAsString = myuuid.ToString();
//                string filePath = Path.Combine($"~/App_Data/{myuuidAsString}.xml");

//                var settings = new XmlWriterSettings
//                {
//                    Indent = true,
//                    OmitXmlDeclaration = false,
//                    Encoding = new UTF8Encoding(false)
//                };

//                factura.CalculaTotal();

//                using (XmlWriter writer = XmlWriter.Create(filePath, settings))
//                {
//                    writer.WriteStartDocument(true);
//                    writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");

//                    writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
//                    writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
//                    writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");

//                    writer.WriteAttributeString("Version", "4.0");
//                    writer.WriteAttributeString("Serie", factura.Serie);
//                    writer.WriteAttributeString("Folio", factura.Folio);
//                    writer.WriteAttributeString("Fecha", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
//                    writer.WriteAttributeString("FormaPago", factura.IdTipoPago);
//                    writer.WriteAttributeString("SubTotal", factura.Subtotal.ToString("F2"));
//                    writer.WriteAttributeString("Moneda", factura.Moneda);
//                    writer.WriteAttributeString("TipoCambio", factura.TipoCambio.ToString());
//                    writer.WriteAttributeString("Total", factura.Total.ToString(("F2")));
//                    writer.WriteAttributeString("TipoDeComprobante", "I");
//                    writer.WriteAttributeString("Exportacion", factura.Exportacion);
//                    writer.WriteAttributeString("MetodoPago", factura.MdpFactura);
//                    writer.WriteAttributeString("LugarExpedicion", factura.CpE);

//                    writer.WriteStartAttribute("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance");
//                    writer.WriteString("http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd");
//                    writer.WriteEndAttribute();

//                    writer.WriteStartElement("cfdi", "Emisor", null);
//                    writer.WriteAttributeString("Rfc", factura.RfcEmisor);
//                    writer.WriteAttributeString("Nombre", factura.RsoEmisor);
//                    writer.WriteAttributeString("RegimenFiscal", factura.Rege);
//                    writer.WriteEndElement();

//                    writer.WriteStartElement("cfdi", "Receptor", null);
//                    writer.WriteAttributeString("Rfc", factura.RfcCliente);
//                    writer.WriteAttributeString("Nombre", factura.RsoCliente);
//                    writer.WriteAttributeString("DomicilioFiscalReceptor", factura.CpR);
//                    writer.WriteAttributeString("RegimenFiscalReceptor", factura.Regc);
//                    writer.WriteAttributeString("UsoCFDI", factura.IdUsoCFDI);
//                    writer.WriteEndElement();

//                    writer.WriteStartElement("cfdi", "Conceptos", null);
//                    foreach (DataRow row in factura.Tproductos.Rows)
//                    {
//                        writer.WriteStartElement("cfdi", "Concepto", null);
//                        writer.WriteAttributeString("ClaveProdServ", row["articulo"].ToString());
//                        writer.WriteAttributeString("NoIdentificacion", "1");
//                        writer.WriteAttributeString("Cantidad", row["Cantidad"].ToString());
//                        writer.WriteAttributeString("ClaveUnidad", row["ClaveUnidad"].ToString());
//                        writer.WriteAttributeString("Unidad", row["Unidad"].ToString());
//                        writer.WriteAttributeString("Descripcion", row["descripcion"].ToString());
//                        writer.WriteAttributeString("ValorUnitario", row["precio"].ToString());
//                        writer.WriteAttributeString("Importe", row["importe"].ToString());
//                        writer.WriteAttributeString("ObjetoImp", row["objeto"].ToString());

//                        writer.WriteStartElement("cfdi", "Impuestos", null);
//                        writer.WriteStartElement("cfdi", "Traslados", null);
//                        writer.WriteStartElement("cfdi", "Traslado", null);
//                        writer.WriteAttributeString("Base", row["importe"].ToString());
//                        writer.WriteAttributeString("Impuesto", "002");
//                        writer.WriteAttributeString("TipoFactor", "Tasa");
//                        writer.WriteAttributeString("TasaOCuota", "0.160000");
//                        writer.WriteAttributeString("Importe", row["iva"].ToString());
//                        writer.WriteEndElement(); // Traslado
//                        writer.WriteEndElement(); // Traslados
//                        writer.WriteEndElement(); // Impuestos

//                        writer.WriteEndElement(); // Concepto
//                    }
//                    writer.WriteEndElement(); // Conceptos

//                    writer.WriteStartElement("cfdi", "Impuestos", null);
//                    writer.WriteAttributeString("TotalImpuestosTrasladados", factura.IVA.ToString());
//                    writer.WriteStartElement("cfdi", "Traslados", null);
//                    writer.WriteStartElement("cfdi", "Traslado", null);
//                    writer.WriteAttributeString("Base", factura.Subtotal.ToString(("F2")));
//                    writer.WriteAttributeString("Impuesto", "002");
//                    writer.WriteAttributeString("TipoFactor", "Tasa");
//                    writer.WriteAttributeString("TasaOCuota", "0.160000");
//                    writer.WriteAttributeString("Importe", factura.IVA.ToString(("F2")));
//                    writer.WriteEndElement(); // Traslado
//                    writer.WriteEndElement(); // Traslados
//                    writer.WriteEndElement(); // Impuestos

//                    writer.WriteEndElement(); // Comprobante
//                    writer.WriteEndDocument();
//                }

//                return Timbrado(factura, myuuidAsString);
//            }
//            catch (Exception ex)
//            {
//                return new TimbradoResult
//                {
//                    Success = false,
//                    Message = "Error al generar el archivo XML: " + ex.Message
//                };
//            }
//        }
//        // Método ficticio para calcular el total (puedes adaptarlo a tus necesidades)


//        public int GuardarFactura(Factura factura)
//        {
//            int id = 0;

//            string strSQL = @"
//INSERT INTO factura 
//(serie, Folio, idTipoFactura, idCliente, RfcCliente, RsoCliente, emlCliente, idEmisor, RfcEmisor,
// RsoEmisor, idExpedicion, idUsuario, Fecha, fechaTimbrado, statusFactura, mdpFactura, xmlFactura,
// idLugarExp, idTipoPago, UUID, importe, Descuento, subtotal, iva, total, Saldo, idPedido,
// retISR, retIVA, moneda, Observaciones, IdVendedor, UsoCFDI, idUsoCFDI, cbb, Parcialidad,
// SelloSat, selloCFDI, CadenaOriginal, oc, tdc, Anticipo, reg_fisR, reg_fisE, cpR, cpE)
//OUTPUT INSERTED.id
//VALUES 
//(@serie, @Folio, @idTipoFactura, @idCliente, @RfcCliente, @RsoCliente, @emlCliente, @idEmisor, @RfcEmisor,
// @RsoEmisor, @idExpedicion, @idUsuario, @Fecha, @fechaTimbrado, @statusFactura, @mdpFactura, @xmlFactura,
// @idLugarExp, @idTipoPago, @UUID, @importe, @Descuento, @subtotal, @iva, @total, @Saldo, @idPedido,
// @retISR, @retIVA, @moneda, @Observaciones, @IdVendedor, @UsoCFDI, @idUsoCFDI, @cbb, @Parcialidad,
// @SelloSat, @selloCFDI, @CadenaOriginal, @oc, @tdc, @Anticipo, @reg_fisR, @reg_fisE, @cpR, @cpE);";

//            var parameters = new Dictionary<string, object>
//            {
//                ["serie"] = factura.Serie,
//                ["Folio"] = factura.Folio,
//                ["idTipoFactura"] = factura.IdTipoFactura,
//                ["idCliente"] = factura.IdCliente,
//                ["RfcCliente"] = factura.RfcCliente,
//                ["RsoCliente"] = factura.RsoCliente,
//                ["emlCliente"] = factura.EmlCliente,
//                ["idEmisor"] = factura.IdEmisor,
//                ["RfcEmisor"] = factura.RfcEmisor,
//                ["RsoEmisor"] = factura.RsoEmisor,
//                ["idExpedicion"] = factura.IdExpedicion,
//                ["idUsuario"] = factura.IdUsuario,
//                ["Fecha"] = factura.Fecha,
//                ["fechaTimbrado"] = factura.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
//                ["statusFactura"] = factura.StatusFactura,
//                ["mdpFactura"] = factura.MdpFactura,
//                ["xmlFactura"] = factura.XmlFactura,
//                ["idLugarExp"] = factura.IdLugarExp,
//                ["idTipoPago"] = factura.IdTipoPago,
//                ["UUID"] = factura.UUID,
//                ["importe"] = factura.Importe,
//                ["Descuento"] = factura.Descuento,
//                ["subtotal"] = factura.Subtotal,
//                ["iva"] = factura.IVA,
//                ["total"] = factura.Total,
//                ["Saldo"] = factura.Saldo,
//                ["idPedido"] = factura.IdPedido,
//                ["retISR"] = factura.RetISR,
//                ["retIVA"] = factura.RetIVA,
//                ["moneda"] = factura.Moneda,
//                ["Observaciones"] = factura.Observaciones,
//                ["IdVendedor"] = factura.IdVendedor,
//                ["UsoCFDI"] = factura.UsoCFDI,
//                ["idUsoCFDI"] = factura.IdUsoCFDI,
//                ["cbb"] = factura.Cbb,
//                ["Parcialidad"] = 0,
//                ["SelloSat"] = factura.SelloSAT,
//                ["selloCFDI"] = factura.SelloCFDI,
//                ["CadenaOriginal"] = factura.CadenaOriginal,
//                ["oc"] = factura.Oc,
//                ["tdc"] = factura.Tdc,
//                ["Anticipo"] = factura.Anticipo,
//                ["reg_fisR"] = factura.RegFisR,
//                ["reg_fisE"] = factura.RegFisE,
//                ["cpR"] = factura.CpR,
//                ["cpE"] = factura.CpE
//            };

//            var result = RunQuery(strSQL, parameters);
//            if (result != null && result.Count > 0)
//            {
//                id = Convert.ToInt32(result[0]["id"]);
//            }

//            // Insertar productos
//            foreach (DataRow row in factura.Tproductos.Rows)
//            {
//                string getIdSql = "SELECT ISNULL(MAX(idfactura), 0) + 1 AS nuevo_id FROM dfactura;";
//                var resultado = RunQuery(getIdSql);

//                int idfactura = 1; // Valor por defecto
//                if (resultado.Count > 0 && resultado[0].ContainsKey("nuevo_id"))
//                {
//                    idfactura = Convert.ToInt32(resultado[0]["nuevo_id"]);
//                }

//                string sqlDetalle = @"
//INSERT INTO dfactura 
//(idfactura, idfac, idproducto, descripcion, cantidad, precio, cpr, descuento, saldo, udm, claveprodserv, claveprod, idndv, lote, pedimento, cant_ndc)
//VALUES 
//(@idfactura, @idfac, @idproducto, @descripcion, @cantidad, @precio, @cpr, @descuento, @saldo, @udm, @claveprodserv, @claveprod, @idndv, @lote, @pedimento, @cant_ndc);";

//                var parametrosDetalle = new Dictionary<string, object>
//                {
//                    ["idfactura"] = idfactura,
//                    ["idfac"] = id, // Este es tu id padre (de la factura principal)
//                    ["idproducto"] = 1,
//                    ["descripcion"] = row["descripcion"],
//                    ["cantidad"] = row["cantidad"],
//                    ["precio"] = row["precio"],
//                    ["cpr"] = 0,
//                    ["descuento"] = 0,
//                    ["saldo"] = 0,
//                    ["udm"] = row["ClaveUnidad"],
//                    ["claveprodserv"] = row["articulo"],
//                    ["claveprod"] = row["claveprod"],
//                    ["idndv"] = 0,
//                    ["lote"] = "-",
//                    ["pedimento"] = "-",
//                    ["cant_ndc"] = 0
//                };
//                RunQuery(sqlDetalle, parametrosDetalle);
//            }

//            return id;
//        }

//        public class TimbreFiscalReader
//        {
//            public static void ComplementarFacturaDesdeXML(Factura factura, string xmlPath)
//            {
//                XDocument xdoc = XDocument.Load(xmlPath);
//                XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";
//                XNamespace tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

//                var comprobante = xdoc.Descendants(cfdi + "Comprobante").FirstOrDefault();
//                if (comprobante != null)
//                {
//                    factura.TipoDeComprobante = (string)comprobante.Attribute("TipoDeComprobante") ?? "";
//                    factura.Serie = (string)comprobante.Attribute("Serie") ?? "";
//                    factura.Folio = (string)comprobante.Attribute("Folio") ?? "";
//                    factura.LugarExpedicion = (string)comprobante.Attribute("LugarExpedicion") ?? "";
//                    factura.FormaPago = (string)comprobante.Attribute("FormaPago") ?? "";
//                    factura.NoCertificado = (string)comprobante.Attribute("NoCertificado") ?? "";
//                }

//                var timbre = xdoc.Descendants(tfd + "TimbreFiscalDigital").FirstOrDefault();
//                if (timbre != null)
//                {
//                    string version = (string)timbre.Attribute("Version") ?? "";
//                    string uuid = (string)timbre.Attribute("UUID") ?? "";
//                    string fechaTimbrado = (string)timbre.Attribute("FechaTimbrado") ?? "";
//                    string selloCFD = (string)timbre.Attribute("SelloCFD") ?? "";
//                    string noCertificadoSAT = (string)timbre.Attribute("NoCertificadoSAT") ?? "";

//                    factura.UUID = uuid;
//                    factura.FechaTimbrado = fechaTimbrado;
//                    factura.SelloSAT = (string)timbre.Attribute("SelloSAT") ?? "";
//                    factura.SelloCFDI = selloCFD;
//                    factura.NoCertificadoSAT = noCertificadoSAT;
//                    factura.TotalTexto = NumeroALetras(factura.Total);
//                    // Generar Cadena Original manualmente
//                    factura.CadenaOriginal = $"||{version}|{uuid}|{fechaTimbrado}|{selloCFD}|{noCertificadoSAT}||";
//                }
//            }
//        }
//        public static class QrGenerator
//        {
//            public static void GenerarQrFactura(Factura factura, string rutaArchivo)
//            {
//                // URL base SAT para CFDI 4.0
//                string url = $"https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx?id={factura.UUID}&re={factura.RfcEmisor}&rr={factura.RfcCliente}&tt={factura.Total:0.00}&fe={factura.SelloCFDI?.Substring(factura.SelloCFDI.Length - 8)}";

//                using (var qr = new QRCoder.QRCodeGenerator())
//                {
//                    var datos = qr.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.Q);
//                    var qrCode = new QRCoder.QRCode(datos);
//                    using (var bitmap = qrCode.GetGraphic(20))
//                    {
//                        bitmap.Save(rutaArchivo, System.Drawing.Imaging.ImageFormat.Png);
//                    }
//                }
//            }
//        }

//        public TimbradoResult Timbrado(Factura factura, string xmla)
//        {
//            try
//            {
//                var result = new TimbradoResult();
//                string filePath = Path.Combine($"~/App_Data/{xmla}.xml");
//                string base64String = ConvertFileToBase64(filePath);

//                using (var client = new ServiceReference1.TimbradoServiceClient())
//                {
//                    client.ClientCredentials.UserName.UserName = "SRS080522T77";
//                    client.ClientCredentials.UserName.Password = "R0g1T1kSJ83tNEmC";
//                    client.Open();

//                    var response = Convert.ToBoolean(GetSetting("perfil_factura"))
//    ? client.TimbrarSellarBase64(base64String)
//    : client.TimbrarSellarBase64Test(base64String);

//                    if (response.message == "Documento timbrado exitosamente")
//                    {
//                        factura.UUID = response.uuid;
//                        factura.XmlFactura = response.xml;

//                        string filePathXml = Path.Combine($"~/App_Data/{response.uuid}.xml");
//                        string filePathPdf = Path.Combine($"~/App_Data/{response.uuid}.pdf");

//                        // Guardar XML
//                        System.IO.File.WriteAllText(filePathXml, response.xml);

//                        // Convertir y guardar PDF
//                        ConvertirBase64APdf(response.pdf, filePathPdf);

//                        // Complementar y generar QR
//                        TimbreFiscalReader.ComplementarFacturaDesdeXML(factura, filePathXml);
//                        string rutaQrFisica = Path.Combine($"~/content/qrcodes/qr_{factura.UUID}.png");
//                        QrGenerator.GenerarQrFactura(factura, rutaQrFisica);
//                        factura.RutaQr = Url.Content($"~/content/qrcodes/qr_{factura.UUID}.png");

//                        GuardarFactura(factura);
//                        GenerarFactura(factura);

//                        result.Success = true;
//                        result.Message = "Timbrado con éxito";
//                        result.UUID = factura.UUID;
//                    }
//                    else
//                    {
//                        result.Success = false;
//                        result.Message = "Error al timbrar: " + response.message;
//                    }
//                }

//                return result;
//            }
//            catch (Exception ex)
//            {
//                return new TimbradoResult
//                {
//                    Success = false,
//                    Message = "Error al conectar con el servicio de timbrado: " + ex.Message
//                };
//            }
//        }

//        private string RenderViewToString(string viewName, object model)
//        {
//            var controllerContext = ControllerContext;
//            var viewEngineResult = ViewEngines.Engines.FindPartialView(controllerContext, viewName);

//            using (var sw = new StringWriter())
//            {
//                var viewContext = new ViewContext(controllerContext, viewEngineResult.View, new ViewDataDictionary(model), new TempDataDictionary(), sw);
//                viewEngineResult.View.Render(viewContext, sw);
//                return sw.ToString();
//            }
//        }

//        public IActionResult GenerarFactura(Factura factura)
//        {
//            if (factura == null || string.IsNullOrWhiteSpace(factura.UUID))
//            {
//                return Content("Factura no válida o UUID vacío.");
//            }

//            // 1. Renderizar el header dinámico como HTML
//            string headerHtml = RenderViewToString("Header", factura); // Vista: Views/Factura/Header.cshtml
//            string headerPath = Path.Combine("~/content/pdf/header.html");

//            // 2. Guardar el archivo HTML en disco
//            System.IO.File.WriteAllText(headerPath, headerHtml, Encoding.UTF8);

//            // 3. Configurar el PDF
//            var pdf = new Rotativa.ViewAsPdf("FacturaPdf", factura)
//            {
//                PageSize = Rotativa.Options.Size.A4,
//                PageMargins = new Rotativa.Options.Margins(45, 10, 20, 10), // Deja espacio para el header
//                FileName = $"Factura_{factura.UUID}.pdf",
//                CustomSwitches = $"--encoding utf-8 --header-html \"{headerPath}\" --header-spacing 5 --footer-center \"Página [page] de [toPage]\" --footer-line --footer-font-size 10"
//            };

//            // 4. Guardar el PDF en disco
//            string rutaPDF = $@"D:\Factura_{factura.UUID}.pdf";
//            Response.ContentEncoding = System.Text.Encoding.UTF8;
//            byte[] pdfBytes = pdf.BuildFile(ControllerContext);
//            System.IO.File.WriteAllBytes(rutaPDF, pdfBytes);

//            return Content($"Factura PDF generada correctamente en: {rutaPDF}");
//        }

//        public string ConvertFileToBase64(string filePath)
//        {
//            // Leer el archivo como un array de bytes
//            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);

//            // Convertir los bytes a Base64
//            return Convert.ToBase64String(fileBytes);
//        }

//        // Método para convertir Base64 a un archivo PDF y guardarlo
//        public void ConvertirBase64APdf(string base64String, string rutaArchivo)
//        {
//            // Convertir Base64 a un arreglo de bytes
//            byte[] pdfBytes = Convert.FromBase64String(base64String);

//            // Guardar los bytes en un archivo PDF
//            System.IO.File.WriteAllBytes(rutaArchivo, pdfBytes);

//            Console.WriteLine("PDF guardado en: " + rutaArchivo);
//        }


//        public static string NumeroALetras(decimal numero)
//        {
//            string[] unidades = { "", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve" };
//            string[] especiales = { "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve" };
//            string[] decenas = { "", "diez", "veinte", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa" };
//            string[] centenas = { "", "cien", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos" };

//            if (numero == 0)
//                return "cero";

//            string letras = "";
//            long num = Convert.ToInt64(Math.Truncate(numero)); // Parte entera

//            // Millones
//            if (num >= 1000000)
//            {
//                if (num >= 2000000)
//                {
//                    letras += NumeroALetras(Math.Truncate(num / 1000000m)) + " millones ";
//                }
//                else
//                {
//                    letras += "un millón ";
//                }
//                num %= 1000000;
//            }

//            // Miles
//            if (num >= 1000)
//            {
//                if (num >= 2000)
//                {
//                    letras += NumeroALetras(Math.Truncate(num / 1000m)) + " mil ";
//                }
//                else
//                {
//                    letras += "mil ";
//                }
//                num %= 1000;
//            }

//            // Centenas
//            if (num >= 100)
//            {
//                if (num == 100)
//                {
//                    letras += "cien ";
//                }
//                else
//                {
//                    letras += centenas[num / 100] + " ";
//                }
//                num %= 100;
//            }

//            // Decenas y unidades
//            if (num >= 20)
//            {
//                letras += decenas[num / 10];
//                if ((num % 10) > 0)
//                {
//                    letras += " y " + unidades[num % 10];
//                }
//            }
//            else if (num >= 10)
//            {
//                letras += especiales[num - 10];
//            }
//            else if (num > 0)
//            {
//                letras += unidades[num];
//            }

//            // Parte decimal
//            int decimalPart = Convert.ToInt32(Math.Round((numero - Math.Truncate(numero)) * 100));
//            if (decimalPart > 0)
//            {
//                letras += " con " + NumeroALetras(decimalPart) + " centavos";
//            }

//            return letras.Trim();
//        }

//    }
//}