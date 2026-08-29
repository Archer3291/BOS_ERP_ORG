namespace BOS_ERP.Models
{
    public class Impuesto
    {
        public int IdImpuesto {get; set;}
        public decimal Subtotal { get; set; }
        public decimal Importe { get; set; }
        public decimal ImpVariable { get; set; }
        public string Clave { get; set; }
        public bool EsRetencion { get; set; }
    }
}