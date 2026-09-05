using BOS_ERP.Controllers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.controllers
{
    public partial class RecursosHumanosController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender _emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper _correoHelper;
        private readonly BOS_ERP.Services.SalasEstadoService _estadoSalas;
        private readonly IWebHostEnvironment _env;

        private const string AreaSalas = "Recursos Humanos";

        public RecursosHumanosController(BOS_ERP.Services.EmailSender emailSender, BOS_ERP.Helpers.CorreoHelper correoHelper,
            BOS_ERP.Services.SalasEstadoService estadoSalas, IWebHostEnvironment env)
        {
            _emailSender = emailSender;
            _correoHelper = correoHelper;
            _estadoSalas = estadoSalas;
            _env = env;
        }

        #region Helpers

        private bool EsAdministradorSalas() => DoesUserHasArea(User.Identity.Name, AreaSalas);

        private int UsuarioActual() => GetUserId(User.Identity.Name);

        private int EmpresaActual() => HttpContext.Session.GetInt32("Empresa") ?? 0;

        private static string Folio(int id) => $"SALA-{id:D6}";

        private static string ValidarHorario(DateTime inicio, DateTime fin)
        {
            if (fin <= inicio)
                return "La hora de fin debe ser posterior a la de inicio";

            if (inicio.Date != fin.Date)
                return "Una reservación no puede abarcar más de un día";

            if (inicio.DayOfWeek == DayOfWeek.Sunday)
                return "Los domingos no son días laborales";

            var apertura = new TimeSpan(9, 0, 0);
            var cierre = inicio.DayOfWeek == DayOfWeek.Saturday ? new TimeSpan(14, 0, 0) : new TimeSpan(18, 0, 0);

            if (inicio.TimeOfDay < apertura || fin.TimeOfDay > cierre)
                return inicio.DayOfWeek == DayOfWeek.Saturday
                    ? "Los sábados el horario disponible es de 9:00 a 14:00"
                    : "De lunes a viernes el horario disponible es de 9:00 a 18:00";

            return null;
        }

        private bool ExisteTraslape(int salaId, DateTime inicio, DateTime fin, int excluirId)
        {
            var parameters = new Dictionary<string, object>
            {
                { "sala_id", salaId },
                { "inicio", inicio },
                { "fin", fin },
                { "excluir", excluirId }
            };

            string query = "SELECT COUNT(*) FROM salas_reservadas " +
                "WHERE sala_id = @sala_id " +
                "  AND COALESCE(estado, 'reservada') <> 'cancelada' " +
                "  AND id_sala_reservada <> @excluir " +
                "  AND inicio_apartado < @fin AND fin_apartado > @inicio";

            return Convert.ToInt32(RunScalar(query, parameters)) > 0;
        }

        private Dictionary<string, object> Reservacion(int id)
        {
            var parameters = new Dictionary<string, object>
            {
                { "id", id },
                { "empresa_id", EmpresaActual() }
            };

            string query = "SELECT sr.id_sala_reservada, sr.sala_id, sr.asunto, sr.comentarios, sr.inicio_apartado, " +
                "   sr.fin_apartado, COALESCE(sr.estado, 'reservada') AS estado, sr.apartado_por, " +
                "   cs.nombre AS sala, us.nombre || ' ' || COALESCE(us.apellido, '') AS anfitrion, us.email AS anfitrion_email " +
                "FROM salas_reservadas sr " +
                "INNER JOIN catsalas cs ON cs.id_sala = sr.sala_id " +
                "INNER JOIN usuarios us ON us.usuarioid = sr.apartado_por " +
                "WHERE sr.id_sala_reservada = @id AND cs.empresa_id = @empresa_id";

            return RunQuery(query, parameters).FirstOrDefault();
        }

        private List<Dictionary<string, object>> MinutasDe(int id)
        {
            var parameters = new Dictionary<string, object> { { "id", id } };

            string query = "SELECT ra.archivo_id, ra.nombre_original, ra.fecha, " +
                "   us.nombre || ' ' || COALESCE(us.apellido, '') AS subio " +
                "FROM reservaciones_archivos ra " +
                "INNER JOIN usuarios us ON us.usuarioid = ra.subido_por " +
                "WHERE ra.sala_reservada_id = @id " +
                "ORDER BY ra.fecha";

            return RunQuery(query, parameters);
        }

        /// <summary>
        /// La minuta es el cierre de la junta: sólo se sube cuando la reservación ya
        /// terminó y nunca sobre una cancelada. El estado lo mueve SalasEstadoService en
        /// segundo plano, así que se acepta también la reservación cuya hora de fin ya
        /// pasó aunque el job todavía no la haya marcado como finalizada.
        /// </summary>
        private bool AdmiteMinuta(Dictionary<string, object> cabecera)
        {
            string estado = GetString(cabecera["estado"], "reservada");

            if (estado == "cancelada")
                return false;

            return estado == "finalizada" || Convert.ToDateTime(cabecera["fin_apartado"]) < DateTime.Now;
        }

        private List<Dictionary<string, object>> ParticipantesDe(int id)
        {
            var parameters = new Dictionary<string, object> { { "id", id } };

            string query = "SELECT us.usuarioid, us.nombre || ' ' || COALESCE(us.apellido, '') AS nombre, us.email, " +
                "   COALESCE(ps.estado::text, 'pendiente') AS estado " +
                "FROM participantes_sala ps " +
                "INNER JOIN usuarios us ON us.usuarioid = ps.participante_id " +
                "WHERE ps.sala_reservada_id = @id " +
                "ORDER BY 2";

            return RunQuery(query, parameters);
        }

        private List<object> Mapear(List<Dictionary<string, object>> reservaciones, int usuarioId, bool esAdmin)
        {
            if (reservaciones.Count == 0)
                return new List<object>();

            var ids = reservaciones.Select(r => Convert.ToInt32(r["id_sala_reservada"])).ToArray();

            var parameters = new Dictionary<string, object> { { "ids", ids } };
            string query = "SELECT ps.sala_reservada_id, us.usuarioid, " +
                "   us.nombre || ' ' || COALESCE(us.apellido, '') AS nombre, " +
                "   COALESCE(ps.estado::text, 'pendiente') AS estado " +
                "FROM participantes_sala ps " +
                "INNER JOIN usuarios us ON us.usuarioid = ps.participante_id " +
                "WHERE ps.sala_reservada_id = ANY(@ids) " +
                "ORDER BY 3";

            var participantes = RunQuery(query, parameters)
                .GroupBy(p => Convert.ToInt32(p["sala_reservada_id"]))
                .ToDictionary(g => g.Key, g => g.ToList());

            query = "SELECT ra.archivo_id, ra.sala_reservada_id, ra.nombre_original, ra.fecha, " +
                "   us.nombre || ' ' || COALESCE(us.apellido, '') AS subio " +
                "FROM reservaciones_archivos ra " +
                "INNER JOIN usuarios us ON us.usuarioid = ra.subido_por " +
                "WHERE ra.sala_reservada_id = ANY(@ids) " +
                "ORDER BY ra.fecha";

            var minutas = RunQuery(query, parameters)
                .GroupBy(a => Convert.ToInt32(a["sala_reservada_id"]))
                .ToDictionary(g => g.Key, g => g.ToList());

            return reservaciones.Select(r =>
            {
                int id = Convert.ToInt32(r["id_sala_reservada"]);
                int organizador = Convert.ToInt32(r["apartado_por"]);
                var suyos = participantes.TryGetValue(id, out var lista) ? lista : new List<Dictionary<string, object>>();

                string estado = GetString(r["estado"], "reservada");
                bool esOrganizador = organizador == usuarioId;
                var mio = suyos.FirstOrDefault(p => Convert.ToInt32(p["usuarioid"]) == usuarioId);
                bool esParticipante = esOrganizador || mio != null;
                bool verDetalle = esParticipante || esAdmin;
                bool termino = Convert.ToDateTime(r["fin_apartado"]) < DateTime.Now;
                bool cerrada = estado == "cancelada" || estado == "finalizada" || termino;

                var actas = minutas.TryGetValue(id, out var archivos) ? archivos : new List<Dictionary<string, object>>();

                // La minuta cierra la junta: se sube cuando ya terminó y nunca sobre una
                // cancelada. La administra quien organizó, o Recursos Humanos.
                bool admiteMinuta = estado != "cancelada" && (estado == "finalizada" || termino);
                bool administraMinutas = esOrganizador || esAdmin;

                return (object)new
                {
                    id,
                    salaId = Convert.ToInt32(r["sala_id"]),
                    apartadoPor = organizador,
                    sala = GetString(r["sala"]),
                    inicio = Convert.ToDateTime(r["inicio_apartado"]).ToString("s"),
                    fin = Convert.ToDateTime(r["fin_apartado"]).ToString("s"),
                    estado,
                    anfitrion = GetString(r["anfitrion"], ""),
                    anfitrionEmail = verDetalle ? GetString(r["anfitrion_email"]) : null,
                    asunto = verDetalle ? GetString(r["asunto"]) : null,
                    comentarios = verDetalle ? GetString(r["comentarios"]) : null,
                    motivo = verDetalle && r.ContainsKey("motivo_cancelado") ? GetString(r["motivo_cancelado"]) : null,
                    participantes = verDetalle
                        ? suyos.Select(p => new
                        {
                            id = Convert.ToInt32(p["usuarioid"]),
                            nombre = GetString(p["nombre"], ""),
                            estado = GetString(p["estado"], "pendiente")
                        }).ToList()
                        : null,
                    totalParticipantes = suyos.Count,
                    confirmados = suyos.Count(p => GetString(p["estado"]) == "asistira"),
                    rechazados = suyos.Count(p => GetString(p["estado"]) == "no_asistira"),
                    pendientes = suyos.Count(p => GetString(p["estado"], "pendiente") == "pendiente"),
                    miEstado = esOrganizador ? "asistira" : (mio == null ? null : GetString(mio["estado"], "pendiente")),
                    esOrganizador,
                    esParticipante,
                    verDetalle,
                    puedeConfirmar = esParticipante && !esOrganizador && !cerrada,
                    puedeEditar = esOrganizador && !cerrada,
                    puedeCancelar = (esOrganizador || esAdmin) && !cerrada,
                    minutas = verDetalle
                        ? actas.Select(a => new
                        {
                            id = Convert.ToInt32(a["archivo_id"]),
                            nombre = GetString(a["nombre_original"], ""),
                            fecha = Convert.ToDateTime(a["fecha"]).ToString("s"),
                            subio = GetString(a["subio"], "")
                        }).ToList()
                        : null,
                    totalMinutas = actas.Count,
                    maxMinutas = BOS_ERP.Helpers.MinutasSalas.MaximoArchivos,
                    admiteMinuta,
                    puedeSubirMinuta = administraMinutas && admiteMinuta && actas.Count < BOS_ERP.Helpers.MinutasSalas.MaximoArchivos,
                    puedeBorrarMinuta = administraMinutas && admiteMinuta
                };
            }).ToList();
        }

        private async Task NotificarAsync(string tipo, int reservacionId, Dictionary<string, object> cabecera,
            List<Dictionary<string, object>> participantes, string motivo = null)
        {
            int organizador = Convert.ToInt32(cabecera["apartado_por"]);
            var nombres = participantes.Select(p => GetString(p["nombre"], "")).ToList();

            var destinatarios = participantes
                .Where(p => Convert.ToInt32(p["usuarioid"]) != organizador && !string.IsNullOrWhiteSpace(GetString(p["email"])))
                .ToList();

            foreach (var destinatario in destinatarios)
            {
                try
                {
                    var modelo = new ReservacionSalaEmailModel
                    {
                        Folio = Folio(reservacionId),
                        Tipo = tipo,
                        Asunto = GetString(cabecera["asunto"]),
                        Comentarios = GetString(cabecera["comentarios"]),
                        Sala = GetString(cabecera["sala"]),
                        Inicio = Convert.ToDateTime(cabecera["inicio_apartado"]),
                        Fin = Convert.ToDateTime(cabecera["fin_apartado"]),
                        Organizador = GetString(cabecera["anfitrion"], ""),
                        OrganizadorEmail = GetString(cabecera["anfitrion_email"]),
                        Destinatario = GetString(destinatario["nombre"], ""),
                        Motivo = motivo,
                        Participantes = nombres,
                        URL = Url.Action("SolicitudSalas", "RecursosHumanos", new { reservacion = reservacionId }, Request.Scheme)
                    };

                    string asunto = tipo switch
                    {
                        "cancelacion" => $"Cancelada: {modelo.Asunto} ({modelo.Folio})",
                        "actualizacion" => $"Actualizada: {modelo.Asunto} ({modelo.Folio})",
                        _ => $"Invitación: {modelo.Asunto} ({modelo.Folio})"
                    };

                    string html = await _emailSender.RenderViewToStringAsync("~/Views/Email/_ReservacionSala.cshtml", modelo);
                    string emailDestinatario = GetString(destinatario["email"]);
                    string uid = $"reservacion-{reservacionId}@sellosyretenes.com";

                    int sequence = Convert.ToInt32(
                        cabecera.ContainsKey("sequence")
                            ? cabecera["sequence"]
                            : 0
                    );

                    await _correoHelper.EnviarCorreoReservacionAsync(
                        destinatario: emailDestinatario,
                        asunto: asunto,
                        mensajeHtml: html,
                        uid: uid,
                        inicio: modelo.Inicio,
                        fin: modelo.Fin,
                        titulo: modelo.Asunto,
                        descripcion: modelo.Comentarios,
                        sala: modelo.Sala,
                        organizador: modelo.Organizador,
                        organizadorEmail: modelo.OrganizadorEmail,
                        tipo: tipo,
                        sequence: sequence
                    );

                    _ = SendNotificationInterno(GetString(destinatario["nombre"], ""), GetString(destinatario["email"]), new
                    {
                        icon = "info",
                        modulo = "salas",
                        title = $"Reunion programada para {Convert.ToDateTime(cabecera["inicio_apartado"])}",
                        message = $"El usuario {GetString(cabecera["anfitrion"], "")} ha reservado una sala para una reubion con el asunto: {GetString(cabecera["asunto"])} y te agrego como participante.",
                        buttons = new[] {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{Url.Action("SolicitudSalas", "RecursosHumanos", new { reservacion = reservacionId }, Request.Scheme)}')" },
                    },
                        timer = 0
                    });
                }
                catch
                {
                    LogErrorHelper.RegistrarLog(
                        "Debug",
                        Folio(reservacionId),
                        $"Error enviando correo de reservación a {GetString(destinatario["email"])}",
                        nivel: "DEBUG"
                    );
                }
            }
        }

        #endregion

        #region Datos Generales

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetCatalogosSalas()
        {
            var parameters = new Dictionary<string, object> { { "empresa_id", EmpresaActual() } };

            string query = "SELECT id_sala, nombre, descripcion " +
                "FROM catsalas " +
                "WHERE empresa_id = @empresa_id " +
                "ORDER BY nombre";
            var salas = RunQuery(query, parameters);

            query = "SELECT usuarioid, nombre || ' ' || COALESCE(apellido, '') AS nombre, email " +
                "FROM usuarios " +
                "WHERE empresaid = @empresa_id AND activo = true " +
                "ORDER BY 2";
            var usuarios = RunQuery(query, parameters);

            return Json(new
            {
                salas,
                usuarios,
                usuarioId = UsuarioActual(),
                esAdmin = EsAdministradorSalas()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetReservaciones(DateTime? desde, DateTime? hasta, int sala_id = 0)
        {
            _estadoSalas.SincronizarConThrottle();

            int usuarioId = UsuarioActual();
            bool esAdmin = EsAdministradorSalas();

            var parameters = new Dictionary<string, object>
            {
                { "empresa_id", EmpresaActual() },
                { "desde", desde ?? DateTime.Today.AddMonths(-1) },
                { "hasta", hasta ?? DateTime.Today.AddMonths(2) },
                { "sala_id", sala_id }
            };

            string query = "SELECT sr.id_sala_reservada, sr.sala_id, sr.asunto, sr.comentarios, sr.inicio_apartado, " +
                "   sr.fin_apartado, COALESCE(sr.estado, 'reservada') AS estado, sr.motivo_cancelado, sr.apartado_por, " +
                "   cs.nombre AS sala, us.nombre || ' ' || COALESCE(us.apellido, '') AS anfitrion, us.email AS anfitrion_email " +
                "FROM salas_reservadas sr " +
                "INNER JOIN catsalas cs ON cs.id_sala = sr.sala_id " +
                "INNER JOIN usuarios us ON us.usuarioid = sr.apartado_por " +
                "WHERE cs.empresa_id = @empresa_id " +
                "  AND (@sala_id = 0 OR sr.sala_id = @sala_id) " +
                "  AND sr.inicio_apartado < @hasta AND sr.fin_apartado > @desde " +
                "ORDER BY sr.inicio_apartado";

            return Json(Mapear(RunQuery(query, parameters), usuarioId, esAdmin));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetAgendaDia(DateTime? dia)
        {
            _estadoSalas.SincronizarConThrottle();

            int usuarioId = UsuarioActual();
            var fecha = (dia ?? DateTime.Today).Date;

            var parameters = new Dictionary<string, object>
            {
                { "empresa_id", EmpresaActual() },
                { "usuario", usuarioId },
                { "desde", fecha },
                { "hasta", fecha.AddDays(1) }
            };

            string query = "SELECT sr.id_sala_reservada, sr.sala_id, sr.asunto, sr.comentarios, sr.inicio_apartado, " +
                "   sr.fin_apartado, COALESCE(sr.estado::text, 'reservada') AS estado, sr.motivo_cancelado, sr.apartado_por, " +
                "   cs.nombre AS sala, us.nombre || ' ' || COALESCE(us.apellido, '') AS anfitrion, us.email AS anfitrion_email " +
                "FROM salas_reservadas sr " +
                "INNER JOIN catsalas cs ON cs.id_sala = sr.sala_id " +
                "INNER JOIN usuarios us ON us.usuarioid = sr.apartado_por " +
                "WHERE cs.empresa_id = @empresa_id " +
                "  AND COALESCE(sr.estado::text, 'reservada') <> 'cancelada' " +
                "  AND sr.fin_apartado > NOW() " +
                "  AND sr.inicio_apartado >= @desde AND sr.inicio_apartado < @hasta " +
                "  AND (sr.apartado_por = @usuario OR EXISTS ( " +
                "        SELECT 1 FROM participantes_sala ps " +
                "        WHERE ps.sala_reservada_id = sr.id_sala_reservada AND ps.participante_id = @usuario)) " +
                "ORDER BY sr.inicio_apartado";

            return Json(Mapear(RunQuery(query, parameters), usuarioId, EsAdministradorSalas()));
        }

        #endregion

        #region Salas

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GuardarSala(int id_sala, string nombre, string descripcion)
        {
            try
            {
                if (!EsAdministradorSalas())
                    return Json(new { icon = "error", title = "Sin permisos", html = "Solo Recursos Humanos puede administrar las salas", showCancelButton = false });

                if (string.IsNullOrWhiteSpace(nombre))
                    return Json(new { icon = "error", title = "Error", html = "El nombre de la sala es obligatorio", showCancelButton = false });

                var parameters = new Dictionary<string, object>
                {
                    { "id_sala", id_sala },
                    { "nombre", nombre.Trim() },
                    { "descripcion", string.IsNullOrWhiteSpace(descripcion) ? (object)DBNull.Value : descripcion.Trim() },
                    { "empresa_id", EmpresaActual() },
                    { "usuario", UsuarioActual() }
                };

                string query = "SELECT COUNT(*) FROM catsalas " +
                    "WHERE empresa_id = @empresa_id AND LOWER(nombre) = LOWER(@nombre) AND id_sala <> @id_sala";

                if (Convert.ToInt32(RunScalar(query, parameters)) > 0)
                    return Json(new { icon = "error", title = "Error", html = "Ya existe una sala con ese nombre", showCancelButton = false });

                if (id_sala > 0)
                {
                    query = "UPDATE catsalas SET nombre = @nombre, descripcion = @descripcion " +
                        "WHERE id_sala = @id_sala AND empresa_id = @empresa_id";
                    RunUpdate(query, parameters);

                    return Json(new { icon = "success", title = "Sala actualizada", html = "Los datos de la sala se guardaron correctamente", showCancelButton = false });
                }

                query = "INSERT INTO catsalas (nombre, descripcion, empresa_id, creada_por) " +
                    "VALUES (@nombre, @descripcion, @empresa_id, @usuario)";
                RunUpdate(query, parameters);

                return Json(new { icon = "success", title = "Sala registrada", html = $"La sala {nombre.Trim()} ya está disponible para reservarse", showCancelButton = false });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", html = ex.Message, showCancelButton = false });
            }
        }

        #endregion

        #region Reservaciones

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<JsonResult> ReservarSala(int sala_id, string asunto, string comentarios,
            DateTime inicio_apartado, DateTime fin_apartado, List<int> participantes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(asunto))
                    return Json(new { icon = "error", title = "Error", html = "El asunto de la reunión es obligatorio", showCancelButton = false });

                string invalido = ValidarHorario(inicio_apartado, fin_apartado);
                if (invalido != null)
                    return Json(new { icon = "error", title = "Horario no válido", html = invalido, showCancelButton = false });

                if (ExisteTraslape(sala_id, inicio_apartado, fin_apartado, 0))
                    return Json(new { icon = "error", title = "Sala ocupada", html = "La sala ya está reservada en ese horario", showCancelButton = false });

                if (participantes.Count() == 0)
                    return Json(new { icon = "error", title = "Sin participantes", html = "Agrega por lo menos un participante", showCancelButton = false });

                participantes ??= new List<int>();

                int apartadoPor = UsuarioActual();
                int salaReservada;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var parameters = new Dictionary<string, object>
                            {
                                { "sala_id", sala_id },
                                { "asunto", asunto.Trim() },
                                { "comentarios", string.IsNullOrWhiteSpace(comentarios) ? (object)DBNull.Value : comentarios.Trim() },
                                { "inicio_apartado", inicio_apartado },
                                { "fin_apartado", fin_apartado },
                                { "apartado_por", apartadoPor }
                            };

                            string query = "INSERT INTO salas_reservadas " +
                                "   (sala_id, asunto, comentarios, inicio_apartado, fin_apartado, apartado_por) " +
                                "VALUES " +
                                "   (@sala_id, @asunto, @comentarios, @inicio_apartado, @fin_apartado, @apartado_por) " +
                                "RETURNING id_sala_reservada";
                            salaReservada = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                            parameters["sala_reservada_id"] = salaReservada;
                            query = "INSERT INTO participantes_sala (sala_reservada_id, participante_id, estado) " +
                                "VALUES (@sala_reservada_id, @participante_id, @estado::estado_participante_sala)";

                            foreach (var participante in participantes.Append(apartadoPor).Distinct())
                            {
                                parameters["participante_id"] = participante;
                                parameters["estado"] = participante == apartadoPor ? "asistira" : "pendiente";
                                RunUpdate(query, parameters, false, conn, tx);
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                var cabecera = Reservacion(salaReservada);
                await NotificarAsync("invitacion", salaReservada, cabecera, ParticipantesDe(salaReservada));

                return Json(new { icon = "success", title = "Sala reservada", html = $"Tu reservación quedó registrada con el folio {Folio(salaReservada)}", showCancelButton = false });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", html = ex.Message, showCancelButton = false });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<JsonResult> EditarReservacion(int sala_reservada_id, int sala_id, string asunto, string comentarios,
            DateTime inicio_apartado, DateTime fin_apartado, List<int> participantes)
        {
            try
            {
                var actual = Reservacion(sala_reservada_id);

                if (actual == null)
                    return Json(new { icon = "error", title = "Error", html = "La reservación no existe", showCancelButton = false });

                if (Convert.ToInt32(actual["apartado_por"]) != UsuarioActual())
                    return Json(new { icon = "error", title = "Sin permisos", html = "Solo quien creó la reservación puede editarla", showCancelButton = false });

                if (GetString(actual["estado"]) == "cancelada")
                    return Json(new { icon = "error", title = "Error", html = "Una reservación cancelada no puede editarse", showCancelButton = false });

                if (string.IsNullOrWhiteSpace(asunto))
                    return Json(new { icon = "error", title = "Error", html = "El asunto de la reunión es obligatorio", showCancelButton = false });

                string invalido = ValidarHorario(inicio_apartado, fin_apartado);
                if (invalido != null)
                    return Json(new { icon = "error", title = "Horario no válido", html = invalido });

                if (ExisteTraslape(sala_id, inicio_apartado, fin_apartado, sala_reservada_id))
                    return Json(new { icon = "error", title = "Sala ocupada", html = "La sala ya está reservada en ese horario", showCancelButton = false });

                if (participantes.Count() == 0)
                    return Json(new { icon = "error", title = "Sin participantes", html = "Agrega por lo menos un participante", showCancelButton = false });

                participantes ??= new List<int>();

                int apartadoPor = UsuarioActual();

                bool cambioLogistico = Convert.ToInt32(actual["sala_id"]) != sala_id
                    || Convert.ToDateTime(actual["inicio_apartado"]) != inicio_apartado
                    || Convert.ToDateTime(actual["fin_apartado"]) != fin_apartado;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var parameters = new Dictionary<string, object>
                            {
                                { "sala_reservada_id", sala_reservada_id },
                                { "sala_id", sala_id },
                                { "asunto", asunto.Trim() },
                                { "comentarios", string.IsNullOrWhiteSpace(comentarios) ? (object)DBNull.Value : comentarios.Trim() },
                                { "inicio_apartado", inicio_apartado },
                                { "fin_apartado", fin_apartado }
                            };

                            string query = "UPDATE salas_reservadas " +
                                "SET sala_id = @sala_id, asunto = @asunto, comentarios = @comentarios, " +
                                "    inicio_apartado = @inicio_apartado, fin_apartado = @fin_apartado " +
                                "WHERE id_sala_reservada = @sala_reservada_id";
                            RunUpdate(query, parameters, false, conn, tx);

                            query = "SELECT participante_id FROM participantes_sala WHERE sala_reservada_id = @sala_reservada_id";
                            var previos = RunQuery(query, parameters, false, conn, tx)
                                .Select(x => Convert.ToInt32(x["participante_id"]))
                                .ToList();

                            var finales = participantes.Append(apartadoPor).Distinct().ToList();
                            var quitados = previos.Except(finales).ToArray();

                            if (quitados.Length > 0)
                            {
                                parameters["quitados"] = quitados;
                                query = "DELETE FROM participantes_sala " +
                                    "WHERE sala_reservada_id = @sala_reservada_id AND participante_id = ANY(@quitados)";
                                RunUpdate(query, parameters, false, conn, tx);
                            }

                            query = "INSERT INTO participantes_sala (sala_reservada_id, participante_id, estado) " +
                                "VALUES (@sala_reservada_id, @participante_id, @estado::estado_participante_sala)";

                            foreach (var participante in finales.Except(previos))
                            {
                                parameters["participante_id"] = participante;
                                parameters["estado"] = participante == apartadoPor ? "asistira" : "pendiente";
                                RunUpdate(query, parameters, false, conn, tx);
                            }

                            // Cambiar sala u horario invalida lo que ya habían contestado:
                            // vuelven a quedar pendientes de confirmar.
                            if (cambioLogistico)
                            {
                                parameters["apartado_por"] = apartadoPor;
                                query = "UPDATE participantes_sala " +
                                    "SET estado = 'pendiente'::estado_participante_sala, fecha_confirmacion = NULL " +
                                    "WHERE sala_reservada_id = @sala_reservada_id AND participante_id <> @apartado_por";
                                RunUpdate(query, parameters, false, conn, tx);
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                var cabecera = Reservacion(sala_reservada_id);
                await NotificarAsync("actualizacion", sala_reservada_id, cabecera, ParticipantesDe(sala_reservada_id));

                return Json(new { icon = "success", title = "Reservación actualizada", html = "Se notificó a los participantes de los cambios", showCancelButton = false });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", html = ex.Message, showCancelButton = false });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<JsonResult> CancelarReservacion(int sala_reservada_id, string motivo)
        {
            try
            {
                var cabecera = Reservacion(sala_reservada_id);

                if (cabecera == null)
                    return Json(new { icon = "error", title = "Error", html = "La reservación no existe", showCancelButton = false });

                bool esOrganizador = Convert.ToInt32(cabecera["apartado_por"]) == UsuarioActual();

                if (!esOrganizador && !EsAdministradorSalas())
                    return Json(new { icon = "error", title = "Sin permisos", html = "Solo el organizador o Recursos Humanos pueden cancelar esta reservación", showCancelButton = false });

                if (GetString(cabecera["estado"]) == "cancelada")
                    return Json(new { icon = "error", title = "Error", html = "La reservación ya estaba cancelada", showCancelButton = false });

                if (string.IsNullOrWhiteSpace(motivo))
                    return Json(new { icon = "error", title = "Error", html = "Indica el motivo de la cancelación", showCancelButton = false });

                var participantes = ParticipantesDe(sala_reservada_id);

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var parameters = new Dictionary<string, object>
                            {
                                { "sala_reservada_id", sala_reservada_id },
                                { "motivo", motivo.Trim() },
                                { "usuario", GetUserId(User.Identity.Name) }
                            };

                            string query = "UPDATE salas_reservadas " +
                                "SET estado = 'cancelada'::estado_sala, motivo_cancelado = @motivo, fecha_cancelacion = NOW(), cancelado_por = @usuario " +
                                "WHERE id_sala_reservada = @sala_reservada_id";
                            RunUpdate(query, parameters, false, conn, tx);

                            query = "UPDATE participantes_sala SET estado = 'cancelada'::estado_participante_sala, fecha_confirmacion = NOW() " +
                                "WHERE sala_reservada_id = @sala_reservada_id";
                            RunUpdate(query, parameters, false, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                await NotificarAsync("cancelacion", sala_reservada_id, cabecera, participantes, motivo.Trim());

                return Json(new { icon = "success", title = "Reservación cancelada", html = "La sala quedó liberada y se notificó a los participantes", showCancelButton = false });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", html = ex.Message, showCancelButton = false });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult ConfirmarAsistencia(int sala_reservada_id, string respuesta)
        {
            try
            {
                if (respuesta != "asistira" && respuesta != "no_asistira")
                    return Json(new { icon = "error", title = "Error", html = "Respuesta no válida", showCancelButton = false });

                var cabecera = Reservacion(sala_reservada_id);

                if (cabecera == null)
                    return Json(new { icon = "error", title = "Error", html = "La reservación no existe", showCancelButton = false });

                if (GetString(cabecera["estado"]) == "cancelada")
                    return Json(new { icon = "error", title = "Error", html = "La reservación está cancelada", showCancelButton = false });

                int usuarioId = UsuarioActual();

                if (Convert.ToInt32(cabecera["apartado_por"]) == usuarioId)
                    return Json(new { icon = "info", title = "Eres el organizador", html = "Tu asistencia se da por confirmada", showCancelButton = false });

                if (Convert.ToDateTime(cabecera["fin_apartado"]) < DateTime.Now)
                    return Json(new { icon = "error", title = "Error", html = "La reunión ya terminó", showCancelButton = false });

                var parameters = new Dictionary<string, object>
                {
                    { "sala_reservada_id", sala_reservada_id },
                    { "participante_id", usuarioId },
                    { "estado", respuesta }
                };

                string query = "SELECT COUNT(*) FROM participantes_sala " +
                    "WHERE sala_reservada_id = @sala_reservada_id AND participante_id = @participante_id";

                if (Convert.ToInt32(RunScalar(query, parameters)) == 0)
                    return Json(new { icon = "error", title = "Sin permisos", html = "No estás invitado a esta reunión", showCancelButton = false });

                query = "UPDATE participantes_sala " +
                    "SET estado = @estado::estado_participante_sala, fecha_confirmacion = NOW() " +
                    "WHERE sala_reservada_id = @sala_reservada_id AND participante_id = @participante_id";
                RunUpdate(query, parameters);

                parameters = new Dictionary<string, object>
                {
                    { "organizador", Convert.ToInt32(cabecera["apartado_por"]) },
                    { "participante", usuarioId }
                };

                query = "SELECT usuarioid, nombreusuario, email, nombre || ' ' || COALESCE(apellido, '') AS nombre " +
                    "FROM usuarios WHERE usuarioid IN (@organizador, @participante)";
                var gente = RunQuery(query, parameters);

                var organizador = gente.FirstOrDefault(u => Convert.ToInt32(u["usuarioid"]) == Convert.ToInt32(cabecera["apartado_por"]));
                var quienResponde = gente.FirstOrDefault(u => Convert.ToInt32(u["usuarioid"]) == usuarioId);

                bool asiste = respuesta == "asistira";

                if (organizador != null)
                {
                    _ = SendNotificationInterno(GetString(organizador["nombreusuario"], ""), GetString(organizador["email"]), new
                    {
                        icon = asiste ? "success" : "warning",
                        modulo = "salas",
                        title = asiste ? "Asistencia confirmada" : "No podrá asistir",
                        message = $"{GetString(quienResponde?["nombre"], "Un participante")} {(asiste ? "confirmó su asistencia a" : "no podrá asistir a")} " +
                                  $"\"{GetString(cabecera["asunto"])}\" del {Convert.ToDateTime(cabecera["inicio_apartado"]):dd/MM/yyyy HH:mm}.",
                        buttons = new[] {
                            new { text = "Ver Detalles", style = "primary", action = $"window.open('{Url.Action("SolicitudSalas", "RecursosHumanos", new { reservacion = sala_reservada_id }, Request.Scheme)}')" },
                        },
                        timer = 0
                    });
                }

                return Json(new
                {
                    icon = "success",
                    title = asiste ? "Asistencia confirmada" : "Respuesta registrada",
                    html = asiste
                        ? "Se avisó al organizador que sí asistirás"
                        : "Se avisó al organizador que no podrás asistir",
                    showCancelButton = false
                });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", html = ex.Message, showCancelButton = false });
            }
        }

        #endregion

        #region Minutas

        /// <summary>
        /// Sube la minuta de una reservación ya terminada: el acta o el plan que salió
        /// de la junta, en PDF y hasta tres por reservación.
        ///
        /// El límite se cuenta contra lo que la reservación ya tiene, no contra el lote,
        /// para que no se pueda llegar a nueve subiendo de tres en tres.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(BOS_ERP.Helpers.MinutasSalas.TamanoMaximoPeticionBytes)]
        public async Task<JsonResult> SubirMinuta(int sala_reservada_id, List<IFormFile> archivos)
        {
            try
            {
                var cabecera = Reservacion(sala_reservada_id);

                if (cabecera == null)
                    return Json(new { icon = "error", title = "Error", html = "La reservación no existe", showCancelButton = false });

                bool esOrganizador = Convert.ToInt32(cabecera["apartado_por"]) == UsuarioActual();

                if (!esOrganizador && !EsAdministradorSalas())
                    return Json(new { icon = "error", title = "Sin permisos", html = "Solo el organizador o Recursos Humanos pueden subir la minuta", showCancelButton = false });

                if (GetString(cabecera["estado"]) == "cancelada")
                    return Json(new { icon = "error", title = "Error", html = "Una reservación cancelada no lleva minuta", showCancelButton = false });

                if (!AdmiteMinuta(cabecera))
                    return Json(new { icon = "error", title = "La reunión no ha terminado", html = "La minuta se sube una vez que la reservación finaliza", showCancelButton = false });

                int yaGuardadas = MinutasDe(sala_reservada_id).Count;

                string invalido = BOS_ERP.Helpers.MinutasSalas.Validar(archivos, yaGuardadas);
                if (invalido != null)
                    return Json(new { icon = "error", title = "Archivo no válido", html = invalido, showCancelButton = false });

                int guardadas = await BOS_ERP.Helpers.MinutasSalas.GuardarAsync(
                    this, _env.ContentRootPath, archivos, sala_reservada_id, UsuarioActual());

                return Json(new
                {
                    icon = "success",
                    title = guardadas == 1 ? "Minuta subida" : "Minutas subidas",
                    html = $"Se adjuntaron {guardadas} {(guardadas == 1 ? "archivo" : "archivos")} a {Folio(sala_reservada_id)}",
                    showCancelButton = false
                });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", html = ex.Message, showCancelButton = false });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult EliminarMinuta(int archivo_id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id", archivo_id },
                    { "empresa_id", EmpresaActual() }
                };

                string query = "SELECT ra.sala_reservada_id, ra.uuid, ra.extension, ra.nombre_original, " +
                    "   sr.apartado_por " +
                    "FROM reservaciones_archivos ra " +
                    "INNER JOIN salas_reservadas sr ON sr.id_sala_reservada = ra.sala_reservada_id " +
                    "INNER JOIN catsalas cs ON cs.id_sala = sr.sala_id " +
                    "WHERE ra.archivo_id = @id AND cs.empresa_id = @empresa_id";

                var minuta = RunQuery(query, parameters).FirstOrDefault();

                if (minuta == null)
                    return Json(new { icon = "error", title = "Error", html = "La minuta no existe", showCancelButton = false });

                bool esOrganizador = Convert.ToInt32(minuta["apartado_por"]) == UsuarioActual();

                if (!esOrganizador && !EsAdministradorSalas())
                    return Json(new { icon = "error", title = "Sin permisos", html = "Solo el organizador o Recursos Humanos pueden borrar la minuta", showCancelButton = false });

                RunUpdate("DELETE FROM reservaciones_archivos WHERE archivo_id = @id", parameters);

                // El archivo se borra después del registro: si el borrado en disco falla
                // queda un huérfano inerte, mientras que al revés quedaría una fila
                // apuntando a un archivo que ya no está y la descarga daría 404.
                try
                {
                    string rutaFisica = RutaFisicaMinuta(minuta["uuid"]?.ToString() + minuta["extension"]);

                    if (rutaFisica != null && System.IO.File.Exists(rutaFisica))
                        System.IO.File.Delete(rutaFisica);
                }
                catch (Exception ex)
                {
                    LogErrorHelper.RegistrarLog("Salas", Folio(Convert.ToInt32(minuta["sala_reservada_id"])),
                        $"No se pudo borrar del disco la minuta {archivo_id}: {ex.Message}", nivel: "DEBUG");
                }

                return Json(new { icon = "success", title = "Minuta eliminada", html = $"Se quitó {GetString(minuta["nombre_original"], "el archivo")}", showCancelButton = false });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error", html = ex.Message, showCancelButton = false });
            }
        }

        /// <summary>
        /// Entrega el PDF. Como los adjuntos de soporte, la minuta no se sirve como
        /// estático: se exige que quien pide participe en la reunión -o sea Recursos
        /// Humanos-, y el nombre en disco se reconstruye desde el uuid guardado, así que
        /// la petición nunca aporta una ruta.
        /// </summary>
        public IActionResult DescargarMinuta(int id)
        {
            if (id <= 0)
                return NotFound();

            int usuarioId = UsuarioActual();

            var parameters = new Dictionary<string, object>
            {
                { "id", id },
                { "empresa_id", EmpresaActual() },
                { "usuario", usuarioId }
            };

            string query = "SELECT ra.uuid, ra.extension, ra.nombre_original, sr.apartado_por, " +
                "   EXISTS (SELECT 1 FROM participantes_sala ps " +
                "           WHERE ps.sala_reservada_id = sr.id_sala_reservada " +
                "             AND ps.participante_id = @usuario) AS invitado " +
                "FROM reservaciones_archivos ra " +
                "INNER JOIN salas_reservadas sr ON sr.id_sala_reservada = ra.sala_reservada_id " +
                "INNER JOIN catsalas cs ON cs.id_sala = sr.sala_id " +
                "WHERE ra.archivo_id = @id AND cs.empresa_id = @empresa_id";

            var minuta = RunQuery(query, parameters).FirstOrDefault();

            if (minuta == null)
                return NotFound();

            bool participa = Convert.ToInt32(minuta["apartado_por"]) == usuarioId || Convert.ToBoolean(minuta["invitado"]);

            if (!participa && !EsAdministradorSalas())
                return StatusCode(403);

            string nombreArchivo = minuta["uuid"]?.ToString() + minuta["extension"];
            string rutaFisica = RutaFisicaMinuta(nombreArchivo);

            if (rutaFisica == null || !System.IO.File.Exists(rutaFisica))
                return NotFound();

            // Sólo el nombre, sin componentes de ruta que pudieran venir de un registro viejo.
            string nombreDescarga = Path.GetFileName(GetString(minuta["nombre_original"], ""));
            if (string.IsNullOrWhiteSpace(nombreDescarga))
                nombreDescarga = nombreArchivo;

            return PhysicalFile(rutaFisica, "application/pdf", nombreDescarga);
        }

        /// <summary>
        /// Resuelve el archivo dentro de la carpeta de minutas. Devuelve null si la ruta
        /// se sale de ella: cinturón y tirantes, porque aunque el uuid salga de la base,
        /// nada debe poder apuntar fuera.
        /// </summary>
        private string RutaFisicaMinuta(string nombreArchivo)
        {
            var carpeta = Path.GetFullPath(Path.Combine(_env.ContentRootPath, BOS_ERP.Helpers.MinutasSalas.Carpeta));
            var ruta = Path.GetFullPath(Path.Combine(carpeta, nombreArchivo));

            return ruta.StartsWith(carpeta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? ruta
                : null;
        }

        #endregion
    }
}
