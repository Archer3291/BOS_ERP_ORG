using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class InventarioController : Utilities
    {
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDocumentos(string nombre, string sortColumn, string sortDir, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>
            {
                { "sucursal", HttpContext.Session.GetInt32("Sucursal") },
                { "nombre", nombre ?? "" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
            };

            var allowedColumns = new HashSet<string> {
                "folio", "fecha",
                "comentario", "tipo_proceso", "usuario", "dipo_documento"
            };

            if (!allowedColumns.Contains(sortColumn))
            {
                sortDir = "desc";
                sortColumn = "fecha";
            }

            string where = "";

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where = " AND (folio ILIKE '%' || @nombre || '%' " +
                    "   OR comentario ILIKE '%' || @nombre || '%' " +
                    "   OR tipo_proceso ILIKE '%' || @nombre || '%' " +
                    "   OR usuario || ' ' || u.apellido ILIKE '%' || @nombre || '%' " +
                    "   OR dipo_documento ILIKE '%' || @nombre || '%' " +
                    ") ";
            }

            string query = "SELECT e.folio folio, e.fch fecha, e.coment1 comentario, e.tipo_proceso tipo_proceso, t.tpdoc dipo_documento, u.nombre || ' ' || u.apellido usuario, " +
                "   e.id_encabezado " +
                "FROM encabezadomov e " +
                "INNER JOIN tpdoc t ON t.idtpdoc = e.nro_tp_doc " +
                "INNER JOIN usuarios u ON u.usuarioid = e.usr0 " +
                $"WHERE e.gen = 'INV' AND e.suc = @sucursal {where}" +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) FROM encabezadomov e " +
                "INNER JOIN tpdoc t ON t.idtpdoc = e.nro_tp_doc " +
                "INNER JOIN usuarios u ON u.usuarioid = e.usr0 " +
                $"WHERE e.gen = 'INV' AND e.suc = @sucursal {where}";

            int total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new { data, total });
        }

        public JsonResult GetPartidasDocumento(int id)
        {
            var parameters = new Dictionary<string, object>
            {
                { "encabezado_id", id },
            };
            string query = "SELECT p.cve_prod, p.descr_prod, p.cant_ud, p.ud " +
                "FROM partidasdoc p  " +
                "WHERE p.encabezado_id = @encabezado_id";
            var data = RunQuery(query, parameters);
            return Json(data);
        }
    }
}
