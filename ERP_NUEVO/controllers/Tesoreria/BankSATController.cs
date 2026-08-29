using System.Data;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Tesoreria
{
    public class BankSATController : Utilities
    {

        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetBanks()
        {
            try
            {
                string query = "SELECT c1 as Clave, c2 as Nombre, c3 as RazonSocial FROM sellosop.kdrhfeba";
                var banks = RunQuery(query);

                return Json(new { data = banks });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public JsonResult getBankDetails(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>()
            {
                { "clave", fc["clave"].ToString()}
            };

            string query = "SELECT c1 clave, c2 nombre, c3 razonsocial FROM sellosop.kdrhfeba WHERE c1 = @clave";
            var bank = RunQuery(query, parameters);

            return Json(bank);
        }

        [HttpPost]
        public JsonResult Create(BankSATModel bank)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    parameters.Add("Clave", bank.Clave);
                    var bankQty = RunScalar("SELECT COUNT(*) FROM sellosop.kdrhfeba WHERE c1 = @Clave", parameters);

                    if (Convert.ToInt32(bankQty) > 0)
                    {
                        return Json(new { success = false, message = "La clave ya existe. Por favor ingrese una clave única." });
                    }

                    string query = "INSERT INTO sellosop.kdrhfeba (c1, c2, c3) VALUES (@Clave, @Nombre, @RazonSocial)";
                    parameters.Add("Nombre", bank.Nombre);
                    parameters.Add("RazonSocial", bank.RazonSocial);
                    RunUpdate(query, parameters);

                    return Json(new { success = true, message = "Banco SAT creado exitosamente" });
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

        [HttpPost]
        public JsonResult Edit(BankSATModel bank)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    parameters.Add("Clave", bank.Clave);
                    var bankQty = RunScalar("SELECT COUNT(*) FROM sellosop.kdrhfeba WHERE c1 = @Clave", parameters);

                    if (Convert.ToInt32(bankQty) == 0)
                    {
                        return Json(new { success = false, message = "La clave no existe. Por favor ingrese una clave válida." });
                    }

                    // Update the bank details
                    string query = "UPDATE sellosop.kdrhfeba SET c2 = @Nombre, c3 = @RazonSocial WHERE c1 = @Clave";
                    parameters.Add("Nombre", bank.Nombre);
                    parameters.Add("RazonSocial", bank.RazonSocial);
                    RunUpdate(query, parameters);

                    return Json(new { success = true, message = "Banco SAT actualizado exitosamente" });
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

        [HttpPost]
        public JsonResult Delete(string clave)
        {
            if (string.IsNullOrEmpty(clave))
            {
                return Json(new { success = false, message = "La clave es requerida para eliminar un banco SAT." });
            }

            var parameters = new Dictionary<string, object>
            {
                { "Clave", clave }
            };

            try
            {
                // Check if the bank exists
                string query = "SELECT COUNT(*) FROM sellosop.kdrhfeba WHERE c1 = @Clave";
                var bankQty = RunScalar(query, parameters);

                if (Convert.ToInt32(bankQty) == 0)
                {
                    return Json(new { success = false, message = "La clave no existe. Por favor ingrese una clave válida." });
                }

                // Delete the bank
                query = "DELETE FROM sellosop.kdrhfeba WHERE c1 = @Clave";
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Banco SAT eliminado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }
    }
}