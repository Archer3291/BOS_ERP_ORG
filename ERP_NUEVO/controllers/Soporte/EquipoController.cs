using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers.Soporte
{
    [SoporteGerenteAuthorize]
    public class EquipoController : Utilities
    {
        /// <summary>tkt_roles: 3 = Usuario, el rol del usuario final (no atiende tickets).</summary>
        private const int RolTicketsUsuarioFinal = 3;

        [SoporteStaffAuthorize]
        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryRoles = "SELECT id_rol_tkt,nombre FROM tkt_roles  ORDER BY nombre DESC";


            result.Add("roles", RunQuery(queryRoles));

            // Las categorias que este usuario puede repartir. Un asignador solo puede
            // meter gente a las suyas, asi que el combo se sirve ya acotado: si llegara
            // completo, la pantalla ofreceria opciones que Guardar va a rechazar.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            var parametros = new Dictionary<string, object>();
            string alcance = SoporteAlcance.Filtro(HttpContext.Session, usuarioId, "c.id_cat", parametros);

            result.Add("categorias", RunQuery(
                "SELECT c.id_cat, c.n_cat FROM tkts_categorias c " +
                "WHERE c.activo = true " +
                (alcance != null ? $"AND {alcance} " : "") +
                "ORDER BY c.n_cat",
                parametros));

            return Json(result);
        }

        /// <summary>
        /// Que categorias atiende una persona.
        ///
        /// Devuelve TODAS las suyas, no solo las que el que pregunta administra: la
        /// pantalla necesita saber cuales ya tiene marcadas para no borrarlas sin
        /// querer al guardar. Lo que si se acota es cuales puede MODIFICAR, y eso lo
        /// decide Guardar; aqui cada categoria viene con su bandera "editable" para
        /// que la vista las muestre deshabilitadas en vez de esconderlas.
        /// </summary>
        [HttpPost]
        [Authorize]
        [SoporteStaffAuthorize]
        public JsonResult CategoriasDe(int idUsuario)
        {
            if (idUsuario <= 0)
                return Json(new { success = false, message = "Usuario invalido." });

            if (!SoporteAlcance.TablaLista())
                return Json(new { success = false, message = "Falta correr sql/soporte_resolvedores_categoria.sql." });

            try
            {
                var yo = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                var mias = SoporteAlcance.De(HttpContext.Session, yo);

                var data = RunQuery(
                    "SELECT c.id_cat, c.n_cat, " +
                    "       (cr.id_usr IS NOT NULL) AS atiende, " +
                    "       (c.responsable = @usr)  AS es_asignador " +
                    "FROM tkts_categorias c " +
                    "LEFT JOIN tkt_categoria_resolvedor cr " +
                    "       ON cr.id_cat = c.id_cat AND cr.id_usr = @usr " +
                    "WHERE c.activo = true ORDER BY c.n_cat",
                    new Dictionary<string, object> { { "usr", idUsuario } });

                foreach (var fila in data)
                {
                    int idCat = Convert.ToInt32(fila["id_cat"]);
                    fila["editable"] = mias.VeTodo || mias.Categorias.Contains(idCat);
                }

                return Json(new { success = true, data });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Equipo", "No se pudieron cargar las categorias");
            }
        }

        /// <summary>
        /// Guarda que categorias atiende una persona.
        ///
        /// Solo toca las categorias que el que guarda administra. Las demas se quedan
        /// como estaban aunque no vengan en la lista: si se borrara todo lo que no
        /// llega, un asignador de Almacen sacaria a esa persona de Contabilidad sin
        /// enterarse, con solo abrir el modal y darle guardar.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [SoporteStaffAuthorize]
        public JsonResult GuardarCategorias(int idUsuario, List<int> categorias)
        {
            if (idUsuario <= 0)
                return Json(new { success = false, message = "Usuario invalido." });

            if (!SoporteAlcance.TablaLista())
                return Json(new { success = false, message = "Falta correr sql/soporte_resolvedores_categoria.sql." });

            var yo = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (yo <= 0)
                return Json(new { success = false, message = "Tu sesion expiro. Vuelve a iniciar sesion." });

            categorias ??= new List<int>();

            // Lo que el que guarda tiene derecho a tocar. Todo lo que caiga fuera se
            // ignora en silencio en vez de rechazar la operacion completa: la peticion
            // pudo salir de una pantalla con datos de hace un rato.
            var mias = SoporteAlcance.De(HttpContext.Session, yo);
            var pedidas = categorias.Where(c => c > 0).Distinct().ToList();

            if (!mias.VeTodo)
                pedidas = pedidas.Where(c => mias.Categorias.Contains(c)).ToList();

            try
            {
                var parametros = new Dictionary<string, object>
                {
                    { "usr", idUsuario },
                    { "pedidas", pedidas.ToArray() },
                    { "alta_por", yo }
                };

                // El ambito del borrado es la clave de todo el metodo: se limita a las
                // categorias del que guarda. Un asignador no puede dejar sin cobertura
                // una categoria que no es suya.
                string ambito;
                if (mias.VeTodo)
                {
                    ambito = "";
                }
                else
                {
                    parametros["mias"] = mias.Categorias.ToArray();
                    ambito = " AND id_cat = ANY(@mias)";
                }

                RunUpdate(
                    "DELETE FROM tkt_categoria_resolvedor " +
                    "WHERE id_usr = @usr AND NOT (id_cat = ANY(@pedidas))" + ambito,
                    parametros);

                if (pedidas.Count > 0)
                {
                    RunUpdate(
                        "INSERT INTO tkt_categoria_resolvedor (id_cat, id_usr, alta_por) " +
                        "SELECT c.id_cat, @usr, @alta_por FROM tkts_categorias c " +
                        "WHERE c.activo = true AND c.id_cat = ANY(@pedidas) " +
                        "ON CONFLICT (id_cat, id_usr) DO NOTHING",
                        parametros);
                }

                return Json(new { success = true, message = $"Categorias actualizadas: {pedidas.Count}." });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Equipo", "No se pudieron guardar las categorias");
            }
        }

        /// <summary>Columnas ordenables. Lista blanca: el valor llega del cliente.</summary>
        private static readonly Dictionary<string, string> OrdenEquipo = new(StringComparer.OrdinalIgnoreCase)
        {
            { "nombre",        "u.nombre" },
            { "apellido",      "u.apellido" },
            { "email",         "u.email" },
            { "nombreusuario", "u.nombreusuario" },
            { "nombrerol",     "tr.nombre" }
        };

        /// <summary>Equipo de soporte, paginado en servidor para createTable.</summary>
        [HttpPost]
        [Authorize]
        [SoporteStaffAuthorize]
        public JsonResult UsuariosTodos(IFormCollection fc)
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

                string where = "";
                string busqueda = fc["nombre"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    parameters.Add("busqueda", $"%{busqueda}%");
                    where = " WHERE (u.nombre ILIKE @busqueda OR u.apellido ILIKE @busqueda " +
                            "OR u.email ILIKE @busqueda OR u.nombreusuario ILIKE @busqueda) ";
                }

                string sortColumn = fc["sortColumn"].ToString();
                string orden = OrdenEquipo.TryGetValue(sortColumn ?? "", out var col) ? col : "u.nombreusuario";
                string dir = fc["sortDir"].ToString().Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

                // Alcance: cada asignador administra a la gente de SUS categorias.
                // Sistemas ve la plantilla completa y para ellos FiltroMiembros
                // devuelve null.
                var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
                string alcance = SoporteAlcance.FiltroMiembros(
                    HttpContext.Session, usuarioId, "u.usuarioid", parameters);

                if (alcance != null)
                    where += (string.IsNullOrEmpty(where) ? " WHERE " : " AND ") + alcance;

                string origen =
                    "FROM usuarios u " +
                    "INNER JOIN tkt_usuario_rol ur ON ur.id_usr = u.usuarioid " +
                    "INNER JOIN tkt_roles tr ON tr.id_rol_tkt = ur.id_rol_tkt " +
                    where;

                // Las categorias que atiende cada quien viajan agregadas en la misma
                // consulta: pedirlas despues seria una consulta por fila.
                //
                // Se omite mientras la tabla no exista. Sin esta guarda, una base donde
                // todavia no se ha corrido sql/soporte_resolvedores_categoria.sql
                // responderia 42P01 y dejaria la pestana Equipo en blanco.
                string columnaCategorias = SoporteAlcance.TablaLista()
                    ? "(SELECT string_agg(c.n_cat, ', ' ORDER BY c.n_cat) " +
                      "   FROM tkt_categoria_resolvedor cr " +
                      "   JOIN tkts_categorias c ON c.id_cat = cr.id_cat AND c.activo = true " +
                      "  WHERE cr.id_usr = u.usuarioid) AS categorias "
                    : "NULL::text AS categorias ";

                string query =
                    "SELECT u.usuarioid, u.nombre, u.apellido, u.email, u.nombreusuario, " +
                    "u.rolid, tr.nombre AS nombrerol, tr.id_rol_tkt, " +
                    columnaCategorias +
                    origen +
                    $" ORDER BY {orden} {dir} " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);
                int total = Convert.ToInt32(RunScalar("SELECT COUNT(*) " + origen, parameters));

                return Json(new { data, total });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Equipo", "Error al obtener el equipo");
            }
        }

        /// <summary>
        /// Cambia el rol de tickets de un usuario.
        ///
        /// El JS mandaba JSON, pero la accion no lleva [FromBody] y el proyecto no usa
        /// [ApiController], asi que el DTO llegaba con todo en null y la respuesta era
        /// siempre "Datos invalidos". Ahora el front manda FormData, que ademas permite
        /// viajar el token antiforgery.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [SoporteStaffAuthorize]
        public JsonResult Editar(UsuarioDto dto)
        {
            if (dto == null || dto.Id <= 0)
            {
                return Json(new { success = false, message = "Usuario invalido." });
            }

            if (!int.TryParse(dto.Rol, out int idRol) || idRol <= 0)
            {
                return Json(new { success = false, message = "Rol invalido." });
            }

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id_rol_tkt", idRol },
                    { "id_usr", dto.Id }
                };

                // UPSERT: hay usuarios sin fila en tkt_usuario_rol (solo se crea al
                // registrarse), y sin esto editarlos no hacia nada en silencio.
                // El ON CONFLICT se apoya en el UNIQUE(id_usr) que agrega sql/soporte_staff.sql.
                string usuarioQuery =
                    "INSERT INTO tkt_usuario_rol (id_usr, id_rol_tkt) VALUES (@id_usr, @id_rol_tkt) " +
                    "ON CONFLICT (id_usr) DO UPDATE SET id_rol_tkt = EXCLUDED.id_rol_tkt " +
                    "RETURNING id_usr";

                var filas = RunQuery(usuarioQuery, parameters);
                if (filas.Count == 0)
                {
                    return Json(new { success = false, message = "No se pudo actualizar el rol." });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Equipo", "No se pudo editar al integrante");
            }
        }

        /// <summary>
        /// Saca al usuario del equipo de soporte devolviendolo a rol Usuario (3).
        ///
        /// NO borra la cuenta del ERP ni la fila de tkt_usuario_rol. Borrar la fila lo
        /// haria desaparecer de esta pantalla, que es la unica que administra el equipo y
        /// no tiene alta: quedaria fuera sin forma de volver a entrar. Asi la baja se
        /// revierte desde el mismo boton "Editar".
        ///
        /// Antes este metodo era un TODO vacio que respondia success = true, de modo que
        /// la pantalla confirmaba "Usuario eliminado correctamente" sin haber hecho nada.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [SoporteStaffAuthorize]
        public JsonResult Eliminar(UsuarioDto dto)
        {
            if (dto == null || dto.Id <= 0)
            {
                return Json(new { success = false, message = "Usuario invalido." });
            }

            try
            {
                var filas = RunQuery(
                    "UPDATE tkt_usuario_rol SET id_rol_tkt = @rol_usuario " +
                    "WHERE id_usr = @id_usr AND id_rol_tkt <> @rol_usuario " +
                    "RETURNING id_usr",
                    new Dictionary<string, object>
                    {
                        { "id_usr", dto.Id },
                        { "rol_usuario", RolTicketsUsuarioFinal }
                    });

                if (filas.Count == 0)
                {
                    return Json(new { success = false, message = "El usuario no forma parte del equipo de soporte." });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return ErrorConTicket(ex, "Soporte/Equipo", "No se pudo eliminar al integrante");
            }
        }




    }
}