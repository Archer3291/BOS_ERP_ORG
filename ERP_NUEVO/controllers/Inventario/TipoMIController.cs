using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers.Inventario
{
    public class TipoMIController : Utilities
    {
        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryRoles = "SELECT id_rol_tkt,nombre FROM tkt_roles  ORDER BY nombre DESC";


            result.Add("roles", RunQuery(queryRoles));


            return Json(result);
        }

        [HttpGet]
        [Authorize]
        public JsonResult Movimientos()
        {
            try
            {
                string movimientos = "SELECT id, nombre, signo, descripcion FROM t_tipos_movimiento;";
                var movimientosResult = RunQuery(movimientos);

                var movimiento = movimientosResult.Select(t => new
                {
                    Id = t["id"],
                    Nombre = t["nombre"],
                    Signo = t["signo"],
                    Descripcion = t["descripcion"]
                }).ToList();

                return Json(new { data = movimiento });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al obtener los tipos de movimiento: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Editar()
        {
            try
            {
                var idStr = Request.Form["Id"].ToString();
                var nombre = Request.Form["Nombre"].ToString();
                var signo = Request.Form["Signo"].ToString();
                var descripcion = Request.Form["Descripcion"].ToString();

                if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(signo) || string.IsNullOrWhiteSpace(idStr))
                {
                    return Json(new { success = false, message = "Datos inválidos." });
                }

                int id = int.Parse(idStr);

                var parameters = new Dictionary<string, object>
        {
            { "id", id },
            { "nombre", nombre },
            { "signo", int.Parse(signo) },
            { "descripcion", descripcion.ToString() ?? "" }
        };

                string query = @"UPDATE t_tipos_movimiento
                         SET nombre = @nombre, signo = @signo, descripcion = @descripcion
                         WHERE id = @id;";

                RunUpdate(query, parameters);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public JsonResult Eliminar(UsuarioDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.NombreUsuario))
            {
                return Json(new { success = false, message = "Usuario inválido." });
            }

            try
            {
                // TODO: Eliminar usuario por dto.NombreUsuario

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }




    }
}