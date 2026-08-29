namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Decide si cancelar una factura debe devolver mercancía al almacén.
    ///
    /// Antes esta decisión era implícita: la cancelación revertía el inventario de todo lo
    /// que encontrara en factura_remisiones_origen. Como esa tabla estaba casi vacía, el
    /// resultado correcto salía por omisión y no por criterio; en cuanto se empezó a poblar
    /// (ver RegistrarOrigenFacturaTotal) habría empezado a revertir donde no debe.
    ///
    /// La regla es por naturaleza del documento de factura, no por lo que haya en la tabla.
    /// </summary>
    public static class ReversionInventarioPolicy
    {
        /// <summary>
        /// Facturas de venta cuya cancelación sí regresa la mercancía: su remisión de origen
        /// es la que descargó el almacén y no hay otro documento que compense la salida.
        /// </summary>
        private static readonly HashSet<string> NatsQueDevuelvenMercancia =
            new(StringComparer.OrdinalIgnoreCase) { "VIFAC", "VNFAC", "VINFAC" };

        /// <summary>
        /// Naturalezas de documento que sí descargan almacén y por tanto pueden revertirse.
        /// Sirve para acotar el respaldo por documento padre de las facturas viejas: evita
        /// que se intente revertir un pedido, una cotización u otra factura.
        /// </summary>
        private static readonly HashSet<string> NatsDeRemision =
            new(StringComparer.OrdinalIgnoreCase) { "VIREM", "VNREM", "VINREM" };

        /// <summary>
        /// <paramref name="motivo"/> explica la decisión para dejarla en la bitácora: cuando
        /// una cancelación no repone existencias conviene saber por qué sin leer el código.
        /// </summary>
        public static bool DevuelveMercancia(string natFactura, out string motivo)
        {
            string nat = (natFactura ?? "").Trim().ToUpperInvariant();

            if (NatsQueDevuelvenMercancia.Contains(nat))
            {
                motivo = $"{nat}: factura de venta con remisión propia.";
                return true;
            }

            motivo = nat switch
            {
                // El complemento de pago no ampara mercancía, sólo el cobro de una factura
                // que sigue vigente.
                "CPFAC" => "CPFAC: complemento de pago, no ampara mercancía.",

                // La nota de crédito es el documento que compensa; su cancelación no puede
                // volver a mover el almacén.
                "NT" or "NC" => $"{nat}: nota de crédito, el ajuste de inventario es de su propio flujo.",

                // Punto de venta: la salida de almacén ocurrió en la VSUC al momento de
                // vender, no en la VSREM. Además la global se cancela para re-emitirla sin
                // las ventas ya facturadas al cliente — un movimiento puramente fiscal.
                // Revertir aquí inflaría las existencias en cada re-facturación.
                // VSPED aparece aquí porque la re-facturación global cuelga el CFDI del
                // pedido que genera, no de un documento de factura.
                "VSFAC" or "GLFAC" or "VSUC" or "VSPED" =>
                    $"{nat}: punto de venta, la salida de almacén es de la venta y la cancelación es fiscal.",

                // Arrendamiento y predial no tocan inventario.
                "FAR" or "RICD" => $"{nat}: servicio, sin movimiento de almacén.",

                // La factura libre se emite sin remisión y por diseño nunca descarga
                // almacén, así que cancelarla no tiene inventario que reponer.
                "FACLIB" => "FACLIB: factura libre, se emite sin remisión y no descarga almacén.",

                _ => $"{nat}: naturaleza sin regla de reversión definida."
            };

            return false;
        }

        public static bool EsRemision(string nat) => NatsDeRemision.Contains((nat ?? "").Trim());
    }
}
