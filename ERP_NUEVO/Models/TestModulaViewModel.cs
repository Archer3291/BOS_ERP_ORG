using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class TestModulaViewModel
    {
        public string Usuario { get; set; }
        public string Password { get; set; }
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public string Unidad { get; set; }
        public string Resultado { get; set; }
        public bool ResultadoExito { get; set; }
        public string OrdenNumero { get; set; }
        public string OrdenDescripcion { get; set; }
        public string Cantidad { get; set; }
        public string Lote { get; set; }   // RIG_SUB1
        public string LineaHost { get; set; }   // RIG_HOSTINF
        public string Nota { get; set; }   // RIG_REQ_NOTE  (Maintenance)
        public string Prioridad { get; set; }   // RIG_PRIO      (Maintenance)
    }
}
