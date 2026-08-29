using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Data;

namespace BOS_ERP.Controllers.Tesoreria
{
    public class BankController : Utilities
    {
        public IActionResult Index()
        {
            var model = new BankModel
            {
                Monedas = GetMonedas()
            };
            return View(model);
        }

        public JsonResult GetBanks()
        {
            try
            {
                string query = "SELECT c1 as Clave, c2 as Nombre, c3 as CuentaBancaria, c4 as Moneda, " +
                    "   c5 as NumeroCuenta, c7 as Saldo, c9 as RFC " +
                    "FROM sellosop.kdb1";

                var banks = RunQuery(query);

                return Json(new { data = banks });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Create(BankModel bank)
        {
            var parameters = new Dictionary<string, object>();

            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kdb1 WHERE c1 = @Clave";
                    parameters.Add("Clave", bank.Clave);
                    var bankQty = RunScalar(query, parameters);

                    if (Convert.ToInt32(bankQty) > 0)
                    {
                        return Json(new { success = false, message = "La clave ya existe. Por favor ingrese una clave única." });
                    }

                    parameters.Clear();
                    query = "INSERT INTO sellosop.kdb1 (c1, c2, c3, c4, c5, c7, c9) " +
                        "VALUES (@Clave, @Nombre, @CuentaBancaria, @Moneda, @NumeroCuenta, @Saldo, @RFC)";

                    parameters.Add("Clave", bank.Clave);
                    parameters.Add("Nombre", bank.Nombre);
                    parameters.Add("CuentaBancaria", bank.CuentaBancaria);
                    parameters.Add("Moneda", bank.Moneda);
                    parameters.Add("NumeroCuenta", bank.NumeroCuenta);
                    parameters.Add("Saldo", bank.Saldo);
                    parameters.Add("RFC", bank.RFC);

                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Banco creado exitosamente" });
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
        public JsonResult Edit(BankModel bank)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kdb1 WHERE c1 = @Clave";
                    parameters.Add("Clave", bank.Clave);
                    var bankQty = RunScalar(query, parameters);

                    if (Convert.ToInt32(bankQty) == 0)
                    {
                        return Json(new { success = false, message = "La clave no existe. Por favor ingrese una clave válida." });
                    }

                    query = "UPDATE sellosop.kdb1 SET c2 = @Nombre, c3 = @CuentaBancaria, c4 = @Moneda, " +
                        "c5 = @NumeroCuenta, c7 = @Saldo, c9 = @RFC WHERE c1 = @Clave";
                    parameters.Add("Nombre", bank.Nombre);
                    parameters.Add("CuentaBancaria", bank.CuentaBancaria);
                    parameters.Add("Moneda", bank.Moneda);
                    parameters.Add("NumeroCuenta", bank.NumeroCuenta);
                    parameters.Add("Saldo", bank.Saldo);
                    parameters.Add("RFC", bank.RFC);

                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Banco actualizado exitosamente" });
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
            var parameters = new Dictionary<string, object>()
            {
                {"Clave", clave }
            };

            if (string.IsNullOrEmpty(clave))
            {
                return Json(new { success = false, message = "La clave del banco es requerida" });
            }

            try
            {
                string query = "SELECT COUNT(*) FROM sellosop.kdb1 WHERE c1 = @Clave";
                var bankQty = RunScalar(query, parameters);

                if (Convert.ToInt32(bankQty) == 0)
                {
                    return Json(new { success = false, message = "La clave no existe. Por favor ingrese una clave válida." });
                }

                query = "DELETE FROM sellosop.kdb1 WHERE c1 = @Clave";
                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Banco eliminado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }

        // Método para cargar monedas
        private List<SelectListItem> GetMonedas()
        {
            string query = "SELECT c1 as Moneda, c2 as Descripcion FROM sellosop.kdmy";
            var result = RunQuery(query);

            var monedas = new List<SelectListItem>();

            foreach (var row in result)
            {
                monedas.Add(new SelectListItem
                {
                    Value = row["Moneda"]?.ToString(),
                    Text = row["Descripcion"]?.ToString()
                });
            }

            return monedas;
        }
        public JsonResult GetBankDetails(IFormCollection fc)
        {
            var userId = HttpContext.Session.GetInt32("UsuarioId");
            var parameters = new Dictionary<string, object>()
            {
                { "clave", fc["clave"].ToString() }
            };

            string query = "SELECT c1 as clave, c2 as nombre, c3 as cuentabancaria, c4 moneda, c5 numerocuenta, " +
                "   c7 as saldo, c9 as rfc  FROM sellosop.kdb1 " +
                "WHERE c1 = @clave";
            var bank = RunQuery(query, parameters);

            return Json(bank);
        }
    }
}