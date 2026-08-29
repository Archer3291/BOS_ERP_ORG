using Newtonsoft.Json;

namespace BOS_ERP.Models
{

    public class RemisionPartidaParaFacturar
    {

        [JsonProperty("remisionId")]
        public int RemisionId { get; set; }

        /// <summary>
        /// id_partidas de partidasdoc. Es nullable porque los modos
        /// "múltiple" / "individual" históricamente enviaban null cuando
        /// se facturaba la remisión completa. Cuando llega null el
        /// controlador lo resuelve por cve_prod contra el tracking.
        /// </summary>
        [JsonProperty("id_partida_remision")]
        public int? IdPartidaRemision { get; set; }

        [JsonProperty("cve_prod")]
        public string CveProd { get; set; }

        [JsonProperty("cantidad_a_facturar")]
        public decimal CantidadAFacturar { get; set; }

        [JsonProperty("precio_unitario")]
        public decimal PrecioUnitario { get; set; }

        [JsonProperty("descuento")]
        public decimal Descuento { get; set; }

        [JsonProperty("esCompleta")]
        public bool EsCompleta { get; set; }

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; }

        [JsonProperty("unidad")]
        public string Unidad { get; set; }

        [JsonProperty("folio_remision")]
        public string FolioRemision { get; set; }
    }
}
