using Newtonsoft.Json.Linq;
using BOS_ERP.Controllers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Services
{
    public class ParidadService : Utilities
    {
        // ============================
        // MÉTODO PRINCIPAL
        // ============================
        public async Task<JsonResult> Cargar()
        {
            // Fecha fiscal SAT (día inmediato anterior hábil)
            DateTime fechaFiscal = ObtenerFechaFiscalSAT();

            // Fecha publicación Banxico (un día antes)
            DateTime fechaPublicacion = fechaFiscal;

            string fechaInicio = fechaPublicacion.AddDays(-7).ToString("yyyy-MM-dd");
            string fechaFin = fechaPublicacion.ToString("yyyy-MM-dd");

            string seriesIds = "SF60653,SF46410"; // USD DOF, EUR
            string apiUrl =
                $"https://www.banxico.org.mx/SieAPIRest/service/v1/series/{seriesIds}/datos/{fechaInicio}/{fechaFin}";

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add(
                    "Bmx-Token",
                    "c687f4a34e645b5fefd04cea241ac65b3445330d06e21fb07849c57da03cf85d"
                );

                try
                {
                    var response = await httpClient.GetAsync(apiUrl);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);

                    var series = obj["bmx"]?["series"];
                    if (series == null || !series.Any())
                    {
                        return Json(new
                        {
                            success = false,
                            message = "No se obtuvo información de Banxico."
                        });
                    }

                    decimal usd = 0;
                    decimal eur = 0;
                    bool usdOk = false;
                    bool eurOk = false;

                    foreach (var serie in series)
                    {
                        string idSerie = serie["idSerie"]?.ToString();
                        var datos = serie["datos"];

                        if (datos == null || !datos.Any())
                            continue;

                        // ============================
                        // USD — EXACTO DOF (SAT)
                        // ============================
                        if (idSerie == "SF60653")
                        {
                            var datoUsd = datos
                                .Where(d => d["dato"]?.ToString() != "N/E")
                                .OrderByDescending(d => DateTime.ParseExact(
                                    d["fecha"].ToString(),
                                    "dd/MM/yyyy",
                                    CultureInfo.InvariantCulture))
                                .FirstOrDefault();

                            if (datoUsd != null &&
                                decimal.TryParse(
                                    datoUsd["dato"].ToString(),
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture,
                                    out usd))
                            {
                                usdOk = true;
                            }
                        }

                        // ============================
                        // EUR — ÚLTIMO DISPONIBLE
                        // ============================
                        if (idSerie == "SF46410")
                        {
                            var datoEur = datos
                                .FirstOrDefault(d => d["dato"]?.ToString() != "N/E");

                            if (datoEur != null &&
                                decimal.TryParse(
                                    datoEur["dato"].ToString(),
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture,
                                    out eur))
                            {
                                eurOk = true;
                            }
                        }
                    }

                    // ============================
                    // VALIDACIÓN FINAL
                    // ============================
                    if (!usdOk)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "No se pudo obtener el tipo de cambio USD DOF requerido por el SAT."
                        });
                    }

                    // Si EUR no existe, usar último guardado
                    if (!eurOk)
                    {
                        eur = ObtenerUltimoEuroDesdeBD();
                    }

                    GuardarTasaCambio(fechaFiscal, usd, eur, 1);

                    return Json(new
                    {
                        success = true,
                        fechaFiscal = fechaFiscal.ToString("yyyy-MM-dd"),
                        dolar = usd.ToString("F4"),
                        euro = eur.ToString("F4"),
                        peso = "1.0000",
                        message = "Paridades cargadas correctamente conforme a DOF / SAT."
                    });
                }
                catch (Exception ex)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Error al consultar Banxico: " + ex.Message
                    });
                }
            }
        }

        // ============================
        // FECHA FISCAL SAT
        // ============================
        private DateTime ObtenerFechaFiscalSAT()
        {
            DateTime fecha = DateTime.Today;

            while (fecha.DayOfWeek == DayOfWeek.Saturday ||
                   fecha.DayOfWeek == DayOfWeek.Sunday)
            {
                fecha = fecha.AddDays(-1);
            }

            return fecha;
        }

        // ============================
        // GUARDAR EN BD
        // ============================
        private void GuardarTasaCambio(DateTime fecha, decimal dolar, decimal euro, decimal peso)
        {
            string checkSql =
                "SELECT COUNT(*) AS total FROM tasas_cambio WHERE fecha = @fecha";

            var parametrosCheck = new Dictionary<string, object>
            {
                ["@fecha"] = fecha.Date
            };

            var resultado = RunQuery(checkSql, parametrosCheck);
            int count = Convert.ToInt32(resultado[0]["total"]);

            if (count > 0)
                return;

            string insertSql =
                "INSERT INTO tasas_cambio (fecha, dolar, euro, peso) " +
                "VALUES (@fecha, @dolar, @euro, @peso)";

            var parametrosInsert = new Dictionary<string, object>
            {
                ["@fecha"] = fecha.Date,
                ["@dolar"] = dolar,
                ["@euro"] = euro,
                ["@peso"] = peso
            };

            RunQuery(insertSql, parametrosInsert);
        }

        // ============================
        // ÚLTIMO EURO EN BD
        // ============================
        private decimal ObtenerUltimoEuroDesdeBD()
        {
            string sql =
                "SELECT TOP 1 euro FROM tasas_cambio " +
                "WHERE euro > 0 ORDER BY fecha DESC";

            var result = RunQuery(sql);

            if (result.Any())
                return Convert.ToDecimal(result[0]["euro"]);

            return 0;
        }
    }
}
