using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Npgsql;

namespace BOS_ERP.Controllers.Soporte
{
    /// <summary>
    /// Provoca errores a propósito para comprobar que la captura automática
    /// funciona de punta a punta: huella, deduplicación, cortacircuitos y ticket.
    ///
    /// SÓLO EXISTE EN DESARROLLO.
    /// Cada acción verifica el entorno y devuelve 404 fuera de él. Un endpoint que
    /// revienta la aplicación a voluntad no tiene por qué ser alcanzable en
    /// producción, ni siquiera detrás de un login.
    /// </summary>
    [Authorize]
    [Route("Diagnostico/Errores")]
    public class DiagnosticoErroresController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public DiagnosticoErroresController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private bool Permitido => _env.IsDevelopment();

        /// <summary>Excepción normal que escapa: la debe atrapar el middleware.</summary>
        [HttpGet("Reventar")]
        public IActionResult Reventar(string nota = null)
        {
            if (!Permitido) return NotFound();

            throw new InvalidOperationException(
                $"Error de prueba de la captura automática. Nota: {nota ?? "(ninguna)"}");
        }

        /// <summary>
        /// Error de Postgres: comprueba que el payload guarde SqlState, tabla y
        /// constraint, que es lo que más rápido cierra un diagnóstico real.
        /// </summary>
        [HttpGet("ReventarSql")]
        public IActionResult ReventarSql()
        {
            if (!Permitido) return NotFound();

            var utils = new Utilities(true);
            utils.RunQuery("SELECT * FROM tabla_que_no_existe_prueba_captura");
            return Ok();
        }

        /// <summary>
        /// Dos errores con el MISMO origen pero folios distintos en el mensaje.
        /// Deben colapsar en una sola huella: es la prueba de que la normalización
        /// hace su trabajo y no se generan dos tickets.
        /// </summary>
        [HttpGet("ProbarDeduplicacion")]
        public IActionResult ProbarDeduplicacion()
        {
            if (!Permitido) return NotFound();

            var servicio = new ErrorTicketService();
            var folios = new List<string>();

            foreach (int n in new[] { 4471, 4472, 4473 })
            {
                var ex = new InvalidOperationException($"No existe el folio {n} en el documento");
                var e = ErrorCapturado.Desde(ex, "controlador", "Diagnostico/Deduplicacion");
                e.Origen = "DiagnosticoErroresController.cs:99";   // fijo: simula el mismo punto de falla
                e.Ruta = "/Diagnostico/Errores/ProbarDeduplicacion";
                e.Metodo = "GET";
                e.UsuarioId = HttpContext.Session.GetInt32("UsuarioId");

                folios.Add(servicio.Registrar(e) ?? "(sin ticket)");
            }

            return Json(new
            {
                folios,
                esperado = "Los tres folios deben ser IDÉNTICOS: una sola huella, un solo ticket con 3 ocurrencias."
            });
        }

        /// <summary>
        /// Comprueba que el cortacircuitos DE VERDAD corte.
        ///
        /// Es la guarda que impide que una caída de Postgres genere miles de tickets
        /// en minutos, y hasta ahora nunca se había visto disparar. La prueba genera
        /// errores distintos -cada uno con su huella- hasta rebasar el tope, y
        /// verifica las dos mitades del contrato:
        ///
        ///   * pasado el tope YA NO se crean tickets;
        ///   * pero la telemetría se sigue registrando, que es lo que permite saber
        ///     qué pasó durante el incidente.
        ///
        /// EXIGE BAJAR EL TOPE ANTES. Con el valor de producción (20) esta prueba
        /// dejaría 20 tickets basura, así que se niega a correr si no está en 5 o
        /// menos.
        ///
        /// El tope se baja en appsettings.json, NO en appsettings.Development.json:
        /// Utilities.ConstruirConfiguracion() arma su propio IConfiguration con un
        /// solo AddJsonFile("appsettings.json"), sin la capa por entorno que pone el
        /// host. Todo CapturaErrores:* se lee por ahí, así que un valor puesto en el
        /// archivo de Development se ignora en silencio. Y como el base path es
        /// AppDomain.CurrentDomain.BaseDirectory -o sea bin-, hace falta recompilar
        /// para que el cambio llegue: reiniciar desde el IDE basta.
        /// </summary>
        [HttpGet("ProbarCortacircuitos")]
        public IActionResult ProbarCortacircuitos()
        {
            if (!Permitido) return NotFound();

            var utils = new Utilities(true);
            int tope = utils._configuration.GetValue("CapturaErrores:TicketsPorHora", 20);

            if (tope > 5)
            {
                return Json(new
                {
                    error = $"El tope está en {tope}. Esta prueba crearía {tope} tickets basura.",
                    comoCorregir = "Pon \"CapturaErrores\": { \"TicketsPorHora\": 2 } en " +
                                   "appsettings.json (NO en appsettings.Development.json: " +
                                   "Utilities arma su configuración con ese solo archivo y " +
                                   "la capa por entorno se ignora), reinicia y vuelve a llamar.",
                    alTerminar = "Devuelve el tope a 20 y corre sql/soporte_errores_limpiar_pruebas.sql."
                });
            }

            var servicio = new ErrorTicketService();
            var resultados = new List<IntentoCortacircuitos>();

            // Marca única por corrida: sin ella, una segunda ejecución reutilizaría
            // las huellas de la primera y no se crearía ningún ticket nuevo, que
            // parecería un corte cuando en realidad es deduplicación.
            string corrida = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Un par de más que el tope, para ver el corte suceder.
            for (int i = 1; i <= tope + 2; i++)
            {
                var ex = new InvalidOperationException($"Prueba de cortacircuitos {corrida} caso {i}");
                var e = ErrorCapturado.Desde(ex, "controlador", "Diagnostico/Cortacircuitos");

                // Origen distinto por iteración: así cada error es una huella nueva
                // y no se deduplican entre sí.
                e.Origen = $"Cortacircuitos.cs:{i}";
                e.Ruta = "/Diagnostico/Errores/ProbarCortacircuitos";
                e.Metodo = "GET";
                e.UsuarioId = HttpContext.Session.GetInt32("UsuarioId");

                string folio = servicio.Registrar(e);
                resultados.Add(new IntentoCortacircuitos
                {
                    Intento = i,
                    Folio = folio ?? "(cortado)",
                    HuboTicket = folio != null
                });
            }

            int conTicket = resultados.Count(r => r.HuboTicket);

            // La telemetría tiene que estar completa aunque los tickets se hayan
            // cortado: ésa es la mitad del contrato que de verdad importa.
            var registradas = RunScalarInt(utils,
                "SELECT COUNT(*) FROM tkt_error_huella WHERE mensaje_norm LIKE @patron",
                new Dictionary<string, object> { { "patron", $"%{corrida}%" } });

            return Json(new
            {
                tope,
                intentos = resultados.Count,
                ticketsCreados = conTicket,
                huellasRegistradas = registradas,
                resultados,
                veredicto = (conTicket <= tope && registradas == resultados.Count)
                    ? "OK: el cortacircuitos limitó los tickets y aun así se registró todo."
                    : "REVISAR: no cuadra. Se esperaban <= " + tope + " tickets y " +
                      resultados.Count + " huellas."
            });
        }

        private static int RunScalarInt(Utilities utils, string sql, Dictionary<string, object> p)
            => Convert.ToInt32(utils.RunScalar(sql, p));

        /// <summary>Un intento de la prueba del cortacircuitos.</summary>
        public sealed class IntentoCortacircuitos
        {
            public int Intento { get; set; }
            public string Folio { get; set; }
            public bool HuboTicket { get; set; }
        }

        /// <summary>Lo que la captura lleva registrado, para revisarlo sin abrir un cliente de SQL.</summary>
        [HttpGet("Estado")]
        public IActionResult Estado()
        {
            if (!Permitido) return NotFound();

            var utils = new Utilities(true);

            var huellas = utils.RunQuery(
                "SELECT h.id_huella, h.tipo_exc, h.mensaje_norm, h.origen, " +
                "       h.ocurrencias, h.afectados, h.silenciada, " +
                "       h.primera_vez, h.ultima_vez, t.folio_tkt, st.n AS estado " +
                "FROM tkt_error_huella h " +
                "LEFT JOIN tkts t      ON t.id_tkts = h.id_tkt " +
                "LEFT JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt " +
                "ORDER BY h.ultima_vez DESC LIMIT 25");

            var enUltimaHora = utils.RunScalar(
                "SELECT COUNT(*) FROM tkts t " +
                "JOIN tkts_categorias c ON c.id_cat = t.id_cat " +
                "WHERE c.n_cat = 'Errores del sistema' " +
                "  AND t.fch_crea > now() - interval '1 hour'",
                new Dictionary<string, object>());

            // Las cuatro condiciones que hacen que CrearTicket devuelva null. Sin
            // esto, "se registró la huella pero no salió ticket" obliga a adivinar
            // entre cortacircuitos, categoría, responsable y catálogo de prioridades.
            int tope = utils._configuration.GetValue("CapturaErrores:TicketsPorHora", 20);
            int usados = Convert.ToInt32(enUltimaHora);

            var categoria = utils.RunQuery(
                "SELECT id_cat, responsable FROM tkts_categorias WHERE n_cat = 'Errores del sistema' LIMIT 1");

            var responsable = categoria.Count == 0 ? null : utils.RunQuery(
                "SELECT u.usuarioid, u.nombreusuario FROM tkts_categorias c " +
                "JOIN usuarios u ON u.usuarioid = c.responsable WHERE c.id_cat = @id",
                new Dictionary<string, object> { { "id", categoria[0]["id_cat"] } });

            var prioridades = utils.RunQuery("SELECT id_prio, n FROM prio ORDER BY id_prio");

            var motivos = new List<string>();
            if (!utils._configuration.GetValue("CapturaErrores:Activo", true))
                motivos.Add("CapturaErrores:Activo esta en false: no se registra nada.");
            if (usados >= tope)
                motivos.Add($"CORTACIRCUITOS ABIERTO: {usados} tickets en la ultima hora, tope {tope}. " +
                            "Se sigue registrando la huella pero NO se crean tickets.");
            if (categoria.Count == 0)
                motivos.Add("Falta la categoria 'Errores del sistema'. Corre sql/soporte_errores_auto.sql.");
            if (categoria.Count > 0 && (responsable == null || responsable.Count == 0))
                motivos.Add("La categoria apunta a un responsable que no existe en usuarios.");
            if (prioridades.Count == 0)
                motivos.Add("El catalogo prio esta vacio: sin prioridad no se puede insertar el ticket.");

            return Json(new
            {
                huellas,
                cortacircuitos = new { tope, usados, abierto = usados >= tope },
                categoria = categoria.Count > 0 ? categoria[0] : null,
                responsable = responsable != null && responsable.Count > 0 ? responsable[0] : null,
                prioridades,
                porQueNoHayTicket = motivos.Count > 0 ? motivos : new List<string> { "Nada lo impide." },
                nota = "Si una huella aparece con folio_tkt en null, la causa esta en porQueNoHayTicket."
            });
        }
    }
}
