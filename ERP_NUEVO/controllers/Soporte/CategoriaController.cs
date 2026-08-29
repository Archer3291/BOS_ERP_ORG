using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers.Soporte
{
    [SoporteGerenteAuthorize]
    public class CategoriaController : Utilities
    {
        [Authorize]
        public IActionResult EnviarTicket()
        {
            return View();
        }
        
        /// <summary>
        /// Rechazo con el mismo texto en "error" y en "message".
        ///
        /// El JS de esta pantalla lee data.error en los tres modales, pero el resto del
        /// módulo lee data.message. Mandar sólo uno hacía que el motivo real del rechazo
        /// se perdiera y saliera el texto genérico ("No se pudo crear."), que no dice
        /// nada. Es el mismo criterio que ya usa ErrorConTicket.
        /// </summary>
        private JsonResult Rechazo(string mensaje)
            => Json(new { success = false, error = mensaje, message = mensaje });

        [Authorize]
        [HttpGet]
        [SoporteStaffAuthorize]
        public JsonResult Datos()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();

            string prioridadQuery = "SELECT id_prio, n FROM prio";

            // Los candidatos a responsable se acotan al equipo de MIS categorías. Este
            // combo alimenta el alta -reservada a Sistemas, que ve a todos- pero también
            // la edición, que sí puede usar un asignador: sin el filtro podía entregarle
            // su categoría a cualquiera del ERP con rol de tickets.
            var parametros = new Dictionary<string, object>();
            string alcance = SoporteAlcance.FiltroMiembros(
                HttpContext.Session,
                SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0,
                "u.UsuarioId",
                parametros);

            string responsableQuery = @"
        SELECT UsuarioId, NombreUsuario
        FROM Usuarios u
        INNER JOIN tkt_usuario_rol tur ON tur.id_usr = u.UsuarioId
        WHERE tur.id_rol_tkt IN (1, 2) " +
        (alcance != null ? $"AND {alcance} " : "") + @"
        ORDER BY NombreUsuario DESC";

            var resultPrioridad = RunQuery(prioridadQuery);
            var resultResponsable = RunQuery(responsableQuery, parametros);

            result.Add("prioridad", resultPrioridad);
            result.Add("responsable", resultResponsable);

            // La pantalla necesita saber si puede ofrecer el botón de alta. Esconderlo
            // no es la seguridad -esa la pone CrearCategoria- pero sí evita el camino
            // de llenar el formulario entero para que al final lo rechacen.
            result.Add("permisos", new List<Dictionary<string, object>>
            {
                new()
                {
                    { "puedeCrear", SoporteAlcance.VeTodo(
                        HttpContext.Session, SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0) }
                }
            });

            return Json(result); // asegúrate que sea compatible con GET
        }

        /// <summary>Columnas ordenables. Lista blanca: el valor llega del cliente.</summary>
        private static readonly Dictionary<string, string> OrdenCategorias = new(StringComparer.OrdinalIgnoreCase)
        {
            { "id_cat",        "c.id_cat" },
            { "n_cat",         "c.n_cat" },
            { "prioridad",     "p.id_prio" },
            { "total_tickets", "total_tickets" }
        };

        /// <summary>Catálogo de categorías, paginado en servidor para createTable.</summary>
        [HttpPost]
        [Authorize]
        [SoporteStaffAuthorize]
        public JsonResult CategoriasTodos(IFormCollection fc)
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

                string where = " WHERE c.activo = true ";
                string busqueda = fc["nombre"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    parameters.Add("busqueda", $"%{busqueda}%");
                    where += " AND c.n_cat ILIKE @busqueda ";
                }

                // Cada quien administra las categorías a las que pertenece. Sistemas ve
                // todas y para ellos Filtro devuelve null.
                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                string alcance = SoporteAlcance.Filtro(HttpContext.Session, usuarioId, "c.id_cat", parameters);
                if (alcance != null) where += $" AND {alcance} ";

                string sortColumn = fc["sortColumn"].ToString();
                string orden = OrdenCategorias.TryGetValue(sortColumn ?? "", out var col) ? col : "total_tickets";
                string dir = fc["sortDir"].ToString().Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

                string origen =
                    "FROM tkts_categorias c " +
                    "LEFT JOIN (SELECT id_cat, COUNT(*) AS total_tickets FROM tkts GROUP BY id_cat) t " +
                    "       ON c.id_cat = t.id_cat " +
                    "CROSS JOIN (SELECT COUNT(*) AS total_global FROM tkts) tt " +
                    "INNER JOIN prio p ON p.id_prio = c.id_prio " +
                    where;

                string query =
                    "SELECT c.id_cat, c.n_cat, p.n AS prioridad, c.id_prio, c.icon_class, " +
                    "c.responsable, u.nombreusuario AS responsable_nombre, " +
                    "COALESCE(t.total_tickets, 0) AS total_tickets, " +
                    "ROUND(COALESCE(t.total_tickets * 100.0 / NULLIF(tt.total_global, 0), 0), 2) AS porcentaje " +
                    origen.Replace("INNER JOIN prio p", "LEFT JOIN usuarios u ON u.usuarioid = c.responsable INNER JOIN prio p") +
                    $" ORDER BY {orden} {dir} " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);
                int total = Convert.ToInt32(RunScalar("SELECT COUNT(*) " + origen, parameters));

                return Json(new { data, total });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Categoria", "Error al obtener categorías");
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [SoporteStaffAuthorize]
        public JsonResult CrearCategoria(string nombre, int responsable, int prioridad, string icono)
        {
            // Dar de alta una categoría es tocar la taxonomía del módulo entero, no
            // administrar la propia: define a dónde van a caer tickets de todos y quién
            // los recibe. Se reserva a Sistemas.
            //
            // Editar sí sigue abierto al asignador de esa categoría (renombrar, cambiar
            // prioridad o icono es trabajo del día a día), acotado por PuedeAsignarEn.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (!SoporteAlcance.VeTodo(HttpContext.Session, usuarioId))
                return Rechazo("Sólo Sistemas puede crear categorías.");

            try
            {
                string query = @"INSERT INTO tkts_categorias (n_cat, responsable, id_prio, icon_class)
                         VALUES (@nombre, @responsable, @prioridad, @icono)";

                var parametros = new Dictionary<string, object>
        {
            { "nombre", nombre },
            { "responsable", responsable },
            { "prioridad", prioridad },
            { "icono", icono }
        };

                RunQuery(query, parametros); 

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Categoria", "No se pudo crear la categoría");
            }
        }


        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [SoporteStaffAuthorize]
        public JsonResult EditarCategoria(int id, string nombre, int responsable, int prioridad, string icono)
        {
            // Acotar el listado no basta: el id llega del cliente, y sin esta guarda un
            // asignador podía editar la categoría de otro con sólo mandar su id.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (!SoporteAlcance.PuedeAsignarEn(HttpContext.Session, usuarioId, id))
                return Rechazo("Esa categoría no está a tu cargo.");

            try
            {
                string query = @"UPDATE tkts_categorias 
                         SET n_cat = @nombre, responsable = @responsable, id_prio = @prioridad, icon_class = @icono 
                         WHERE id_cat = @id";

                var parametros = new Dictionary<string, object>
        {
            { "id", id },
            { "nombre", nombre },
            { "responsable", responsable },
            { "prioridad", prioridad },
            { "icono", icono }
        };

                RunUpdate(query, parametros);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Categoria", "No se pudo editar la categoría");
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [SoporteStaffAuthorize]
        public JsonResult EliminarCategoria(int id)
        {
            // Igual que crear: retirar una categoría reordena la taxonomía de todos.
            // Y además es peor que crear, porque deja huérfanos los tickets que ya
            // estaban dentro. Sólo Sistemas.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (!SoporteAlcance.VeTodo(HttpContext.Session, usuarioId))
                return Rechazo("Sólo Sistemas puede eliminar categorías.");

            try
            {
                string query = @"UPDATE tkts_categorias 
                         SET activo = false
                         WHERE id_cat = @id";

                var parametros = new Dictionary<string, object>
        {
            { "id", id }
        };

                RunUpdate(query, parametros);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Categoria", "No se pudo eliminar la categoría");
            }
        }

    }
}