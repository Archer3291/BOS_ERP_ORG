using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class DocumentoEncabezado
    {
        public int EmpresaId { get; set; }
        public int IdArea { get; set; }
        public int IdTpDoc { get; set; }
        public string Gen { get; set; }
        public int Anio { get; set; }
        public string Nat { get; set; }
        public string Fol { get; set; }
        public int? IdDoc { get; set; }


        // Datos generales
        public int? Suc { get; set; }
        public string Ccy { get; set; }
        public string Alm { get; set; }
        public DateTime Fch { get; set; }
        public string CliProv { get; set; }
        public int? Ref { get; set; }
        public string VdrCpr { get; set; }
        public decimal? Dto { get; set; }
        public string Firma3 { get; set; }
        public string Firma4 { get; set; }
        public decimal? Imp { get; set; }
        public int? PlDias { get; set; }
        public DateTime? FchPgEntrega { get; set; }
        public decimal? Sub { get; set; }
        public string Firma2 { get; set; }
        public string Coment1 { get; set; }
        public string Coment2 { get; set; }
        public string Coment3 { get; set; }
        public string Firma5 { get; set; }
        public string NroCotPrev { get; set; }
        public string PedOrig { get; set; }
        public int? IdCartera { get; set; }
        public string CvePais { get; set; }
        public string CveEdo { get; set; }
        public string CveMpio { get; set; }
        public string CveConf { get; set; }
        public string CFDI { get; set; }
        public string Veh { get; set; }
        public int? FPago { get; set; }
        public decimal? CtoAdFte { get; set; }
        public string Incoterm { get; set; }
        public decimal? TcFte { get; set; }
        public decimal? PesoTeor { get; set; }
        public string UsrDoc { get; set; }
        public DateTime? FchCap { get; set; }

        // Campos adicionales
        public TimeSpan? HrPgEntrega { get; set; }
        public string BasFol { get; set; }
        public int? NroComentXPart { get; set; }
        public int? NroCarComentXPart { get; set; }
        public int? SucOrigen { get; set; }
        public string TpMov { get; set; }
        public string NCli { get; set; }
        public string ClCli { get; set; }
        public string ColCli { get; set; }
        public string PobCli { get; set; }
        public int? CentroCostos { get; set; }
        public bool EnPresupuesto { get; set; }
        public Decimal? Flete { get; set; }
        public string UsrDep { get; set; }
        public Decimal? Par { get; set; }
        public DateTime? FchRef { get; set; }
        public decimal? SaldoDoc { get; set; }
        public string Stat { get; set; }
        public string CveProy { get; set; }
        public string CvaBco { get; set; }
        public string CveCli { get; set; }
        public string DestCh { get; set; }
        public int? NPers { get; set; }
        public decimal? MtoAntic { get; set; }
        public decimal? MtExtra1 { get; set; }
        public decimal? MtExtra2 { get; set; }
        public decimal? MtExtra3 { get; set; }
        public decimal? MtExtra4 { get; set; }
        public decimal? MtExtra5 { get; set; }
        public decimal? MtExtra6 { get; set; }
        public decimal? MtExtra7 { get; set; }
        public decimal? MtExtra8 { get; set; }
        public decimal? MtExtra9 { get; set; }
        public decimal? MtExtra10 { get; set; }
        public string Mdp { get; set; }
        public string ComentAut { get; set; }
        public string Variacion { get; set; }
        public DateTime? FechaEdicion { get; set; }
        public string EditadoPor { get; set; }
        public string GenDocPadreChar { get; set; }
        public string NatDocPadreChar { get; set; }
        public string TipoProducto { get; set; }
        public string TipoPoceso { get; set; }
        public string FolDocPadreChar { get; set; }
        public string DocPadreCompl { get; set; }
        public string CveVehEmb { get; set; }
        public int? Usr0 { get; set; }
        public DateTime? Fch0 { get; set; }
        public int? Usr1 { get; set; }
        public DateTime? Fch1 { get; set; }
        public string Firma1 {get; set;}
        public string Firma0 { get; set; }
        public int? Usr2 { get; set; }
        public DateTime? Fch2 { get; set; }
        public int? Usr3 { get; set; }
        public DateTime? Fch3 { get; set; }
        public int? Usr4 { get; set; }
        public DateTime? Fch4 { get; set; }
        public int? Usr5 { get; set; }
        public DateTime? Fch5 { get; set; }
        public int? Usr6 { get; set; }
        public string Firma6 { get; set; }
        public DateTime? Fch6 { get; set; }
        public int? EncabezadoPadre { get; set; }
        public int Estatus { get; set; }
        public bool EsServicio { get; set; }
        public bool TienePendientes { get; set; }

        public string Folio { get; set; }
    }

}
