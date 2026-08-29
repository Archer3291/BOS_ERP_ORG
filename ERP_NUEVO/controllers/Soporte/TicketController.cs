using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers.Soporte
{
    public class TicketController : Utilities
    {
        [Authorize]
        public IActionResult EnviarTicket()
        {
            return View();
        }

        /// <summary>
        /// Columnas por las que se puede ordenar la tabla de tickets.
        ///
        /// El nombre llega del cliente y se concatena en el ORDER BY, asi que NO puede
        /// usarse tal cual: sin esta lista blanca seria una inyeccion de SQL directa.
        /// La clave es el "data" que declara la columna en el front.
        /// </summary>
        private static readonly Dictionary<string, string> OrdenTickets = new(StringComparer.OrdinalIgnoreCase)
        {
            { "folio_tkt",         "t.folio_tkt" },
            { "fechaactualizacion","fechaactualizacion" },
            { "categoria",         "c.n_cat" },
            { "creador",           "u.nombreusuario" },
            { "asunto",            "t.tit" },
            { "estado",            "st.n" },
            { "ultimarespuesta",   "ultimarespuesta" },
            { "prioridad",         "p.id_prio" },
            { "asignado",          "u_asig.nombreusuario" }
        };

        /// <summary>
        /// Datos de la tabla de administracion, paginados en servidor.
        ///
        /// Antes devolvia la tabla completa en cada carga, con el subconsulta de ultima
        /// respuesta recalculandose entera; ahora solo viaja la pagina pedida.
        /// </summary>
        [HttpPost]
        [Authorize]
        [SoporteStaffAuthorize]
        public JsonResult TicketTodos(IFormCollection fc)
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

                string busqueda = fc["nombre"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    parameters.Add("busqueda", $"%{busqueda}%");
                    filtros.Add("(t.folio_tkt ILIKE @busqueda OR t.tit ILIKE @busqueda " +
                                "OR u.nombreusuario ILIKE @busqueda OR c.n_cat ILIKE @busqueda)");
                }

                // Filtros de la barra superior. Llegan por el "data" de createTable.
                foreach (var (campo, columna) in new[]
                {
                    ("estado",    "t.id_stat_tkt"),
                    ("prioridad", "t.id_prio"),
                    ("creador",   "t.id_usr"),
                    ("asignado",  "t.id_usr_asig")
                })
                {
                    string valor = fc[campo].ToString();
                    if (!string.IsNullOrWhiteSpace(valor) && int.TryParse(valor, out int id) && id > 0)
                    {
                        parameters.Add(campo, id);
                        filtros.Add($"{columna} = @{campo}");
                    }
                }

                // Alcance por categoria: cada quien ve la cola de las categorias a las
                // que pertenece -como asignador o como resolvedor- y nada mas. Sistemas
                // ve todo y para esos Filtro devuelve null, dejando la consulta intacta.
                //
                // Va DESPUES de los filtros de la barra a proposito: es un recorte que
                // el usuario no puede levantar, no una opcion mas de la pantalla.
                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                string alcance = SoporteAlcance.Filtro(HttpContext.Session, usuarioId, "t.id_cat", parameters);
                if (alcance != null) filtros.Add(alcance);

                string where = filtros.Count > 0 ? " WHERE " + string.Join(" AND ", filtros) : "";

                // Orden: solo se acepta lo que este en la lista blanca.
                string sortColumn = fc["sortColumn"].ToString();
                string orden = OrdenTickets.TryGetValue(sortColumn ?? "", out var col) ? col : "fechaactualizacion";
                string dir = fc["sortDir"].ToString().Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

                string origen =
                    "FROM tkts t " +
                    "INNER JOIN usuarios u ON u.usuarioid = t.id_usr " +
                    "INNER JOIN tkts_categorias c ON c.id_cat = t.id_cat " +
                    "LEFT JOIN usuarios u_asig ON u_asig.usuarioid = t.id_usr_asig " +
                    "INNER JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt " +
                    "INNER JOIN prio p ON p.id_prio = t.id_prio " +
                    // LATERAL con LIMIT 1 en lugar de agrupar toda seg_tkts en cada carga.
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
                return ErrorConTicket(ex, "Soporte/Ticket", "Error al buscar tickets");
            }
        }

        /// <summary>
        /// Columnas ordenables de la tabla "Mis tickets". Misma razon que
        /// <see cref="OrdenTickets"/>: el nombre llega del cliente y termina en el ORDER BY.
        /// </summary>
        private static readonly Dictionary<string, string> OrdenMisTickets = new(StringComparer.OrdinalIgnoreCase)
        {
            { "folio_tkt",          "t.folio_tkt" },
            { "folio_doc",          "em.folio" },
            { "asunto",             "t.tit" },
            { "categoria",          "c.n_cat" },
            { "estado",             "st.n" },
            { "prioridad",          "p.id_prio" },
            { "asignado",           "u_asig.nombreusuario" },
            { "fch_crea",           "t.fch_crea" },
            { "fechaactualizacion", "fechaactualizacion" },
            { "respuestas",         "respuestas" }
        };

        /// <summary>
        /// Tickets creados por el usuario de la sesion, paginados en servidor.
        ///
        /// A diferencia de <see cref="TicketTodos"/> no exige ser staff: cualquiera puede
        /// ver lo suyo. El dueno NO se acepta como parametro, sale de la sesion; si
        /// llegara por el request bastaria con cambiar un id para leer los tickets de
        /// otro.
        /// </summary>
        [HttpPost]
        [Authorize]
        public JsonResult MisTickets(IFormCollection fc)
        {
            try
            {
                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                if (usuarioId <= 0)
                {
                    return Json(new { success = false, error = "Tu sesion expiro. Vuelve a iniciar sesion." });
                }

                int page = int.TryParse(fc["page"], out var pg) && pg > 0 ? pg : 1;
                int pageSize = int.TryParse(fc["pageSize"], out var ps) && ps > 0 ? ps : 10;

                var parameters = new Dictionary<string, object>
                {
                    { "offset", (page - 1) * pageSize },
                    { "pageSize", pageSize },
                    { "id_usr", usuarioId }
                };

                var filtros = new List<string> { "t.id_usr = @id_usr" };

                string busqueda = fc["nombre"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    parameters.Add("busqueda", $"%{busqueda}%");
                    filtros.Add("(t.folio_tkt ILIKE @busqueda OR t.tit ILIKE @busqueda " +
                                "OR c.n_cat ILIKE @busqueda OR t.descr ILIKE @busqueda)");
                }

                foreach (var (campo, columna) in new[]
                {
                    ("estado",    "t.id_stat_tkt"),
                    ("prioridad", "t.id_prio")
                })
                {
                    string valor = fc[campo].ToString();
                    if (!string.IsNullOrWhiteSpace(valor) && int.TryParse(valor, out int id) && id > 0)
                    {
                        parameters.Add(campo, id);
                        filtros.Add($"{columna} = @{campo}");
                    }
                }

                string where = " WHERE " + string.Join(" AND ", filtros);

                string sortColumn = fc["sortColumn"].ToString();
                string orden = OrdenMisTickets.TryGetValue(sortColumn ?? "", out var col) ? col : "fechaactualizacion";
                string dir = fc["sortDir"].ToString().Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

                string origen =
                    "FROM tkts t " +
                    "INNER JOIN tkts_categorias c ON c.id_cat = t.id_cat " +
                    "INNER JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt " +
                    "INNER JOIN prio p ON p.id_prio = t.id_prio " +
                    "LEFT JOIN usuarios u_asig ON u_asig.usuarioid = t.id_usr_asig " +
                    // Documento del ERP que respalda el ticket (el folio que se le muestra
                    // al usuario al darlo de alta).
                    "LEFT JOIN encabezadomov em ON em.id_encabezado = t.id_encabezado " +
                    // Ultimo seguimiento, con LATERAL + LIMIT 1 en vez de agrupar seg_tkts.
                    "LEFT JOIN LATERAL ( " +
                    "    SELECT s.fch FROM seg_tkts s " +
                    "    WHERE s.id_tkt = t.id_tkts ORDER BY s.fch DESC LIMIT 1 " +
                    ") ult_seg ON TRUE " +
                    where;

                string query =
                    "SELECT t.id_tkts, t.folio_tkt, t.tit AS asunto, t.descr AS descripcion, " +
                    "t.fch_crea, COALESCE(ult_seg.fch, t.fch_crea) AS fechaactualizacion, " +
                    "c.n_cat AS categoria, c.icon_class, st.n AS estado, t.id_stat_tkt, " +
                    "p.n AS prioridad, p.color, " +
                    "COALESCE(u_asig.nombreusuario, '') AS asignado, " +
                    "COALESCE(em.folio || CASE WHEN em.variacion > 0 " +
                    "         THEN '-' || num_to_letters(em.variacion) ELSE '' END, '') AS folio_doc, " +
                    "(SELECT COUNT(*) FROM seg_tkts s2 WHERE s2.id_tkt = t.id_tkts) AS respuestas " +
                    origen +
                    $" ORDER BY {orden} {dir} " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);
                int total = Convert.ToInt32(RunScalar("SELECT COUNT(*) " + origen, parameters));

                return Json(new { data, total });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Ticket", "Error al buscar tus tickets");
            }
        }

        /// <summary>
        /// Asigna varios tickets al mismo responsable desde la tabla de administraci�n.
        ///
        /// Reusa AsignarTicketSoporte, el mismo camino que CambiarResponsable: escribe en
        /// tkt_asig como bit�cora y dentro de una transacci�n. Antes este m�todo llevaba su
        /// propia copia de la l�gica; la diferencia era que aqu� s� estaba en sintaxis
        /// PostgreSQL y en EstatusController estaba en T-SQL.
        ///
        /// Tambi�n desaparece el rechazo "est� resuelto o inactivo": ese caso dejaba el
        /// ticket imposible de reasignar de forma permanente.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [SoporteStaffAuthorize]
        public JsonResult HistorialAsignacionMultiple(List<string> tickets, int usuario)
        {
            var errores = new List<string>();
            int exitos = 0;

            if (tickets == null || !tickets.Any())
            {
                return Json(new { success = false, message = "No se recibieron tickets para asignar." });
            }

            if (usuario <= 0)
            {
                return Json(new { success = false, message = "Selecciona un responsable v�lido." });
            }

            var asignadorId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (asignadorId <= 0)
            {
                return Json(new { success = false, message = "Tu sesi�n expir�. Vuelve a iniciar sesi�n." });
            }

            foreach (var folio in tickets)
            {
                try
                {
                    var tkt = SoporteAuthz.CargarPorFolio(folio);
                    if (!tkt.Existe)
                    {
                        errores.Add($"Ticket {folio}: no existe.");
                        continue;
                    }

                    // Las dos mitades del permiso de reasignar, por ticket y no una vez
                    // para todo el lote: la seleccion puede mezclar categorias, y basta
                    // que una no sea suya para que ese ticket no le toque.
                    int idCat = SoporteAlcance.CategoriaDelTicket(tkt.IdTkt);

                    if (!SoporteAlcance.PuedeAsignarEn(HttpContext.Session, asignadorId, idCat))
                    {
                        errores.Add($"Ticket {folio}: no perteneces a su categoria.");
                        continue;
                    }

                    if (!SoporteAlcance.AtiendeCategoria(usuario, idCat))
                    {
                        errores.Add($"Ticket {folio}: el responsable elegido no atiende esa categoria.");
                        continue;
                    }

                    AsignarTicketSoporte(tkt.IdTkt, usuario, asignadorId);
                    exitos++;
                }
                catch (Exception exInner)
                {
                    errores.Add($"Ticket {folio}: error al asignar. {exInner.Message}");
                }
            }

            return Json(new
            {
                success = errores.Count == 0,
                message = $"Se procesaron {tickets.Count} tickets. Asignados correctamente: {exitos}.",
                errores = errores
            });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public JsonResult ActualizarDatosTicket()
        {
            try
            {
                string idStr = Request.Form["idTkt"].ToString();
                string nuevoTitulo = Request.Form["nuevoTitulo"].ToString();
                string nuevaDescripcion = Request.Form["nuevaDescripcion"].ToString();

                if (!int.TryParse(idStr, out int idTkt))
                {
                    return Json(new { success = false, message = "ID de ticket inv�lido." });
                }

                // Validar campos
                if (string.IsNullOrWhiteSpace(nuevoTitulo) || string.IsNullOrWhiteSpace(nuevaDescripcion))
                {
                    return Json(new { success = false, message = "T�tulo y descripci�n son obligatorios." });
                }

                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                if (usuarioId <= 0)
                {
                    return Json(new { success = false, message = "Tu sesi�n expir�. Vuelve a iniciar sesi�n." });
                }

                var tkt = SoporteAuthz.CargarPorId(idTkt);
                if (!tkt.Existe)
                {
                    return Json(new { success = false, message = "El ticket no existe." });
                }

                // Asunto y descripci�n los edita el creador o el staff. Antes cualquier
                // usuario con sesi�n pod�a reescribir el contenido de cualquier ticket
                // con s�lo conocer su id.
                if (!SoporteAuthz.PuedeEditarTicket(HttpContext.Session, usuarioId, tkt))
                {
                    return Json(new { success = false, message = "No tienes permisos para editar este ticket." });
                }

                var parameters = new Dictionary<string, object>
        {
            { "titulo", nuevoTitulo },
            { "descripcion", nuevaDescripcion },
            { "id", idTkt }
        };

                string updateQuery = @"
            UPDATE tkts 
            SET tit = @titulo, 
                descr = @descripcion
            WHERE id_tkts = @id";

                RunUpdate(updateQuery, parameters);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Ticket", "No se pudo guardar el ticket");
            }
        }
    }
}