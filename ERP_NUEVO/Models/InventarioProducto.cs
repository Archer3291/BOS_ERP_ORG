using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class InventarioProducto
    {
        public string NoParteSRS { get; set; }
        public string DescripcionProducto { get; set; }
        public string UnidadMedida { get; set; }
        public double TotalAlmacenes { get; set; }
        public double ConsumoMes { get; set; }
        public double PorCubrir { get; set; }
        public double EnFabricacionListo { get; set; }
        public double EnFabricacion { get; set; }
        public double InventarioMaximo { get; set; }
        public double Compra { get; set; }
        public double Peso { get; set; }
        public double PesoTotal { get; set; }
    }

}
