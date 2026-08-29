using BOS_ERP.Controllers;
using BOS_ERP.Filters;
using Microsoft.AspNetCore.Http;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Qué categorías alcanza a ver una persona del staff de soporte.
    ///
    /// SoporteAuthz responde "¿es staff?" -la puerta de entrada al módulo-. Este
    /// helper responde la pregunta que viene después: ya dentro, ¿qué parte del
    /// tablero le toca? Son dos filtros distintos y encadenados; ninguno sustituye
    /// al otro. Todo lo que use SoporteAlcance ya pasó por [SoporteStaffAuthorize].
    ///
    /// EL MODELO
    ///   * Un asignador por categoría: tkts_categorias.responsable.
    ///   * N resolvedores por categoría: tkt_categoria_resolvedor.
    ///   * Perteneces a una categoría si eres cualquiera de los dos.
    ///   * Ves los tickets de las categorías a las que perteneces, y sólo puedes
    ///     asignárselos a gente de esa misma categoría.
    ///   * Sistemas ve todo.
    /// </summary>
    public static class SoporteAlcance
    {
        /// <summary>areas: areaid = 1 es Sistemas. Mismo valor que usa ErrorTicketService.</summary>
        private const int AreaSistemas = 1;

        /// <summary>El rol del ERP que abre el alcance completo.</summary>
        private const string RolSistemas = "Sistemas";

        /// <summary>
        /// Permisos que abren el alcance completo. Son los mismos que ya usa el menú
        /// lateral, así que quien ve el enlace de Administración no se encuentra la
        /// pantalla recortada sin explicación.
        /// </summary>
        private static readonly string[] PermisosGlobales = { "sistemas", "super_admin" };

        /// <summary>El rol del ERP que administra un área del módulo.</summary>
        private const string RolGerente = "Gerente";

        /// <summary>
        /// Lo que una persona alcanza a ver, resuelto una sola vez por petición.
        /// </summary>
        public sealed class Alcance
        {
            /// <summary>Sin límite de categorías: Sistemas.</summary>
            public bool VeTodo { get; init; }

            /// <summary>Rol del ERP 'Gerente': administra las categorías que dirige.</summary>
            public bool EsGerente { get; init; }

            /// <summary>
            /// Categorías que alcanza a ver, ya resueltas SEGÚN SU PAPEL. Vacío si
            /// <see cref="VeTodo"/>.
            ///
            ///   * Gerente    -> sólo donde es el responsable (asignador) de la categoría.
            ///   * Resolvedor -> las que atiende, vía tkt_categoria_resolvedor.
            ///
            /// La distinción importa porque la siembra inicial metió a todo el staff
            /// como resolvedor de todas las categorías: sin ella, un gerente seguiría
            /// viéndolo todo por la puerta de atrás.
            /// </summary>
            public List<int> Categorias { get; init; } = new();

            /// <summary>
            /// True cuando la persona no es Sistemas y además no pertenece a ninguna
            /// categoría: no debe ver ni una fila. Es un caso real -alguien con rol de
            /// staff a quien todavía no han metido a ninguna categoría- y hay que
            /// distinguirlo de "ve todo", porque un filtro vacío significaría lo contrario.
            /// </summary>
            public bool SinCategorias => !VeTodo && Categorias.Count == 0;

            /// <summary>
            /// Quién puede entrar a las pestañas de Categorías, Equipo e Informes.
            /// El resolvedor se queda sólo con la de Tickets: administrar el catálogo,
            /// el equipo y las cifras del área es trabajo de quien la dirige.
            /// </summary>
            public bool PuedeAdministrar => VeTodo || EsGerente;
        }

        /// <summary>
        /// Resuelve el alcance del usuario. Como mucho tres consultas, y las de abajo
        /// se saltan en cuanto una de arriba resuelve el caso.
        /// </summary>
        public static Alcance De(ISession session, int usuarioId)
        {
            if (usuarioId <= 0)
                return new Alcance { VeTodo = false };

            if (VeTodo(session, usuarioId))
                return new Alcance { VeTodo = true };

            bool gerente = EsGerente(session, usuarioId);

            return new Alcance
            {
                VeTodo = false,
                EsGerente = gerente,
                // El gerente ve su área; el resolvedor, lo que atiende.
                Categorias = CategoriasDe(usuarioId, soloDirigidas: gerente)
            };
        }

        /// <summary>
        /// ¿Tiene el rol del ERP 'Gerente'?
        ///
        /// Se resuelve primero contra la sesión y el cache en memoria de
        /// AuthorizeRoleAttribute, que es como lo consulta el resto del ERP. La
        /// consulta a la base es sólo el respaldo por si el rol no viajó en sesión.
        /// </summary>
        public static bool EsGerente(ISession session, int usuarioId)
        {
            if (usuarioId <= 0) return false;

            if (AuthorizeRoleAttribute.TieneRol(session, new[] { RolGerente }))
                return true;

            try
            {
                var utils = new Utilities(true);
                var r = utils.RunScalar(
                    "SELECT COUNT(*) FROM usuarios u " +
                    "JOIN roles r ON r.rolid = u.rolid " +
                    "WHERE u.usuarioid = @id AND r.nombre = @rol",
                    new Dictionary<string, object>
                    {
                        { "id", usuarioId },
                        { "rol", RolGerente }
                    });

                return r != null && Convert.ToInt32(r) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ¿Es el asignador -el responsable- de esta categoría?
        ///
        /// Distinto de pertenecer a ella: un resolvedor atiende la categoría pero no
        /// la dirige, y no puede repartir su trabajo ni el de los demás.
        /// </summary>
        public static bool EsAsignadorDe(int usuarioId, int idCategoria)
        {
            if (usuarioId <= 0 || idCategoria <= 0) return false;

            try
            {
                var utils = new Utilities(true);
                var r = utils.RunScalar(
                    "SELECT COUNT(*) FROM tkts_categorias " +
                    "WHERE id_cat = @cat AND responsable = @usr",
                    new Dictionary<string, object>
                    {
                        { "cat", idCategoria },
                        { "usr", usuarioId }
                    });

                return r != null && Convert.ToInt32(r) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ¿Ve todas las categorías? Tres caminos, cualquiera basta:
        /// rol del ERP 'Sistemas', área Sistemas, o permiso 'sistemas'/'super_admin'.
        ///
        /// El rol 'Super Administrador' NO entra a propósito: cuenta como staff en
        /// SoporteAuthz pero no abre el alcance. Quien lo tenga y necesite ver todo
        /// debe traer además el permiso 'super_admin'.
        /// </summary>
        public static bool VeTodo(ISession session, int usuarioId)
        {
            if (usuarioId <= 0) return false;

            // El rol del ERP se resuelve contra la sesión y un cache en memoria, sin
            // tocar la base. Se pregunta primero por eso.
            if (AuthorizeRoleAttribute.TieneRol(session, new[] { RolSistemas }))
                return true;

            try
            {
                var utils = new Utilities(true);
                var f = utils.RunQuery(
                    "SELECT " +
                    "  EXISTS (SELECT 1 FROM usuarios u " +
                    "           WHERE u.usuarioid = @id AND u.areaid = @area) AS area, " +
                    "  EXISTS (SELECT 1 FROM usuarios u " +
                    "            JOIN roles r ON r.rolid = u.rolid " +
                    "           WHERE u.usuarioid = @id AND r.nombre = @rol) AS rol, " +
                    "  EXISTS (SELECT 1 FROM permisos_usuario pu " +
                    "            JOIN permisos p ON p.id_permiso = pu.permiso_id " +
                    "           WHERE pu.usuario_id = @id AND p.nombre = ANY(@permisos)) AS permiso",
                    new Dictionary<string, object>
                    {
                        { "id", usuarioId },
                        { "area", AreaSistemas },
                        { "rol", RolSistemas },
                        { "permisos", PermisosGlobales }
                    });

                if (f.Count == 0) return false;

                return Bandera(f[0], "area") || Bandera(f[0], "rol") || Bandera(f[0], "permiso");
            }
            catch
            {
                // Un fallo de catálogo no puede regalar el alcance completo: se
                // devuelve el más restringido y la persona verá sólo sus categorías.
                return false;
            }
        }

        /// <summary>
        /// Categorías activas que alcanza la persona.
        /// </summary>
        /// <param name="soloDirigidas">
        /// true para quedarse únicamente con las que dirige como responsable. Es lo que
        /// se usa con el gerente: la siembra inicial metió a todo el staff como
        /// resolvedor de todas las categorías, así que sin este recorte un gerente
        /// seguiría alcanzándolas todas por ser resolvedor nominal de ellas.
        /// </param>
        public static List<int> CategoriasDe(int usuarioId, bool soloDirigidas = false)
        {
            var ids = new List<int>();
            if (usuarioId <= 0) return ids;

            try
            {
                var utils = new Utilities(true);

                string pertenencia = soloDirigidas
                    ? "c.responsable = @id"
                    : "(c.responsable = @id " +
                      " OR EXISTS (SELECT 1 FROM tkt_categoria_resolvedor cr " +
                      "             WHERE cr.id_cat = c.id_cat AND cr.id_usr = @id))";

                var filas = utils.RunQuery(
                    "SELECT c.id_cat FROM tkts_categorias c " +
                    "WHERE c.activo = true AND " + pertenencia,
                    new Dictionary<string, object> { { "id", usuarioId } });

                foreach (var f in filas)
                    if (f["id_cat"] != null) ids.Add(Convert.ToInt32(f["id_cat"]));
            }
            catch
            {
                // Ver la nota de TablaLista: si el script todavía no se ha corrido, la
                // consulta revienta con 42P01 y aquí se devuelve vacío. Los llamadores
                // consultan TablaLista antes de acotar, así que ese caso no llega.
            }

            return ids;
        }

        /// <summary>
        /// ¿Existe ya tkt_categoria_resolvedor?
        ///
        /// Mismo motivo que Utilities.ExisteColumna: el código nuevo tiene que convivir
        /// con una base donde su script aún no se ha corrido. Aquí la consecuencia de
        /// no comprobarlo sería severa -Administración entera respondiendo 42P01-, así
        /// que mientras la tabla no exista NO se acota nada y el módulo se comporta
        /// exactamente como antes.
        ///
        /// Ojo con la dirección del respaldo: se falla ABRIENDO, no cerrando. Es
        /// deliberado y es seguro aquí porque quien llega a este punto ya pasó por
        /// [SoporteStaffAuthorize]; lo que está en juego es cuánto ve un miembro del
        /// staff, no si un extraño entra. Fallar cerrando dejaría el módulo sin nadie
        /// que pudiera atenderlo, que es peor y además se ve como una caída.
        /// </summary>
        private static bool? _tablaLista;

        public static bool TablaLista()
        {
            if (_tablaLista.HasValue) return _tablaLista.Value;

            try
            {
                var utils = new Utilities(true);
                var r = utils.RunScalar(
                    "SELECT COUNT(*) FROM information_schema.tables " +
                    "WHERE table_name = 'tkt_categoria_resolvedor'",
                    new Dictionary<string, object>());

                _tablaLista = r != null && Convert.ToInt32(r) > 0;
            }
            catch
            {
                _tablaLista = false;
            }

            return _tablaLista.Value;
        }

        /// <summary>
        /// Fragmento SQL que acota una consulta a las categorías del usuario, o null
        /// si no hay que acotar nada (Sistemas, o tabla todavía sin crear).
        ///
        /// Los ids se pasan como parámetro array y NO se interpolan: aunque vengan de
        /// la base y no del cliente, concatenarlos abriría la puerta a que mañana
        /// alguien los alimente desde un formulario.
        /// </summary>
        /// <param name="columna">La columna id_cat de la consulta, ya calificada. Ej: "t.id_cat".</param>
        /// <param name="parametros">Diccionario de la consulta; se le agrega el array.</param>
        /// <returns>El fragmento para el WHERE, o null si la consulta no cambia.</returns>
        public static string Filtro(ISession session, int usuarioId, string columna,
                                    Dictionary<string, object> parametros)
        {
            if (!TablaLista()) return null;

            var alcance = De(session, usuarioId);
            if (alcance.VeTodo) return null;

            // Sin categorías no es "sin filtro": es "ninguna fila". Un WHERE ... = ANY
            // sobre un array vacío devuelve 0 filas, que es exactamente lo correcto.
            parametros["alcance_cats"] = alcance.Categorias.ToArray();
            return $"{columna} = ANY(@alcance_cats)";
        }

        /// <summary>
        /// Como <see cref="Filtro"/>, pero para consultas cuya fila es una PERSONA y no
        /// un ticket: deja pasar sólo a quien comparte alguna categoría con el usuario.
        ///
        /// Es lo que hace que la pestaña Equipo muestre "mi equipo" y no la plantilla
        /// entera. La condición mira las dos formas de pertenecer -ser el responsable
        /// de la categoría o estar en tkt_categoria_resolvedor-, igual que
        /// <see cref="CategoriasDe"/>.
        /// </summary>
        /// <param name="columnaUsuario">
        /// La columna con el id del usuario, ya calificada. Ej: "u.usuarioid". Viene del
        /// código, nunca del cliente: se interpola porque un nombre de columna no puede
        /// viajar como parámetro, igual que la lista blanca de ORDER BY.
        /// </param>
        public static string FiltroMiembros(ISession session, int usuarioId, string columnaUsuario,
                                            Dictionary<string, object> parametros)
        {
            if (!TablaLista()) return null;

            var alcance = De(session, usuarioId);
            if (alcance.VeTodo) return null;

            parametros["alcance_cats"] = alcance.Categorias.ToArray();

            return "EXISTS (SELECT 1 FROM tkts_categorias c_alc " +
                   "         WHERE c_alc.id_cat = ANY(@alcance_cats) " +
                   $"          AND (c_alc.responsable = {columnaUsuario} " +
                   "               OR EXISTS (SELECT 1 FROM tkt_categoria_resolvedor cr_alc " +
                   $"                          WHERE cr_alc.id_cat = c_alc.id_cat " +
                   $"                            AND cr_alc.id_usr = {columnaUsuario})))";
        }

        /// <summary>
        /// ¿Puede esta persona repartir el trabajo de esta categoría?
        ///
        /// Sólo el asignador de esa categoría, o Sistemas. Ni ser staff ni siquiera
        /// atenderla alcanza: un resolvedor trabaja los tickets de su categoría pero no
        /// decide de quién son, ni suyos ni de sus compañeros.
        ///
        /// Es también la puerta de editar y borrar la categoría misma.
        ///
        /// NO se apoya en TablaLista: mira tkts_categorias.responsable, que existe
        /// desde siempre, así que esta regla aplica aunque el script de resolvedores
        /// todavía no se haya corrido.
        /// </summary>
        public static bool PuedeAsignarEn(ISession session, int usuarioId, int idCategoria)
        {
            if (idCategoria <= 0) return false;

            return VeTodo(session, usuarioId) || EsAsignadorDe(usuarioId, idCategoria);
        }

        /// <summary>
        /// ¿Pertenece el destinatario a la categoría del ticket?
        ///
        /// Es la mitad que faltaba: sin esto un asignador podía mandarle un ticket de
        /// su categoría a cualquiera que tuviera el rol, incluida gente que no atiende
        /// esa categoría y que se lo iba a encontrar sin contexto.
        /// </summary>
        public static bool AtiendeCategoria(int idUsuario, int idCategoria)
        {
            if (!TablaLista()) return true;
            if (idUsuario <= 0 || idCategoria <= 0) return false;

            try
            {
                var utils = new Utilities(true);
                var r = utils.RunScalar(
                    "SELECT COUNT(*) FROM tkts_categorias c " +
                    "WHERE c.id_cat = @cat " +
                    "  AND (c.responsable = @usr " +
                    "       OR EXISTS (SELECT 1 FROM tkt_categoria_resolvedor cr " +
                    "                   WHERE cr.id_cat = c.id_cat AND cr.id_usr = @usr))",
                    new Dictionary<string, object>
                    {
                        { "cat", idCategoria },
                        { "usr", idUsuario }
                    });

                return r != null && Convert.ToInt32(r) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Categoría a la que pertenece un ticket. 0 si no existe.</summary>
        public static int CategoriaDelTicket(int idTkt)
        {
            if (idTkt <= 0) return 0;

            try
            {
                var utils = new Utilities(true);
                var r = utils.RunScalar(
                    "SELECT id_cat FROM tkts WHERE id_tkts = @id",
                    new Dictionary<string, object> { { "id", idTkt } });

                return r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// PostgreSQL devuelve los EXISTS como bool, pero el mapeo genérico de RunQuery
        /// entrega object: se normaliza aquí en vez de repetir el Convert en cada uso.
        /// </summary>
        private static bool Bandera(Dictionary<string, object> fila, string clave)
        {
            if (!fila.TryGetValue(clave, out var v) || v == null || v == DBNull.Value)
                return false;

            return Convert.ToBoolean(v);
        }
    }
}
