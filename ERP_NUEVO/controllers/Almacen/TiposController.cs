using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Almacen
{
    public class TiposController : Utilities
    {
        [Route("Almacen/Tipos/Datos")]
        [HttpGet]
        public IActionResult obtenerTiposDatos()
        {
            var returnResult = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryTipos = "SELECT c1,c2, c4 FROM sellosop.kdie";
            var result = RunQuery(queryTipos);
            returnResult.Add("tipos", result);
            return Json(returnResult);
        }

        [Route("Almacen/Tipos/BuscarTipos")]
        [HttpGet]
        public IActionResult buscarTipos(string id)
        {
            var parameters = new Dictionary<string, object>();
            string queryProducto = "SELECT * FROM sellosop.kdie WHERE c1 = @id";
            parameters.Add("id", id);
            var result = RunQuery(queryProducto, parameters);
            return Json(result);
        }

        [Route("Almacen/Tipos/Buscar")]
        [HttpGet]
        public IActionResult Buscar(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            string query = " SELECT c1 AS id, c2 As descripcion " +
                            "FROM sellosop.kdie " +
                            "WHERE LOWER(c2) LIKE LOWER(@nombre) " +
                            "OR LOWER(c1) LIKE LOWER(@nombre) " +
                            "ORDER BY c1 " +
                            "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize); // Calcula el offset
            parameters.Add("pageSize", pageSize); // Número de resultados por página

            var result = RunQuery(query, parameters);
            return Json(result);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Route("Almacen/Tipos/Crear")]
        public IActionResult Create(TypeModel tipo)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }

            if (tipo.Clave.Length > 2)
            {
                return Json(new { success = false, message = "La clave debe tener máximo 2 caracteres." });
            }

            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "SELECT COUNT(*) FROM sellosop.kdie WHERE c1 = @id";
                parameters.Add("id", tipo.Clave);
                var countResult = RunScalar(query, parameters);

                if (Convert.ToInt32(countResult) > 0)
                {
                    return Json(new { success = false, message = "Ya existe un tipo con esta clave." });
                }

                query = "INSERT INTO sellosop.kdie (c1, c2, c4) VALUES (@id, @descripcion, @estado) ";
                parameters.Clear();
                parameters.Add("id", tipo.Clave);
                parameters.Add("descripcion", tipo.Description);
                parameters.Add("estado", tipo.Status);
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Tipo creado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al crear el tipo: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Route("Almacen/Tipos/Editar")]
        public IActionResult Edit(TypeModel tipo)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }

            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "SELECT COUNT(*) FROM sellosop.kdie WHERE c1 = @id";
                parameters.Add("id", tipo.Clave);
                var countResult = RunScalar(query, parameters);

                if (Convert.ToInt32(countResult) == 0)
                {
                    return Json(new { success = false, message = "El tipo con esta clave no existe." });
                }

                query = "UPDATE sellosop.kdie SET c2 = @descripcion, c4 = @estado WHERE c1 = @id";
                parameters.Clear();
                parameters.Add("id", tipo.Clave);
                parameters.Add("descripcion", tipo.Description);
                parameters.Add("estado", tipo.Status);
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Tipo editado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al editar el tipo: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Route("Almacen/Tipos/Eliminar")]
        public IActionResult Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "La clave es requerida." });
            }

            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "SELECT COUNT(*) FROM sellosop.kdie WHERE c1 = @id";
                parameters.Add("id", id);
                var countResult = RunScalar(query, parameters);

                if (Convert.ToInt32(countResult) == 0)
                {
                    return Json(new { success = false, message = "El tipo con esta clave no existe." });
                }

                query = "DELETE FROM sellosop.kdie WHERE c1 = @id";
                parameters.Clear();
                parameters.Add("id", id);
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Tipo eliminado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar el tipo: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Route("Almacen/Tipos/Detalles")]
        public IActionResult Detalles(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "Selecciona un tipo primero." });
            }

            try
            {
                var parameters = new Dictionary<string, object>();

                string query = "SELECT c1 AS Clave, c2 AS Descripcion, c4 AS Estado FROM sellosop.kdie WHERE c1 = @id";
                parameters.Add("id", id);
                var result = RunQuery(query, parameters);

                if (result.Count == 0)
                {
                    return Json(new { success = false, message = "El tipo con esta clave no existe." });
                }

                return Json(new { success = true, data = result.First() });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al obtener los detalles del tipo: " + ex.Message });
            }
        }
    }
}