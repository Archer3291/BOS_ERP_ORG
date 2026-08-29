using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BOS_ERP.Models
{
    public class MovimientoDetalle
    {
        [JsonProperty("id_tarima_producto")]
        public int? IdTarimaProducto { get; set; }

        [JsonProperty("id_tarima_producto_origen")]
        public int? IdTarimaProductoOrigen { get; set; }

        [JsonProperty("producto_id")]
        public int ProductoId { get; set; }

        [JsonProperty("pedimento")]
        public string Pedimento { get; set; }

        [JsonProperty("cantidad")]
        public decimal Cantidad { get; set; }

        [JsonProperty("tipo")]
        public string Tipo { get; set; }
    }
}
