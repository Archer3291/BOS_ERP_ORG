using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Sistemas
{
    public class ClaveModuloController : Utilities
    {
        private const int MaxIntentos = 3;
        private const int MinutosBloqueo = 30;

        public IActionResult Index() => View();

        // ── GET: lista de claves ──────────────────────────────────────────
        [HttpGet]
        public JsonResult ObtenerClaves(string buscar = "", string estado = "")
        {
            try
            {
                string sql = @"
                    SELECT
                        c.id,
                        c.modulo_id      AS ModuloId,
                        c.nombre         AS Nombre,
                        c.activo         AS Activo,
                        c.fecha_creacion AS FechaCreacion,
                        COUNT(cu.id)     AS TotalUsuarios
                    FROM claves_modulo c
                    LEFT JOIN clave_modulo_usuarios cu ON cu.clave_id = c.id
                    WHERE (
                        @buscar = ''
                        OR c.modulo_id ILIKE '%' || @buscar || '%'
                        OR c.nombre    ILIKE '%' || @buscar || '%'
                    )
                    AND (
                        @estado = ''
                        OR CASE WHEN c.activo THEN 'activo' ELSE 'inactivo' END = @estado
                    )
                    GROUP BY c.id
                    ORDER BY c.id";

                var p = new Dictionary<string, object>
                {
                    { "@buscar", buscar ?? "" },
                    { "@estado", estado ?? "" }
                };

                return Json(new { exito = true, datos = RunQuery(sql, p) });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── GET: usuarios asignados a una clave ───────────────────────────
        [HttpGet]
        public JsonResult ObtenerUsuariosClave(int claveId)
        {
            try
            {
                string sql = @"
                    SELECT
                        cu.id,
                        cu.usuario_id        AS UsuarioId,
                        cu.activo            AS Activo,
                        cu.intentos_fallidos AS IntentosFallidos,
                        cu.bloqueado_hasta   AS BloqueadoHasta,
                        cu.fecha_asignacion  AS FechaAsignacion,
                        cu.acceso_directo    AS AccesoDirecto,          -- ← agrega esto
                        u.nombre || ' ' || u.apellido AS NombreUsuario
                    FROM clave_modulo_usuarios cu
                    JOIN usuarios u ON u.usuarioid = cu.usuario_id
                    WHERE cu.clave_id = @claveId
                    ORDER BY u.nombre";

                var p = new Dictionary<string, object> { { "@claveId", claveId } };
                return Json(new { exito = true, datos = RunQuery(sql, p) });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── GET: usuarios disponibles para asignar ────────────────────────
        [HttpGet]
        public JsonResult ObtenerUsuariosDisponibles(int claveId)
        {
            try
            {
                // Usuarios activos que aún NO están asignados a esta clave
                string sql = @"
                    SELECT usuarioid AS Id, nombre || ' ' || apellido AS Nombre
                    FROM usuarios
                    WHERE activo = true
                      AND usuarioid NOT IN (
                          SELECT usuario_id FROM clave_modulo_usuarios
                          WHERE clave_id = @claveId
                      )
                    ORDER BY nombre";

                var p = new Dictionary<string, object> { { "@claveId", claveId } };
                return Json(new { exito = true, datos = RunQuery(sql, p) });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: crear clave ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Crear(ClaveModuloVM model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return Json(new { exito = false, mensaje = "Revise los datos." });

                if (string.IsNullOrWhiteSpace(model.Password) || model.Password.Length < 8)
                    return Json(new { exito = false, mensaje = "La contraseña debe tener al menos 8 caracteres." });

                string hash = BCrypt.Net.BCrypt.HashPassword(model.Password);

                string sql = @"
                    INSERT INTO claves_modulo
                        (modulo_id, nombre, password_hash, activo, fecha_creacion)
                    VALUES
                        (@moduloId, @nombre, @hash, @activo, NOW())
                    RETURNING id";

                var p = new Dictionary<string, object>
                {
                    { "@moduloId", model.ModuloId.ToUpper() },
                    { "@nombre",   model.Nombre              },
                    { "@hash",     hash                      },
                    { "@activo",   model.Activo              }
                };

                var rows = RunQuery(sql, p);
                int newId = Convert.ToInt32(((IDictionary<string, object>)rows[0])["id"]);

                return Json(new { exito = true, mensaje = "Clave creada.", id = newId });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: editar clave ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Editar(ClaveModuloVM model)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    if (model.Password.Length < 8)
                        return Json(new { exito = false, mensaje = "Mínimo 8 caracteres." });

                    string hash = BCrypt.Net.BCrypt.HashPassword(model.Password);
                    RunQuery(@"UPDATE claves_modulo SET
                                   nombre = @nombre, modulo_id = @moduloId,
                                   password_hash = @hash, activo = @activo
                               WHERE id = @id",
                        new Dictionary<string, object>
                        {
                            { "@nombre",   model.Nombre              },
                            { "@moduloId", model.ModuloId.ToUpper()  },
                            { "@hash",     hash                      },
                            { "@activo",   model.Activo              },
                            { "@id",       model.Id                  }
                        });
                }
                else
                {
                    RunQuery(@"UPDATE claves_modulo SET
                                   nombre = @nombre, modulo_id = @moduloId, activo = @activo
                               WHERE id = @id",
                        new Dictionary<string, object>
                        {
                            { "@nombre",   model.Nombre             },
                            { "@moduloId", model.ModuloId.ToUpper() },
                            { "@activo",   model.Activo             },
                            { "@id",       model.Id                 }
                        });
                }

                return Json(new { exito = true, mensaje = "Clave actualizada." });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: eliminar clave ──────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Eliminar(int id)
        {
            try
            {
                RunQuery("DELETE FROM claves_modulo WHERE id = @id",
                    new Dictionary<string, object> { { "@id", id } });
                return Json(new { exito = true, mensaje = "Clave eliminada." });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: asignar usuario a clave ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult AsignarUsuario(int claveId, int usuarioId)
        {
            try
            {
                RunQuery(@"INSERT INTO clave_modulo_usuarios
                               (clave_id, usuario_id, activo, fecha_asignacion)
                           VALUES (@claveId, @usuarioId, true, NOW())",
                    new Dictionary<string, object>
                    {
                        { "@claveId",   claveId   },
                        { "@usuarioId", usuarioId }
                    });

                return Json(new { exito = true, mensaje = "Usuario asignado." });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: quitar usuario de clave ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult QuitarUsuario(int asignacionId)
        {
            try
            {
                RunQuery("DELETE FROM clave_modulo_usuarios WHERE id = @id",
                    new Dictionary<string, object> { { "@id", asignacionId } });
                return Json(new { exito = true, mensaje = "Usuario removido." });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: resetear intentos de un usuario en una clave ────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ResetearIntentos(int asignacionId)
        {
            try
            {
                RunQuery(@"UPDATE clave_modulo_usuarios SET
                               intentos_fallidos = 0, bloqueado_hasta = NULL
                           WHERE id = @id",
                    new Dictionary<string, object> { { "@id", asignacionId } });
                return Json(new { exito = true, mensaje = "Intentos reseteados." });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── POST: toggle activo de asignación ─────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ToggleAsignacion(int asignacionId)
        {
            try
            {
                var rows = RunQuery(@"UPDATE clave_modulo_usuarios SET activo = NOT activo
                                     WHERE id = @id RETURNING activo",
                    new Dictionary<string, object> { { "@id", asignacionId } });

                bool activo = Convert.ToBoolean(((IDictionary<string, object>)rows[0])["activo"]);
                return Json(new { exito = true, activo });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }


        // ── POST: toggle acceso directo de asignación ─────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ToggleAccesoDirecto(int asignacionId)
        {
            try
            {
                var rows = RunQuery(@"UPDATE clave_modulo_usuarios 
                                 SET acceso_directo = NOT COALESCE(acceso_directo, false)
                               WHERE id = @id 
                           RETURNING acceso_directo",
                    new Dictionary<string, object> { { "@id", asignacionId } });

                bool acceso = Convert.ToBoolean(((IDictionary<string, object>)rows[0])["acceso_directo"]);
                return Json(new { exito = true, accesoDirecto = acceso });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ── ValidarAcceso: uso interno desde otros controllers ────────────
        // El usuario ingresa la contraseña de la clave de ese módulo.
        // Se verifica que esté asignado Y que la contraseña sea correcta.
        // El bloqueo es por usuario-clave (no global).
    } 
}