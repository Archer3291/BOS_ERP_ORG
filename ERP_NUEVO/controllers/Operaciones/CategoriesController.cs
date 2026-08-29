using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Operaciones
{
    public class CategoriesController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }

        [Route("categories/list")]
        [HttpGet]
        public JsonResult getCategories()
        {
            string query = "SELECT id, nombre, descripcion FROM categorias_prov ORDER BY nombre ASC";
            var categories = RunQuery(query);

            return Json(new { data = categories });
        }

        [Route("categories/details")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult details(int id)
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "ID inválido." });
            }

            var parameters = new Dictionary<string, object>
            {
                { "id", id }
            };

            string query = "SELECT id, nombre, descripcion FROM categorias_prov WHERE id = @id";
            var category = RunQuery(query, parameters).FirstOrDefault();

            if (category == null)
            {
                return Json(new { success = false, message = "Categoría no encontrada." });
            }

            return Json(new { success = true, data = category });
        }

        [Route("categories/create")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult create(CategoriesModel cat)
        {
            var parameters = new Dictionary<string, object>();

            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
                }

                string query = "SELECT COUNT(*) FROM categorias_prov WHERE nombre = @nombre";
                parameters.Add("nombre", cat.Nombre);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) > 0)
                {
                    return Json(new { success = false, message = "La categoría ya existe." });
                }

                query = "INSERT INTO categorias_prov (nombre, descripcion) VALUES (@nombre, @descripcion)";
                parameters.Clear();
                parameters.Add("nombre", cat.Nombre);
                parameters.Add("descripcion", cat.Descripcion ?? null);

                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Categoría creada exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al crear la categoría: " + ex.Message });
            }
        }

        [Route("categories/update")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult update(CategoriesModel cat)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
                }

                string query = "SELECT COUNT(*) FROM categorias_prov WHERE id = @id";
                parameters.Add("id", cat.Id);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "La categoría no existe." });
                }

                query = "UPDATE categorias_prov SET descripcion = @descripcion WHERE nombre = @nombre";
                parameters.Add("nombre", cat.Nombre);
                parameters.Add("descripcion", cat.Descripcion ?? null);

                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Categoría actualizada exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al actualizar la categoría: " + ex.Message });
            }
        }
    }
}