// Controllers/IncotermController.cs
using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    public class IncotermController : Utilities
    {
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetIncoterms()
        {
            string query = "SELECT c1 clave, c2 descripcion FROM sellosop.kdfe33ceinco";
            var incoterms = RunQuery(query);
            return Json(new { data = incoterms });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult CreateIncoterm(IncotermModel incoterm)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kdfe33ceinco WHERE c1 = @clave";
                    parameters.Add("clave", incoterm.Clave);
                    var count = RunScalar(query, parameters);

                    if (Convert.ToInt32(count) > 0)
                    {
                        return Json(new { success = false, message = "La clave ya existe." });
                    }

                    query = "INSERT INTO sellosop.kdfe33ceinco (c1, c2) VALUES (@clave, @descripcion)";
                    parameters.Clear();
                    parameters.Add("clave", incoterm.Clave);
                    parameters.Add("descripcion", incoterm.Descripcion);
                    RunUpdate(query, parameters);

                    return Json(new { success = true, message = "Incoterm guardado exitosamente." });
                }
                else
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return Json(new { success = false, message = string.Join("; ", errors) });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult UpdateIncoterm(IncotermModel incoterm)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                if (ModelState.IsValid)
                {
                    string query = "SELECT COUNT(*) FROM sellosop.kdfe33ceinco WHERE c1 = @clave";
                    parameters.Add("clave", incoterm.Clave);
                    var count = RunScalar(query, parameters);

                    if (Convert.ToInt32(count) == 0)
                    {
                        return Json(new { success = false, message = "Incoterm no encontrado." });
                    }

                    query = "UPDATE sellosop.kdfe33ceinco SET c2 = @descripcion WHERE c1 = @clave";
                    parameters.Clear();
                    parameters.Add("clave", incoterm.Clave);
                    parameters.Add("descripcion", incoterm.Descripcion);
                    RunUpdate(query, parameters);
                    return Json(new { success = true, message = "Incoterm actualizado exitosamente." });
                }
                else
                {
                    return Json(new { success = false, message = "Datos inválidos." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al actualizar: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult DeleteIncoterm(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "ID no puede estar vacío." });
            }

            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "SELECT COUNT(*) FROM sellosop.kdfe33ceinco WHERE c1 = @clave";
                parameters.Add("clave", id);
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "Incoterm no encontrado." });
                }

                query = "DELETE FROM sellosop.kdfe33ceinco WHERE c1 = @clave";
                parameters.Clear();
                parameters.Add("@clave", id);
                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Incoterm eliminado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult GetIncotermDetails(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "ID no puede estar vacío." });
            }

            var parameters = new Dictionary<string, object>();

            string query = "SELECT c1 clave, c2 descripcion FROM sellosop.kdfe33ceinco WHERE c1 = @clave";
            parameters.Add("clave", id);

            var incoterm = RunQuery(query, parameters)[0];

            return Json(new { success = true, incoterm });
        }
    }
}