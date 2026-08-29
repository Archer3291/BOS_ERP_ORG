using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class ProductoResponsiva
    {
        public int ProductoId { get; set; }
        public decimal Cantidad { get; set; }
        public int AlmacenId { get; set; }
    }

}
