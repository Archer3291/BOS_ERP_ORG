using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers
{
    public partial class ContabilidadController : Utilities
    {
        public JsonResult GetDepartments(IFormCollection fc)
        {
            int page = Convert.ToInt32(fc["page"].ToString());
            int pageSize = Convert.ToInt32(fc["pageSize"].ToString());
            var parameters = new Dictionary<string, object>();
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string where = "";

            if (!string.IsNullOrWhiteSpace(fc["nombre"].ToString()))
            {
                parameters.Add("nombre", $"%{fc["nombre"].ToString()}%");
                where = " AND (descripcion ILIKE @nombre OR nombre ILIKE @nombre) ";
            }

            string query = "SELECT areaid, nombre, descripcion, abreviatura " +
                "FROM areas " +
                $"WHERE 1=1 {where} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM areas";
            var total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new { data, total });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult ActualizarDepartamento(DepartmentModel department)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string saveQuery = "UPDATE areas SET nombre = @nombre, descripcion = @descripcion, abreviatura = @abreviatura WHERE areaid = @id";
                parameters.Add("nombre", department.nombre);
                parameters.Add("descripcion", department.descripcion);
                parameters.Add("abreviatura", department.abreviatura);
                parameters.Add("id", department.id);

                var result = RunQuery(saveQuery, parameters);

                return Json(new { success = true, message = "Departamento guardado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar: " + ex.Message });
            }
        }
        
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult CrearDepartamento(DepartmentModel department)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string saveQuery = "INSERT INTO areas (nombre, descripcion, abreviatura) " +
                    "VALUES (@nombre, @descripcion, @abreviatura)";
                parameters.Add("nombre", department.nombre);
                parameters.Add("descripcion", department.descripcion);
                parameters.Add("abreviatura", department.abreviatura);

                var result = RunQuery(saveQuery, parameters);

                return Json(new { success = true, message = "Departamento guardado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar: " + ex.Message });
            }
        }

        public JsonResult GetDepartmentById(int id)
        {
            DepartmentModel department = new DepartmentModel();

            var parameters = new Dictionary<string, object>();
            string departmentIdQuery = "SELECT nombre, descripcion, abreviatura FROM areas WHERE areaid = @id";
            parameters.Add("id", id);
            var rdr = RunQuery(departmentIdQuery, parameters);
            if (rdr.Count > 0)
            {
                var row = rdr[0]; // el primer resultado

                department.nombre = row["nombre"]?.ToString();
                department.descripcion = row["descripcion"]?.ToString();
                department.abreviatura = row["abreviatura"]?.ToString();
            }

            return Json(department);
        }
    }
}