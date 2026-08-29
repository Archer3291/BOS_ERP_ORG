using System.Data;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Almacen
{
    public class LineasController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetLineas()
        {
            string query = "SELECT c1 clavelinea, c2 nombre, c3 descripcion, c4 activo FROM sellosop.lineas_producto";
            var lineas = RunQuery(query);
            return Json(new { data = lineas });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(Linea linea)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if(linea.clavelinea.Length > 4)
                {
                    return Json(new { success = false, message = "La clave no debe contener mas de 4 digitos." });
                }

                // Validar duplicados en el cliente
                if (linea.clavelinea == null || linea.nombre == null)
                {
                    return Json(new { success = false, message = "La clave y el nombre son obligatorios." });
                }

                string query = "SELECT COUNT(*) FROM sellosop.lineas_producto WHERE c1 = @Id";
                parameters.Add("Id", linea.clavelinea);

                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) > 0)
                {
                    return Json(new { success = false, message = "La clave de línea ya existe." });
                }

                query = "SELECT COUNT(*) FROM sellosop.lineas_producto WHERE c2 = @Nombre";
                parameters.Clear();
                parameters.Add("Nombre", linea.nombre);

                count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) > 0)
                {
                    return Json(new { success = false, message = "El nombre de línea ya existe." });
                }

                if (ModelState.IsValid)
                {
                    query = "INSERT INTO sellosop.lineas_producto  (c1, c2, c3, c4) " +
                    "VALUES (@clavelinea, @nombre, @descripcion, @activo)";
                    parameters.Clear();
                    parameters.Add("clavelinea", linea.clavelinea);
                    parameters.Add("nombre", linea.nombre);
                    parameters.Add("descripcion", linea.descripcion ?? string.Empty);
                    parameters.Add("activo", linea.activo);

                    RunUpdate(query, parameters);

                    return Json(new { success = true, message = "Línea creada correctamente." });
                }
                else
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                    return Json(new { success = false, message = "Error de validación", errors = errors });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Update(Linea linea)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                // Validar duplicados en el cliente
                if (linea.clavelinea == null || linea.nombre == null)
                {
                    return Json(new { success = false, message = "La clave y el nombre son obligatorios." });
                }

                string query = "SELECT COUNT(*) FROM sellosop.lineas_producto WHERE c1 = @Id";
                parameters.Add("Id", linea.clavelinea);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "La clave de línea no existe." });
                }

                query = "SELECT COUNT(*) FROM sellosop.lineas_producto WHERE c2 = @Nombre";
                parameters.Clear();
                parameters.Add("Nombre", linea.nombre);
                count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) > 0)
                {
                    return Json(new { success = false, message = "El nombre de línea ya existe." });
                }

                if (ModelState.IsValid)
                {
                    query = "UPDATE sellosop.lineas_producto SET c2 = @nombre, c3 = @descripcion, c4 = @activo WHERE c1 = @clavelinea";
                    parameters.Clear();
                    parameters.Add("clavelinea", linea.clavelinea);
                    parameters.Add("nombre", linea.nombre);
                    parameters.Add("descripcion", linea.descripcion ?? string.Empty);
                    parameters.Add("activo", linea.activo);
                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Línea actualizada correctamente." });
                }
                else
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                    return Json(new { success = false, message = "Error de validación", errors = errors });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "ID de línea inválido" });
            }

            var parameters = new Dictionary<string, object>();
            try
            {
                // Validar si la línea existe
                string query = "SELECT COUNT(*) FROM sellosop.lineas_producto WHERE c1 = @Id";
                parameters.Add("Id", id);

                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "La línea no existe." });
                }

                // Eliminar la línea
                query = "DELETE FROM sellosop.lineas_producto WHERE c1 = @Id";
                parameters.Clear();
                parameters.Add("Id", id);

                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Línea eliminada correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Detalles(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "ID de línea no puede estar vacío" });
            }

            var parameters = new Dictionary<string, object>();
            try
            {
                // Obtener detalles de la línea
                string query = "SELECT c1 clavelinea, c2 nombre, c3 descripcion, c4 activo " +
                    "FROM sellosop.lineas_producto WHERE c1 = @Id";
                parameters.Add("Id", id);
                var linea = RunQuery(query, parameters)[0];

                if (linea == null)
                {
                    return Json(new { success = false, message = "La línea no existe." });
                }

                return Json(new { success = true, data = linea });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al obtener detalles: " + ex.Message });
            }
        }

        //[HttpGet]
        //public JsonResult ValidarClaveUnica(string clavelinea, int idlinea = 0)
        //{
        //    if (string.IsNullOrEmpty(clavelinea))
        //    {
        //        return Json(false);
        //    }

        //    if (idlinea < 0)
        //    {
        //        return Json(false);
        //    }

        //    var parameters = new Dictionary<string, object>();

        //    string query = "SELECT COUNT(1) FROM sellosop.lineas_producto WHERE c1 = @Clave AND c1 != @Id";
        //    parameters.Add("Clave", clavelinea);
        //    parameters.Add("Id", idlinea);
        //    var count = RunQuery(query, parameters);

        //    bool existe = Convert.ToInt32(count) > 0;
        //    return Json(!existe);
        //}

        //[HttpGet]
        //public JsonResult ValidarNombreUnico(string nombre, int idlinea = 0)
        //{
        //    var parameters = new Dictionary<string, object>();

        //    string query = "SELECT COUNT(1) FROM sellosop.lineas_producto WHERE c2 = @Nombre AND c1 != @Id";

        //    parameters.Add("Nombre", nombre);
        //    parameters.Add("Id", idlinea);

        //    var count = RunScalar(query, parameters);
        //    bool existe = Convert.ToInt32(count) > 0;
        //    return Json(!existe);
        //}
    }
}