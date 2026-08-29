using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Almacen
{
    public class GruposController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetGroups()
        {
            string query = "SELECT c1 clave, c2 descripcion, c4 activo FROM sellosop.kdif";
            var groups = RunQuery(query).ToList();
            return Json(new { data = groups });
        }

        public JsonResult GroupDetails(string clave)
        {
            if (string.IsNullOrEmpty(clave))
            {
                return Json(new { success = false, message = "La clave es requerida." });
            }

            var parameters = new Dictionary<string, object>();

            string query = "SELECT c1 clave, c2 descripcion, c4 activo FROM sellosop.kdif WHERE c1 = @clave";
            parameters.Add("clave", clave);
            var group = RunQuery(query, parameters)[0];

            return Json(group);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(GroupModel group)
        {
            var parameters = new Dictionary<string, object>();

            if (!ModelState.IsValid)
            {
                return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }

            if (group.Clave.Length > 3)
            {
                return Json(new { success = false, message = "La clave no puede exceder 3 caracteres." });
            }

            string query = "SELECT COUNT(*) FROM sellosop.kdif WHERE c1 = @clave";
            parameters.Add("clave", group.Clave);
            var count = RunScalar(query, parameters);

            if (Convert.ToInt32(count) > 0)
            {
                return Json(new { success = false, message = "El grupo ya existe." });
            }

            query = "INSERT INTO sellosop.kdif (c1, c2, c4) VALUES (@clave, @descripcion, @activo)";
            parameters.Clear();
            parameters.Add("clave", group.Clave);
            parameters.Add("descripcion", group.Description);
            parameters.Add("activo", group.Activo);
            RunQuery(query, parameters);
            return Json(new { success = true, message = "Grupo creado exitosamente." });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Update(GroupModel group)
        {
            var parameters = new Dictionary<string, object>();
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }

            if (group.Clave.Length > 3)
            {
                return Json(new { success = false, message = "La clave no puede exceder 3 caracteres." });
            }

            string query = "SELECT COUNT(*) FROM sellosop.kdif WHERE c1 = @clave";
            parameters.Add("clave", group.Clave);
            var count = RunScalar(query, parameters);

            if (Convert.ToInt32(count) == 0)
            {
                return Json(new { success = false, message = "El grupo no existe." });
            }

            query = "UPDATE sellosop.kdif SET c2 = @descripcion, c4 = @activo WHERE c1 = @clave";
            parameters.Clear();
            parameters.Add("clave", group.Clave);
            parameters.Add("descripcion", group.Description);
            parameters.Add("activo", group.Activo);

            RunUpdate(query, parameters);
            return Json(new { success = true, message = "Grupo actualizado exiteso." });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Delete(string clave)
        {
            var parameters = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(clave))
            {
                return Json(new { success = false, message = "La clave es requerida." });
            }

            string query = "SELECT COUNT(*) FROM sellosop.kdif WHERE c1 = @clave";
            parameters.Add("clave", clave);
            var count = RunScalar(query, parameters);

            if (Convert.ToInt32(count) == 0)
            {
                return Json(new { success = false, message = "El grupo no existe." });
            }

            query = "DELETE FROM sellosop.kdif WHERE c1 = @clave";
            parameters.Clear();
            parameters.Add("clave", clave);
            RunUpdate(query, parameters);
            return Json(new { success = true, message = "Grupo eliminado correctamente" });
        }
    }
}