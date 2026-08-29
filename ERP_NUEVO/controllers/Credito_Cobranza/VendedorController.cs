using System.Data;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    public class VendedorController : Utilities
    {
        public JsonResult GetSucursales()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT id_sucursal, cve_sucursal, descripcion FROM catsucursales WHERE empresa_id = @empresa";
            parameters.Add("empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var sucursales = RunQuery(query, parameters);
            return Json(sucursales);
        }

        public JsonResult GetVendedores(IFormCollection fc)
        {
            int? page = GetInt(fc["page"].ToString(), 1);
            int? pageSize = GetInt(fc["pageSize"].ToString(), 10);
            string nombre = GetString(fc["nombre"].ToString());
            string where = "";

            var parameters = new Dictionary<string, object>
                {
                    { "offset",     (page - 1) * pageSize },
                    { "pageSize",   pageSize }
                };

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                parameters.Add("nombre", nombre);

                where += " AND (" +
                    "   v.clave_vendedor ILIKE '%' || @nombre || '%' OR " +
                    "   v.nombre ILIKE '%' || @nombre || '%' OR " +
                    "   v.telefono1 ILIKE '%' || @nombre || '%' " +
                    ") ";
            }

            string query = "SELECT v.id, v.clave_vendedor, v.nombre, v.comision1, v.comision2, v.telefono1, v.es_cobrador, v.activo, v.sucursal_id, v.correo1, v.correo2 " +
                "FROM vendedores v " +
                $"WHERE es_cobrador = false {where} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var vendedores = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM vendedores v " +
                $"WHERE es_cobrador = false {where}";
            var total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new { data = vendedores, total });
        }
        
        public JsonResult GetCobradores(IFormCollection fc)
        {
            int? page = GetInt(fc["page"].ToString(), 1);
            int? pageSize = GetInt(fc["pageSize"].ToString(), 10);
            string nombre = GetString(fc["nombre"].ToString());
            string where = "";

            var parameters = new Dictionary<string, object>
                {
                    { "offset",     (page - 1) * pageSize },
                    { "pageSize",   pageSize }
                };

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                parameters.Add("nombre", nombre);

                where += " AND (" +
                    "   v.clave_vendedor ILIKE '%' || @nombre || '%' OR " +
                    "   v.nombre ILIKE '%' || @nombre || '%' OR " +
                    "   v.telefono1 ILIKE '%' || @nombre || '%' " +
                    ") ";
            }

            string query = "SELECT v.id, v.clave_vendedor, v.nombre, v.comision1, v.comision2, v.telefono1, v.es_cobrador, v.activo, v.sucursal_id, v.correo1, v.correo2 " +
                "FROM vendedores v " +
                $"WHERE es_cobrador = true {where} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var vendedores = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM vendedores v " +
                $"WHERE es_cobrador = true {where}";
            var total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new { data = vendedores, total });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SaveVendedor(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                // Verificar si el vendedor ya existe
                if (string.IsNullOrEmpty(fc["clave_vendedor"].ToString()) || fc["clave_vendedor"].ToString().Length > 5)
                {
                    return Json(new { icon = "error", title = "La clave del vendedor debe tener entre 1 y 5 caracteres." });
                }

                string queryCheck = "SELECT COUNT(1) FROM vendedores WHERE clave_vendedor = @clave_vendedor";
                parameters.Add("clave_vendedor", fc["clave_vendedor"].ToString());
                var result = RunScalar(queryCheck, parameters);

                if (result == null || Convert.ToInt32(result) > 0)
                {
                    return Json(new { icon = "error", title = "Ya existe un vendedor con esta clave." });
                }
                // Preparar la consulta de inserción/actualización
                parameters = new Dictionary<string, object>();
                string queryInsert = "INSERT INTO vendedores " +
                    "(sucursal_id, clave_vendedor, nombre, comision1, comision2, telefono1, telefono2, correo1, correo2, es_cobrador, fecha_creacion, activo) " +
                    "VALUES " +
                    "(@sucursal_id, @clave_vendedor, @nombre, @comision1, @comision2, @telefono1, @telefono2, @correo1, @correo2, @es_cobrador, now(), @activo)";

                parameters.Add("sucursal_id", GetInt(fc["sucursal_id"].ToString()));
                parameters.Add("clave_vendedor", fc["clave_vendedor"].ToString());
                parameters.Add("nombre", GetString(fc["nombre"].ToString()));
                parameters.Add("comision1", GetInt(fc["comision1"].ToString()));
                parameters.Add("comision2", GetInt(fc["comision2"].ToString()));
                parameters.Add("telefono1", GetString(fc["telefono1"].ToString()));
                parameters.Add("telefono2", GetString(fc["telefono2"].ToString()));
                parameters.Add("correo1", GetString(fc["correo1"].ToString()));
                parameters.Add("correo2", GetString(fc["correo2"].ToString()));
                parameters.Add("es_cobrador", GetBool(fc["es_cobrador"].ToString()));
                parameters.Add("activo", GetBool(fc["activo"].ToString()));

                RunUpdate(queryInsert, parameters);
                return Json(new { icon = "success", title = "Vendedor guardado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error al guardar: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult editarVendedor(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT COUNT(1) FROM vendedores WHERE id = @id";
            parameters.Add("id", GetInt(fc["id"].ToString()));
            var result = RunScalar(query, parameters);

            if (result == null || Convert.ToInt32(result) == 0)
            {
                return Json(new { icon = "error", title = "Vendedor no encontrado." });
            }

            parameters = new Dictionary<string, object>();

            query = "UPDATE vendedores SET sucursal_id = @sucursal_id, nombre = @nombre, comision1 = @comision1, comision2 = @comision2, " +
                "   telefono1 = @telefono1, telefono2 = @telefono2, correo1 = @correo1, correo2 = @correo2, es_cobrador = @es_cobrador, activo = @activo " +
                "WHERE id = @id";
            parameters.Add("sucursal_id", GetInt(fc["sucursal_id"].ToString()));
            parameters.Add("id", GetInt(fc["id"].ToString()));
            parameters.Add("nombre", GetString(fc["nombre"].ToString()));
            parameters.Add("comision1", GetDecimal(fc["comision1"].ToString()));
            parameters.Add("comision2", GetDecimal(fc["comision2"].ToString()));
            parameters.Add("telefono1", GetString(fc["telefono1"].ToString()));
            parameters.Add("telefono2", GetString(fc["telefono2"].ToString()));
            parameters.Add("correo1", GetString(fc["correo1"].ToString()));
            parameters.Add("correo2", GetString(fc["correo2"].ToString()));
            parameters.Add("es_cobrador", GetBool(fc["es_cobrador"].ToString()));
            parameters.Add("activo", GetBool(fc["activo"].ToString()));

            try
            {
                RunUpdate(query, parameters);
                return Json(new { icon = "success", title = "Vendedor actualizado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error al actualizar: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult DesactivarVendedor(int? id, bool? status)
        {
            if (!id.HasValue || id <= 0)
            {
                return Json(new { icon = "error", title = "No se encontro un usuario válido." });
            }
            
            if (!status.HasValue)
            {
                return Json(new { icon = "error", title = "ID del vendedor inválido." });
            }

            var parameters = new Dictionary<string, object>();
            try
            {
                parameters.Add("id", id);
                parameters.Add("status", status);
                string query = "SELECT COUNT(*) FROM vendedores WHERE id = @id";
                int qty = Convert.ToInt32(RunScalar(query, parameters));

                if (qty <= 0)
                {
                    return Json(new { icon = "error", title = "El vendedor no existe" });
                }

                query = "UPDATE vendedores SET activo = @status WHERE id = @id";
                RunUpdate(query, parameters);

                return Json(new { icon = "success", title = "Vendedor actualizado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error al actualizar: " + ex.Message });
            }
        }
    }
}