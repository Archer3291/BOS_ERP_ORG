namespace BOS_ERP.Models
{
    /// <summary>
    /// Datos que se imprimen en el ticket de mostrador del punto de venta.
    /// </summary>
    public class TicketVenta
    {
        public int IdVenta { get; set; }
        public string Folio { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string Sucursal { get; set; } = "";
        public string Cajero { get; set; } = "";

        public string ClienteClave { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public string ClienteRfc { get; set; } = "";
        public string Observaciones { get; set; } = "";

        public List<TicketPartida> Partidas { get; set; } = new();

        /// <summary>Material que el cliente todavía no se lleva.</summary>
        public List<TicketPartida> Pendientes { get; set; } = new();

        public List<TicketPago> Pagos { get; set; } = new();

        public decimal Subtotal { get; set; }
        public decimal Descuento { get; set; }
        public decimal Iva { get; set; }
        public decimal Total { get; set; }

        public decimal TotalPagado { get; set; }
        public decimal Cambio { get; set; }

        /// <summary>
        /// true cuando el cobro cubrió la venta completa, incluido el material que queda
        /// pendiente de entregar. false cuando sólo se cobró lo que el cliente se llevó.
        /// </summary>
        public bool PagadoCompleto { get; set; }

        public bool HayPendientes { get; set; }

        /// <summary>Importe que se cobrará al entregar lo pendiente. Cero si ya se pagó.</summary>
        public decimal SaldoPorCobrar { get; set; }

        /// <summary>
        /// Venta a crédito: no entró dinero a la caja y el importe quedó en la cartera
        /// del cliente. El ticket no imprime formas de pago sino el saldo y su plazo.
        /// </summary>
        public bool EsCredito { get; set; }

        /// <summary>Días de plazo del cliente, para avisar cuándo vence el saldo.</summary>
        public int PlazoDias { get; set; }

        public string FolioFactura { get; set; } = "";
        public string UuidFactura { get; set; } = "";
        public bool FacturaCancelada { get; set; }

        public bool TieneFactura => !string.IsNullOrWhiteSpace(UuidFactura);
    }

    public class TicketPartida
    {
        public string Clave { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public decimal Cantidad { get; set; }
        public string Unidad { get; set; } = "";
        public decimal PrecioUnitario { get; set; }
        public decimal Descuento { get; set; }
        public decimal Importe { get; set; }
    }

    public class TicketPago
    {
        public string Metodo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal Monto { get; set; }
    }
}
