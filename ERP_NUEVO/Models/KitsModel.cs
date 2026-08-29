using System.Collections.Generic;

namespace BOS_ERP.Models
{
    public class MaterialModel
    {
        public bool EsKit { get; set; }
        public int ProductoId { get; set; }
        public decimal Cantidad { get; set; }
        public int Unidad { get; set; }
        public int? Orden { get; set; }
    }

    public class KitsModel
    {
        public int KitId { get; set; }
        public decimal Cantidad { get; set; }
        public List<MaterialModel> Materiales { get; set; }
    }
}