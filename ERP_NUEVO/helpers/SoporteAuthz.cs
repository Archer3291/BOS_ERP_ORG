using BOS_ERP.Controllers;
using BOS_ERP.Filters;
using Microsoft.AspNetCore.Http;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Datos mínimos de un ticket necesarios para decidir permisos.
    /// </summary>
    public class TicketPermisos
    {
        /// <summary>False cuando el folio/id no existe: el llamador debe devolver 404.</summary>
        public bool Existe { get; set; }

        public int IdTkt { get; set; }
        public int IdCreador { get; set; }
        public int IdAsignado { get; set; }
        public int IdEstado { get; set; }
        public string Folio { get; set; } = "";
    }

    /// <summary>
    /// Autorización del módulo de Soporte.
    ///
    /// El módulo tiene DOS catálogos de roles que conviven y ninguno de los dos alcanza
    /// por sí solo:
    ///
    ///  - Los roles del ERP (tabla <c>roles</c>, que es lo que el login deja en
    ///    <c>Session["Rol"]</c>): incluyen "Resolvedor Tickets" y "Asignador Tickets".
    ///    Es el mecanismo que usa <see cref="AuthorizeRoleAttribute"/>, con cache.
    ///  - El catálogo propio del módulo (<c>tkt_usuario_rol</c> / <c>tkt_roles</c>):
    ///    1 = Revisor, 2 = Asignador, 3 = Usuario.
    ///
    /// Se aceptan ambos: hoy no hay ni un solo usuario con Revisor o Asignador en
    /// tkt_usuario_rol (los 8 registros existentes son rol 3 = Usuario), así que exigir
    /// únicamente el catálogo propio dejaría el módulo sin nadie que pudiera atenderlo.
    ///
    /// Modelo de visibilidad: un usuario ve los tickets que creó o que tiene asignados;
    /// el staff de soporte ve todos.
    /// </summary>
    public static class SoporteAuthz
    {
        /// <summary>Roles del ERP que dan acceso de staff al módulo de soporte.</summary>
        public static readonly string[] RolesStaff =
        {
            "Sistemas",
            "Super Administrador",
            "Resolvedor Tickets",
            "Asignador Tickets"
        };

        /// <summary>
        /// Roles de <c>tkt_roles</c> que cuentan como staff: 1 = Revisor, 2 = Asignador.
        /// El 3 (Usuario) es el usuario final y no da ningún permiso extra.
        /// </summary>
        private static readonly int[] RolesTktStaff = { 1, 2 };

        /// <summary>
        /// Id del usuario autenticado. El login lo deja en sesión, así que no hace falta
        /// volver a resolverlo por NombreUsuario en cada acción (era una consulta por
        /// request repetida en ocho lugares del módulo).
        /// </summary>
        public static int? UsuarioActualId(ISession session) => session?.GetInt32("UsuarioId");

        /// <summary>
        /// Permisos (tabla permisos / permisos_usuario) que dan acceso al módulo.
        /// Es el tercer mecanismo de autorización que convive en el ERP, junto al rol
        /// del ERP y al catálogo propio de tickets, y es el que usa el menú lateral.
        /// </summary>
        public static readonly string[] PermisosStaff = { "super_admin", "sistemas" };

        /// <summary>
        /// True si el usuario puede atender tickets.
        ///
        /// Se aceptan los TRES mecanismos que conviven en el ERP porque cada capa venía
        /// mirando uno distinto y eso dejaba el menú y el controlador en desacuerdo: el
        /// enlace de Administración aparecía y al entrar respondía "acceso no autorizado".
        ///
        ///   1. Permiso 'sistemas' o 'super_admin' (permisos_usuario)
        ///   2. Rol del ERP (usuarios.rolid -> roles.nombre)
        ///   3. Rol propio del módulo (tkt_usuario_rol: 1 Revisor, 2 Asignador)
        /// </summary>
        public static bool EsStaff(ISession session, int usuarioId)
        {
            // Primero el rol del ERP: se resuelve contra la sesión y un cache en memoria,
            // sin tocar la base. Los otros dos sí consultan, así que van después.
            if (AuthorizeRoleAttribute.TieneRol(session, RolesStaff))
                return true;

            if (TienePermisoDeSoporte(usuarioId))
                return true;

            return TieneRolDeSoporte(usuarioId);
        }

        /// <summary>Consulta el esquema de permisos, el mismo que usa el menú.</summary>
        public static bool TienePermisoDeSoporte(int usuarioId)
        {
            if (usuarioId <= 0) return false;

            try
            {
                var utils = new Utilities(true);
                var filas = utils.RunQuery(
                    "SELECT 1 FROM permisos_usuario pu " +
                    "INNER JOIN permisos p ON p.id_permiso = pu.permiso_id " +
                    "WHERE pu.usuario_id = @id_usr AND p.nombre = ANY(@permisos) LIMIT 1",
                    new Dictionary<string, object>
                    {
                        { "id_usr", usuarioId },
                        { "permisos", PermisosStaff }
                    });

                return filas.Count > 0;
            }
            catch
            {
                // Igual que en TieneRolDeSoporte: ante un fallo de catálogo se niega
                // el acceso en vez de abrirlo.
                return false;
            }
        }

        /// <summary>Consulta el catálogo propio del módulo (tkt_usuario_rol).</summary>
        public static bool TieneRolDeSoporte(int usuarioId)
        {
            if (usuarioId <= 0) return false;

            try
            {
                var utils = new Utilities(true);
                var filas = utils.RunQuery(
                    "SELECT 1 FROM tkt_usuario_rol WHERE id_usr = @id_usr AND id_rol_tkt = ANY(@roles) LIMIT 1",
                    new Dictionary<string, object>
                    {
                        { "id_usr", usuarioId },
                        { "roles", RolesTktStaff }
                    });

                return filas.Count > 0;
            }
            catch
            {
                // Ante un fallo de catálogo se niega el acceso en vez de abrirlo, igual
                // que hace AuthorizeRoleAttribute cuando no puede leer los roles.
                return false;
            }
        }

        /// <summary>
        /// Carga los datos de permisos de un ticket por su id interno.
        /// Devuelve <c>Existe = false</c> si no hay tal ticket.
        /// </summary>
        public static TicketPermisos CargarPorId(int idTkt)
            => Cargar("t.id_tkts = @clave", idTkt);

        /// <summary>Igual que <see cref="CargarPorId"/> pero por folio público.</summary>
        public static TicketPermisos CargarPorFolio(string folio)
            => Cargar("t.folio_tkt = @clave", folio);

        private static TicketPermisos Cargar(string filtro, object clave)
        {
            if (clave == null || (clave is string s && string.IsNullOrWhiteSpace(s)))
                return new TicketPermisos { Existe = false };

            var utils = new Utilities(true);
            var filas = utils.RunQuery(
                $@"SELECT t.id_tkts, t.id_usr, t.id_usr_asig, t.id_stat_tkt, t.folio_tkt
                   FROM tkts t
                   WHERE {filtro}
                   LIMIT 1",
                new Dictionary<string, object> { { "clave", clave } });

            if (filas.Count == 0)
                return new TicketPermisos { Existe = false };

            var f = filas[0];
            return new TicketPermisos
            {
                Existe = true,
                IdTkt = Convert.ToInt32(f["id_tkts"]),
                IdCreador = f["id_usr"] == null ? 0 : Convert.ToInt32(f["id_usr"]),
                IdAsignado = f["id_usr_asig"] == null ? 0 : Convert.ToInt32(f["id_usr_asig"]),
                IdEstado = f["id_stat_tkt"] == null ? 0 : Convert.ToInt32(f["id_stat_tkt"]),
                Folio = f["folio_tkt"]?.ToString() ?? ""
            };
        }

        /// <summary>Leer el ticket y su hilo: creador, asignado o staff.</summary>
        public static bool PuedeVer(ISession session, int usuarioId, TicketPermisos tkt)
        {
            if (tkt == null || !tkt.Existe) return false;

            return tkt.IdCreador == usuarioId
                || tkt.IdAsignado == usuarioId
                || EsStaff(session, usuarioId);
        }

        /// <summary>
        /// Mover el ticket (estado, prioridad, categoría): staff o el responsable asignado.
        /// El creador puede abrir y responder, pero no reclasificar su propio ticket.
        /// </summary>
        public static bool PuedeGestionar(ISession session, int usuarioId, TicketPermisos tkt)
        {
            if (tkt == null || !tkt.Existe) return false;

            return tkt.IdAsignado == usuarioId
                || EsStaff(session, usuarioId);
        }

        /// <summary>Editar asunto y descripción: sólo el creador o el staff.</summary>
        public static bool PuedeEditarTicket(ISession session, int usuarioId, TicketPermisos tkt)
        {
            if (tkt == null || !tkt.Existe) return false;

            return tkt.IdCreador == usuarioId
                || EsStaff(session, usuarioId);
        }

        /// <summary>
        /// Reasignar el responsable es una acción de staff: el asignado actual no puede
        /// pasarle el ticket a otro por su cuenta.
        /// </summary>
        public static bool PuedeAsignar(ISession session, int usuarioId)
            => EsStaff(session, usuarioId);
    }
}
