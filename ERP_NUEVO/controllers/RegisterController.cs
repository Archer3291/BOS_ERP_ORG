using BOS_ERP.Controllers;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BOS_ERP.controllers
{
    public class RegisterController : Controller
    {
        private readonly FacturacionDbContext db;
        private readonly EmailSender _emailSender;
        private readonly CorreoHelper _correoHelper;
        private readonly LoginService _loginService;

        public RegisterController(FacturacionDbContext db, EmailSender emailSender, CorreoHelper correoHelper, LoginService loginService)
        {
            this.db = db;
            _emailSender = emailSender;
            _correoHelper = correoHelper;
            _loginService = loginService;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Register()
        {
            var model = new RegisterViewModel
            {
                RolId = 2,
                Empresas = await db.Empresas
                    .Select(e => new SelectListItem
                    {
                        Value = e.EmpresaId.ToString(),
                        Text = e.Nombre
                    }).ToListAsync(),

                Sucursales = new List<SelectListItem>(),

                Areas = await db.AreaId
                    .Select(a => new SelectListItem
                    {
                        Value = a.Id_area.ToString(),
                        Text = a.Id_area + " - " + a.Descripcion
                    }).ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await CargarCombos(model);
                return View(model);
            }

            try
            {
                // ===== VALIDACIONES DE NEGOCIO =====

                if (await db.Usuarios.AnyAsync(u => u.NombreUsuario == model.NombreUsuario))
                {
                    ModelState.AddModelError(nameof(model.NombreUsuario),
                        "El nombre de usuario ya está en uso.");
                }

                var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.EmpresaId == model.EmpresaId);
                if (empresa == null)
                {
                    ModelState.AddModelError(nameof(model.EmpresaId),
                        "Debe seleccionar una empresa válida.");
                }

                var sucursal = await db.SucursalId.FirstOrDefaultAsync(s => s.Id_sucursal == model.Id_sucursal);
                if (sucursal == null)
                {
                    ModelState.AddModelError(nameof(model.Id_sucursal),
                        "Debe seleccionar una sucursal válida.");
                }

                var area = await db.AreaId.FirstOrDefaultAsync(a => a.Id_area == model.Id_Area);
                if (area == null)
                {
                    ModelState.AddModelError(nameof(model.Id_Area),
                        "Debe seleccionar un área válida.");
                }

                if (!ModelState.IsValid)
                {
                    await CargarCombos(model);
                    return View(model);
                }

                // ===== CREAR USUARIO =====

                // ✅ HashPassword(TUser user, string password) — firma nueva de Core
                string hashedPassword = _loginService.EncryptString(model.NombreUsuario, model.Contrasena);

                var nuevoUsuario = new Usuario
                {
                    NombreUsuario = model.NombreUsuario,
                    Contrasena = hashedPassword,
                    Nombre = model.Nombre,
                    Apellido = model.Apellido,
                    Email = model.Email,
                    SuperiorEmail = model.SuperiorEmail,
                    EmpresaId = empresa!.EmpresaId,
                    Activo = false,
                    RolId = model.RolId,
                    SucursalId = sucursal!.Id_sucursal,
                    AreaId = area!.Id_area
                };
                Console.WriteLine($"Longitud contraseña: {hashedPassword.Length}");

                db.Usuarios.Add(nuevoUsuario);
                await db.SaveChangesAsync();

                // ===== ROL DE TICKETS =====

                var newUsuarioTicket = new tkt_usuario_rol
                {
                    id_usr = nuevoUsuario.UsuarioId,
                    id_rol_tkt = 3
                };

                db.tkt_usuario_rol.Add(newUsuarioTicket);
                await db.SaveChangesAsync();

                // ===== CORREO =====

                // ✅ Request.Url.GetLeftPart(UriPartial.Authority) -> Request.Scheme + Request.Host
                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string urlActivacion = $"{urlBase}/Gerentes/GestionUsuarios/{nuevoUsuario.UsuarioId}";

                var emailData = new UserRegistrationEmailModel
                {
                    NombreUsuario = model.NombreUsuario,
                    Nombre = model.Nombre,
                    Apellido = model.Apellido,
                    Email = model.Email,
                    SuperiorEmail = model.SuperiorEmail,
                    EmpresaNombre = empresa.Nombre,
                    RolId = model.RolId,
                    UrlActivacion = urlActivacion
                };

                string htmlBody = await _emailSender.RenderViewToStringAsync(
                    "~/Views/Email/_UserRegistrationNotification.cshtml",
                    emailData
                );

                //await _correoHelper.EnviarCorreoNotificacionAsync(
                //    nuevoUsuario.SuperiorEmail,
                //    "Nuevo registro de usuario",
                //    htmlBody
                //);

                return RedirectToAction("Index", "Home");
            }
            catch (DbUpdateException ex)
            {
                if (ex.InnerException is Npgsql.PostgresException pg)
                {
                    var detalle = $@"
                        SQLSTATE: {pg.SqlState}
                        Mensaje: {pg.MessageText}
                        Detalle: {pg.Detail}
                        Tabla: {pg.TableName}
                        Columna: {pg.ColumnName}
                        Restricción: {pg.ConstraintName}";

                    LogErrorHelper.RegistrarLog(
                        "REGISTRAR_USUARIO",
                        "SIN_FOLIO",
                        detalle,
                        nivel: "ERROR"
                    );

                    //await _correoHelper.EnviarCorreoNotificacionAsync(
                    //    nuevoUsuario.SuperiorEmail,
                    //    "Nuevo registro de usuario",
                    //    htmlBody
                    //);
                }
                else
                {
                    LogErrorHelper.RegistrarLog(
                        "REGISTRAR_USUARIO",
                        "SIN_FOLIO",
                        ex.ToString(),
                        nivel: "ERROR"
                    );
                }

                ModelState.AddModelError("",
                    "Ocurrió un error al registrar el usuario. Intenta nuevamente o contacta a soporte.");
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(
                       "REGISTRAR_USUARIO",
                       "SIN_FOLIO",
                       ex.Message,
                       nivel: "ERROR"
                   );

                ModelState.AddModelError("",
                    "Ocurrió un error inesperado al registrar el usuario. Intenta nuevamente o contacta a soporte.");
                System.Diagnostics.Debug.WriteLine(ex);

            }

            await CargarCombos(model);
            return View(model);
        }

        private async Task CargarCombos(RegisterViewModel model)
        {
            model.Empresas = await db.Empresas
                .Select(e => new { e.EmpresaId, e.Nombre })
                .Select(e => new SelectListItem
                {
                    Value = e.EmpresaId.ToString(),
                    Text = e.Nombre
                }).ToListAsync();

            model.Sucursales = await db.SucursalId
                .Select(s => new { s.Id_sucursal, s.Descripcion })
                .Select(s => new SelectListItem
                {
                    Value = s.Id_sucursal.ToString(),
                    Text = $"{s.Id_sucursal} - {s.Descripcion}"
                }).ToListAsync();

            model.Areas = await db.AreaId
                .Select(a => new { a.Id_area, a.Descripcion })
                .Select(a => new SelectListItem
                {
                    Value = a.Id_area.ToString(),
                    Text = $"{a.Id_area} - {a.Descripcion}"
                }).ToListAsync();
        }
    }
}