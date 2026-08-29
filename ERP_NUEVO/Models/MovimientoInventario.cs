using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class MovimientoInventario
    {
        [Range(1, int.MaxValue, ErrorMessage = "ProductoId debe ser mayor que 0")]
        public int ProductoId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "TipoMovimientoId debe ser mayor que 0")]
        public int TipoMovimientoId { get; set; }

        [Range(0.001, double.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0")]
        public decimal Cantidad { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "AlmacenId debe ser mayor que 0")]
        public int AlmacenId { get; set; }

        [MaxLength(100)]
        public string ReferenciaExterna { get; set; }

        [MaxLength(100)]
        public string OrigenProceso { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "UsuarioId debe ser mayor que 0")]
        public int UsuarioId { get; set; }

        [MaxLength(500)]
        public string Observaciones { get; set; }

        public int? ResponsivaId { get; set; }
    }

}
