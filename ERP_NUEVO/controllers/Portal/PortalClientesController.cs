using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Security.Cryptography;

namespace BOS_ERP.Controllers.Portal
{
    /// <summary>
    /// Portal de clientes: consulta de sus propias facturas.
    ///
    /// Es un módulo para usuarios EXTERNOS, y de ahí sus tres reglas de fondo:
    ///
    ///  1. Hereda de Controller, no de Utilities. Utilities expone decenas de métodos
    ///     públicos que MVC publicaría como endpoints de este controlador anónimo.
    ///     Las consultas se hacen aquí con Npgsql directo.
    ///
    ///  2. Lleva [AllowAnonymous] para que el SessionManagementFilter global —que
    ///     exige Session["UsuarioId"]— no lo rebote al login del ERP. La autorización
    ///     real la hace [AutorizarCliente] con el esquema de cookie del portal.
    ///
    ///  3. El cliente NUNCA viaja en la petición. Se toma del claim de la sesión, así
    ///     que cambiar un parámetro no permite ver documentos ajenos.
    /// </summary>
    [AllowAnonymous]
    [Route("PortalClientes")]
    public class PortalClientesController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly CorreoHelper _correo;

        // Naturalezas que representan un comprobante emitido al cliente. Es la misma
        // lista de la consulta interna de facturas; si allá se agrega un tipo, aquí
        // también debe agregarse o el cliente dejará de ver esas facturas.
        private const string NatsDeFactura = "'VSFAC','VIFAC','VNFAC','VINFAC','FAR','RICD','FACLIB'";

        // Freno a la fuerza bruta: tras este número de fallos la cuenta descansa.
        private const int MaxIntentosFallidos = 5;
        private static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(15);
        
        private readonly BOS_ERP.Services.SocioTiburonService _socioTiburon;

        public PortalClientesController(
            IConfiguration config, IWebHostEnvironment env, CorreoHelper correo,
            BOS_ERP.Services.SocioTiburonService socioTiburon)
        {
            _config = config;
            _env = env;
            _correo = correo;
            _socioTiburon = socioTiburon;
        }

        private NpgsqlConnection Abrir()
        {
            var conn = new NpgsqlConnection(_config.GetConnectionString("ERP_SRS"));
            conn.Open();
            return conn;
        }

        private string IpCliente() =>
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        // ════════════════════════════════════════════════════════════════
        // Acceso
        // ════════════════════════════════════════════════════════════════

        [HttpGet("")]
        [HttpGet("Acceso")]
        public IActionResult Acceso()
        {
            // Si ya hay sesión, no tiene caso volver a pedir credenciales.
            if (PortalClientesAuth.ClienteId(
                    HttpContext.AuthenticateAsync(PortalClientesAuth.Scheme)
                               .GetAwaiter().GetResult().Principal) > 0)
                return RedirectToAction(nameof(Facturas));

            return View("~/Views/Portal/Acceso.cshtml");
        }

        [HttpPost("Entrar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Entrar(string claveCliente, string correo, string password, int empresaId = 0)
        {
            claveCliente = (claveCliente ?? "").Trim();
            correo = (correo ?? "").Trim().ToLowerInvariant();

            // Mensaje único para todos los fallos de credenciales: distinguir "no
            // existe" de "contraseña incorrecta" permitiría enumerar clientes.
            const string errorGenerico = "Clave de cliente, correo o contraseña incorrectos.";

            if (string.IsNullOrWhiteSpace(claveCliente) ||
                string.IsNullOrWhiteSpace(correo) ||
                string.IsNullOrWhiteSpace(password))
                return Json(new { success = false, message = "Captura los tres datos para entrar." });

            using var conn = Abrir();

            var candidatos = LeerAccesos(conn, claveCliente, correo, empresaId);

            if (candidatos.Count == 0)
            {
                RegistrarBitacora(conn, null, correo, "login_fallido", $"clave={claveCliente}");
                return Json(new { success = false, message = errorGenerico });
            }

            // Cuando la misma clave y correo tienen cuenta en varias empresas, se desempata
            // con la contraseña en vez de pedir la empresa por adelantado: así el 99% de los
            // clientes —que sólo tienen una— no cargan con un campo extra.
            //
            // El orden importa: primero se verifica la contraseña y sólo DESPUÉS se ofrece
            // el selector. Preguntarlo antes revelaría a cualquiera que esa combinación de
            // clave y correo existe, y en qué empresas.
            AccesoPortal acceso;

            if (candidatos.Count == 1)
            {
                acceso = candidatos[0];
            }
            else
            {
                var coinciden = candidatos.Where(c => c.Activo && PasswordCoincide(c, password)).ToList();

                if (coinciden.Count == 0)
                {
                    // Se penaliza a todas las cuentas de la combinación: si no, probar
                    // contraseñas contra una cuenta multiempresa saldría gratis.
                    foreach (var c in candidatos) RegistrarFalloLogin(conn, c, correo);
                    return Json(new { success = false, message = errorGenerico });
                }

                if (coinciden.Count > 1)
                {
                    return Json(new
                    {
                        success = false,
                        requiereEmpresa = true,
                        message = "Tu correo tiene acceso en más de una empresa. Selecciona con cuál quieres entrar.",
                        empresas = coinciden.Select(c => new { id = c.EmpresaId, nombre = c.EmpresaNombre }).ToList()
                    });
                }

                acceso = coinciden[0];
            }

            if (acceso.BloqueadoHasta.HasValue && acceso.BloqueadoHasta.Value > DateTime.Now)
            {
                int restan = (int)Math.Ceiling((acceso.BloqueadoHasta.Value - DateTime.Now).TotalMinutes);
                return Json(new
                {
                    success = false,
                    message = $"La cuenta está bloqueada por intentos fallidos. Intenta de nuevo en {restan} minuto(s)."
                });
            }

            if (!acceso.Activo || string.IsNullOrWhiteSpace(acceso.PasswordHash))
                return Json(new
                {
                    success = false,
                    message = "Esta cuenta aún no está activada. Solicita el enlace de activación."
                });

            if (!PasswordCoincide(acceso, password))
            {
                RegistrarFalloLogin(conn, acceso, correo);
                return Json(new { success = false, message = errorGenerico });
            }

            Ejecutar(conn, @"
                UPDATE portal_clientes_acceso
                SET intentos_fallidos = 0, bloqueado_hasta = NULL, ultimo_acceso = NOW()
                WHERE id_acceso = @id",
                ("id", acceso.IdAcceso));

            RegistrarBitacora(conn, acceso.ClienteId, correo, "login_ok", null);

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, acceso.NombreCliente ?? claveCliente),
                new(PortalClientesAuth.ClaimClienteId, acceso.ClienteId.ToString()),
                new(PortalClientesAuth.ClaimClaveCliente, acceso.ClaveCliente ?? ""),
                new(PortalClientesAuth.ClaimCorreo, correo),
                new(PortalClientesAuth.ClaimEmpresaId, acceso.EmpresaId.ToString())
            };

            var identidad = new ClaimsIdentity(claims, PortalClientesAuth.Scheme);

            await HttpContext.SignInAsync(
                PortalClientesAuth.Scheme,
                new ClaimsPrincipal(identidad),
                new AuthenticationProperties { IsPersistent = false });

            return Json(new { success = true, redirectUrl = Url.Action(nameof(Facturas)) });
        }

        [HttpGet("Salir")]
        public async Task<IActionResult> Salir()
        {
            await HttpContext.SignOutAsync(PortalClientesAuth.Scheme);
            return RedirectToAction(nameof(Acceso));
        }

        // ════════════════════════════════════════════════════════════════
        // Activación y restablecimiento
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Envía el enlace para definir contraseña, pero SÓLO si ese correo ya está
        /// registrado en correos_cliente para esa clave de cliente. Es lo que impide
        /// que conocer una clave alcance para ver facturas ajenas.
        /// </summary>
        /// <summary>
        /// Empresas disponibles para el selector del alta. Es un catálogo corto y sin
        /// datos sensibles —los mismos nombres que aparecen en cualquier factura—, así
        /// que exponerlo no revela nada de la cartera de clientes.
        /// </summary>
        [HttpGet("Empresas")]
        public IActionResult Empresas()
        {
            try
            {
                using var conn = Abrir();

                var empresas = Consultar(conn, @"
                    SELECT e.empresaid, e.nombre
                    FROM empresas e
                    WHERE EXISTS (SELECT 1 FROM catclientes c WHERE c.empresa_id = e.empresaid)
                    ORDER BY e.nombre")
                    .Select(e => new
                    {
                        id = Convert.ToInt32(e["empresaid"]),
                        nombre = e["nombre"]?.ToString()
                    })
                    .ToList();

                return Json(new { success = true, empresas });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog("PortalClientes", "",
                    $"No se pudo leer el catálogo de empresas: {ex.Message}", nivel: "ERROR");

                return Json(new { success = false, message = "No se pudo cargar el catálogo de empresas." });
            }
        }

        [HttpPost("SolicitarAcceso")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarAcceso(string claveCliente, string correo, int empresaId)
        {
            claveCliente = (claveCliente ?? "").Trim();
            correo = (correo ?? "").Trim().ToLowerInvariant();

            // Respuesta idéntica exista o no la combinación: si dijéramos "ese correo
            // no está registrado", el formulario serviría para averiguar qué correos
            // tiene dado de alta un cliente.
            const string respuestaNeutra =
                "Si la clave y el correo corresponden a un cliente registrado, te enviamos un enlace para definir tu contraseña. " +
                "Revisa tu bandeja; el enlace vence en 2 horas.";

            if (string.IsNullOrWhiteSpace(claveCliente) || string.IsNullOrWhiteSpace(correo))
                return Json(new { success = false, message = "Captura tu clave de cliente y tu correo." });

            if (empresaId <= 0)
                return Json(new { success = false, message = "Selecciona la empresa con la que facturas." });

            using var conn = Abrir();

            // La empresa entra en la búsqueda desde el inicio. cve_cli se repite entre
            // empresas —1,559 casos— y es (cve_cli, empresa_id) lo único que identifica
            // sin ambigüedad. Pedirla aquí evita el caso irresoluble de dos clientes con
            // la misma clave y el mismo correo, en vez de tener que rechazarlo después.
            //
            // Y como es un campo más del formulario, la respuesta sigue siendo neutra: si
            // la empresa no corresponde, se contesta igual que si no existiera el correo,
            // sin pistas sobre en qué empresa sí está registrado el cliente.
            var candidatos = Consultar(conn, @"
                SELECT c.id_cliente, c.cve_cli, c.n_cli, c.empresa_id
                FROM catclientes c
                WHERE c.cve_cli = @clave
                  AND c.empresa_id = @empresa
                  AND EXISTS (
                      SELECT 1 FROM correos_cliente cc
                      WHERE cc.cliente_id = c.id_cliente
                        AND LOWER(cc.correo) = @correo
                  )",
                ("clave", claveCliente), ("correo", correo), ("empresa", empresaId));

            if (candidatos.Count == 0)
            {
                RegistrarBitacora(conn, null, correo, "activacion_sin_coincidencia",
                    $"clave={claveCliente} empresa={empresaId}");
                return Json(new { success = true, message = respuestaNeutra });
            }

            // (cve_cli, empresa_id) es único en catclientes: lo verifiqué y no hay
            // duplicados. Si algún día los hubiera, se rechaza en vez de elegir, porque
            // adivinar mostraría las facturas del cliente equivocado.
            if (candidatos.Count > 1)
            {
                RegistrarBitacora(conn, null, correo, "activacion_ambigua",
                    $"clave={claveCliente} empresa={empresaId} coincide con {candidatos.Count} clientes");

                return Json(new
                {
                    success = false,
                    message = "Tu clave de cliente está asociada a más de una cuenta en esa empresa. " +
                              "Comunícate con nosotros para darte acceso."
                });
            }

            var cliente = candidatos[0];
            int clienteId = Convert.ToInt32(cliente["id_cliente"]);

            // Token de un solo uso. Se guarda el hash: si alguien leyera la tabla no
            // podría activar cuentas con lo que encuentre ahí.
            string token = PortalClientesAuth.GenerarToken();
            string tokenHash = PortalClientesAuth.HashearToken(token);

            Ejecutar(conn, @"
                INSERT INTO portal_clientes_acceso
                    (cliente_id, correo, token_hash, token_expira, token_proposito, activo)
                VALUES (@cliente, @correo, @hash, @expira, 'activacion', false)
                ON CONFLICT (cliente_id, correo) DO UPDATE
                SET token_hash      = EXCLUDED.token_hash,
                    token_expira    = EXCLUDED.token_expira,
                    token_proposito = EXCLUDED.token_proposito",
                ("cliente", clienteId),
                ("correo", correo),
                ("hash", tokenHash),
                ("expira", DateTime.Now.Add(PortalClientesAuth.VigenciaToken)));

            RegistrarBitacora(conn, clienteId, correo, "activacion_solicitada", null);

            string enlace = Url.Action(nameof(Activar), "PortalClientes",
                new { t = token, c = clienteId }, Request.Scheme);

            try
            {
                await _correo.EnviarCorreoNotificacionAsync(correo,
                    "Acceso al portal de clientes",
                    $@"<p>Hola {cliente["n_cli"]},</p>
                       <p>Recibimos una solicitud para acceder al portal de clientes con la clave
                          <strong>{claveCliente}</strong>.</p>
                       <p><a href=""{enlace}"" style=""background:#1976d2;color:#fff;padding:10px 18px;
                          border-radius:6px;text-decoration:none;display:inline-block;"">
                          Definir mi contraseña</a></p>
                       <p style=""color:#666;font-size:13px;"">El enlace vence en 2 horas y sólo puede usarse una vez.
                          Si no fuiste tú, puedes ignorar este mensaje: sin él nadie puede entrar.</p>");
            }
            catch (Exception ex)
            {
                // El token ya quedó guardado; el problema es el envío. Se avisa sin
                // revelar si la cuenta existe.
                LogErrorHelper.RegistrarLog("PortalClientes", correo,
                    $"No se pudo enviar el correo de activación: {ex.Message}", nivel: "ERROR");
            }

            return Json(new { success = true, message = respuestaNeutra });
        }

        [HttpGet("Activar")]
        public IActionResult Activar(string t, int c)
        {
            ViewBag.Token = t;
            ViewBag.ClienteId = c;
            return View("~/Views/Portal/Activar.cshtml");
        }

        [HttpPost("EstablecerPassword")]
        [ValidateAntiForgeryToken]
        public IActionResult EstablecerPassword(string token, int clienteId, string password, string confirmacion)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return Json(new { success = false, message = "La contraseña debe tener al menos 8 caracteres." });

            if (password != confirmacion)
                return Json(new { success = false, message = "Las contraseñas no coinciden." });

            using var conn = Abrir();

            var filas = Consultar(conn, @"
                SELECT id_acceso, correo, token_hash, token_expira
                FROM portal_clientes_acceso
                WHERE cliente_id = @cliente AND token_hash IS NOT NULL",
                ("cliente", clienteId));

            // Se compara contra cada acceso pendiente del cliente porque el token no
            // dice a qué correo pertenece: el hash es lo único que los liga.
            var coincidencia = filas.FirstOrDefault(f =>
            {
                return PortalClientesAuth.TokenCoincide(token, f["token_hash"]?.ToString());
            });

            if (coincidencia == null)
                return Json(new { success = false, message = "El enlace no es válido. Solicita uno nuevo." });

            var expira = coincidencia["token_expira"] as DateTime?;
            if (!expira.HasValue || expira.Value < DateTime.Now)
                return Json(new { success = false, message = "El enlace venció. Solicita uno nuevo." });

            Ejecutar(conn, @"
                UPDATE portal_clientes_acceso
                SET password_hash     = @hash,
                    activo            = true,
                    token_hash        = NULL,
                    token_expira      = NULL,
                    token_proposito   = NULL,
                    intentos_fallidos = 0,
                    bloqueado_hasta   = NULL
                WHERE id_acceso = @id",
                ("hash", BCrypt.Net.BCrypt.HashPassword(password)),
                ("id", Convert.ToInt32(coincidencia["id_acceso"])));

            RegistrarBitacora(conn, clienteId, coincidencia["correo"]?.ToString(), "activacion_completada", null);

            return Json(new { success = true, message = "Tu contraseña quedó definida. Ya puedes entrar.", redirectUrl = Url.Action(nameof(Acceso)) });
        }

        // ════════════════════════════════════════════════════════════════
        // Consulta de facturas
        // ════════════════════════════════════════════════════════════════

        [HttpGet("Facturas")]
        [AutorizarCliente]
        public IActionResult Facturas()
        {
            ViewBag.Cliente = User.Identity?.Name;
            ViewBag.ClaveCliente = PortalClientesAuth.ClaveCliente(User);
            ViewBag.Correo = PortalClientesAuth.Correo(User);
            return View("~/Views/Portal/Facturas.cshtml");
        }

        /// <summary>
        /// Método de pago del CFDI expresado como "¿es a crédito?". PPD (pago en
        /// parcialidades o diferido) es lo que en la calle se llama factura a crédito;
        /// PUE es contado. Se compara también contra la descripción larga porque las
        /// facturas migradas de Kepler guardan el texto del catálogo del SAT en lugar de
        /// la clave, y comparando sólo contra 'PPD' quedarían marcadas como contado.
        ///
        /// La misma expresión se usa en el SELECT y en el WHERE para que la etiqueta que
        /// ve el cliente y el filtro que aplica nunca puedan discrepar.
        /// </summary>
        private const string EsCreditoSql = @"
            (UPPER(COALESCE(f.mdpfactura::text, '')) = 'PPD'
             OR UPPER(COALESCE(f.mdpfactura::text, '')) LIKE '%PARCIALIDADES%'
             OR UPPER(COALESCE(f.mdpfactura::text, '')) LIKE '%DIFERIDO%')";

        /// <summary>
        /// Saldo de la factura tomado de cartera. Va como LATERAL y no como JOIN normal
        /// porque un encabezado puede tener más de un renglón de cartera y un JOIN
        /// duplicaría la factura en el listado. Las filas canceladas quedan fuera: ya no
        /// son deuda.
        /// </summary>
        private const string CarteraSql = @"
            LEFT JOIN LATERAL (
                SELECT COUNT(*)                    AS renglones,
                       SUM(cc.monto_total)         AS monto_total,
                       SUM(cc.saldo_pendiente)     AS saldo_pendiente,
                       MIN(cc.fecha_vencimiento)   AS fecha_vencimiento
                FROM cartera_clientes cc
                WHERE cc.encabezado_id = f.encabezado_id
                  AND cc.cancelada = false
            ) car ON TRUE";

        [HttpGet("ObtenerFacturas")]
        [AutorizarCliente]
        public IActionResult ObtenerFacturas(string desde = null, string hasta = null,
            string estatus = null, string tipo = null, string pago = null)
        {
            int clienteId = PortalClientesAuth.ClienteId(User);

            try
            {
                using var conn = Abrir();

                // El filtro por cliente sale del claim, nunca de la petición.
                string filtroEstatus = (estatus ?? "").ToLowerInvariant() switch
                {
                    "canceladas" => " AND LOWER(f.statusfactura) LIKE '%cancel%' ",
                    "vigentes" => " AND LOWER(f.statusfactura) NOT LIKE '%cancel%' ",
                    _ => ""
                };

                // Contado y crédito son complementarios a propósito: todo lo que no es PPD
                // se trata como contado. Si se dejara un tercer grupo "sin clasificar", una
                // factura con el método de pago vacío no saldría en ninguno de los dos
                // filtros y el cliente creería que se le perdió un comprobante.
                string filtroTipo = (tipo ?? "").ToLowerInvariant() switch
                {
                    "credito" => $" AND {EsCreditoSql} ",
                    "contado" => $" AND NOT {EsCreditoSql} ",
                    _ => ""
                };

                // El estado de pago sale de cartera y sólo se aplica sobre facturas a
                // crédito. Una de contado con saldo abierto casi siempre es un cobro que
                // todavía no capturamos de nuestro lado —de ahí vive el módulo interno de
                // cobranza de contado—, y presentárselo al cliente como adeudo sería
                // cobrarle otra vez lo que ya pagó en mostrador.
                //
                // Una factura cancelada no tiene cartera viva, así que también se excluye
                // de "pagadas": no está pagada, está cancelada.
                string filtroPago = (pago ?? "").ToLowerInvariant() switch
                {
                    "pendientes" => $" AND {EsCreditoSql} AND COALESCE(car.saldo_pendiente, 0) > 0 ",
                    "vencidas" => $" AND {EsCreditoSql} AND COALESCE(car.saldo_pendiente, 0) > 0 " +
                                  " AND car.fecha_vencimiento::date < CURRENT_DATE ",
                    "pagadas" => $" AND {EsCreditoSql} AND COALESCE(car.renglones, 0) > 0 " +
                                 " AND COALESCE(car.saldo_pendiente, 0) <= 0 " +
                                 " AND LOWER(f.statusfactura) NOT LIKE '%cancel%' ",
                    _ => ""
                };

                string filtroFechas = "";
                if (DateTime.TryParse(desde, out DateTime dDesde)) filtroFechas += " AND f.fecha >= @desde ";
                if (DateTime.TryParse(hasta, out DateTime dHasta)) filtroFechas += " AND f.fecha < @hasta ";

                var parametros = new List<(string, object)> { ("cliente", clienteId) };
                if (DateTime.TryParse(desde, out DateTime d1)) parametros.Add(("desde", d1.Date));
                if (DateTime.TryParse(hasta, out DateTime d2)) parametros.Add(("hasta", d2.Date.AddDays(1)));

                var filas = Consultar(conn, $@"
                    SELECT f.uuid, f.serie, f.folio, f.fecha, f.fechatimbrado,
                           f.subtotal, f.descuento, f.iva, f.total, f.moneda,
                           f.statusfactura, f.idusocfdi, f.mdpfactura,
                           em.nat,
                           {EsCreditoSql}                   AS es_credito,
                           COALESCE(car.renglones, 0)       AS renglones_cartera,
                           COALESCE(car.monto_total, 0)     AS cartera_total,
                           COALESCE(car.saldo_pendiente, 0) AS saldo_pendiente,
                           car.fecha_vencimiento,
                           -- El atraso lo calcula el motor de base de datos y no C#: el
                           -- filtro de vencidas usa CURRENT_DATE, y compararlo en el
                           -- servidor web contra su fecha local haría que una factura
                           -- saliera en el filtro pero sin la etiqueta de vencida.
                           (car.fecha_vencimiento::date < CURRENT_DATE)          AS vencida,
                           (CURRENT_DATE - car.fecha_vencimiento::date)          AS dias_vencido
                    FROM factura f
                    INNER JOIN encabezadomov em ON em.id_encabezado = f.encabezado_id
                    {CarteraSql}
                    WHERE em.refe = @cliente
                      AND em.nat IN ({NatsDeFactura})
                      AND f.uuid IS NOT NULL
                      {filtroEstatus}
                      {filtroTipo}
                      {filtroPago}
                      {filtroFechas}
                    ORDER BY f.fecha DESC
                    LIMIT 500",
                    parametros.ToArray());

                var facturas = filas.Select(f =>
                {
                    string status = f["statusfactura"]?.ToString() ?? "";
                    bool cancelada = status.ToLowerInvariant().Contains("cancel");
                    bool esCredito = f["es_credito"] is bool cr && cr;

                    bool tieneCartera = Convert.ToInt32(f["renglones_cartera"] ?? 0) > 0;
                    decimal saldo = Convert.ToDecimal(f["saldo_pendiente"] ?? 0m);
                    decimal carteraTotal = Convert.ToDecimal(f["cartera_total"] ?? 0m);
                    DateTime? vence = f["fecha_vencimiento"] as DateTime?;
                    bool vencida = f["vencida"] is bool vc && vc;
                    int diasAtraso = f["dias_vencido"] is null ? 0 : Convert.ToInt32(f["dias_vencido"]);

                    // Sólo las de crédito llevan estado de pago, por lo mismo que el filtro:
                    // el saldo abierto de una de contado es rezago de captura nuestro, no
                    // deuda del cliente. Y "sin dato" no es lo mismo que "pagada": las
                    // facturas viejas que nunca generaron cartera no tienen saldo que
                    // mostrar, y darlas por pagadas sería afirmar algo que no nos consta.
                    string estadoPago =
                        cancelada || !esCredito ? ""
                        : !tieneCartera ? ""
                        : saldo <= 0 ? "Pagada"
                        : vencida ? "Vencida"
                        : "Pendiente";

                    bool muestraSaldo = estadoPago != "";

                    return new
                    {
                        uuid = f["uuid"]?.ToString(),
                        serie = f["serie"]?.ToString(),
                        folio = f["folio"]?.ToString(),
                        fecha = f["fecha"] is DateTime fe ? fe.ToString("dd/MM/yyyy") : "",
                        subtotal = f["subtotal"],
                        descuento = f["descuento"],
                        iva = f["iva"],
                        total = f["total"],
                        moneda = f["moneda"]?.ToString(),
                        // Los estados internos ("Pendiente Cancelación", "Error al
                        // Cancelar") no se muestran: para el cliente el comprobante
                        // sigue siendo válido ante el SAT y el detalle es ruido nuestro.
                        estatus = cancelada ? "Cancelada" : "Vigente",
                        usoCfdi = f["idusocfdi"]?.ToString(),
                        metodoPago = f["mdpfactura"]?.ToString(),

                        // ── Contado / crédito ────────────────────────────────
                        esCredito,
                        tipo = esCredito ? "Crédito" : "Contado",
                        // Una factura cancelada dejó de ser deuda: su cartera ya no vive y
                        // mostrarle un saldo al cliente sería cobrarle algo inexistente.
                        saldo = muestraSaldo ? saldo : 0m,
                        pagado = muestraSaldo ? Math.Max(carteraTotal - saldo, 0m) : 0m,
                        estadoPago,
                        vencimiento = muestraSaldo ? vence?.ToString("dd/MM/yyyy") : null,
                        diasVencido = muestraSaldo && saldo > 0 && vencida ? diasAtraso : 0
                    };
                }).ToList();

                return Json(new
                {
                    success = true,
                    facturas,
                    resumen = new
                    {
                        total = facturas.Count,
                        vigentes = facturas.Count(x => x.estatus == "Vigente"),
                        canceladas = facturas.Count(x => x.estatus == "Cancelada"),
                        credito = facturas.Count(x => x.esCredito),
                        contado = facturas.Count(x => !x.esCredito),
                        // Cuenta, no importe: el resumen se arma con lo que quedó a la
                        // vista y puede mezclar monedas. Los pesos y los dólares se suman
                        // por separado en el adeudo, que además ignora los filtros.
                        porPagar = facturas.Count(x => x.saldo > 0),
                        vencidas = facturas.Count(x => x.diasVencido > 0)
                    },
                    adeudo = AdeudoDelCliente(conn, clienteId)
                });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog("PortalClientes", clienteId.ToString(),
                    $"Error al listar facturas: {ex.Message}", nivel: "ERROR");

                return Json(new { success = false, message = "No se pudieron cargar tus facturas." });
            }
        }

        /// <summary>
        /// Lo que el cliente debe hoy, sin filtros de por medio: si filtrara por "este mes"
        /// y el adeudo se recalculara con ese filtro, vería una deuda menor que la real —
        /// justo el error que un estado de cuenta no puede cometer.
        ///
        /// Se limita a facturas a crédito porque es la deuda que el cliente reconoce como
        /// tal: una de contado con saldo abierto casi siempre es un cobro que todavía no
        /// capturamos de nuestro lado, no dinero que él deba.
        ///
        /// Se agrupa por moneda en vez de sumar todo junto: sumar pesos con dólares daría
        /// una cifra que no significa nada.
        /// </summary>
        private List<object> AdeudoDelCliente(NpgsqlConnection conn, int clienteId)
        {
            // La factura entra por LATERAL con LIMIT 1 y no con un JOIN normal: si un
            // encabezado llegara a tener dos comprobantes, el JOIN repetiría el renglón de
            // cartera y el cliente vería su deuda multiplicada.
            var filas = Consultar(conn, $@"
                SELECT COALESCE(NULLIF(TRIM(f.moneda::text), ''), 'MXN')    AS moneda,
                       COUNT(*)                                             AS facturas,
                       SUM(cc.saldo_pendiente)                              AS saldo,
                       SUM(CASE WHEN cc.fecha_vencimiento::date < CURRENT_DATE
                                THEN cc.saldo_pendiente ELSE 0 END)         AS vencido,
                       COUNT(*) FILTER (
                           WHERE cc.fecha_vencimiento::date < CURRENT_DATE)  AS facturas_vencidas,
                       MIN(cc.fecha_vencimiento) FILTER (
                           WHERE cc.fecha_vencimiento::date >= CURRENT_DATE) AS proximo_vencimiento
                FROM cartera_clientes cc
                INNER JOIN encabezadomov em ON em.id_encabezado = cc.encabezado_id
                INNER JOIN LATERAL (
                    SELECT fa.moneda, fa.statusfactura, fa.mdpfactura
                    FROM factura fa
                    WHERE fa.encabezado_id = cc.encabezado_id
                      AND fa.uuid IS NOT NULL
                    ORDER BY fa.id DESC
                    LIMIT 1
                ) f ON TRUE
                WHERE cc.cliente_id = @cliente
                  AND cc.cancelada = false
                  AND cc.saldo_pendiente > 0
                  AND em.nat IN ({NatsDeFactura})
                  AND LOWER(COALESCE(f.statusfactura::text, '')) NOT LIKE '%cancel%'
                  AND {EsCreditoSql}
                GROUP BY 1
                ORDER BY 3 DESC",
                ("cliente", clienteId));

            return filas.Select(f => (object)new
            {
                moneda = f["moneda"]?.ToString(),
                facturas = Convert.ToInt32(f["facturas"] ?? 0),
                facturasVencidas = Convert.ToInt32(f["facturas_vencidas"] ?? 0),
                saldo = Convert.ToDecimal(f["saldo"] ?? 0m),
                vencido = Convert.ToDecimal(f["vencido"] ?? 0m),
                proximoVencimiento = f["proximo_vencimiento"] is DateTime pv
                    ? pv.ToString("dd/MM/yyyy")
                    : null
            }).ToList();
        }

        /// <summary>
        /// Puntos del cliente en Socio Tiburón. Va en su propia llamada, después de que la
        /// página ya cargó: es un servicio externo y no debe retrasar —ni tumbar— la
        /// consulta de facturas, que es a lo que el cliente entró.
        /// </summary>
        [HttpGet("SocioTiburon")]
        [AutorizarCliente]
        public async Task<IActionResult> SocioTiburon()
        {
            var puntos = await _socioTiburon.ObtenerPuntosAsync(
                PortalClientesAuth.ClaveCliente(User),
                PortalClientesAuth.EmpresaId(User));

            return Json(new
            {
                success = true,
                // Cuando no se encuentra, la pantalla oculta la tarjeta. Da igual si fue
                // porque el cliente no está inscrito, porque su empresa no participa o
                // porque el servicio no respondió: en los tres casos no hay nada que
                // mostrarle, y detallarlo sólo lo confundiría.
                inscrito = puntos.Encontrado,
                puntos = puntos.Puntos,
                membresia = puntos.Membresia,
                canjes = puntos.Canjes,
                url = puntos.Url
            });
        }

        // ════════════════════════════════════════════════════════════════
        // Descargas y envío
        // ════════════════════════════════════════════════════════════════

        [HttpGet("DescargarPdf")]
        [AutorizarCliente]
        public IActionResult DescargarPdf(string uuid) => Descargar(uuid, "pdf");

        [HttpGet("DescargarXml")]
        [AutorizarCliente]
        public IActionResult DescargarXml(string uuid) => Descargar(uuid, "xml");

        private IActionResult Descargar(string uuid, string tipo)
        {
            int clienteId = PortalClientesAuth.ClienteId(User);

            // Sin esta comprobación, cambiar el uuid en la URL bastaría para bajar la
            // factura de cualquier otro cliente.
            if (!FacturaEsDelCliente(uuid, clienteId, out string error))
                return NotFound(error);

            string ruta = tipo == "pdf"
                ? Path.Combine(_env.WebRootPath, "Facturacion", "facturas", $"{uuid}.pdf")
                : Path.Combine(_env.WebRootPath, "Facturacion", "xml_timbrados", $"{uuid}.xml");

            if (!System.IO.File.Exists(ruta))
                return NotFound($"El archivo {tipo.ToUpperInvariant()} de esta factura no está disponible. Solicítalo por correo.");

            using var conn = Abrir();
            RegistrarBitacora(conn, clienteId, PortalClientesAuth.Correo(User), "descarga", $"{tipo} {uuid}");

            return PhysicalFile(ruta,
                tipo == "pdf" ? "application/pdf" : "application/xml",
                $"{uuid}.{tipo}");
        }

        [HttpPost("EnviarPorCorreo")]
        [AutorizarCliente]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarPorCorreo(string uuid)
        {
            int clienteId = PortalClientesAuth.ClienteId(User);
            // Se envía al correo con el que inició sesión, no a uno capturado: si no,
            // el portal serviría para reenviar comprobantes ajenos a cualquier buzón.
            string destino = PortalClientesAuth.Correo(User);

            if (!FacturaEsDelCliente(uuid, clienteId, out string error))
                return Json(new { success = false, message = error });

            var adjuntos = new List<(string, byte[], string)>();

            string pdf = Path.Combine(_env.WebRootPath, "Facturacion", "facturas", $"{uuid}.pdf");
            string xml = Path.Combine(_env.WebRootPath, "Facturacion", "xml_timbrados", $"{uuid}.xml");

            if (System.IO.File.Exists(pdf))
                adjuntos.Add(($"{uuid}.pdf", await System.IO.File.ReadAllBytesAsync(pdf), "application/pdf"));
            if (System.IO.File.Exists(xml))
                adjuntos.Add(($"{uuid}.xml", await System.IO.File.ReadAllBytesAsync(xml), "application/xml"));

            if (adjuntos.Count == 0)
                return Json(new { success = false, message = "Los archivos de esta factura no están disponibles." });

            try
            {
                await _correo.EnviarCorreoConAdjuntosAsync(destino,
                    $"Tu factura {uuid}",
                    $@"<p>Hola,</p><p>Adjuntamos los archivos de tu factura con folio fiscal
                       <strong>{uuid}</strong>.</p>
                       <p style=""color:#666;font-size:13px;"">Enviado desde el portal de clientes.</p>",
                    adjuntos);

                using var conn = Abrir();
                RegistrarBitacora(conn, clienteId, destino, "envio_correo", uuid);

                return Json(new { success = true, message = $"Enviamos la factura a {destino}." });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog("PortalClientes", uuid,
                    $"No se pudo enviar la factura por correo: {ex.Message}", nivel: "ERROR");

                return Json(new { success = false, message = "No se pudo enviar el correo. Intenta más tarde." });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Apoyo
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Comprueba que la factura pertenezca al cliente de la sesión. Es la barrera
        /// que convierte el uuid en un dato inofensivo dentro de la URL.
        /// </summary>
        private bool FacturaEsDelCliente(string uuid, int clienteId, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(uuid) || !Guid.TryParse(uuid, out _))
            {
                error = "Folio fiscal inválido.";
                return false;
            }

            using var conn = Abrir();

            var filas = Consultar(conn, $@"
                SELECT 1
                FROM factura f
                INNER JOIN encabezadomov em ON em.id_encabezado = f.encabezado_id
                WHERE f.uuid = @uuid
                  AND em.refe = @cliente
                  AND em.nat IN ({NatsDeFactura})
                LIMIT 1",
                ("uuid", Guid.Parse(uuid)), ("cliente", clienteId));

            if (filas.Count == 0)
            {
                // Mismo mensaje exista o no la factura: no confirmar la existencia de
                // comprobantes ajenos.
                error = "No encontramos esa factura en tu cuenta.";

                LogErrorHelper.RegistrarLog("PortalClientes", uuid,
                    $"Cliente {clienteId} intentó acceder a una factura que no le pertenece.",
                    nivel: "WARN");

                return false;
            }

            return true;
        }

        private sealed class AccesoPortal
        {
            public int IdAcceso { get; init; }
            public int ClienteId { get; init; }
            public string ClaveCliente { get; init; }
            public string NombreCliente { get; init; }
            public int EmpresaId { get; init; }
            public string EmpresaNombre { get; init; }
            public string PasswordHash { get; init; }
            public bool Activo { get; init; }
            public int IntentosFallidos { get; init; }
            public DateTime? BloqueadoHasta { get; init; }
        }

        /// <summary>
        /// Accesos que coinciden con la clave y el correo. Puede devolver más de uno
        /// cuando el cliente activó su portal en varias empresas con la misma clave;
        /// el llamador desempata verificando la contraseña.
        /// </summary>
        private List<AccesoPortal> LeerAccesos(
            NpgsqlConnection conn, string claveCliente, string correo, int empresaId = 0)
        {
            string filtroEmpresa = empresaId > 0 ? " AND c.empresa_id = @empresa " : "";

            var parametros = new List<(string, object)>
            {
                ("correo", correo), ("clave", claveCliente)
            };
            if (empresaId > 0) parametros.Add(("empresa", empresaId));

            return Consultar(conn, $@"
                SELECT a.id_acceso, a.cliente_id, a.password_hash, a.activo,
                       a.intentos_fallidos, a.bloqueado_hasta,
                       c.cve_cli, c.n_cli, c.empresa_id, e.nombre AS empresa_nombre
                FROM portal_clientes_acceso a
                INNER JOIN catclientes c ON c.id_cliente = a.cliente_id
                LEFT JOIN empresas e ON e.empresaid = c.empresa_id
                WHERE a.correo = @correo AND c.cve_cli = @clave {filtroEmpresa}",
                parametros.ToArray())
                .Select(f => new AccesoPortal
                {
                    IdAcceso = Convert.ToInt32(f["id_acceso"]),
                    ClienteId = Convert.ToInt32(f["cliente_id"]),
                    ClaveCliente = f["cve_cli"]?.ToString(),
                    NombreCliente = f["n_cli"]?.ToString(),
                    EmpresaId = f["empresa_id"] is null or DBNull ? 0 : Convert.ToInt32(f["empresa_id"]),
                    EmpresaNombre = f["empresa_nombre"]?.ToString(),
                    PasswordHash = f["password_hash"]?.ToString(),
                    Activo = f["activo"] is bool b && b,
                    IntentosFallidos = f["intentos_fallidos"] is null or DBNull ? 0 : Convert.ToInt32(f["intentos_fallidos"]),
                    BloqueadoHasta = f["bloqueado_hasta"] as DateTime?
                })
                .ToList();
        }

        /// <summary>Suma un intento fallido y bloquea la cuenta al llegar al tope.</summary>
        private void RegistrarFalloLogin(NpgsqlConnection conn, AccesoPortal acceso, string correo)
        {
            int intentos = acceso.IntentosFallidos + 1;
            bool bloquear = intentos >= MaxIntentosFallidos;

            Ejecutar(conn, @"
                UPDATE portal_clientes_acceso
                SET intentos_fallidos = @intentos,
                    bloqueado_hasta   = CASE WHEN @bloquear THEN @hasta ELSE bloqueado_hasta END
                WHERE id_acceso = @id",
                ("intentos", intentos),
                ("bloquear", bloquear),
                ("hasta", DateTime.Now.Add(DuracionBloqueo)),
                ("id", acceso.IdAcceso));

            RegistrarBitacora(conn, acceso.ClienteId, correo, "login_fallido",
                $"intento {intentos}{(bloquear ? " — cuenta bloqueada" : "")}");
        }

        private static bool PasswordCoincide(AccesoPortal acceso, string password)
        {
            if (string.IsNullOrWhiteSpace(acceso.PasswordHash)) return false;

            try { return BCrypt.Net.BCrypt.Verify(password, acceso.PasswordHash); }
            catch { return false; }   // hash corrupto: credencial inválida, no excepción
        }

        private void RegistrarBitacora(NpgsqlConnection conn, int? clienteId, string correo,
            string evento, string detalle)
        {
            try
            {
                Ejecutar(conn, @"
                    INSERT INTO portal_clientes_bitacora (cliente_id, correo, evento, detalle, ip)
                    VALUES (@cliente, @correo, @evento, @detalle, @ip)",
                    ("cliente", (object)clienteId ?? DBNull.Value),
                    ("correo", (object)correo ?? DBNull.Value),
                    ("evento", evento),
                    ("detalle", (object)detalle ?? DBNull.Value),
                    ("ip", IpCliente()));
            }
            catch (Exception ex)
            {
                // La bitácora no debe tumbar una operación válida.
                LogErrorHelper.RegistrarLog("PortalClientes", correo ?? "",
                    $"No se pudo escribir la bitácora ({evento}): {ex.Message}", nivel: "WARN");
            }
        }


        // ── Acceso a datos, sin heredar de Utilities ───────────────────

        private static List<Dictionary<string, object>> Consultar(
            NpgsqlConnection conn, string sql, params (string Nombre, object Valor)[] parametros)
        {
            using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var (nombre, valor) in parametros)
                cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);

            var resultado = new List<Dictionary<string, object>>();
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var fila = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                resultado.Add(fila);
            }

            return resultado;
        }

        private static int Ejecutar(
            NpgsqlConnection conn, string sql, params (string Nombre, object Valor)[] parametros)
        {
            using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var (nombre, valor) in parametros)
                cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
    }
}
