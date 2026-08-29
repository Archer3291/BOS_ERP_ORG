namespace BOS_ERP.Models
{
    /// <summary>
    /// Un renglón capturado en el módulo de facturación libre.
    ///
    /// Puede venir de dos lados y por eso todos los campos fiscales viajan explícitos: si el
    /// usuario eligió un producto del catálogo, la pantalla ya los trajo resueltos desde
    /// catrelacion; si lo escribió a mano, los capturó él. El servidor no distingue entre
    /// ambos casos —los valida igual— pero sí revalida contra los catálogos del SAT antes de
    /// timbrar, porque el navegador no es una fuente confiable.
    /// </summary>
    public class ConceptoFacturaLibre
    {
        /// <summary>Clave interna del producto. Vacía cuando el concepto se capturó libre.</summary>
        public string CveProd { get; set; } = string.Empty;

        /// <summary>ClaveProdServ del SAT (c_ClaveProdServ).</summary>
        public string ClaveProdServ { get; set; } = string.Empty;

        /// <summary>ClaveUnidad del SAT (c_ClaveUnidad).</summary>
        public string ClaveUnidad { get; set; } = string.Empty;

        /// <summary>Unidad legible que se imprime en el comprobante (PZA, SERVICIO…).</summary>
        public string Unidad { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public decimal Cantidad { get; set; }

        public decimal PrecioUnit { get; set; }

        /// <summary>Descuento en porcentaje sobre el importe del renglón.</summary>
        public decimal Descuento { get; set; }

        /// <summary>
        /// ObjetoImp del SAT: 01 no objeto, 02 sí objeto, 03 sí objeto y no obligado al desglose.
        /// Por omisión 02, que es el caso común de una factura con IVA trasladado.
        /// </summary>
        public string ObjetoImp { get; set; } = "02";

        /// <summary>Importe bruto del renglón, antes de descuento.</summary>
        public decimal Bruto => Math.Round(Cantidad * PrecioUnit, 2);

        /// <summary>Importe del descuento del renglón.</summary>
        public decimal ImporteDescuento => Math.Round(Bruto * Descuento / 100m, 2);

        /// <summary>Importe neto del renglón, ya con el descuento aplicado.</summary>
        public decimal Neto => Bruto - ImporteDescuento;
    }
}
