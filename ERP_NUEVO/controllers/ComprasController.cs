using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    [Authorize]
    public class ComprasController : Utilities
    {
        public IActionResult GestionSolicitudes()
        {
            // El modal de gestion de OC necesita el catalogo de centros de costo
            ViewBag.areas = RunQuery("SELECT areaid, nombre FROM areas ORDER BY nombre");
            return View();
        }

        // Presupuesto: paso independiente del de Compras, por eso vive en su propia vista
        public IActionResult GestionSolicitudesFinanzas()
        {
            return View();
        }

        public IActionResult MisRequisiciones()
        {
            return View();
        }

        public IActionResult OrdenesCompra()
        {
            return View();
        }

        public IActionResult RequisicionMaterial()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT DISTINCT u.usuarioid, u.nombre || ' ' || u.apellido AS nombre_completo " +
                "FROM usuarios u " +
                "LEFT JOIN permisos_usuario pu ON pu.usuario_id = u.usuarioid " +
                "LEFT JOIN permisos p ON p.id_permiso = pu.permiso_id " +
                "WHERE u.activo = true AND u.sucursal_id = @sucursal AND (u.areaid = 2 AND u.rolid IN (13, 8))";
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            var result = RunQuery(query, parameters);

            query = "SELECT usuarioid, nombre || ' ' || apellido AS nombre_completo " +
                "FROM usuarios " +
                "WHERE activo = true AND rolid = 11";
            var resultDirector = RunQuery(query);

            query = "SELECT areaid, nombre FROM areas";
            var areas = RunQuery(query);

            ViewBag.comprador = result;
            ViewBag.directores = resultDirector;
            ViewBag.areas = areas;
            return View();
        }

        public IActionResult GestionSolicitudesGerente()
        {
            ViewBag.areas = RunQuery("SELECT areaid, nombre FROM areas ORDER BY nombre");
            return View();
        }

        [HttpGet]
        public IActionResult EstatusDocumento()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();
            var userId = GetUserId(User.Identity.Name);
            var role = GetUserRole(User.Identity.Name);
            var area = GetAreaName(User.Identity.Name);

            string query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut, em.usr1, em.id_encabezado, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS nuevo_codigo, " +
                "u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "(SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "a.nombre AS nombre_area, fch1 AS fch, NOW() AS fechaActual, uuid " +
                "FROM encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr0 " +
                "INNER JOIN areas a ON a.areaid = u.areaid ";

            var camposUsuario = new List<string>();
            var estatusExcluir = new List<int>();

            // Simulación de switch (role, area) compatible con C# 7.3
            if (role == "Usuario" && area == "Compras Nacionales")
            {
                camposUsuario.AddRange(new[] { "usr2", "usr0" });
                estatusExcluir.AddRange(new[] { 11, 16 });
            }
            else if (role == "Gerente" && area == "Compras Nacionales")
            {
                camposUsuario.AddRange(new[] { "usr1", "usr2", "usr0" });
                estatusExcluir.AddRange(new[] { 11, 16 });
            }
            else if (role == "Usuario" && area == "Administración y Finanzas")
            {
                camposUsuario.AddRange(new[] { "usr4", "usr0" });
                estatusExcluir.Add(11);
            }
            else if (role == "Gerente" && area == "Administración y Finanzas")
            {
                camposUsuario.AddRange(new[] { "usr1", "usr4", "usr0" });
                estatusExcluir.Add(11);
            }
            else if (role == "Director")
            {
                camposUsuario.AddRange(new[] { "usr6", "usr0" });
                estatusExcluir.AddRange(new[] { 11, 16 });
            }
            else if (role == "Gerente")
            {
                camposUsuario.AddRange(new[] { "usr1", "usr0", "usr3" });
                estatusExcluir.AddRange(new[] { 11, 16 });
            }
            else if (role == "Usuario")
            {
                camposUsuario.Add("usr0");
                estatusExcluir.AddRange(new[] { 11, 16 });
            }

            // Construcción del WHERE
            var condiciones = new List<string> { "em.gen = 'CPN'" };

            if (estatusExcluir.Count > 0)
                condiciones.Add($"em.estatus_id NOT IN ({string.Join(", ", estatusExcluir)})");

            if (camposUsuario.Count > 0)
                condiciones.Add("(" + string.Join(" OR ", camposUsuario.Select(c => $"em.{c} = @usuarioId")) + ")");

            if (condiciones.Count > 0)
                query += " WHERE " + string.Join(" AND ", condiciones);

            parameters.Add("usuarioId", userId);

            result.Add("documentos", RunQuery(query, parameters));
            return View(result);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetEstatusDocumentos(string uuid)
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();

            // Obtener documento actual
            string query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, COALESCE(em.sub, em.imp) AS sub, COALESCE(em.dto, 0) AS dto, " +
                "   em.usr0, em.usr1, em.usr2, em.usr3, em.usr4, em.usr5, em.usr6, " +
                "   em.fch0, em.fch1, em.fch2, em.fch3, em.fch4, em.fch5, em.fch6, " +
                "   em.firma1 AS firmagerente, em.firma6 AS firmadireccion, " +
                "   em.id_encabezado, em.coment_aut AS observaciones, " +
                "   em.folio, " +
                "   u0.nombre || ' ' || u0.apellido AS solicitante, " +
                "   u1.nombre || ' ' || u1.apellido AS gerente, " +
                "   u2.nombre || ' ' || u2.apellido AS comprador, " +
                "   u3.nombre || ' ' || u3.apellido AS gerenteRevision, " +
                //"   u4.nombre || ' ' || u4.apellido AS finanzas, " +
                "   u5.nombre || ' ' || u5.apellido AS compradorRevision, " +
                "   u6.nombre || ' ' || u6.apellido AS direccion, " +
                "   a.nombre AS nombre_area, em.encabezados_padre, variacion, fch_cap, usr_dep, en_presupuesto, tpd.tpdoc AS tipo_documento " +
                "FROM encabezadomov em " +
                "LEFT JOIN usuarios u0 ON u0.usuarioid = em.usr0 " +
                "LEFT JOIN usuarios u1 ON u1.usuarioid = em.usr1 " +
                "LEFT JOIN usuarios u2 ON u2.usuarioid = em.usr2 " +
                "LEFT JOIN usuarios u3 ON u3.usuarioid = em.usr3 " +
                //"LEFT JOIN usuarios u4 ON u4.usuarioid = em.usr4 " +
                "LEFT JOIN usuarios u5 ON u5.usuarioid = em.usr5 " +
                "LEFT JOIN usuarios u6 ON u6.usuarioid = em.usr6 " +
                "LEFT JOIN areas a ON a.areaid = u1.areaid " +
                "INNER JOIN tpdoc tpd ON tpd.abreviaturatpdoc = em.nat " +
                "WHERE em.uuid = @uuid";
            parameters.Add("uuid", uuid);
            result.Add("documento", RunQuery(query, parameters));

            // Obtener todos los documentos vinculados con el actual
            query = "WITH RECURSIVE relacionados AS (" +
                "   SELECT em.id_encabezado, em.encabezados_padre, " +
                "       em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "       em.variacion, em.variacion_padre, ARRAY[em.id_encabezado] AS visitados " +
                "   FROM encabezadomov em " +
                "   WHERE em.uuid = @uuid " +
                "   UNION ALL " +
                "   SELECT e.id_encabezado, e.encabezados_padre, " +
                "       e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio, " +
                "       e.variacion, e.variacion_padre, r.visitados || e.id_encabezado " +
                "   FROM encabezadomov e " +
                "   INNER JOIN relacionados r ON e.id_encabezado = r.encabezados_padre OR e.encabezados_padre = r.id_encabezado " +
                "   WHERE NOT e.id_encabezado = ANY(r.visitados) " +
                ")" +
                "SELECT DISTINCT r.id_encabezado, r.encabezados_padre, r.folio, r.variacion, r.variacion_padre, " +
                "   a.path, a.uuid, a.extencion, a.nombre_original " +
                "FROM relacionados r " +
                "LEFT JOIN archivos_solicitud a ON a.encabezado_id = r.id_encabezado " +
                "ORDER BY r.id_encabezado";
            result.Add("encabezadosAnteriores", RunQuery(query, parameters));

            return Json(result);
        }

        [HttpGet]
        public IActionResult VISeguimientoPedidos()
        {
            return View();
        }
    }
}