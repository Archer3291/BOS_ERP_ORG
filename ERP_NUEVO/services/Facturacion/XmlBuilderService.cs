using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System.Data;
using System.Globalization;
using System.Text;
using System.Xml;

namespace BOS_ERP.services.Facturacion
{
    public class XmlBuilderService : Utilities
    {
        // RFC genérico del público en general: obliga a emitir el CFDI como factura global.
        private const string RfcPublicoEnGeneral = "XAXX010101000";

        public XmlResult GenerateXmlAsync(Factura factura, string facturacionPath, List<PagoComplemento>? pagos = null)
        {
            if (factura == null)
                throw new ArgumentNullException(nameof(factura));

            return factura.TipoFacturacion.ToLowerInvariant() switch
            {
                "contado" or "credito" when string.Equals(factura.RfcCliente, "XEXX010101000", StringComparison.OrdinalIgnoreCase) => GenerarXmlInternacional(factura, facturacionPath),

                "contado" or "credito" => GenerarXmlContadoAsync(factura, facturacionPath),

                // Factura global (público en general): mismo comprobante de ingreso, con el
                // nodo InformacionGlobal y los datos de receptor que fija el SAT. Se reutiliza
                // el generador de contado en vez de duplicarlo: sólo cambian el nodo global y
                // cuatro atributos del receptor, todo lo demás (conceptos, descuentos, IVA,
                // totales) debe seguir calculándose exactamente igual.
                "global" => GenerarXmlContadoAsync(factura, facturacionPath, esGlobal: true),

                "anticipo" => GenerarXmlAnticipo(factura, facturacionPath),

                "nc_cancelacion" or "nc_descuento" or "nc_devolucion" or "aplicacion_anticipo" => GenerarNotaCredito(factura, facturacionPath),

                "arrendamiento" => GenerarXmlArrendamiento(factura, facturacionPath),

                "complemento" => GenerarXmlComplemento(factura, facturacionPath, pagos ?? throw new ArgumentNullException(nameof(pagos), "Los pagos son obligatorios para generar un complemento.")),

                _ => throw new ArgumentException($"Tipo de facturación no soportado: {factura.TipoFacturacion}", nameof(factura)
                )
            };
        }

        private XmlResult GenerarXmlContadoAsync(Factura factura, string FacturacionPath, bool esGlobal = false)
        {
            try
            {
                Guid myuuid = Guid.NewGuid();
                string myuuidAsString = myuuid.ToString();
                string folderPath = FacturacionPath;
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
                string filePath = Path.Combine(folderPath, $"{myuuidAsString}.xml");

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = false,
                    Encoding = new UTF8Encoding(false)
                };

                // ── 1. Calcular totales CORRECTOS
                decimal subtotalBruto = 0;
                decimal subtotalNeto = 0;
                decimal descuentoGlobalComprobante = 0;
                decimal ivaTotal = 0;

                var conceptosCalculados = new List<(
                    DataRow row,
                    decimal importeBruto,
                    decimal importeNeto,
                    decimal montoDescuento,
                    decimal ivaConcepto,
                    string objetoImp
                )>();

                foreach (DataRow row in factura.Tproductos.Rows)
                {
                    decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                    decimal precioUnit = Convert.ToDecimal(row["precioUnit"]);
                    decimal descPct = row.Table.Columns.Contains("descuento") ? Convert.ToDecimal(row["descuento"]) : 0m;

                    decimal importeBruto = Math.Round(cantidad * precioUnit, 2);
                    decimal montoDescuento = Math.Round(importeBruto * (descPct / 100m), 2);
                    decimal importeNeto = importeBruto - montoDescuento;
                    string objImp = row["objetoImp"]?.ToString() ?? "01";

                    decimal ivaConcepto = 0m;
                    if (objImp == "02")
                        ivaConcepto = Math.Round(importeNeto * 0.16m, 2);

                    subtotalBruto += importeBruto;
                    subtotalNeto += importeNeto;
                    descuentoGlobalComprobante += montoDescuento;
                    ivaTotal += ivaConcepto;

                    conceptosCalculados.Add((row, importeBruto, importeNeto, montoDescuento, ivaConcepto, objImp));
                }

                // Flete
                decimal subtotalFlete = Math.Round(factura.Flete, 2);
                decimal ivaFlete = 0m;
                if (factura.Flete > 0)
                {
                    ivaFlete = Math.Round(subtotalFlete * 0.16m, 2);
                    ivaTotal += ivaFlete;
                    subtotalNeto += subtotalFlete;
                    subtotalBruto += subtotalFlete;
                }

                // ── 2. Asignar al objeto factura
                factura.Subtotal = subtotalBruto;
                factura.IVA = ivaTotal;
                factura.Total = subtotalBruto - descuentoGlobalComprobante + ivaTotal;

                LogErrorHelper.RegistrarLog("Debug", "SIN_FOLIO",
                    $"DENTRO de GenerarXml → Subtotal={factura.Subtotal} IVA={factura.IVA} Total={factura.Total}",
                    nivel: "DEBUG");

                // ── 3. Generar XML
                using (XmlWriter writer = XmlWriter.Create(filePath, settings))
                {
                    writer.WriteStartDocument(true);
                    writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");

                    writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");
                    writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                    writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");

                    if (factura.Addenda?.DatosTemplate?.Count > 0)
                    {
                        writer.WriteAttributeString("xmlns", factura.Addenda.Prefix, null, factura.Addenda.Namespace);
                    }

                    writer.WriteAttributeString("Version", "4.0");
                    writer.WriteAttributeString("Serie", factura.Serie ?? "");
                    writer.WriteAttributeString("Folio", factura.FolioCorto ?? "");
                    writer.WriteAttributeString("Fecha", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
                    writer.WriteAttributeString("FormaPago", factura.IdTipoPago ?? "99");
                    writer.WriteAttributeString("SubTotal", factura.Subtotal.ToString("0.00"));
                    writer.WriteAttributeString("Moneda", factura.Moneda ?? "MXN");
                    writer.WriteAttributeString("TipoCambio", factura.TipoCambio.ToString());
                    writer.WriteAttributeString("Total", factura.Total.ToString("0.00"));
                    writer.WriteAttributeString("TipoDeComprobante", string.IsNullOrWhiteSpace(factura.TipoDeComprobante) ? "I" : factura.TipoDeComprobante);
                    writer.WriteAttributeString("Exportacion", factura.Exportacion ?? "01");
                    writer.WriteAttributeString("MetodoPago", factura.metodoPagoTexto ?? "PUE");
                    writer.WriteAttributeString("LugarExpedicion", factura.LugarExpedicion ?? "64000");
                    writer.WriteAttributeString("xsi", "schemaLocation", null, "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd");

                    if (descuentoGlobalComprobante > 0)
                        writer.WriteAttributeString("Descuento", descuentoGlobalComprobante.ToString("0.00"));

                    // ── Factura global (público en general) ────────────────────────────
                    // Llega por TipoFacturacion = "global", no se deduce del RFC: hay
                    // clientes reales (extranjeros, mostrador) dados de alta con
                    // XAXX010101000 que NO son ventas al público en general y deben
                    // facturarse de forma nominativa.
                    // El nodo va antes de CfdiRelacionados y Emisor, según el XSD.

                    // Una factura global sólo puede emitirse al RFC genérico; si no coincide
                    // es un error de configuración y el PAC la rechazaría sin explicación útil.
                    if (esGlobal && !string.Equals(factura.RfcCliente, RfcPublicoEnGeneral,
                                                   StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"La factura global requiere el RFC {RfcPublicoEnGeneral}, pero el receptor tiene '{factura.RfcCliente}'.");

                    if (esGlobal)
                    {
                        writer.WriteStartElement("cfdi", "InformacionGlobal", null);
                        writer.WriteAttributeString("Periodicidad",
                            string.IsNullOrWhiteSpace(factura.Periodicidad) ? "01" : factura.Periodicidad);
                        writer.WriteAttributeString("Meses", factura.Fecha.ToString("MM"));
                        writer.WriteAttributeString("Año", factura.Fecha.Year.ToString());
                        writer.WriteEndElement();
                    }

                    if (factura.TAnticipos?.Rows.Count > 0)
                    {
                        writer.WriteStartElement("cfdi", "CfdiRelacionados", null);
                        writer.WriteAttributeString("TipoRelacion", "07");
                        foreach (DataRow antRow in factura.TAnticipos.Rows)
                        {
                            writer.WriteStartElement("cfdi", "CfdiRelacionado", null);
                            writer.WriteAttributeString("UUID", antRow["uuid"].ToString());
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }

                    writer.WriteStartElement("cfdi", "Emisor", null);
                    writer.WriteAttributeString("Rfc", factura.RfcEmisor);
                    writer.WriteAttributeString("Nombre", factura.RsoEmisor);
                    writer.WriteAttributeString("RegimenFiscal", factura.Rege);
                    writer.WriteEndElement();

                    writer.WriteStartElement("cfdi", "Receptor", null);
                    writer.WriteAttributeString("Rfc", factura.RfcCliente);

                    if (esGlobal)
                    {
                        // El SAT fija estos valores para el público en general: el nombre
                        // debe ser literalmente PUBLICO EN GENERAL, el domicilio fiscal del
                        // receptor es el lugar de expedición del emisor, el régimen 616 y
                        // el uso S01. Tomarlos del catálogo de clientes hacía que el PAC
                        // rechazara el comprobante.
                        writer.WriteAttributeString("Nombre", "PUBLICO EN GENERAL");
                        writer.WriteAttributeString("DomicilioFiscalReceptor",
                            string.IsNullOrWhiteSpace(factura.LugarExpedicion) ? factura.CpR : factura.LugarExpedicion);
                        writer.WriteAttributeString("RegimenFiscalReceptor", "616");
                        writer.WriteAttributeString("UsoCFDI", "S01");
                    }
                    else
                    {
                        writer.WriteAttributeString("Nombre", factura.RsoCliente);
                        writer.WriteAttributeString(
                            "DomicilioFiscalReceptor",
                            factura.RfcCliente == "XAXX010101000"
                                ? factura.LugarExpedicion
                                : factura.CpR
                        );
                        writer.WriteAttributeString("RegimenFiscalReceptor", factura.Regc);
                        writer.WriteAttributeString("UsoCFDI", factura.IdUsoCFDI);
                    }

                    writer.WriteEndElement();

                    writer.WriteStartElement("cfdi", "Conceptos", null);

                    foreach (var (row, importeBruto, importeNeto, montoDescuento, ivaConcepto, objImp) in conceptosCalculados)
                    {
                        decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                        decimal precioUnit = Convert.ToDecimal(row["precioUnit"]);

                        string descripcionFinal = row["descripcion"].ToString();
                        if (row.Table.Columns.Contains("comentario") &&
                            !string.IsNullOrWhiteSpace(row["comentario"]?.ToString()))
                            descripcionFinal += $"\n{row["comentario"]}";

                        writer.WriteStartElement("cfdi", "Concepto", null);
                        writer.WriteAttributeString("ClaveProdServ", row["claveProdServ"].ToString());
                        writer.WriteAttributeString("NoIdentificacion", row["numero"].ToString());
                        writer.WriteAttributeString("Cantidad", cantidad.ToString("0.######"));
                        writer.WriteAttributeString("ClaveUnidad", row["claveUnidad"].ToString());
                        writer.WriteAttributeString("Unidad", row["unidad"]?.ToString() ?? "PZA");
                        writer.WriteAttributeString("Descripcion", descripcionFinal);
                        writer.WriteAttributeString("ValorUnitario", precioUnit.ToString("0.######"));
                        writer.WriteAttributeString("Importe", importeBruto.ToString("0.00"));

                        if (montoDescuento > 0)
                            writer.WriteAttributeString("Descuento", montoDescuento.ToString("0.00"));

                        writer.WriteAttributeString("ObjetoImp", objImp);

                        if (objImp == "02")
                        {
                            writer.WriteStartElement("cfdi", "Impuestos", null);
                            writer.WriteStartElement("cfdi", "Traslados", null);
                            writer.WriteStartElement("cfdi", "Traslado", null);
                            writer.WriteAttributeString("Base", importeNeto.ToString("0.00"));
                            writer.WriteAttributeString("Impuesto", "002");
                            writer.WriteAttributeString("TipoFactor", "Tasa");
                            writer.WriteAttributeString("TasaOCuota", "0.160000");
                            writer.WriteAttributeString("Importe", ivaConcepto.ToString("0.00"));
                            writer.WriteEndElement(); // Traslado
                            writer.WriteEndElement(); // Traslados
                            writer.WriteEndElement(); // Impuestos
                        }

                        writer.WriteEndElement(); // Concepto
                    }

                    if (factura.Flete > 0)
                    {
                        decimal ivaConceptoFlete = Math.Round(subtotalFlete * 0.16m, 2);

                        writer.WriteStartElement("cfdi", "Concepto", null);
                        writer.WriteAttributeString("ClaveProdServ", "78101800");
                        writer.WriteAttributeString("NoIdentificacion", "FLETE");
                        writer.WriteAttributeString("Cantidad", "1.00");
                        writer.WriteAttributeString("ClaveUnidad", "E48");
                        writer.WriteAttributeString("Unidad", "Servicio");
                        writer.WriteAttributeString("Descripcion", "Servicio de flete");
                        writer.WriteAttributeString("ValorUnitario", subtotalFlete.ToString("0.00"));
                        writer.WriteAttributeString("Importe", subtotalFlete.ToString("0.00"));
                        writer.WriteAttributeString("ObjetoImp", "02");

                        writer.WriteStartElement("cfdi", "Impuestos", null);
                        writer.WriteStartElement("cfdi", "Traslados", null);
                        writer.WriteStartElement("cfdi", "Traslado", null);
                        writer.WriteAttributeString("Base", subtotalFlete.ToString("0.00"));
                        writer.WriteAttributeString("Impuesto", "002");
                        writer.WriteAttributeString("TipoFactor", "Tasa");
                        writer.WriteAttributeString("TasaOCuota", "0.160000");
                        writer.WriteAttributeString("Importe", ivaConceptoFlete.ToString("0.00"));
                        writer.WriteEndElement(); // Traslado
                        writer.WriteEndElement(); // Traslados
                        writer.WriteEndElement(); // Impuestos

                        writer.WriteEndElement(); // Concepto
                    }

                    writer.WriteEndElement(); // Conceptos

                    if (factura.IVA > 0)
                    {
                        writer.WriteStartElement("cfdi", "Impuestos", null);
                        writer.WriteAttributeString("TotalImpuestosTrasladados", factura.IVA.ToString("0.00"));
                        writer.WriteStartElement("cfdi", "Traslados", null);
                        writer.WriteStartElement("cfdi", "Traslado", null);
                        writer.WriteAttributeString("Base", subtotalNeto.ToString("0.00"));
                        writer.WriteAttributeString("Impuesto", "002");
                        writer.WriteAttributeString("TipoFactor", "Tasa");
                        writer.WriteAttributeString("TasaOCuota", "0.160000");
                        writer.WriteAttributeString("Importe", factura.IVA.ToString("0.00"));
                        writer.WriteEndElement(); // Traslado
                        writer.WriteEndElement(); // Traslados
                        writer.WriteEndElement(); // Impuestos
                    }

                    if (factura.Addenda?.DatosTemplate?.Count > 0)
                        EscribirAddenda(writer, factura);

                    writer.WriteEndElement(); // Comprobante
                    writer.WriteEndDocument();
                }

                XmlResult xml = new XmlResult();
                xml.factura = factura;
                xml.uuid = myuuidAsString;

                return xml;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al generar el archivo XML: " + ex.Message);
            }
        }

        private XmlResult GenerarXmlAnticipo(Factura factura, string FacturacionPath)
        {
            try
            {
                Guid myuuid = Guid.NewGuid();
                string myuuidAsString = myuuid.ToString();
                string folderPath = FacturacionPath;

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string filePath = Path.Combine(folderPath, $"{myuuidAsString}.xml");

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = false,
                    Encoding = new UTF8Encoding(false)
                };

                decimal montoAnticipo = factura.MontoAnticipo;
                decimal total = factura.Total;
                decimal iva = total - montoAnticipo;

                factura.Subtotal = montoAnticipo;
                factura.IVA = iva;
                factura.Total = total;

                using (XmlWriter writer = XmlWriter.Create(filePath, settings))
                {
                    writer.WriteStartDocument(true);
                    writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");

                    writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");
                    writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                    writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");

                    writer.WriteAttributeString("Version", "4.0");
                    writer.WriteAttributeString("Serie", factura.Serie ?? "");
                    writer.WriteAttributeString("Folio", factura.Folio ?? "");
                    writer.WriteAttributeString("Fecha", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
                    writer.WriteAttributeString("FormaPago", factura.IdTipoPago ?? "03");
                    writer.WriteAttributeString("SubTotal", factura.Subtotal.ToString("0.00"));
                    writer.WriteAttributeString("Moneda", factura.Moneda ?? "MXN");
                    writer.WriteAttributeString("TipoCambio", factura.TipoCambio.ToString());
                    writer.WriteAttributeString("Total", factura.Total.ToString("0.00"));
                    writer.WriteAttributeString("TipoDeComprobante", "I");
                    writer.WriteAttributeString("Exportacion", factura.Exportacion ?? "01");
                    writer.WriteAttributeString("MetodoPago", factura.metodoPagoTexto ?? "PUE");
                    writer.WriteAttributeString("LugarExpedicion", factura.LugarExpedicion ?? "64000");

                    writer.WriteAttributeString("xsi", "schemaLocation", null,
                                            "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd");

                    writer.WriteStartElement("cfdi", "Emisor", null);
                    writer.WriteAttributeString("Rfc", factura.RfcEmisor);
                    writer.WriteAttributeString("Nombre", factura.RsoEmisor);
                    writer.WriteAttributeString("RegimenFiscal", factura.Rege);
                    writer.WriteEndElement();

                    writer.WriteStartElement("cfdi", "Receptor", null);
                    writer.WriteAttributeString("Rfc", factura.RfcCliente);
                    writer.WriteAttributeString("Nombre", factura.RsoCliente);
                    writer.WriteAttributeString("DomicilioFiscalReceptor", factura.CpR);
                    writer.WriteAttributeString("RegimenFiscalReceptor", factura.Regc);
                    writer.WriteAttributeString("UsoCFDI", "CP01");
                    writer.WriteEndElement();

                    writer.WriteStartElement("cfdi", "Conceptos", null);
                    writer.WriteStartElement("cfdi", "Concepto", null);
                    writer.WriteAttributeString("ClaveProdServ", "84111506");
                    writer.WriteAttributeString("Cantidad", "1");
                    writer.WriteAttributeString("ClaveUnidad", "ACT");
                    writer.WriteAttributeString("Descripcion", "ANTICIPO");
                    writer.WriteAttributeString("ValorUnitario", factura.Subtotal.ToString("F2"));
                    writer.WriteAttributeString("Importe", factura.Subtotal.ToString("F2"));
                    writer.WriteAttributeString("ObjetoImp", "02");

                    writer.WriteStartElement("cfdi", "Impuestos", null);
                    writer.WriteStartElement("cfdi", "Traslados", null);
                    writer.WriteStartElement("cfdi", "Traslado", null);
                    writer.WriteAttributeString("Base", factura.Subtotal.ToString("F2"));
                    writer.WriteAttributeString("Impuesto", "002");
                    writer.WriteAttributeString("TipoFactor", "Tasa");
                    writer.WriteAttributeString("TasaOCuota", "0.160000");
                    writer.WriteAttributeString("Importe", factura.IVA.ToString("F2"));
                    writer.WriteEndElement(); // Traslado
                    writer.WriteEndElement(); // Traslados
                    writer.WriteEndElement(); // Impuestos

                    writer.WriteEndElement(); // Concepto
                    writer.WriteEndElement(); // Conceptos

                    writer.WriteStartElement("cfdi", "Impuestos", null);
                    writer.WriteAttributeString("TotalImpuestosTrasladados", factura.IVA.ToString("0.00"));
                    writer.WriteStartElement("cfdi", "Traslados", null);
                    writer.WriteStartElement("cfdi", "Traslado", null);
                    writer.WriteAttributeString("Base", factura.Subtotal.ToString("F2"));
                    writer.WriteAttributeString("Impuesto", "002");
                    writer.WriteAttributeString("TipoFactor", "Tasa");
                    writer.WriteAttributeString("TasaOCuota", "0.160000");
                    writer.WriteAttributeString("Importe", factura.IVA.ToString("F2"));
                    writer.WriteEndElement(); // Traslado
                    writer.WriteEndElement(); // Traslados
                    writer.WriteEndElement(); // Impuestos

                    writer.WriteEndElement(); // Comprobante
                    writer.WriteEndDocument();
                }

                XmlResult xml = new XmlResult();
                xml.factura = factura;
                xml.uuid = myuuidAsString;

                return xml;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al generar el archivo XML de anticipo: " + ex.Message);
            }
        }

        private XmlResult GenerarNotaCredito(Factura nc, string FacturacionPath)
        {
            if (nc.Tproductos == null || nc.Tproductos.Rows.Count == 0)
                throw new Exception("No hay partidas para generar el XML.");

            // ══════════════════════════════════════════════════════
            //  PASO 0: CALCULAR TOTALES — misma lógica que Venta
            // ══════════════════════════════════════════════════════
            decimal subtotalBruto = 0m;
            decimal descuentoGlobal = 0m;
            decimal subtotalNeto = 0m;
            decimal ivaTotal = 0m;

            var conceptosCalculados = new List<(
                DataRow row,
                decimal importeBruto,
                decimal importeNeto,
                decimal montoDescuento,
                decimal ivaConcepto,
                string objImp
            )>();

            foreach (DataRow row in nc.Tproductos.Rows)
            {
                decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                decimal precioUnit = Convert.ToDecimal(row["precioUnit"]);
                decimal descPct = row.Table.Columns.Contains("descuento") ? Convert.ToDecimal(row["descuento"]) : 0m;
                string objImp = row["objetoImp"]?.ToString() ?? "02";

                decimal importeBruto = Math.Round(cantidad * precioUnit, 2);
                decimal montoDescuento = Math.Round(importeBruto * (descPct / 100m), 2);
                decimal importeNeto = importeBruto - montoDescuento;
                decimal ivaConcepto = objImp == "02" ? Math.Round(importeNeto * 0.16m, 2) : 0m;

                subtotalBruto += importeBruto;
                descuentoGlobal += montoDescuento;
                subtotalNeto += importeNeto;
                ivaTotal += ivaConcepto;

                conceptosCalculados.Add((row, importeBruto, importeNeto, montoDescuento, ivaConcepto, objImp));
            }

            decimal xmlTotal = subtotalBruto - descuentoGlobal + ivaTotal;

            // ══════════════════════════════════════════════════════
            //  PASO 1: GENERAR XML
            // ══════════════════════════════════════════════════════
            string tempId = Guid.NewGuid().ToString();
            string folder = FacturacionPath;
            Directory.CreateDirectory(folder);
            string filePath = Path.Combine(folder, $"{tempId}.xml");

            var xmlSettings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false)
            };

            using (XmlWriter w = XmlWriter.Create(filePath, xmlSettings))
            {
                w.WriteStartDocument(true);

                // ── cfdi:Comprobante ──────────────────────────────────
                w.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");

                w.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");
                w.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                w.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
                w.WriteAttributeString("xsi", "schemaLocation", null,
                    "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd");

                w.WriteAttributeString("Version", "4.0");
                w.WriteAttributeString("Serie", nc.Serie ?? "NC");
                w.WriteAttributeString("Folio", nc.FolioCorto ?? "");
                w.WriteAttributeString("Fecha", nc.Fecha.ToString("yyyy-MM-ddTHH:mm:ss"));
                w.WriteAttributeString("FormaPago", nc.IdTipoPago ?? "99");
                w.WriteAttributeString("SubTotal", subtotalBruto.ToString("0.00"));

                if (descuentoGlobal > 0)
                    w.WriteAttributeString("Descuento", descuentoGlobal.ToString("0.00"));

                w.WriteAttributeString("Moneda", nc.Moneda ?? "MXN");

                if (!string.Equals(nc.Moneda, "MXN", StringComparison.OrdinalIgnoreCase))
                    w.WriteAttributeString("TipoCambio", nc.TipoCambio.ToString("0.0000"));

                w.WriteAttributeString("Total", xmlTotal.ToString("0.00"));
                w.WriteAttributeString("TipoDeComprobante", "E");
                w.WriteAttributeString("Exportacion", nc.Exportacion ?? "01");
                w.WriteAttributeString("MetodoPago", nc.metodoPagoTexto ?? "PUE");
                w.WriteAttributeString("LugarExpedicion", nc.LugarExpedicion ?? nc.CpE ?? "64000");

                // ── CfdiRelacionados ──────────────────────────────────
                if (!string.IsNullOrWhiteSpace(nc.UUIDsRelacionados))
                {
                    var uuids = nc.UUIDsRelacionados.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (uuids.Length > 0)
                    {
                        w.WriteStartElement("cfdi", "CfdiRelacionados", null);
                        w.WriteAttributeString("TipoRelacion", nc.TipoRelacion ?? "01");
                        foreach (string uuidRel in uuids)
                        {
                            w.WriteStartElement("cfdi", "CfdiRelacionado", null);
                            w.WriteAttributeString("UUID", uuidRel.Trim());
                            w.WriteEndElement();
                        }
                        w.WriteEndElement(); // CfdiRelacionados
                    }
                }

                // ── Emisor ────────────────────────────────────────────
                w.WriteStartElement("cfdi", "Emisor", null);
                w.WriteAttributeString("Rfc", nc.RfcEmisor);
                w.WriteAttributeString("Nombre", nc.RsoEmisor);
                w.WriteAttributeString("RegimenFiscal", nc.Rege);
                w.WriteEndElement();

                // ── Receptor ──────────────────────────────────────────
                w.WriteStartElement("cfdi", "Receptor", null);
                w.WriteAttributeString("Rfc", nc.RfcCliente);
                w.WriteAttributeString("Nombre", nc.RsoCliente);
                w.WriteAttributeString("DomicilioFiscalReceptor", nc.CpR);
                w.WriteAttributeString("RegimenFiscalReceptor", nc.Regc);
                w.WriteAttributeString("UsoCFDI", nc.IdUsoCFDI);
                w.WriteEndElement();

                // ── Conceptos ─────────────────────────────────────────
                w.WriteStartElement("cfdi", "Conceptos", null);

                foreach (var (row, importeBruto, importeNeto, montoDescuento, ivaConcepto, objImp) in conceptosCalculados)
                {
                    decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                    decimal precioUnit = Convert.ToDecimal(row["precioUnit"]);

                    string descripcion = row["descripcion"]?.ToString() ?? "";
                    if (row.Table.Columns.Contains("comentario") &&
                        !string.IsNullOrWhiteSpace(row["comentario"]?.ToString()))
                        descripcion += $"\n{row["comentario"]}";

                    w.WriteStartElement("cfdi", "Concepto", null);
                    w.WriteAttributeString("ClaveProdServ", row["claveProdServ"]?.ToString() ?? "01010101");
                    w.WriteAttributeString("NoIdentificacion", row["descripcion"]?.ToString() ?? "");
                    w.WriteAttributeString("Cantidad", cantidad.ToString("0.######"));
                    w.WriteAttributeString("ClaveUnidad", row["claveUnidad"]?.ToString() ?? "H87");
                    w.WriteAttributeString("Unidad", row["unidad"]?.ToString() ?? "PZA");
                    w.WriteAttributeString("Descripcion", descripcion);
                    w.WriteAttributeString("ValorUnitario", precioUnit.ToString("0.######"));
                    w.WriteAttributeString("Importe", importeBruto.ToString("0.00"));

                    if (montoDescuento > 0)
                        w.WriteAttributeString("Descuento", montoDescuento.ToString("0.00"));

                    w.WriteAttributeString("ObjetoImp", objImp);

                    if (objImp == "02")
                    {
                        w.WriteStartElement("cfdi", "Impuestos", null);
                        w.WriteStartElement("cfdi", "Traslados", null);
                        w.WriteStartElement("cfdi", "Traslado", null);
                        w.WriteAttributeString("Base", importeNeto.ToString("0.00"));
                        w.WriteAttributeString("Impuesto", "002");
                        w.WriteAttributeString("TipoFactor", "Tasa");
                        w.WriteAttributeString("TasaOCuota", "0.160000");
                        w.WriteAttributeString("Importe", ivaConcepto.ToString("0.00"));
                        w.WriteEndElement(); // Traslado
                        w.WriteEndElement(); // Traslados
                        w.WriteEndElement(); // Impuestos
                    }

                    w.WriteEndElement(); // Concepto
                }

                w.WriteEndElement(); // Conceptos

                // ── Impuestos globales ────────────────────────────────
                if (ivaTotal > 0)
                {
                    w.WriteStartElement("cfdi", "Impuestos", null);
                    w.WriteAttributeString("TotalImpuestosTrasladados", ivaTotal.ToString("0.00"));
                    w.WriteStartElement("cfdi", "Traslados", null);
                    w.WriteStartElement("cfdi", "Traslado", null);
                    w.WriteAttributeString("Base", subtotalNeto.ToString("0.00"));
                    w.WriteAttributeString("Impuesto", "002");
                    w.WriteAttributeString("TipoFactor", "Tasa");
                    w.WriteAttributeString("TasaOCuota", "0.160000");
                    w.WriteAttributeString("Importe", ivaTotal.ToString("0.00"));
                    w.WriteEndElement(); // Traslado
                    w.WriteEndElement(); // Traslados
                    w.WriteEndElement(); // Impuestos
                }

                w.WriteEndElement(); // Comprobante
                w.WriteEndDocument();
            }

            XmlResult xml = new XmlResult();
            xml.factura = nc;
            xml.uuid = tempId;

            return xml;
        }

        private XmlResult GenerarXmlArrendamiento(Factura factura, string FacturacionPath)
        {
            Guid myuuid = Guid.NewGuid();
            string myuuidAsString = myuuid.ToString();
            string folderPath = FacturacionPath;

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string filePath = Path.Combine(folderPath, $"{myuuidAsString}.xml");

            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false)
            };

            // ======================
            // Calcular totales
            // ======================
            decimal subtotal = 0;
            decimal totalIva = 0;

            foreach (DataRow row in factura.Tproductos.Rows)
            {
                decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                decimal precioUnitario = Convert.ToDecimal(row["precioUnit"]);
                decimal importe = Math.Round(cantidad * precioUnitario, 2);
                decimal ivaConcepto = Math.Round(importe * 0.16m, 2);

                subtotal += importe;
                totalIva += ivaConcepto;
            }

            factura.Subtotal = subtotal;
            factura.IVA = totalIva;
            factura.Total = subtotal + totalIva;

            using (XmlWriter writer = XmlWriter.Create(filePath, settings))
            {
                writer.WriteStartDocument(true);
                writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");

                // Namespaces
                writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
                writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");

                // Atributos principales
                writer.WriteAttributeString("Version", "4.0");
                writer.WriteAttributeString("Serie", factura.Serie);
                writer.WriteAttributeString("Folio", factura.Folio);
                writer.WriteAttributeString("Fecha", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
                writer.WriteAttributeString("FormaPago", factura.IdTipoPago);
                writer.WriteAttributeString("SubTotal", factura.Subtotal.ToString("F2"));
                writer.WriteAttributeString("Moneda", factura.Moneda);
                writer.WriteAttributeString("TipoCambio", factura.TipoCambio.ToString());
                writer.WriteAttributeString("Total", factura.Total.ToString("F2"));
                writer.WriteAttributeString("TipoDeComprobante", factura.TipoDeComprobante ?? "I");
                writer.WriteAttributeString("Exportacion", factura.Exportacion ?? "01");
                writer.WriteAttributeString("MetodoPago", factura.metodoPagoTexto ?? "PUE");
                writer.WriteAttributeString("LugarExpedicion", factura.LugarExpedicion);

                writer.WriteStartAttribute("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance");
                writer.WriteString("http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd");
                writer.WriteEndAttribute();

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
                    double cantidad = Convert.ToDouble(row["cantidad"]);
                    double precioUnitario = Convert.ToDouble(row["precioUnit"]);
                    double importe = Math.Round(cantidad * precioUnitario, 2);
                    double ivaConcepto = Math.Round(importe * 0.16, 2);

                    writer.WriteStartElement("cfdi", "Concepto", null);
                    writer.WriteAttributeString("ClaveProdServ", row["claveProdServ"].ToString());
                    writer.WriteAttributeString("NoIdentificacion", row["numero"].ToString());
                    writer.WriteAttributeString("Cantidad", cantidad.ToString("F2"));
                    writer.WriteAttributeString("ClaveUnidad", row["claveUnidad"].ToString());
                    writer.WriteAttributeString("Unidad", "SERVICIO");
                    writer.WriteAttributeString("Descripcion", row["descripcion"].ToString());
                    writer.WriteAttributeString("ValorUnitario", precioUnitario.ToString("F2"));
                    writer.WriteAttributeString("Importe", importe.ToString("F2"));
                    writer.WriteAttributeString("ObjetoImp", row["objetoImp"].ToString());

                    // ✅ Impuestos por concepto (antes que CuentaPredial)
                    writer.WriteStartElement("cfdi", "Impuestos", null);
                    writer.WriteStartElement("cfdi", "Traslados", null);
                    writer.WriteStartElement("cfdi", "Traslado", null);
                    writer.WriteAttributeString("Base", importe.ToString("F2"));
                    writer.WriteAttributeString("Impuesto", "002"); // IVA
                    writer.WriteAttributeString("TipoFactor", "Tasa");
                    writer.WriteAttributeString("TasaOCuota", "0.160000"); // 16%
                    writer.WriteAttributeString("Importe", ivaConcepto.ToString("F2"));
                    writer.WriteEndElement(); // Traslado
                    writer.WriteEndElement(); // Traslados
                    writer.WriteEndElement(); // Impuestos

                    // ✅ Cuenta Predial después de Impuestos
                    if (!string.IsNullOrEmpty(row["cuentaPredial"].ToString()))
                    {
                        writer.WriteStartElement("cfdi", "CuentaPredial", null);
                        writer.WriteAttributeString("Numero", row["cuentaPredial"].ToString());
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement(); // Concepto
                }
                writer.WriteEndElement(); // Conceptos

                // Impuestos globales
                writer.WriteStartElement("cfdi", "Impuestos", null);
                writer.WriteAttributeString("TotalImpuestosTrasladados", factura.IVA.ToString("F2"));
                writer.WriteStartElement("cfdi", "Traslados", null);
                writer.WriteStartElement("cfdi", "Traslado", null);
                writer.WriteAttributeString("Base", factura.Subtotal.ToString("F2"));
                writer.WriteAttributeString("Impuesto", "002");
                writer.WriteAttributeString("TipoFactor", "Tasa");
                writer.WriteAttributeString("TasaOCuota", "0.160000");
                writer.WriteAttributeString("Importe", factura.IVA.ToString("F2"));
                writer.WriteEndElement(); // Traslado
                writer.WriteEndElement(); // Traslados
                writer.WriteEndElement(); // Impuestos

                writer.WriteEndElement(); // Comprobante
                writer.WriteEndDocument();
            }

            XmlResult xml = new XmlResult();
            xml.uuid = myuuidAsString;
            xml.factura = factura;

            return xml;
        }

        private XmlResult GenerarXmlComplemento(Factura factura, string facturacionPath, List<PagoComplemento> pagos)
        {
            if (pagos == null || pagos.Count == 0)
                throw new Exception("No hay pagos para generar el XML de complemento.");

            Guid uuid = Guid.NewGuid();
            string folderPath = facturacionPath;

            LogErrorHelper.RegistrarLog(
                "Generar XML complemento pago",
                "SIN_FOLIO",
                $"Folder path XML: {folderPath}",
                nivel: "DEBUG"
            );

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string filePath = Path.Combine(folderPath, $"{uuid}.xml");
            const string NS_PAGOS20 = "http://www.sat.gob.mx/Pagos20";

            LogErrorHelper.RegistrarLog(
                "Generar XML complemento pago",
                "SIN_FOLIO",
                $"file path XML: {filePath}",
                nivel: "DEBUG"
            );

            LogErrorHelper.RegistrarLog(
                "Generar XML complemento pago",
                "SIN_FOLIO",
                $"Serie XML: {factura.Serie ?? "No se encontro serie"}",
                nivel: "DEBUG"
            );

            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false)
            };

            using (XmlWriter writer = XmlWriter.Create(filePath, settings))
            {
                writer.WriteStartDocument(true);

                // =====================================
                //   CFDI 4.0 - Comprobante
                // =====================================
                writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");
                writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");
                writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                writer.WriteAttributeString("xmlns", "pago20", null, NS_PAGOS20);

                writer.WriteAttributeString("xsi", "schemaLocation", null,
                    "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd " +
                    "http://www.sat.gob.mx/Pagos20 http://www.sat.gob.mx/sitio_internet/cfd/Pagos/Pagos20.xsd");

                writer.WriteAttributeString("Version", "4.0");
                writer.WriteAttributeString("Serie", factura.Serie ?? "");
                writer.WriteAttributeString("Folio", factura.FolioCorto ?? "");
                writer.WriteAttributeString("Fecha", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
                writer.WriteAttributeString("Moneda", "XXX");
                writer.WriteAttributeString("SubTotal", "0");
                writer.WriteAttributeString("Total", "0");
                writer.WriteAttributeString("TipoDeComprobante", "P");
                writer.WriteAttributeString("Exportacion", "01");
                writer.WriteAttributeString("LugarExpedicion", factura.LugarExpedicion ?? "64000");

                // =====================================
                //   EMISOR
                // =====================================
                writer.WriteStartElement("cfdi", "Emisor", null);
                writer.WriteAttributeString("Rfc", factura.RfcEmisor);
                writer.WriteAttributeString("Nombre", factura.RsoEmisor);
                writer.WriteAttributeString("RegimenFiscal", factura.Rege);
                writer.WriteEndElement();

                // =====================================
                //   RECEPTOR
                // =====================================
                writer.WriteStartElement("cfdi", "Receptor", null);
                writer.WriteAttributeString("Rfc", factura.RfcCliente);
                writer.WriteAttributeString("Nombre", factura.RsoCliente);
                writer.WriteAttributeString("DomicilioFiscalReceptor", factura.CpR);
                writer.WriteAttributeString("RegimenFiscalReceptor", factura.Regc);
                writer.WriteAttributeString("UsoCFDI", factura.IdUsoCFDI ?? "CP01");
                writer.WriteEndElement();

                // =====================================
                //   CONCEPTO ÚNICO (Pago)
                // =====================================
                writer.WriteStartElement("cfdi", "Conceptos", null);
                writer.WriteStartElement("cfdi", "Concepto", null);
                writer.WriteAttributeString("ClaveProdServ", "84111506");
                writer.WriteAttributeString("Cantidad", "1");
                writer.WriteAttributeString("ClaveUnidad", "ACT");
                writer.WriteAttributeString("Descripcion", "Pago");
                writer.WriteAttributeString("ValorUnitario", "0");
                writer.WriteAttributeString("Importe", "0");
                writer.WriteAttributeString("ObjetoImp", "01");
                writer.WriteEndElement();
                writer.WriteEndElement();

                // =====================================
                //      COMPLEMENTO PAGOS 2.0
                // =====================================
                writer.WriteStartElement("cfdi", "Complemento", null);
                writer.WriteStartElement("pago20", "Pagos", NS_PAGOS20);
                writer.WriteAttributeString("Version", "2.0");

                // TOTALES
                decimal totalBaseIVA16 = 0m;
                decimal totalIVA16 = 0m;
                decimal totalMontoTotalPagos = 0m;

                foreach (var p in pagos ?? new List<PagoComplemento>())
                {
                    totalMontoTotalPagos += Math.Round(p.Monto, 2);

                    if (p.Documentos != null)
                    {
                        foreach (var d in p.Documentos)
                        {
                            decimal impPagado = Math.Abs(Math.Round(d.ImpPagado, 2));
                            decimal baseDR = Math.Round(impPagado / 1.16m, 2);
                            decimal ivaDR = Math.Round(baseDR * 0.16m, 2);

                            totalBaseIVA16 += baseDR;
                            totalIVA16 += ivaDR;
                        }
                    }
                }

                writer.WriteStartElement("pago20", "Totales", NS_PAGOS20);
                writer.WriteAttributeString("TotalTrasladosBaseIVA16", totalBaseIVA16.ToString("0.00"));
                writer.WriteAttributeString("TotalTrasladosImpuestoIVA16", totalIVA16.ToString("0.00"));
                writer.WriteAttributeString("MontoTotalPagos", totalMontoTotalPagos.ToString("0.00"));
                writer.WriteEndElement();

                // =====================================
                //     LISTA DE PAGOS
                // =====================================
                foreach (var pago in pagos)
                {
                    decimal montoPago = Math.Round(pago.Monto, 2);
                    decimal tipoCambioPago = Math.Round(pago.TipoCambio, 6);

                    string formaDePagoP = "99";
                    if (!string.IsNullOrWhiteSpace(pago.FormaPago))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(pago.FormaPago.Trim(), @"^\d{2}");
                        formaDePagoP = m.Success ? m.Value : pago.FormaPago.Trim();
                    }

                    writer.WriteStartElement("pago20", "Pago", NS_PAGOS20);
                    writer.WriteAttributeString("FechaPago", pago.FechaPago.ToString("yyyy-MM-ddTHH:mm:ss"));
                    writer.WriteAttributeString("FormaDePagoP", formaDePagoP);
                    writer.WriteAttributeString("MonedaP", pago.Moneda ?? "MXN");
                    writer.WriteAttributeString("Monto", montoPago.ToString("0.00"));

                    string monedaP = (pago.Moneda ?? "MXN").Trim().ToUpper();

                    if (monedaP == "MXN")
                        writer.WriteAttributeString("TipoCambioP", "1");
                    else
                    {
                        tipoCambioPago = Math.Round(pago.TipoCambio, 6);
                        if (tipoCambioPago <= 0) tipoCambioPago = 1;
                        writer.WriteAttributeString("TipoCambioP", tipoCambioPago.ToString("0.000000"));
                    }

                    // ── Acumuladores para ImpuestosP ──────────────────────────────
                    decimal baseTotalP = 0m;
                    decimal ivaTotalP = 0m;

                    // =====================================
                    //  DOCTOS RELACIONADOS
                    // =====================================
                    if (pago.Documentos != null)
                    {
                        foreach (var doc in pago.Documentos)
                        {
                            decimal impSaldoAnt = Math.Abs(Math.Round(doc.ImpSaldoAnt, 2));
                            decimal impPagado = Math.Abs(Math.Round(doc.ImpPagado, 2));
                            decimal impSaldoInsoluto = Math.Round(impSaldoAnt - impPagado, 2);
                            if (impSaldoInsoluto < 0) impSaldoInsoluto = 0;

                            // ── Base e IVA de ESTE documento ──────────────────────
                            // impPagado ya incluye IVA → base = impPagado / 1.16
                            decimal baseDR = Math.Round(impPagado / 1.16m, 2);
                            decimal ivaDR = Math.Round(baseDR * 0.16m, 2);

                            // Acumular para ImpuestosP
                            baseTotalP += baseDR;
                            ivaTotalP += ivaDR;

                            writer.WriteStartElement("pago20", "DoctoRelacionado", NS_PAGOS20);

                            if (!string.IsNullOrWhiteSpace(doc.IdDocumento))
                                writer.WriteAttributeString("IdDocumento", doc.IdDocumento);

                            writer.WriteAttributeString("Serie", doc.Serie ?? "");
                            writer.WriteAttributeString("Folio", doc.Folio ?? "");
                            writer.WriteAttributeString("MonedaDR", doc.MonedaDR ?? pago.Moneda ?? "MXN");
                            writer.WriteAttributeString("NumParcialidad", "1");

                            string monedaDR = (doc.MonedaDR ?? pago.Moneda ?? "MXN").Trim().ToUpper();
                            if (monedaDR.Equals(monedaP, StringComparison.OrdinalIgnoreCase))
                                writer.WriteAttributeString("EquivalenciaDR", "1");
                            else
                            {
                                decimal equiv = tipoCambioPago > 0 ? tipoCambioPago : 1;
                                writer.WriteAttributeString("EquivalenciaDR", equiv.ToString("0.000000"));
                            }

                            writer.WriteAttributeString("ImpSaldoAnt", impSaldoAnt.ToString("0.00"));
                            writer.WriteAttributeString("ImpPagado", impPagado.ToString("0.00"));
                            writer.WriteAttributeString("ImpSaldoInsoluto", impSaldoInsoluto.ToString("0.00"));
                            writer.WriteAttributeString("ObjetoImpDR", "02");

                            // ImpuestosDR — traslado de ESTE documento
                            writer.WriteStartElement("pago20", "ImpuestosDR", NS_PAGOS20);
                            writer.WriteStartElement("pago20", "TrasladosDR", NS_PAGOS20);
                            writer.WriteStartElement("pago20", "TrasladoDR", NS_PAGOS20);
                            writer.WriteAttributeString("BaseDR", baseDR.ToString("0.00"));
                            writer.WriteAttributeString("ImpuestoDR", "002");
                            writer.WriteAttributeString("TipoFactorDR", "Tasa");
                            writer.WriteAttributeString("TasaOCuotaDR", "0.160000");
                            writer.WriteAttributeString("ImporteDR", ivaDR.ToString("0.00"));
                            writer.WriteEndElement(); // TrasladoDR
                            writer.WriteEndElement(); // TrasladosDR
                            writer.WriteEndElement(); // ImpuestosDR

                            writer.WriteEndElement(); // DoctoRelacionado
                        }
                    }

                    // =====================================
                    //  ImpuestosP — resumen del pago
                    //  Debe coincidir exactamente con la
                    //  suma de todos los TrasladoDR
                    // =====================================
                    writer.WriteStartElement("pago20", "ImpuestosP", NS_PAGOS20);
                    writer.WriteStartElement("pago20", "TrasladosP", NS_PAGOS20);
                    writer.WriteStartElement("pago20", "TrasladoP", NS_PAGOS20);
                    writer.WriteAttributeString("BaseP", baseTotalP.ToString("0.00"));
                    writer.WriteAttributeString("ImpuestoP", "002");
                    writer.WriteAttributeString("TipoFactorP", "Tasa");
                    writer.WriteAttributeString("TasaOCuotaP", "0.160000");
                    writer.WriteAttributeString("ImporteP", ivaTotalP.ToString("0.00"));
                    writer.WriteEndElement(); // TrasladoP
                    writer.WriteEndElement(); // TrasladosP
                    writer.WriteEndElement(); // ImpuestosP

                    writer.WriteEndElement(); // Pago
                }

                writer.WriteEndElement(); // Pagos
                writer.WriteEndElement(); // Complemento
                writer.WriteEndElement(); // Comprobante

                writer.WriteEndDocument();
            }

            return new XmlResult
            {
                uuid = uuid.ToString(),
                factura = factura
            };
        }

        private XmlResult GenerarXmlInternacional(Factura factura, string facturacionPath)
        {
            Guid myuuid = Guid.NewGuid();
            string myuuidAsString = myuuid.ToString();
            string folderPath = facturacionPath;

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string filePath = Path.Combine(folderPath, $"{myuuidAsString}.xml");

            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false)
            };

            System.Diagnostics.Debug.WriteLine($"TipoCambio Comprobante: {factura.TipoCambio}");
            System.Diagnostics.Debug.WriteLine($"TipoCambioUSD CE: {factura.ComercioExterior?.TipoCambioUSD}");
            // === CALCULAR TOTALES ===
            decimal subtotalProductosBruto = 0;
            decimal totalDescuentos = 0;
            var importesBasePorProducto = new Dictionary<string, decimal>();
            var importesBrutoPorProducto = new Dictionary<string, decimal>();

            var conceptosCalculados = new List<(
                DataRow row,
                decimal importeBruto,
                decimal montoDescuento,
                decimal importeNeto
            )>();

            foreach (DataRow row in factura.Tproductos.Rows)
            {
                decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                decimal precioUnitario = Convert.ToDecimal(row["precioUnit"]);
                decimal importeBruto = Math.Round(cantidad * precioUnitario, 6); // ← más precisión antes de redondear

                decimal descPct = row.Table.Columns.Contains("descuento")
                                        ? Convert.ToDecimal(row["descuento"]) : 0m;
                decimal montoDesc = Math.Round(importeBruto * (descPct / 100m), 2); // ← redondeo FINAL a 2 decimales

                importeBruto = Math.Round(importeBruto, 2); // ← ahora sí redondear el bruto
                decimal importeNeto = importeBruto - montoDesc;

                subtotalProductosBruto += importeBruto;
                totalDescuentos += montoDesc; // ← suma de valores YA redondeados a 2 dec

                string noIdentificacion = row["numero"]?.ToString() ?? "";

                if (!importesBasePorProducto.ContainsKey(noIdentificacion))
                    importesBasePorProducto[noIdentificacion] = 0;
                importesBasePorProducto[noIdentificacion] += importeNeto;

                if (!importesBrutoPorProducto.ContainsKey(noIdentificacion))
                    importesBrutoPorProducto[noIdentificacion] = 0;
                importesBrutoPorProducto[noIdentificacion] += importeBruto;

                conceptosCalculados.Add((row, importeBruto, montoDesc, importeNeto));
            }

            decimal subtotalFlete = Math.Round(factura.Flete, 2);

            factura.Subtotal = subtotalProductosBruto + subtotalFlete;
            // ✅ Descuento del comprobante = suma exacta de los Descuento de cada concepto
            factura.Descuento = conceptosCalculados.Sum(c => c.montoDescuento);
            factura.Total = factura.Subtotal - factura.Descuento;

            decimal baseTotalImpuestos = (subtotalProductosBruto - factura.Descuento) + subtotalFlete;

            TimeZoneInfo tzFrontera = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time (Mexico)");
            DateTime fechaFrontera = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzFrontera);

            // === DEBUG ===
            System.Diagnostics.Debug.WriteLine($"RFC Cliente: '{factura.RfcCliente}'");
            System.Diagnostics.Debug.WriteLine($"NumRegIdTrib: '{factura.ComercioExterior?.NumRegIdTrib}'");
            System.Diagnostics.Debug.WriteLine($"Longitud NumRegIdTrib: {factura.ComercioExterior?.NumRegIdTrib?.Length}");

            using (XmlWriter writer = XmlWriter.Create(filePath, settings))
            {
                writer.WriteStartDocument(true);

                // ===== COMPROBANTE =====
                writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");
                writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");
                writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

                if (factura.ComercioExterior != null)
                    writer.WriteAttributeString("xmlns", "cce20", null, "http://www.sat.gob.mx/ComercioExterior20");

                string schemaLocation = "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd";
                if (factura.ComercioExterior != null)
                    schemaLocation += " http://www.sat.gob.mx/ComercioExterior20 http://www.sat.gob.mx/sitio_internet/cfd/ComercioExterior20/ComercioExterior20.xsd";

                writer.WriteAttributeString("xsi", "schemaLocation", null, schemaLocation);
                writer.WriteAttributeString("Version", "4.0");
                writer.WriteAttributeString("Serie", factura.Serie ?? "");
                writer.WriteAttributeString("Folio", factura.FolioCorto ?? "");
                writer.WriteAttributeString("Fecha", fechaFrontera.ToString("yyyy-MM-ddTHH:mm:ss"));
                writer.WriteAttributeString("FormaPago", factura.IdTipoPago ?? "99");
                writer.WriteAttributeString("SubTotal", factura.Subtotal.ToString("0.00"));
                writer.WriteAttributeString("Moneda", factura.Moneda ?? "MXN");

                // ← NUEVO: escribir Descuento a nivel comprobante si hay descuentos
                if (factura.Descuento > 0)
                    writer.WriteAttributeString("Descuento", factura.Descuento.ToString("0.00"));

                if (factura.Moneda?.ToUpper() == "MXN")
                    writer.WriteAttributeString("TipoCambio", "1");
                else
                    writer.WriteAttributeString("TipoCambio", factura.TipoCambio.ToString("0.0000", CultureInfo.InvariantCulture));

                writer.WriteAttributeString("Total", factura.Total.ToString("0.00"));
                writer.WriteAttributeString("TipoDeComprobante", string.IsNullOrWhiteSpace(factura.TipoDeComprobante) ? "I" : factura.TipoDeComprobante);
                writer.WriteAttributeString("Exportacion", factura.Exportacion ?? "01");
                writer.WriteAttributeString("MetodoPago", factura.metodoPagoTexto ?? "PUE");
                writer.WriteAttributeString("LugarExpedicion", factura.LugarExpedicion ?? factura.CpR);

                // ===== CfdiRelacionados =====
                if (factura.TAnticipos != null && factura.TAnticipos.Rows.Count > 0)
                {
                    writer.WriteStartElement("cfdi", "CfdiRelacionados", null);
                    writer.WriteAttributeString("TipoRelacion", "07");
                    foreach (DataRow antRow in factura.TAnticipos.Rows)
                    {
                        writer.WriteStartElement("cfdi", "CfdiRelacionado", null);
                        writer.WriteAttributeString("UUID", antRow["uuid"].ToString());
                        writer.WriteEndElement(); // CfdiRelacionado
                    }
                    writer.WriteEndElement(); // CfdiRelacionados
                }

                // ===== EMISOR =====
                writer.WriteStartElement("cfdi", "Emisor", null);
                writer.WriteAttributeString("Rfc", factura.RfcEmisor);
                if (!string.IsNullOrEmpty(factura.RsoEmisor))
                    writer.WriteAttributeString("Nombre", factura.RsoEmisor);
                writer.WriteAttributeString("RegimenFiscal", factura.Rege ?? "");
                writer.WriteEndElement(); // Emisor

                // ===== RECEPTOR =====
                writer.WriteStartElement("cfdi", "Receptor", null);
                writer.WriteAttributeString("Rfc", factura.RfcCliente);
                if (!string.IsNullOrEmpty(factura.RsoCliente))
                    writer.WriteAttributeString("Nombre", factura.RsoCliente);

                // ResidenciaFiscal solo aplica cuando hay complemento CE
                if (factura.ComercioExterior?.DomicilioDestinatario?.Pais is string paisReceptor
                    && !string.IsNullOrEmpty(paisReceptor))
                {
                    writer.WriteAttributeString("ResidenciaFiscal", paisReceptor);
                }

                // NumRegIdTrib solo si RFC genérico y hay CE
                if (factura.ComercioExterior != null
                    && factura.RfcCliente == "XEXX010101000"
                    && !string.IsNullOrWhiteSpace(factura.ComercioExterior.NumRegIdTrib))
                {
                    writer.WriteAttributeString("NumRegIdTrib", factura.ComercioExterior.NumRegIdTrib.Trim());
                }

                if (!string.IsNullOrEmpty(factura.CpR))
                    writer.WriteAttributeString("DomicilioFiscalReceptor", factura.LugarExpedicion ?? factura.CpR);
                writer.WriteAttributeString("RegimenFiscalReceptor", factura.Regc ?? "");
                writer.WriteAttributeString("UsoCFDI", factura.IdUsoCFDI ?? "");
                writer.WriteEndElement(); // Receptor

                // ===== CONCEPTOS =====
                writer.WriteStartElement("cfdi", "Conceptos", null);

                foreach (var (row, importeBruto, montoDescuento, importeNeto) in conceptosCalculados)
                {
                    decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                    decimal precioUnitario = Convert.ToDecimal(row["precioUnit"]);

                    string noIdentificacion = row["numero"]?.ToString() ?? "";

                    writer.WriteStartElement("cfdi", "Concepto", null);
                    writer.WriteAttributeString("ClaveProdServ", row["claveProdServ"].ToString());
                    writer.WriteAttributeString("NoIdentificacion", noIdentificacion);
                    writer.WriteAttributeString("Cantidad", cantidad.ToString("0.00"));
                    writer.WriteAttributeString("ClaveUnidad", row["claveUnidad"].ToString());
                    writer.WriteAttributeString("Unidad", row["unidad"]?.ToString() ?? "PZA");
                    writer.WriteAttributeString("Descripcion", row["descripcion"].ToString());
                    writer.WriteAttributeString("ValorUnitario", precioUnitario.ToString("0.00"));
                    writer.WriteAttributeString("Importe", importeBruto.ToString("0.00"));   // ← decimal

                    if (montoDescuento > 0)
                        writer.WriteAttributeString("Descuento", montoDescuento.ToString("0.00"));     // ← mismo decimal redondeado

                    writer.WriteAttributeString("ObjetoImp", "02");

                    // IVA 0% — Base usa el neto (importe - descuento)
                    writer.WriteStartElement("cfdi", "Impuestos", null);
                    writer.WriteStartElement("cfdi", "Traslados", null);
                    writer.WriteStartElement("cfdi", "Traslado", null);
                    writer.WriteAttributeString("Base", importeNeto.ToString("0.00", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("Impuesto", "002");
                    writer.WriteAttributeString("TipoFactor", "Tasa");
                    writer.WriteAttributeString("TasaOCuota", "0.000000");
                    writer.WriteAttributeString("Importe", "0.00");
                    writer.WriteEndElement(); // Traslado
                    writer.WriteEndElement(); // Traslados
                    writer.WriteEndElement(); // Impuestos

                    writer.WriteEndElement(); // Concepto
                }

                // ===== FLETE =====
                if (factura.Flete > 0)
                {
                    writer.WriteStartElement("cfdi", "Concepto", null);
                    writer.WriteAttributeString("ClaveProdServ", "78101800");
                    writer.WriteAttributeString("NoIdentificacion", "FLETE");
                    writer.WriteAttributeString("Cantidad", "1.00");
                    writer.WriteAttributeString("ClaveUnidad", "E48");
                    writer.WriteAttributeString("Unidad", "Servicio");
                    writer.WriteAttributeString("Descripcion", "Servicio de flete");
                    writer.WriteAttributeString("ValorUnitario", subtotalFlete.ToString("0.00"));
                    writer.WriteAttributeString("Importe", subtotalFlete.ToString("0.00"));
                    writer.WriteAttributeString("ObjetoImp", "02");

                    // IVA 0% a nivel Concepto Flete
                    writer.WriteStartElement("cfdi", "Impuestos", null);
                    writer.WriteStartElement("cfdi", "Traslados", null);
                    writer.WriteStartElement("cfdi", "Traslado", null);
                    writer.WriteAttributeString("Base", subtotalFlete.ToString("0.00", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("Impuesto", "002");
                    writer.WriteAttributeString("TipoFactor", "Tasa");
                    writer.WriteAttributeString("TasaOCuota", "0.000000");
                    writer.WriteAttributeString("Importe", "0.00");
                    writer.WriteEndElement(); // Traslado
                    writer.WriteEndElement(); // Traslados
                    writer.WriteEndElement(); // Impuestos

                    writer.WriteEndElement(); // Concepto Flete
                }

                writer.WriteEndElement(); // Conceptos

                // ===== IMPUESTOS A NIVEL COMPROBANTE (IVA 0%) =====
                writer.WriteStartElement("cfdi", "Impuestos", null);
                writer.WriteAttributeString("TotalImpuestosTrasladados", "0.00");
                writer.WriteStartElement("cfdi", "Traslados", null);
                writer.WriteStartElement("cfdi", "Traslado", null);
                writer.WriteAttributeString("Base", baseTotalImpuestos.ToString("0.00", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("Impuesto", "002");
                writer.WriteAttributeString("TipoFactor", "Tasa");
                writer.WriteAttributeString("TasaOCuota", "0.000000");
                writer.WriteAttributeString("Importe", "0.00");
                writer.WriteEndElement(); // Traslado
                writer.WriteEndElement(); // Traslados
                writer.WriteEndElement(); // Impuestos Comprobante

                // ===== COMPLEMENTO: COMERCIO EXTERIOR V2.0 =====
                if (factura.ComercioExterior != null)
                {
                    var ce = factura.ComercioExterior;

                    // 🔹 PASO 1: Consolidar mercancías ANTES de escribir cualquier XML
                    var mercanciasConsolidadas = ce.Mercancias
                        .GroupBy(m => m.NoIdentificacion + "|" + m.FraccionArancelaria)
                        .Select(g =>
                        {
                            var primera = g.First();
                            decimal cantidadTotal = g.Sum(m => m.CantidadAduana);

                            decimal importeBrutoConsolidado = importesBrutoPorProducto.ContainsKey(primera.NoIdentificacion)
                                ? importesBrutoPorProducto[primera.NoIdentificacion]
                                : 0m;

                            decimal valorDolaresCalculado;

                            switch (factura.Moneda?.ToUpper())
                            {
                                case "USD":
                                    valorDolaresCalculado = importeBrutoConsolidado;
                                    break;

                                case "MXN":
                                    valorDolaresCalculado = importeBrutoConsolidado / ce.TipoCambioUSD;
                                    break;

                                case "EUR":
                                    valorDolaresCalculado = (importeBrutoConsolidado * (decimal)factura.TipoCambio) / ce.TipoCambioUSD;
                                    break;

                                default:
                                    valorDolaresCalculado = (importeBrutoConsolidado * (decimal)factura.TipoCambio) / ce.TipoCambioUSD;
                                    break;
                            }

                            decimal valorDolaresTotal = Math.Round(valorDolaresCalculado, 2, MidpointRounding.AwayFromZero);
                            decimal valorUnitario = cantidadTotal > 0
                                ? Math.Round(valorDolaresTotal / cantidadTotal, 2, MidpointRounding.AwayFromZero)
                                : primera.ValorUnitarioAduana;

                            return new MercanciaExportada
                            {
                                NoIdentificacion = primera.NoIdentificacion,
                                FraccionArancelaria = primera.FraccionArancelaria,
                                CantidadAduana = cantidadTotal,
                                UnidadAduana = primera.UnidadAduana,
                                ValorUnitarioAduana = valorUnitario,
                                ValorDolares = valorDolaresTotal,
                            };
                        })
                        .ToList();

                    // 🔹 PASO 2: TotalUSD correcto ANTES de escribir el XML
                    ce.TotalUSD = mercanciasConsolidadas.Sum(m => m.ValorDolares);

                    System.Diagnostics.Debug.WriteLine($"TotalUSD consolidado: {ce.TotalUSD:0.00}");

                    // 🔹 PASO 3: Recién ahora empezar a escribir el XML del complemento
                    writer.WriteStartElement("cfdi", "Complemento", null);
                    writer.WriteStartElement("cce20", "ComercioExterior", "http://www.sat.gob.mx/ComercioExterior20");

                    writer.WriteAttributeString("Version", ce.Version ?? "2.0");
                    if (!string.IsNullOrEmpty(ce.ClaveDePedimento))
                        writer.WriteAttributeString("ClaveDePedimento", ce.ClaveDePedimento);
                    if (!string.IsNullOrEmpty(ce.CertificadoOrigen))
                        writer.WriteAttributeString("CertificadoOrigen", ce.CertificadoOrigen);
                    if (!string.IsNullOrEmpty(ce.NumCertificadoOrigen))
                        writer.WriteAttributeString("NumCertificadoOrigen", ce.NumCertificadoOrigen);
                    if (!string.IsNullOrEmpty(ce.NumeroExportadorConfiable))
                        writer.WriteAttributeString("NumeroExportadorConfiable", ce.NumeroExportadorConfiable);
                    if (!string.IsNullOrEmpty(ce.Incoterm))
                        writer.WriteAttributeString("Incoterm", ce.Incoterm);

                    writer.WriteAttributeString("TipoCambioUSD", ce.TipoCambioUSD.ToString("0.0000", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("TotalUSD", ce.TotalUSD.ToString("0.00", CultureInfo.InvariantCulture)); // ✅ ya consolidado

                    // ===== EMISOR CE =====
                    if (ce.DomicilioEmisor != null)
                    {
                        writer.WriteStartElement("cce20", "Emisor", null);
                        writer.WriteStartElement("cce20", "Domicilio", null);

                        if (!string.IsNullOrEmpty(ce.DomicilioEmisor.Calle))
                            writer.WriteAttributeString("Calle", ce.DomicilioEmisor.Calle);
                        if (!string.IsNullOrEmpty(ce.DomicilioEmisor.NumeroExterior) &&
                            !ce.DomicilioEmisor.NumeroExterior.StartsWith("0"))
                            writer.WriteAttributeString("NumeroExterior", ce.DomicilioEmisor.NumeroExterior);
                        if (!string.IsNullOrEmpty(ce.DomicilioEmisor.Colonia))
                            writer.WriteAttributeString("Colonia", ce.DomicilioEmisor.Colonia);
                        if (!string.IsNullOrEmpty(ce.DomicilioEmisor.Municipio))
                            writer.WriteAttributeString("Municipio", ce.DomicilioEmisor.Municipio);
                        if (!string.IsNullOrEmpty(ce.DomicilioEmisor.Estado))
                            writer.WriteAttributeString("Estado", ce.DomicilioEmisor.Estado);
                        if (!string.IsNullOrEmpty(ce.DomicilioEmisor.Pais))
                            writer.WriteAttributeString("Pais", ce.DomicilioEmisor.Pais);
                        if (!string.IsNullOrEmpty(ce.DomicilioEmisor.CodigoPostal))
                            writer.WriteAttributeString("CodigoPostal", ce.DomicilioEmisor.CodigoPostal);

                        writer.WriteEndElement(); // Domicilio Emisor CE
                        writer.WriteEndElement(); // Emisor CE
                    }

                    // ===== RECEPTOR CE =====
                    writer.WriteStartElement("cce20", "Receptor", null);
                    if (!string.IsNullOrEmpty(ce.NumRegIdTrib))
                        writer.WriteAttributeString("NumRegIdTrib", ce.NumRegIdTrib.Trim());

                    writer.WriteStartElement("cce20", "Domicilio", null);
                    writer.WriteAttributeString("Calle", ce.DomicilioDestinatario.Calle);
                    writer.WriteAttributeString("Estado", ce.DomicilioDestinatario.Estado);
                    writer.WriteAttributeString("Pais", ce.DomicilioDestinatario.Pais);
                    writer.WriteAttributeString("CodigoPostal", ce.DomicilioDestinatario.CodigoPostal);
                    writer.WriteEndElement(); // Domicilio Receptor CE
                    writer.WriteEndElement(); // Receptor CE

                    // ===== DESTINATARIO CE =====
                    writer.WriteStartElement("cce20", "Destinatario", null);
                    if (ce.DomicilioDestinatario != null)
                    {
                        writer.WriteStartElement("cce20", "Domicilio", null);
                        if (!string.IsNullOrEmpty(ce.DomicilioDestinatario.Calle))
                            writer.WriteAttributeString("Calle", ce.DomicilioDestinatario.Calle);
                        if (!string.IsNullOrEmpty(ce.DomicilioDestinatario.Municipio))
                            writer.WriteAttributeString("Municipio", ce.DomicilioDestinatario.Municipio);
                        if (!string.IsNullOrEmpty(ce.DomicilioDestinatario.Estado))
                            writer.WriteAttributeString("Estado", ce.DomicilioDestinatario.Estado);
                        if (!string.IsNullOrEmpty(ce.DomicilioDestinatario.Pais))
                            writer.WriteAttributeString("Pais", ce.DomicilioDestinatario.Pais);
                        if (!string.IsNullOrEmpty(ce.DomicilioDestinatario.CodigoPostal))
                            writer.WriteAttributeString("CodigoPostal", ce.DomicilioDestinatario.CodigoPostal);
                        writer.WriteEndElement(); // Domicilio Destinatario CE
                    }
                    writer.WriteEndElement(); // Destinatario CE

                    // ===== MERCANCIAS CE =====
                    writer.WriteStartElement("cce20", "Mercancias", null);
                    foreach (var m in mercanciasConsolidadas) // ✅ usa la lista ya calculada
                    {
                        writer.WriteStartElement("cce20", "Mercancia", null);
                        if (!string.IsNullOrEmpty(m.NoIdentificacion))
                            writer.WriteAttributeString("NoIdentificacion", m.NoIdentificacion);
                        if (!string.IsNullOrEmpty(m.FraccionArancelaria))
                            writer.WriteAttributeString("FraccionArancelaria", m.FraccionArancelaria);
                        writer.WriteAttributeString("CantidadAduana",
                            Math.Round(m.CantidadAduana, 2, MidpointRounding.AwayFromZero)
                                .ToString("0.00", CultureInfo.InvariantCulture));
                        if (!string.IsNullOrEmpty(m.UnidadAduana))
                            writer.WriteAttributeString("UnidadAduana", m.UnidadAduana);
                        writer.WriteAttributeString("ValorUnitarioAduana",
                            Math.Round(m.ValorUnitarioAduana, 2, MidpointRounding.AwayFromZero)
                                .ToString("0.00", CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("ValorDolares",
                            m.ValorDolares.ToString("0.00", CultureInfo.InvariantCulture));
                        writer.WriteEndElement(); // Mercancia
                    }
                    writer.WriteEndElement(); // Mercancias

                    writer.WriteEndElement(); // ComercioExterior
                    writer.WriteEndElement(); // Complemento
                }

                writer.WriteEndElement(); // Comprobante
                writer.WriteEndDocument();

            } // FIN using XmlWriter

            XmlResult result = new XmlResult
            {
                uuid = myuuidAsString,
                factura = factura
            };

            return result;
        }

        #region Funciones auxiliares
        private void EscribirAddenda(XmlWriter writer, Factura factura)
        {
            var addenda = factura.Addenda;
            if (addenda == null) return;

            writer.WriteStartElement("cfdi", "Addenda", null);
            writer.WriteStartElement(addenda.Prefix, "Factura", addenda.Namespace);
            writer.WriteAttributeString("xmlns", addenda.Prefix, null, addenda.Namespace);

            if (!string.IsNullOrWhiteSpace(addenda.SchemaLocation))
            {
                writer.WriteAttributeString(
                    "xsi",
                    "schemaLocation",
                    "http://www.w3.org/2001/XMLSchema-instance",
                    addenda.SchemaLocation
                );
            }

            writer.WriteAttributeString("ordenCompra", ObtenerValorTemplate(addenda, "ordenCompra", factura));
            writer.WriteAttributeString("tipoDocumento", ObtenerValorTemplate(addenda, "tipoDocumento", factura));
            writer.WriteAttributeString("referencia1", factura.Folio);
            writer.WriteAttributeString("version", ObtenerValorTemplate(addenda, "version", factura, "1.0"));
            writer.WriteAttributeString("folio", factura.Folio);
            writer.WriteAttributeString("fecha", factura.Fecha.ToString("yyyy-MM-dd"));

            writer.WriteStartElement(addenda.Prefix, "Moneda", addenda.Namespace);
            writer.WriteAttributeString("importeConLetra", NumeroALetras1(factura.Total));
            writer.WriteAttributeString("tipoMoneda", factura.Moneda);
            writer.WriteEndElement();

            writer.WriteStartElement(addenda.Prefix, "Proveedor", addenda.Namespace);
            writer.WriteAttributeString("codigo", ObtenerValorTemplate(addenda, "proveedor.codigo", factura));
            writer.WriteEndElement();

            writer.WriteStartElement(addenda.Prefix, "Entrega", addenda.Namespace);
            writer.WriteAttributeString("plantaEntrega", ObtenerValorTemplate(addenda, "entrega.plantaEntrega", factura));
            writer.WriteAttributeString("calle", ObtenerValorTemplate(addenda, "entrega.calle", factura));
            writer.WriteAttributeString("noExterior", ObtenerValorTemplate(addenda, "entrega.noExterior", factura));
            writer.WriteAttributeString("noInterior", ObtenerValorTemplate(addenda, "entrega.noInterior", factura, "NA"));
            writer.WriteAttributeString("codigoPostal", ObtenerValorTemplate(addenda, "entrega.codigoPostal", factura));
            writer.WriteEndElement();

            EscribirDetallesMabe(writer, factura, addenda);

            writer.WriteStartElement(addenda.Prefix, "Subtotal", addenda.Namespace);
            writer.WriteAttributeString("importe", factura.Subtotal.ToString("0.00"));
            writer.WriteEndElement();

            writer.WriteStartElement(addenda.Prefix, "Traslados", addenda.Namespace);
            writer.WriteStartElement(addenda.Prefix, "traslado", addenda.Namespace);
            writer.WriteAttributeString("Importe", factura.IVA.ToString("0.00"));
            writer.WriteAttributeString("Tipo", "IVA");
            writer.WriteAttributeString("Tasa", "16");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement(addenda.Prefix, "Total", addenda.Namespace);
            writer.WriteAttributeString("importe", factura.Total.ToString("0.00"));
            writer.WriteEndElement();

            writer.WriteEndElement(); // Factura
            writer.WriteEndElement(); // cfdi:Addenda
        }

        private void EscribirDetallesMabe(XmlWriter writer, Factura factura, Addenda addenda)
        {
            writer.WriteStartElement(addenda.Prefix, "Detalles", addenda.Namespace);

            int lineaArticulo = 1;
            foreach (DataRow row in factura.Tproductos.Rows)
            {
                if (addenda.Options.ExcluirFlete && row["numero"].ToString().ToUpper() == "FLETE")
                    continue;

                writer.WriteStartElement(addenda.Prefix, "Detalle", addenda.Namespace);

                writer.WriteAttributeString("descripcion", row["descripcion"].ToString());
                writer.WriteAttributeString("noLineaArticulo", lineaArticulo.ToString());
                writer.WriteAttributeString("precioSinIva", Convert.ToDecimal(row["precioUnit"]).ToString("0.00"));
                writer.WriteAttributeString("importeSinIva", Convert.ToDecimal(row["importe"]).ToString("0.00"));
                writer.WriteAttributeString("codigoArticulo", row["numero"].ToString());
                writer.WriteAttributeString("unidad", row["unidad"]?.ToString() ?? "PZA");
                writer.WriteAttributeString("cantidad", Convert.ToDecimal(row["cantidad"]).ToString("0.00"));

                writer.WriteEndElement();
                lineaArticulo++;
            }

            writer.WriteEndElement();
        }

        private string ObtenerValorTemplate(Addenda addenda, string key, Factura factura, string defaultValue = "")
        {
            if (addenda.DatosTemplate.ContainsKey(key))
                return addenda.DatosTemplate[key];

            return defaultValue;
        }

        private string NumeroALetras1(decimal numero)
        {
            int entero = (int)numero;
            int centavos = (int)((numero - entero) * 100);
            return $"{entero.ToString().ToUpper()} PESOS {centavos:00}/100";
        }
        #endregion
    }
}

