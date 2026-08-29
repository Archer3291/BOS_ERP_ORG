// Services/Refacturacion/XmlCfdiBuilder.cs
using BOS_ERP.Controllers;
using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Models;
using BOS_ERP.services.Facturacion;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;

namespace BOS_ERP.Services.Refacturacion
{
    /// <summary>
    /// Misma lógica de armado de XML que FacturacionVentaController.GenerarXml,
    /// extraída a un servicio para poder reutilizarla en refacturación.
    /// Regresa el XML en memoria (string) — el timbrado real lo hace IPacService.
    /// </summary>
    public static class XmlCfdiBuilder
    {
        // <paramref name="utils"/>: si se pasa y la factura trae Comercio Exterior, se refresca el
        // TipoCambioUSD con el valor DOF vigente (tabla tasas_cambio) y se recalculan los importes en
        // dólares. Necesario porque el CFDI se re-timbra en una fecha distinta a la original y el SAT
        // valida TipoCambioUSD contra el DOF del día (CCE121).
        public static string Construir(Factura factura, string tipoRelacion = null, string uuidRelacionado = null,
            Utilities utils = null)
        {
            using var sw = new StringWriter();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = Encoding.UTF8
            };

            // ── 1. Calcular totales (idéntico a GenerarXml) ──
            decimal subtotalBruto = 0;
            decimal subtotalNeto = 0;
            decimal descuentoGlobalComprobante = 0;
            decimal ivaTotal = 0;
            decimal baseTrasladados = 0;    // base gravada (solo conceptos con ObjetoImp='02')
            decimal tasaComprobante = 0m;   // tasa uniforme del traslado (0.16 nacional, 0.00 exportación)
            bool hayTraslados = false;

            // Importe bruto (en la moneda del comprobante) por NoIdentificacion — para recalcular
            // los ValorDolares del complemento de Comercio Exterior. Igual que FacturacionVentaIN.
            var importesBrutoPorProducto = new Dictionary<string, decimal>();

            var conceptosCalculados = new List<(
                DataRow row, decimal importeBruto, decimal importeNeto,
                decimal montoDescuento, decimal ivaConcepto, string objetoImp, decimal tasaCuota)>();

            foreach (DataRow row in factura.Tproductos.Rows)
            {
                decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                decimal precioUnit = Convert.ToDecimal(row["precioUnit"]);
                decimal descPct = row.Table.Columns.Contains("descuento") ? Convert.ToDecimal(row["descuento"]) : 0m;

                decimal importeBruto = Math.Round(cantidad * precioUnit, 2);
                decimal montoDescuento = Math.Round(importeBruto * (descPct / 100m), 2);
                decimal importeNeto = importeBruto - montoDescuento;
                string objImp = row["objetoImp"]?.ToString() ?? "01";

                // Tasa real del traslado tomada del XML original (0.16 nacional, 0.00 exportación).
                // Facturas de exportación llevan ObjetoImp='02' pero TasaOCuota=0 → NO se puede asumir 16%.
                decimal tasaCuota = row.Table.Columns.Contains("tasaCuota") && row["tasaCuota"] != DBNull.Value
                    ? Convert.ToDecimal(row["tasaCuota"])
                    : 0.16m; // fallback defensivo para tablas legacy sin la columna

                decimal ivaConcepto = 0m;
                if (objImp == "02")
                {
                    ivaConcepto = Math.Round(importeNeto * tasaCuota, 2);
                    baseTrasladados += importeNeto;
                    tasaComprobante = tasaCuota; // uniforme en estas facturas (todas 16% o todas 0%)
                    hayTraslados = true;
                }

                subtotalBruto += importeBruto;
                subtotalNeto += importeNeto;
                descuentoGlobalComprobante += montoDescuento;
                ivaTotal += ivaConcepto;

                string noId = row["numero"]?.ToString() ?? "";
                if (!importesBrutoPorProducto.ContainsKey(noId)) importesBrutoPorProducto[noId] = 0m;
                importesBrutoPorProducto[noId] += importeBruto;

                conceptosCalculados.Add((row, importeBruto, importeNeto, montoDescuento, ivaConcepto, objImp, tasaCuota));
            }

            decimal subtotalFlete = Math.Round(factura.Flete, 2);
            if (factura.Flete > 0)
            {
                decimal ivaFlete = Math.Round(subtotalFlete * 0.16m, 2);
                ivaTotal += ivaFlete;
                subtotalNeto += subtotalFlete;
                subtotalBruto += subtotalFlete;
            }

            factura.Subtotal = subtotalBruto;
            factura.IVA = ivaTotal;
            factura.Total = subtotalBruto - descuentoGlobalComprobante + ivaTotal;

            // ── 2. Armar XML ──
            using (XmlWriter writer = XmlWriter.Create(sw, settings))
            {
                // ¿La factura original trae complemento de Comercio Exterior?
                bool tieneCE = factura.ComercioExterior != null;

                // Refrescar el TipoCambioUSD con el valor DOF vigente: el CFDI se re-timbra en otra
                // fecha y el SAT valida TipoCambioUSD contra el DOF del día (CCE121). Los ValorDolares
                // se recalculan luego con este TC dentro de EscribirComercioExterior.
                if (tieneCE && utils != null)
                {
                    decimal tcUsdVigente = ObtenerTipoCambioUSDVigente(utils);
                    if (tcUsdVigente > 0)
                        factura.ComercioExterior.TipoCambioUSD = tcUsdVigente;
                }

                writer.WriteStartDocument(true);
                writer.WriteStartElement("cfdi", "Comprobante", "http://www.sat.gob.mx/cfd/4");
                writer.WriteAttributeString("xmlns", "cfdi", null, "http://www.sat.gob.mx/cfd/4");
                writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");

                if (tieneCE)
                    writer.WriteAttributeString("xmlns", "cce20", null, "http://www.sat.gob.mx/ComercioExterior20");

                if (factura.Addenda?.DatosTemplate?.Count > 0)
                    writer.WriteAttributeString("xmlns", factura.Addenda.Prefix, null, factura.Addenda.Namespace);

                writer.WriteAttributeString("Version", "4.0");
                writer.WriteAttributeString("Serie", factura.Serie ?? "");
                writer.WriteAttributeString("Folio", factura.FolioCorto ?? "");
                writer.WriteAttributeString("Fecha", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
                writer.WriteAttributeString("FormaPago", factura.IdTipoPago ?? "99");
                writer.WriteAttributeString("SubTotal", factura.Subtotal.ToString("0.00"));
                writer.WriteAttributeString("Moneda", factura.Moneda ?? "MXN");
                // TipoCambio: MXN → "1"; moneda extranjera → invariante con 4 decimales (evita
                // "19,20" bajo culturas con coma decimal, que invalidaría el CFDI). Espeja la
                // lógica de FacturacionVentaIN.GenerarFactura para el flujo internacional.
                writer.WriteAttributeString("TipoCambio",
                    (factura.Moneda?.ToUpper() == "MXN")
                        ? "1"
                        : factura.TipoCambio.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteAttributeString("Total", factura.Total.ToString("0.00"));
                writer.WriteAttributeString("TipoDeComprobante", string.IsNullOrWhiteSpace(factura.TipoDeComprobante) ? "I" : factura.TipoDeComprobante);
                writer.WriteAttributeString("Exportacion", factura.Exportacion ?? "01");
                writer.WriteAttributeString("MetodoPago", factura.metodoPagoTexto ?? "PUE");
                writer.WriteAttributeString("LugarExpedicion", factura.LugarExpedicion ?? "64000");

                string schemaLocation = "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd";
                if (tieneCE)
                    schemaLocation += " http://www.sat.gob.mx/ComercioExterior20 http://www.sat.gob.mx/sitio_internet/cfd/ComercioExterior20/ComercioExterior20.xsd";
                writer.WriteAttributeString("xsi", "schemaLocation", null, schemaLocation);

                if (descuentoGlobalComprobante > 0)
                    writer.WriteAttributeString("Descuento", descuentoGlobalComprobante.ToString("0.00"));

                // ── CfdiRelacionados: anticipos (igual que GenerarXml) ──
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

                // ── CfdiRelacionados: sustitución por refacturación (nuevo) ──
                if (!string.IsNullOrWhiteSpace(tipoRelacion) && !string.IsNullOrWhiteSpace(uuidRelacionado))
                {
                    writer.WriteStartElement("cfdi", "CfdiRelacionados", null);
                    writer.WriteAttributeString("TipoRelacion", tipoRelacion);
                    writer.WriteStartElement("cfdi", "CfdiRelacionado", null);
                    writer.WriteAttributeString("UUID", uuidRelacionado);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                writer.WriteStartElement("cfdi", "Emisor", null);
                writer.WriteAttributeString("Rfc", factura.RfcEmisor);
                writer.WriteAttributeString("Nombre", factura.RsoEmisor);
                writer.WriteAttributeString("RegimenFiscal", factura.Rege);
                writer.WriteEndElement();

                writer.WriteStartElement("cfdi", "Receptor", null);
                writer.WriteAttributeString("Rfc", factura.RfcCliente);
                writer.WriteAttributeString("Nombre", factura.RsoCliente);

                // Comercio Exterior: el receptor extranjero requiere ResidenciaFiscal y, si el RFC es
                // el genérico de extranjero, NumRegIdTrib. Se derivan del CE igual que FacturacionVentaIN.
                if (factura.ComercioExterior != null)
                {
                    string paisReceptor = factura.ComercioExterior.DomicilioDestinatario?.Pais;
                    if (!string.IsNullOrWhiteSpace(paisReceptor))
                        writer.WriteAttributeString("ResidenciaFiscal", paisReceptor);

                    if (factura.RfcCliente == "XEXX010101000"
                        && !string.IsNullOrWhiteSpace(factura.ComercioExterior.NumRegIdTrib))
                        writer.WriteAttributeString("NumRegIdTrib", factura.ComercioExterior.NumRegIdTrib.Trim());
                }

                writer.WriteAttributeString("DomicilioFiscalReceptor", factura.CpR);
                writer.WriteAttributeString("RegimenFiscalReceptor", factura.Regc);
                writer.WriteAttributeString("UsoCFDI", factura.IdUsoCFDI);
                writer.WriteEndElement();

                writer.WriteStartElement("cfdi", "Conceptos", null);

                foreach (var (row, importeBruto, importeNeto, montoDescuento, ivaConcepto, objImp, tasaCuota) in conceptosCalculados)
                {
                    decimal cantidad = Convert.ToDecimal(row["cantidad"]);
                    decimal precioUnit = Convert.ToDecimal(row["precioUnit"]);

                    string descripcionFinal = row["descripcion"].ToString();
                    if (row.Table.Columns.Contains("comentario") && !string.IsNullOrWhiteSpace(row["comentario"]?.ToString()))
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
                        writer.WriteAttributeString("Base", importeNeto.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("Impuesto", "002");
                        writer.WriteAttributeString("TipoFactor", "Tasa");
                        // Tasa real del concepto (0.160000 nacional, 0.000000 exportación).
                        writer.WriteAttributeString("TasaOCuota", tasaCuota.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("Importe", ivaConcepto.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                        writer.WriteEndElement();
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
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteEndElement(); // Concepto
                }

                writer.WriteEndElement(); // Conceptos

                // Nodo Impuestos a nivel comprobante: requerido cuando existe al menos un concepto
                // con traslado, AUNQUE la tasa sea 0% (facturas de exportación). Antes se omitía si
                // IVA==0, lo que dejaba el CFDI de exportación sin este nodo obligatorio.
                if (hayTraslados)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    writer.WriteStartElement("cfdi", "Impuestos", null);
                    writer.WriteAttributeString("TotalImpuestosTrasladados", factura.IVA.ToString("0.00", inv));
                    writer.WriteStartElement("cfdi", "Traslados", null);
                    writer.WriteStartElement("cfdi", "Traslado", null);
                    writer.WriteAttributeString("Base", baseTrasladados.ToString("0.00", inv));
                    writer.WriteAttributeString("Impuesto", "002");
                    writer.WriteAttributeString("TipoFactor", "Tasa");
                    writer.WriteAttributeString("TasaOCuota", tasaComprobante.ToString("0.000000", inv));
                    writer.WriteAttributeString("Importe", factura.IVA.ToString("0.00", inv));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                // ── Complemento: Comercio Exterior ──
                // Se reconstruye el nodo cce20:ComercioExterior recalculando TipoCambioUSD/ValorDolares/
                // TotalUSD con el TC del DOF vigente. Va después de Impuestos y antes de Addenda.
                if (tieneCE)
                    EscribirComercioExterior(writer, factura, importesBrutoPorProducto);

                if (factura.Addenda?.DatosTemplate?.Count > 0)
                    EscribirAddenda(writer, factura);

                writer.WriteEndElement(); // Comprobante
                writer.WriteEndDocument();
            }

            return sw.ToString();
        }

        // TipoCambioUSD DOF vigente (tabla tasas_cambio, columna dolar). El job diario 'cargar-paridades'
        // la mantiene. Se toma la fila más reciente. Devuelve 0 si no hay dato (se conserva el original).
        private static decimal ObtenerTipoCambioUSDVigente(Utilities utils)
        {
            try
            {
                var val = utils.RunScalar(
                    "SELECT dolar FROM tasas_cambio WHERE dolar > 0 ORDER BY fecha DESC LIMIT 1",
                    new Dictionary<string, object>());
                return (val != null && val != DBNull.Value) ? Convert.ToDecimal(val) : 0m;
            }
            catch { return 0m; }
        }

        // Reconstruye el complemento cce20:ComercioExterior a partir de factura.ComercioExterior,
        // recalculando ValorDolares/ValorUnitarioAduana/TotalUSD con el TipoCambioUSD ya refrescado.
        // Espeja la lógica de FacturacionVentaIN.GenerarFactura (consolidación por NoIdentificacion).
        private static void EscribirComercioExterior(XmlWriter writer, Factura factura,
            Dictionary<string, decimal> importesBrutoPorProducto)
        {
            var ce = factura.ComercioExterior;
            if (ce == null) return;
            var inv = CultureInfo.InvariantCulture;

            // Consolidar mercancías por NoIdentificacion+Fracción y recalcular USD.
            var mercanciasConsolidadas = (ce.Mercancias ?? new List<MercanciaExportada>())
                .GroupBy(m => m.NoIdentificacion + "|" + m.FraccionArancelaria)
                .Select(g =>
                {
                    var primera = g.First();
                    decimal cantidadTotal = g.Sum(m => m.CantidadAduana);
                    decimal importeBruto = importesBrutoPorProducto.TryGetValue(primera.NoIdentificacion, out var ib) ? ib : 0m;

                    decimal valorDolares;
                    switch (factura.Moneda?.ToUpper())
                    {
                        case "USD": valorDolares = importeBruto; break;                                   // ya en dólares
                        case "MXN": valorDolares = ce.TipoCambioUSD > 0 ? importeBruto / ce.TipoCambioUSD : 0m; break;
                        default:    // otras divisas (EUR, ...): a pesos vía TipoCambio y luego a USD.
                            valorDolares = ce.TipoCambioUSD > 0 ? (importeBruto * factura.TipoCambio) / ce.TipoCambioUSD : 0m;
                            break;
                    }

                    decimal valorDolaresTotal = Math.Round(valorDolares, 2, MidpointRounding.AwayFromZero);
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

            ce.TotalUSD = mercanciasConsolidadas.Sum(m => m.ValorDolares);

            writer.WriteStartElement("cfdi", "Complemento", null);
            writer.WriteStartElement("cce20", "ComercioExterior", "http://www.sat.gob.mx/ComercioExterior20");

            writer.WriteAttributeString("Version", string.IsNullOrWhiteSpace(ce.Version) ? "2.0" : ce.Version);
            if (!string.IsNullOrEmpty(ce.ClaveDePedimento)) writer.WriteAttributeString("ClaveDePedimento", ce.ClaveDePedimento);
            if (!string.IsNullOrEmpty(ce.CertificadoOrigen)) writer.WriteAttributeString("CertificadoOrigen", ce.CertificadoOrigen);
            if (!string.IsNullOrEmpty(ce.NumCertificadoOrigen)) writer.WriteAttributeString("NumCertificadoOrigen", ce.NumCertificadoOrigen);
            if (!string.IsNullOrEmpty(ce.NumeroExportadorConfiable)) writer.WriteAttributeString("NumeroExportadorConfiable", ce.NumeroExportadorConfiable);
            if (!string.IsNullOrEmpty(ce.Incoterm)) writer.WriteAttributeString("Incoterm", ce.Incoterm);
            writer.WriteAttributeString("TipoCambioUSD", ce.TipoCambioUSD.ToString("0.0000", inv));
            writer.WriteAttributeString("TotalUSD", ce.TotalUSD.ToString("0.00", inv));

            // Emisor
            if (ce.DomicilioEmisor != null)
            {
                var d = ce.DomicilioEmisor;
                writer.WriteStartElement("cce20", "Emisor", null);
                writer.WriteStartElement("cce20", "Domicilio", null);
                if (!string.IsNullOrEmpty(d.Calle)) writer.WriteAttributeString("Calle", d.Calle);
                if (!string.IsNullOrEmpty(d.NumeroExterior) && !d.NumeroExterior.StartsWith("0"))
                    writer.WriteAttributeString("NumeroExterior", d.NumeroExterior);
                if (!string.IsNullOrEmpty(d.Colonia)) writer.WriteAttributeString("Colonia", d.Colonia);
                if (!string.IsNullOrEmpty(d.Municipio)) writer.WriteAttributeString("Municipio", d.Municipio);
                if (!string.IsNullOrEmpty(d.Estado)) writer.WriteAttributeString("Estado", d.Estado);
                if (!string.IsNullOrEmpty(d.Pais)) writer.WriteAttributeString("Pais", d.Pais);
                if (!string.IsNullOrEmpty(d.CodigoPostal)) writer.WriteAttributeString("CodigoPostal", d.CodigoPostal);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            // Receptor
            writer.WriteStartElement("cce20", "Receptor", null);
            if (!string.IsNullOrEmpty(ce.NumRegIdTrib)) writer.WriteAttributeString("NumRegIdTrib", ce.NumRegIdTrib.Trim());
            if (ce.DomicilioDestinatario != null)
            {
                var d = ce.DomicilioDestinatario;
                writer.WriteStartElement("cce20", "Domicilio", null);
                writer.WriteAttributeString("Calle", d.Calle);
                writer.WriteAttributeString("Estado", d.Estado);
                writer.WriteAttributeString("Pais", d.Pais);
                writer.WriteAttributeString("CodigoPostal", d.CodigoPostal);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            // Destinatario
            writer.WriteStartElement("cce20", "Destinatario", null);
            if (ce.DomicilioDestinatario != null)
            {
                var d = ce.DomicilioDestinatario;
                writer.WriteStartElement("cce20", "Domicilio", null);
                if (!string.IsNullOrEmpty(d.Calle)) writer.WriteAttributeString("Calle", d.Calle);
                if (!string.IsNullOrEmpty(d.Municipio)) writer.WriteAttributeString("Municipio", d.Municipio);
                if (!string.IsNullOrEmpty(d.Estado)) writer.WriteAttributeString("Estado", d.Estado);
                if (!string.IsNullOrEmpty(d.Pais)) writer.WriteAttributeString("Pais", d.Pais);
                if (!string.IsNullOrEmpty(d.CodigoPostal)) writer.WriteAttributeString("CodigoPostal", d.CodigoPostal);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            // Mercancias
            writer.WriteStartElement("cce20", "Mercancias", null);
            foreach (var m in mercanciasConsolidadas)
            {
                writer.WriteStartElement("cce20", "Mercancia", null);
                if (!string.IsNullOrEmpty(m.NoIdentificacion)) writer.WriteAttributeString("NoIdentificacion", m.NoIdentificacion);
                if (!string.IsNullOrEmpty(m.FraccionArancelaria)) writer.WriteAttributeString("FraccionArancelaria", m.FraccionArancelaria);
                writer.WriteAttributeString("CantidadAduana",
                    Math.Round(m.CantidadAduana, 2, MidpointRounding.AwayFromZero).ToString("0.00", inv));
                if (!string.IsNullOrEmpty(m.UnidadAduana)) writer.WriteAttributeString("UnidadAduana", m.UnidadAduana);
                writer.WriteAttributeString("ValorUnitarioAduana",
                    Math.Round(m.ValorUnitarioAduana, 2, MidpointRounding.AwayFromZero).ToString("0.00", inv));
                writer.WriteAttributeString("ValorDolares", m.ValorDolares.ToString("0.00", inv));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            writer.WriteEndElement(); // ComercioExterior
            writer.WriteEndElement(); // Complemento
        }

        // Idéntico a FacturacionVentaController.EscribirAddenda, movido aquí para reuso.
        private static void EscribirAddenda(XmlWriter writer, Factura factura)
        {
            var addenda = factura.Addenda;
            if (addenda == null) return;

            writer.WriteStartElement("cfdi", "Addenda", null);
            writer.WriteStartElement(addenda.Prefix, "Factura", addenda.Namespace);
            writer.WriteAttributeString("xmlns", addenda.Prefix, null, addenda.Namespace);

            if (!string.IsNullOrWhiteSpace(addenda.SchemaLocation))
                writer.WriteAttributeString("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance", addenda.SchemaLocation);

            writer.WriteAttributeString("ordenCompra", ObtenerValorTemplate(addenda, "ordenCompra"));
            writer.WriteAttributeString("tipoDocumento", ObtenerValorTemplate(addenda, "tipoDocumento"));
            writer.WriteAttributeString("referencia1", factura.Folio);
            writer.WriteAttributeString("version", ObtenerValorTemplate(addenda, "version", "1.0"));
            writer.WriteAttributeString("folio", factura.Folio);
            writer.WriteAttributeString("fecha", factura.Fecha.ToString("yyyy-MM-dd"));

            writer.WriteStartElement(addenda.Prefix, "Moneda", addenda.Namespace);
            writer.WriteAttributeString("importeConLetra", ComprobanteFiscalService.NumeroALetras(factura.Total));
            writer.WriteAttributeString("tipoMoneda", factura.Moneda);
            writer.WriteEndElement();

            writer.WriteStartElement(addenda.Prefix, "Proveedor", addenda.Namespace);
            writer.WriteAttributeString("codigo", ObtenerValorTemplate(addenda, "proveedor.codigo"));
            writer.WriteEndElement();

            writer.WriteStartElement(addenda.Prefix, "Entrega", addenda.Namespace);
            writer.WriteAttributeString("plantaEntrega", ObtenerValorTemplate(addenda, "entrega.plantaEntrega"));
            writer.WriteAttributeString("calle", ObtenerValorTemplate(addenda, "entrega.calle"));
            writer.WriteAttributeString("noExterior", ObtenerValorTemplate(addenda, "entrega.noExterior"));
            writer.WriteAttributeString("noInterior", ObtenerValorTemplate(addenda, "entrega.noInterior", "NA"));
            writer.WriteAttributeString("codigoPostal", ObtenerValorTemplate(addenda, "entrega.codigoPostal"));
            writer.WriteEndElement();

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
            writer.WriteEndElement(); // Detalles

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

        private static string ObtenerValorTemplate(Addenda addenda, string key, string defaultValue = "")
            => addenda.DatosTemplate.TryGetValue(key, out var v) ? v : defaultValue;
    }
}
