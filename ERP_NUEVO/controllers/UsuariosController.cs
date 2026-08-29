// Controllers/UsuariosController.cs
using BOS_ERP.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Data.Entity;

namespace BOS_ERP.Controllers
{
    [RightAuthorize("sistemas")]
    [AreaAuthorize("Sistemas")]
    public class UsuariosController : Utilities
    {
        private readonly FacturacionDbContext db;
        private readonly PasswordHasher<object> passwordHasher;

        public UsuariosController(FacturacionDbContext context)
        {
            db = context;
            passwordHasher = new PasswordHasher<object>();
        }


        public IActionResult Usuarios()
        {
            var roles = db.Roles.ToList(); // Asegúrate de tener acceso a la tabla Roles
            var empresas = db.Empresas.ToList();
            var permisos = db.Permisos.ToList();
            ViewBag.Roles = new SelectList(roles, "RolId", "Nombre");
            ViewBag.Empresas = new SelectList(empresas, "EmpresaId", "Nombre");
            ViewBag.Permisos = new SelectList(permisos, "Id_permiso", "Descripcion");
            ViewBag.PermisosRaw = permisos;

            return View();
        }

        // Obtener usuarios para DataTable
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetUsuarios(string nombre, string sortColumn, string sortDir, int page = 1, int pageSize = 50)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 50 : pageSize;

            var query = db.Usuarios
                .Include(u => u.Rol)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                nombre = nombre.Trim().ToLower();

                query = query.Where(u =>
                    u.NombreUsuario.ToLower().Contains(nombre) ||
                    u.Nombre.ToLower().Contains(nombre) ||
                    u.Apellido.ToLower().Contains(nombre) ||
                    u.Email.ToLower().Contains(nombre) ||
                    u.Rol.Nombre.ToLower().Contains(nombre)
                );
            }

            var total = query.Count();

            query = (sortColumn, sortDir?.ToLower()) switch
            {
                ("nombreusuario", "desc") => query.OrderByDescending(u => u.NombreUsuario),
                ("nombreusuario", _) => query.OrderBy(u => u.NombreUsuario),

                ("nombre", "desc") => query.OrderByDescending(u => u.Nombre),
                ("nombre", _) => query.OrderBy(u => u.Nombre),

                ("apellido", "desc") => query.OrderByDescending(u => u.Apellido),
                ("apellido", _) => query.OrderBy(u => u.Apellido),

                ("email", "desc") => query.OrderByDescending(u => u.Email),
                ("email", _) => query.OrderBy(u => u.Email),

                ("activo", "desc") => query.OrderByDescending(u => u.Activo),
                ("activo", _) => query.OrderBy(u => u.Activo),

                ("rol", "desc") => query.OrderByDescending(u => u.Rol.Nombre),
                ("rol", _) => query.OrderBy(u => u.Rol.Nombre),

                _ => query.OrderBy(u => u.UsuarioId)
            };

            var data = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.UsuarioId,
                    u.NombreUsuario,
                    u.Nombre,
                    u.Apellido,
                    u.Email,
                    u.Activo,
                    Rol = new
                    {
                        u.Rol.RolId,
                        u.Rol.Nombre
                    }
                })
                .ToList();

            return Json(new
            {
                data,
                total
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Details(IFormCollection fc)
        {
            var returnResult = new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>();

            string query = "SELECT usuarioid, nombre, apellido, email, nombreusuario, telefono, activo, rolid, empresaid, sucursal_id, areaid " +
                "FROM usuarios WHERE usuarioid = @id";
            parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));
            var usuario = RunQuery(query, parameters)[0];

            returnResult.Add("usuario", usuario);

            query = "SELECT pu.id_permiso_usuario, pu.usuario_id, pu.permiso_id " +
                "FROM permisos_usuario pu " +
                "WHERE pu.usuario_id = @id";

            var permisos = RunQuery(query, parameters);

            returnResult.Add("permisos", permisos);

            query = "SELECT id_sucursal, cve_sucursal, descripcion, empresa_id " +
                "FROM catsucursales ";
            var sucursales = RunQuery(query);

            returnResult.Add("sucursales", sucursales);

            return Json(returnResult);
        }

        // POST: Usuarios/Create
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(Usuario usuario)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "Nombre", usuario.Nombre },
                    { "Apellido", usuario.Apellido },
                    { "Email", usuario.Email },
                    { "NombreUsuario", usuario.NombreUsuario },
                    { "Contrasena", passwordHasher.HashPassword(User.Identity.Name, usuario.Contrasena) },
                    { "Telefono", usuario.Telefono },
                    { "Activo", usuario.Activo },
                    { "RolId", usuario.RolId },
                    { "Empresa",       usuario.EmpresaId },
                    { "SucursalId",    usuario.SucursalId },
                    { "Area",          usuario.AreaId }
                };

                string query = "SELECT COUNT(*) FROM usuarios WHERE nombreusuario = @NombreUsuario OR email = @Email";
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) > 0)
                {
                    return Json(new { success = false, message = "Ya existe un usuario con ese nombre de usuario o email" });
                }

                // Inserta el nuevo usuario en la base de datos
                query = "INSERT INTO usuarios (nombre, apellido, email, nombreusuario, contrasena, telefono, activo, rolid, empresaid, sucursal_id, areaid) " +
                    "VALUES (@Nombre, @Apellido, @Email, @NombreUsuario, @Contrasena, @Telefono, @Activo, @RolId, @Empresa, @SucursalId, @Area) " +
                    "RETURNING usuarioid";
                var usuarioId = RunScalar(query, parameters);

                query = "INSERT INTO permisos_usuario (permiso_id, usuario_id) VALUES (@permiso, @UsuarioId)";
                var _parameters = new List<Dictionary<string, object>>();
                foreach (var permiso in usuario.Permisos)
                {
                    var permisoParams = new Dictionary<string, object>
                    {
                        { "UsuarioId", usuarioId },
                        { "permiso", permiso }
                    };

                    _parameters.Add(permisoParams);
                }
                RunUpdate(query, _parameters);

                return Json(new { success = true, message = "Usuario creado exitosamente" });
            }
            catch (Exception ex)
            {
                // Log del error (implementa tu propio sistema de logging)
                System.Diagnostics.Debug.WriteLine($"Error al crear usuario: {ex.Message}");

                return Json(new
                {
                    success = false,
                    message = "Error interno al crear el usuario",
                    error = ex.Message
                });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Edit(UsuarioEditVM usuario)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "UsuarioId", usuario.UsuarioId },
                    { "Nombre", usuario.Nombre },
                    { "Apellido", usuario.Apellido },
                    { "Email", usuario.Email },
                    { "NombreUsuario", usuario.NombreUsuario },
                    { "Telefono", usuario.Telefono },
                    { "Activo", usuario.Activo },
                    { "RolId", usuario.RolId },
                    { "Empresa", usuario.EmpresaId },
                    { "SucursalId", usuario.SucursalId },
                    { "Area",usuario.Area }
                };

                if (usuario.Permisos == null || !usuario.Permisos.Any())
                {
                    return Json(new { success = false, message = "Debe seleccionar al menos un permiso." });
                }

                // Actualiza el usuario en la base de datos
                string query = "UPDATE usuarios SET nombre = @Nombre, apellido = @Apellido, email = @Email, " +
                    "   nombreusuario = @NombreUsuario, telefono = @Telefono, activo = @Activo, rolid = @RolId, empresaid = @Empresa, " +
                    "   sucursal_id = @SucursalId, areaid= @Area " +
                    "WHERE usuarioid = @UsuarioId";
                RunUpdate(query, parameters);

                parameters.Clear();
                query = "DELETE FROM permisos_usuario WHERE usuario_id = @UsuarioId";
                parameters.Add("UsuarioId", usuario.UsuarioId);
                RunUpdate(query, parameters);

                parameters.Clear();
                var _parameters = new List<Dictionary<string, object>>();
                query = "INSERT INTO permisos_usuario(permiso_id, usuario_id) VALUES (@permiso, @UsuarioId)";

                foreach (var permiso in usuario.Permisos)
                {
                    parameters = new Dictionary<string, object>();
                    parameters.Add("UsuarioId", usuario.UsuarioId);
                    parameters.Add("permiso", permiso);
                    _parameters.Add(parameters);
                }
                RunUpdate(query, _parameters);

                return Json(new { success = true, message = "Usuario actualizado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error interno al actualizar el usuario", error = ex.Message });
            }
        }

        // GET: Usuarios/Activar/5
        [HttpGet]
        [RoleAuthorize("Super Administrador", "ERP_SRS")]
        public JsonResult Activar(int id)
        {
            try
            {
                var usuario = db.Usuarios.Find(id);

                if (usuario == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Usuario no encontrado"
                    });
                }

                // Verificar si el usuario ya está activo
                if (usuario.Activo)
                {
                    return Json(new
                    {
                        success = false,
                        message = "El usuario ya está activo"
                    });
                }

                // Activar al usuario
                usuario.Activo = true;
                db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Usuario activado exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error interno al activar el usuario",
                    error = ex.Message
                });
            }
        }

        [HttpPost]
        public JsonResult ResetearContrasena(IFormCollection fc)
        {
            try
            {
                int usuarioId;
                if (!int.TryParse(fc["id"], out usuarioId) || usuarioId <= 0)
                    return Json(new { success = false, message = "Usuario inválido." });

                // Verificar que el usuario exista
                var parameters = new Dictionary<string, object> { { "id", usuarioId } };
                string query = "SELECT usuarioid, nombre, apellido, nombreusuario FROM usuarios WHERE usuarioid = @id";
                var resultado = RunQuery(query, parameters);

                if (resultado == null || resultado.Count == 0)
                    return Json(new { success = false, message = "Usuario no encontrado." });

                const string contrasenaDefault = "1234";
                string nuevoHash = passwordHasher.HashPassword(resultado[0]["nombreusuario"] , contrasenaDefault);        
                parameters = new Dictionary<string, object>
                {
                    { "id",         usuarioId },
                    { "contrasena", nuevoHash }
                };
                query = "UPDATE usuarios SET contrasena = @contrasena WHERE usuarioid = @id";
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Contraseña restablecida a '1234' correctamente." });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al resetear contraseña: {ex.Message}");
                return Json(new { success = false, message = "Ocurrió un error al resetear la contraseña." });
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}