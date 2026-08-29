namespace BOS_ERP.Models
{
    /// <summary>
    /// Regla de precio/descuento sugerida para un producto en cotizaciones
    /// (Barras / Industriales). Antes estaba duplicada idéntica en
    /// VTCotizacion y VICotizacion.
    /// </summary>
    public class ReglaProductoInfo
    {
        public decimal PrecioFinal { get; set; }
        public decimal PrecioMinimo { get; set; }
        public decimal DescuentoSugerido { get; set; }
        public decimal DescuentoMaximo { get; set; }
        public bool AplicarAutomatico { get; set; }
        public string TipoRegla { get; set; }
    }
}
