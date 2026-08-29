namespace BOS_ERP.Models
{
    public class PolizaDetalle
    {
        public int Cuenta { get; set; }
        public int? Centro { get; set; }
        public decimal? Debe { get; set; }
        public decimal? Haber { get; set; }
        public string Descripcion { get; set; }
    }
}