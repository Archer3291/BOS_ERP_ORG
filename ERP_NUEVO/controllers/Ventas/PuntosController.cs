using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas
{
    /// <summary>
    /// Programa de puntos Socio Tiburón: cálculo, otorgamiento y ajustes.
    ///
    /// El principio que ordena todo el módulo: el saldo NUNCA se guarda. Se registran
    /// movimientos —acumulaciones ligadas a su documento, canjes, ajustes— y el saldo
    /// es su suma. Eso es lo que arregla el problema de origen,
    /// donde el saldo vivía en cuatro lugares que no coincidían y cada recálculo lo
    /// sobrescribía.
    ///
    /// Consecuencias prácticas:
    ///   · Recalcular es seguro: un documento sólo acumula una vez, y lo impone un
    ///     índice único, no el cuidado del operador.
    ///   · Todo punto se rastrea hasta la venta que lo generó, y se sabe si vino de
    ///     Kepler o del ERP.
    ///   · Corregir es agregar un ajuste, nunca editar un movimiento.
    /// </summary>
    [RightAuthorize(new[] { "puntos_socio_tiburon", "credito_cobranza" })]
    public class PuntosController : Utilities
    {
        private readonly PuntosCalculoService _calculo;
        private readonly PuntosSyncService _sync;

        private const string LogTag = "PuntosSocioTiburon";

        /// <summary>Ranura de sesión con el último cálculo, para paginar sin recalcular.</summary>
        private const string CacheFilas = "PuntosUltimoCalculo";

        public PuntosController(PuntosCalculoService calculo, PuntosSyncService sync)
        {
            _calculo = calculo;
            _sync = sync;
        }

        public IActionResult Index() => View("~/Views/Ventas/Puntos.cshtml");

        // ════════════════════════════════════════════════════════════════
        // Catálogos
        // ════════════════════════════════════════════════════════════════

        [HttpGet]
        public JsonResult BuscarClientes(string term)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
                    return Json(new { success = true, clientes = new List<object>() });

                var filas = RunQuery(@"
                    SELECT c.id_cliente, c.cve_cli, c.n_cli, c.rfc,
                           COALESCE(s.saldo, 0) AS saldo
                    FROM catclientes c
                    LEFT JOIN v_puntos_saldo s ON s.cliente_id = c.id_cliente
                    WHERE c.empresa_id = @empresa
                      AND (LOWER(c.cve_cli) LIKE LOWER(@term) OR LOWER(c.n_cli) LIKE LOWER(@term))
                    ORDER BY c.n_cli
                    LIMIT 25",
                    new Dictionary<string, object>
                    {
                        { "empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                        { "term",    $"%{term.Trim()}%" }
                    });

                return Json(new
                {
                    success = true,
                    clientes = filas.Select(f => new
                    {
                        id = Convert.ToInt32(f["id_cliente"]),
                        clave = f["cve_cli"]?.ToString(),
                        nombre = f["n_cli"]?.ToString(),
                        rfc = f["rfc"]?.ToString(),
                        saldo = Dec(f["saldo"])
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/BuscarClientes");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult Clasificaciones()
        {
            try
            {
                var filas = RunQuery(@"
                    SELECT clasificacion, COUNT(*) AS escalones
                    FROM puntos_tarifas WHERE activo = true
                    GROUP BY clasificacion ORDER BY clasificacion");

                return Json(new
                {
                    success = true,
                    clasificaciones = filas.Select(f => new
                    {
                        nombre = f["clasificacion"]?.ToString(),
                        escalones = Convert.ToInt32(f["escalones"])
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/Clasificaciones");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Alta del cliente en Socio Tiburón
        //
        // Otorgar puntos a alguien que no existe en Socio Tiburón deja el saldo
        // colgando de una clave que el sitio no sabe a quién pertenece: se guarda
        // el número y no lo ve nadie. Por eso el alta va ANTES, y la pantalla la
        // pide en cuanto se elige al cliente, no al final cuando ya se registró
        // todo y corregirlo cuesta.
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Dice si el cliente ya está en Socio Tiburón y, si no, con qué datos del ERP
        /// se prellena el alta. Nunca falla por culpa del servidor externo: si no
        /// responde devuelve consultado=false y la pantalla no molesta al operador con
        /// un alta que no puede saber si hace falta.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> EstadoSocioTiburon(int clienteId, string cveCli)
        {
            try
            {
                if (clienteId <= 0 && string.IsNullOrWhiteSpace(cveCli))
                    return Json(new { success = false, message = "Falta el cliente." });

                var erp = await _sync.LeerContactoDelErpAsync(clienteId);
                string clave = !string.IsNullOrWhiteSpace(cveCli) ? cveCli.Trim() : erp.CveCli;

                var st = await _sync.ConsultarClienteAsync(clave);

                return Json(new
                {
                    success = true,
                    consultado = st.Consultado,
                    existe = st.Completo,
                    enClientes = st.EnClientes,
                    enUsuarios = st.EnUsuarios,
                    message = st.Mensaje,

                    // La membresía viaja en dos formas a propósito: `membresia` es el
                    // nombre tal cual lo tiene Socio Tiburón, para poder decírselo al
                    // operador, y `clasificacion` es a qué tarifa del ERP equivale, que
                    // es lo único que el selector puede seleccionar.
                    membresia = st.Membresia,
                    clasificacion = ClasificacionDeMembresia(st.Membresia),

                    // Los datos que se ofrecen para editar salen del ERP, que es donde el
                    // capturista los mantiene. Lo que Socio Tiburón ya tenga sólo rellena
                    // los huecos: si allá hay un correo y en el ERP no, ese correo es el
                    // que el cliente usa hoy para entrar y perderlo lo dejaría fuera.
                    datos = new
                    {
                        cveCli = clave,
                        nombre = Primero(erp.Nombre, st.Nombre),
                        telefono = Primero(erp.Telefono, st.Telefono),
                        correo = Primero(erp.Correo, st.Correo)
                    }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/EstadoSocioTiburon");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Da de alta al cliente en Socio Tiburón —padrón y usuario del portal— con los
        /// datos que el operador revisó en pantalla. Es requisito para otorgar puntos.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas", Accion = "Alta de cliente en Socio Tiburón")]
        public async Task<JsonResult> AltaSocioTiburon(
            string cveCli, string nombre, string telefono, string correo, string contrasena)
        {
            try
            {
                var r = await _sync.AltaClienteAsync(cveCli, nombre, telefono, correo, contrasena);

                // La contraseña NO se registra en la bitácora, ni siquiera la inicial:
                // un log con contraseñas es un log que hay que custodiar como si fuera
                // la propia tabla de usuarios.
                if (r.Exito)
                    LogErrorHelper.RegistrarLog(LogTag, cveCli,
                        $"Alta en Socio Tiburón desde la pantalla de puntos: {nombre} / {correo}",
                        User.Identity?.Name, nivel: "INFO");

                return Json(new
                {
                    success = r.Exito,
                    message = r.Mensaje,
                    creoCliente = r.CreoCliente,
                    creoUsuario = r.CreoUsuario,
                    aviso = r.Aviso
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/AltaSocioTiburon");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>El primero que traiga algo; "" si ninguno.</summary>
        private static string Primero(string preferido, string alterno) =>
            !string.IsNullOrWhiteSpace(preferido) ? preferido.Trim()
            : (alterno ?? "").Trim();

        /// <summary>
        /// Los dos sistemas nombran los mismos niveles con distinta letra. VIP, PREMIUM y
        /// CLIENTEVN coinciden exactamente; el de entrada no —Socio Tiburón lo escribe
        /// CLASICA y el ERP CLASICO—, y esa sola letra bastaría para no encontrar la
        /// tarifa y cobrar puntos con el escalón equivocado.
        ///
        /// Va como tabla explícita y no como un recorte de la última letra: una regla
        /// así también haría iguales a dos niveles que no lo son, y aquí equivocarse
        /// significa otorgar de más o de menos.
        /// </summary>
        private static readonly Dictionary<string, string> AliasMembresia =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "CLASICA", "CLASICO" },
                { "CLASICO", "CLASICA" }
            };

        /// <summary>
        /// A qué clasificación de tarifas del ERP corresponde la membresía que Socio
        /// Tiburón tiene asignada. Sin membresía —o con una que el ERP no tarifica—
        /// devuelve la de entrada, que es el comportamiento que se pidió: nunca deja el
        /// selector en un valor que no exista.
        ///
        /// Se resuelve contra las clasificaciones VIVAS de puntos_tarifas y no contra una
        /// lista escrita aquí: si mañana se agrega un nivel, empieza a funcionar solo.
        /// </summary>
        private string ClasificacionDeMembresia(string membresia)
        {
            var disponibles = RunQuery(
                    "SELECT DISTINCT clasificacion FROM puntos_tarifas WHERE activo = true")
                .Select(f => f["clasificacion"]?.ToString()?.Trim() ?? "")
                .Where(c => c.Length > 0)
                .ToList();

            if (disponibles.Count == 0) return "";

            // La de entrada se busca por su raíz para que dé igual cómo esté escrita en
            // el catálogo del ERP; si no hubiera ninguna, la primera de la lista antes
            // que devolver algo que el selector no tiene.
            string porOmision =
                disponibles.FirstOrDefault(c => c.StartsWith("CLASIC", StringComparison.OrdinalIgnoreCase))
                ?? disponibles[0];

            membresia = (membresia ?? "").Trim();
            if (membresia.Length == 0) return porOmision;

            var exacta = disponibles.FirstOrDefault(
                c => c.Equals(membresia, StringComparison.OrdinalIgnoreCase));

            if (exacta is not null) return exacta;

            if (AliasMembresia.TryGetValue(membresia, out string equivalente))
            {
                var porAlias = disponibles.FirstOrDefault(
                    c => c.Equals(equivalente, StringComparison.OrdinalIgnoreCase));

                if (porAlias is not null) return porAlias;
            }

            return porOmision;
        }

        // ════════════════════════════════════════════════════════════════
        // Cálculo
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calcula sin guardar. Otorgar puntos tiene efecto económico, así que siempre
        /// se revisa antes: nunca hay un solo clic que lo haga a ciegas.
        ///
        /// Es POST y no GET por el tamaño de <paramref name="clasesKepler"/>: un cliente
        /// con cientos de documentos históricos manda un mapa que no cabe en una URL.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<JsonResult> Calcular(
            string cveCli, string desde, string hasta, string clasificacion,
            bool usarKepler = true, bool usarErp = false, string clasesKepler = null)
        {
            if (!DateTime.TryParse(desde, out DateTime dDesde) || !DateTime.TryParse(hasta, out DateTime dHasta))
                return Json(new { success = false, message = "Las fechas no son válidas." });

            if (dHasta < dDesde)
                return Json(new { success = false, message = "La fecha final no puede ser anterior a la inicial." });

            var origenes = OrigenVentas.Ninguno;
            if (usarKepler) origenes |= OrigenVentas.Kepler;
            if (usarErp) origenes |= OrigenVentas.Erp;

            var r = await _calculo.CalcularAsync(
                cveCli, dDesde, dHasta, clasificacion, origenes, LeerClases(clasesKepler));

            if (!r.Exito)
                return Json(new { success = false, message = r.Mensaje });

            var filas = Filas(r);

            // Se guarda el resultado para que la tabla pueda paginar sin recalcular. La
            // tabla es paginada del lado del servidor y su buscador dispara una petición por
            // tecla: sin esta caché, escribir un folio de siete dígitos lanzaría siete
            // recálculos completos, cada uno con su consulta a Kepler en el otro servidor.
            HttpContext.Session.SetString(CacheFilas,
                Newtonsoft.Json.JsonConvert.SerializeObject(filas));

            // Las filas también viajan completas en esta respuesta, y no es redundante: la
            // pantalla necesita el total del periodo para el resumen y para saber qué está
            // marcado en las páginas que no se están viendo. La tabla muestra una página; el
            // resumen describe lo que se va a guardar.
            return Json(new { success = true, clasificacion = r.Clasificacion, resumen = Resumen(r), movimientos = filas });
        }

        /// <summary>
        /// Una página de la tabla, servida desde lo último que se calculó. NO recalcula: si
        /// la sesión expiró o nunca se pulsó "Calcular", devuelve vacío y la pantalla lo
        /// muestra como tabla sin datos.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Movimientos(
            string nombre, string sortColumn, string sortDir, int page = 1, int pageSize = 25)
        {
            var todas = LeerCacheFilas();

            // El buscador de la tabla busca por folio: el propio y el del documento
            // relacionado. En un complemento de pago el folio propio es el del complemento y
            // la factura pagada está en el relacionado, así que buscar sólo en uno dejaría
            // fuera justo el caso de rastrear qué pagos entraron contra una factura.
            string q = (nombre ?? "").Trim();

            if (q.Length > 0)
                todas = todas.Where(f =>
                    f.Folio.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    f.FolioRelacionado.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            todas = Ordenar(todas, sortColumn, sortDir);

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;

            return Json(new
            {
                data = todas.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                total = todas.Count
            });
        }

        private List<PuntosFila> LeerCacheFilas()
        {
            string json = HttpContext.Session.GetString(CacheFilas);

            if (string.IsNullOrWhiteSpace(json)) return new List<PuntosFila>();

            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<PuntosFila>>(json)
                       ?? new List<PuntosFila>();
            }
            catch
            {
                return new List<PuntosFila>();
            }
        }

        /// <summary>
        /// El orden por omisión es el del cálculo —fecha y folio— que es como se lee un
        /// periodo. Una columna desconocida no altera nada en vez de reventar.
        /// </summary>
        private static List<PuntosFila> Ordenar(List<PuntosFila> filas, string columna, string dir)
        {
            if (string.IsNullOrWhiteSpace(columna) || columna == "null") return filas;

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            Func<PuntosFila, object> llave = columna switch
            {
                "origen" => f => f.Origen,
                "clase" => f => f.Clase,
                "folio" => f => f.Folio,
                "fecha" => f => f.FechaOrden,
                "importe" => f => f.Importe,
                "baseSinIva" => f => f.BaseSinIva,
                "tasa" => f => f.Tasa,
                "puntos" => f => f.Puntos,
                _ => null
            };

            if (llave is null) return filas;

            return (desc ? filas.OrderByDescending(llave) : filas.OrderBy(llave)).ToList();
        }

        /// <summary>
        /// Clasificación manual de los documentos de Kepler, indexada por clave de
        /// movimiento. Un JSON ilegible se ignora en lugar de reventar el cálculo: sin
        /// mapa todo se trata como contado, que es el comportamiento histórico.
        /// </summary>
        private static Dictionary<string, string> LeerClases(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Claves de los documentos de Kepler que el operador marcó para registrar.
        ///
        /// `null` significa "sin selección" y registra todo, que es como se comportaba el
        /// módulo antes de existir las casillas. Un arreglo vacío es distinto: significa
        /// que el operador desmarcó todo, y entonces no se registra ningún documento de
        /// Kepler. Confundir los dos casos haría que desmarcar todo otorgara todo.
        /// </summary>
        private static HashSet<string> LeerClaves(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                var claves = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);

                return claves is null
                    ? null
                    : new HashSet<string>(claves.Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.Ordinal);
            }
            catch
            {
                return null;
            }
        }

        private static object Resumen(ResultadoCalculoPuntos r) => new
        {
            movimientos = r.Movimientos.Count,
            mueven = r.Movimientos.Count(m => m.Mueve),
            yaRegistrados = r.Movimientos.Count(m => m.YaRegistrado),
            deKepler = r.Movimientos.Count(m => m.Origen == "kepler"),
            delErp = r.Movimientos.Count(m => m.Origen == "erp"),
            puntosNuevos = r.PuntosNuevos,
            puntosYaRegistrados = r.PuntosYaRegistrados,

            // Desglose por evento: es lo que permite ver de un vistazo si un cliente está
            // ganando por vender de contado o por fin pagar lo que debía.
            porContado = r.PorEvento(EventoPuntos.FacturaContado),
            porPagos = r.PorEvento(EventoPuntos.ComplementoPago),
            porAnticipos = r.PorEvento(EventoPuntos.Anticipo),
            porNotas = r.PorEvento(EventoPuntos.NotaCredito),
            porCancelaciones = r.PorEvento(EventoPuntos.Cancelacion)
        };

        private static List<PuntosFila> Filas(ResultadoCalculoPuntos r) =>
            r.Movimientos.Select(m => new PuntosFila
            {
                Clave = m.Clave,
                Origen = m.Origen,
                Evento = m.Evento,

                // Si el cálculo no puso clase, la etiqueta cae al evento. Pasa sólo con
                // movimientos que no clasifican nada, como un reverso de Kepler.
                Clase = string.IsNullOrWhiteSpace(m.Clase) ? m.Evento : m.Clase,

                Tipo = m.TipoMovimiento,
                Sucursal = m.Sucursal,
                Folio = m.Folio,
                FolioRelacionado = m.FolioRelacionado,
                Fecha = m.Fecha.ToString("dd/MM/yyyy"),
                FechaOrden = m.Fecha,
                Importe = m.Importe,
                BaseSinIva = m.Base,
                Tasa = m.Tasa,
                Puntos = m.Puntos,
                Mueve = m.Mueve,
                YaRegistrado = m.YaRegistrado,
                Motivo = m.Motivo
            }).ToList();

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas", Accion = "Otorgamiento de puntos Socio Tiburón")]
        public async Task<JsonResult> Otorgar(
            int clienteId, string cveCli, string desde, string hasta, string clasificacion,
            bool usarKepler = true, bool usarErp = false, string clasesKepler = null,
            string clavesKepler = null)
        {
            if (clienteId <= 0 || string.IsNullOrWhiteSpace(cveCli))
                return Json(new { success = false, message = "Falta el cliente." });

            if (!DateTime.TryParse(desde, out DateTime dDesde) || !DateTime.TryParse(hasta, out DateTime dHasta))
                return Json(new { success = false, message = "Las fechas no son válidas." });

            // Primero el alta, después los puntos. La pantalla ya lo pidió al elegir al
            // cliente, así que llegar aquí sin el alta significa que se cerró el modal o
            // que el envío vino de otro lado; en los dos casos se detiene, porque el
            // saldo quedaría escrito bajo una clave que el sitio no puede mostrar.
            //
            // Se exige la fila del padrón (`clientes`), no la del portal: hay clientes
            // históricos con puntos y sin cuenta de acceso, y bloquearlos obligaría a
            // inventarles un correo para poder otorgarles lo que ya compraron. Cuando no
            // se puede consultar Socio Tiburón tampoco se bloquea: no saber si falta no
            // es lo mismo que saber que falta, y el movimiento del ERP no depende de que
            // un servidor ajeno responda.
            var socio = await _sync.ConsultarClienteAsync(cveCli);

            if (socio.Consultado && !socio.EnClientes)
            {
                var contacto = await _sync.LeerContactoDelErpAsync(clienteId);

                return Json(new
                {
                    success = false,
                    requiereAlta = true,
                    message = "Este cliente todavía no existe en Socio Tiburón. " +
                              "Hay que darlo de alta antes de otorgarle puntos.",
                    datos = new
                    {
                        cveCli = cveCli.Trim(),
                        nombre = Primero(contacto.Nombre, socio.Nombre),
                        telefono = Primero(contacto.Telefono, socio.Telefono),
                        correo = Primero(contacto.Correo, socio.Correo)
                    }
                });
            }

            var origenes = OrigenVentas.Ninguno;
            if (usarKepler) origenes |= OrigenVentas.Kepler;
            if (usarErp) origenes |= OrigenVentas.Erp;

            // Se recalcula en el servidor en vez de confiar en lo que muestre la pantalla:
            // si alguien manipulara el envío, estaría otorgando puntos a discreción. La
            // clasificación de Kepler sí viene del cliente porque es un juicio del operador
            // que ningún dato de kdm1 puede reemplazar; lo que no viaja es el importe ni
            // los puntos, que siempre se recalculan aquí.
            var r = await _calculo.CalcularAsync(
                cveCli, dDesde, dHasta, clasificacion, origenes, LeerClases(clasesKepler));

            if (!r.Exito)
                return Json(new { success = false, message = r.Mensaje });

            // Se registran también los negativos: una nota de crédito o la cancelación de
            // una factura son movimientos igual de reales que una venta, y si sólo se
            // guardara lo que suma el saldo nunca bajaría.
            var nuevos = r.Movimientos.Where(m => m.Mueve && !m.YaRegistrado).ToList();

            // Guardado parcial: sólo aplica a Kepler, donde cada documento es una decisión
            // del operador y tiene sentido ir registrando por tandas. Los del ERP no se
            // pueden escoger a mano — son deterministas, y dejar fuera una nota de crédito
            // o una cancelación mientras se guarda su factura descuadraría el saldo.
            var seleccion = LeerClaves(clavesKepler);

            var porRegistrar = seleccion is null
                ? nuevos
                : nuevos.Where(m => m.Origen != "kepler" || seleccion.Contains(m.Clave)).ToList();

            int keplerOmitidos = nuevos.Count - porRegistrar.Count;

            if (porRegistrar.Count == 0)
                return Json(new
                {
                    success = false,
                    message = keplerOmitidos > 0
                        ? "No hay nada que registrar: los documentos de Kepler de este periodo están todos sin marcar."
                        : "No hay movimientos nuevos de puntos en ese periodo."
                });

            using var conn = AbrirConexion();
            using var tx = conn.BeginTransaction();

            try
            {
                int insertados = 0;
                decimal puntos = 0m;
                int usuarioId = GetUserId(User.Identity.Name);

                foreach (var mov in porRegistrar)
                {
                    // ON CONFLICT contra el índice único de doc_clave: si dos usuarios corren
                    // el proceso a la vez, el segundo no duplica. La idempotencia la garantiza
                    // la base, no el orden de ejecución. El RETURNING distingue lo insertado
                    // de lo que chocó.
                    var id = RunScalar(@"
                        INSERT INTO puntos_movimientos
                            (cliente_id, cve_cli, tipo, puntos, origen, evento, doc_clave,
                             doc_ref, doc_ref_rel,
                             doc_sucursal, doc_genero, doc_naturaleza, doc_grupo, doc_tipo,
                             doc_folio, doc_fecha, doc_importe, doc_base,
                             clasificacion, tasa_aplicada, usuario_id, comentario)
                        VALUES
                            (@cliente, @cve, @tipoMov, @puntos, @origen, @evento, @clave,
                             @ref, @refRel,
                             @suc, @gen, @nat, @grupo, @tipo,
                             @folio, @fecha, @importe, @base,
                             @clasif, @tasa, @usuario, @comentario)
                        ON CONFLICT DO NOTHING
                        RETURNING id_movimiento",
                        new Dictionary<string, object>
                        {
                            { "cliente",    clienteId },
                            { "cve",        cveCli.Trim() },
                            { "tipoMov",    mov.TipoMovimiento },
                            { "puntos",     mov.Puntos },
                            { "origen",     mov.Origen },
                            { "evento",     mov.Evento },
                            { "clave",      mov.Clave },
                            { "ref",        mov.Ref > 0 ? mov.Ref : (object)DBNull.Value },
                            { "refRel",     mov.RefRelacionada > 0 ? mov.RefRelacionada : (object)DBNull.Value },
                            { "suc",        mov.Sucursal },
                            { "gen",        mov.Genero },
                            { "nat",        mov.Naturaleza },
                            { "grupo",      mov.Grupo },
                            { "tipo",       mov.Tipo },
                            { "folio",      mov.Folio },
                            { "fecha",      mov.Fecha },
                            { "importe",    mov.Importe },
                            { "base",       mov.Base },
                            { "clasif",     r.Clasificacion },
                            { "tasa",       mov.Tasa },
                            { "usuario",    usuarioId },

                            // Se guarda el motivo antes que el comentario del documento: es lo
                            // que explica POR QUÉ ese movimiento vale lo que vale, y es la
                            // única pista que tendrá quien revise el estado de cuenta dentro
                            // de un año.
                            { "comentario", Comentario(mov) }
                        }, false, conn, tx);

                    if (id is not null and not DBNull)
                    {
                        insertados++;
                        puntos += mov.Puntos;
                    }
                }

                tx.Commit();

                // El saldo ya cambió en el ERP; se refleja en Socio Tiburón para que el
                // cliente lo vea enseguida. Va DESPUÉS del commit y no propaga errores:
                // si la plataforma externa no responde, el movimiento del ERP ya quedó
                // bien y la publicación se reintenta.
                await _sync.PublicarClienteAsync(cveCli);

                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"Registrados {insertados} movimiento(s) por un neto de {puntos} puntos del " +
                    $"{dDesde:dd/MM/yyyy} al {dHasta:dd/MM/yyyy} ({r.Clasificacion}, origen {origenes})" +
                    (keplerOmitidos > 0 ? $"; {keplerOmitidos} documento(s) de Kepler sin marcar quedaron fuera." : "."),
                    User.Identity?.Name, nivel: "INFO");

                // El neto puede ser negativo —un periodo con más notas de crédito y
                // cancelaciones que ventas cobradas— así que el mensaje no puede decir
                // "se otorgaron" sin más.
                string resultado = puntos >= 0
                    ? $"Se otorgaron {puntos:N0} puntos"
                    : $"Se retiraron {Math.Abs(puntos):N0} puntos";

                string mensaje = insertados == porRegistrar.Count
                    ? $"{resultado} en {insertados} movimiento(s)."
                    : $"{resultado} en {insertados} de {porRegistrar.Count} movimiento(s); " +
                      "el resto ya estaba registrado.";

                // Se dice explícitamente lo que NO se guardó. Un guardado parcial silencioso
                // se confunde con uno completo, y el operador daría por cerrado un periodo
                // que todavía tiene documentos sin otorgar.
                if (keplerOmitidos > 0)
                    mensaje += $" Quedaron fuera {keplerOmitidos} documento(s) de Kepler sin marcar; " +
                               "vuelven a aparecer al recalcular el periodo.";

                return Json(new
                {
                    success = true,
                    message = mensaje,
                    puntosOtorgados = puntos,
                    documentos = insertados,
                    omitidos = keplerOmitidos,
                    saldo = SaldoDe(clienteId)
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/Otorgar");
                try { tx.Rollback(); } catch { }

                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"Error al otorgar puntos: {ex.Message}", User.Identity?.Name);

                return Json(new { success = false, message = "No se pudieron otorgar los puntos: " + ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Estado de cuenta y ajustes
        // ════════════════════════════════════════════════════════════════

        [HttpGet]
        public JsonResult EstadoDeCuenta(int clienteId, int limite = 300)
        {
            try
            {
                var s = RunQuery("SELECT * FROM v_puntos_saldo WHERE cliente_id = @c",
                    new Dictionary<string, object> { { "c", clienteId } }).FirstOrDefault();

                var movimientos = RunQuery(@"
                    SELECT m.id_movimiento, m.tipo, m.evento, m.puntos, m.origen,
                           m.doc_folio, m.doc_fecha,
                           m.doc_importe, m.doc_base, m.clasificacion, m.tasa_aplicada,
                           m.comentario, m.fecha_registro, u.nombreusuario
                    FROM puntos_movimientos m
                    LEFT JOIN usuarios u ON u.usuarioid = m.usuario_id
                    WHERE m.cliente_id = @c
                    ORDER BY m.fecha_registro DESC, m.id_movimiento DESC
                    LIMIT @l",
                    new Dictionary<string, object> { { "c", clienteId }, { "l", limite } });

                return Json(new
                {
                    success = true,
                    saldo = s == null ? 0m : Dec(s["saldo"]),
                    acumulados = s == null ? 0m : Dec(s["acumulados"]),
                    canjeados = s == null ? 0m : Dec(s["canjeados"]),
                    devoluciones = s == null ? 0m : Dec(s["devoluciones"]),
                    reversos = s == null ? 0m : Dec(s["reversos"]),
                    ajustes = s == null ? 0m : Dec(s["ajustes"]),
                    porContado = s == null ? 0m : Dec(s["por_contado"]),
                    porPagos = s == null ? 0m : Dec(s["por_pagos"]),
                    porAnticipos = s == null ? 0m : Dec(s["por_anticipos"]),
                    documentos = s == null ? 0 : Convert.ToInt32(s["documentos"]),
                    deKepler = s == null ? 0 : Convert.ToInt32(s["documentos_kepler"]),
                    delErp = s == null ? 0 : Convert.ToInt32(s["documentos_erp"]),
                    movimientos = movimientos.Select(m => new
                    {
                        id = Convert.ToInt32(m["id_movimiento"]),
                        tipo = m["tipo"]?.ToString(),
                        evento = m["evento"]?.ToString(),
                        puntos = Dec(m["puntos"]),
                        origen = m["origen"]?.ToString(),
                        folio = m["doc_folio"]?.ToString(),
                        fechaDoc = m["doc_fecha"] is DateTime fd ? fd.ToString("dd/MM/yyyy") : "",
                        importe = Dec(m["doc_importe"]),
                        baseSinIva = Dec(m["doc_base"]),
                        clasificacion = m["clasificacion"]?.ToString(),
                        tasa = Dec(m["tasa_aplicada"]),
                        comentario = m["comentario"]?.ToString(),
                        registrado = m["fecha_registro"] is DateTime fr ? fr.ToString("dd/MM/yyyy HH:mm") : "",
                        usuario = m["nombreusuario"]?.ToString()
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/EstadoDeCuenta");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Ajuste manual. Corregir se hace agregando un movimiento, nunca editando los
        /// existentes: así la corrección también queda explicada y con responsable.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas", Accion = "Ajuste manual de puntos Socio Tiburón")]
        public async Task<JsonResult> Ajustar(int clienteId, string cveCli, decimal puntos, string motivo)
        {
            if (clienteId <= 0 || string.IsNullOrWhiteSpace(cveCli))
                return Json(new { success = false, message = "Falta el cliente." });

            if (puntos == 0)
                return Json(new { success = false, message = "El ajuste no puede ser de cero puntos." });

            if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
                return Json(new { success = false, message = "Explica el motivo del ajuste (mínimo 5 caracteres)." });

            try
            {
                RunUpdate(@"
                    INSERT INTO puntos_movimientos (cliente_id, cve_cli, tipo, puntos, usuario_id, comentario)
                    VALUES (@c, @cve, 'ajuste', @p, @u, @m)",
                    new Dictionary<string, object>
                    {
                        { "c", clienteId }, { "cve", cveCli.Trim() }, { "p", puntos },
                        { "u", GetUserId(User.Identity.Name) }, { "m", motivo.Trim() }
                    });

                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"Ajuste manual de {puntos} puntos: {motivo}", User.Identity?.Name, nivel: "INFO");

                await _sync.PublicarClienteAsync(cveCli);

                decimal saldo = SaldoDe(clienteId);

                return Json(new
                {
                    success = true,
                    message = $"Ajuste registrado. Nuevo saldo: {saldo:N0} puntos.",
                    saldo
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/Ajustar");
                return Json(new { success = false, message = "No se pudo registrar el ajuste: " + ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Publicación hacia Socio Tiburón
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compara el saldo del ERP contra el que hoy muestra Socio Tiburón, sin
        /// escribir nada. Sirve para ver a quién le cambiaría el número antes de
        /// publicarlo, que es justo lo que el cliente notaría.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> CompararSaldos()
        {
            try
            {
                var comparacion = await _sync.CompararAsync();

                return Json(new
                {
                    success = true,
                    resumen = new
                    {
                        clientes = comparacion.Count,
                        diferentes = comparacion.Count(c => c.SaldoErp != c.SaldoWeb),
                        totalErp = comparacion.Sum(c => c.SaldoErp),
                        totalWeb = comparacion.Sum(c => c.SaldoWeb)
                    },
                    clientes = comparacion.Take(300).Select(c => new
                    {
                        clave = c.Cve,
                        saldoErp = c.SaldoErp,
                        saldoWeb = c.SaldoWeb,
                        diferencia = c.SaldoErp - c.SaldoWeb
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Puntos/CompararSaldos");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Escribe el saldo del ERP en la tabla `puntos` de Socio Tiburón, que es de
        /// donde el sitio lee. No modifica la página: cambia quién escribe, no quién lee.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas", Accion = "Publicación de saldos en Socio Tiburón")]
        public async Task<JsonResult> PublicarSaldos(string claves)
        {
            var lista = (claves ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            var r = await _sync.PublicarAsync(lista.Count > 0 ? lista : null);

            return Json(new
            {
                success = r.Exito,
                message = r.Mensaje,
                actualizados = r.Actualizados,
                insertados = r.Insertados,
                sinCambio = r.SinCambio
            });
        }

        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Qué queda escrito en el movimiento. El motivo explica el cálculo ("Pago
        /// recibido de la factura X", "Neta de $5,000 de anticipo ya bonificado") y por
        /// eso gana al comentario del documento, que suele ser del capturista y no dice
        /// nada sobre puntos. Cuando hay los dos, van juntos.
        /// </summary>
        private static object Comentario(MovimientoPuntos mov)
        {
            bool hayMotivo = !string.IsNullOrWhiteSpace(mov.Motivo);
            bool hayDoc = !string.IsNullOrWhiteSpace(mov.Comentario);

            if (!hayMotivo && !hayDoc) return DBNull.Value;
            if (!hayDoc) return Recortar(mov.Motivo);
            if (!hayMotivo) return Recortar(mov.Comentario);

            return Recortar($"{mov.Motivo.Trim()} — {mov.Comentario.Trim()}");
        }

        // comentario es varchar(300): un texto más largo abortaría la transacción entera
        // y con ella el resto de los movimientos del periodo.
        private static string Recortar(string texto) =>
            texto.Trim().Length <= 300 ? texto.Trim() : texto.Trim()[..300];

        private decimal SaldoDe(int clienteId)
        {
            var s = RunQuery("SELECT saldo FROM v_puntos_saldo WHERE cliente_id = @c",
                new Dictionary<string, object> { { "c", clienteId } }).FirstOrDefault();

            return s == null ? 0m : Dec(s["saldo"]);
        }

        private static decimal Dec(object valor) =>
            valor is null or DBNull ? 0m : Convert.ToDecimal(valor);
    }
}
