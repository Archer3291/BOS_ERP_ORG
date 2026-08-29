using Newtonsoft.Json;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers.Documento
{
    public class TipoDocumentoController : Utilities
    {
        [HttpGet]
        [Authorize]
        public JsonResult ObtenerTipoDocumentos()
        {
            try
            {
                string tipoQuery = "SELECT " +
                                       "idtpdoc, " +
                                       "idarea, " +
                                       "tpdoc, " +
                                       "abreviaturatpdoc, " +
                                       "consec, " +
                                       "descr, " +
                                       "fch " +
                                   "FROM tpdoc;";

                var resultTipos = RunQuery(tipoQuery);

                var tiposDocumento = resultTipos.Select(t => new
                {
                    Id = t["idtpdoc"],
                    Area = t["idarea"],
                    Tipo = t["tpdoc"],
                    Abreviatura = t["abreviaturatpdoc"],
                    Consecutivo = t["consec"],
                    Descripcion = t["descr"],
                    Fecha = t["fch"]
                }).ToList();

                return Json(new { data = tiposDocumento });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al obtener los tipos de documentos: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Crear()
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                int idarea = int.Parse(Request.Form["idarea"].ToString());
                string tpdoc = Request.Form["tpdoc"].ToString();
                string abreviatura = Request.Form["abreviaturatpdoc"].ToString();
                int  consec = int.Parse(Request.Form["consec"].ToString());
                string descr = Request.Form["descr"].ToString();
                string fch = Request.Form["fch"].ToString();

                string crear = @"INSERT INTO tpdoc (idarea, tpdoc, abreviaturatpdoc, consec, descr, fch)
                         VALUES (@idarea, @tpdoc, @abreviaturatpdoc, @consec, @descr, @fch)";

                parameters.Add("@idarea", idarea);
                parameters.Add("@tpdoc", tpdoc);
                parameters.Add("@abreviaturatpdoc", abreviatura);
                parameters.Add("@consec", consec);
                parameters.Add("@descr", descr);
                parameters.Add("@fch", string.IsNullOrEmpty(fch) ? (object)DBNull.Value : DateTime.Parse(fch));

                RunUpdate(crear, parameters);

                return Json(new { success = true, message = "Documento creado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public JsonResult Eliminar()
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                int idtpdoc = int.Parse(Request.Form["idtpdoc"].ToString());

                string eliminar = "DELETE FROM tpdoc WHERE idtpdoc = @idtpdoc";
                parameters.Add("@idtpdoc", idtpdoc);

                RunUpdate(eliminar, parameters);

                return Json(new { success = true, message = "Documento eliminado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public JsonResult Editar()
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                int idtpdoc = int.Parse(Request.Form["idtpdoc"].ToString());
                int idarea = int.Parse(Request.Form["idarea"].ToString());
                string tpdoc = Request.Form["tpdoc"].ToString();
                string abreviatura = Request.Form["abreviaturatpdoc"].ToString();
                int consec = int.Parse(Request.Form["consec"].ToString());
                string descr = Request.Form["descr"].ToString();
                string fch = Request.Form["fch"].ToString();


                var updateQuery = "UPDATE tpdoc " +
                                  "SET idarea = @idarea, " +
                                  "    tpdoc = @tpdoc, " +
                                  "    abreviaturatpdoc = @abreviaturatpdoc, " +
                                  "    consec = @consec, " +
                                  "    descr = @descr, " +
                                  "    fch = @fch " +
                                  "WHERE idtpdoc = @idtpdoc";


                parameters.Add("@idtpdoc", idtpdoc);
                parameters.Add("@idarea", idarea);
                parameters.Add("@tpdoc", tpdoc);
                parameters.Add("@abreviaturatpdoc", abreviatura);
                parameters.Add("@consec", consec);
                parameters.Add("@descr", descr);
                parameters.Add("@fch", string.IsNullOrEmpty(fch) ? (object)DBNull.Value : DateTime.Parse(fch));


                RunUpdate(updateQuery, parameters);
                return Json(new { success = true, message = "Documento actualizado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        public JsonResult GuardarCotizacion(IFormCollection form)
        {
            try
            {
                string cliente = form["cliente"];
                string fecha = form["fecha"];
                string moneda = form["moneda"];

                string partidasJson = form["partidas"];
                var partidas = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(partidasJson);

                // Procesar datos
                foreach (var partida in partidas)
                {
                    string articulo = partida.ContainsKey("articulo") ? partida["articulo"] : "";
                    // ...
                }

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = "Error al procesar los datos: " + ex.Message });
            }
        }

    }

}