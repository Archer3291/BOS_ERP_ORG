using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class DatosXML
    {
        public string Version { get; set; }
        public string Serie { get; set; }
        public string Folio { get; set; }
        public string Fecha { get; set; }
        public string SubTotal { get; set; }
        public string Total { get; set; }
        public string Moneda { get; set; }
        public string TipoCambio { get; set; }
        public string EmisorRFC { get; set; }
        public string EmisorNombre { get; set; }
        public string ReceptorRFC { get; set; }
        public string ReceptorNombre { get; set; }
    }
}
