using BOS_ERP.Controllers;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using Npgsql;

namespace BOS_ERP.Services
{
    /// <summary>
    /// Convierte una falla del sistema en un ticket de soporte, deduplicando por
    /// huella para que un error que le pega a 40 personas produzca UN ticket y no 40.
    ///
    /// CONTRATO
    /// Este servicio NUNCA lanza. Lo llaman un middleware de excepciones, los
    /// catch de los controladores y un endpoint que recibe errores del navegador:
    /// en los tres casos, un fallo aquí convertiría un error en dos. Todo método
    /// público termina en un catch que se traga lo que sea y devuelve null.
    ///
    /// Se construye con el ctor manual de Utilities -igual que LogErrorHelper y
    /// SoporteAuthz- para poder usarse desde jobs de Hangfire y desde middleware,
    /// donde no siempre hay un scope de DI a la mano.
    /// </summary>
    public sealed class ErrorTicketService
    {
        /// <summary>Categoría bajo la que se agrupan. La crea sql/soporte_errores_auto.sql.</summary>
        private const string NombreCategoria = "Errores del sistema";

        /// <summary>areas: areaid = 1 es Sistemas. Área de respaldo cuando el usuario no tiene una.</summary>
        private const int AreaSistemas = 1;

        // stat_tkt. El 5 es "En progreso", NO "Cancelado" (ver EstatusController).
        //
        // Estos ids SÍ se pueden fijar: EstatusController los tiene hardcodeados y
        // corre en producción, así que se sabe que coinciden en los dos esquemas.
        // Los de prio NO -ver Prioridades()-.
        private const int EstadoAbierto = 1;
        private const int EstadoResuelto = 4;
        private const int EstadoEnProgreso = 5;

        /// <summary>Módulos donde cualquier falla es alta por definición: mueven dinero o timbran ante el SAT.</summary>
        private static readonly string[] ModulosCriticos =
        {
            "factura", "timbr", "cancelacion", "cancelación", "refactur", "poliza", "póliza", "cartera"
        };

        private readonly Utilities _utils;
        private readonly IConfiguration _cfg;

        public ErrorTicketService()
        {
            _utils = new Utilities(true);
            _cfg = _utils._configuration;
        }

        // --------------------------------------------------------------------
        // Configuración
        // --------------------------------------------------------------------

        private bool Activo => _cfg.GetValue("CapturaErrores:Activo", true);

        /// <summary>
        /// Cortacircuitos. Sin esto, una caída de Postgres genera miles de tickets
        /// en minutos y el módulo queda inservible. Al rebasarlo se sigue
        /// registrando TODO el diagnóstico; sólo se dejan de crear tickets.
        /// </summary>
        private int TicketsPorHora => _cfg.GetValue("CapturaErrores:TicketsPorHora", 20);

        /// <summary>Si el error vuelve dentro de esta ventana, es regresión: se reabre en vez de duplicar.</summary>
        private int DiasRegresion => _cfg.GetValue("CapturaErrores:DiasRegresion", 30);

        /// <summary>
        /// Ocurrencias en las que se comenta en el hilo. Comentar en cada una
        /// inundaría el ticket y haría inútil la deduplicación.
        /// </summary>
        private static readonly int[] UmbralesSeguimiento = { 10, 50, 100, 500, 1000 };

        // --------------------------------------------------------------------
        // Entrada única
        // --------------------------------------------------------------------

        /// <summary>
        /// Registra la ocurrencia y devuelve el folio del ticket que la representa,
        /// o null si no se creó ninguno (silenciada, cortacircuitos abierto, o
        /// falló el registro). Nunca lanza.
        /// </summary>
        public string Registrar(ErrorCapturado e)
        {
            try
            {
                if (!Activo || e == null) return null;

                string mensajeNorm = ErrorFingerprint.NormalizarMensaje(e.Mensaje);
                string origen = string.IsNullOrWhiteSpace(e.Origen)
                    ? ErrorFingerprint.ExtraerOrigen(e.Stack)
                    : e.Origen;
                string huella = ErrorFingerprint.Calcular(e.TipoExc, mensajeNorm, origen);

                e.Origen = origen;

                // El diagnóstico se guarda en su propia transacción, ANTES de
                // decidir nada sobre el ticket: si el alta del ticket falla, la
                // evidencia del error ya quedó registrada de todos modos.
                var ocurrencia = RegistrarOcurrencia(huella, mensajeNorm, origen, e);
                if (ocurrencia == null) return null;

                if (ocurrencia.Silenciada) return null;

                return AplicarTicket(ocurrencia, e);
            }
            catch
            {
                // Un fallo aquí no puede convertir un error en dos. Se pierde el
                // ticket, no la petición del usuario.
                return null;
            }
        }

        // --------------------------------------------------------------------
        // 1) Huella + detalle
        // --------------------------------------------------------------------

        private sealed class Ocurrencia
        {
            public int IdHuella { get; set; }
            public int? IdTkt { get; set; }
            public int Ocurrencias { get; set; }
            public bool Silenciada { get; set; }
            public bool EsNueva => Ocurrencias == 1;
        }

        private Ocurrencia RegistrarOcurrencia(string huella, string mensajeNorm, string origen, ErrorCapturado e)
        {
            try
            {
                using var conn = new NpgsqlConnection(_cfg.GetConnectionString("ERP_SRS"));
                conn.Open();
                using var tx = conn.BeginTransaction();

                // ON CONFLICT es lo que hace atómica la deduplicación: dos peticiones
                // simultáneas con el mismo error no pueden crear dos huellas.
                var filas = _utils.RunQuery(
                    "INSERT INTO tkt_error_huella (huella, tipo_exc, mensaje_norm, origen) " +
                    "VALUES (@huella, @tipo, @msg, @origen) " +
                    "ON CONFLICT (huella) DO UPDATE " +
                    "   SET ocurrencias = tkt_error_huella.ocurrencias + 1, " +
                    "       ultima_vez  = now() " +
                    "RETURNING id_huella, id_tkt, ocurrencias, silenciada",
                    new Dictionary<string, object>
                    {
                        { "huella", huella },
                        { "tipo",   Recortar(e.TipoExc, 200) },
                        { "msg",    mensajeNorm },
                        { "origen", Recortar(origen, 300) }
                    },
                    false, conn, tx);

                if (filas.Count == 0) { tx.Rollback(); return null; }

                var o = new Ocurrencia
                {
                    IdHuella = Convert.ToInt32(filas[0]["id_huella"]),
                    IdTkt = filas[0]["id_tkt"] == null ? null : Convert.ToInt32(filas[0]["id_tkt"]),
                    Ocurrencias = Convert.ToInt32(filas[0]["ocurrencias"]),
                    Silenciada = Convert.ToBoolean(filas[0]["silenciada"])
                };

                // Usuarios distintos afectados. Se consulta ANTES de insertar el
                // detalle, para no tener que excluir la fila recién creada.
                bool usuarioNuevo = false;
                if (!o.EsNueva && e.UsuarioId.HasValue)
                {
                    var previo = _utils.RunQuery(
                        "SELECT 1 FROM tkt_error_detalle " +
                        "WHERE id_huella = @id_huella AND id_usr = @id_usr LIMIT 1",
                        new Dictionary<string, object>
                        {
                            { "id_huella", o.IdHuella },
                            { "id_usr", e.UsuarioId.Value }
                        },
                        false, conn, tx);

                    usuarioNuevo = previo.Count == 0;
                }

                // Techo de escritura del detalle.
                //
                // El contador de la huella siempre sube, pero las FILAS de detalle se
                // frenan: pasadas las primeras 50 sólo se guarda una de cada 25. Con
                // 50 ocurrencias ya se diagnostica cualquier cosa, y esto acota lo que
                // puede escribir un endpoint público -la autofacturación es anónima-
                // si alguien provoca el mismo error en bucle. La poda nocturna recorta
                // igual, pero entre corridas no habría nada que limitara el ritmo.
                //
                // Efecto lateral asumido: pasado el techo, `afectados` deja de contar
                // usuarios nuevos, porque esa comprobación se apoya en el detalle.
                bool guardarDetalle = o.Ocurrencias <= 50 || o.Ocurrencias % 25 == 0;

                if (guardarDetalle)
                {
                    _utils.RunUpdate(
                        "INSERT INTO tkt_error_detalle " +
                        "(id_huella, capa, id_usr, empresa_id, sucursal_id, ruta, metodo, trace_id, ip, user_agent, payload) " +
                        "VALUES " +
                        "(@id_huella, @capa, @id_usr, @empresa, @sucursal, @ruta, @metodo, @trace, @ip, @ua, @payload::jsonb)",
                        new Dictionary<string, object>
                        {
                            { "id_huella", o.IdHuella },
                            { "capa",      Recortar(e.Capa, 16) },
                            { "id_usr",    e.UsuarioId },
                            { "empresa",   e.EmpresaId },
                            { "sucursal",  e.SucursalId },
                            { "ruta",      e.Ruta },
                            { "metodo",    Recortar(e.Metodo, 10) },
                            { "trace",     Recortar(e.TraceId, 100) },
                            { "ip",        Recortar(e.Ip, 64) },
                            { "ua",        e.UserAgent },
                            { "payload",   e.PayloadJson() }
                        },
                        false, conn, tx);
                }

                if (usuarioNuevo)
                {
                    _utils.RunUpdate(
                        "UPDATE tkt_error_huella SET afectados = afectados + 1 WHERE id_huella = @id",
                        new Dictionary<string, object> { { "id", o.IdHuella } },
                        false, conn, tx);
                }

                tx.Commit();
                return o;
            }
            catch (Exception ex)
            {
                // Si no se pudo ni registrar el diagnóstico, lo más probable es que
                // la base esté caída -que es justo el error que más urge saber-. Se
                // deja constancia por el camino que no depende de Postgres.
                RespaldoEnDisco(huella, e, ex);
                return null;
            }
        }

        /// <summary>
        /// Último recurso cuando la base no responde: una línea NDJSON en disco.
        ///
        /// Sin esto, las caídas de infraestructura -las más graves- son justamente
        /// las únicas que no dejarían rastro, porque el servicio escribe en la misma
        /// base que se cayó.
        /// </summary>
        private void RespaldoEnDisco(string huella, ErrorCapturado e, Exception fallo)
        {
            try
            {
                // La ruta la decide ErroresDrenajeService: es quien lee estos archivos
                // y las dos mitades tienen que mirar a la misma carpeta.
                string dir = ErroresDrenajeService.RutaCarpeta(_cfg);
                Directory.CreateDirectory(dir);

                string archivo = Path.Combine(dir, $"errores-{DateTime.UtcNow:yyyyMMdd}.ndjson");

                // El stack va incluido aunque engorde el archivo. Este respaldo es
                // justo el de los incidentes graves -la base caída-, que es cuando
                // más falta hace el diagnóstico completo; un NDJSON de unos MB es
                // barato comparado con no poder reconstruir qué pasó.
                var linea = System.Text.Json.JsonSerializer.Serialize(new
                {
                    fch = DateTime.UtcNow,
                    huella,
                    capa = e.Capa,
                    modulo = e.Modulo,
                    tipoExc = e.TipoExc,
                    mensaje = e.Mensaje,
                    stack = e.Stack,
                    origen = e.Origen,
                    ruta = e.Ruta,
                    metodo = e.Metodo,
                    idUsr = e.UsuarioId,
                    empresaId = e.EmpresaId,
                    sucursalId = e.SucursalId,
                    falloAlGuardar = fallo?.Message
                });

                File.AppendAllText(archivo, linea + Environment.NewLine);
            }
            catch
            {
                // Si tampoco se puede escribir en disco, no queda nada por hacer.
            }
        }

        // --------------------------------------------------------------------
        // 2) Decisión sobre el ticket
        // --------------------------------------------------------------------

        private string AplicarTicket(Ocurrencia o, ErrorCapturado e)
        {
            // Sin ticket todavía: se crea, si el cortacircuitos lo permite.
            if (!o.IdTkt.HasValue)
                return CrearTicket(o, e);

            var tkt = _utils.RunQuery(
                "SELECT t.id_tkts, t.folio_tkt, t.id_stat_tkt, " +
                "       (SELECT MAX(h.fch_cambio) FROM hst_est h " +
                "         WHERE h.id_tkts = t.id_tkts AND h.id_stat_tkt_nuev = @resuelto) AS fch_resuelto " +
                "FROM tkts t WHERE t.id_tkts = @id",
                new Dictionary<string, object>
                {
                    { "id", o.IdTkt.Value },
                    { "resuelto", EstadoResuelto }
                });

            // El ticket ya no existe (lo borraron): la FK dejó id_tkt en NULL o
            // apunta a nada. Se crea uno nuevo.
            if (tkt.Count == 0) return CrearTicket(o, e);

            int estado = Convert.ToInt32(tkt[0]["id_stat_tkt"]);
            string folio = tkt[0]["folio_tkt"]?.ToString();

            // Vivo: sólo se engorda, y se comenta únicamente al cruzar un umbral.
            if (estado == EstadoAbierto || estado == EstadoEnProgreso)
            {
                if (UmbralesSeguimiento.Contains(o.Ocurrencias))
                    Comentar(o.IdTkt.Value, o, e,
                        $"Este error sigue ocurriendo: van {o.Ocurrencias} veces.");

                return folio;
            }

            // Resuelto hace poco: es una regresión, no un error nuevo. Reabrir
            // conserva el historial de lo que ya se investigó.
            object fchResuelto = tkt[0]["fch_resuelto"];
            if (fchResuelto != null)
            {
                var dias = (DateTime.Now - Convert.ToDateTime(fchResuelto)).TotalDays;
                if (dias <= DiasRegresion)
                {
                    Reabrir(o.IdTkt.Value, o, e, dias);
                    return folio;
                }
            }

            // Resuelto hace mucho: cuenta como problema nuevo.
            return CrearTicket(o, e);
        }

        private string CrearTicket(Ocurrencia o, ErrorCapturado e)
        {
            try
            {
                int idCategoria = ObtenerCategoria();
                if (idCategoria <= 0) return null;

                if (CortacircuitosAbierto(idCategoria)) return null;

                // El creador es el usuario logueado al momento de la falla. Cuando no
                // hay sesión -jobs de Hangfire, sesión expirada- se cae al responsable
                // de la categoría: tkts.id_usr tiene FK y necesita un usuario real.
                int idResponsable = ObtenerResponsable(idCategoria);
                if (idResponsable <= 0) return null;

                // El catálogo prio no comparte ids entre esquemas, así que se
                // resuelve antes de abrir la transacción: sin prioridad válida el
                // INSERT fallaría por FK y no vale la pena empezar.
                int idPrioridad = Prioridad(e);
                if (idPrioridad <= 0) return null;

                int idCreador = e.UsuarioId ?? idResponsable;
                object idArea = ObtenerArea(idCreador);

                using var conn = new NpgsqlConnection(_cfg.GetConnectionString("ERP_SRS"));
                conn.Open();
                using var tx = conn.BeginTransaction();

                int idTicket;
                string folio;

                try
                {
                    // A diferencia del alta manual, aquí NO se genera documento del
                    // ERP: un ticket automático no tiene contraparte contable y
                    // llenaría encabezadomov de ruido.
                    (idTicket, folio) = TicketFactory.InsertarConFolio(
                        _utils, conn, tx,
                        Titulo(e), Descripcion(o, e), idPrioridad,
                        idCreador, idArea, idResponsable, idCategoria);

                    _utils.RegistrarCambioEstado(
                        idTicket, null, EstadoAbierto, idCreador, null, null, conn, tx);

                    _utils.RunUpdate(
                        "UPDATE tkt_error_huella SET id_tkt = @id_tkt WHERE id_huella = @id_huella",
                        new Dictionary<string, object>
                        {
                            { "id_tkt", idTicket },
                            { "id_huella", o.IdHuella }
                        },
                        false, conn, tx);

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }

                // El aviso va después del commit y SÓLO en la primera ocurrencia: si
                // se mandara en cada repetición, la deduplicación del ticket no
                // serviría de nada porque el buzón se inundaría igual.
                Avisar(idResponsable, folio, e, o.IdHuella, esRegresion: false);

                return folio;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// ¿Ya se crearon demasiados tickets automáticos en la última hora?
        /// Se cuenta sobre tkts y no sobre las huellas porque lo que hay que
        /// limitar es el ruido que llega a Sistemas, no la telemetría.
        /// </summary>
        private bool CortacircuitosAbierto(int idCategoria)
        {
            try
            {
                var n = _utils.RunScalar(
                    "SELECT COUNT(*) FROM tkts " +
                    "WHERE id_cat = @id_cat AND fch_crea > now() - interval '1 hour'",
                    new Dictionary<string, object> { { "id_cat", idCategoria } });

                return Convert.ToInt32(n) >= TicketsPorHora;
            }
            catch
            {
                // Ante la duda, no crear: es preferible perder un ticket que abrir
                // la puerta a una avalancha.
                return true;
            }
        }

        private void Reabrir(int idTicket, Ocurrencia o, ErrorCapturado e, double dias)
        {
            try
            {
                _utils.RunUpdate(
                    "UPDATE tkts SET id_stat_tkt = @abierto WHERE id_tkts = @id",
                    new Dictionary<string, object>
                    {
                        { "abierto", EstadoAbierto },
                        { "id", idTicket }
                    });

                int idUsr = e.UsuarioId ?? 0;
                if (idUsr > 0)
                    _utils.RegistrarCambioEstado(idTicket, EstadoResuelto, EstadoAbierto, idUsr, null, null);

                Comentar(idTicket, o, e,
                    $"REGRESIÓN: este error se había marcado como resuelto hace {dias:F0} días y volvió a ocurrir.");

                // Una regresión sí merece correo aunque el ticket no sea nuevo: algo
                // que se dio por arreglado no lo está, y eso no puede quedarse en una
                // campana que quizá nadie tiene abierta.
                var tkt = _utils.RunQuery(
                    "SELECT id_usr_asig, folio_tkt FROM tkts WHERE id_tkts = @id",
                    new Dictionary<string, object> { { "id", idTicket } });

                if (tkt.Count > 0 && tkt[0]["id_usr_asig"] != null)
                {
                    Avisar(Convert.ToInt32(tkt[0]["id_usr_asig"]),
                           tkt[0]["folio_tkt"]?.ToString(),
                           e, o.IdHuella, esRegresion: true);
                }
            }
            catch
            {
                // Reabrir es mejor esfuerzo: la ocurrencia ya quedó registrada.
            }
        }

        private void Comentar(int idTicket, Ocurrencia o, ErrorCapturado e, string encabezado)
        {
            try
            {
                int idUsr = e.UsuarioId ?? ObtenerResponsable(ObtenerCategoria());
                if (idUsr <= 0) return;

                string texto =
                    $"{encabezado}\n\n" +
                    $"Última vez: {DateTime.Now:dd/MM/yyyy HH:mm}\n" +
                    $"Ruta: {e.Metodo} {e.Ruta}\n" +
                    $"Huella #{o.IdHuella}";

                _utils.RunUpdate(
                    "INSERT INTO seg_tkts (id_tkt, id_usr, coment) VALUES (@id_tkt, @id_usr, @coment)",
                    new Dictionary<string, object>
                    {
                        { "id_tkt", idTicket },
                        { "id_usr", idUsr },
                        { "coment", texto }
                    });
            }
            catch
            {
                // Un comentario perdido no justifica propagar nada.
            }
        }

        /// <summary>
        /// Avisa al responsable por los dos canales.
        ///
        /// La notificación interna (la campana de SignalR) sólo la ve quien tenga el
        /// ERP abierto: para un job que revienta a las 3 de la mañana, eso es nadie.
        /// De ahí el correo, que se encola en Hangfire en vez de mandarse aquí -ver
        /// ErrorCorreoJob para el porqué-.
        /// </summary>
        private void Avisar(int idResponsable, string folio, ErrorCapturado e, int idHuella, bool esRegresion)
        {
            try
            {
                var resp = _utils.RunQuery(
                    "SELECT nombreusuario, email FROM usuarios WHERE usuarioid = @id",
                    new Dictionary<string, object> { { "id", idResponsable } });

                if (resp.Count == 0) return;

                _ = _utils.SendNotificationInterno(
                    resp[0]["nombreusuario"]?.ToString(),
                    resp[0]["email"]?.ToString(),
                    new
                    {
                        titulo = esRegresion ? "Regresión detectada" : "Error automático detectado",
                        mensaje = $"Ticket {folio}: {e.TipoExc} en {e.Modulo}",
                        url = $"/Soporte/Ticket/{folio}"
                    });
            }
            catch
            {
                // El ticket ya existe: el aviso es lo único que se pierde.
            }

            // Encolar va en su propio try: que falle el storage de Hangfire no debe
            // impedir la notificación interna, ni al revés.
            try
            {
                Hangfire.BackgroundJob.Enqueue<ErrorCorreoJob>(j => j.Enviar(idHuella, esRegresion));
            }
            catch
            {
                // Sin Hangfire disponible se pierde el correo, no el ticket.
            }
        }

        // --------------------------------------------------------------------
        // Contenido del ticket
        // --------------------------------------------------------------------

        private static string Titulo(ErrorCapturado e)
        {
            // Sin longitud conocida para tkts.tit, se recorta corto a propósito:
            // el título es un resumen y el detalle vive en el panel de diagnóstico.
            string tipoCorto = e.TipoExc?.Split('.').LastOrDefault() ?? "Error";
            return Recortar($"[Auto] {tipoCorto} en {e.Modulo}", 100);
        }

        private string Descripcion(Ocurrencia o, ErrorCapturado e)
        {
            return
                "Ticket generado automáticamente por la captura de errores del sistema.\n\n" +
                $"Tipo:    {e.TipoExc}\n" +
                $"Mensaje: {e.Mensaje}\n" +
                $"Origen:  {e.Origen}\n" +
                $"Módulo:  {e.Modulo}\n" +
                $"Ruta:    {e.Metodo} {e.Ruta}\n" +
                $"Capa:    {e.Capa}\n" +
                $"Fecha:   {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n\n" +
                $"El stack completo y el resto del diagnóstico están en el panel de " +
                $"diagnóstico de este ticket (huella #{o.IdHuella}).";
        }

        /// <summary>
        /// Severidad → prioridad. Devuelve 0 si el catálogo prio no se pudo leer,
        /// en cuyo caso no se crea ticket (id_prio tiene FK).
        /// </summary>
        private int Prioridad(ErrorCapturado e)
        {
            var prio = Prioridades();
            if (prio == null) return 0;

            // Lo que reporta el navegador no trae stack de servidor y suele ser
            // cosmético: no debe competir con una falla de facturación.
            if (e.Capa == "navegador") return prio.Baja;

            // Un rechazo del PAC importa -sobre todo repetido-, pero el ERP no se
            // rompió: alguien capturó un dato que el SAT no acepta. Va antes de la
            // comprobación de módulos críticos justamente para que "Facturación" no
            // lo suba a alta y tape las fallas que sí dejaron algo a medias.
            if (e.Capa == "pac") return prio.Media;

            string modulo = (e.Modulo ?? "").ToLowerInvariant();
            if (ModulosCriticos.Any(m => modulo.Contains(m))) return prio.Alta;

            // Una excepción que escapó del todo, o una que reventó un job nocturno,
            // rompió algo de verdad: nadie la manejó.
            if (e.Capa == "middleware" || e.Capa == "job") return prio.Alta;

            return prio.Media;
        }

        private sealed class Prios
        {
            public int Alta { get; init; }
            public int Media { get; init; }
            public int Baja { get; init; }
        }

        private static Prios _prioCache;
        private static readonly object _prioLock = new();

        /// <summary>
        /// Ids del catálogo prio, resueltos POR NOMBRE.
        ///
        /// No se pueden fijar en constantes: srs tiene 1 = Alta, 2 = Media, 3 = Baja,
        /// pero srs_prod usa otros ids y un INSERT con id_prio = 1 revienta ahí con
        /// 23503. El resto del módulo nunca tropezó con esto porque siempre leyó las
        /// prioridades del catálogo (el formulario las pinta con un foreach sobre
        /// prio y manda el id elegido); este servicio fue el primero en asumirlas.
        ///
        /// Cuando el nombre no permite identificarlas, se cae al orden por id, que
        /// en esta base va de más urgente a menos.
        /// </summary>
        private Prios Prioridades()
        {
            if (_prioCache != null) return _prioCache;

            lock (_prioLock)
            {
                if (_prioCache != null) return _prioCache;

                try
                {
                    var filas = _utils.RunQuery("SELECT id_prio, n FROM prio ORDER BY id_prio");
                    if (filas.Count == 0) return null;

                    int PorPrefijo(string prefijo, int respaldo)
                    {
                        foreach (var f in filas)
                        {
                            string n = (f["n"]?.ToString() ?? "").Trim().ToLowerInvariant();
                            if (n.StartsWith(prefijo)) return Convert.ToInt32(f["id_prio"]);
                        }
                        return respaldo;
                    }

                    int primera = Convert.ToInt32(filas[0]["id_prio"]);
                    int ultima = Convert.ToInt32(filas[^1]["id_prio"]);
                    int enmedio = Convert.ToInt32(filas[filas.Count / 2]["id_prio"]);

                    _prioCache = new Prios
                    {
                        Alta = PorPrefijo("alt", primera),
                        Media = PorPrefijo("med", enmedio),
                        Baja = PorPrefijo("baj", ultima)
                    };

                    return _prioCache;
                }
                catch
                {
                    return null;
                }
            }
        }

        // --------------------------------------------------------------------
        // Catálogos
        // --------------------------------------------------------------------

        // La categoría no cambia en caliente y se consulta en cada error.
        private static int _idCategoriaCache;

        private int ObtenerCategoria()
        {
            if (_idCategoriaCache > 0) return _idCategoriaCache;

            try
            {
                var f = _utils.RunQuery(
                    "SELECT id_cat FROM tkts_categorias WHERE n_cat = @n LIMIT 1",
                    new Dictionary<string, object> { { "n", NombreCategoria } });

                if (f.Count == 0) return 0;   // falta correr sql/soporte_errores_auto.sql

                _idCategoriaCache = Convert.ToInt32(f[0]["id_cat"]);
                return _idCategoriaCache;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Responsable de la categoría, resuelto en cascada igual que en el alta
        /// manual: hay categorías cuyo responsable apunta a usuarios que ya no
        /// existen, y asignarlo tal cual haría fallar el INSERT por la FK.
        /// </summary>
        private int ObtenerResponsable(int idCategoria)
        {
            if (idCategoria <= 0) return 0;

            try
            {
                var f = _utils.RunQuery(
                    "SELECT COALESCE( " +
                    "    (SELECT c.responsable FROM tkts_categorias c " +
                    "       JOIN usuarios u ON u.usuarioid = c.responsable " +
                    "      WHERE c.id_cat = @id_cat), " +
                    "    (SELECT tur.id_usr FROM tkt_usuario_rol tur " +
                    "       JOIN usuarios u2 ON u2.usuarioid = tur.id_usr " +
                    "      WHERE tur.id_rol_tkt IN (1, 2) ORDER BY tur.id_usr LIMIT 1) " +
                    ") AS responsable",
                    new Dictionary<string, object> { { "id_cat", idCategoria } });

                if (f.Count == 0 || f[0]["responsable"] == null) return 0;
                return Convert.ToInt32(f[0]["responsable"]);
            }
            catch { return 0; }
        }

        private object ObtenerArea(int idUsuario)
        {
            try
            {
                var f = _utils.RunQuery(
                    "SELECT areaid FROM usuarios WHERE usuarioid = @id",
                    new Dictionary<string, object> { { "id", idUsuario } });

                if (f.Count > 0 && f[0]["areaid"] != null) return f[0]["areaid"];
            }
            catch { }

            return AreaSistemas;
        }

        private static string Recortar(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
