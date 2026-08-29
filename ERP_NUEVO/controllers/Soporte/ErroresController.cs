using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Filters;

namespace BOS_ERP.Controllers.Soporte
{
    /// <summary>
    /// Tablero de errores del sistema: qué se está rompiendo, cuánto y a cuántos.
    ///
    /// Es lo que convierte la captura automática en herramienta de trabajo. La tabla
    /// de huellas ordenada por ocurrencias responde "¿qué se rompe más?", una
    /// pregunta que hoy nadie en el equipo puede contestar.
    ///
    /// TODO ES SÓLO PARA STAFF. Se enseñan stack traces, nombres de tablas,
    /// constraints y parámetros de peticiones de otros usuarios.
    /// </summary>
    [Authorize]
    [SoporteStaffAuthorize]
    public class ErroresController : Utilities
    {
        [RightAuthorize("sistemas")]
        [AreaAuthorize("Sistemas")]
        public IActionResult Index() => View("~/Views/Soporte/Errores.cshtml");

        /// <summary>
        /// Columnas por las que se puede ordenar.
        ///
        /// El nombre llega del cliente y se concatena en el ORDER BY: sin esta lista
        /// blanca sería una inyección de SQL directa. Misma razón que en
        /// TicketController.OrdenTickets.
        /// </summary>
        private static readonly Dictionary<string, string> OrdenHuellas = new(StringComparer.OrdinalIgnoreCase)
        {
            { "id_huella",   "h.id_huella" },
            { "tipo_exc",    "h.tipo_exc" },
            { "mensaje",     "h.mensaje_norm" },
            { "origen",      "h.origen" },
            { "ocurrencias", "h.ocurrencias" },
            { "afectados",   "h.afectados" },
            { "primera_vez", "h.primera_vez" },
            { "ultima_vez",  "h.ultima_vez" },
            { "folio_tkt",   "t.folio_tkt" },
            { "estado",      "st.n" }
        };

        [HttpPost]
        public JsonResult Listado(IFormCollection fc)
        {
            try
            {
                int page = int.TryParse(fc["page"], out var pg) && pg > 0 ? pg : 1;
                int pageSize = int.TryParse(fc["pageSize"], out var ps) && ps > 0 ? ps : 25;

                var parameters = new Dictionary<string, object>
                {
                    { "offset", (page - 1) * pageSize },
                    { "pageSize", pageSize }
                };

                var filtros = new List<string>();

                string busqueda = fc["nombre"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    parameters.Add("busqueda", $"%{busqueda}%");
                    filtros.Add("(h.tipo_exc ILIKE @busqueda OR h.mensaje_norm ILIKE @busqueda " +
                                "OR h.origen ILIKE @busqueda OR t.folio_tkt ILIKE @busqueda)");
                }

                // Por defecto se esconde el ruido ya silenciado: quien lo quiera ver
                // lo pide explícitamente.
                if (fc["silenciadas"].ToString() != "1")
                    filtros.Add("h.silenciada = false");

                string capa = fc["capa"].ToString();
                if (!string.IsNullOrWhiteSpace(capa))
                {
                    parameters.Add("capa", capa);
                    filtros.Add("EXISTS (SELECT 1 FROM tkt_error_detalle d " +
                                "         WHERE d.id_huella = h.id_huella AND d.capa = @capa)");
                }

                string where = filtros.Count > 0 ? " WHERE " + string.Join(" AND ", filtros) : "";

                // Default: lo que más se rompe primero. createTable arranca con
                // sortColumn en null, así que la carga inicial cae siempre aquí, y es
                // el orden que la vista promete ("ordena por ocurrencias para ver qué urge").
                string sortColumn = fc["sortColumn"].ToString();
                string orden = OrdenHuellas.TryGetValue(sortColumn ?? "", out var col) ? col : "h.ocurrencias";
                string dir = fc["sortDir"].ToString().Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

                string origen =
                    "FROM tkt_error_huella h " +
                    "LEFT JOIN tkts t      ON t.id_tkts = h.id_tkt " +
                    "LEFT JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt " +
                    where;

                var data = RunQuery(
                    "SELECT h.id_huella, h.tipo_exc, h.mensaje_norm AS mensaje, h.origen, " +
                    "       h.ocurrencias, h.afectados, h.silenciada, " +
                    "       h.primera_vez, h.ultima_vez, " +
                    "       t.folio_tkt, COALESCE(st.n, '(sin ticket)') AS estado " +
                    origen +
                    $" ORDER BY {orden} {dir} " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    parameters);

                int total = Convert.ToInt32(RunScalar("SELECT COUNT(*) " + origen, parameters));

                return Json(new { data, total });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Errores", "Error al cargar el tablero de errores");
            }
        }

        /// <summary>
        /// Las cifras de cabecera del tablero.
        ///
        /// Van arriba y no en la tabla porque responden lo primero que uno pregunta al
        /// abrir la pantalla -¿está el sistema bien?- sin tener que leer filas. El
        /// estado del cortacircuitos es el más importante: si está abierto, la tabla
        /// no muestra todo lo que está pasando.
        /// </summary>
        [HttpPost]
        public JsonResult Resumen()
        {
            try
            {
                var f = RunQuery(
                    "SELECT " +
                    "  (SELECT COUNT(*) FROM tkt_error_huella WHERE NOT silenciada) AS tipos, " +
                    "  (SELECT COUNT(*) FROM tkt_error_huella WHERE silenciada) AS silenciadas, " +
                    "  (SELECT COALESCE(SUM(ocurrencias), 0) FROM tkt_error_huella " +
                    "    WHERE NOT silenciada AND ultima_vez > now() - interval '24 hours') AS ocurrencias24h, " +
                    "  (SELECT COUNT(*) FROM tkt_error_huella " +
                    "    WHERE NOT silenciada AND primera_vez > now() - interval '24 hours') AS nuevas24h, " +
                    "  (SELECT COUNT(*) FROM tkt_error_huella h " +
                    "     JOIN tkts t ON t.id_tkts = h.id_tkt " +
                    "    WHERE t.id_stat_tkt <> 4) AS ticketsAbiertos");

                int tope = _configuration.GetValue("CapturaErrores:TicketsPorHora", 20);

                var usados = RunScalar(
                    "SELECT COUNT(*) FROM tkts t " +
                    "JOIN tkts_categorias c ON c.id_cat = t.id_cat " +
                    "WHERE c.n_cat = 'Errores del sistema' " +
                    "  AND t.fch_crea > now() - interval '1 hour'",
                    new Dictionary<string, object>());

                int n = Convert.ToInt32(usados);

                return Json(new
                {
                    success = true,
                    resumen = f.Count > 0 ? f[0] : null,
                    cortacircuitos = new { tope, usados = n, abierto = n >= tope }
                });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Errores", "No se pudo cargar el resumen");
            }
        }

        /// <summary>Las últimas ocurrencias de una huella, para el detalle expandible.</summary>
        [HttpPost]
        public JsonResult Ocurrencias(int idHuella)
        {
            try
            {
                if (idHuella <= 0)
                    return Json(new { success = false, message = "Huella inválida." });

                var data = RunQuery(
                    "SELECT d.id_detalle, d.fch, d.capa, d.ruta, d.metodo, d.trace_id, d.ip, " +
                    "       COALESCE(u.nombreusuario, '(sin sesion)') AS usuario, " +
                    "       d.payload::text AS payload " +
                    "FROM tkt_error_detalle d " +
                    "LEFT JOIN usuarios u ON u.usuarioid = d.id_usr " +
                    "WHERE d.id_huella = @id_huella " +
                    "ORDER BY d.fch DESC LIMIT 25",
                    new Dictionary<string, object> { { "id_huella", idHuella } });

                return Json(new { success = true, data });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Errores", "Error al cargar las ocurrencias");
            }
        }

        /// <summary>
        /// Silencia o reactiva una huella.
        ///
        /// Silenciar NO borra: se sigue registrando cada ocurrencia en
        /// tkt_error_detalle, sólo se deja de crear y tocar tickets. Es la válvula
        /// para el ruido conocido -un error de un módulo que se va a retirar, una
        /// librería de terceros que escupe warnings- sin perder el histórico.
        /// </summary>
        [HttpPost]
        public JsonResult Silenciar(int idHuella, bool silenciar)
        {
            try
            {
                if (idHuella <= 0)
                    return Json(new { success = false, message = "Huella inválida." });

                RunUpdate(
                    "UPDATE tkt_error_huella SET silenciada = @silenciada WHERE id_huella = @id_huella",
                    new Dictionary<string, object>
                    {
                        { "silenciada", silenciar },
                        { "id_huella", idHuella }
                    });

                return Json(new
                {
                    success = true,
                    message = silenciar
                        ? "Huella silenciada. Se seguirá registrando, pero ya no generará tickets."
                        : "Huella reactivada. Volverá a generar tickets."
                });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Errores", "No se pudo cambiar el silenciado");
            }
        }
    }
}
