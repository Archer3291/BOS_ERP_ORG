using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;

namespace BOS_ERP.Controllers
{
    public class SoporteController : Utilities
    {
        /// <summary>
        /// Carpeta de adjuntos, relativa al ContentRoot. Vive fuera de wwwroot a
        /// propósito: los adjuntos de un ticket llevan información interna y no deben
        /// servirse como archivos estáticos con URL adivinable.
        /// Debe coincidir con la que usa EnviarTicketsController al guardarlos.
        /// </summary>
        public const string CarpetaAdjuntos = "Adjuntos";

        private readonly IWebHostEnvironment _env;

        public SoporteController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [Authorize]
        public IActionResult EnviarTicket()
        {
            string categoriasQuery = "SELECT id_cat, n_cat, icon_class FROM tkts_categorias WHERE activo = true";
            var resultCategorias = RunQuery(categoriasQuery);
            ViewBag.Categorias = resultCategorias;
            return View(resultCategorias);
        }
        [Authorize]
        public IActionResult VerTicket()
        {
            // Los catalogos de los filtros se sirven desde aqui y no por /Soporte/DatosSelect:
            // esa accion esta reservada al staff y esta pantalla la usa cualquier usuario.
            ViewBag.Estados = RunQuery("SELECT id_stat_tkt, n FROM stat_tkt ORDER BY id_stat_tkt");
            ViewBag.Prioridades = RunQuery("SELECT id_prio, n, color FROM prio ORDER BY id_prio");

            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;

            // Resumen de la cabecera. Los ids de estado son los mismos que usa el resto
            // del modulo: 1 = Abierto, 4 = Resuelto.
            var resumen = RunQuery(
                "SELECT COUNT(*) AS total, " +
                "       COUNT(*) FILTER (WHERE id_stat_tkt = 1) AS abiertos, " +
                "       COUNT(*) FILTER (WHERE id_stat_tkt = 4) AS resueltos " +
                "FROM tkts WHERE id_usr = @id",
                new Dictionary<string, object> { { "id", usuarioId } });

            ViewBag.Resumen = resumen.Count > 0 ? resumen[0] : null;

            return View();
        }

        [Authorize]
        public IActionResult FormularioTicket(int categoria = 0)
        {
            // El select de prioridad estaba escrito a mano en la vista como
            // 1=Bajo, 2=Medio, 3=Alto, pero el catalogo prio es 1=Alta, 2=Media, 3=Baja:
            // cada ticket se guardaba con la prioridad invertida. Se sirve el catalogo.
            ViewBag.Prioridades = RunQuery("SELECT id_prio, n, color FROM prio ORDER BY id_prio");

            // Nombre y correo son de solo lectura: el ticket se crea siempre a nombre del
            // usuario de la sesion, asi que dejarlos editables solo invitaba a escribir
            // datos que el servidor ignoraba.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            var datos = RunQuery(
                "SELECT nombre || ' ' || apellido AS nombre_completo, email " +
                "FROM usuarios WHERE usuarioid = @id",
                new Dictionary<string, object> { { "id", usuarioId } });

            ViewBag.NombreUsuario = datos.Count > 0 ? datos[0]["nombre_completo"]?.ToString() : "";
            ViewBag.EmailUsuario = datos.Count > 0 ? datos[0]["email"]?.ToString() : "";

            // La categoria llega por query string desde las tarjetas de EnviarTicket.
            ViewBag.CategoriaSeleccionada = categoria;

            // Nombre e icono de esa categoria, para que el formulario muestre a que area
            // se esta escribiendo. Si el id no existe (o viene en 0) el ViewBag queda
            // vacio y la vista avisa que hay que elegir categoria.
            ViewBag.CategoriaNombre = "";
            ViewBag.CategoriaIcono = "";

            if (categoria > 0)
            {
                var cat = RunQuery(
                    "SELECT n_cat, icon_class FROM tkts_categorias WHERE id_cat = @id AND activo = true",
                    new Dictionary<string, object> { { "id", categoria } });

                if (cat.Count > 0)
                {
                    ViewBag.CategoriaNombre = cat[0]["n_cat"]?.ToString();
                    ViewBag.CategoriaIcono = cat[0]["icon_class"]?.ToString();
                }
            }

            return View();
        }

        [Authorize]
        public IActionResult Ticket(string id)
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction("EnviarTicket");
            var parameters = new Dictionary<string, object>
            {
                { "folio_tkt", id }
            };

            string ticketQuery = @"
                                 SELECT 
                                     t.id_tkts,
                                     COALESCE(CONCAT(u3.nombre, ' ', u3.apellido), CONCAT(u1.nombre, ' ', u1.apellido)) AS nombreusuarioseguimiento,
                                     CONCAT(u1.nombre, ' ', u1.apellido) AS nombrecompleto,
                                     t.tit,
                                     t.descr,
                                     t.id_stat_tkt,
                                     st.n AS estado,
                                     t.id_prio,
                                     p.n AS prioridad,
                                     -- Color del catalogo prio e icono de la categoria:
                                     -- la vista los usa para pintar el ticket con la misma
                                     -- identidad que las tarjetas del centro de soporte.
                                     p.color,
                                     c.icon_class,
                                     t.id_usr,
                                     u1.nombre AS usuariocreador,
                                     t.id_area,
                                     a.nombre AS nombrearea,
                                     t.id_usr_asig,
                                     u2.nombre AS usuarioasignado,
                                     CONCAT(u2.nombre, ' ', u2.apellido) AS asignadocompleto,
                                     t.fch_crea,
                                     t.fch_crr,
                                     t.id_cat,
                                     c.n_cat AS categoria,
                                     t.folio_tkt,
                                     -- Folio del documento del ERP que respalda el ticket.
                                     COALESCE(em.folio || CASE WHEN em.variacion > 0
                                              THEN '-' || num_to_letters(em.variacion) ELSE '' END, '') AS folio_doc
                                 FROM tkts t
                                 INNER JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt
                                 INNER JOIN prio p ON p.id_prio = t.id_prio
                                 INNER JOIN usuarios u1 ON u1.usuarioid = t.id_usr
                                 INNER JOIN usuarios u2 ON u2.usuarioid = t.id_usr_asig
                                 INNER JOIN tkts_categorias c ON c.id_cat = t.id_cat
                                 INNER JOIN areas a ON a.areaid = t.id_area
                                 -- LATERAL con LIMIT 1 en vez de un LEFT JOIN directo a
                                 -- seg_tkts: ese join devolvia una fila por seguimiento,
                                 -- se tomaba result[0] sin ORDER BY y la ultima respuesta
                                 -- acababa siendo un seguimiento cualquiera.
                                 LEFT JOIN LATERAL (
                                     SELECT s.id_usr
                                     FROM seg_tkts s
                                     WHERE s.id_tkt = t.id_tkts
                                     ORDER BY s.fch DESC
                                     LIMIT 1
                                 ) segt ON TRUE
                                 LEFT JOIN usuarios u3 ON u3.usuarioid = segt.id_usr
                                 LEFT JOIN encabezadomov em ON em.id_encabezado = t.id_encabezado
                                 WHERE t.folio_tkt = @folio_tkt";


            // ORDER BY segt.fch: sin el, el orden del hilo lo decidia el motor y las
            // respuestas podian salir intercaladas sin criterio. id_usr se usa para
            // distinguir en la vista los mensajes del propio usuario.
            string respuestas = "SELECT " +
                            "segt.id_seg_tkts, " +
                            "segt.id_usr, " +
                            "u.Nombre || ' ' || u.Apellido AS NombreCompleto, " +
                            "segt.coment, " +
                            "segt.fch " +
                            "FROM seg_tkts segt " +
                            "INNER JOIN Usuarios u ON u.UsuarioId = segt.id_usr " +
                            "WHERE segt.id_tkt = @id_tkt " +
                            "ORDER BY segt.fch ASC, segt.id_seg_tkts ASC;";

            // id_seg_tkts IS NULL = adjunto del ticket original. Los que cuelgan de una
            // respuesta se traen aparte y se pintan dentro de su mensaje del hilo.
            string adjuntosQuery = "SELECT id_tkts_adj, n_arch_orig, n_arch_uid, ext, fechacarga " +
           "FROM tkts_adj WHERE id_tkts = @id_tkt AND id_seg_tkts IS NULL";

            string adjuntosRespuestaQuery = "SELECT id_tkts_adj, id_seg_tkts, n_arch_orig, ext, fechacarga " +
           "FROM tkts_adj WHERE id_tkts = @id_tkt AND id_seg_tkts IS NOT NULL ORDER BY id_tkts_adj";

            // El id del usuario ya viene en la sesión: no hace falta resolverlo por
            // NombreUsuario en cada petición.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (usuarioId <= 0)
                return RedirectToAction("Login", "Account");

            // La validación de existencia va ANTES de tocar el resultado: antes se hacía
            // ticketResult[0] siete líneas arriba de este if, así que un folio inexistente
            // reventaba con NullReference en vez de redirigir.
            var permisos = SoporteAuthz.CargarPorFolio(id);
            if (!permisos.Existe)
                return RedirectToAction("EnviarTicket");

            if (!SoporteAuthz.PuedeVer(HttpContext.Session, usuarioId, permisos))
                return View("~/Views/Shared/Unauthorized.cshtml");

            var ticketResult = RunQuery(ticketQuery, parameters);
            if (ticketResult == null || ticketResult.Count == 0)
                return RedirectToAction("EnviarTicket");

            parameters.Clear();
            parameters.Add("id_tkt", ticketResult[0]["id_tkts"]);
            var respestasResult = RunQuery(respuestas, parameters);
            var adjuntosResult = RunQuery(adjuntosQuery, parameters);
            var adjuntosRespuestaResult = RunQuery(adjuntosRespuestaQuery, parameters);

            result.Add("Ticket", new List<Dictionary<string, object>> { ticketResult[0] });
            result.Add("usuarioActivoResult", new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "usuarioid", usuarioId } }
            });
            result.Add("Respuestas", respestasResult);
            result.Add("Adjuntos", adjuntosResult);
            result.Add("AdjuntosRespuesta", adjuntosRespuestaResult);

            CargarDiagnosticoError(result, parameters, usuarioId, permisos);

            ViewBag.Result = result;
            return View();
        }

        /// <summary>
        /// Diagnóstico técnico del error, cuando el ticket lo generó la captura
        /// automática.
        ///
        /// SÓLO PARA STAFF, y no por comodidad: el panel enseña stack traces,
        /// nombres de tablas, constraints y parámetros de la petición. Desde que el
        /// creador de un ticket automático es el usuario al que le falló, dejarlo
        /// visible para el creador equivaldría a publicarle las tripas del ERP a
        /// cualquiera que provoque un error.
        ///
        /// Todo va en try/catch porque las tablas las crea sql/soporte_errores_auto.sql:
        /// en una base donde ese script no se ha corrido, el ticket debe abrirse igual.
        /// </summary>
        private void CargarDiagnosticoError(
            Dictionary<string, List<Dictionary<string, object>>> result,
            Dictionary<string, object> parameters,
            int usuarioId,
            TicketPermisos permisos)
        {
            if (!SoporteAuthz.PuedeGestionar(HttpContext.Session, usuarioId, permisos))
                return;

            try
            {
                var huella = RunQuery(
                    "SELECT h.id_huella, h.tipo_exc, h.mensaje_norm, h.origen, " +
                    "       h.ocurrencias, h.afectados, h.silenciada, " +
                    "       h.primera_vez, h.ultima_vez " +
                    "FROM tkt_error_huella h WHERE h.id_tkt = @id_tkt", parameters);

                if (huella.Count == 0) return;

                result.Add("Diagnostico", huella);

                // Las últimas ocurrencias, no todas: una huella con 5 000 golpes no
                // puede volver ilegible -ni lenta- la vista del ticket.
                var ocurrencias = RunQuery(
                    "SELECT d.fch, d.capa, d.ruta, d.metodo, d.trace_id, d.ip, " +
                    "       COALESCE(u.nombreusuario, '(sin sesion)') AS usuario, " +
                    "       d.payload::text AS payload " +
                    "FROM tkt_error_detalle d " +
                    "LEFT JOIN usuarios u ON u.usuarioid = d.id_usr " +
                    "WHERE d.id_huella = @id_huella " +
                    "ORDER BY d.fch DESC LIMIT 10",
                    new Dictionary<string, object> { { "id_huella", huella[0]["id_huella"] } });

                result.Add("DiagnosticoOcurrencias", ocurrencias);
            }
            catch
            {
                // Sin las tablas de captura, el ticket simplemente no muestra panel.
            }
        }

        /// <summary>
        /// Vista de gestión del ticket (cambiar estado, prioridad, categoría y responsable).
        /// A diferencia de <see cref="Ticket"/>, aquí sí se exige poder gestionar: el
        /// creador que sólo quiere leer su ticket tiene la otra vista.
        /// </summary>
        [Authorize]
        public IActionResult TicketT(string id)
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction("EnviarTicket");

            var parameters = new Dictionary<string, object>
            {
                { "folio_tkt", id }
            };

            string ticketQuery = @"
SELECT 
    t.id_tkts,
    COALESCE(CONCAT(u3.Nombre, ' ', u3.Apellido), CONCAT(u1.Nombre, ' ', u1.Apellido)) AS NombreUsuarioSeguimiento,
    CONCAT(u1.Nombre, ' ', u1.Apellido) AS NombreCompleto,
    t.tit,
    t.descr,
    t.id_stat_tkt,
    st.n AS Estado,
    t.id_prio,
    p.n AS Prioridad,
    -- Color del catalogo prio e icono de la categoria: la vista los usa para
    -- pintar el ticket con la misma identidad que el centro de soporte.
    p.color,
    c.icon_class,
    t.id_usr,
    u1.Nombre AS UsuarioCreador,
    t.id_area,
    a.Nombre AS NombreArea,
    t.id_usr_asig,
    u2.Nombre AS UsuarioAsignado,
    CONCAT(u2.Nombre, ' ', u2.Apellido) AS asignadocompleto,
    t.fch_crea,
    t.fch_crr,
    t.id_cat,
    c.n_cat AS Categoria,
    t.folio_tkt,
    -- Folio del documento del ERP que respalda el ticket.
    COALESCE(em.folio || CASE WHEN em.variacion > 0
             THEN '-' || num_to_letters(em.variacion) ELSE '' END, '') AS folio_doc
FROM tkts t
INNER JOIN stat_tkt st ON st.id_stat_tkt = t.id_stat_tkt
INNER JOIN prio p ON p.id_prio = t.id_prio
INNER JOIN usuarios u1 ON u1.usuarioid = t.id_usr
INNER JOIN usuarios u2 ON u2.usuarioid = t.id_usr_asig
INNER JOIN tkts_categorias c ON c.id_cat = t.id_cat
INNER JOIN areas a ON a.areaid = t.id_area
-- LATERAL con LIMIT 1: el LEFT JOIN directo a seg_tkts devolvia una fila por
-- seguimiento y se tomaba la primera sin ordenar.
LEFT JOIN LATERAL (
    SELECT s.id_usr
    FROM seg_tkts s
    WHERE s.id_tkt = t.id_tkts
    ORDER BY s.fch DESC
    LIMIT 1
) segt ON TRUE
LEFT JOIN usuarios u3 ON u3.usuarioid = segt.id_usr
LEFT JOIN encabezadomov em ON em.id_encabezado = t.id_encabezado
WHERE t.folio_tkt = @folio_tkt";



string historial = @"
SELECT  
    'asignacion' AS tipo_evento,
    ta.id_tkt,
    NULL AS id_stat_tkt_ant,
    NULL AS id_stat_tkt_nuev,
    NULL AS estado_nombre,
    ta.fch_asig AS fecha_evento,
    ta.id_usr,
    u1.nombreusuario AS usuario_nombre,
    NULL AS id_cat,
    NULL AS categoria_nombre,
    NULL AS id_prio,
    NULL AS prioridad_nombre,
    CONCAT('Ticket asignado al usuario ', u1.nombreusuario, ' por ', u2.nombreusuario) AS descripcion_evento
FROM tkt_asig ta
INNER JOIN usuarios u1 ON u1.usuarioid = ta.id_usr
INNER JOIN usuarios u2 ON u2.usuarioid = ta.asig_por
WHERE ta.id_tkt = @id_tkt

UNION ALL

SELECT  
    'estado' AS tipo_evento,
    he.id_tkts,
    he.id_stat_tkt_ant,
    he.id_stat_tkt_nuev,
    st.n AS estado_nombre,
    he.fch_cambio AS fecha_evento,
    he.id_usr,
    u.nombreusuario AS usuario_nombre,
    he.id_cat,
    c.n_cat AS categoria_nombre,
    he.id_prio,
    p.n AS prioridad_nombre,
    CONCAT(
        CASE WHEN he.id_stat_tkt_nuev IS NOT NULL THEN CONCAT('Cambio de estado a ', COALESCE(st.n, 'Desconocido')) ELSE '' END,
        CASE WHEN he.id_cat IS NOT NULL THEN CONCAT(' | Cambio de categor�a a ', COALESCE(c.n_cat, 'Desconocida')) ELSE '' END,
        CASE WHEN he.id_prio IS NOT NULL THEN CONCAT(' | Cambio de prioridad a ', COALESCE(p.n, 'Desconocida')) ELSE '' END,
        ' por ', u.nombreusuario
    ) AS descripcion_evento
FROM hst_est he
LEFT JOIN stat_tkt st ON st.id_stat_tkt = he.id_stat_tkt_nuev
LEFT JOIN tkts_categorias c ON c.id_cat = he.id_cat
LEFT JOIN prio p ON p.id_prio = he.id_prio
INNER JOIN usuarios u ON u.usuarioid = he.id_usr
WHERE he.id_tkts = @id_tkt

UNION ALL

SELECT
    'respuesta' AS tipo_evento,
    seg.id_tkt,
    NULL AS id_stat_tkt_ant,
    NULL AS id_stat_tkt_nuev,
    NULL AS estado_nombre,
    seg.fch AS fecha_evento,
    seg.id_usr,
    u.nombreusuario AS usuario_nombre,
    NULL AS id_cat,
    NULL AS categoria_nombre,
    NULL AS id_prio,
    NULL AS prioridad_nombre,
    CONCAT(u.nombreusuario, ' respondi� al ticket') AS descripcion_evento
FROM seg_tkts seg
INNER JOIN usuarios u ON u.usuarioid = seg.id_usr
WHERE seg.id_tkt = @id_tkt

ORDER BY fecha_evento ASC;";

            // �ltimos 5 tickets anteriores del usuario
            string anteriores = @"
SELECT id_tkts, tit, folio_tkt
FROM tkts
WHERE id_usr = @id_usr
ORDER BY fch_crea DESC
LIMIT 5";

            // Obtener respuestas con nombre completo del usuario
            string respuestas = @"
SELECT 
    segt.id_seg_tkts,
    segt.id_usr,
    u.nombre || ' ' || u.apellido AS nombrecompleto,
    segt.coment,
    segt.fch
FROM seg_tkts segt
INNER JOIN usuarios u ON u.usuarioid = segt.id_usr
WHERE segt.id_tkt = @id_tkt
ORDER BY segt.fch ASC, segt.id_seg_tkts ASC";

            // Consulta de estatus de ticket
            string estatus = @"
SELECT id_stat_tkt, n
FROM stat_tkt";

            // Archivos adjuntos del ticket
            // id_seg_tkts IS NULL = adjunto del ticket original; el resto cuelga de una
            // respuesta concreta del hilo.
            string adjuntosQuery = @"
SELECT id_tkts_adj, n_arch_orig, n_arch_uid, ext, fechacarga
FROM tkts_adj
WHERE id_tkts = @id_tkt AND id_seg_tkts IS NULL";

            string adjuntosRespuestaQuery = @"
SELECT id_tkts_adj, id_seg_tkts, n_arch_orig, ext, fechacarga
FROM tkts_adj
WHERE id_tkts = @id_tkt AND id_seg_tkts IS NOT NULL
ORDER BY id_tkts_adj";

            // Categor�as
            string categoriasQuery = @"
SELECT id_cat, n_cat
FROM tkts_categorias
WHERE activo = true
ORDER BY n_cat";

            // Prioridades
            string prioridadesQuery = @"
SELECT id_prio, n, color
FROM prio";

            // Quien puede quedarse este ticket: gente que atiende SU categoria, no
            // cualquiera con el rol. El combo ya sale acotado para que la pantalla no
            // ofrezca opciones que CambiarResponsable va a rechazar despues.
            //
            // Mientras no exista tkt_categoria_resolvedor se ofrece la lista completa,
            // que es el comportamiento anterior: asi el modulo sigue funcionando en una
            // base donde todavia no se ha corrido el script.
            string responsableQuery = @"
SELECT
    u.usuarioid AS id_usr,
    u.nombreusuario AS nombre
FROM
    usuarios u
INNER JOIN
    tkt_usuario_rol tur ON tur.id_usr = u.usuarioid
WHERE
    tur.id_rol_tkt IN (1, 2)";

            if (SoporteAlcance.TablaLista())
            {
                responsableQuery += @"
  AND EXISTS (
        SELECT 1
        FROM tkts t_r
        JOIN tkts_categorias c_r ON c_r.id_cat = t_r.id_cat
        WHERE t_r.folio_tkt = @folio_tkt
          AND (c_r.responsable = u.usuarioid
               OR EXISTS (SELECT 1 FROM tkt_categoria_resolvedor cr_r
                           WHERE cr_r.id_cat = c_r.id_cat
                             AND cr_r.id_usr = u.usuarioid)))";
            }

            responsableQuery += @"
ORDER BY
    u.nombreusuario DESC";


            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (usuarioId <= 0)
                return RedirectToAction("Login", "Account");

            var permisos = SoporteAuthz.CargarPorFolio(id);
            if (!permisos.Existe)
                return RedirectToAction("EnviarTicket");

            if (!SoporteAuthz.PuedeGestionar(HttpContext.Session, usuarioId, permisos))
                return View("~/Views/Shared/Unauthorized.cshtml");

            var ticketResult = RunQuery(ticketQuery, parameters);
            if (ticketResult == null || ticketResult.Count == 0)
                return RedirectToAction("EnviarTicket");

            parameters.Clear();
            parameters.Add("id_usr", ticketResult[0]["id_usr"]);
            var anterioresResult = RunQuery(anteriores, parameters);
            parameters.Clear();
            parameters.Add("id_tkt", ticketResult[0]["id_tkts"]);
            var respestasResult = RunQuery(respuestas, parameters);
            var estatusResult = RunQuery(estatus);
            var adjuntosResult = RunQuery(adjuntosQuery, parameters);
            var adjuntosRespuestaResult = RunQuery(adjuntosRespuestaQuery, parameters);
            var categoriasResult = RunQuery(categoriasQuery);
            var prioridadResult = RunQuery(prioridadesQuery);
            // Diccionario propio: en este punto "parameters" ya se limpio y trae id_tkt,
            // y la consulta de responsables se acota por folio.
            var responsableResult = RunQuery(responsableQuery,
                new Dictionary<string, object> { { "folio_tkt", id } });
            var historialResult = RunQuery(historial, parameters);

            result.Add("Ticket", new List<Dictionary<string, object>> { ticketResult[0] });
            result.Add("usuarioActivoResult", new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "usuarioid", usuarioId } }
            });
            result.Add("anterioresResult", anterioresResult);
            result.Add("Respuestas", respestasResult);
            result.Add("estatus", estatusResult);
            result.Add("Adjuntos", adjuntosResult);
            result.Add("AdjuntosRespuesta", adjuntosRespuestaResult);
            result.Add("Categorias", categoriasResult);
            result.Add("Prioridades", prioridadResult);
            result.Add("Responsable", responsableResult);
            result.Add("Historial", historialResult);

            ViewBag.Result = result;

            // Qué controles de gestión puede tocar quien está mirando. Se resuelve aquí
            // y no en la vista para que el Razor no tenga que consultar la base.
            //
            // Reasignar es del asignador de ESA categoría (o Sistemas): un resolvedor
            // trabaja su ticket pero no decide de quién es. Mover el ticket de
            // categoría es sólo de Sistemas, porque lo saca del alcance de un área y lo
            // mete en el de otra.
            //
            // Esconder el control NO es el permiso: lo ponen CambiarResponsable y
            // CambiarCategoriaTicket. Esto evita ofrecer un select que va a rebotar.
            int idCatTicket = ticketResult[0]["id_cat"] == null
                ? 0 : Convert.ToInt32(ticketResult[0]["id_cat"]);

            ViewBag.PuedeReasignar = SoporteAlcance.PuedeAsignarEn(
                HttpContext.Session, usuarioId, idCatTicket);

            ViewBag.PuedeCambiarCategoria = SoporteAlcance.VeTodo(HttpContext.Session, usuarioId);

            return View();
        }

        [Authorize]
        // Antes usaba [RoleAuthorize]. Ese atributo hereda de Attribute pero NO implementa
        // IAuthorizationFilter, así que ASP.NET Core nunca llegaba a ejecutarlo: la
        // pantalla quedaba abierta a cualquier usuario con sesión.
        //
        // Tampoco sirve [AuthorizeRole] a secas: sólo mira el rol del ERP, mientras que
        // el menú decide con DoesUserHasRight, que mira los permisos. Quien tenía el
        // permiso 'sistemas' pero un rol como Gerente o Usuario veía el enlace y recibía
        // "acceso no autorizado" al entrar. [SoporteStaffAuthorize] usa el criterio
        // completo de SoporteAuthz.EsStaff: permiso, rol del ERP o rol de tickets.
        [SoporteStaffAuthorize]
        public IActionResult AdministracionTicket()
        {
            return View();
        }

        [Authorize]
        [SoporteStaffAuthorize]
        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();

            // El combo de asignacion masiva ofrece solo gente de MIS categorias. La
            // seleccion de la tabla puede mezclar categorias, asi que este filtro es
            // una ayuda -no la garantia-: HistorialAsignacionMultiple valida ticket por
            // ticket que el destinatario atienda la categoria de cada uno.
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
            string queryUsuarioPropietario = "SELECT  " +
                "u.UsuarioId AS id_usr, " +
                "                u.NombreUsuario AS nombre  " +
                "                FROM " +
                "                Usuarios u  " +
                "                INNER JOIN  " +
                "                tkt_usuario_rol tur ON tur.id_usr = u.UsuarioId                " +
                "                ORDER BY  " +
                "                u.NombreUsuario DESC;";

            // El filtro de prioridad estaba escrito a mano en la vista; se sirve desde el
            // catalogo para que no pueda quedar desalineado con la tabla prio.
            string queryPrioridades = "SELECT id_prio, n FROM prio ORDER BY id_prio";

            // El filtro "Asignados a mi" comparaba User.Identity.Name contra la columna
            // Asignado, que trae NombreUsuario: si el login es por correo no casaban nunca.
            // Se manda el NombreUsuario real del usuario en sesion.
            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            var queryYo = "SELECT usuarioid, nombreusuario FROM usuarios WHERE usuarioid = @id";

            result.Add("usuarios", RunQuery(queryUsuarios, parametrosUsuarios));
            result.Add("estatus", RunQuery(queryEstatus));
            result.Add("usuarioPropietario", RunQuery(queryUsuarioPropietario));
            result.Add("prioridades", RunQuery(queryPrioridades));
            result.Add("usuarioActual", RunQuery(queryYo,
                new Dictionary<string, object> { { "id", usuarioId } }));

            return Json(result);
        }

        /// <summary>
        /// Descarga un adjunto por su id.
        ///
        /// Antes recibía el nombre del archivo y lo concatenaba a la carpeta, así que
        /// ?nombre=../../appsettings.json servía cualquier archivo del servidor a
        /// cualquier usuario con sesión. Ahora el id se resuelve contra la BD, se valida
        /// que el usuario pueda ver el ticket dueño del adjunto, y el nombre en disco se
        /// reconstruye desde el uuid guardado: la petición nunca aporta una ruta.
        /// </summary>
        [Authorize]
        public IActionResult DescargarAdjunto(int id)
        {
            if (id <= 0)
                return NotFound();

            var usuarioId = SoporteAuthz.UsuarioActualId(HttpContext.Session) ?? 0;
            if (usuarioId <= 0)
                return StatusCode(403);

            var filas = RunQuery(
                "SELECT n_arch_uid, ext, n_arch_orig, id_tkts FROM tkts_adj WHERE id_tkts_adj = @id",
                new Dictionary<string, object> { { "id", id } });

            if (filas.Count == 0)
                return NotFound();

            var adj = filas[0];

            var permisos = SoporteAuthz.CargarPorId(Convert.ToInt32(adj["id_tkts"]));
            if (!SoporteAuthz.PuedeVer(HttpContext.Session, usuarioId, permisos))
                return StatusCode(403);

            string nombreArchivo = adj["n_arch_uid"]?.ToString() + adj["ext"];
            var carpeta = Path.GetFullPath(Path.Combine(_env.ContentRootPath, CarpetaAdjuntos));
            var rutaFisica = Path.GetFullPath(Path.Combine(carpeta, nombreArchivo));

            // Cinturón y tirantes: aunque el uuid salga de la BD, se confirma que la ruta
            // resuelta sigue dentro de la carpeta de adjuntos antes de abrir nada.
            if (!rutaFisica.StartsWith(carpeta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return NotFound();

            if (!System.IO.File.Exists(rutaFisica))
                return NotFound();

            // Sólo el nombre, sin componentes de ruta que pudieran venir de un registro viejo.
            string nombreDescarga = Path.GetFileName(adj["n_arch_orig"]?.ToString() ?? "");
            if (string.IsNullOrWhiteSpace(nombreDescarga))
                nombreDescarga = nombreArchivo;

            var provider = new FileExtensionContentTypeProvider();

            if (!provider.TryGetContentType(nombreDescarga, out string mime))
            {
                mime = "application/octet-stream";
            }

            return PhysicalFile(rutaFisica, mime, nombreDescarga);
        }
    }

}