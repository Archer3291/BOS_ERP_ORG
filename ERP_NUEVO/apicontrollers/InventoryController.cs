using BOS_ERP.Filters;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Newtonsoft.Json;
using Npgsql;

namespace BOS_ERP.ApiControllers
{
    [ApiKeyAuthorize]
    [ApiController]
    [Route("api/inventory")]
    [EnableRateLimiting("api")]
    public class InventoryController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public InventoryController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost]
        [Route("")]
        public IActionResult Create()
        {
            try
            {
                LogErrorHelper.RegistrarLog(
                    "API",
                    "INVENTARIO",
                    $"Entrando al controlador de registrar Inventario",
                    nivel: "DEBUG"
                );

                string body = string.Empty;
                using (var reader = new System.IO.StreamReader(Request.Body))
                {
                    body = reader.ReadToEndAsync().GetAwaiter().GetResult();
                }

                var products = JsonConvert.DeserializeObject<List<ProductApi>>(body);

                if (products == null || !products.Any())
                    return BadRequest("La lista de productos está vacía.");

                foreach (var product in products)
                {
                    if (string.IsNullOrWhiteSpace(product.Clave) ||
                        string.IsNullOrWhiteSpace(product.Descripcion) ||
                        string.IsNullOrWhiteSpace(product.Unidad) ||
                        product.Cantidad <= 0)
                    {
                        LogErrorHelper.RegistrarLog(
                            "API",
                            "INVENTARIO",
                            $"Uno o más productos tienen datos inválidos: {product}",
                            nivel: "ERROR"
                        );
                        return BadRequest("Uno o más productos tienen datos inválidos.");
                    }
                }

                string connStr = _configuration.GetConnectionString("ERP_SRS")!;
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var inventoryService = new InventoryApiService();
                            inventoryService.RegisterInventory(products, conn, tx);
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            LogErrorHelper.RegistrarLog(
                                "API",
                                "INVENTARIO",
                                $"Fallo en la transaccion: {ex.Message}",
                                nivel: "ERROR"
                            );
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Created($"api/inventory/123", products);
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(
                    "API",
                    "INVENTARIO",
                    $"Error general: {ex.Message}",
                    nivel: "ERROR"
                );
                return BadRequest("Ocurrio un error al crear el producto, por favor revisa la informacion y vuelve a intentarlo.");
            }
        }
    }
}