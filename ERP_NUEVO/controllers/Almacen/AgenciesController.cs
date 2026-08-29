using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Almacen
{
    public class AgenciesController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetAgencies()
        {
            string query = "SELECT c1 claveaduana, c2 nombre, c3 calle , c4 colonia, c5 poblacion, " +
                "   c6 telefono, c7 telefono2, c8 fax " +
                "FROM sellosop.kdiu";
            var aduanas = RunQuery(query);
            return Json(new { data = aduanas });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(AgencyModel aduana)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (aduana.Clave.Length > 5)
                {
                    return Json(new { success = false, message = "La clave no debe contener mas de 5 digitos." });
                }

                // Validar duplicados en el cliente
                if (aduana.Clave == null || aduana.Nombre == null)
                {
                    return Json(new { success = false, message = "La clave y el nombre son obligatorios." });
                }

                string query = "SELECT COUNT(*) FROM sellosop.kdiu WHERE c1 = @Id";
                parameters.Add("Id", aduana.Clave);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) > 0)
                {
                    return Json(new { success = false, message = "La clave de aduana ya existe." });
                }

                query = "INSERT INTO sellosop.kdiu (c1, c2, c3, c4, c5, c6, c7, c8) VALUES (@ClaveAduana, @Nombre, @Calle, @Colonia, @Poblacion, @Telefono, @Telefono2, @Fax)";
                parameters.Clear();
                parameters.Add("ClaveAduana", aduana.Clave);
                parameters.Add("Nombre", aduana.Nombre);
                parameters.Add("Calle", aduana.Calle);
                parameters.Add("Colonia", aduana.Colonia);
                parameters.Add("Poblacion", aduana.Poblacion);
                parameters.Add("Telefono", aduana.Telefono);
                parameters.Add("Telefono2", aduana.Telefono2);
                parameters.Add("Fax", aduana.Fax);
                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Aduana creada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Edit(AgencyModel aduana)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (aduana.Clave.Length > 5)
                {
                    return Json(new { success = false, message = "La clave no debe contener mas de 5 digitos." });
                }

                // Validar duplicados en el cliente
                if (aduana.Clave == null || aduana.Nombre == null)
                {
                    return Json(new { success = false, message = "La clave y el nombre son obligatorios." });
                }

                string query = "SELECT COUNT(*) FROM sellosop.kdiu WHERE c1 = @ClaveAduana";
                parameters.Add("ClaveAduana", aduana.Clave);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "La clave de aduana no existe." });
                }

                query = "UPDATE sellosop.kdiu SET c2 = @Nombre, c3 = @Calle, c4 = @Colonia, c5 = @Poblacion, c6 = @Telefono, c7 = @Telefono2, c8 = @Fax WHERE c1 = @ClaveAduana";
                parameters.Clear();
                parameters.Add("ClaveAduana", aduana.Clave);
                parameters.Add("Nombre", aduana.Nombre);
                parameters.Add("Calle", aduana.Calle);
                parameters.Add("Colonia", aduana.Colonia);
                parameters.Add("Poblacion", aduana.Poblacion);
                parameters.Add("Telefono", aduana.Telefono);
                parameters.Add("Telefono2", aduana.Telefono2);
                parameters.Add("Fax", aduana.Fax);
                
                RunUpdate(query, parameters);
                
                return Json(new { success = true, message = "Aduana actualizada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Delete(string claveAduana)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (string.IsNullOrEmpty(claveAduana))
                {
                    return Json(new { success = false, message = "La clave de agencia es requerida." });
                }

                string query = "DELETE FROM sellosop.kdiu WHERE c1 = @ClaveAduana";
                parameters.Add("ClaveAduana", claveAduana);
                
                RunUpdate(query, parameters);
                
                return Json(new { success = true, message = "Agencia eliminada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetAgencyById(string claveAduana)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (string.IsNullOrEmpty(claveAduana))
                {
                    return Json(new { success = false, message = "La clave de aduana es requerida." });
                }

                string query = "SELECT c1 claveaduana, c2 nombre, c3 calle, c4 colonia, c5 poblacion, " +
                    "   c6 telefono, c7 telefono2, c8 fax " +
                    "FROM sellosop.kdiu WHERE c1 = @claveaduana";
                parameters.Add("claveaduana", claveAduana);
                
                var aduana = RunQuery(query, parameters)[0];
                
                if (aduana == null)
                {
                    return Json(new { success = false, message = "No se encontró la aduana." });
                }
                
                return Json(new { success = true, data = aduana });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}