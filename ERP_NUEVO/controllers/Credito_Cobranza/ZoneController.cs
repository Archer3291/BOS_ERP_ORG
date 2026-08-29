using System.Data;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    public class ZoneController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetZones()
        {
            try
            {
                string query = "SELECT c1 clave, c2 descripcion FROM sellosop.kduk";
                var zones = RunQuery(query);
                return Json(new { data = zones });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult zoneDetails(string clave)
        {
            var parameters = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(clave))
            {
                return Json(new { success = false, message = "La clave es requerida" });
            }

            try
            {
                string query = "SELECT c1 clave, c2 descripcion FROM sellosop.kduk WHERE c1 = @Clave";
                parameters.Add("Clave", clave);
                var zone = RunQuery(query, parameters).FirstOrDefault();
                if (zone == null)
                {
                    return Json(new { success = false, message = "Zona no encontrada" });
                }
                return Json(new { success = true, data = zone });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(ZoneModel zone)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kduk WHERE c1 = @Clave";
                    parameters.Add("Clave", zone.Clave);
                    var count = RunScalar(query, parameters);

                    if (Convert.ToInt32(count) > 0)
                    {
                        return Json(new { success = false, message = "La clave ya existe" });
                    }

                    query = "INSERT INTO sellosop.kduk (c1, c2) VALUES (@Clave, @Descripcion)";
                    parameters.Clear();
                    parameters.Add("@Clave", zone.Clave);
                    parameters.Add("@Descripcion", zone.Descripcion);
                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Zona creada exitosamente" });
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
        public JsonResult Edit(ZoneModel zone)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kduk WHERE c1 = @Clave";
                    parameters.Add("Clave", zone.Clave);
                    var count = RunScalar(query, parameters);

                    if (Convert.ToInt32(count) == 0)
                    {
                        return Json(new { success = false, message = "La clave no existe" });
                    }

                    parameters.Clear();
                    query = "UPDATE sellosop.kduk SET c2 = @Descripcion WHERE c1 = @Clave";
                    parameters.Add("Clave", zone.Clave);
                    parameters.Add("Descripcion", zone.Descripcion);

                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Zona actualizada exitosamente" });
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
            var parameters = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(clave))
            {
                return Json(new { success = false, message = "La clave es requerida" });
            }

            try
            {
                string query = "SELECT COUNT(*) FROM sellosop.kduk WHERE c1 = @Clave";
                parameters.Add("Clave", clave);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "La clave no existe" });
                }

                query = "DELETE FROM sellosop.kduk WHERE c1 = @Clave";
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Zona eliminada exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }
    }
}