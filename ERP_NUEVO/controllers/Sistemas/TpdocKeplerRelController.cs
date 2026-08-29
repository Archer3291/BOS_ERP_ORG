using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Sistemas
{
    public class TpdocKeplerRelController : Utilities
    {

        // ─────────────────────────────────────────────────────────────
        //  GET — Todas las relaciones (para DataTable)
        // ─────────────────────────────────────────────────────────────

        public JsonResult GetRelaciones()
        {
            string query = @"
                SELECT r.idrel,
                       r.idtpdoc,
                       t.tpdoc              AS nombre_tpdoc,
                       t.abreviaturatpdoc,
                       r.id_doc,
                       k.genero,
                       k.naturaleza,
                       k.grupo,
                       k.tipo,
                       k.descripcion        AS descripcion_kepler,
                       r.fecha_creacion
                FROM tpdoc_doc_kepler_rel r
                INNER JOIN tpdoc t                 ON t.idtpdoc = r.idtpdoc
                INNER JOIN definicion_doc_kepler k ON k.id_doc  = r.id_doc
                ORDER BY t.tpdoc, r.fecha_creacion DESC";

            var relaciones = RunQuery(query);
            return Json(new { data = relaciones });
        }

        // ─────────────────────────────────────────────────────────────
        //  GET — Catálogo de TpDocs (para dropdown)
        // ─────────────────────────────────────────────────────────────

        public JsonResult GetTpdocs()
        {
            string query = @"
                SELECT idtpdoc, tpdoc, abreviaturatpdoc, descr
                FROM tpdoc
                ORDER BY tpdoc";

            var tpdocs = RunQuery(query);
            return Json(new { data = tpdocs });
        }

        // ─────────────────────────────────────────────────────────────
        //  GET — Docs Kepler disponibles para un TpDoc dado
        //        (excluye los que ya están vinculados a ese TpDoc)
        // ─────────────────────────────────────────────────────────────

        public JsonResult GetDocsKeplerDisponibles(int idtpdoc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("idtpdoc", idtpdoc);

            string query = @"
                SELECT id_doc,
                       genero,
                       naturaleza,
                       grupo,
                       tipo,
                       descripcion,
                       CONCAT('[', genero, '] ', grupo, ' > ', tipo, ': ', descripcion) AS display_text
                FROM definicion_doc_kepler
                WHERE id_doc NOT IN (
                    SELECT id_doc
                    FROM tpdoc_doc_kepler_rel
                    WHERE idtpdoc = @idtpdoc
                )
                ORDER BY genero, grupo, tipo";

            var docs = RunQuery(query, parameters);
            return Json(new { data = docs });
        }

        // ─────────────────────────────────────────────────────────────
        //  GET — Relaciones de un TpDoc específico (para la tabla detalle)
        // ─────────────────────────────────────────────────────────────

        public JsonResult GetRelacionesPorTpdoc(int idtpdoc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("idtpdoc", idtpdoc);

            string query = @"
                SELECT r.idrel,
                       r.idtpdoc,
                       t.tpdoc              AS nombre_tpdoc,
                       t.abreviaturatpdoc,
                       r.id_doc,
                       k.genero,
                       k.naturaleza,
                       k.grupo,
                       k.tipo,
                       k.descripcion        AS descripcion_kepler,
                       r.fecha_creacion
                FROM tpdoc_doc_kepler_rel r
                INNER JOIN tpdoc t                 ON t.idtpdoc = r.idtpdoc
                INNER JOIN definicion_doc_kepler k ON k.id_doc  = r.id_doc
                WHERE r.idtpdoc = @idtpdoc
                ORDER BY r.fecha_creacion DESC";

            var relaciones = RunQuery(query, parameters);
            return Json(new { data = relaciones });
        }

        // ─────────────────────────────────────────────────────────────
        //  POST — Crear relación
        // ─────────────────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(TpdocKeplerRelModel model)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (model.Idtpdoc <= 0 || model.IdDoc <= 0)
                    return Json(new { success = false, message = "Debes seleccionar un documento nuevo y uno de Kepler." });

                // Verificar duplicado antes de golpear el UNIQUE constraint
                string queryCheck = @"
                    SELECT COUNT(*)
                    FROM tpdoc_doc_kepler_rel
                    WHERE idtpdoc = @idtpdoc AND id_doc = @idDoc";
                parameters.Add("idtpdoc", model.Idtpdoc);
                parameters.Add("idDoc", model.IdDoc);

                var count = RunScalar(queryCheck, parameters);
                if (Convert.ToInt32(count) > 0)
                    return Json(new { success = false, message = "Esta relación ya existe." });

                string query = @"
                    INSERT INTO tpdoc_doc_kepler_rel (idtpdoc, id_doc)
                    VALUES (@idtpdoc, @idDoc)";
                parameters.Clear();
                parameters.Add("idtpdoc", model.Idtpdoc);
                parameters.Add("idDoc", model.IdDoc);

                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Relación creada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  POST — Editar relación (swapea el doc Kepler vinculado)
        // ─────────────────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Edit(TpdocKeplerRelModel model)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (model.Idrel <= 0 || model.IdDoc <= 0)
                    return Json(new { success = false, message = "Datos inválidos para editar la relación." });

                // Verificar que el registro exista
                string queryCheck = "SELECT COUNT(*) FROM tpdoc_doc_kepler_rel WHERE idrel = @idrel";
                parameters.Add("idrel", model.Idrel);
                var count = RunScalar(queryCheck, parameters);

                if (Convert.ToInt32(count) == 0)
                    return Json(new { success = false, message = "La relación no existe." });

                // Verificar que el nuevo par no genere un duplicado en otra fila
                string queryDup = @"
                    SELECT COUNT(*)
                    FROM tpdoc_doc_kepler_rel
                    WHERE idtpdoc = @idtpdoc AND id_doc = @idDoc AND idrel <> @idrel";
                parameters.Clear();
                parameters.Add("idtpdoc", model.Idtpdoc);
                parameters.Add("idDoc", model.IdDoc);
                parameters.Add("idrel", model.Idrel);
                var dup = RunScalar(queryDup, parameters);

                if (Convert.ToInt32(dup) > 0)
                    return Json(new { success = false, message = "Ya existe esa combinación de documento nuevo y Kepler." });

                string query = @"
                    UPDATE tpdoc_doc_kepler_rel
                    SET id_doc = @idDoc
                    WHERE idrel = @idrel";
                parameters.Clear();
                parameters.Add("idDoc", model.IdDoc);
                parameters.Add("idrel", model.Idrel);

                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Relación actualizada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  POST — Eliminar relación
        // ─────────────────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Delete(int idrel)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (idrel <= 0)
                    return Json(new { success = false, message = "ID de relación inválido." });

                string query = "DELETE FROM tpdoc_doc_kepler_rel WHERE idrel = @idrel";
                parameters.Add("idrel", idrel);

                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Relación eliminada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
