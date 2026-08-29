using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Models;
using BOS_ERP.services.Facturacion;
using System.Xml.Linq;

public static class AddendaXmlWriter
{
    public static XElement ConstruirNodo(Factura factura)
    {
        var addenda = factura.Addenda;
        XNamespace cfdiNs = "http://www.sat.gob.mx/cfd/4";
        XNamespace addNs = addenda.Namespace;

        var facturaEl = new XElement(addNs + "Factura",
            new XAttribute(XNamespace.Xmlns + addenda.Prefix, addenda.Namespace),
            new XAttribute("ordenCompra", ObtenerValor(addenda, "ordenCompra")),
            new XAttribute("tipoDocumento", ObtenerValor(addenda, "tipoDocumento")),
            new XAttribute("referencia1", factura.Folio),
            new XAttribute("version", ObtenerValor(addenda, "version", "1.0")),
            new XAttribute("folio", factura.Folio),
            new XAttribute("fecha", factura.Fecha.ToString("yyyy-MM-dd")),
            new XElement(addNs + "Moneda",
                new XAttribute("importeConLetra", ComprobanteFiscalService.NumeroALetras(factura.Total)),
                new XAttribute("tipoMoneda", factura.Moneda)),
            new XElement(addNs + "Proveedor",
                new XAttribute("codigo", ObtenerValor(addenda, "proveedor.codigo"))),
            new XElement(addNs + "Entrega",
                new XAttribute("plantaEntrega", ObtenerValor(addenda, "entrega.plantaEntrega")),
                new XAttribute("calle", ObtenerValor(addenda, "entrega.calle")),
                new XAttribute("noExterior", ObtenerValor(addenda, "entrega.noExterior")),
                new XAttribute("noInterior", ObtenerValor(addenda, "entrega.noInterior", "NA")),
                new XAttribute("codigoPostal", ObtenerValor(addenda, "entrega.codigoPostal"))),
            new XElement(addNs + "Subtotal", new XAttribute("importe", factura.Subtotal.ToString("0.00"))),
            new XElement(addNs + "Traslados",
                new XElement(addNs + "traslado",
                    new XAttribute("Importe", factura.IVA.ToString("0.00")),
                    new XAttribute("Tipo", "IVA"),
                    new XAttribute("Tasa", "16"))),
            new XElement(addNs + "Total", new XAttribute("importe", factura.Total.ToString("0.00")))
        );

        return new XElement(cfdiNs + "Addenda", facturaEl);
    }

    private static string ObtenerValor(Addenda addenda, string key, string def = "")
        => addenda.DatosTemplate.TryGetValue(key, out var v) ? v : def;
}
