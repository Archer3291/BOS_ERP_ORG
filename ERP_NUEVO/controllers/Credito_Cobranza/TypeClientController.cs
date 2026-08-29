using System.Data;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    public class TypeClientController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetTypeClients()
        {
            try
            {
                string query = "SELECT c1 as Clave, c2 as Descripcion FROM sellosop.kdujtp";
                var typeClients = RunQuery(query);
                return Json(new { data = typeClients });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetTypeClientDetails(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT c1 as Clave, c2 as Descripcion FROM sellosop.kdujtp WHERE c1 = @clave";
            parameters.Add("clave", fc["clave"].ToString());

            var cliente = RunQuery(query, parameters)[0];
            return Json(cliente);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(TypeClientModel typeclient)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kdujtp WHERE c1 = @Clave";
                    parameters.Add("@Clave", typeclient.Clave);
                    var count = RunScalar(query, parameters);

                    if (Convert.ToInt32(count) > 0)
                    {
                        return Json(new { success = false, message = "La clave ya existe" });
                    }

                    query = "INSERT INTO sellosop.kdujtp (c1, c2) VALUES (@Clave, @Descripcion)";
                    parameters.Clear();
                    parameters.Add("@Clave", typeclient.Clave);
                    parameters.Add("@Descripcion", typeclient.Descripcion);

                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Tipo de Cliente creado exitosamente" });
                }
                else
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return Json(new { success = false, message = string.Join("; ", errors) });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Edit(TypeClientModel typeclient)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kdujtp WHERE c1 = @Clave";
                    parameters.Add("@Clave", typeclient.Clave);
                    var count = RunScalar(query, parameters);

                    if (Convert.ToInt32(count) == 0)
                    {
                        return Json(new { success = false, message = "La clave no existe" });
                    }

                    query = "UPDATE sellosop.kdujtp SET c2 = @Descripcion WHERE c1 = @Clave";
                    parameters.Clear();
                    parameters.Add("@Clave", typeclient.Clave);
                    parameters.Add("@Descripcion", typeclient.Descripcion);

                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Tipo de Cliente actualizado exitosamente" });
                }
                else
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return Json(new { success = false, message = string.Join("; ", errors) });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Delete(string clave)
        {
            if (string.IsNullOrEmpty(clave))
            {
                return Json(new { success = false, message = "La clave es requerida" });
            }

            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "SELECT COUNT(*) FROM sellosop.kdujtp WHERE c1 = @Clave";
                parameters.Add("@Clave", clave);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "La clave no existe" });
                }

                query = "DELETE FROM sellosop.kdujtp WHERE c1 = @Clave";
                parameters.Clear();
                parameters.Add("@Clave", clave);

                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Tipo de Cliente eliminado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }
    }
}