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
    [Route("api/product")]
    [EnableRateLimiting("api")]
    public class ProductsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public ProductsController(IConfiguration configuration)
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
                    "PRODUCTO",
                    $"Entrando al controlador Create",
                    nivel: "DEBUG"
                );

                var body = string.Empty;
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
                        string.IsNullOrWhiteSpace(product.Linea) ||
                        string.IsNullOrWhiteSpace(product.Tipo) ||
                        string.IsNullOrWhiteSpace(product.Grupo) ||
                        string.IsNullOrWhiteSpace(product.Naturaleza) ||
                        string.IsNullOrWhiteSpace(product.Unidad) ||
                        string.IsNullOrWhiteSpace(product.ProductoSat) ||
                        string.IsNullOrWhiteSpace(product.UnidadSat) ||
                        string.IsNullOrWhiteSpace(product.ObjetoImpuesto) ||
                        product.Cantidad <= 0)
                    {
                        LogErrorHelper.RegistrarLog(
                            "API",
                            "PRODUCTO",
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
                            var productoService = new ProductApiService();
                            productoService.CreateProduct(products, conn, tx);
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            LogErrorHelper.RegistrarLog(
                                "API",
                                "PRODUCTO",
                                $"Fallo en la transaccion: {ex.Message}",
                                nivel: "ERROR"
                            );
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Created($"api/product/{products.FirstOrDefault()?.Clave}", products);
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(
                    "API",
                    "PRODUCTO",
                    $"Error general: {ex.Message}",
                    nivel: "ERROR"
                );
                return BadRequest("Ocurrio un error al crear el producto, por favor revisa la informacion y vuelve a intentarlo.");
            }
        }

        [HttpGet]
        [Route("{cve}")]
        public IActionResult Get(string cve)
        {
            try
            {
                LogErrorHelper.RegistrarLog(
                    "API",
                    "PRODUCTO",
                    $"Entrando al controlador de consulta: {cve}",
                    nivel: "DEBUG"
                );

                var productService = new ProductApiService();
                var producto = productService.GetProduct(cve);

                if (producto == null)
                {
                    LogErrorHelper.RegistrarLog(
                        "API",
                        "PRODUCTO",
                        $"Producto no encontrado: {cve}",
                        nivel: "ERROR"
                    );

                    return NotFound("No se encontró el producto con la clave especificada.");
                }

                LogErrorHelper.RegistrarLog(
                    "API",
                    "PRODUCTO",
                    $"Producto consultado: {JsonConvert.SerializeObject(producto)}",
                    nivel: "INFO"
                );

                return Ok(producto);
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(
                    "API",
                    "PRODUCTO",
                    $"Error general {ex.Message}",
                    nivel: "ERROR"
                );

                return BadRequest("Ocurrio un error al consultar el producto, por favor revisa la informacion y vuelve a intentarlo");
            }
        }
    }
}