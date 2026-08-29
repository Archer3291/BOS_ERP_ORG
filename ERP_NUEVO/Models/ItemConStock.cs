using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class ItemConStock
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public decimal QuantityOriginal { get; set; }  // lo que pidió el cliente
        public decimal QuantityAvailable { get; set; } // stock real
        public decimal QuantityToDeliver { get; set; } // lo que se va a entregar ahora
        public decimal QuantityPending { get; set; }   // lo que queda pendiente
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public string Unidad { get; set; }
        public int ProductoId { get; set; }
        public bool HasStockIssue => QuantityAvailable < QuantityOriginal;
    }
}
