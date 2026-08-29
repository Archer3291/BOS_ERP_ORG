using Newtonsoft.Json.Linq;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Sistemas
{
    public class ParidadesController : Utilities
    {
        //[Route("Almacen/Tipos/Datos")]
        //[HttpGet]
        //public IActionResult BuscarSucursales()
        //{
        //    var returnResult = new Dictionary<string, List<Dictionary<string, object>>>();
        //    string queryTipos = "SELECT c1,c2 FROM sellosop.kdie";
        //    var result = RunQuery(queryTipos);
        //    returnResult.Add("tipos", result);
        //    return Json(returnResult);
        //}
        //public IActionResult Index()
        //{
        //    var tasas = ObtenerTasas();
        //    return View(tasas);
        //}
        [Route("Sistemas/Paridad/Cargar")]
        [HttpPost]
        public async Task<JsonResult> Cargar()
        {
            DateTime fechaAnterior = ObtenerUltimoDiaHabil();
            string fechaStr = fechaAnterior.ToString("yyyy-MM-dd");

            string seriesIds = "SF43718,SF46410"; // Dólar, Euro
            string apiUrl = $"https://www.banxico.org.mx/SieAPIRest/service/v1/series/{seriesIds}/datos/{fechaStr}/{fechaStr}";

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Bmx-Token", "c687f4a34e645b5fefd04cea241ac65b3445330d06e21fb07849c57da03cf85d");

                try
                {
                    var response = await httpClient.GetAsync(apiUrl);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);

                    var series = obj["bmx"]?["series"];
                    if (series == null || series.Count() < 2)
                        return Json(new { success = false, message = "No se encontraron ambas series en la respuesta." });

                    decimal usdToMxn = 0, eurToMxn = 0;
                    bool dolarOk = false, euroOk = false;

                    foreach (var serie in series)
                    {
                        string idSerie = serie["idSerie"]?.ToString();
                        string dato = serie["datos"]?.FirstOrDefault()?["dato"]?.ToString();

                        if (string.IsNullOrEmpty(dato))
                            continue;

                        if (idSerie == "SF43718" && decimal.TryParse(dato, NumberStyles.Any, CultureInfo.InvariantCulture, out usdToMxn))
                            dolarOk = true;

                        if (idSerie == "SF46410" && decimal.TryParse(dato, NumberStyles.Any, CultureInfo.InvariantCulture, out eurToMxn))
                            euroOk = true;
                    }

                    if (!dolarOk || !euroOk)
                    {
                        return Json(new { success = false, message = "No se pudieron obtener ambas tasas correctamente." });
                    }

                    GuardarTasaCambio(fechaAnterior, usdToMxn, eurToMxn, 1);

                    return Json(new
                    {
                        success = true,
                        fecha = fechaStr,
                        dolar = usdToMxn.ToString("F4"),
                        euro = eurToMxn.ToString("F4"),
                        peso = "1.0000",
                        message = $"Tasa del día {fechaStr} guardada correctamente."
                    });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error en la solicitud: " + ex.Message });
                }
            }
        }



        private DateTime ObtenerUltimoDiaHabil()
        {
            DateTime fecha = DateTime.Today.AddDays(-1);

            while (fecha.DayOfWeek == DayOfWeek.Saturday ||
                   fecha.DayOfWeek == DayOfWeek.Sunday)
            {
                fecha = fecha.AddDays(-1);
            }

            return fecha;
        }



        private void GuardarTasaCambio(DateTime fecha, decimal dolar, decimal euro, decimal peso)
        {
            string checkSql = "SELECT COUNT(*) AS total FROM tasas_cambio WHERE fecha = @fecha";
            var parametrosCheck = new Dictionary<string, object>
            {
                ["@fecha"] = fecha.Date
            };

            var resultado = RunQuery(checkSql, parametrosCheck);
            int count = Convert.ToInt32(resultado[0]["total"]);
            if (count > 0) return;

            string insertSql = "INSERT INTO tasas_cambio (fecha, dolar, euro, peso) VALUES (@fecha, @dolar, @euro, @peso)";
            var parametrosInsert = new Dictionary<string, object>
            {
                ["@fecha"] = fecha,
                ["@dolar"] = dolar,
                ["@euro"] = euro,
                ["@peso"] = peso
            };

            RunQuery(insertSql, parametrosInsert);
        }

        private List<TasaCambio> ObtenerTasas()
        {
            var lista = new List<TasaCambio>();
            var sql = "SELECT fecha, dolar, euro, peso FROM tasas_cambio ORDER BY fecha DESC";
            var resultados = RunQuery(sql);

            foreach (var fila in resultados)
            {
                var tasa = new TasaCambio
                {
                    Fecha = Convert.ToDateTime(fila["fecha"]),
                    Dolar = Convert.ToDecimal(fila["dolar"]),
                    Euro = Convert.ToDecimal(fila["euro"]),
                    Peso = Convert.ToDecimal(fila["peso"])
                };
                lista.Add(tasa);
            }

            return lista;
        }

        [HttpGet]
        [Route("Sistemas/Paridad/ObtenerTasasJson")]
        public JsonResult ObtenerTasasJson()
        {
            var tasas = ObtenerTasas();

            var data = tasas.Select(t => new
            {
                Fecha = t.Fecha.ToString("yyyy-MM-dd"), //Formato ISO compatible con JS
                Dolar = t.Dolar,
                Euro = t.Euro,
                Peso = t.Peso
            });

            return Json(new { data = data });
        }


    }
}