using Microsoft.AspNetCore.Http;

namespace BOS_ERP.Models
{
    /// <summary>
    /// Contexto mínimo compartido por los flujos de facturación de Ventas
    /// (VI/VN/VIN/VS): transporta el formulario recibido y el id del encabezado
    /// recién creado hacia GenerarFacturaVentas / generación de nota de crédito.
    /// Antes estaba duplicado idéntico en cada controlador de factura.
    /// </summary>
    public class NCContext
    {
        public IFormCollection Form { get; set; }
        public int EncabezadoId { get; set; }
    }
}
