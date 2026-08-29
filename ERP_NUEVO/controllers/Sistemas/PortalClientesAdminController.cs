using BOS_ERP.Controllers;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Sistemas
{
    /// <summary>
    /// Administración de accesos al portal de clientes.
    ///
    /// Existe porque el autoservicio no puede cubrir todos los casos legítimos. Ese
    /// camino exige que el correo ya esté en correos_cliente, y esa tabla es la lista
    /// de distribución de las FACTURAS: agregar ahí a la contadora del cliente sólo
    /// para darle portal haría que empezara a recibir todos los CFDI.
    ///
    /// Aquí un administrador verifica la solicitud y da el acceso con el correo que el
    /// cliente pida, sin tocar la distribución fiscal. El autoservicio se queda estrecho
    /// —ahí no interviene ningún humano— y esto es la excepción atendida.
    ///
    /// El administrador NUNCA fija la contraseña: manda el enlace y el cliente la
    /// define. Así nadie de adentro conoce la credencial de un cliente.
    /// </summary>
    [RightAuthorize(new[] { "portal_clientes_admin" })]
    public class PortalClientesAdminController : Utilities
    {
        private readonly IConfiguration _config;
        private readonly CorreoHelper _correo;

        private const string LogTag = "PortalClientesAdmin";

        public PortalClientesAdminController(IConfiguration config, CorreoHelper correo)
        {
            _config = config;
            _correo = correo;
        }

        public IActionResult Index() => View("~/Views/Sistemas/PortalClientes.cshtml");

        // ════════════════════════════════════════════════════════════════
        // Consulta
        // ════════════════════════════════════════════════════════════════

        /// <summary>Accesos existentes, con el estado en que se encuentra cada uno.</summary>
        [HttpGet]
        public JsonResult Accesos(string term = null)
        {
            try
            {
                string filtro = string.IsNullOrWhiteSpace(term)
                    ? ""
                    : @" AND (LOWER(a.correo) LIKE LOWER(@term)
                           OR LOWER(c.cve_cli) LIKE LOWER(@term)
                           OR LOWER(c.n_cli)   LIKE LOWER(@term)) ";

                var parametros = new Dictionary<string, object>();
                if (!string.IsNullOrWhiteSpace(term)) parametros.Add("term", $"%{term}%");

                var filas = RunQuery($@"
                    SELECT a.id_acceso, a.correo, a.activo, a.origen_alta, a.notas,
                           a.ultimo_acceso, a.fecha_creacion,
                           a.intentos_fallidos, a.bloqueado_hasta,
                           a.password_hash IS NOT NULL AS tiene_password,
                           a.token_hash    IS NOT NULL AS tiene_token,
                           a.token_expira,
                           c.id_cliente, c.cve_cli, c.n_cli, c.empresa_id,
                           e.nombre AS empresa,
                           u.nombreusuario AS creado_por
                    FROM portal_clientes_acceso a
                    INNER JOIN catclientes c ON c.id_cliente = a.cliente_id
                    LEFT JOIN empresas e ON e.empresaid = c.empresa_id
                    LEFT JOIN usuarios u ON u.usuarioid = a.creado_por
                    WHERE 1=1 {filtro}
                    ORDER BY a.fecha_creacion DESC
                    LIMIT 300", parametros);

                var accesos = filas.Select(f =>
                {
                    bool activo = f["activo"] is bool b && b;
                    bool tienePassword = f["tiene_password"] is bool p && p;
                    var bloqueado = f["bloqueado_hasta"] as DateTime?;
                    bool estaBloqueado = bloqueado.HasValue && bloqueado.Value > DateTime.Now;

                    return new
                    {
                        idAcceso = Convert.ToInt32(f["id_acceso"]),
                        clienteId = Convert.ToInt32(f["id_cliente"]),
                        clave = f["cve_cli"]?.ToString(),
                        cliente = f["n_cli"]?.ToString(),
                        empresa = f["empresa"]?.ToString(),
                        correo = f["correo"]?.ToString(),
                        origen = f["origen_alta"]?.ToString(),
                        notas = f["notas"]?.ToString(),
                        creadoPor = f["creado_por"]?.ToString(),
                        // Un solo campo con el estado real, para no obligar a la
                        // pantalla a recomponerlo desde cuatro banderas.
                        estado = estaBloqueado ? "Bloqueado"
                               : activo && tienePassword ? "Activo"
                               : "Pendiente de activar",
                        bloqueadoHasta = estaBloqueado ? bloqueado.Value.ToString("dd/MM/yyyy HH:mm") : null,
                        intentosFallidos = f["intentos_fallidos"] is null or DBNull ? 0 : Convert.ToInt32(f["intentos_fallidos"]),
                        ultimoAcceso = f["ultimo_acceso"] is DateTime ua ? ua.ToString("dd/MM/yyyy HH:mm") : "Nunca",
                        creado = f["fecha_creacion"] is DateTime fc ? fc.ToString("dd/MM/yyyy") : ""
                    };
                }).ToList();

                return Json(new { success = true, accesos });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, "", $"Error al listar accesos: {ex.Message}", User.Identity?.Name);
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Intentos de activación que no encontraron cliente. Es la cola de seguimiento:
        /// cada renglón es alguien que quiso entrar y no pudo, y que hoy está esperando
        /// un correo que nunca va a llegar.
        /// </summary>
        [HttpGet]
        public JsonResult IntentosFallidos(int dias = 30)
        {
            try
            {
                var filas = RunQuery(@"
                    SELECT b.fecha, b.correo, b.detalle, b.ip, b.evento
                    FROM portal_clientes_bitacora b
                    WHERE b.evento IN ('activacion_sin_coincidencia', 'activacion_ambigua')
                      AND b.fecha >= NOW() - (@dias || ' days')::interval
                      -- Se ocultan los que ya se resolvieron: si ese correo terminó
                      -- teniendo acceso, el intento dejó de ser un pendiente.
                      AND NOT EXISTS (
                          SELECT 1 FROM portal_clientes_acceso a
                          WHERE a.correo = b.correo
                      )
                    ORDER BY b.fecha DESC
                    LIMIT 200",
                    new Dictionary<string, object> { { "dias", dias } });

                var intentos = filas.Select(f => new
                {
                    fecha = f["fecha"] is DateTime d ? d.ToString("dd/MM/yyyy HH:mm") : "",
                    correo = f["correo"]?.ToString(),
                    detalle = f["detalle"]?.ToString(),
                    ip = f["ip"]?.ToString(),
                    motivo = f["evento"]?.ToString() == "activacion_ambigua"
                        ? "La clave coincide con más de un cliente"
                        : "No hay cliente con esa clave, correo y empresa"
                }).ToList();

                return Json(new { success = true, intentos });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>Clientes, para elegir a quién se le da el acceso.</summary>
        [HttpGet]
        public JsonResult BuscarClientes(string term)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
                    return Json(new { success = true, clientes = new List<object>() });

                var filas = RunQuery(@"
                    SELECT c.id_cliente, c.cve_cli, c.n_cli, c.rfc, c.empresa_id,
                           e.nombre AS empresa,
                           (SELECT COUNT(*) FROM correos_cliente cc WHERE cc.cliente_id = c.id_cliente) AS correos
                    FROM catclientes c
                    LEFT JOIN empresas e ON e.empresaid = c.empresa_id
                    WHERE LOWER(c.cve_cli) LIKE LOWER(@term)
                       OR LOWER(c.n_cli)   LIKE LOWER(@term)
                       OR LOWER(c.rfc)     LIKE LOWER(@term)
                    ORDER BY c.n_cli
                    LIMIT 25",
                    new Dictionary<string, object> { { "term", $"%{term.Trim()}%" } });

                var clientes = filas.Select(f => new
                {
                    id = Convert.ToInt32(f["id_cliente"]),
                    clave = f["cve_cli"]?.ToString(),
                    nombre = f["n_cli"]?.ToString(),
                    rfc = f["rfc"]?.ToString(),
                    empresa = f["empresa"]?.ToString(),
                    correosRegistrados = Convert.ToInt32(f["correos"])
                }).ToList();

                return Json(new { success = true, clientes });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Altas y mantenimiento
        // ════════════════════════════════════════════════════════════════

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Sistemas", Accion = "Alta de acceso al portal de clientes")]
        public async Task<JsonResult> Crear(int clienteId, string correo, string notas)
        {
            correo = (correo ?? "").Trim().ToLowerInvariant();

            if (clienteId <= 0)
                return Json(new { success = false, message = "Selecciona el cliente." });

            if (string.IsNullOrWhiteSpace(correo) || !correo.Contains('@') || correo.Contains(' '))
                return Json(new { success = false, message = "Captura un correo válido." });

            try
            {
                var cliente = RunQuery(
                    "SELECT cve_cli, n_cli FROM catclientes WHERE id_cliente = @id",
                    new Dictionary<string, object> { { "id", clienteId } }).FirstOrDefault();

                if (cliente == null)
                    return Json(new { success = false, message = "El cliente no existe." });

                var existente = RunQuery(@"
                    SELECT id_acceso, activo, password_hash IS NOT NULL AS tiene_password
                    FROM portal_clientes_acceso
                    WHERE cliente_id = @cliente AND correo = @correo",
                    new Dictionary<string, object> { { "cliente", clienteId }, { "correo", correo } })
                    .FirstOrDefault();

                if (existente != null
                    && existente["activo"] is bool act && act
                    && existente["tiene_password"] is bool tp && tp)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Ese correo ya tiene acceso activo para este cliente. " +
                                  "Si el cliente no puede entrar, usa Reenviar enlace."
                    });
                }

                string token = PortalClientesAuth.GenerarToken();

                RunUpdate(@"
                    INSERT INTO portal_clientes_acceso
                        (cliente_id, correo, token_hash, token_expira, token_proposito,
                         activo, creado_por, origen_alta, notas)
                    VALUES (@cliente, @correo, @hash, @expira, 'activacion',
                            false, @usuario, 'administrador', @notas)
                    ON CONFLICT (cliente_id, correo) DO UPDATE
                    SET token_hash      = EXCLUDED.token_hash,
                        token_expira    = EXCLUDED.token_expira,
                        token_proposito = EXCLUDED.token_proposito,
                        creado_por      = EXCLUDED.creado_por,
                        origen_alta     = EXCLUDED.origen_alta,
                        notas           = EXCLUDED.notas",
                    new Dictionary<string, object>
                    {
                        { "cliente", clienteId },
                        { "correo",  correo },
                        { "hash",    PortalClientesAuth.HashearToken(token) },
                        { "expira",  DateTime.Now.Add(PortalClientesAuth.VigenciaToken) },
                        { "usuario", GetUserId(User.Identity.Name) },
                        { "notas",   string.IsNullOrWhiteSpace(notas) ? (object)DBNull.Value : notas.Trim() }
                    });

                Bitacora(clienteId, correo, "alta_administrador",
                    $"creado por {User.Identity?.Name}");

                string mensaje = await EnviarEnlace(clienteId, correo, token,
                    cliente["n_cli"]?.ToString(), cliente["cve_cli"]?.ToString());

                return Json(new { success = true, message = mensaje });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, correo,
                    $"Error al crear acceso: {ex.Message}", User.Identity?.Name);

                return Json(new { success = false, message = "No se pudo crear el acceso: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Sistemas", Accion = "Reenvío de enlace del portal de clientes")]
        public async Task<JsonResult> ReenviarEnlace(int idAcceso)
        {
            try
            {
                var acceso = RunQuery(@"
                    SELECT a.cliente_id, a.correo, c.cve_cli, c.n_cli
                    FROM portal_clientes_acceso a
                    INNER JOIN catclientes c ON c.id_cliente = a.cliente_id
                    WHERE a.id_acceso = @id",
                    new Dictionary<string, object> { { "id", idAcceso } }).FirstOrDefault();

                if (acceso == null)
                    return Json(new { success = false, message = "El acceso no existe." });

                string token = PortalClientesAuth.GenerarToken();

                RunUpdate(@"
                    UPDATE portal_clientes_acceso
                    SET token_hash      = @hash,
                        token_expira    = @expira,
                        token_proposito = 'restablecer'
                    WHERE id_acceso = @id",
                    new Dictionary<string, object>
                    {
                        { "hash",   PortalClientesAuth.HashearToken(token) },
                        { "expira", DateTime.Now.Add(PortalClientesAuth.VigenciaToken) },
                        { "id",     idAcceso }
                    });

                int clienteId = Convert.ToInt32(acceso["cliente_id"]);
                string correo = acceso["correo"]?.ToString();

                Bitacora(clienteId, correo, "reenvio_administrador", $"por {User.Identity?.Name}");

                string mensaje = await EnviarEnlace(clienteId, correo, token,
                    acceso["n_cli"]?.ToString(), acceso["cve_cli"]?.ToString());

                return Json(new { success = true, message = mensaje });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "No se pudo reenviar: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Sistemas", Accion = "Desbloqueo de acceso al portal de clientes")]
        public JsonResult Desbloquear(int idAcceso)
        {
            try
            {
                RunUpdate(@"
                    UPDATE portal_clientes_acceso
                    SET intentos_fallidos = 0, bloqueado_hasta = NULL
                    WHERE id_acceso = @id",
                    new Dictionary<string, object> { { "id", idAcceso } });

                BitacoraPorAcceso(idAcceso, "desbloqueo_administrador", $"por {User.Identity?.Name}");

                return Json(new { success = true, message = "Cuenta desbloqueada." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Suspende o reactiva un acceso. No se borra: la bitácora referencia estos
        /// accesos y borrarlos dejaría huérfano el historial de quién vio qué.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Sistemas", Accion = "Cambio de estado de acceso al portal de clientes")]
        public JsonResult CambiarEstado(int idAcceso, bool activo)
        {
            try
            {
                RunUpdate(
                    "UPDATE portal_clientes_acceso SET activo = @activo WHERE id_acceso = @id",
                    new Dictionary<string, object> { { "activo", activo }, { "id", idAcceso } });

                BitacoraPorAcceso(idAcceso,
                    activo ? "reactivado_administrador" : "suspendido_administrador",
                    $"por {User.Identity?.Name}");

                return Json(new
                {
                    success = true,
                    message = activo ? "Acceso reactivado." : "Acceso suspendido."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Apoyo
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Manda el enlace para definir contraseña. Devuelve el mensaje que verá el
        /// administrador: si el correo falla, el acceso YA quedó creado y sólo falta
        /// reenviar, así que conviene decirlo en vez de dar el alta por fallida.
        /// </summary>
        [NonAction]
        private async Task<string> EnviarEnlace(int clienteId, string correo, string token,
            string nombreCliente, string claveCliente)
        {
            string enlace = Url.Action("Activar", "PortalClientes",
                new { t = token, c = clienteId }, Request.Scheme);

            try
            {
                await _correo.EnviarCorreoNotificacionAsync(correo,
                    "Acceso al portal de clientes",
                    $@"<p>Hola {nombreCliente},</p>
                       <p>Se habilitó tu acceso al portal de clientes con la clave
                          <strong>{claveCliente}</strong>. Define tu contraseña con el siguiente enlace:</p>
                       <p><a href=""{enlace}"" style=""background:#1976d2;color:#fff;padding:10px 18px;
                          border-radius:6px;text-decoration:none;display:inline-block;"">
                          Definir mi contraseña</a></p>
                       <p style=""color:#666;font-size:13px;"">El enlace vence en 2 horas y sólo puede usarse una vez.</p>");

                return $"Acceso creado. Se envió el enlace a {correo}.";
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, correo,
                    $"Acceso creado pero falló el envío del enlace: {ex.Message}",
                    User.Identity?.Name, nivel: "ERROR");

                return $"El acceso quedó creado, pero no se pudo enviar el correo a {correo}. " +
                       "Usa «Reenviar enlace» cuando se restablezca el servicio.";
            }
        }

        [NonAction]
        private void Bitacora(int? clienteId, string correo, string evento, string detalle)
        {
            try
            {
                RunUpdate(@"
                    INSERT INTO portal_clientes_bitacora (cliente_id, correo, evento, detalle, ip)
                    VALUES (@cliente, @correo, @evento, @detalle, @ip)",
                    new Dictionary<string, object>
                    {
                        { "cliente", (object)clienteId ?? DBNull.Value },
                        { "correo",  (object)correo ?? DBNull.Value },
                        { "evento",  evento },
                        { "detalle", (object)detalle ?? DBNull.Value },
                        { "ip",      HttpContext.Connection.RemoteIpAddress?.ToString() ?? "" }
                    });
            }
            catch (Exception ex)
            {
                // La bitácora no debe tumbar una operación válida.
                LogErrorHelper.RegistrarLog(LogTag, correo ?? "",
                    $"No se pudo escribir la bitácora ({evento}): {ex.Message}", nivel: "WARN");
            }
        }

        [NonAction]
        private void BitacoraPorAcceso(int idAcceso, string evento, string detalle)
        {
            var a = RunQuery(
                "SELECT cliente_id, correo FROM portal_clientes_acceso WHERE id_acceso = @id",
                new Dictionary<string, object> { { "id", idAcceso } }).FirstOrDefault();

            if (a == null) return;

            Bitacora(Convert.ToInt32(a["cliente_id"]), a["correo"]?.ToString(), evento, detalle);
        }
    }
}
