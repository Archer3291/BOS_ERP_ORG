using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class PartidaDocumento
    {
        [Column("nropart")]
        public int NroPart { get; set; }
        [Column("cveprod")]
        public string CveProd { get; set; }
        public decimal? CantUd { get; set; }
        public string DescrProd { get; set; }
        public string Ud { get; set; }
        public decimal? PvProd { get; set; }
        public decimal? ImpPart { get; set; }
        public decimal? Dto1 { get; set; }
        public decimal? Iva { get; set; }
        public decimal? Ieps { get; set; }
        public int? FPagoId { get; set; }
        public int? GpoDocAnt { get; set; }
        public string TpDocAnt { get; set; }
        public string FolDocAnt { get; set; }
        public int? PartDocAnt { get; set; }
        public decimal? SaldoUdPart { get; set; }
        public string CveCli { get; set; }
        public decimal? CtoVtaPart { get; set; }
        public string CveVdrCpr { get; set; }
        public int? Ref { get; set; }
        public string CveAlm { get; set; }
        public DateTime? Fch { get; set; }
        public decimal? MtCto { get; set; }
        public decimal? ExisPrevU { get; set; }
        public decimal? ExisPrevPeso { get; set; }
        public string Ccy { get; set; }
        public decimal? CtoCcy { get; set; }
        public decimal? VtaCcy { get; set; }
        public string Pedimento { get; set; }
        public string Conc { get; set; }
        public int IdEncabezado { get; set; }
        public int? IdProducto { get; set; }
    }
}
