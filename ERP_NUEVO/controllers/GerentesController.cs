using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public class GerentesController : Utilities
    {
        public IActionResult GestionUsuarios()
        {
            return View();
        }

        public JsonResult GetUsuarios()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT usuarioid, nombre, apellido, email, telefono, nombreusuario, activo " +
                "FROM usuarios " +
                "WHERE areaid = @area AND sucursal_id = @sucursal " +
                "ORDER BY activo DESC";
            parameters.Add("area", GetAreaID(User.Identity.Name));
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            var usuarios = RunQuery(query, parameters);
            return Json(usuarios);
        }

        public JsonResult ActivarUsuario(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE usuarios SET activo = true WHERE usuarioid = @usuario";
                parameters.Add("usuario", Convert.ToInt32(fc["usuarioid"].ToString()));
                RunUpdate(query, parameters);
                return Json(new { icon = "success", title = "Usuario activado correctamente." });
            } catch (Exception ex)
            {
                return Json(new { icon = "success", title = "Ocurrio un error.", html = ex.Message });
            }
        }
        
        public JsonResult DesactivarUsuario(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE usuarios SET activo = false WHERE usuarioid = @usuario";
                parameters.Add("usuario", Convert.ToInt32(fc["usuarioid"].ToString()));
                RunUpdate(query, parameters);
                return Json(new { icon = "success", title = "Usuario desactivado correctamente." });
            } catch (Exception ex)
            {
                return Json(new { icon = "success", title = "Ocurrio un error.", html = ex.Message });
            }
        }
    }
}