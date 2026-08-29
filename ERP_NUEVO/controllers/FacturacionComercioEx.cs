using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;
using System.Xml;
using System.Data; // Para usar DataTable
using System.Text;
using System.Globalization;
using System.Drawing;
using System.Drawing.Imaging;

namespace BOS_ERP.Controllers
{
    public class FacturacionComercioExController : Utilities
    {
        [HttpPost]
        public void GenerarXml(FacturaComercioEx factura)
        {
            factura.CalculaTotal(); // Asegura que los totales estén calculados

            string fileName = Guid.NewGuid().ToString();
            string filePath = Path.Combine($"~/Adjuntos/{fileName}.xml");

            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false)
            };

            try
            {
                using (XmlWriter writer = XmlWriter.Create(filePath, settings))
                {
                    writer.WriteStartDocument(true);
                    writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");
                    writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                    writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");
                    writer.WriteAttributeString("xmlns", "cce20", null, "http://www.sat.gob.mx/ComercioExterior20");

                    writer.WriteAttributeString("xsi", "schemaLocation", null,
                        "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd " +
                        "http://www.sat.gob.mx/ComercioExterior20 http://www.sat.gob.mx/sitio_internet/cfd/ComercioExterior20/ComercioExterior20.xsd");

                    writer.WriteAttributeString("Version", "4.0");
                    writer.WriteAttributeString("Serie", factura.Serie);
                    writer.WriteAttributeString("Folio", factura.Folio);
                    writer.WriteAttributeString("Fecha", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
                    writer.WriteAttributeString("FormaPago", factura.IdTipoPago);
                    writer.WriteAttributeString("SubTotal", factura.Subtotal.ToString("F2"));
                    writer.WriteAttributeString("Moneda", factura.Moneda);
                    writer.WriteAttributeString("TipoCambio",
    factura.TipoCambio == 1 ? "1" : factura.TipoCambio.ToString("F4"));

                    writer.WriteAttributeString("Total", factura.Total.ToString("F2"));
                    writer.WriteAttributeString("TipoDeComprobante", "I");
                    writer.WriteAttributeString("Exportacion", factura.Exportacion);
                    writer.WriteAttributeString("MetodoPago", factura.MdpFactura);
                    writer.WriteAttributeString("LugarExpedicion", factura.CpE);

                    // Emisor
                    writer.WriteStartElement("cfdi", "Emisor", null);
                    writer.WriteAttributeString("Rfc", factura.RfcEmisor);
                    writer.WriteAttributeString("Nombre", factura.RsoEmisor);
                    writer.WriteAttributeString("RegimenFiscal", factura.Rege);
                    writer.WriteEndElement();

                    // Receptor
                    writer.WriteStartElement("cfdi", "Receptor", null);
                    writer.WriteAttributeString("Rfc", factura.RfcCliente);
                    writer.WriteAttributeString("Nombre", factura.RsoCliente);
                    writer.WriteAttributeString("DomicilioFiscalReceptor", factura.CpR);
                    writer.WriteAttributeString("RegimenFiscalReceptor", factura.Regc);
                    writer.WriteAttributeString("UsoCFDI", factura.IdUsoCFDI);
                    writer.WriteEndElement();

                    // Conceptos
                    writer.WriteStartElement("cfdi", "Conceptos", null);
                    foreach (DataRow row in factura.Tproductos.Rows)
                    {
                        writer.WriteStartElement("cfdi", "Concepto", null);
                        writer.WriteAttributeString("ClaveProdServ", row["articulo"].ToString());
                        writer.WriteAttributeString("NoIdentificacion", row["articulo"].ToString());
                        writer.WriteAttributeString("Cantidad", Convert.ToDecimal(row["cantidad"]).ToString("F2"));
                        writer.WriteAttributeString("ClaveUnidad", row["ClaveUnidad"].ToString());
                        writer.WriteAttributeString("Unidad", row["Unidad"].ToString());
                        writer.WriteAttributeString("Descripcion", row["descripcion"].ToString());
                        writer.WriteAttributeString("ValorUnitario",(Convert.ToDecimal(row["preciouni"]) / Convert.ToDecimal(factura.TipoCambio)).ToString("F2"));

                        writer.WriteAttributeString("Importe", Convert.ToDecimal(row["importe"]).ToString("F2"));
                        writer.WriteAttributeString("ObjetoImp", "01");

                        // Impuestos
                        //writer.WriteStartElement("cfdi", "Impuestos", null);
                        //writer.WriteStartElement("cfdi", "Traslados", null);
                        //writer.WriteStartElement("cfdi", "Traslado", null);
                        //writer.WriteAttributeString("Base", Convert.ToDecimal(row["importe"]).ToString("F2"));
                        //writer.WriteAttributeString("Impuesto", "002");
                        //writer.WriteAttributeString("TipoFactor", "Tasa");
                        //writer.WriteAttributeString("TasaOCuota", "0.160000");
                        //writer.WriteAttributeString("Importe", Convert.ToDecimal(row["iva"]).ToString("F2"));
                        //writer.WriteEndElement(); // Traslado
                        //writer.WriteEndElement(); // Traslados
                        //writer.WriteEndElement(); // Impuestos

                        writer.WriteEndElement(); // Concepto
                    }
                    writer.WriteEndElement(); // Conceptos

                    // Totales de impuestos
                    //writer.WriteStartElement("cfdi", "Impuestos", null);
                    //writer.WriteAttributeString("TotalImpuestosTrasladados", factura.IVA.ToString("F2"));
                    //writer.WriteStartElement("cfdi", "Traslados", null);
                    //writer.WriteStartElement("cfdi", "Traslado", null);
                    //writer.WriteAttributeString("Base", factura.Subtotal.ToString("F2"));
                    //writer.WriteAttributeString("Impuesto", "002");
                    //writer.WriteAttributeString("TipoFactor", "Tasa");
                    //writer.WriteAttributeString("TasaOCuota", "0.160000");
                    //writer.WriteAttributeString("Importe", factura.IVA.ToString("F2"));
                    //writer.WriteEndElement(); // Traslado
                    //writer.WriteEndElement(); // Traslados
                    //writer.WriteEndElement(); // Impuestos

                    // Complemento Comercio Exterior
                    decimal tipoCambio = 18.4033m;
                    decimal totalUSD = 0m;
                    List<decimal> valoresDolares = new List<decimal>();

                    foreach (DataRow row in factura.Tproductos.Rows)
                    {
                        decimal precioUnitario = Convert.ToDecimal(row["preciouni"]);
                        decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                        decimal valorPesos = precioUnitario * cantidad;
                        decimal valorDolares = valorPesos / tipoCambio;

                        valoresDolares.Add(valorDolares);
                        totalUSD += valorDolares; // Acumula sin redondear aún
                    }


                    writer.WriteStartElement("cfdi", "Complemento", null);
                    writer.WriteStartElement("cce20", "ComercioExterior", "http://www.sat.gob.mx/ComercioExterior20");
                    writer.WriteAttributeString("Version", "2.0");
                    writer.WriteAttributeString("ClaveDePedimento", "A1");
                    writer.WriteAttributeString("CertificadoOrigen", "0");
                    writer.WriteAttributeString("Incoterm", "DAP");
                    writer.WriteAttributeString("Observaciones", "factura.Observaciones");
                    writer.WriteAttributeString("TipoCambioUSD", tipoCambio.ToString("F4", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("TotalUSD", Math.Round(totalUSD, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture));


                    // Nodo Emisor
                    writer.WriteStartElement("cce20", "Emisor", null);
                    writer.WriteStartElement("cce20", "Domicilio", null);
                    writer.WriteAttributeString("Calle", "AV. INSURGENTES SUR");
                    writer.WriteAttributeString("NumeroExterior", "123");
                    writer.WriteAttributeString("Colonia", "0244");
                    writer.WriteAttributeString("Municipio", "028");
                    writer.WriteAttributeString("Estado", "SLP");
                    writer.WriteAttributeString("Pais", "MEX");
                    writer.WriteAttributeString("CodigoPostal", "78395");
                    writer.WriteEndElement(); // Domicilio
                    writer.WriteEndElement(); // Emisor

                    // Nodo Receptor
                    writer.WriteStartElement("cce20", "Receptor", null);
                    writer.WriteStartElement("cce20", "Domicilio", null);
                    writer.WriteAttributeString("Calle", "456 OAK AVE");
                    writer.WriteAttributeString("Estado", "CA");
                    writer.WriteAttributeString("Pais", "USA");
                    writer.WriteAttributeString("CodigoPostal", "90210");
                    writer.WriteEndElement(); // Domicilio
                    writer.WriteEndElement(); // Receptor

                    //Nodo Destinatario
                    writer.WriteStartElement("cce20", "Destinatario", null);
                    writer.WriteAttributeString("NumRegIdTrib", "756985236");
                    writer.WriteAttributeString("Nombre", "EL COMERCIO USA INC");
                    writer.WriteStartElement("cce20", "Domicilio", null);
                    writer.WriteAttributeString("Calle", "123 MAIN ST");
                    writer.WriteAttributeString("Estado", "TX");
                    writer.WriteAttributeString("Pais", "USA");
                    writer.WriteAttributeString("CodigoPostal", "75001");
                    writer.WriteEndElement(); // Domicilio
                    writer.WriteEndElement(); // Destinatario

                    writer.WriteStartElement("cce20", "Mercancias", null);
                    for (int i = 0; i < factura.Tproductos.Rows.Count; i++)
                    {
                        DataRow row = factura.Tproductos.Rows[i];
                        decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                        decimal valorTotalUSD = valoresDolares[i];
                        decimal valorUnitarioUSD = valorTotalUSD / cantidad;

                        writer.WriteStartElement("cce20", "Mercancia", null);
                        writer.WriteAttributeString("NoIdentificacion", row["articulo"].ToString());
                        writer.WriteAttributeString("FraccionArancelaria", "84849099"+"99");
                        writer.WriteAttributeString("CantidadAduana", cantidad.ToString("F2", CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("ValorUnitarioAduana", valorUnitarioUSD.ToString("F2", CultureInfo.InvariantCulture));

                        // ¡ESTO debe ser el total por mercancía, NO el unitario!
                        writer.WriteAttributeString("ValorDolares", Math.Round(valorTotalUSD, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture));

                        writer.WriteAttributeString("UnidadAduana", "01");
                        writer.WriteEndElement(); // Mercancia
                    }


                    writer.WriteEndElement(); // Mercancias

                    writer.WriteEndElement(); // ComercioExterior
                    writer.WriteEndElement(); // Complemento
                    writer.WriteEndElement(); // Comprobante
                    writer.WriteEndDocument();
                }

                // Si llegaste aquí, el XML fue generado correctamente
                Timbrado(factura, fileName); // Puedes poner try-catch interno aquí si lo deseas
            }
            catch (Exception ex)
            {
                throw new Exception("Error al generar el archivo XML: " + ex.Message);
            }
        }


        public int GuardarFactura(FacturaComercioEx factura)
        {
            int id = 0;

            string strSQL = @"
INSERT INTO factura 
(serie, Folio, idTipoFactura, idCliente, RfcCliente, RsoCliente, emlCliente, idEmisor, RfcEmisor,
 RsoEmisor, idExpedicion, idUsuario, Fecha, fechaTimbrado, statusFactura, mdpFactura, xmlFactura,
 idLugarExp, idTipoPago, UUID, importe, Descuento, subtotal, iva, total, Saldo, idPedido,
 retISR, retIVA, moneda, Observaciones, IdVendedor, UsoCFDI, idUsoCFDI, cbb, Parcialidad,
 SelloSat, selloCFDI, CadenaOriginal, oc, tdc, Anticipo, reg_fisR, reg_fisE, cpR, cpE)
OUTPUT INSERTED.id
VALUES 
(@serie, @Folio, @idTipoFactura, @idCliente, @RfcCliente, @RsoCliente, @emlCliente, @idEmisor, @RfcEmisor,
 @RsoEmisor, @idExpedicion, @idUsuario, @Fecha, @fechaTimbrado, @statusFactura, @mdpFactura, @xmlFactura,
 @idLugarExp, @idTipoPago, @UUID, @importe, @Descuento, @subtotal, @iva, @total, @Saldo, @idPedido,
 @retISR, @retIVA, @moneda, @Observaciones, @IdVendedor, @UsoCFDI, @idUsoCFDI, @cbb, @Parcialidad,
 @SelloSat, @selloCFDI, @CadenaOriginal, @oc, @tdc, @Anticipo, @reg_fisR, @reg_fisE, @cpR, @cpE);";

            var parameters = new Dictionary<string, object>
            {
                ["serie"] = factura.Serie,
                ["Folio"] = factura.Folio,
                ["idTipoFactura"] = factura.IdTipoFactura,
                ["idCliente"] = factura.IdCliente,
                ["RfcCliente"] = factura.RfcCliente,
                ["RsoCliente"] = factura.RsoCliente,
                ["emlCliente"] = factura.EmlCliente,
                ["idEmisor"] = factura.IdEmisor,
                ["RfcEmisor"] = factura.RfcEmisor,
                ["RsoEmisor"] = factura.RsoEmisor,
                ["idExpedicion"] = factura.IdExpedicion,
                ["idUsuario"] = factura.IdUsuario,
                ["Fecha"] = factura.Fecha,
                ["fechaTimbrado"] = factura.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                ["statusFactura"] = factura.StatusFactura,
                ["mdpFactura"] = factura.MdpFactura,
                ["xmlFactura"] = factura.XmlFactura,
                ["idLugarExp"] = factura.IdLugarExp,
                ["idTipoPago"] = factura.IdTipoPago,
                ["UUID"] = factura.UUID,
                ["importe"] = factura.Importe,
                ["Descuento"] = factura.Descuento,
                ["subtotal"] = factura.Subtotal,
                ["iva"] = factura.IVA,
                ["total"] = factura.Total,
                ["Saldo"] = factura.Saldo,
                ["idPedido"] = factura.IdPedido,
                ["retISR"] = factura.RetISR,
                ["retIVA"] = factura.RetIVA,
                ["moneda"] = factura.Moneda,
                ["Observaciones"] = factura.Observaciones,
                ["IdVendedor"] = factura.IdVendedor,
                ["UsoCFDI"] = factura.UsoCFDI,
                ["idUsoCFDI"] = factura.IdUsoCFDI,
                ["cbb"] = factura.Cbb,
                ["Parcialidad"] = 0,
                ["SelloSat"] = factura.SelloSAT,
                ["selloCFDI"] = factura.SelloCFDI,
                ["CadenaOriginal"] = factura.CadenaOriginal,
                ["oc"] = factura.Oc,
                ["tdc"] = factura.Tdc,
                ["Anticipo"] = factura.Anticipo,
                ["reg_fisR"] = factura.RegFisR,
                ["reg_fisE"] = factura.RegFisE,
                ["cpR"] = factura.CpR,
                ["cpE"] = factura.CpE
            };

            var result = RunQuery(strSQL, parameters);
            if (result != null && result.Count > 0)
            {
                id = Convert.ToInt32(result[0]["id"]);
            }

            // Insertar productos
            foreach (DataRow row in factura.Tproductos.Rows)
            {
                string getIdSql = "SELECT ISNULL(MAX(idfactura), 0) + 1 AS nuevo_id FROM dfactura;";
                var resultado = RunQuery(getIdSql);

                int idfactura = 1; // Valor por defecto
                if (resultado.Count > 0 && resultado[0].ContainsKey("nuevo_id"))
                {
                    idfactura = Convert.ToInt32(resultado[0]["nuevo_id"]);
                }

                string sqlDetalle = @"
INSERT INTO dfactura 
(idfactura, idfac, idproducto, descripcion, cantidad, precio, cpr, descuento, saldo, udm, claveprodserv, claveprod, idndv, lote, pedimento, cant_ndc)
VALUES 
(@idfactura, @idfac, @idproducto, @descripcion, @cantidad, @precio, @cpr, @descuento, @saldo, @udm, @claveprodserv, @claveprod, @idndv, @lote, @pedimento, @cant_ndc);";

                var parametrosDetalle = new Dictionary<string, object>
                {
                    ["idfactura"] = idfactura,
                    ["idfac"] = id, // Este es tu id padre (de la factura principal)
                    ["idproducto"] = 1,
                    ["descripcion"] = row["descripcion"],
                    ["cantidad"] = row["cantidad"],
                    ["precio"] = row["precio"],
                    ["cpr"] = 0,
                    ["descuento"] = 0,
                    ["saldo"] = 0,
                    ["udm"] = row["ClaveUnidad"],
                    ["claveprodserv"] = row["articulo"],
                    ["claveprod"] = row["claveprod"],
                    ["idndv"] = 0,
                    ["lote"] = "-",
                    ["pedimento"] = "-",
                    ["cant_ndc"] = 0
                };


                RunQuery(sqlDetalle, parametrosDetalle);
            }

            return id;
        }
        private void Timbrado(FacturaComercioEx factura, string xmla)
        {
            try
            {
                string filePath = Path.Combine($"~/Adjuntos/{xmla}.xml");
                string base64String = ConvertFileToBase64(filePath);
//                var client2 = new ModulaService.InterfaceClient();
//                var login = new ModulaService.Login { User = "USER", Pass = "123" };
//                var items = new[] {
//    new ModulaService.Item { ART_ARTICOLO = "A001", ART_DES = "Artículo", ART_UMI = "PZ" }
//};

//                // Llamar método
//                var respuesta = client2.postItemMasterAsync(login, items).Result;

//                // Procesar resultado
//                foreach (var result in respuesta)
//                {
//                    Console.WriteLine($"{result.Status}: {result.Message}");
//                }
                using (var client = new ServiceReference1.TimbradoServiceClient())
                {
                    client.ClientCredentials.UserName.UserName = "SRS080522T77";
                    client.ClientCredentials.UserName.Password = "R0g1T1kSJ83tNEmC";

                    client.Open();
                    var response = Convert.ToBoolean(GetSetting("perfil_factura"))
    ? client.TimbrarSellarBase64(base64String)
    : client.TimbrarSellarBase64Test(base64String);

                    if (response.message != "Documento timbrado exitosamente")
                        throw new Exception("Error del servicio de timbrado: " + response.message);

                    factura.UUID = response.uuid;
                    factura.XmlFactura = response.xml;

                    string xmlContent = response.xml;
                    
                    string pdfPath = $"D:/Facturas/{response.uuid}.pdf";
                    string xmlPath = $"D:/Facturas/{response.uuid}.xml";

                    ConvertirBase64APdf(response.pdf, pdfPath);
                    System.IO.File.WriteAllText(xmlPath, xmlContent);

                    GuardarFactura(factura); // Asume que este método también maneja sus propias excepciones

                    var response1 = client.GetCfdiTest(base64String);
                    string base64Qr = response1.qr;

                    // Convertir base64 a arreglo de bytes
                    byte[] imageBytes = Convert.FromBase64String(base64Qr);

                    // Crear imagen desde los bytes
                    using (MemoryStream ms = new MemoryStream(imageBytes))
                    {
                        Image image = Image.FromStream(ms);

                        // Guardar como PNG
                        image.Save(@"D:\Facturas\qr.png", ImageFormat.Png);
                    }
                    var m = response1.message;
                    
                }
            }
            catch (Exception ex)
            {
                // Puedes registrar el error si tienes logging
                throw new Exception("Error al timbrar la factura: " + ex.Message, ex);
            }
        }


        public string ConvertFileToBase64(string filePath)
        {
            // Leer el archivo como un array de bytes
            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);

            // Convertir los bytes a Base64
            return Convert.ToBase64String(fileBytes);
        }

        // Método para convertir Base64 a un archivo PDF y guardarlo
        public void ConvertirBase64APdf(string base64String, string rutaArchivo)
        {
            // Convertir Base64 a un arreglo de bytes
            byte[] pdfBytes = Convert.FromBase64String(base64String);

            // Guardar los bytes en un archivo PDF
            System.IO.File.WriteAllBytes(rutaArchivo, pdfBytes);

            Console.WriteLine("PDF guardado en: " + rutaArchivo);
        }

    }
}