using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;
using BOS_ERP.Helpers;
using Npgsql;
using System.Security.Cryptography;
using static CaptchaController;

namespace BOS_ERP.Controllers.Soporte
{
    public class EnviarTicketsController : Utilities
    {
        /// <summary>tpdoc: idtpdoc = 1 es "Ticket de soporte" (abreviatura TKS).</summary>
        private const int TipoDocTicketSoporte = 1;

        /// <summary>areas: areaid = 1 es Sistemas, el area a la que pertenece ese tipo.</summary>
        private const int AreaSistemas = 1;

        /// <summary>Codigo de movimiento del documento, igual que VIFAC/VIREM en ventas.</summary>
        private const string TpMovTicket = "TKS";

        private readonly IWebHostEnvironment _env;
        private readonly CorreoHelper _correoHelper;

        public EnviarTicketsController(IWebHostEnvironment env, CorreoHelper correoHelper)
        {
            _env = env;
            _correoHelper = correoHelper;
        }

        [Authorize]
        public IActionResult EnviarTicket()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> EnviarTicket(
            string nombre, string email, string prioridad, string asunto,
            string mensaje, string captchaRespuesta, int categoria,
            List<IFormFile> archivosAdjuntos)
        {
            var parameters = new Dictionary<string, object>();

            // Validar CAPTCHA
            if (!CaptchaHelper.ValidarCaptcha(captchaRespuesta, HttpContext))
            {
                return Json(new { success = false, error = "Captcha incorrecto. Intenta de nuevo." });
            }

            // Validaci�n b�sica de campos
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(asunto) || string.IsNullOrWhiteSpace(mensaje) ||
                string.IsNullOrWhiteSpace(prioridad) || categoria <= 0)
            {
                return Json(new { success = false, error = "Todos los campos son obligatorios." });
            }

            // Validaci�n de email
            if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return Json(new { success = false, error = "Correo electr�nico no v�lido." });
            }

            // Se valida el lote completo antes de tocar la base: si un archivo no pasa,
            // no se crea el ticket. Antes la comprobacion vivia dentro del bucle de
            // guardado, con el ticket ya insertado y algunos archivos ya en disco.
            string errorAdjuntos = SoporteAdjuntos.Validar(archivosAdjuntos);
            if (errorAdjuntos != null)
            {
                return Json(new { success = false, error = errorAdjuntos });
            }

            try
            {
                // El usuario sale de la sesion, igual que en el resto del modulo.
                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                if (usuarioId <= 0)
                    return Json(new { success = false, error = "Tu sesion expiro. Vuelve a iniciar sesion." });

                parameters.Clear();
                parameters.Add("id", usuarioId);
                var userResult = RunQuery(
                    "SELECT usuarioid, email, areaid, nombreusuario FROM usuarios WHERE usuarioid = @id", parameters);

                if (userResult.Count == 0)
                    return Json(new { success = false, error = "Usuario no encontrado." });

                // Responsable por defecto: el que tenga configurado la categoria. Antes
                // era id_usr_asig = 1 fijo, lo que ignoraba tkts_categorias.responsable y
                // mandaba todos los tickets al mismo usuario.
                parameters.Clear();
                parameters.Add("id_cat", categoria);
                var catResult = RunQuery(
                    "SELECT responsable FROM tkts_categorias WHERE id_cat = @id_cat", parameters);

                if (catResult.Count == 0)
                    return Json(new { success = false, error = "La categoria seleccionada no existe." });

                // tkts.id_usr_asig tiene FK a usuarios, y en la base hay categorias cuyo
                // "responsable" apunta a usuarios que ya no existen: asignarlo tal cual
                // haria fallar el INSERT. Se resuelve en cascada y el ultimo escalon es
                // el propio creador, que siempre existe.
                parameters.Clear();
                parameters.Add("id_cat", categoria);
                parameters.Add("id_usr", usuarioId);
                var respResult = RunQuery(
                    "SELECT COALESCE( " +
                    "    (SELECT c.responsable FROM tkts_categorias c " +
                    "       JOIN usuarios u ON u.usuarioid = c.responsable " +
                    "      WHERE c.id_cat = @id_cat), " +
                    "    (SELECT tur.id_usr FROM tkt_usuario_rol tur " +
                    "       JOIN usuarios u2 ON u2.usuarioid = tur.id_usr " +
                    "      WHERE tur.id_rol_tkt IN (1, 2) ORDER BY tur.id_usr LIMIT 1), " +
                    "    @id_usr " +
                    ") AS responsable", parameters);

                int idResponsable = Convert.ToInt32(respResult[0]["responsable"]);

                // Empresa y sucursal salen de la sesion. Antes se hacia
                // Convert.ToInt32(Session.GetInt32(...)) directo, que con la sesion vacia
                // devolvia 0 en silencio y generaba el documento contra una empresa 0.
                int empresaId = HttpContext.Session.GetInt32("Empresa") ?? 0;
                int sucursalId = HttpContext.Session.GetInt32("Sucursal") ?? 0;

                if (empresaId <= 0 || sucursalId <= 0)
                    return Json(new { success = false, error = "No hay empresa o sucursal en la sesion. Vuelve a iniciar sesion." });

                using var conn = AbrirConexion();
                using var tx = conn.BeginTransaction();

                int idTicket;
                string folioTicket;

                // El folio del documento se usa despues del commit, para el aviso y para
                // lo que se le muestra al usuario.
                string folioDocumento = "";

                try
                {
                    // El folio se genera al azar y no habia nada que garantizara que no
                    // se repitiera. Con el indice unico de sql/soporte_folio_unico.sql,
                    // una colision lanza y aqui se reintenta con otro folio.
                    (idTicket, folioTicket) = TicketFactory.InsertarConFolio(
                        this, conn, tx,
                        asunto.Trim(), mensaje.Trim(), int.Parse(prioridad),
                        usuarioId, userResult[0]["areaid"], idResponsable, categoria);

                    // Adjuntos dentro de la misma transaccion: si algo falla despues, no
                    // queda un ticket a medias con archivos registrados.
                    await SoporteAdjuntos.GuardarAsync(
                        this, _env.ContentRootPath, archivosAdjuntos, idTicket, null, conn, tx);

                    RegistrarCambioEstado(idTicket, null, 1, usuarioId, null, null, conn, tx);

                    // Documento del ERP que respalda el ticket. El tipo 1 / area 1 no era
                    // relleno: el catalogo tpdoc tiene idtpdoc = 1, idarea = 1 (Sistemas),
                    // "Ticket de soporte", abreviatura TKS. Lo que si estaba inventado era
                    // el resto (Anio = 2025 fijo, CliProv = "CL123" -que no existe en
                    // catclientes-, UsrDoc = "admin") y, sobre todo, que el folio no se
                    // guardaba en ningun lado: el documento quedaba inalcanzable.
                    string nombreUsuarioCreador = userResult[0]["nombreusuario"]?.ToString();

                    var encabezado = new DocumentoEncabezado
                    {
                        EmpresaId = empresaId,
                        IdArea = AreaSistemas,
                        IdTpDoc = TipoDocTicketSoporte,
                        Anio = DateTime.Now.Year,          // antes 2025 fijo
                        Suc = sucursalId,
                        Fch = DateTime.Now,
                        // Un ticket es interno y no tiene contraparte comercial, asi que
                        // aqui va el NombreUsuario de quien lo reporta: es lo que
                        // identifica el documento. La columna es varchar(100) sin FK y el
                        // nombre de usuario mas largo del sistema tiene 15 caracteres.
                        CliProv = nombreUsuarioCreador,
                        TpMov = TpMovTicket,
                        Coment1 = $"Ticket {folioTicket} - {asunto.Trim()}",
                        UsrDoc = nombreUsuarioCreador,
                        FchCap = DateTime.Now,
                        Estatus = 1
                    };

                    // Sin partidas: el ticket no mueve inventario ni importes.
                    var documento = GenerarDocumentoConPartidas(
                        encabezado, new List<PartidaDocumento>(), conn, tx);

                    // El enlace es lo que faltaba: sin esto no habia forma de saber que
                    // documento corresponde a que ticket.
                    if (documento != null && documento.ContainsKey("IdEncabezado"))
                    {
                        RunUpdate(
                            "UPDATE tkts SET id_encabezado = @id_encabezado WHERE id_tkts = @id_tkts",
                            new Dictionary<string, object>
                            {
                                { "id_encabezado", documento["IdEncabezado"] },
                                { "id_tkts", idTicket }
                            },
                            false, conn, tx);
                    }

                    if (documento != null && documento.ContainsKey("folio_generado"))
                    {
                        folioDocumento = documento["folio_generado"]?.ToString() ?? "";
                    }

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }

                // Aviso al responsable. Va DESPUES del commit a proposito: si se hiciera
                // dentro y la transaccion terminara en rollback, se habria avisado de un
                // ticket que no existe.
                await AvisarResponsable(
                    idResponsable, usuarioId, folioTicket, folioDocumento, asunto.Trim(), int.Parse(prioridad));

                return Json(new
                {
                    success = true,
                    idTicket,
                    folioTicket,
                    folioDocumento,
                    urlTicket = Url.Action("Ticket", "Soporte", new { id = folioTicket })
                });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/EnviarTicket", "Error al procesar el ticket");
            }
        }

        /// <summary>
        /// Avisa al responsable del ticket recien creado: correo y notificacion interna
        /// (la campana / toast que sirve SignalR).
        ///
        /// Todo el metodo esta envuelto en try/catch porque el ticket ya esta commiteado
        /// cuando se llama: que falle el SMTP no puede convertir un alta correcta en un
        /// error para quien lo reporto.
        /// </summary>
        private async Task AvisarResponsable(
            int idResponsable, int idCreador, string folioTicket, string folioDocumento,
            string asunto, int idPrioridad)
        {
            try
            {
                var responsable = RunQuery(
                    "SELECT nombreusuario, email, " +
                    "       COALESCE(nombre || ' ' || apellido, nombreusuario) AS nombre_completo " +
                    "FROM usuarios WHERE usuarioid = @id",
                    new Dictionary<string, object> { { "id", idResponsable } });

                if (responsable.Count == 0)
                    return;

                string correoResponsable = responsable[0]["email"]?.ToString();
                string usuarioResponsable = responsable[0]["nombreusuario"]?.ToString();

                if (string.IsNullOrWhiteSpace(correoResponsable))
                    return;

                var creador = RunQuery(
                    "SELECT COALESCE(nombre || ' ' || apellido, nombreusuario) AS nombre_completo " +
                    "FROM usuarios WHERE usuarioid = @id",
                    new Dictionary<string, object> { { "id", idCreador } });

                string nombreCreador = creador.Count > 0
                    ? creador[0]["nombre_completo"]?.ToString()
                    : "un usuario";

                var prioridad = RunQuery(
                    "SELECT n FROM prio WHERE id_prio = @id",
                    new Dictionary<string, object> { { "id", idPrioridad } });

                string nombrePrioridad = prioridad.Count > 0 ? prioridad[0]["n"]?.ToString() : "";

                string baseUrl = $"{Request.Scheme}://{Request.Host}";
                string urlTicket = $"{baseUrl}/Soporte/Ticket/{folioTicket}";

                // El asunto lo escribe el usuario: se codifica antes de meterlo en el HTML
                // del correo.
                string asuntoHtml = System.Net.WebUtility.HtmlEncode(asunto);
                string creadorHtml = System.Net.WebUtility.HtmlEncode(nombreCreador);
                string prioridadHtml = System.Net.WebUtility.HtmlEncode(nombrePrioridad);
                string documentoHtml = System.Net.WebUtility.HtmlEncode(folioDocumento);

                string mensajeHtml = $@"
        <table width='100%' cellpadding='0' cellspacing='0' style='font-family: Arial, sans-serif; background-color: #f4f4f4; padding: 20px;'>
            <tr>
                <td align='center'>
                    <table width='600' cellpadding='0' cellspacing='0' style='background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,0.1);'>
                        <tr style='background-color: #004080; color: #ffffff;'>
                            <td style='padding: 20px; text-align: center;'>
                                <span style='font-size: 22px;'>🎫 Nuevo ticket asignado</span>
                            </td>
                        </tr>
                        <tr>
                            <td style='padding: 30px;'>
                                <p style='font-size: 16px; color: #333;'>Hola,</p>
                                <p style='font-size: 16px; color: #333;'>
                                    <strong>{creadorHtml}</strong> levanto un ticket de soporte que quedo a tu nombre.
                                </p>

                                <table cellpadding='8' cellspacing='0' width='100%' style='margin-top: 20px; border-collapse: collapse; font-size: 15px; color: #333;'>
                                    <tr>
                                        <td style='border-bottom: 1px solid #eee; color: #777; width: 45%;'>ID de seguimiento</td>
                                        <td style='border-bottom: 1px solid #eee;'><strong>{folioTicket}</strong></td>
                                    </tr>
                                    <tr>
                                        <td style='border-bottom: 1px solid #eee; color: #777;'>Folio del documento</td>
                                        <td style='border-bottom: 1px solid #eee;'><strong>{documentoHtml}</strong></td>
                                    </tr>
                                    <tr>
                                        <td style='border-bottom: 1px solid #eee; color: #777;'>Asunto</td>
                                        <td style='border-bottom: 1px solid #eee;'>{asuntoHtml}</td>
                                    </tr>
                                    <tr>
                                        <td style='border-bottom: 1px solid #eee; color: #777;'>Prioridad</td>
                                        <td style='border-bottom: 1px solid #eee;'>{prioridadHtml}</td>
                                    </tr>
                                </table>

                                <table cellpadding='0' cellspacing='0' border='0' align='center' style='margin: 30px auto 0 auto;'>
                                    <tr bgcolor='#004080'>
                                        <td style='background-color: #004080; border-radius: 5px; text-align: center;'>
                                            <a href='{urlTicket}'
                                               style='display: inline-block; padding: 12px 24px; color: #ffffff; font-size: 16px;
                                                      text-decoration: none; font-weight: bold; font-family: Arial, sans-serif;'>
                                                🔎 Ver ticket
                                            </a>
                                        </td>
                                    </tr>
                                </table>

                                <p style='margin-top: 30px; font-size: 14px; color: #888;'>Este correo es una notificacion automatica. No respondas a este mensaje.</p>
                            </td>
                        </tr>
                        <tr style='background-color: #f0f0f0;'>
                            <td style='padding: 20px; text-align: center; font-size: 12px; color: #999;'>
                                © {DateTime.Now.Year} Sellos y Retenes. Todos los derechos reservados.
                            </td>
                        </tr>
                    </table>
                </td>
            </tr>
        </table>";

                try
                {
                    await _correoHelper.EnviarCorreoNotificacionAsync(
                        correoResponsable,
                        $"Nuevo ticket {folioTicket} - {asunto}",
                        mensajeHtml);
                }
                catch (Exception exCorreo)
                {
                    Console.WriteLine($"[Soporte] No se pudo avisar por correo a {correoResponsable}: {exCorreo.Message}");
                }

                // Notificacion interna. El payload es el mismo contrato que consume
                // _Layout ('NotificacionInterna'): icon / title / message / buttons / timer.
                // En "action" no entra texto del usuario: el cliente lo evalua con
                // new Function(...).
                _ = SendNotificationInterno(usuarioResponsable, correoResponsable, new
                {
                    icon = "info",
                    title = "Nuevo ticket de soporte",
                    message = $"{nombreCreador} levanto el ticket {folioTicket} ({nombrePrioridad}): {asunto}",
                    folio = folioTicket,
                    buttons = new[]
                    {
                        new { text = "Ver ticket", style = "primary", action = $"window.open('{urlTicket}', '_blank')" },
                        new { text = "Mas Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0
                });
            }
            catch (Exception ex)
            {
                // El ticket ya existe: el aviso es lo unico que se pierde.
                Console.WriteLine($"[Soporte] Fallo el aviso del ticket {folioTicket}: {ex.Message}");
            }
        }

    }
}