using BOS_ERP.Controllers;
using BOS_ERP.Extensions;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.controllers
{
    public class PerfilController : Utilities
    {
        private readonly PasswordHasher<object> passwordHasher;

        public PerfilController()
        {
            passwordHasher = new PasswordHasher<object>();
        }

        [Authorize]
        public ActionResult Perfil()
        {
            var parameters = new Dictionary<string, object>();
            string usuario = User.Identity?.Name ?? string.Empty;
            int usuarioId = GetUserId(usuario);
            parameters.Add("usuario", usuario);
            parameters.Add("usuarioId", usuarioId);

            // Actividad reciente (últimos 5)
            string query = @"SELECT modulo, accion, fecha 
                     FROM historial_usuarios 
                     WHERE usuario = @usuario 
                     ORDER BY fecha DESC 
                     LIMIT 5";
            ViewBag.Movimientos = RunQuery(query, parameters);

            // Stats tareas
            query = "SELECT COUNT(*) FROM encabezadomov WHERE estatus_id = 11 AND usr_doc = @usuario";
            ViewBag.Completados = RunScalar(query, parameters);
            query = "SELECT COUNT(*) FROM encabezadomov WHERE estatus_id != 11 AND usr_doc = @usuario";
            ViewBag.Pendientes = RunScalar(query, parameters);

            // Documentos activos/pendientes (últimos 8)
            query = @"SELECT id_encabezado, estatus_id, fch, usr_doc 
              FROM encabezadomov 
              WHERE estatus_id != 11 AND usr_doc = @usuario 
              ORDER BY fch DESC 
              LIMIT 8";
            ViewBag.Documentos = RunQuery(query, parameters);

            // Sesiones activas
            query = @"SELECT deviceidentifier, lastactivity, createdat, isactive 
              FROM usersessions 
              WHERE userid = @usuarioId AND isactive = true 
              ORDER BY lastactivity DESC";
            ViewBag.Sesiones = RunQuery(query, parameters);

            // Actividad por día (últimos 14 días) para gráfica
            query = @"SELECT DATE(fecha) as dia, COUNT(*) as total, modulo
              FROM historial_usuarios 
              WHERE usuario = @usuario 
                AND fecha >= NOW() - INTERVAL '14 days'
              GROUP BY DATE(fecha), modulo
              ORDER BY dia ASC";
            ViewBag.ActividadGrafica = RunQuery(query, parameters);

            // Acciones rápidas
            ViewBag.AccionesDisponibles = ObtenerAccionesDisponibles(usuarioId);
            ViewBag.AccionesUsuario = ObtenerAccionesRapidas(usuarioId);

            var tema = CargarTemaUsuario(usuarioId);
            ViewBag.TemaUsuario = tema;

            // ✅ Antes: Session["TemaUsuario"] = tema (objeto complejo, no soportado nativamente)
            // Ahora: se serializa a JSON con la extensión SetObjectAsJson
            //HttpContext.Session.SetObjectAsJson("TemaUsuario", tema);

            return View();
        }

        private UsuarioTema CargarTemaUsuario(int usuarioId)
        {
            string query = @"
                SELECT id, usuarioid, modo,
                    primary_blue, primary_blue_hover, secondary_blue,
                    light_blue, very_light_blue, dark_blue_light,
                    accent_blue, bg_light, bg_gray,
                    text_dark, text_light, border_light,
                    dark_primary_blue, dark_blue_hover, dark_secondary_blue,
                    dark_light_blue, dark_very_light_blue, dark_dark_blue,
                    dark_accent_blue, dark_bg_light, dark_bg_gray,
                    dark_text_dark, dark_text_light, dark_border_light,
                    particulas_activas, particulas_cantidad, particulas_forma, particulas_preset,
                    sidebar_posicion, sidebar_ancho, sidebar_icon_style, sidebar_hover_efecto,
                    sidebar_bg_color, sidebar_link_color, sidebar_active_color,
                    sidebar_active_link_color, sidebar_header_bg, sidebar_modo,
                    layout_tipo,
                    preset_nombre
                FROM usuario_tema
                WHERE usuarioid = @usuarioId";

            var parameters = new Dictionary<string, object> { ["usuarioId"] = usuarioId };
            var rows = RunQuery(query, parameters);

            if (rows == null || rows.Count == 0)
                return TemaPresets.Presets["default"];

            var r = rows[0];
            var d = TemaPresets.Presets["default"];

            return new UsuarioTema
            {
                Id = Convert.ToInt32(r["id"]),
                UsuarioId = usuarioId,
                Modo = r["modo"]?.ToString() ?? "light",

                PrimaryBlue = r["primary_blue"]?.ToString() ?? d.PrimaryBlue,
                PrimaryBlueHover = r["primary_blue_hover"]?.ToString() ?? d.PrimaryBlueHover,
                SecondaryBlue = r["secondary_blue"]?.ToString() ?? d.SecondaryBlue,
                LightBlue = r["light_blue"]?.ToString() ?? d.LightBlue,
                VeryLightBlue = r["very_light_blue"]?.ToString() ?? d.VeryLightBlue,
                DarkBlueLight = r["dark_blue_light"]?.ToString() ?? d.DarkBlueLight,
                AccentBlue = r["accent_blue"]?.ToString() ?? d.AccentBlue,
                BgLight = r["bg_light"]?.ToString() ?? d.BgLight,
                BgGray = r["bg_gray"]?.ToString() ?? d.BgGray,
                TextDark = r["text_dark"]?.ToString() ?? d.TextDark,
                TextLight = r["text_light"]?.ToString() ?? d.TextLight,
                BorderLight = r["border_light"]?.ToString() ?? d.BorderLight,

                DarkPrimaryBlue = r["dark_primary_blue"]?.ToString() ?? d.DarkPrimaryBlue,
                DarkBlueHover = r["dark_blue_hover"]?.ToString() ?? d.DarkBlueHover,
                DarkSecondaryBlue = r["dark_secondary_blue"]?.ToString() ?? d.DarkSecondaryBlue,
                DarkLightBlue = r["dark_light_blue"]?.ToString() ?? d.DarkLightBlue,
                DarkVeryLightBlue = r["dark_very_light_blue"]?.ToString() ?? d.DarkVeryLightBlue,
                DarkDarkBlue = r["dark_dark_blue"]?.ToString() ?? d.DarkDarkBlue,
                DarkAccentBlue = r["dark_accent_blue"]?.ToString() ?? d.DarkAccentBlue,
                DarkBgLight = r["dark_bg_light"]?.ToString() ?? d.DarkBgLight,
                DarkBgGray = r["dark_bg_gray"]?.ToString() ?? d.DarkBgGray,
                DarkTextDark = r["dark_text_dark"]?.ToString() ?? d.DarkTextDark,
                DarkTextLight = r["dark_text_light"]?.ToString() ?? d.DarkTextLight,
                DarkBorderLight = r["dark_border_light"]?.ToString() ?? d.DarkBorderLight,

                ParticulasActivas = r["particulas_activas"] as bool? ?? true,
                ParticulasCantidad = r["particulas_cantidad"] as int? ?? 200,
                ParticulasForma = r["particulas_forma"]?.ToString() ?? "circle",
                ParticulasPreset = r["particulas_preset"]?.ToString() ?? "theme",

                SidebarPosicion = r["sidebar_posicion"]?.ToString() ?? "left",
                SidebarAncho = r["sidebar_ancho"]?.ToString() ?? "285",
                SidebarIconStyle = r["sidebar_icon_style"]?.ToString() ?? "duotone",
                SidebarHoverEfecto = r["sidebar_hover_efecto"]?.ToString() ?? "slide",
                SidebarBgColor = r["sidebar_bg_color"]?.ToString(),
                SidebarLinkColor = r["sidebar_link_color"]?.ToString(),
                SidebarActiveColor = r["sidebar_active_color"]?.ToString(),
                SidebarActiveLinkColor = r["sidebar_active_link_color"]?.ToString(),
                SidebarHeaderBg = r["sidebar_header_bg"]?.ToString(),
                SidebarModo = r["sidebar_modo"]?.ToString() ?? "normal",

                LayoutTipo = r["layout_tipo"]?.ToString() ?? "sidebar",

                PresetNombre = r["preset_nombre"]?.ToString()
            };
        }

        #region Cambio de contraseña
        [HttpPost]
        public JsonResult CambiarContrasena(IFormCollection fc)
        {
            try
            {
                string contrasenaActual = fc["contrasenaActual"];
                string nuevaContrasena = fc["nuevaContrasena"];
                string confirmarContrasena = fc["confirmarContrasena"];

                // Validaciones básicas
                if (string.IsNullOrWhiteSpace(contrasenaActual) ||
                    string.IsNullOrWhiteSpace(nuevaContrasena) ||
                    string.IsNullOrWhiteSpace(confirmarContrasena))
                    return Json(new { success = false, message = "Todos los campos son requeridos." });

                if (nuevaContrasena != confirmarContrasena)
                    return Json(new { success = false, message = "La nueva contraseña y la confirmación no coinciden." });

                if (nuevaContrasena.Length < 6)
                    return Json(new { success = false, message = "La nueva contraseña debe tener al menos 6 caracteres." });

                if (contrasenaActual == nuevaContrasena)
                    return Json(new { success = false, message = "La nueva contraseña no puede ser igual a la actual." });

                // Obtener hash actual de la BD
                int usuarioId = GetUserId(User.Identity.Name);
                var parameters = new Dictionary<string, object> { { "id", usuarioId } };

                string query = "SELECT contrasena FROM usuarios WHERE usuarioid = @id";
                var resultado = RunQuery(query, parameters);

                if (resultado == null || resultado.Count == 0)
                    return Json(new { success = false, message = "Usuario no encontrado." });

                string hashActual = resultado[0]["contrasena"].ToString();

                // Verificar contraseña actual
                var verificacion = passwordHasher.VerifyHashedPassword(User.Identity.Name, hashActual, contrasenaActual);
                if (verificacion == PasswordVerificationResult.Failed)
                    return Json(new { success = false, message = "La contraseña actual es incorrecta." });

                // Actualizar con nuevo hash
                string nuevoHash = passwordHasher.HashPassword(User.Identity.Name, nuevaContrasena);
                parameters = new Dictionary<string, object>
                {
                    { "id",         usuarioId },
                    { "contrasena", nuevoHash }
                };

                query = "UPDATE usuarios SET contrasena = @contrasena WHERE usuarioid = @id";
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Contraseña actualizada correctamente." });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cambiar contraseña: {ex.Message}");
                return Json(new { success = false, message = "Ocurrió un error al cambiar la contraseña." });
            }
        }
        #endregion
    }
}
