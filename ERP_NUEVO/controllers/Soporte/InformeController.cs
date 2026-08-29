using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BOS_ERP.Controllers;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using static CaptchaController;

namespace BOS_ERP.Controllers.Soporte
{
    [SoporteGerenteAuthorize]
    public class InformeController : Utilities
    {
        [SoporteStaffAuthorize]
        public JsonResult DatosInforme(int? idEstado = null, int? idPrioridad = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null)
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();

            // Si no se proporcionan fechas, usamos el primer y �ltimo d�a del mes actual
            var now = DateTime.Now;
            if (!fechaDesde.HasValue)
                fechaDesde = new DateTime(now.Year, now.Month, 1);

            if (!fechaHasta.HasValue)
                fechaHasta = fechaDesde.Value.AddMonths(1).AddDays(-1);

            // Generamos condiciones din�micas para filtros
            var filtros = "WHERE 1=1 ";
            if (idEstado.HasValue)
            {
                filtros += "AND t.id_stat_tkt = @idEstado ";
                parameters.Add("@idEstado", idEstado.Value);
            }
            if (idPrioridad.HasValue)
            {
                filtros += "AND t.id_prio = @idPrioridad ";
                parameters.Add("@idPrioridad", idPrioridad.Value);
            }
            if (fechaDesde.HasValue && fechaHasta.HasValue)
            {
                filtros += "AND CAST(t.fch_crea AS DATE) BETWEEN @fechaDesde AND @fechaHasta ";
                parameters.Add("@fechaDesde", fechaDesde.Value.Date);
                parameters.Add("@fechaHasta", fechaHasta.Value.Date);
            }

            // Alcance por categoria. Las cuatro consultas de abajo comparten el mismo
            // fragmento {filtros}, asi que basta agregarlo aqui una vez para que las
            // graficas cuenten lo mismo que muestra la tabla.
            string alcance = SoporteAlcance.Filtro(
                HttpContext.Session,
                SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0,
                "t.id_cat",
                parameters);

            if (alcance != null) filtros += $"AND {alcance} ";

            // Consultas SQL ajustadas con filtros
            string conteoTickets = $@"
        SELECT COUNT(*) AS Todos
        FROM tkts t
        {filtros}";

            string conteoEstado = $@"
        SELECT st.n AS Estado, COUNT(*) AS TotalTickets
        FROM tkts t
        INNER JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt
        {filtros}
        GROUP BY st.n
        ORDER BY TotalTickets DESC;";

            string conteoFecha = $@"
        SELECT CAST(t.fch_crea AS DATE) AS Fecha, COUNT(*) AS TotalTickets
        FROM tkts t
        {filtros}
        GROUP BY CAST(t.fch_crea AS DATE)
        ORDER BY Fecha DESC;";

            string conteoPrioridad = $@"
        SELECT p.n, t.id_prio, COUNT(*) AS TotalTickets
        FROM tkts t
        INNER JOIN prio p ON p.id_prio = t.id_prio
        {filtros}
        GROUP BY t.id_prio, p.n
        ORDER BY TotalTickets DESC;";

            // La tabla de detalle ya no viaja aqui: la pide createTable a
            // DetalleTickets, paginada. Antes se traian TODOS los tickets del periodo
            // en cada refresco del informe, solo para pintar la tabla.

            // Ejecutar consultas
            var conteoTicketsResult = RunQuery(conteoTickets, parameters);
            var conteoEstadoActivoResult = RunQuery(conteoEstado, parameters);
            var conteoFechaResult = RunQuery(conteoFecha, parameters);
            var conteoPrioridadResult = RunQuery(conteoPrioridad, parameters);

            // Armar resultado
            result.Add("conteoTickets", conteoTicketsResult);
            result.Add("conteoEstadoActivo", conteoEstadoActivoResult);
            result.Add("conteoFecha", conteoFechaResult);
            result.Add("conteoPrioridad", conteoPrioridadResult);

            return Json(result);
        }


        /// <summary>Columnas ordenables del detalle. Lista blanca: el valor llega del cliente.</summary>
        private static readonly Dictionary<string, string> OrdenDetalle = new(StringComparer.OrdinalIgnoreCase)
        {
            { "folio_tkt",          "t.folio_tkt" },
            { "asunto",             "t.tit" },
            { "categoria",          "c.n_cat" },
            { "prioridad",          "p.id_prio" },
            { "estado",             "st.n" },
            { "fechaactualizacion", "fechaactualizacion" },
            { "creador",            "u.nombreusuario" },
            { "asignado",           "u_asig.nombreusuario" }
        };

        /// <summary>
        /// Detalle de tickets del informe, paginado en servidor.
        /// Respeta los mismos filtros que los graficos (estado, prioridad y rango de fechas).
        /// </summary>
        [HttpPost]
        [Authorize]
        [SoporteStaffAuthorize]
        public JsonResult DetalleTickets(IFormCollection fc)
        {
            try
            {
                int page = int.TryParse(fc["page"], out var pg) && pg > 0 ? pg : 1;
                int pageSize = int.TryParse(fc["pageSize"], out var ps) && ps > 0 ? ps : 10;

                var parameters = new Dictionary<string, object>
                {
                    { "offset", (page - 1) * pageSize },
                    { "pageSize", pageSize }
                };

                var filtros = new List<string>();

                if (int.TryParse(fc["idEstado"], out int idEstado) && idEstado > 0)
                {
                    parameters.Add("idEstado", idEstado);
                    filtros.Add("t.id_stat_tkt = @idEstado");
                }

                if (int.TryParse(fc["idPrioridad"], out int idPrioridad) && idPrioridad > 0)
                {
                    parameters.Add("idPrioridad", idPrioridad);
                    filtros.Add("t.id_prio = @idPrioridad");
                }

                if (DateTime.TryParse(fc["fechaDesde"], out DateTime desde))
                {
                    parameters.Add("fechaDesde", desde.Date);
                    filtros.Add("CAST(t.fch_crea AS DATE) >= @fechaDesde");
                }

                if (DateTime.TryParse(fc["fechaHasta"], out DateTime hasta))
                {
                    parameters.Add("fechaHasta", hasta.Date);
                    filtros.Add("CAST(t.fch_crea AS DATE) <= @fechaHasta");
                }

                string busqueda = fc["nombre"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    parameters.Add("busqueda", $"%{busqueda}%");
                    filtros.Add("(t.folio_tkt ILIKE @busqueda OR t.tit ILIKE @busqueda " +
                                "OR u.nombreusuario ILIKE @busqueda OR c.n_cat ILIKE @busqueda)");
                }

                // El informe se acota igual que la tabla de tickets: no tendria sentido
                // esconder una categoria en Administracion y dejar que sus cifras se
                // consulten aqui.
                string alcance = SoporteAlcance.Filtro(
                    HttpContext.Session,
                    SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0,
                    "t.id_cat",
                    parameters);

                if (alcance != null) filtros.Add(alcance);

                string where = filtros.Count > 0 ? " WHERE " + string.Join(" AND ", filtros) : "";

                string sortColumn = fc["sortColumn"].ToString();
                string orden = OrdenDetalle.TryGetValue(sortColumn ?? "", out var col) ? col : "fechaactualizacion";
                string dir = fc["sortDir"].ToString().Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

                string origen =
                    "FROM tkts t " +
                    "INNER JOIN usuarios u ON u.usuarioid = t.id_usr " +
                    "INNER JOIN tkts_categorias c ON c.id_cat = t.id_cat " +
                    "LEFT JOIN usuarios u_asig ON u_asig.usuarioid = t.id_usr_asig " +
                    "INNER JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt " +
                    "INNER JOIN prio p ON p.id_prio = t.id_prio " +
                    // LATERAL en vez de agrupar toda seg_tkts en cada consulta.
                    "LEFT JOIN LATERAL ( " +
                    "    SELECT s.fch, s.id_usr FROM seg_tkts s " +
                    "    WHERE s.id_tkt = t.id_tkts ORDER BY s.fch DESC LIMIT 1 " +
                    ") ult_seg ON TRUE " +
                    "LEFT JOIN usuarios u2 ON u2.usuarioid = ult_seg.id_usr " +
                    where;

                string query =
                    "SELECT t.id_tkts, t.folio_tkt, u.nombreusuario AS creador, " +
                    "u_asig.nombreusuario AS asignado, " +
                    "COALESCE(ult_seg.fch, t.fch_crea) AS fechaactualizacion, " +
                    "c.n_cat AS categoria, t.tit AS asunto, st.n AS estado, " +
                    "COALESCE(u2.nombreusuario, u.nombreusuario) AS ultimarespuesta, " +
                    "p.n AS prioridad, p.color " +
                    origen +
                    $" ORDER BY {orden} {dir} " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);
                int total = Convert.ToInt32(RunScalar("SELECT COUNT(*) " + origen, parameters));

                return Json(new { data, total });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Informe", "Error al obtener el detalle");
            }
        }

        [SoporteStaffAuthorize]
        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();

            // El filtro "asignado" del informe se acota igual que sus cifras: ofrecer
            // nombres de gente de otras categorias dejaria filtros que siempre
            // devuelven cero y revelaria quien mas atiende tickets.
            var parametrosUsuarios = new Dictionary<string, object>();
            string alcanceUsuarios = SoporteAlcance.FiltroMiembros(
                HttpContext.Session,
                SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0,
                "u.UsuarioId",
                parametrosUsuarios);

            string queryUsuarios = "SELECT " +
                "u.UsuarioId AS id_usr, " +
                "u.NombreUsuario AS nombre " +
                "FROM " +
                "Usuarios u " +
                "INNER JOIN " +
                "tkt_usuario_rol tur ON tur.id_usr = u.UsuarioId " +
                "WHERE " +
                "tur.id_rol_tkt IN (1, 2) " +
                (alcanceUsuarios != null ? $"AND {alcanceUsuarios} " : "") +
                "ORDER BY " +
                "u.NombreUsuario DESC;";
            string queryEstatus = "SELECT id_stat_tkt ,n FROM stat_tkt";

            // El filtro de prioridad del informe estaba escrito a mano en la vista.
            string queryPrioridades = "SELECT id_prio, n FROM prio ORDER BY id_prio";

            result.Add("usuarios", RunQuery(queryUsuarios, parametrosUsuarios));
            result.Add("estatus", RunQuery(queryEstatus));
            result.Add("prioridades", RunQuery(queryPrioridades));

            return Json(result);
        }
    }
}