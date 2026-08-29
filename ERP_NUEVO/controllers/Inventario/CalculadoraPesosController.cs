using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Inventario
{
    public class CalculadoraPesosController : Utilities
    {
        public JsonResult ObtenerProducto(string cve_prod)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "cve_prod", cve_prod },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string query = @"
                                WITH proveedores AS (
                                    SELECT fa.prod_id, fa.gpo_marca AS provider, fa.peso AS weight
                                    FROM frac_arancelarias fa
                                    WHERE fa.gpo_marca IS NOT NULL
                                
                                    UNION ALL
                                    SELECT fa.prod_id, fa.gpo_marca2, fa.peso2
                                    FROM frac_arancelarias fa
                                    WHERE fa.gpo_marca2 IS NOT NULL
                                
                                    UNION ALL
                                    SELECT fa.prod_id, fa.gpo_marca3, fa.peso3
                                    FROM frac_arancelarias fa
                                    WHERE fa.gpo_marca3 IS NOT NULL
                                )
                                SELECT jsonb_build_object(
                                    'id', c.id_catproductos,
                                    'name', c.descr_prod,
                                    'code', c.cve_prod,
                                    'icon', '📦',
                                    'providers',
                                    (
                                        SELECT COALESCE(
                                            jsonb_agg(
                                                jsonb_build_object(
                                                    'name', p.provider,
                                                    'weightPerUnit', p.weight,
                                                    'unit', 'kg'
                                                )
                                            ),
                                            '[]'::jsonb
                                        )
                                        FROM proveedores p
                                        WHERE p.prod_id = c.id_catproductos
                                    )
                                ) AS product
                                FROM catproductos c
                                WHERE c.cve_prod = @cve_prod 
                                AND empresa_id = @empresa_id;
                                        ";

                var data = RunQuery(query, parameters); // List<Dictionary<string, object>>

                if (data == null || data.Count == 0)
                {
                    Response.StatusCode = 404;
                    return Json(new
                    {
                        icon = "error",
                        message = "Producto no encontrado."
                    });
                }

                Response.StatusCode = 200;
                return Json(new
                {
                    data = data[0], // primer registro
                    icon = "success"
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Inventario/ObtenerProducto");
                Response.StatusCode = 500;
                return Json(new
                {
                    icon = "error",
                    message = "Error al obtener el producto."
                });
            }
        }

        public JsonResult GuardarProveedores(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("prod_id", Convert.ToInt32(fc["productId"].ToString()));
                var providers = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["providers"].ToString());

                parameters.Add("gpo_marca",
                    providers.Count > 0 ? providers[0]["name"] : DBNull.Value);

                parameters.Add("peso",
                    providers.Count > 0
                        ? (object)Convert.ToDecimal(providers[0]["weightPerUnit"])
                        : DBNull.Value);

                parameters.Add("gpo_marca2",
                    providers.Count > 1 ? providers[1]["name"] : DBNull.Value);

                parameters.Add("peso2",
                    providers.Count > 1
                        ? (object)Convert.ToDecimal(providers[1]["weightPerUnit"])
                        : DBNull.Value);

                parameters.Add("gpo_marca3",
                    providers.Count > 2 ? providers[2]["name"] : DBNull.Value);

                parameters.Add("peso3",
                    providers.Count > 2
                        ? (object)Convert.ToDecimal(providers[2]["weightPerUnit"])
                        : DBNull.Value);

                string query = "UPDATE frac_arancelarias " +
                    "SET gpo_marca=@gpo_marca, peso=@peso, gpo_marca2=@gpo_marca2, gpo_marca3=@gpo_marca3, peso2=@peso2, peso3=@peso3 " +
                    "WHERE prod_id=@prod_id;";

                RunUpdate(query, parameters);
                return Json(new
                {
                    success = true,
                    icon = "success"
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Inventario/GuardarProveedores");
                return Json(new
                {
                    success = false,
                    icon = "error",
                    message = "Error al guardar los proveedores."
                });
            }
        }
    }
}