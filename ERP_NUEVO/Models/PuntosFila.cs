namespace BOS_ERP.Models
{
    /// <summary>
    /// Un renglón de la tabla de puntos, tal como lo consume la pantalla.
    ///
    /// Existe como clase y no como objeto anónimo porque se guarda en sesión entre
    /// peticiones: la tabla es paginada del lado del servidor, y recalcular en cada
    /// cambio de página o tecla del buscador significaría volver a consultar Kepler y
    /// el ERP —cinco consultas, una de ellas a otro servidor— por cada pulsación.
    /// El cálculo se hace una vez al pulsar "Calcular" y de ahí se pagina.
    /// </summary>
    public sealed class PuntosFila
    {
        /// <summary>Identidad del movimiento; es también la llave de fila de la tabla.</summary>
        public string Clave { get; set; } = "";

        /// <summary>"kepler" o "erp".</summary>
        public string Origen { get; set; } = "";

        /// <summary>Lo que se registraría en el ledger.</summary>
        public string Evento { get; set; } = "";

        /// <summary>
        /// Cómo se clasificó el documento. Es lo que se pinta como etiqueta, y no siempre
        /// coincide con <see cref="Evento"/>: una factura a crédito lleva evento
        /// `factura_contado` —marcador de posición, porque no registra nada— pero su clase
        /// es `credito`, que es lo que el usuario tiene que ver.
        /// </summary>
        public string Clase { get; set; } = "";

        /// <summary>acumulacion | devolucion | reverso.</summary>
        public string Tipo { get; set; } = "";

        public string Sucursal { get; set; } = "";
        public string Folio { get; set; } = "";

        /// <summary>Factura pagada por el complemento, o afectada por la nota de crédito.</summary>
        public string FolioRelacionado { get; set; } = "";

        /// <summary>Formateada para mostrar; para ordenar se usa <see cref="FechaOrden"/>.</summary>
        public string Fecha { get; set; } = "";

        /// <summary>
        /// La fecha como fecha. Ordenar por el texto "dd/MM/yyyy" agruparía por día del mes
        /// y pondría el 01/12/2025 antes que el 02/01/2024.
        /// </summary>
        public DateTime FechaOrden { get; set; }

        public decimal Importe { get; set; }
        public decimal BaseSinIva { get; set; }
        public decimal Tasa { get; set; }
        public decimal Puntos { get; set; }

        public bool Mueve { get; set; }
        public bool YaRegistrado { get; set; }

        public string Motivo { get; set; } = "";
    }
}
