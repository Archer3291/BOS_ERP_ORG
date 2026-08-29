using System.Collections.Generic;
using System;
namespace BOS_ERP.Models
{
    public class CargaInicialModel
    {
        public string Usuario { get; set; }
        public string Folio { get; set; }
        public DateTime FechaCaptura { get; set; }
        public string TipoDocumento { get; set; }
        public string FormaPago { get; set; }
        public decimal? Subtotal { get; set; }
        public decimal? Descuento { get; set; }
        public string Moneda { get; set; }
        public decimal? TipoCambio { get; set; }
        public decimal? Total { get; set; }
        public string Plazo { get; set; }
        public string TipoComprobante { get; set; }
        public string MetodoPago { get; set; }
        public string CPReceptor { get; set; }
        public string RFCEmisor { get; set; }
        public string RazonEmisor { get; set; }
        public string RegimenReceptor { get; set; }
        public string RFCReceptor { get; set; }
        public string RazonReceptor { get; set; }
        public string UsoCFDI { get; set; }
        public decimal? IVAPorcentual { get; set; }
        public decimal? IVARetenido { get; set; }
        public decimal? ISRRetenido { get; set; }
        public decimal? TotalImpuestosRetenidos { get; set; }
        public decimal? TotalImpuestosTraslado { get; set; }
        public string Tipo { get; set; }
        public string FolioKepler { get; set; }
        public string Estatus { get; set; }
        public string Uuid { get; set; }
    }
}