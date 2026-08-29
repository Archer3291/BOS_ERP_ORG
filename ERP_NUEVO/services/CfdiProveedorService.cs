using System.Globalization;
using System.Xml.Linq;

namespace BOS_ERP.Services
{
    public class ImpuestoCfdi
    {
        public string Clave { get; set; }
        public decimal Tasa { get; set; }
        public decimal Importe { get; set; }
        public bool EsRetencion { get; set; }
    }

    public class ComprobanteProveedor
    {
        public string Uuid { get; set; }
        public string Serie { get; set; }
        public string Folio { get; set; }
        public DateTime? Fecha { get; set; }
        public string RfcEmisor { get; set; }
        public string NombreEmisor { get; set; }
        public string RfcReceptor { get; set; }
        public string Moneda { get; set; }
        public string MetodoPago { get; set; }
        public string FormaPago { get; set; }

        public decimal SubTotal { get; set; }
        public decimal Descuento { get; set; }
        public decimal Total { get; set; }
        public decimal Traslados { get; set; }
        public decimal Retenciones { get; set; }

        public List<ImpuestoCfdi> Impuestos { get; set; } = new();

        public string FolioCompleto =>
            string.IsNullOrWhiteSpace(Serie) ? Folio : $"{Serie}-{Folio}";
    }

    public class DiferenciaFactura
    {
        public string Concepto { get; set; }
        public string EnLaOrden { get; set; }
        public string EnLaFactura { get; set; }
        public string Severidad { get; set; }   // critica | advertencia
        public string Nota { get; set; }
    }

    /// <summary>
    /// Lee el CFDI que manda el proveedor y lo confronta contra la orden de compra.
    /// Soporta CFDI 3.3 y 4.0: solo cambia el namespace del comprobante.
    /// </summary>
    public static class CfdiProveedorService
    {
        private static readonly XNamespace Tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

        // Tolerancia por redondeo del PAC; debajo de esto no vale la pena alertar.
        public const decimal ToleranciaImporte = 0.50m;

        public static ComprobanteProveedor Leer(string xml)
        {
            var doc = XDocument.Parse(xml);
            var comprobante = doc.Root
                ?? throw new InvalidOperationException("El XML no tiene un nodo raiz valido.");

            // El namespace lo dicta el propio comprobante: sirve para 3.3 y para 4.0
            XNamespace cfdi = comprobante.Name.Namespace;

            if (comprobante.Name.LocalName != "Comprobante")
                throw new InvalidOperationException("El archivo no es un CFDI: se esperaba el nodo Comprobante.");

            var emisor = comprobante.Element(cfdi + "Emisor");
            var receptor = comprobante.Element(cfdi + "Receptor");
            var impuestos = comprobante.Element(cfdi + "Impuestos");
            var timbre = comprobante.Element(cfdi + "Complemento")?.Descendants(Tfd + "TimbreFiscalDigital").FirstOrDefault();

            var resultado = new ComprobanteProveedor
            {
                Uuid = Attr(timbre, "UUID"),
                Serie = Attr(comprobante, "Serie"),
                Folio = Attr(comprobante, "Folio"),
                Fecha = Fecha(Attr(comprobante, "Fecha")),
                RfcEmisor = Attr(emisor, "Rfc"),
                NombreEmisor = Attr(emisor, "Nombre"),
                RfcReceptor = Attr(receptor, "Rfc"),
                Moneda = Attr(comprobante, "Moneda"),
                MetodoPago = Attr(comprobante, "MetodoPago"),
                FormaPago = Attr(comprobante, "FormaPago"),
                SubTotal = Numero(Attr(comprobante, "SubTotal")),
                Descuento = Numero(Attr(comprobante, "Descuento")),
                Total = Numero(Attr(comprobante, "Total")),
            };

            foreach (var t in Nodos(impuestos, cfdi, "Traslados", "Traslado"))
            {
                resultado.Impuestos.Add(new ImpuestoCfdi
                {
                    Clave = Attr(t, "Impuesto"),
                    Tasa = Numero(Attr(t, "TasaOCuota")) * 100m,
                    Importe = Numero(Attr(t, "Importe")),
                    EsRetencion = false
                });
            }

            foreach (var r in Nodos(impuestos, cfdi, "Retenciones", "Retencion"))
            {
                resultado.Impuestos.Add(new ImpuestoCfdi
                {
                    Clave = Attr(r, "Impuesto"),
                    Tasa = Numero(Attr(r, "TasaOCuota")) * 100m,
                    Importe = Numero(Attr(r, "Importe")),
                    EsRetencion = true
                });
            }

            // Si el comprobante no trae los totales agregados, se suman los renglones
            resultado.Traslados = Numero(Attr(impuestos, "TotalImpuestosTrasladados"));
            if (resultado.Traslados == 0)
                resultado.Traslados = resultado.Impuestos.Where(i => !i.EsRetencion).Sum(i => i.Importe);

            resultado.Retenciones = Numero(Attr(impuestos, "TotalImpuestosRetenidos"));
            if (resultado.Retenciones == 0)
                resultado.Retenciones = resultado.Impuestos.Where(i => i.EsRetencion).Sum(i => i.Importe);

            return resultado;
        }

        /// <summary>
        /// Confronta el CFDI contra los totales de la orden de compra.
        /// Devuelve la lista de diferencias; vacia significa que todo cuadra.
        /// </summary>
        public static List<DiferenciaFactura> Comparar(
            ComprobanteProveedor cfdi,
            decimal ocSubtotal,
            decimal ocDescuento,
            decimal ocTraslados,
            decimal ocRetenciones,
            decimal ocTotal,
            string ocProveedor,
            string ocRfcProveedor = null,
            string ocMoneda = null)
        {
            var diferencias = new List<DiferenciaFactura>();

            void Comparar1(string concepto, decimal enOrden, decimal enFactura, string severidad = "critica", string nota = null)
            {
                if (Math.Abs(enOrden - enFactura) <= ToleranciaImporte) return;

                diferencias.Add(new DiferenciaFactura
                {
                    Concepto = concepto,
                    EnLaOrden = enOrden.ToString("C2", CultureInfo.GetCultureInfo("es-MX")),
                    EnLaFactura = enFactura.ToString("C2", CultureInfo.GetCultureInfo("es-MX")),
                    Severidad = severidad,
                    Nota = nota ?? $"Diferencia de {Math.Abs(enOrden - enFactura).ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}"
                });
            }

            Comparar1("Subtotal", ocSubtotal, cfdi.SubTotal);
            Comparar1("Descuento", ocDescuento, cfdi.Descuento, "advertencia");
            Comparar1("Impuestos trasladados", ocTraslados, cfdi.Traslados);
            Comparar1("Retenciones", ocRetenciones, cfdi.Retenciones, "advertencia");
            Comparar1("Total", ocTotal, cfdi.Total);

            // El nombre del proveedor se captura a mano y suele traer espacios de sobra
            if (!string.IsNullOrWhiteSpace(ocProveedor) && !string.IsNullOrWhiteSpace(cfdi.NombreEmisor)
                && !CoincideNombre(ocProveedor, cfdi.NombreEmisor))
            {
                diferencias.Add(new DiferenciaFactura
                {
                    Concepto = "Proveedor",
                    EnLaOrden = Normalizar(ocProveedor),
                    EnLaFactura = cfdi.NombreEmisor,
                    Severidad = "advertencia",
                    Nota = "El nombre del emisor no coincide con el proveedor de la orden."
                });
            }

            if (!string.IsNullOrWhiteSpace(ocRfcProveedor) && !string.IsNullOrWhiteSpace(cfdi.RfcEmisor)
                && !string.Equals(ocRfcProveedor.Trim(), cfdi.RfcEmisor.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                diferencias.Add(new DiferenciaFactura
                {
                    Concepto = "RFC del emisor",
                    EnLaOrden = ocRfcProveedor,
                    EnLaFactura = cfdi.RfcEmisor,
                    Severidad = "critica",
                    Nota = "El RFC del CFDI no es el del proveedor registrado."
                });
            }

            if (!string.IsNullOrWhiteSpace(cfdi.Moneda) && !string.IsNullOrWhiteSpace(ocMoneda)
                && !MonedaEquivalente(ocMoneda, cfdi.Moneda))
            {
                diferencias.Add(new DiferenciaFactura
                {
                    Concepto = "Moneda",
                    EnLaOrden = ocMoneda,
                    EnLaFactura = cfdi.Moneda,
                    Severidad = "critica",
                    Nota = "La factura viene en una moneda distinta a la de la orden."
                });
            }

            if (string.IsNullOrWhiteSpace(cfdi.Uuid))
            {
                diferencias.Add(new DiferenciaFactura
                {
                    Concepto = "Timbre fiscal",
                    EnLaOrden = "—",
                    EnLaFactura = "Sin UUID",
                    Severidad = "critica",
                    Nota = "El XML no tiene timbre fiscal digital: puede no estar timbrado."
                });
            }

            return diferencias;
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static IEnumerable<XElement> Nodos(XElement impuestos, XNamespace cfdi, string grupo, string hijo)
            => impuestos?.Element(cfdi + grupo)?.Elements(cfdi + hijo) ?? Enumerable.Empty<XElement>();

        private static string Attr(XElement el, string nombre) => el?.Attribute(nombre)?.Value;

        private static decimal Numero(string valor) =>
            decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        private static DateTime? Fecha(string valor) =>
            DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

        public static string Normalizar(string texto) =>
            string.IsNullOrWhiteSpace(texto) ? "" : string.Join(" ", texto.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        private static bool CoincideNombre(string a, string b)
        {
            string na = Normalizar(a).ToUpperInvariant();
            string nb = Normalizar(b).ToUpperInvariant();

            // Las razones sociales varian en puntos y abreviaturas: SA DE CV / S.A. DE C.V.
            foreach (var c in new[] { ".", ",", "-" })
            {
                na = na.Replace(c, "");
                nb = nb.Replace(c, "");
            }

            return na == nb || na.Contains(nb) || nb.Contains(na);
        }

        private static bool MonedaEquivalente(string ocMoneda, string cfdiMoneda)
        {
            string oc = Normalizar(ocMoneda).ToUpperInvariant();
            string cf = Normalizar(cfdiMoneda).ToUpperInvariant();

            if (oc == cf) return true;
            // El encabezado guarda "PESOS" donde el CFDI usa la clave del SAT
            if (cf == "MXN" && (oc == "PESOS" || oc == "MXP" || oc == "")) return true;
            if (cf == "USD" && (oc == "DOLARES" || oc == "DLLS")) return true;

            return false;
        }
    }
}
