using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BOS_ERP.Controllers
{
    public class TesoreriaController : Utilities
    {
        // GET: Tesoreria
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Bancos()
        {
            var model = new BankModel
            {
                Monedas = GetMonedas()
            };
            return View(model);
        }

        public IActionResult BancosSAT()
        {
            return View();
        }

        private List<SelectListItem> GetMonedas()
        {
            string query = "SELECT c1 as Moneda, c2 as Descripcion FROM sellosop.kdmy";
            var result = RunQuery(query);
            var monedas = new List<SelectListItem>();

            foreach (var row in result)
            {
                monedas.Add(new SelectListItem
                {
                    Value = row["moneda"].ToString(),
                    Text = row["descripcion"].ToString()
                });
            }

            return monedas;
        }
    }
}