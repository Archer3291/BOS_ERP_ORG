using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Almacen
{
    public class UnidadesController : Utilities
    {
        [Route("Almacen/Unidades/Datos")]
        [HttpGet]
        public IActionResult Sucursales()
        {
            var returnResult = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryUnidades = "SELECT c1,c2 FROM sellosop.kdid";
            var result = RunQuery(queryUnidades);
            returnResult.Add("unidades", result);
            return Json(returnResult);
        }

        [Route("Almacen/Unidades/BuscarUnidades")]
        [HttpGet]
        public IActionResult BuscarLineas(string id)
        {
            var parameters = new Dictionary<string, object>();
            string queryProducto = "SELECT * FROM sellosop.kdid WHERE c1 = @id";
            parameters.Add("id", id);
            var result = RunQuery(queryProducto, parameters);
            return Json(result);
        }

        [Route("Almacen/Unidades/Buscar")]
        [HttpGet]
        public IActionResult Buscar(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            string query = " SELECT c1 AS id, c2 As descripcion " +
                            "FROM sellosop.kdid " +
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

    }
}