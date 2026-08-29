using System;

namespace TuNamespace.Models
{
    public class FacturaEmailModel
    {
        // Información de la factura
        public string UUID { get; set; }
        public string Serie { get; set; }
        public string Folio { get; set; }
        public DateTime FechaEmision { get; set; }
        public decimal Total { get; set; }
        public decimal Subtotal { get; set; }
        public decimal IVA { get; set; }
        public string FormaPago { get; set; }
        public string MetodoPago { get; set; }
        public string UsoCFDI { get; set; }

        // Información del emisor (tu empresa)
        public string EmisorRazonSocial { get; set; }
        public string EmisorRFC { get; set; }
        public string EmisorDireccion { get; set; }
        public string EmisorTelefono { get; set; }
        public string EmisorEmail { get; set; }
        public string EmisorLogoUrl { get; set; }

        // Información del receptor (cliente)
        public string ReceptorNombre { get; set; }
        public string ReceptorRFC { get; set; }
        public string ReceptorEmail { get; set; }

        // Conceptos/Productos
        public int CantidadConceptos { get; set; }
        public string DescripcionResumen { get; set; }

        // Mensaje personalizado (DEPRECADO - usar ObservacionesVendedor)
        [Obsolete("Use ObservacionesVendedor en su lugar")]
        public string MensajePersonalizado { get; set; }

        // NUEVO: Observaciones/Comentarios del vendedor
        public string ObservacionesVendedor { get; set; }

        // URLs de descarga
        public string PdfUrl { get; set; }
        public string XmlUrl { get; set; }

        // IDs de referencia
        public int? PedidoId { get; set; }
        public int? RemisionId { get; set; }

        // Propiedades calculadas
        public string SerieFolio => !string.IsNullOrEmpty(Serie) && !string.IsNullOrEmpty(Folio)
            ? $"{Serie}-{Folio}"
            : Folio ?? "N/A";

        public string TotalFormateado => Total.ToString("C2");
        public string SubtotalFormateado => Subtotal.ToString("C2");
        public string IVAFormateado => IVA.ToString("C2");

        // NUEVO: Propiedad para verificar si hay observaciones
        public bool TieneObservaciones => !string.IsNullOrWhiteSpace(ObservacionesVendedor);

        public FacturaEmailModel()
        {
            FechaEmision = DateTime.Now;
        }
    }
}
