using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class TraspasoEmailViewModel
    {
        public string FolioTraspaso { get; set; }
        public string FolioCotizacion { get; set; }
        public int SucursalDestino { get; set; }
        public List<ProductoTraspasoItem> Productos { get; set; } = new List<ProductoTraspasoItem>();
    }

    public class ProductoTraspasoItem
    {
        public string ProductoId { get; set; }
        public string Descripcion { get; set; }
        public string Faltante { get; set; }
    }
}
