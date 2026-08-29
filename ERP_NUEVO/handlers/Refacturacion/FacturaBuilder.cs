// Services/Refacturacion/FacturaBuilder.cs
using System.Data;
using System.Globalization;
using System.Xml.Linq;
using BOS_ERP.Controllers;
using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Models;
using BOS_ERP.services.Facturacion;

namespace BOS_ERP.Services.Refacturacion
{
    public static class FacturaBuilder
    {
        /// <summary>
        /// Reconstruye un objeto Factura desde la BD a partir de encabezado_id.
        /// Los datos de encabezado (cliente, emisor, totales) salen de la tabla `factura`.
        /// Los conceptos (incluyendo claveUnidad/objetoImp, que NO se guardan en dfactura)
        /// se recuperan parseando el XML ya timbrado (fa.textfactura).
        ///
        /// <paramref name="xmlOverride"/>: si se especifica, se usa ese XML timbrado en vez del
        /// de la columna `textfactura` (p. ej. cuando el XML se leyó del archivo en disco porque
        /// la columna venía vacía). Si es null/vacío se cae a `textfactura` como siempre.
        /// </summary>
        public static Factura DesdeEncabezado(Utilities utils, int encabezadoId, string xmlOverride = null)
        {
            var facturaRows = utils.RunQuery(
                @"SELECT fa.*, em.mdp, em.tp_mov
                  FROM factura fa
                  INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
                  WHERE fa.encabezado_id = @encId 
                  ORDER BY fa.id DESC
                  LIMIT 1",
                new Dictionary<string, object> { { "encId", encabezadoId } });

            if (facturaRows.Count == 0)
                throw new InvalidOperationException($"No se encontró una factura Timbrada para encabezado_id={encabezadoId}.");

            var row = facturaRows[0];

            var factura = new Factura
            {
                EncabezadoId = encabezadoId,
                IdFactura = Convert.ToInt32(row["id"]),
                Serie = row["serie"]?.ToString() ?? "",
                Folio = row["folio"]?.ToString() ?? "",
                FolioCorto = row["folio"]?.ToString() ?? "",
                IdTipoFactura = Convert.ToInt32(row["idtipofactura"] ?? 0),
                IdCliente = Convert.ToInt32(row["idcliente"] ?? 0),
                RfcCliente = row["rfccliente"]?.ToString() ?? "",
                RsoCliente = row["rsocliente"]?.ToString() ?? "",
                EmlCliente = row["emlcliente"]?.ToString() ?? "",
                IdEmisor = Convert.ToInt32(row["idemisor"] ?? 0),
                RfcEmisor = row["rfcemisor"]?.ToString() ?? "",
                RsoEmisor = row["rsoemisor"]?.ToString() ?? "",
                IdExpedicion = Convert.ToInt32(row["idexpedicion"] ?? 0),
                IdUsuario = Convert.ToInt32(row["idusuario"] ?? 0),
                Fecha = row["fecha"] != DBNull.Value ? Convert.ToDateTime(row["fecha"]) : DateTime.Now,
                StatusFactura = row["statusfactura"]?.ToString() ?? "TIMBRADA",
                MdpFactura = row["mdpfactura"]?.ToString() ?? "",
                IdLugarExp = Convert.ToInt32(row["idlugarexp"] ?? 0),
                IdTipoPago = row["idtipopago"]?.ToString() ?? "",
                Moneda = row["moneda"]?.ToString() ?? "MXN",
                UUID = row["uuid"]?.ToString() ?? "",
                Importe = Convert.ToDecimal(row["importe"] ?? 0),
                Descuento = Convert.ToDecimal(row["descuento"] ?? 0),
                Subtotal = Convert.ToDecimal(row["subtotal"] ?? 0),
                IVA = Convert.ToDecimal(row["iva"] ?? 0),
                Total = Convert.ToDecimal(row["total"] ?? 0),
                Saldo = Convert.ToDecimal(row["saldo"] ?? 0),
                IdPedido = Convert.ToInt32(row["idpedido"] ?? 0),
                RetISR = Convert.ToDecimal(row["retisr"] ?? 0),
                RetIVA = Convert.ToDecimal(row["retiva"] ?? 0),
                Observaciones = row["observaciones"]?.ToString() ?? "",
                IdVendedor = Convert.ToInt32(row["idvendedor"] ?? 0),
                UsoCFDI = row["usocfdi"]?.ToString() ?? "",
                IdUsoCFDI = row["idusocfdi"]?.ToString() ?? "",
                Cbb = row["cbb"]?.ToString() ?? "",
                SelloSAT = row["sellosat"]?.ToString() ?? "",
                SelloCFDI = row["sellocfdi"]?.ToString() ?? "",
                CadenaOriginal = row["cadenaoriginal"]?.ToString() ?? "",
                Oc = row["oc"]?.ToString() ?? "",
                Tdc = Convert.ToDecimal(row["tdc"] ?? 0),
                Anticipo = Convert.ToDecimal(row["anticipo"] ?? 0) > 0,
                Rege = row["reg_fise"]?.ToString() ?? "",  // OJO: columnas cruzadas igual que en GuardarFactura
                Regc = row["reg_fisr"]?.ToString() ?? "",
                CpR = row["cpr"]?.ToString() ?? "",
                CpE = row["cpe"]?.ToString() ?? "",
                TipoFacturacion = row["tipo"]?.ToString() ?? "",
                metodoPagoTexto = row["mdpfactura"]?.ToString() ?? "PUE",
            };

            // ── Reconstruir Conceptos desde el XML ya timbrado (tiene claveUnidad/objetoImp reales) ──
            // OJO: el XML se guarda en la columna `textfactura` (GuardarFactura mapea @xmlfactura → textfactura).
            // Se prioriza xmlOverride (p. ej. XML leído del archivo en disco) sobre la columna.
            string xmlOriginal = !string.IsNullOrWhiteSpace(xmlOverride)
                ? xmlOverride
                : (row.TryGetValue("textfactura", out var xmlCol) ? xmlCol?.ToString() ?? "" : "");

            // Los campos de catálogo (FormaPago, MetodoPago, Moneda, TipoCambio, LugarExpedicion,
            // Exportacion, TipoDeComprobante, régimenes) se toman del XML ya timbrado — que pasó
            // validación SAT — para evitar formatos inválidos como FormaPago='3' en vez de '03'
            // que provienen de las columnas numéricas de la BD.
            AplicarDatosComprobanteDesdeXml(factura, xmlOriginal);

            factura.Tproductos = ParseConceptosDesdeXml(xmlOriginal);

            // Complemento de Comercio Exterior (facturas de exportación): se pinta en el PDF si viene en el XML.
            ParseComercioExteriorDesdeXml(factura, xmlOriginal);

            // Dirección del cliente: NO viaja en el CFDI estándar, se toma del maestro de clientes.
            factura.DireccionReceptor = ObtenerDireccionReceptor(utils, factura.IdCliente);

            // NOTA: la aplicación de anticipo en este ERP NO se representa con un CfdiRelacionados 07
            // sobre la factura, sino con una NOTA DE CRÉDITO por aplicación de anticipo (encabezadomov
            // nat='NT', encabezados_padre = esta factura). Por eso aquí NO se puebla factura.TAnticipos:
            // el sustituto no debe llevar una relación 07 que el CFDI original no tenía. El manejo de
            // esas notas (cancelar las de la original y crear la del sustituto) es del orquestador.

            return factura;
        }

        // Sobreescribe los campos de nivel Comprobante/Emisor/Receptor con los valores exactos
        // del CFDI original timbrado. Los cambios específicos del tipo (p.ej. datos-fiscales:
        // rfc/razón social/régimen/cp/uso) se aplican DESPUÉS en el handler, así que aquí solo
        // dejamos una base fiel y con formato de catálogo válido.
        private static void AplicarDatosComprobanteDesdeXml(Factura factura, string xmlFactura)
        {
            if (string.IsNullOrWhiteSpace(xmlFactura))
                return;

            XDocument xdoc;
            try { xdoc = XDocument.Parse(xmlFactura); }
            catch { return; } // si el XML no parsea, conservamos los valores de la BD

            XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";
            var comp = xdoc.Descendants(cfdi + "Comprobante").FirstOrDefault();
            if (comp == null) return;

            factura.Serie = (string)comp.Attribute("Serie") ?? factura.Serie;
            factura.FolioCorto = (string)comp.Attribute("Folio") ?? factura.FolioCorto;
            factura.IdTipoPago = (string)comp.Attribute("FormaPago") ?? factura.IdTipoPago;        // '03', '99', ...
            factura.metodoPagoTexto = (string)comp.Attribute("MetodoPago") ?? factura.metodoPagoTexto; // 'PUE'/'PPD'
            factura.Moneda = (string)comp.Attribute("Moneda") ?? factura.Moneda;
            factura.LugarExpedicion = (string)comp.Attribute("LugarExpedicion") ?? factura.LugarExpedicion;
            factura.Exportacion = (string)comp.Attribute("Exportacion") ?? factura.Exportacion;
            factura.TipoDeComprobante = (string)comp.Attribute("TipoDeComprobante") ?? factura.TipoDeComprobante;

            // TipoCambio: usar el del XML; si no viene (típico en MXN) nunca dejar 0 → 1.
            var tcAttr = (string)comp.Attribute("TipoCambio");
            if (!string.IsNullOrWhiteSpace(tcAttr) &&
                decimal.TryParse(tcAttr, NumberStyles.Any, CultureInfo.InvariantCulture, out var tcVal))
                factura.TipoCambio = tcVal;
            else if (factura.TipoCambio <= 0)
                factura.TipoCambio = factura.Tdc > 0 ? factura.Tdc : 1m;

            var emisor = comp.Element(cfdi + "Emisor");
            if (emisor != null)
            {
                factura.RfcEmisor = (string)emisor.Attribute("Rfc") ?? factura.RfcEmisor;
                factura.RsoEmisor = (string)emisor.Attribute("Nombre") ?? factura.RsoEmisor;
                factura.Rege = (string)emisor.Attribute("RegimenFiscal") ?? factura.Rege;
            }

            var receptor = comp.Element(cfdi + "Receptor");
            if (receptor != null)
            {
                factura.RfcCliente = (string)receptor.Attribute("Rfc") ?? factura.RfcCliente;
                factura.RsoCliente = (string)receptor.Attribute("Nombre") ?? factura.RsoCliente;
                factura.CpR = (string)receptor.Attribute("DomicilioFiscalReceptor") ?? factura.CpR;
                factura.Regc = (string)receptor.Attribute("RegimenFiscalReceptor") ?? factura.Regc;
                factura.IdUsoCFDI = (string)receptor.Attribute("UsoCFDI") ?? factura.IdUsoCFDI;
            }

            factura.NoCertificado = (string)comp.Attribute("NoCertificado") ?? factura.NoCertificado;

            // Timbre fiscal: sellos, certificado SAT y cadena original (para el PDF).
            XNamespace tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";
            var timbre = xdoc.Descendants(tfd + "TimbreFiscalDigital").FirstOrDefault();
            if (timbre != null)
            {
                string version = (string)timbre.Attribute("Version") ?? "1.1";
                string uuidT = (string)timbre.Attribute("UUID") ?? factura.UUID;
                string fechaT = (string)timbre.Attribute("FechaTimbrado") ?? "";
                string selloCFD = (string)timbre.Attribute("SelloCFD") ?? factura.SelloCFDI;
                string noCertSat = (string)timbre.Attribute("NoCertificadoSAT") ?? "";

                factura.UUID = uuidT;
                factura.SelloSAT = (string)timbre.Attribute("SelloSAT") ?? factura.SelloSAT;
                factura.SelloCFDI = selloCFD;
                factura.NoCertificadoSAT = noCertSat;
                factura.CadenaOriginal = $"||{version}|{uuidT}|{fechaT}|{selloCFD}|{noCertSat}||";
            }

            // CfdiRelacionados: para pintar la sección de sustitución en el PDF.
            var relacionados = xdoc.Descendants(cfdi + "CfdiRelacionados").FirstOrDefault();
            if (relacionados != null)
            {
                factura.TipoRelacion = (string)relacionados.Attribute("TipoRelacion") ?? factura.TipoRelacion;
                var uuids = relacionados.Descendants(cfdi + "CfdiRelacionado")
                    .Select(r => (string)r.Attribute("UUID"))
                    .Where(u => !string.IsNullOrWhiteSpace(u));
                factura.UUIDsRelacionados = string.Join(",", uuids);
            }

            factura.TotalTexto = ComprobanteFiscalService.NumeroALetras(factura.Total);
        }

        private static DataTable ParseConceptosDesdeXml(string xmlFactura)
        {
            var tabla = new DataTable();
            tabla.Columns.Add("cantidad", typeof(decimal));
            tabla.Columns.Add("precioUnit", typeof(decimal));
            tabla.Columns.Add("descuento", typeof(decimal));   // % de descuento (recalculado desde importe/descuento absoluto del XML)
            tabla.Columns.Add("objetoImp", typeof(string));
            tabla.Columns.Add("descripcion", typeof(string));
            tabla.Columns.Add("comentario", typeof(string));
            tabla.Columns.Add("claveProdServ", typeof(string));
            tabla.Columns.Add("numero", typeof(string));        // NoIdentificacion
            tabla.Columns.Add("claveUnidad", typeof(string));
            tabla.Columns.Add("unidad", typeof(string));
            tabla.Columns.Add("importe", typeof(decimal));       // usado por CalculaTotal()
            tabla.Columns.Add("iva", typeof(decimal));           // usado por CalculaTotal()
            tabla.Columns.Add("tasaCuota", typeof(decimal));     // TasaOCuota real del traslado (0.16 nacional, 0.00 exportación)
            tabla.Columns.Add("pedimento", typeof(string));      // NumeroPedimento (InformacionAduanera), si aplica
            tabla.Columns.Add("clave_cliente", typeof(string));  // no viaja en el CFDI; se deja vacío

            if (string.IsNullOrWhiteSpace(xmlFactura))
                return tabla;

            var xdoc = XDocument.Parse(xmlFactura);
            XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";

            foreach (var concepto in xdoc.Descendants(cfdi + "Concepto"))
            {
                // Excluimos el concepto sintético de Flete: GenerarXml lo vuelve a agregar
                // por separado usando factura.Flete, así que si lo dejamos aquí se duplicaría.
                string noIdent = (string)concepto.Attribute("NoIdentificacion") ?? "";
                if (string.Equals(noIdent, "FLETE", StringComparison.OrdinalIgnoreCase))
                    continue;

                decimal importe = (decimal?)concepto.Attribute("Importe") ?? 0m;
                decimal montoDescuento = (decimal?)concepto.Attribute("Descuento") ?? 0m;
                decimal descPct = importe > 0 ? Math.Round((montoDescuento / importe) * 100m, 4) : 0m;

                var traslado = concepto.Descendants(cfdi + "Traslado").FirstOrDefault();
                decimal ivaConcepto = (decimal?)traslado?.Attribute("Importe") ?? 0m;
                // TasaOCuota real (0.160000 nacional, 0.000000 exportación). Sin traslado → 0.
                decimal tasaCuota = (decimal?)traslado?.Attribute("TasaOCuota") ?? 0m;

                var infoAduanera = concepto.Descendants(cfdi + "InformacionAduanera").FirstOrDefault();

                var fila = tabla.NewRow();
                fila["cantidad"] = (decimal?)concepto.Attribute("Cantidad") ?? 0m;
                fila["precioUnit"] = (decimal?)concepto.Attribute("ValorUnitario") ?? 0m;
                fila["descuento"] = descPct;
                fila["objetoImp"] = (string)concepto.Attribute("ObjetoImp") ?? "01";
                fila["descripcion"] = (string)concepto.Attribute("Descripcion") ?? "";
                fila["comentario"] = "";
                fila["claveProdServ"] = (string)concepto.Attribute("ClaveProdServ") ?? "";
                fila["numero"] = noIdent;
                fila["claveUnidad"] = (string)concepto.Attribute("ClaveUnidad") ?? "";
                fila["unidad"] = (string)concepto.Attribute("Unidad") ?? "";
                fila["importe"] = importe;
                fila["iva"] = ivaConcepto;
                fila["tasaCuota"] = tasaCuota;
                fila["pedimento"] = (string)infoAduanera?.Attribute("NumeroPedimento") ?? "";
                fila["clave_cliente"] = "";

                tabla.Rows.Add(fila);
            }

            return tabla;
        }

        // ── Complemento de Comercio Exterior (cce 1.1 / 2.0) desde el XML timbrado ──
        // Se busca por nombre local para tolerar ambas versiones (namespaces distintos).
        private static void ParseComercioExteriorDesdeXml(Factura factura, string xmlFactura)
        {
            if (string.IsNullOrWhiteSpace(xmlFactura)) return;

            XDocument xdoc;
            try { xdoc = XDocument.Parse(xmlFactura); }
            catch { return; }

            var ceNode = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ComercioExterior");
            if (ceNode == null) return;

            // Guardamos el nodo CE crudo para reemitirlo VERBATIM al refacturar (XmlCfdiBuilder).
            // ToString() de un XElement con namespace reproduce su declaración xmlns:cce20,
            // dejándolo autocontenido para WriteRaw.
            factura.ComercioExteriorXml = ceNode.ToString();

            XNamespace ns = ceNode.Name.Namespace;

            string A(XElement el, string name) => el == null ? "" : (string)el.Attribute(name) ?? "";
            decimal D(XElement el, string name)
                => el != null && decimal.TryParse((string)el.Attribute(name), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var v) ? v : 0m;

            var ce = new ComercioExterior
            {
                Version = A(ceNode, "Version"),
                MotivoTraslado = A(ceNode, "MotivoTraslado"),
                ClaveDePedimento = A(ceNode, "ClaveDePedimento"),
                CertificadoOrigen = A(ceNode, "CertificadoOrigen"),
                NumCertificadoOrigen = A(ceNode, "NumCertificadoOrigen"),
                NumeroExportadorConfiable = A(ceNode, "NumeroExportadorConfiable"),
                Incoterm = A(ceNode, "Incoterm"),
                TipoCambioUSD = D(ceNode, "TipoCambioUSD"),
                TotalUSD = D(ceNode, "TotalUSD"),
            };

            ce.DomicilioEmisor = LeerDomicilioCE(ceNode.Element(ns + "Emisor")?.Element(ns + "Domicilio"));

            var receptorNode = ceNode.Element(ns + "Receptor");
            ce.NumRegIdTrib = A(receptorNode, "NumRegIdTrib");

            // Domicilio del destinatario; si no hay nodo Destinatario, se usa el del Receptor.
            var destinatarioDom = ceNode.Element(ns + "Destinatario")?.Element(ns + "Domicilio")
                                  ?? receptorNode?.Element(ns + "Domicilio");
            ce.DomicilioDestinatario = LeerDomicilioCE(destinatarioDom);

            var mercancias = ceNode.Element(ns + "Mercancias");
            if (mercancias != null)
            {
                ce.Mercancias = mercancias.Elements(ns + "Mercancia").Select(m => new MercanciaExportada
                {
                    NoIdentificacion = A(m, "NoIdentificacion"),
                    FraccionArancelaria = A(m, "FraccionArancelaria"),
                    CantidadAduana = D(m, "CantidadAduana"),
                    UnidadAduana = A(m, "UnidadAduana"),
                    ValorUnitarioAduana = D(m, "ValorUnitarioAduana"),
                    ValorDolares = D(m, "ValorDolares"),
                }).ToList();
            }

            factura.ComercioExterior = ce;
        }

        private static DomicilioComExt LeerDomicilioCE(XElement dom)
        {
            if (dom == null) return new DomicilioComExt();
            string A(string name) => (string)dom.Attribute(name) ?? "";
            return new DomicilioComExt
            {
                Calle = A("Calle"),
                NumeroExterior = A("NumeroExterior"),
                Colonia = A("Colonia"),
                Localidad = A("Localidad"),
                Municipio = A("Municipio"),
                Estado = A("Estado"),
                Pais = A("Pais"),
                CodigoPostal = A("CodigoPostal"),
            };
        }

        // ── Dirección del cliente desde el maestro (direcciones_facturacion → legacy catclientes) ──
        // El CFDI 4.0 solo trae el CP fiscal del receptor; la dirección completa se toma de la BD.
        private static string ObtenerDireccionReceptor(Utilities utils, int idCliente)
        {
            if (idCliente <= 0) return "";
            try
            {
                var rows = utils.RunQuery(
                    @"SELECT df.calle, df.no_exterior, df.no_interior, df.colonia, df.localidad,
                             df.municipio, df.estado, df.pais, df.codigo_postal,
                             cc.dir, cc.col, cc.pob, cc.cp
                      FROM catclientes cc
                      LEFT JOIN direcciones_facturacion df
                             ON df.entidad_clave = cc.cve_cli AND df.empresa_id = cc.empresa_id
                      WHERE cc.id_cliente = @id
                      LIMIT 1",
                    new Dictionary<string, object> { { "id", idCliente } });

                if (rows.Count == 0) return "";
                var r = rows[0];
                string V(string k) => r.TryGetValue(k, out var o) ? o?.ToString()?.Trim() ?? "" : "";

                // Preferir la dirección estructurada; si no hay calle, caer al legacy de catclientes.
                if (!string.IsNullOrWhiteSpace(V("calle")))
                {
                    var l1 = string.Join(" ", new[]
                    {
                        V("calle"), V("no_exterior"),
                        string.IsNullOrWhiteSpace(V("no_interior")) ? "" : "Int. " + V("no_interior")
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    var l2 = string.Join(", ", new[]
                    {
                        V("colonia"), V("localidad"), V("municipio"), V("estado"), V("pais")
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    var l3 = string.IsNullOrWhiteSpace(V("codigo_postal")) ? "" : "C.P. " + V("codigo_postal");

                    return string.Join("\n", new[] { l1, l2, l3 }.Where(s => !string.IsNullOrWhiteSpace(s)));
                }

                var legacy = new[]
                {
                    V("dir"), V("col"), V("pob"),
                    string.IsNullOrWhiteSpace(V("cp")) ? "" : "C.P. " + V("cp")
                }.Where(s => !string.IsNullOrWhiteSpace(s));

                return string.Join("\n", legacy);
            }
            catch { return ""; }
        }
    }
}
