using BOS_ERP.Helpers;
using Npgsql;

namespace BOS_ERP.Services
{
    /// <summary>De dónde salen los documentos que mueven puntos.</summary>
    [Flags]
    public enum OrigenVentas
    {
        Ninguno = 0,
        Kepler = 1,
        Erp = 2,
        Ambos = Kepler | Erp
    }

    /// <summary>
    /// Hecho económico que mueve puntos. No es "un documento": es la razón por la que el
    /// saldo cambia, que es justo lo que distingue emitir una factura de cobrarla.
    /// Los valores coinciden con el CHECK de puntos_movimientos.evento.
    /// </summary>
    public static class EventoPuntos
    {
        public const string FacturaContado = "factura_contado";
        public const string ComplementoPago = "complemento_pago";
        public const string Anticipo = "anticipo";
        public const string NotaCredito = "nota_credito";
        public const string Cancelacion = "cancelacion";
    }

    /// <summary>
    /// Qué ES un documento, que no es lo mismo que qué registra. Un vocabulario ÚNICO
    /// para los dos orígenes: si el ERP dijera `factura_contado` y Kepler `contado` para
    /// la misma cosa, la pantalla tendría que traducir dos juegos de nombres y la mitad
    /// de las etiquetas se quedaría sin traducción ni color.
    /// </summary>
    public static class ClaseDocumento
    {
        public const string Contado = "contado";
        public const string Credito = "credito";
        public const string Anticipo = "anticipo";
        public const string NotaCredito = "nota_credito";
        public const string ComplementoPago = "complemento_pago";
        public const string Cancelacion = "cancelacion";
        public const string Omitir = "omitir";
    }

    /// <summary>
    /// El subconjunto de clases que el operador puede elegir para un documento de Kepler.
    /// En kdm1 no hay columna que diga si una venta fue de contado o a crédito, ni existen
    /// complementos de pago ni factura_anticipos: son datos que ese sistema nunca guardó.
    /// Así que para el histórico la clasificación la pone una persona, documento por
    /// documento. Complemento y cancelación no están porque no se pueden deducir de kdm1
    /// ni el operador tiene con qué afirmarlas.
    /// </summary>
    public static class ClaseKepler
    {
        public const string Contado = ClaseDocumento.Contado;
        public const string Credito = ClaseDocumento.Credito;
        public const string Anticipo = ClaseDocumento.Anticipo;
        public const string NotaCredito = ClaseDocumento.NotaCredito;
        public const string Omitir = ClaseDocumento.Omitir;

        public static bool EsValida(string c) =>
            c is Contado or Credito or Anticipo or NotaCredito or Omitir;
    }

    /// <summary>Un movimiento de puntos que el cálculo propone registrar.</summary>
    public sealed class MovimientoPuntos
    {
        /// <summary>"kepler" o "erp".</summary>
        public string Origen { get; init; } = "";

        /// <summary>Ver <see cref="EventoPuntos"/>.</summary>
        public string Evento { get; set; } = "";

        /// <summary>acumulacion (+) | devolucion (−) | reverso (−).</summary>
        public string TipoMovimiento { get; set; } = "acumulacion";

        /// <summary>
        /// Identidad del evento, y la única garantía de que no se registre dos veces. Se
        /// guarda en puntos_movimientos.doc_clave, que tiene índice único.
        /// </summary>
        public string Clave { get; set; } = "";

        /// <summary>encabezadomov.id_encabezado del documento que causó el movimiento.</summary>
        public int Ref { get; init; }

        /// <summary>Factura que el complemento paga, o que la nota de crédito afecta.</summary>
        public int RefRelacionada { get; set; }

        /// <summary>Folio legible del documento relacionado, para la pantalla.</summary>
        public string FolioRelacionado { get; set; } = "";

        /// <summary>Total del documento relacionado; base de la proporción de una nota.</summary>
        public decimal ImporteRelacionado { get; set; }

        public string Sucursal { get; init; } = "";
        public string Genero { get; init; } = "";
        public string Naturaleza { get; init; } = "";
        public decimal Grupo { get; init; }
        public decimal Tipo { get; init; }
        public string Folio { get; init; } = "";
        public DateTime Fecha { get; init; }

        /// <summary>Importe del documento o del pago, con impuestos.</summary>
        public decimal Importe { get; init; }

        /// <summary>
        /// Base sobre la que se calculan los puntos: el importe SIN IVA, ya neto de lo que
        /// no debe puntuar (por ejemplo la parte de una factura cubierta con un anticipo
        /// que ya otorgó puntos por su cuenta).
        ///
        /// Sin IVA porque es lo que hace el sitio web —verificado contra FacturaPuntos: la
        /// factura 0046816 con total 1,853.49 e IVA 255.65 otorgó 30 puntos, que salen de
        /// 1,597.84 × 0.01875— y porque el IVA se entera al SAT: premiarlo regala un 16%
        /// de puntos sobre un impuesto ajeno.
        /// </summary>
        public decimal Base { get; set; }

        public string Comentario { get; init; } = "";

        public decimal Tasa { get; set; }

        /// <summary>Puntos con signo: positivo acumula, negativo resta.</summary>
        public decimal Puntos { get; set; }

        /// <summary>Por qué otorga lo que otorga, o por qué no otorga nada.</summary>
        public string Motivo { get; set; } = "";

        /// <summary>
        /// Cómo se clasificó el documento: en Kepler lo elige el operador y en el ERP se
        /// deduce de factura.tipo.
        ///
        /// NO es lo mismo que <see cref="Evento"/>, y la diferencia importa en pantalla.
        /// `Evento` es lo que se registra en el ledger, y una factura a crédito no registra
        /// nada: se le pone `factura_contado` como marcador de posición porque el CHECK de
        /// la base sólo admite los cinco eventos reales. Si la etiqueta saliera de ahí, una
        /// venta a crédito se vería como "Contado" —que es justo lo contrario de lo que
        /// pasó— aunque el cálculo la trate bien.
        /// </summary>
        public string Clase { get; set; } = "";

        /// <summary>Ya existe este movimiento en el ledger.</summary>
        public bool YaRegistrado { get; set; }

        public bool Mueve => Puntos != 0;
    }

    public sealed class ResultadoCalculoPuntos
    {
        public bool Exito { get; init; } = true;
        public string Mensaje { get; init; } = "";
        public string Clasificacion { get; init; } = "";
        public List<MovimientoPuntos> Movimientos { get; init; } = new();

        public IEnumerable<MovimientoPuntos> Nuevos => Movimientos.Where(m => !m.YaRegistrado);

        public decimal PuntosNuevos => Nuevos.Sum(m => m.Puntos);
        public decimal PuntosYaRegistrados => Movimientos.Where(m => m.YaRegistrado).Sum(m => m.Puntos);

        public decimal PorEvento(string evento) => Nuevos.Where(m => m.Evento == evento).Sum(m => m.Puntos);
    }

    /// <summary>
    /// Cálculo de puntos del programa Socio Tiburón.
    ///
    /// LA REGLA QUE ORDENA TODO: los puntos siguen al DINERO RECIBIDO, no a la
    /// facturación. De ahí salen las siete reglas del programa sin tener que memorizarlas
    /// una por una:
    ///
    ///   Factura de contado ....... otorga. El dinero entra al emitirla.
    ///   Factura a crédito ........ no otorga. Todavía no hay dinero.
    ///   Complemento de pago ...... otorga. Es el momento en que ese dinero entra.
    ///   Anticipo recibido ........ otorga. También es dinero que entró.
    ///   Factura que aplica un
    ///     anticipo ............... no otorga por esa parte: el anticipo ya la pagó y ya
    ///                              otorgó. Se descuenta de su base.
    ///   Nota de crédito .......... resta, en proporción a lo que la factura relacionada
    ///                              otorgó de verdad.
    ///   Cancelación de factura ... reversa lo que ese documento haya otorgado.
    ///
    /// DOS ORÍGENES. Las ventas históricas viven en Kepler (sellosop.kdm1) y las nuevas en
    /// el ERP. En el ERP las siete reglas se resuelven solas, porque hay tablas que dicen
    /// qué es cada documento: factura.tipo, factura_anticipos, factura_complementos_pago,
    /// factura_relacion. En Kepler no existe ninguna de esas tablas —kdm1 no distingue
    /// contado de crédito ni registró pagos— así que la clasificación de cada documento la
    /// pone el operador desde la pantalla, y queda escrita en el movimiento que genera.
    ///
    /// Tres diferencias deliberadas contra la aplicación de escritorio que este módulo
    /// reemplaza:
    ///
    ///  1. POR DOCUMENTO, no sobre la suma del periodo. Es lo que ya hace el sitio. Sobre
    ///     la suma, los puntos de una factura dependerían de qué otras entraron en la
    ///     corrida, y recalcular otro rango daría otro número para la misma venta.
    ///
    ///  2. SIN IVA. Ver el comentario de MovimientoPuntos.Base.
    ///
    ///  3. SIN CANCELADOS. La consulta de totales de la aplicación no filtraba c43 = 'C'
    ///     —sólo lo hacían las rejillas de detalle—, así que las cancelaciones venían
    ///     otorgando puntos.
    ///
    /// El servicio NO escribe: devuelve lo que movería. Guardar es decisión del
    /// controlador, para que siempre se pueda revisar antes de aplicar.
    /// </summary>
    public class PuntosCalculoService
    {
        private readonly string _cadenaKepler;
        private readonly string _cadenaErp;

        // Un documento por debajo de este importe no otorga puntos (regla original). Se
        // evalúa contra el importe del evento: el total de la factura, el monto del pago o
        // el del anticipo. Los movimientos negativos no lo aplican: si una venta otorgó
        // puntos, devolverla tiene que quitarlos aunque la devolución sea chica.
        private const decimal ImporteMinimo = 200m;

        // Facturas del ERP que representan una venta al cliente, más las naturalezas de
        // nota de crédito. Se excluyen a propósito GLFAC (público en general, sin cliente
        // identificado), RICD (carga inicial de cartera) y FAR (arrendamiento). CPFAC no
        // está aquí porque los complementos se leen por su tabla de aplicaciones y no por
        // el encabezado: un solo complemento puede liquidar varias facturas.
        private const string NatsDocumentoErp =
            "'VIFAC','VNFAC','VSFAC','VINFAC','FACLIB','NT','VINC'";

        private const string LogTag = "PuntosCalculo";

        public PuntosCalculoService(IConfiguration config)
        {
            _cadenaKepler = config.GetConnectionString("SRS") ?? "";
            _cadenaErp = config.GetConnectionString("ERP_SRS") ?? "";
        }

        /// <summary>
        /// Calcula los movimientos de un cliente en un rango, sin guardar nada.
        /// </summary>
        /// <param name="clasesKepler">
        /// Clasificación que el operador dio a cada documento de Kepler, indexada por la
        /// clave del movimiento. Lo que no venga se trata como contado, que es como se
        /// comportaba el módulo antes de existir esta pantalla.
        /// </param>
        public async Task<ResultadoCalculoPuntos> CalcularAsync(
            string cveCli, DateTime desde, DateTime hasta, string clasificacion,
            OrigenVentas origenes, IDictionary<string, string> clasesKepler = null)
        {
            if (string.IsNullOrWhiteSpace(cveCli))
                return Error("Falta la clave del cliente.");

            if (string.IsNullOrWhiteSpace(clasificacion))
                return Error("Falta la clasificación del cliente.");

            if (origenes == OrigenVentas.Ninguno)
                return Error("Selecciona al menos un origen de ventas.");

            try
            {
                var tarifas = await LeerTarifasAsync(clasificacion);

                if (tarifas.Count == 0)
                    return Error($"No hay tarifas configuradas para la clasificación '{clasificacion}'.");

                cveCli = cveCli.Trim();

                var movimientos = new List<MovimientoPuntos>();
                var notas = new List<MovimientoPuntos>();

                // ── 1. Lo que suma ──────────────────────────────────────────
                if (origenes.HasFlag(OrigenVentas.Kepler))
                {
                    var kepler = ClasificarKepler(await LeerDeKeplerAsync(cveCli, desde, hasta), clasesKepler);

                    movimientos.AddRange(kepler.Where(m => m.Evento != EventoPuntos.NotaCredito));
                    notas.AddRange(kepler.Where(m => m.Evento == EventoPuntos.NotaCredito));
                }

                if (origenes.HasFlag(OrigenVentas.Erp))
                {
                    var (positivos, notasErp) = await LeerDelErpAsync(cveCli, desde, hasta);
                    movimientos.AddRange(positivos);
                    notas.AddRange(notasErp);
                }

                foreach (var m in movimientos)
                    AplicarTarifa(m, tarifas);

                // Antes de las notas, no después: el cálculo de una nota necesita saber qué
                // movimientos de esta corrida ya están guardados, porque los guardados ya
                // vienen contados en lo que el ledger reporta como otorgado. Marcarlos
                // después haría que se sumaran dos veces y la nota restaría el doble.
                await MarcarYaRegistradosAsync(movimientos.Concat(notas).ToList());

                // ── 2. Notas de crédito ─────────────────────────────────────
                // Van después porque no se calculan con una tarifa: restan la proporción de
                // lo que su factura otorgó de verdad, y eso incluye lo que se acaba de
                // calcular en esta misma corrida —una factura de contado y su nota de
                // crédito pueden caer en el mismo periodo y todavía no estar guardadas.
                if (notas.Count > 0)
                {
                    var otorgado = await LeerOtorgadoPorFacturaAsync(cveCli);
                    AcumularCalculoEnCurso(movimientos, otorgado);

                    // Las notas ya registradas conservan lo que restaron en su día. Volver a
                    // calcularlas daría otro número —el ledger ya trae su propia resta
                    // descontada del disponible— y la pantalla mostraría una cifra que no es
                    // la que el cliente tiene aplicada.
                    // Por fecha: dos notas contra la misma factura se aplican en el orden en
                    // que ocurrieron, y cada una descuenta del disponible que dejó la
                    // anterior. Sin orden ni descuento, dos notas del 100% restarían el 200%
                    // de lo que la factura llegó a otorgar.
                    foreach (var nc in notas.Where(n => !n.YaRegistrado).OrderBy(n => n.Fecha))
                        AplicarNotaDeCredito(nc, otorgado, tarifas);

                    movimientos.AddRange(notas);
                }

                // ── 3. Cancelaciones ────────────────────────────────────────
                // Deliberadamente SIN filtro de fecha: una factura de enero puede cancelarse
                // en marzo, y si el reverso sólo apareciera al reconciliar enero, nadie
                // volvería a mirar ese periodo y los puntos se quedarían puestos para
                // siempre. Es una corrección del saldo, no una venta.
                if (origenes.HasFlag(OrigenVentas.Erp))
                    movimientos.AddRange(await LeerCancelacionesAsync(cveCli));

                var ordenados = movimientos
                    .OrderBy(m => m.Fecha).ThenBy(m => m.Folio).ThenBy(m => m.Evento)
                    .ToList();

                await MarcarYaRegistradosAsync(ordenados);

                return new ResultadoCalculoPuntos
                {
                    Exito = true,
                    Clasificacion = clasificacion,
                    Movimientos = ordenados
                };
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"Error al calcular puntos: {ex.Message}", nivel: "ERROR");

                return Error("No se pudieron calcular los puntos: " + ex.Message);
            }
        }

        private static ResultadoCalculoPuntos Error(string mensaje) =>
            new() { Exito = false, Mensaje = mensaje };

        // ════════════════════════════════════════════════════════════════
        // Origen: Kepler (histórico, clasificado a mano)
        // ════════════════════════════════════════════════════════════════

        private async Task<List<MovimientoPuntos>> LeerDeKeplerAsync(
            string cveCli, DateTime desde, DateTime hasta)
        {
            var documentos = new List<MovimientoPuntos>();

            await using var conn = new NpgsqlConnection(_cadenaKepler);
            await conn.OpenAsync();

            // Mismos predicados que la aplicación de escritorio: ventas (c2='U', c3='D') de
            // los grupos y tipos que participan. c4 y c5 son numeric en el origen, así que
            // se comparan como números. c14 es el IVA y c16 el total.
            //
            // EL CTE DE SUCURSALES NO ES ADORNO: SIN ÉL LA CONSULTA TARDA DOS MINUTOS.
            // kdm1 pesa 2 GB y sus cinco índices empiezan TODOS por c1 (la sucursal), que
            // es justo la columna por la que este cálculo no filtra —los puntos de un
            // cliente son los de todas sus compras, comprara donde comprara—. Un índice
            // btree sólo sirve desde su primera columna, así que Postgres terminaba
            // recorriendo el índice entero: 19,179 páginas, 111 s en frío. Como el
            // servidor cachea lo leído, el segundo o tercer intento sí entraba en los
            // 30 s de espera de Npgsql y "se arreglaba solo"; en frío volvía a fallar con
            // "Exception while reading from stream", que es como Npgsql reporta que se
            // acabó el tiempo mientras leía.
            //
            // El CTE recursivo es un *loose index scan*: salta de un valor distinto de c1
            // al siguiente por el índice primario —doce saltos, uno por sucursal— y con
            // esa lista la consulta ya puede entrar por sindkdm103 (c1, c10, c9, …) con
            // una búsqueda exacta por sucursal. Medido: 99 páginas y 0.9 ms contra 19,179
            // y 111,591 ms. Se sacan de la tabla y no de una lista fija a propósito: dar
            // de alta una sucursal nueva no puede hacer que sus ventas dejen de sumar
            // puntos en silencio.
            await using var cmd = new NpgsqlCommand(@"
                WITH RECURSIVE sucursales AS (
                    (SELECT c1 FROM sellosop.kdm1 ORDER BY c1 LIMIT 1)
                    UNION ALL
                    SELECT (SELECT k.c1
                              FROM sellosop.kdm1 k
                             WHERE k.c1 > s.c1
                             ORDER BY k.c1
                             LIMIT 1)
                      FROM sucursales s
                     WHERE s.c1 IS NOT NULL
                )
                SELECT c1, c2, c3, c4, c5, c6, c9,
                       c16                       AS total,
                       c16 - COALESCE(c14, 0)    AS base,
                       COALESCE(c24, '')         AS comentario
                FROM sellosop.kdm1
                WHERE c1 IN (SELECT c1 FROM sucursales WHERE c1 IS NOT NULL)
                  AND c2 = 'U'
                  AND c3 = 'D'
                  AND c10 = @cliente
                  AND c4 IN (5, 7)
                  AND c5 IN (10, 1)
                  AND c9 >= @desde
                  AND c9 <  @hasta
                  AND COALESCE(c43, '') <> 'C'
                ORDER BY c9, c6", conn);

            cmd.Parameters.AddWithValue("cliente", cveCli);
            cmd.Parameters.AddWithValue("desde", desde.Date);
            cmd.Parameters.AddWithValue("hasta", hasta.Date.AddDays(1));

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string suc = Txt(reader, "c1"), gen = Txt(reader, "c2"), nat = Txt(reader, "c3");
                decimal gpo = Num(reader, "c4"), tp = Num(reader, "c5");
                string folio = Txt(reader, "c6");

                documentos.Add(new MovimientoPuntos
                {
                    Origen = "kepler",

                    // La clave NO incluye el evento, a propósito: un documento de Kepler
                    // acumula una sola vez, y si el operador se equivocó al clasificarlo la
                    // corrección es un ajuste con motivo, no un segundo movimiento que
                    // conviva con el primero.
                    Clave = $"kepler|{suc}|{gen}|{nat}|{gpo}|{tp}|{folio}",

                    Sucursal = suc,
                    Genero = gen,
                    Naturaleza = nat,
                    Grupo = gpo,
                    Tipo = tp,
                    Folio = folio,
                    Fecha = Convert.ToDateTime(reader["c9"]),
                    Importe = Num(reader, "total"),
                    Base = Num(reader, "base"),
                    Comentario = Txt(reader, "comentario")
                });
            }

            return documentos;
        }

        /// <summary>
        /// Traduce la clasificación del operador a evento y signo. Sin clasificación se
        /// asume contado, que es lo que el módulo hacía con TODO documento de Kepler antes
        /// de existir esta pantalla: así el default no cambia ningún saldo ya otorgado.
        /// </summary>
        private static List<MovimientoPuntos> ClasificarKepler(
            List<MovimientoPuntos> documentos, IDictionary<string, string> clases)
        {
            foreach (var d in documentos)
            {
                string clase = clases != null
                               && clases.TryGetValue(d.Clave, out string elegida)
                               && ClaseKepler.EsValida(elegida)
                    ? elegida
                    : ClaseKepler.Contado;

                d.Clase = clase;

                switch (clase)
                {
                    case ClaseKepler.Anticipo:
                        d.Evento = EventoPuntos.Anticipo;
                        d.TipoMovimiento = "acumulacion";
                        d.Motivo = "Marcado como anticipo recibido.";
                        break;

                    case ClaseKepler.NotaCredito:
                        d.Evento = EventoPuntos.NotaCredito;
                        d.TipoMovimiento = "devolucion";
                        break;

                    case ClaseKepler.Credito:
                        d.Evento = EventoPuntos.FacturaContado;
                        d.Base = 0m;
                        d.Motivo = "Marcado como venta a crédito: los puntos se otorgan al cobrarla.";
                        break;

                    case ClaseKepler.Omitir:
                        d.Evento = EventoPuntos.FacturaContado;
                        d.Base = 0m;
                        d.Motivo = "Excluido por el operador.";
                        break;

                    default:
                        d.Evento = EventoPuntos.FacturaContado;
                        d.TipoMovimiento = "acumulacion";
                        break;
                }
            }

            return documentos;
        }

        // ════════════════════════════════════════════════════════════════
        // Origen: ERP (las siete reglas se resuelven solas)
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve por separado lo que suma y las notas de crédito, porque las notas no se
        /// calculan con una tarifa sino contra lo que su factura otorgó.
        /// </summary>
        private async Task<(List<MovimientoPuntos> Positivos, List<MovimientoPuntos> Notas)>
            LeerDelErpAsync(string cveCli, DateTime desde, DateTime hasta)
        {
            var positivos = new List<MovimientoPuntos>();
            var notas = new List<MovimientoPuntos>();

            await using var conn = new NpgsqlConnection(_cadenaErp);
            await conn.OpenAsync();

            await LeerFacturasErpAsync(conn, cveCli, desde, hasta, positivos, notas);
            await LeerComplementosErpAsync(conn, cveCli, desde, hasta, positivos);
            await ResolverFacturasDeNotasAsync(conn, notas);

            return (positivos, notas);
        }

        /// <summary>
        /// Facturas, anticipos y notas de crédito: los tres salen de la misma tabla y se
        /// distinguen por factura.tipo, que el flujo de ventas escribe con lo que el usuario
        /// eligió ('CONTADO' | 'CREDITO' | 'ANTICIPO' | 'NC_…').
        /// </summary>
        private async Task LeerFacturasErpAsync(
            NpgsqlConnection conn, string cveCli, DateTime desde, DateTime hasta,
            List<MovimientoPuntos> positivos, List<MovimientoPuntos> notas)
        {
            // El anticipo aplicado se resta de la base de la factura para no pagar dos veces
            // el mismo dinero: cuando ese anticipo se recibió ya otorgó puntos por su cuenta.
            // Va neto porque una nota de crédito puede desaplicarlo, y esa desaplicación se
            // guarda como monto_aplicado negativo.
            await using var cmd = new NpgsqlCommand($@"
                SELECT em.id_encabezado, em.suc, em.gen, em.nat,
                       em.nro_gpo_doc, em.nro_tp_doc, em.folio,
                       f.id                                       AS factura_id,
                       f.fecha,
                       f.total                                    AS total,
                       f.total - COALESCE(f.iva, 0)               AS base,
                       UPPER(COALESCE(f.tipo, ''))                AS tipo_fac,
                       UPPER(COALESCE(f.mdpfactura, ''))          AS mdp,
                       COALESCE(em.coment1, '')                   AS comentario,
                       COALESCE((SELECT SUM(fa.monto_aplicado)
                                   FROM factura_anticipos fa
                                  WHERE fa.id_factura_principal = f.id), 0) AS anticipo_aplicado
                FROM factura f
                INNER JOIN encabezadomov em ON em.id_encabezado = f.encabezado_id
                INNER JOIN catclientes c    ON c.id_cliente     = em.refe
                WHERE c.cve_cli = @cliente
                  AND em.nat IN ({NatsDocumentoErp})
                  AND f.uuid IS NOT NULL
                  AND LOWER(COALESCE(f.statusfactura, '')) NOT LIKE '%cancel%'
                  AND f.fecha >= @desde
                  AND f.fecha <  @hasta
                ORDER BY f.fecha, em.folio", conn);

            cmd.Parameters.AddWithValue("cliente", cveCli);
            cmd.Parameters.AddWithValue("desde", desde.Date);
            cmd.Parameters.AddWithValue("hasta", hasta.Date.AddDays(1));

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int encId = Convert.ToInt32(reader["id_encabezado"]);
                string tipoFac = Txt(reader, "tipo_fac");
                string nat = Txt(reader, "nat");
                decimal total = Num(reader, "total");
                decimal baseSinIva = Num(reader, "base");
                decimal anticipoAplicado = Num(reader, "anticipo_aplicado");

                // La aplicación de un anticipo se timbra en este ERP como una nota de crédito
                // de tipo APLICACION_ANTICIPO sobre la factura. No es una devolución: no
                // salió dinero, sólo se acreditó el que ya había entrado. Restarla quitaría
                // puntos dos veces, porque la factura ya viene neteada por factura_anticipos.
                if (tipoFac == "APLICACION_ANTICIPO")
                    continue;

                bool esNota = tipoFac.StartsWith("NC")
                              || nat.Equals("NT", StringComparison.OrdinalIgnoreCase)
                              || nat.Equals("VINC", StringComparison.OrdinalIgnoreCase);

                // Sólo por factura.tipo. La columna factura.anticipo parecería el lugar
                // natural, pero ningún flujo la escribe: FacturaRepository la guarda desde
                // Factura.Anticipo, que nadie pone en true, así que vale 0 siempre. Usarla
                // daría un criterio que nunca dispara y escondería el error.
                bool esAnticipo = tipoFac == "ANTICIPO";

                // PPD es "pago en parcialidades o diferido", el método de una venta a
                // crédito. mdpfactura guarda la descripción y no la clave, así que se
                // reconoce por texto, igual que en Cobranza de contado.
                string mdp = Txt(reader, "mdp");
                bool esCredito = tipoFac == "CREDITO"
                                 || (tipoFac.Length == 0
                                     && (mdp == "PPD"
                                         || mdp.Contains("PARCIALIDADES")
                                         || mdp.Contains("DIFERIDO")));

                var m = new MovimientoPuntos
                {
                    Origen = "erp",
                    Ref = encId,
                    Sucursal = Txt(reader, "suc"),
                    Genero = Txt(reader, "gen"),
                    Naturaleza = nat,
                    Grupo = Num(reader, "nro_gpo_doc"),
                    Tipo = Num(reader, "nro_tp_doc"),
                    Folio = Txt(reader, "folio"),
                    Fecha = Convert.ToDateTime(reader["fecha"]),
                    Importe = total,
                    Base = baseSinIva,
                    Comentario = Txt(reader, "comentario")
                };

                if (esNota)
                {
                    m.Evento = EventoPuntos.NotaCredito;
                    m.Clase = ClaseDocumento.NotaCredito;
                    m.TipoMovimiento = "devolucion";
                    m.Clave = $"erp|{EventoPuntos.NotaCredito}|{encId}";
                    notas.Add(m);
                    continue;
                }

                if (esAnticipo)
                {
                    m.Evento = EventoPuntos.Anticipo;
                    m.Clase = ClaseDocumento.Anticipo;
                    m.TipoMovimiento = "acumulacion";
                    m.Clave = $"erp|{EventoPuntos.Anticipo}|{encId}";
                    m.Motivo = "Anticipo recibido: el dinero entró aquí, no en la factura que lo aplique.";
                    positivos.Add(m);
                    continue;
                }

                m.Evento = EventoPuntos.FacturaContado;
                m.Clase = ClaseDocumento.Contado;
                m.TipoMovimiento = "acumulacion";
                m.Clave = $"erp|{EventoPuntos.FacturaContado}|{encId}";

                if (esCredito)
                {
                    // El evento se queda en factura_contado —es el marcador de posición que
                    // el CHECK admite— pero la CLASE dice la verdad, y es la que se pinta.
                    m.Clase = ClaseDocumento.Credito;
                    m.Base = 0m;
                    m.Motivo = "Factura a crédito: no otorga por sí sola, los puntos entran con el complemento de pago.";
                    positivos.Add(m);
                    continue;
                }

                if (anticipoAplicado > 0 && total > 0)
                {
                    // Se descuenta en proporción porque monto_aplicado viene con IVA, igual
                    // que el total: la parte de la base que ese anticipo cubrió es la misma
                    // fracción del documento.
                    decimal cubierto = Math.Min(anticipoAplicado, total);
                    decimal fraccion = cubierto / total;

                    m.Base = Math.Round(baseSinIva * (1m - fraccion), 2, MidpointRounding.AwayFromZero);
                    m.Motivo = fraccion >= 1m
                        ? $"Cubierta en su totalidad con anticipo ({cubierto:C2}), que ya otorgó puntos."
                        : $"Neta de {cubierto:C2} de anticipo ya bonificado; puntúa sólo el resto.";
                }

                positivos.Add(m);
            }
        }

        /// <summary>
        /// Complementos de pago: el momento en que el dinero de una factura a crédito entra
        /// de verdad. Se leen por factura_complementos_pago y no por el encabezado CPFAC
        /// porque un solo complemento puede liquidar varias facturas, y cada aplicación es
        /// un evento distinto con su propio importe.
        /// </summary>
        private async Task LeerComplementosErpAsync(
            NpgsqlConnection conn, string cveCli, DateTime desde, DateTime hasta,
            List<MovimientoPuntos> positivos)
        {
            await using var cmd = new NpgsqlCommand(@"
                SELECT cp.id_encabezado                    AS cp_enc,
                       cp.suc, cp.gen, cp.nat,
                       cp.nro_gpo_doc, cp.nro_tp_doc,
                       cp.folio                            AS cp_folio,
                       fcp.fecha_aplicacion,
                       fcp.monto_aplicado,
                       COALESCE(fcp.observaciones, '')     AS comentario,
                       fe.id_encabezado                    AS fac_enc,
                       fe.folio                            AS fac_folio,
                       fa.total                            AS fac_total,
                       fa.total - COALESCE(fa.iva, 0)      AS fac_base,
                       UPPER(COALESCE(fa.tipo, ''))        AS fac_tipo,
                       UPPER(COALESCE(fa.mdpfactura, ''))  AS fac_mdp
                FROM factura_complementos_pago fcp
                INNER JOIN encabezadomov cp ON cp.id_encabezado   = fcp.id_encabezado_complemento
                INNER JOIN encabezadomov fe ON fe.id_encabezado   = fcp.id_encabezado_factura
                INNER JOIN factura fa       ON fa.encabezado_id   = fe.id_encabezado
                INNER JOIN catclientes c    ON c.id_cliente       = fe.refe
                LEFT  JOIN factura cpf      ON cpf.encabezado_id  = cp.id_encabezado
                WHERE c.cve_cli = @cliente
                  AND fcp.monto_aplicado > 0
                  AND LOWER(COALESCE(cpf.statusfactura, '')) NOT LIKE '%cancel%'
                  AND LOWER(COALESCE(fa.statusfactura, ''))  NOT LIKE '%cancel%'
                  AND fcp.fecha_aplicacion >= @desde
                  AND fcp.fecha_aplicacion <  @hasta
                ORDER BY fcp.fecha_aplicacion, cp.folio", conn);

            cmd.Parameters.AddWithValue("cliente", cveCli);
            cmd.Parameters.AddWithValue("desde", desde.Date);
            cmd.Parameters.AddWithValue("hasta", hasta.Date.AddDays(1));

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int cpEnc = Convert.ToInt32(reader["cp_enc"]);
                int facEnc = Convert.ToInt32(reader["fac_enc"]);
                decimal monto = Num(reader, "monto_aplicado");
                decimal facTotal = Num(reader, "fac_total");
                decimal facBase = Num(reader, "fac_base");
                string facTipo = Txt(reader, "fac_tipo");
                string facMdp = Txt(reader, "fac_mdp");

                var m = new MovimientoPuntos
                {
                    Origen = "erp",
                    Evento = EventoPuntos.ComplementoPago,
                    Clase = ClaseDocumento.ComplementoPago,
                    TipoMovimiento = "acumulacion",

                    // La factura pagada entra en la clave: el mismo complemento liquida
                    // varias y cada aplicación tiene que poder registrarse por separado.
                    Clave = $"erp|{EventoPuntos.ComplementoPago}|{cpEnc}|{facEnc}",

                    Ref = cpEnc,
                    RefRelacionada = facEnc,
                    FolioRelacionado = Txt(reader, "fac_folio"),
                    ImporteRelacionado = facTotal,
                    Sucursal = Txt(reader, "suc"),
                    Genero = Txt(reader, "gen"),
                    Naturaleza = Txt(reader, "nat"),
                    Grupo = Num(reader, "nro_gpo_doc"),
                    Tipo = Num(reader, "nro_tp_doc"),
                    Folio = Txt(reader, "cp_folio"),
                    Fecha = Convert.ToDateTime(reader["fecha_aplicacion"]),
                    Importe = monto,
                    Comentario = Txt(reader, "comentario")
                };

                // Un complemento sobre una factura que ya cobró al emitirse pagaría el mismo
                // dinero dos veces. No debería existir —el SAT no pide complemento para una
                // PUE— pero si alguien lo timbra, aquí no otorga.
                bool facturaYaCobrada =
                    facTipo == "CONTADO"
                    || (facTipo.Length == 0 && (facMdp == "PUE" || facMdp.Contains("UNA SOLA")));

                if (facturaYaCobrada)
                {
                    m.Base = 0m;
                    m.Motivo = $"La factura {m.FolioRelacionado} es de contado y ya otorgó puntos al emitirse.";
                    positivos.Add(m);
                    continue;
                }

                if (facTotal <= 0)
                {
                    m.Base = 0m;
                    m.Motivo = "La factura pagada no tiene importe con el que separar el IVA.";
                    positivos.Add(m);
                    continue;
                }

                // El IVA del pago se separa en la misma proporción que trae la factura, en
                // lugar de dividir entre 1.16: así sale bien también con partidas exentas, a
                // tasa 0 o con retenciones.
                m.Base = Math.Round(monto * (facBase / facTotal), 2, MidpointRounding.AwayFromZero);
                m.Motivo = $"Pago recibido de la factura {m.FolioRelacionado}.";

                positivos.Add(m);
            }
        }

        /// <summary>
        /// Liga cada nota de crédito con la factura que afecta. La vía formal es
        /// factura_relacion, que es donde el timbrado guarda los CFDI relacionados; cuando
        /// no hay renglón ahí se cae a encabezados_padre, que el flujo de ventas sí puebla.
        /// Una nota sin factura identificable no queda fuera: se calcula con tarifa propia.
        /// </summary>
        private static async Task ResolverFacturasDeNotasAsync(
            NpgsqlConnection conn, List<MovimientoPuntos> notas)
        {
            if (notas.Count == 0) return;

            var porEncabezado = notas
                .Where(n => n.Ref > 0)
                .GroupBy(n => n.Ref)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (porEncabezado.Count == 0) return;

            await using var cmd = new NpgsqlCommand(@"
                SELECT nc_em.id_encabezado         AS nc_enc,
                       origen_em.id_encabezado     AS fac_enc,
                       origen_em.folio             AS fac_folio,
                       origen.total                AS fac_total
                FROM encabezadomov nc_em
                INNER JOIN factura nc ON nc.encabezado_id = nc_em.id_encabezado
                LEFT  JOIN factura_relacion fr ON fr.factura_relacionada_id = nc.id
                LEFT  JOIN factura origen
                       ON origen.id = fr.factura_origen_id
                       OR (fr.factura_origen_id IS NULL
                           AND origen.encabezado_id = NULLIF(nc_em.encabezados_padre, 0))
                LEFT  JOIN encabezadomov origen_em ON origen_em.id_encabezado = origen.encabezado_id
                WHERE nc_em.id_encabezado = ANY(@notas)
                  AND origen.id IS NOT NULL", conn);

            cmd.Parameters.AddWithValue("notas", porEncabezado.Keys.ToArray());

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int ncEnc = Convert.ToInt32(reader["nc_enc"]);

                if (!porEncabezado.TryGetValue(ncEnc, out var destino)) continue;

                foreach (var nota in destino)
                {
                    // Una nota puede relacionar varias facturas. Se queda con la primera:
                    // repartir la resta entre todas necesitaría el desglose por CFDI, que el
                    // ERP no guarda, y proporcionar contra una sola es lo que ya hace el
                    // descuento de saldo al timbrarla.
                    if (nota.RefRelacionada > 0) continue;

                    nota.RefRelacionada = Convert.ToInt32(reader["fac_enc"]);
                    nota.FolioRelacionado = Txt(reader, "fac_folio");
                    nota.ImporteRelacionado = Num(reader, "fac_total");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Notas de crédito
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Una nota de crédito resta la proporción de lo que su factura otorgó DE VERDAD, no
        /// lo que le tocaría por tarifa propia.
        ///
        /// La diferencia importa: una nota sobre una factura a crédito que todavía nadie ha
        /// pagado restaría puntos que jamás se otorgaron, y dejaría al cliente con saldo
        /// negativo por una venta que nunca lo premió. El tope contra lo que sigue en pie
        /// cubre además el caso de dos notas sobre la misma factura.
        /// </summary>
        private static void AplicarNotaDeCredito(
            MovimientoPuntos nc,
            IDictionary<int, (decimal Bruto, decimal Neto)> otorgado,
            List<Tarifa> tarifas)
        {
            if (nc.RefRelacionada <= 0)
            {
                // Sin factura identificable no hay contra qué proporcionar; se cae a la
                // tarifa propia, que es lo más cercano al importe que la nota devuelve. Es
                // también el único camino posible para las notas de Kepler, donde kdm1 no
                // guarda a qué documento afecta cada una.
                var tarifaSuelta = Escalon(tarifas, nc.Base);

                if (tarifaSuelta is null)
                {
                    nc.Puntos = 0m;
                    nc.Motivo = "Nota de crédito sin factura relacionada y sin escalón de tarifa aplicable.";
                    return;
                }

                nc.Tasa = tarifaSuelta.Tasa;
                nc.Puntos = -Math.Round(nc.Base * tarifaSuelta.Tasa, 0, MidpointRounding.AwayFromZero);
                nc.Motivo = "Nota de crédito sin factura relacionada: se resta con tarifa propia.";
                return;
            }

            var (bruto, neto) = otorgado.TryGetValue(nc.RefRelacionada, out var y) ? y : (0m, 0m);

            if (bruto <= 0 || neto <= 0)
            {
                nc.Puntos = 0m;
                nc.Motivo = $"La factura {nc.FolioRelacionado} no ha otorgado puntos: no hay nada que restar.";
                return;
            }

            decimal proporcion = nc.ImporteRelacionado > 0
                ? Math.Min(1m, nc.Importe / nc.ImporteRelacionado)
                : 1m;

            decimal resta = Math.Min(
                Math.Round(bruto * proporcion, 0, MidpointRounding.AwayFromZero), neto);

            nc.Puntos = -resta;

            // Lo que esta nota se lleva deja de estar disponible para la siguiente. El bruto
            // no se toca: la proporción siempre se mide contra lo que la factura llegó a
            // otorgar, no contra lo que va quedando.
            otorgado[nc.RefRelacionada] = (bruto, neto - resta);
            nc.Motivo = proporcion >= 1m
                ? $"Cancela por completo la factura {nc.FolioRelacionado}: se retiran sus {bruto:N0} puntos."
                : $"Devuelve {proporcion:P0} de la factura {nc.FolioRelacionado}, que otorgó {bruto:N0} puntos.";
        }

        /// <summary>
        /// Suma al acumulado por factura lo que se acaba de calcular y todavía no está
        /// guardado. Sin esto, una factura de contado y su nota de crédito emitidas en el
        /// mismo periodo se verían así: la factura otorga 30 puntos y la nota resta 0,
        /// porque el ledger aún no sabe de los 30.
        /// </summary>
        private static void AcumularCalculoEnCurso(
            List<MovimientoPuntos> movimientos,
            IDictionary<int, (decimal Bruto, decimal Neto)> otorgado)
        {
            foreach (var m in movimientos)
            {
                if (m.YaRegistrado || m.Origen != "erp" || m.Puntos <= 0) continue;

                int factura = m.Evento == EventoPuntos.ComplementoPago ? m.RefRelacionada : m.Ref;
                if (factura <= 0) continue;

                var actual = otorgado.TryGetValue(factura, out var y) ? y : (Bruto: 0m, Neto: 0m);
                otorgado[factura] = (actual.Bruto + m.Puntos, actual.Neto + m.Puntos);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Cancelaciones
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Movimientos cuyo documento aparece cancelado en el ERP y todavía no tienen su
        /// reverso. Se exige el estatus exacto 'Cancelada': 'Pendiente Cancelación' y 'Error
        /// al Cancelar' no son cancelaciones consumadas, y reversar por ellas quitaría
        /// puntos de una factura que puede seguir viva.
        /// </summary>
        private async Task<List<MovimientoPuntos>> LeerCancelacionesAsync(string cveCli)
        {
            var reversos = new List<MovimientoPuntos>();

            await using var conn = new NpgsqlConnection(_cadenaErp);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                SELECT m.doc_clave, m.evento, m.puntos, m.doc_ref, m.doc_ref_rel,
                       m.doc_sucursal, m.doc_genero, m.doc_naturaleza,
                       m.doc_grupo, m.doc_tipo, m.doc_folio, m.doc_fecha,
                       m.doc_importe, m.doc_base, m.tasa_aplicada
                FROM puntos_movimientos m
                INNER JOIN factura f ON f.encabezado_id = m.doc_ref
                WHERE m.cve_cli = @cliente
                  AND m.origen  = 'erp'
                  AND m.tipo IN ('acumulacion', 'devolucion')
                  AND m.puntos <> 0
                  AND m.doc_clave IS NOT NULL
                  AND LOWER(TRIM(COALESCE(f.statusfactura, ''))) = 'cancelada'
                  AND NOT EXISTS (SELECT 1
                                    FROM puntos_movimientos r
                                   WHERE r.doc_clave = @prefijo || m.doc_clave)
                ORDER BY m.doc_fecha", conn);

            cmd.Parameters.AddWithValue("cliente", cveCli);
            cmd.Parameters.AddWithValue("prefijo", $"erp|{EventoPuntos.Cancelacion}|");

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string claveOriginal = Txt(reader, "doc_clave");
                decimal puntosOriginales = Num(reader, "puntos");

                reversos.Add(new MovimientoPuntos
                {
                    Origen = "erp",
                    Evento = EventoPuntos.Cancelacion,
                    Clase = ClaseDocumento.Cancelacion,
                    TipoMovimiento = "reverso",
                    Clave = $"erp|{EventoPuntos.Cancelacion}|{claveOriginal}",
                    Ref = reader["doc_ref"] is DBNull ? 0 : Convert.ToInt32(reader["doc_ref"]),
                    RefRelacionada = reader["doc_ref_rel"] is DBNull ? 0 : Convert.ToInt32(reader["doc_ref_rel"]),
                    Sucursal = Txt(reader, "doc_sucursal"),
                    Genero = Txt(reader, "doc_genero"),
                    Naturaleza = Txt(reader, "doc_naturaleza"),
                    Grupo = Num(reader, "doc_grupo"),
                    Tipo = Num(reader, "doc_tipo"),
                    Folio = Txt(reader, "doc_folio"),
                    Fecha = reader["doc_fecha"] is DateTime fd ? fd : DateTime.Now,
                    Importe = Num(reader, "doc_importe"),
                    Base = Num(reader, "doc_base"),
                    Tasa = Num(reader, "tasa_aplicada"),

                    // Signo contrario exacto: se deshace lo que se otorgó, no se recalcula.
                    // Si las tarifas cambiaron desde entonces, recalcular dejaría un residuo
                    // que nadie podría explicar.
                    Puntos = -puntosOriginales,

                    // Cancelar una nota de crédito DEVUELVE puntos, así que el texto no
                    // puede dar por hecho que un reverso siempre quita.
                    Motivo = puntosOriginales >= 0
                        ? $"El documento fue cancelado; se retiran los {puntosOriginales:N0} puntos que otorgó."
                        : $"La nota de crédito fue cancelada; se devuelven los {Math.Abs(puntosOriginales):N0} puntos que había restado."
                });
            }

            return reversos;
        }

        // ════════════════════════════════════════════════════════════════
        // Tarifas
        // ════════════════════════════════════════════════════════════════

        private sealed record Tarifa(decimal Min, decimal? Max, decimal Tasa);

        private async Task<List<Tarifa>> LeerTarifasAsync(string clasificacion)
        {
            var tarifas = new List<Tarifa>();

            await using var conn = new NpgsqlConnection(_cadenaErp);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                SELECT importe_min, importe_max, tasa
                FROM puntos_tarifas
                WHERE UPPER(clasificacion) = UPPER(@clasificacion)
                  AND activo = true
                  AND vigente_desde <= CURRENT_DATE
                ORDER BY importe_min", conn);

            cmd.Parameters.AddWithValue("clasificacion", clasificacion.Trim());

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tarifas.Add(new Tarifa(
                    Num(reader, "importe_min"),
                    reader["importe_max"] is DBNull ? null : Num(reader, "importe_max"),
                    Num(reader, "tasa")));
            }

            return tarifas;
        }

        private static Tarifa Escalon(List<Tarifa> tarifas, decimal baseImponible) =>
            tarifas.FirstOrDefault(t =>
                baseImponible >= t.Min && (t.Max is null || baseImponible <= t.Max));

        /// <summary>
        /// El escalón sale del importe que de verdad puntúa —el pago, el anticipo, o lo que
        /// quede de la factura después del anticipo— y no del tamaño de la venta completa.
        /// </summary>
        private static void AplicarTarifa(MovimientoPuntos m, List<Tarifa> tarifas)
        {
            if (m.Base <= 0m)
            {
                m.Puntos = 0m;
                if (m.Motivo.Length == 0)
                    m.Motivo = "Sin base que puntúe.";
                return;
            }

            // El umbral se evalúa sobre el importe con impuestos, como la regla original
            // (c16 > 200); el escalón y la tasa, sobre la base sin IVA.
            if (m.Importe <= ImporteMinimo)
            {
                m.Puntos = 0m;
                m.Motivo = $"Importe menor o igual a {ImporteMinimo:C2}";
                return;
            }

            var tarifa = Escalon(tarifas, m.Base);

            if (tarifa is null)
            {
                m.Puntos = 0m;
                m.Motivo = "La base no cae en ningún escalón de tarifa";
                return;
            }

            m.Tasa = tarifa.Tasa;

            // Entero: los puntos son unidades canjeables. Verificado contra FacturaPuntos,
            // donde 1,597.84 × 0.01875 = 29.96 quedó registrado como 30.
            m.Puntos = Math.Round(m.Base * tarifa.Tasa, 0, MidpointRounding.AwayFromZero);
        }

        // ════════════════════════════════════════════════════════════════
        // Qué dice ya el ledger
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cuántos puntos lleva otorgados cada factura. `Bruto` es lo que sumó y `Neto` lo
        /// que queda después de notas de crédito y reversos anteriores: una nota nunca puede
        /// restar más de lo que sigue en pie.
        ///
        /// Se agrupa por doc_ref_rel antes que por doc_ref porque los puntos de una factura
        /// a crédito no cuelgan de ella sino de los complementos que la pagaron.
        /// </summary>
        private async Task<Dictionary<int, (decimal Bruto, decimal Neto)>>
            LeerOtorgadoPorFacturaAsync(string cveCli)
        {
            var otorgado = new Dictionary<int, (decimal Bruto, decimal Neto)>();

            await using var conn = new NpgsqlConnection(_cadenaErp);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                SELECT factura,
                       SUM(GREATEST(puntos, 0)) AS bruto,
                       SUM(puntos)              AS neto
                FROM (
                    SELECT COALESCE(NULLIF(m.doc_ref_rel, 0), m.doc_ref) AS factura,
                           m.puntos
                    FROM puntos_movimientos m
                    WHERE m.cve_cli = @cliente
                      AND m.origen  = 'erp'
                      AND COALESCE(NULLIF(m.doc_ref_rel, 0), m.doc_ref) IS NOT NULL
                ) t
                GROUP BY factura", conn);

            cmd.Parameters.AddWithValue("cliente", cveCli);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                otorgado[Convert.ToInt32(reader["factura"])] = (Num(reader, "bruto"), Num(reader, "neto"));

            return otorgado;
        }

        /// <summary>
        /// Marca lo que ya está en el ledger. Sin esto, la pantalla mostraría como "por
        /// registrar" movimientos que el cliente ya tiene aplicados.
        /// </summary>
        private async Task MarcarYaRegistradosAsync(List<MovimientoPuntos> movimientos)
        {
            var claves = movimientos.Select(m => m.Clave)
                                    .Where(c => !string.IsNullOrEmpty(c))
                                    .Distinct()
                                    .ToArray();

            if (claves.Length == 0) return;

            await using var conn = new NpgsqlConnection(_cadenaErp);
            await conn.OpenAsync();

            // Sin filtrar por cliente a propósito: la clave es única en toda la tabla, así
            // que un documento registrado bajo otro cliente hay que verlo como registrado
            // igual —insertarlo chocaría contra el índice— en vez de ofrecerlo de nuevo.
            await using var cmd = new NpgsqlCommand(
                "SELECT doc_clave, puntos FROM puntos_movimientos WHERE doc_clave = ANY(@claves)", conn);

            cmd.Parameters.AddWithValue("claves", claves);

            var existentes = new Dictionary<string, decimal>(StringComparer.Ordinal);

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    existentes[reader["doc_clave"]?.ToString() ?? ""] = Num(reader, "puntos");
            }

            foreach (var m in movimientos)
            {
                if (!existentes.TryGetValue(m.Clave, out decimal registrados)) continue;

                m.YaRegistrado = true;

                // Se muestra lo que el cliente tiene aplicado, no lo que hoy valdría. Si las
                // tarifas cambiaron desde entonces los dos números difieren, y el relevante
                // para quien consulta la pantalla es el que está en el saldo.
                m.Puntos = registrados;
            }
        }

        // ════════════════════════════════════════════════════════════════

        private static string Txt(NpgsqlDataReader r, string col) =>
            r[col]?.ToString()?.Trim() ?? "";

        private static decimal Num(NpgsqlDataReader r, string col) =>
            r[col] is null or DBNull ? 0m : Convert.ToDecimal(r[col]);
    }
}
