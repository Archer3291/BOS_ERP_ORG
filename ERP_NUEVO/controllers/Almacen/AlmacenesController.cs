using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Almacen
{
    public class AlmacenesController : Utilities
    {
        [Route("Almacen/Almacenes/BuscarSucursales")]
        [HttpGet]
        public IActionResult BuscarSucursales()
        {
            var returnResult = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryProducto = "SELECT c1,c2 FROM sellosop.kdms";
            var result = RunQuery(queryProducto);
            returnResult.Add("sucursales", result);
            return Json(returnResult);
        }


        [Route("Almacen/Almacenes/BuscarAlmacenes")]
        [HttpGet]
        public IActionResult BuscarAlmacenes(string id, string sucursal = "")
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT * FROM sellosop.kdiq WHERE c2 = @id";

            parameters.Add("id", id);

            if (!string.IsNullOrWhiteSpace(sucursal))
            {
                query += " AND c1 = @sucursal"; // c1 es la sucursal
                parameters.Add("sucursal", sucursal);
            }

            var result = RunQuery(query, parameters);
            return Json(result);
        }


        [Route("Almacen/Almacenes/Buscar")]
        [HttpGet]
        public IActionResult Buscar(string nombre, string sucursal = "", int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            var whereClauses = new List<string>();

            // Filtro por nombre
            whereClauses.Add("(LOWER(descr_prod) LIKE LOWER(@nombre) OR LOWER(c2) LIKE LOWER(@nombre))");
            parameters.Add("nombre", $"%{nombre}%");

            // Filtro por sucursal si se proporciona
            if (!string.IsNullOrWhiteSpace(sucursal))
            {
                whereClauses.Add("LOWER(c1) = LOWER(@sucursal)");
                parameters.Add("sucursal", sucursal);
            }

            string query = "SELECT cve_prod, descr_prod, udm " +
                "FROM catproductos " +
                $"WHERE {string.Join(" AND ", whereClauses)} " +
                "ORDER BY cve_prod " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);

            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult Datos()
        {
            var almacenes = new BOS_ERP.Models.Almacen();
            string query = "SELECT c1,c2 FROM sellosop.kdms";
            var result = RunQuery(query);

            return Json(new { sucursales = result });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Guardar(WarehouseModel wM)
        {
            var parameters = new Dictionary<string, object>();
            string guardarQuery = "INSERT INTO sellosop.kdiq (c1, c2, c3) " +
                                  "VALUES (@sucursal, @idalmacen, @nombre)";

            parameters.Add("sucursal", wM.Sucursal);
            parameters.Add("idalmacen", wM.Clave);
            parameters.Add("nombre", wM.Description);

            try
            {
                RunQuery(guardarQuery, parameters);
                return Json(new { success = true, message = "Almacén guardado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar el almacén: " + ex.Message });
            }

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Editar(WarehouseModel wM)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string updateQuery = "UPDATE sellosop.kdiq " +
                                     "SET c1 = @sucursal, c3 = @nombre " +
                                     "WHERE c2 = @idalmacen"; // c2 es la clave del almacén

                parameters.Add("sucursal", wM.Sucursal);
                parameters.Add("idalmacen", wM.Clave);
                parameters.Add("nombre", wM.Description);


                var filasAfectadas = RunQuery(updateQuery, parameters);

                return Json(new { success = true, message = "Almacen actualizado." });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al actualizar el almacén: " + ex.Message });
            }
        }

    }
}