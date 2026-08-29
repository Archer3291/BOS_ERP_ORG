using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.controllers;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers
{
    public partial class ContabilidadController : Utilities
    {
        public JsonResult GetConcepts()
        {
            try
            {
                string query = "SELECT c1 as Clave, c2 as Descripcion, c3 as NumeroCuenta, c4 as Activo FROM sellosop.kdco";
                var concepts = RunQuery(query);


                return Json(new { data = concepts });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Create(ConceptModel concept)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("Clave", concept.Clave);
            parameters.Add("Descripcion", concept.Descripcion);
            parameters.Add("NumeroCuenta", concept.NumeroCuenta);
            try
            {
                string query = "INSERT INTO sellosop.kdco (c1, c2, c3) VALUES (@Clave, @Descripcion, @NumeroCuenta)";
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Concepto creado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Edit(ConceptModel concept)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "UPDATE sellosop.kdco SET c2 = @Descripcion, c3 = @NumeroCuenta WHERE c1 = @Clave";
                parameters.Add("Clave", concept.Clave);
                parameters.Add("Descripcion", concept.Descripcion);
                parameters.Add("NumeroCuenta", concept.NumeroCuenta);
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Concepto actualizado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Delete(string clave)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "UPDATE sellosop.kdco SET c4 = false WHERE c1 = @Clave";
                parameters.Add("Clave", clave);
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Concepto desactivado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error general: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Reactivate(string clave)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "UPDATE sellosop.kdco SET c4 = true WHERE c1 = @Clave";
                parameters.Add("Clave", clave);
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Concepto reactivado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al reactivar: " + ex.Message });
            }
        }

    }
}