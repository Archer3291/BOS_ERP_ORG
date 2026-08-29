// Controllers/AccountController.cs
using BOS_ERP.Extensions;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BOS_ERP.Controllers
{
    public class AccountController : Utilities
    {
        private readonly LoginService _loginService;
        private readonly FacturacionDbContext _db;
        private readonly IPasswordHasher<Usuario> _passwordHasher;

        public AccountController(LoginService loginService, FacturacionDbContext db, IPasswordHasher<Usuario> passwordHasher)
        {
            _loginService = loginService;
            _db = db;
            _passwordHasher = passwordHasher;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        public IActionResult Logout()
        {
            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            //UserLoginElement validation = _loginService.ValidateCredentials(model.NombreUsuario, model.Contrasena);
            //if (validation.successLogin)
            //{
            var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == model.NombreUsuario);

            if (usuario != null)
            {
                // ✅ Validación de contraseña con PasswordHasher<Usuario> de Core.
                // Firma nueva: VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
                var passwordVerificationResult = _passwordHasher.VerifyHashedPassword(usuario, usuario.Contrasena, model.Contrasena);

                if (passwordVerificationResult == PasswordVerificationResult.Failed)
                {
                    ModelState.AddModelError("", "Nombre de usuario o contraseña incorrectos.");
                    return View(model);
                }

                if (!usuario.Activo)
                {
                    ModelState.AddModelError("", "El usuario no está activo. Contacta al administrador.");
                    return View(model);
                }

                var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.EmpresaId == usuario.EmpresaId);
                if (empresa == null)
                {
                    ModelState.AddModelError("", "No se encontró una empresa asociada a este usuario.");
                    return View(model);
                }

                var licencia = await _db.Licencias.FirstOrDefaultAsync(l => l.EmpresaId == empresa.EmpresaId);
                if (licencia == null)
                {
                    ModelState.AddModelError("", "No se encontró una licencia asociada al RFC de la empresa.");
                    return View(model);
                }

                if (!licencia.Activa)
                {
                    ModelState.AddModelError("", "La licencia está desactivada.");
                    return View(model);
                }

                var ahora = DateTime.Now;
                if (ahora < licencia.FechaInicio || ahora > licencia.FechaExpiracion)
                {
                    ModelState.AddModelError("", $"La licencia no está vigente. Expiró el {licencia.FechaExpiracion:dd/MM/yyyy}.");
                    return View(model);
                }

                // ✅ Gestión de device identifier
                string deviceIdentifier = GetOrCreateDeviceIdentifier();

                // ✅ Validar límite de dispositivos (excluyendo el actual si ya existe)
                int dispositivosActivos = await _db.UserSessions
                    .Where(s => s.UserId == usuario.UsuarioId &&
                        s.IsActive &&
                        s.DeviceIdentifier != deviceIdentifier)
                    .Select(s => s.DeviceIdentifier)
                    .Distinct()
                    .CountAsync();

                if (dispositivosActivos >= 3)
                {
                    ModelState.AddModelError("", "Ya tienes 3 dispositivos activos. Cierra sesión en alguno para continuar.");
                    return View(model);
                }

                var session = await _db.UserSessions.FirstOrDefaultAsync(s =>
                    s.UserId == usuario.UsuarioId &&
                    s.DeviceIdentifier == deviceIdentifier);

                if (session == null)
                {
                    session = new UserSession
                    {
                        UserId = usuario.UsuarioId,
                        DeviceIdentifier = deviceIdentifier,
                        IsActive = true,
                        CreatedAt = DateTime.Now,
                        LastActivity = DateTime.Now,
                        ExpiryTime = DateTime.Now.AddMinutes(30) // prueba
                    };

                    _db.UserSessions.Add(session);
                }
                else
                {
                    session.IsActive = true;
                    session.LastActivity = DateTime.Now;
                    session.ExpiryTime = DateTime.Now.AddMinutes(30); // prueba

                }

                await _db.SaveChangesAsync();

                HttpContext.Session.SetString("SessionExpiry", session.ExpiryTime.ToString("O"));

                // ✅ Configurar autenticación — reemplazo de FormsAuthentication.SetAuthCookie
                var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, usuario.NombreUsuario),
                        new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioId.ToString())
                    };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.Recordarme, // equivalente al "createPersistentCookie" de SetAuthCookie
                    ExpiresUtc = model.Recordarme ? DateTimeOffset.Now.AddDays(30) : (DateTimeOffset?)null
                };

                // ✅ Configurar datos de sesión.
                // Tipos simples (string/int/bool) usan SetString/SetInt32 nativos.
                // Tipos complejos (DateTime, objetos) se serializan vía SetObjectAsJson.
                HttpContext.Session.SetInt32("UsuarioId", usuario.UsuarioId);
                HttpContext.Session.SetString("NombreCompleto", $"{usuario.Nombre} {usuario.Apellido}");
                HttpContext.Session.SetInt32("AreaId", usuario.AreaId);
                HttpContext.Session.SetInt32("Rol", usuario.RolId);
                HttpContext.Session.SetString("RFC", empresa.RFC ?? string.Empty);
                HttpContext.Session.SetString("Licencia", licencia.LicenciaCodigo ?? string.Empty);
                HttpContext.Session.SetObjectAsJson("Fecha", usuario.Fecha);
                HttpContext.Session.SetString("Activo", usuario.Activo.ToString());
                HttpContext.Session.SetString("Correo", usuario.Email ?? string.Empty);
                HttpContext.Session.SetString("Firma", usuario.Firma ?? string.Empty);
                HttpContext.Session.SetObjectAsJson("SessionStartTime", DateTime.Now);
                HttpContext.Session.SetInt32("Sucursal", usuario.SucursalId);
                HttpContext.Session.SetInt32("Empresa", usuario.EmpresaId);
                HttpContext.Session.SetString("EmpresaFactura", empresa.Nombre ?? string.Empty);
                HttpContext.Session.SetObjectAsJson("Permisos", usuario.Permisos);
                HttpContext.Session.SetObjectAsJson("TemaUsuario", CargarTemaUsuario(usuario.UsuarioId));

                string returnUrl = HttpContext.Request.Query["returnUrl"];

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, authProperties);

                if (!String.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                else
                    return RedirectToAction("Index", "Home");
            }
            else
            {
                return View();
            }
            //}
            //else
            //{
            //    ModelState.AddModelError("LoginError", validation.errorMessage);
            //    return View();
            //}
        }

        // ✅ Método para obtener/crear device identifier
        private string GetOrCreateDeviceIdentifier()
        {
            string? deviceIdentifier = Request.Cookies["DeviceIdentifier"];

            if (string.IsNullOrEmpty(deviceIdentifier))
            {
                deviceIdentifier = Guid.NewGuid().ToString();

                // ✅ CookieOptions reemplaza HttpCookie.
                // Request.IsSecureConnection -> Request.IsHttps
                Response.Cookies.Append("DeviceIdentifier", deviceIdentifier, new CookieOptions
                {
                    Expires = DateTimeOffset.Now.AddDays(30),
                    HttpOnly = true,
                    Secure = Request.IsHttps
                });
            }

            return deviceIdentifier;
        }

        #region temas
        // Cargar tema del usuario (usado en Perfil y en _Layout)
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

        // Acción para guardar tema (llamada por AJAX desde el perfil)
        [HttpPost]
        public ActionResult GuardarTema(UsuarioTema tema)
        {
            try
            {
                int usuarioId = GetUserId(User.Identity.Name);

                var hexRegex = new System.Text.RegularExpressions.Regex(@"^#[0-9A-Fa-f]{6}$");
                var propiedades = new[]
                {
            tema.PrimaryBlue,     tema.PrimaryBlueHover, tema.SecondaryBlue,
            tema.LightBlue,       tema.VeryLightBlue,    tema.DarkBlueLight,
            tema.AccentBlue,      tema.BgLight,          tema.BgGray,
            tema.TextDark,        tema.TextLight,         tema.BorderLight,
            tema.DarkPrimaryBlue, tema.DarkBlueHover,    tema.DarkSecondaryBlue,
            tema.DarkLightBlue,   tema.DarkVeryLightBlue, tema.DarkDarkBlue,
            tema.DarkAccentBlue,  tema.DarkBgLight,      tema.DarkBgGray,
            tema.DarkTextDark,    tema.DarkTextLight,    tema.DarkBorderLight
        };
                if (propiedades.Where(p => p != null).Any(p => !hexRegex.IsMatch(p)))
                    return Json(new { success = false, message = "Color inválido." });

                string query = @"
            INSERT INTO usuario_tema
                (usuarioid, modo,
                 primary_blue, primary_blue_hover, secondary_blue,
                 light_blue, very_light_blue, dark_blue_light,
                 accent_blue, bg_light, bg_gray,
                 text_dark, text_light, border_light,
                 dark_primary_blue, dark_blue_hover, dark_secondary_blue,
                 dark_light_blue, dark_very_light_blue, dark_dark_blue,
                 dark_accent_blue, dark_bg_light, dark_bg_gray,
                 dark_text_dark, dark_text_light, dark_border_light,
                 preset_nombre, fechamodificacion, 
                 sidebar_posicion, sidebar_ancho, sidebar_icon_style, sidebar_hover_efecto,
                 sidebar_bg_color, sidebar_link_color, sidebar_active_color,
                 sidebar_active_link_color, sidebar_header_bg, sidebar_modo, layout_tipo,
                 particulas_activas, particulas_cantidad, particulas_forma, particulas_preset)
            VALUES
                (@usuarioId, @modo,
                 @primaryBlue, @primaryBlueHover, @secondaryBlue,
                 @lightBlue, @veryLightBlue, @darkBlueLight,
                 @accentBlue, @bgLight, @bgGray,
                 @textDark, @textLight, @borderLight,
                 @darkPrimaryBlue, @darkBlueHover, @darkSecondaryBlue,
                 @darkLightBlue, @darkVeryLightBlue, @darkDarkBlue,
                 @darkAccentBlue, @darkBgLight, @darkBgGray,
                 @darkTextDark, @darkTextLight, @darkBorderLight,
                 @presetNombre, now(),
                 @sidebarPosicion, @sidebarAncho, @sidebarIconStyle, @sidebarHoverEfecto,
                 @sidebarBgColor, @sidebarLinkColor, @sidebarActiveColor,
                 @sidebarActiveLinkColor, @sidebarHeaderBg, @sidebarModo, @layoutTipo,
                 @particulasActivas, @particulasCantidad, @particulasForma, @particulasPreset)
            ON CONFLICT (usuarioid) DO UPDATE SET
                modo               = EXCLUDED.modo,
                primary_blue       = EXCLUDED.primary_blue,
                primary_blue_hover = EXCLUDED.primary_blue_hover,
                secondary_blue     = EXCLUDED.secondary_blue,
                light_blue         = EXCLUDED.light_blue,
                very_light_blue    = EXCLUDED.very_light_blue,
                dark_blue_light    = EXCLUDED.dark_blue_light,
                accent_blue        = EXCLUDED.accent_blue,
                bg_light           = EXCLUDED.bg_light,
                bg_gray            = EXCLUDED.bg_gray,
                text_dark          = EXCLUDED.text_dark,
                text_light         = EXCLUDED.text_light,
                border_light       = EXCLUDED.border_light,
                dark_primary_blue  = EXCLUDED.dark_primary_blue,
                dark_blue_hover    = EXCLUDED.dark_blue_hover,
                dark_secondary_blue= EXCLUDED.dark_secondary_blue,
                dark_light_blue    = EXCLUDED.dark_light_blue,
                dark_very_light_blue= EXCLUDED.dark_very_light_blue,
                dark_dark_blue     = EXCLUDED.dark_dark_blue,
                dark_accent_blue   = EXCLUDED.dark_accent_blue,
                dark_bg_light      = EXCLUDED.dark_bg_light,
                dark_bg_gray       = EXCLUDED.dark_bg_gray,
                dark_text_dark     = EXCLUDED.dark_text_dark,
                dark_text_light    = EXCLUDED.dark_text_light,
                dark_border_light  = EXCLUDED.dark_border_light,
                preset_nombre      = EXCLUDED.preset_nombre,
                particulas_cantidad = EXCLUDED.particulas_cantidad,
                particulas_activas  = EXCLUDED.particulas_activas,
                particulas_forma    = EXCLUDED.particulas_forma,

                sidebar_posicion       = EXCLUDED.sidebar_posicion,
                sidebar_ancho          = EXCLUDED.sidebar_ancho,
                sidebar_icon_style     = EXCLUDED.sidebar_icon_style,
                sidebar_hover_efecto   = EXCLUDED.sidebar_hover_efecto,
                sidebar_bg_color       = EXCLUDED.sidebar_bg_color,
                sidebar_link_color     = EXCLUDED.sidebar_link_color,
                sidebar_active_color   = EXCLUDED.sidebar_active_color,
                sidebar_active_link_color = EXCLUDED.sidebar_active_link_color,
                sidebar_header_bg      = EXCLUDED.sidebar_header_bg,
                sidebar_modo = EXCLUDED.sidebar_modo,
                layout_tipo = EXCLUDED.layout_tipo,
                particulas_preset   = EXCLUDED.particulas_preset,

                fechamodificacion  = now()";

                var parameters = new Dictionary<string, object>
                {
                    ["usuarioId"] = usuarioId,
                    ["modo"] = tema.Modo ?? "light",
                    ["primaryBlue"] = (object)tema.PrimaryBlue ?? DBNull.Value,
                    ["primaryBlueHover"] = (object)tema.PrimaryBlueHover ?? DBNull.Value,
                    ["secondaryBlue"] = (object)tema.SecondaryBlue ?? DBNull.Value,
                    ["lightBlue"] = (object)tema.LightBlue ?? DBNull.Value,
                    ["veryLightBlue"] = (object)tema.VeryLightBlue ?? DBNull.Value,
                    ["darkBlueLight"] = (object)tema.DarkBlueLight ?? DBNull.Value,
                    ["accentBlue"] = (object)tema.AccentBlue ?? DBNull.Value,
                    ["bgLight"] = (object)tema.BgLight ?? DBNull.Value,
                    ["bgGray"] = (object)tema.BgGray ?? DBNull.Value,
                    ["textDark"] = (object)tema.TextDark ?? DBNull.Value,
                    ["textLight"] = (object)tema.TextLight ?? DBNull.Value,
                    ["borderLight"] = (object)tema.BorderLight ?? DBNull.Value,
                    ["darkPrimaryBlue"] = (object)tema.DarkPrimaryBlue ?? DBNull.Value,
                    ["darkBlueHover"] = (object)tema.DarkBlueHover ?? DBNull.Value,
                    ["darkSecondaryBlue"] = (object)tema.DarkSecondaryBlue ?? DBNull.Value,
                    ["darkLightBlue"] = (object)tema.DarkLightBlue ?? DBNull.Value,
                    ["darkVeryLightBlue"] = (object)tema.DarkVeryLightBlue ?? DBNull.Value,
                    ["darkDarkBlue"] = (object)tema.DarkDarkBlue ?? DBNull.Value,
                    ["darkAccentBlue"] = (object)tema.DarkAccentBlue ?? DBNull.Value,
                    ["darkBgLight"] = (object)tema.DarkBgLight ?? DBNull.Value,
                    ["darkBgGray"] = (object)tema.DarkBgGray ?? DBNull.Value,
                    ["darkTextDark"] = (object)tema.DarkTextDark ?? DBNull.Value,
                    ["darkTextLight"] = (object)tema.DarkTextLight ?? DBNull.Value,
                    ["darkBorderLight"] = (object)tema.DarkBorderLight ?? DBNull.Value,
                    ["presetNombre"] = (object)tema.PresetNombre ?? DBNull.Value,
                    ["particulasActivas"] = (object)(tema.ParticulasActivas ?? true),
                    ["particulasCantidad"] = (object)(tema.ParticulasCantidad ?? 200),
                    ["particulasForma"] = (object)tema.ParticulasForma ?? "circle",
                    ["particulasPreset"] = (object)tema.ParticulasPreset ?? "theme",
                    ["sidebarPosicion"] = (object)(tema.SidebarPosicion ?? "left"),
                    ["sidebarAncho"] = (object)(tema.SidebarAncho ?? "285"),
                    ["sidebarIconStyle"] = (object)(tema.SidebarIconStyle ?? "duotone"),
                    ["sidebarHoverEfecto"] = (object)(tema.SidebarHoverEfecto ?? "slide"),
                    ["sidebarBgColor"] = (object)tema.SidebarBgColor ?? DBNull.Value,
                    ["sidebarLinkColor"] = (object)tema.SidebarLinkColor ?? DBNull.Value,
                    ["sidebarActiveColor"] = (object)tema.SidebarActiveColor ?? DBNull.Value,
                    ["sidebarActiveLinkColor"] = (object)tema.SidebarActiveLinkColor ?? DBNull.Value,
                    ["sidebarModo"] = (object)(tema.SidebarModo ?? "normal"),
                    ["sidebarHeaderBg"] = (object)tema.SidebarHeaderBg ?? DBNull.Value,
                    ["layoutTipo"] = (object)(tema.LayoutTipo ?? "sidebar"),
                };

                RunQuery(query, parameters);

                var temaGuardado = CargarTemaUsuario(usuarioId);
                HttpContext.Session.SetObjectAsJson("TemaUsuario", temaGuardado);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Acción para aplicar un preset
        [HttpPost]
        public ActionResult AplicarPreset(string preset)
        {
            if (!TemaPresets.Presets.ContainsKey(preset))
                return Json(new { success = false, message = "Preset no encontrado." });

            var tema = TemaPresets.Presets[preset];
            tema.PresetNombre = preset;
            return GuardarTema(tema);
        }
        #endregion

        [Authorize]
        public JsonResult ActivarModoMantanimiento()
        {
            try
            {
                string query = "UPDATE settings SET setting_value = 'true' WHERE setting_name = 'mantenimiento'";
                RunUpdate(query);

                return Json(new { success = true, message = "Modo mantenimiento activado exitosamente" });
            }
            catch
            {
                return Json(new { success = false, message = "Ocurrio un error al activar el modo mantenimiento, no haga nada, el mantenimiento se llevara acabo de todos modos." });
            }
        }

        public JsonResult ActualizarFirma(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE usuarios SET firma = @firma WHERE usuarioid = @usuarioId";
                parameters.Add("firma", fc["firma"].ToString());
                parameters.Add("usuarioId", GetUserId(User.Identity.Name));
                RunUpdate(query, parameters);

                //Session["Firma"] = fc["firma"].ToString();
                HttpContext.Session.SetString("Firma", fc["firma"].ToString());

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ocurrio un error al actualizar la firma" });
            }

        }
    }
}