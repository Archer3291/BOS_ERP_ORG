using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Sistemas
{
    public class UserSessionsController : Utilities
    {

        // ── GET: KPIs ─────────────────────────────────────────────────────
        [HttpGet]
        public JsonResult ObtenerKPIs()
        {
            try
            {
                string sql = "SELECT COUNT(*) AS Total, COUNT(*) FILTER (WHERE isactive = true AND expirytime > NOW()) AS Activas, " +
                    "   COUNT(*) FILTER (WHERE isactive = false OR  expirytime <= NOW()) AS Inactivas, " +
                    "   COUNT(DISTINCT deviceidentifier) FILTER (WHERE deviceidentifier IS NOT NULL AND deviceidentifier <> '') AS DispositivosUnicos " +
                    "FROM usersessions";

                var rows = RunQuery(sql, new Dictionary<string, object>());
                var row  = (IDictionary<string, object>)rows[0];

                return Json(new
                {
                    exito  = true,
                    datos  = new
                    {
                        total = Convert.ToInt64(row["total"]),
                        activas = Convert.ToInt64(row["activas"]),
                        inactivas = Convert.ToInt64(row["inactivas"]),
                        dispositivosUnicos = Convert.ToInt64(row["dispositivosunicos"])
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── GET: lista paginada de sesiones ───────────────────────────────
        [HttpPost]
        public JsonResult ObtenerSesiones(string nombre, string sortColumn, string sortDir, int page = 1, int pageSize = 50)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "nombre", nombre ?? "" },
                    { "offset", (page - 1) * pageSize },
                    { "pageSize", pageSize },
                };

                var allowedColumns = new HashSet<string> {
                    "usuario", "nombreusuario", "empresa", "activo", "ultima_actividad"
                };

                if (!allowedColumns.Contains(sortColumn))
                    sortColumn = "userid";

                string where = "";

                if (!string.IsNullOrWhiteSpace(nombre))
                {
                    where = " AND (" +
                        "   us.nombre ILIKE '%' || @nombre || '%' OR " +
                        "   us.apellido ILIKE '%' || @nombre || '%' OR " +
                        "   e.nombre ILIKE '%' || @nombre || '%' " +
                        ") ";
                }

                string query = "SELECT sessionid, userid, us.nombre || ' ' || us.apellido usuario, nombreusuario, e.nombre empresa,  isactive activo, lastactivity ultima_actividad " +
                    "FROM usersessions u " +
                    "INNER JOIN usuarios us ON us.usuarioid = u.userid " +
                    "INNER JOIN empresas e ON e.empresaid = us.empresaid " +
                    $"WHERE 1=1 {where} " +
                    $"ORDER BY isactive DESC, {sortColumn} {sortDir} " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
                var data = RunQuery(query, parameters);

                query = "SELECT COUNT(*) AS total " +
                    "FROM usersessions u " +
                    "INNER JOIN usuarios us ON us.usuarioid = u.userid " +
                    "INNER JOIN empresas e ON e.empresaid = us.empresaid " +
                    $"WHERE 1=1 {where}";
                var total = Convert.ToInt32(RunScalar(query, parameters));

                return Json(new { data, total });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: cerrar (desactivar) una sesión ──────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CerrarSesion(int sessionId)
        {
            try
            {
                var rows = RunQuery(
                    @"UPDATE usersessions
                      SET isactive = false, expirytime = NOW()
                      WHERE sessionid = @id
                      RETURNING sessionid",
                    new Dictionary<string, object> { { "@id", sessionId } });

                if (rows == null || rows.Count == 0)
                    return Json(new { exito = false, mensaje = "Sesión no encontrada." });

                return Json(new { exito = true, mensaje = "Sesión cerrada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: eliminar registro de sesión ─────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult EliminarSesion(int sessionId)
        {
            try
            {
                RunQuery(
                    "DELETE FROM usersessions WHERE sessionid = @id",
                    new Dictionary<string, object> { { "@id", sessionId } });

                return Json(new { exito = true, mensaje = "Registro eliminado." });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: cerrar TODAS las sesiones activas ───────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CerrarTodasActivas()
        {
            try
            {
                var rows = RunQuery(
                    @"UPDATE usersessions
                      SET isactive = false, expirytime = NOW()
                      WHERE isactive = true AND expirytime > NOW()
                      RETURNING sessionid",
                    new Dictionary<string, object>());

                int cerradas = rows?.Count ?? 0;
                return Json(new
                {
                    exito   = true,
                    mensaje = $"{cerradas} sesión(es) cerrada(s) correctamente.",
                    cerradas
                });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }
    }
}
