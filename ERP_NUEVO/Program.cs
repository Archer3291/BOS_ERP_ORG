using BOS_ERP.Controllers;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Hubs;
using BOS_ERP.Middleware;
using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using BOS_ERP.Services;
using BOS_ERP.Services.Facturacion;
using BOS_ERP.Services.Refacturacion;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Rotativa.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 🔹 Controllers + Views (Razor) — AccountController usa View()/ViewBag,
// con solo AddControllers() (Web API) esto no funciona.
builder.Services.AddScoped<SessionManagementFilter>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<SessionManagementFilter>();
});

// 🔹 SignalR — canal en tiempo real entre vistas abiertas (ver Hubs/InventarioHub.cs)
builder.Services.AddSignalR();

// 🔹 Connection string (se usa para DbContext y Hangfire)
var connectionString = builder.Configuration.GetConnectionString("ERP_SRS");

// 🔹 Filtro de autorización por API Key (para Web API)
builder.Services.AddScoped<ApiKeyAuthorizationFilter>();

// 🔹 Rate limiting (para Web API)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromHours(1);
        opt.QueueLimit = 0;
    });
});

// 🔹 Memory cache (para Web API)
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ApiKeyAuthorizationFilter>();

builder.Services.AddScoped<XmlBuilderService>();
builder.Services.AddScoped<ITimbradoService, TimbradoService>();
builder.Services.AddScoped<IComprobanteFiscalService, ComprobanteFiscalService>();
builder.Services.AddScoped<ITimbradoWorkflow, TimbradoWorkflow>();
builder.Services.AddScoped<IFacturaRepository, FacturaRepository>();

// 🔹 HSTS (para HTTPS en producción)
//builder.Services.AddHsts(options =>
//{
//    options.MaxAge = TimeSpan.FromDays(365);
//});

// 🔹 DbContext (EF Core + Npgsql)
builder.Services.AddDbContext<FacturacionDbContext>(options =>
    options.UseNpgsql(connectionString));

// 🔹 Hangfire
builder.Services.AddHangfire(config =>
{
    config.UsePostgreSqlStorage(connectionString);

    // Los jobs corren fuera del pipeline HTTP, así que CapturaErroresMiddleware
    // no los ve: sin este filtro un recurrente puede llevar semanas fallando en
    // silencio porque nadie presencia el error.
    config.UseFilter(new CapturaErroresJobFilter());
});
builder.Services.AddHangfireServer();

// 🔹 Servicios que usas en jobs
builder.Services.AddScoped<ParidadService>();
builder.Services.AddScoped<ProductoSyncService>();
builder.Services.AddScoped<SalasEstadoService>();
builder.Services.AddScoped<ClientSyncService>();
builder.Services.AddScoped<ProveedorSyncService>();

// 🔹 Servicios de correo (migrados desde EmailSender estático / CorreoHelper estático)
builder.Services.AddScoped<EmailSender>();
builder.Services.AddScoped<CorreoHelper>();

// 🔹 Programa de puntos Socio Tiburón (MySQL externo, sólo lectura)
builder.Services.AddScoped<SocioTiburonService>();

// 🔹 Puntos: cálculo desde Kepler y/o el ERP
builder.Services.AddScoped<PuntosCalculoService>();
builder.Services.AddScoped<PuntosSyncService>();

// 🔹 Servicio de login (para Web API)
builder.Services.AddScoped<LoginService>();

// 🔹 Captura automática de errores → tickets de soporte.
// Se registra para poder inyectarlo, pero el servicio también funciona con
// "new ErrorTicketService()" porque lo llaman middleware y jobs de Hangfire,
// donde no siempre hay un scope de DI disponible.
builder.Services.AddScoped<ErrorTicketService>();
builder.Services.AddScoped<ErroresPodaService>();
builder.Services.AddScoped<ErroresDrenajeService>();
// ErrorCorreoJob lo activa Hangfire por DI: es el worker, no el request, quien
// tiene el scope del que sale CorreoHelper.
builder.Services.AddScoped<ErrorCorreoJob>();

// 🔹 Password hasher (para Web API)
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

// 🔹 Options — equivalente fuertemente tipado a ConfigurationManager.AppSettings.
// Cada Configure<T> lee su sección correspondiente de appsettings.json y la deja
// disponible para inyectar como IOptions<T> en cualquier clase.
builder.Services.Configure<TimbradoOptions>(builder.Configuration.GetSection("Timbrado"));
builder.Services.Configure<ModulaOptions>(builder.Configuration.GetSection("Modula"));
builder.Services.Configure<AutofacturacionOptions>(
    builder.Configuration.GetSection("Autofacturacion"));
// Nota: "Emisores" y "ApiKey" no se registran aquí porque Emisores tiene varias
// subsecciones dinámicas (pruebas/SRS/ITF/AHN/SRV) y ApiKey es un valor suelto;
// ambos se leen directo vía IConfiguration donde se necesiten (ver ejemplo abajo).

// 🔹 Session — requerida porque el controlador usa Session["UsuarioId"], etc.
// Requiere un IDistributedCache; en memoria por defecto (no se comparte entre instancias).
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    //options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Cliente HTTP hacia Ollama
builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri("http://192.168.1.230:11440");
    client.Timeout = TimeSpan.FromSeconds(120);
});

// 🔹 Utilities: los servicios/handlers de refacturación lo reciben por constructor
// para reutilizar sus helpers de acceso a datos (RunQuery/RunScalar/RunUpdate).
// Se construye con el ctor manual para que _configuration quede poblado.
builder.Services.AddScoped<Utilities>(sp => new Utilities(true));

builder.Services.AddScoped<IRefacturacionTypeHandler, DatosFiscalesTypeHandler>();
builder.Services.AddScoped<IRefacturacionTypeHandler, AdendaTypeHandler>();
builder.Services.AddScoped<IRefacturacionTypeHandler, MetodoPagoTypeHandler>();
builder.Services.AddScoped<IRefacturacionTypeHandler, MonedaTypeHandler>();
builder.Services.AddScoped<IRefacturacionTypeHandler, ConceptosTypeHandler>();

builder.Services.AddScoped<IPacService, PacService>();
builder.Services.AddScoped<RefacturacionHandlerFactory>();
builder.Services.AddScoped<RefacturacionOrchestrator>();

// 🔹 Módulo general de generación de PDF de facturas (no exclusivo de refacturación)
builder.Services.AddScoped<BOS_ERP.Services.Facturacion.IFacturaPdfService, BOS_ERP.Services.Facturacion.FacturaPdfService>();
builder.Services.AddScoped<IGemmaService, GemmaService>();
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<IConversacionService, ConversacionService>();

builder.Services.AddHttpContextAccessor();

// 🔹 Cookie Authentication — reemplazo de FormsAuthentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30); // equivalente al "Recordarme"
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // usar Always en producción con HTTPS
    })
    // 🔹 Portal de clientes — esquema SEPARADO a propósito.
    //
    // Los clientes son usuarios externos: no deben poder llegar a ningún módulo del
    // ERP. Con un solo esquema, un cliente autenticado quedaría a un [Authorize] de
    // distancia del resto del sistema. Con dos, la cookie del portal simplemente no
    // sirve para el ERP ni al revés, y el aislamiento no depende de que cada
    // controlador interno esté bien decorado.
    //
    // La cookie va acotada por Path al portal, así que ni siquiera viaja en las
    // peticiones internas.
    .AddCookie(PortalClientesAuth.Scheme, options =>
    {
        options.Cookie.Name = "BOS_PortalClientes";
        options.Cookie.Path = "/PortalClientes";
        options.LoginPath = "/PortalClientes/Acceso";
        options.LogoutPath = "/PortalClientes/Salir";
        options.AccessDeniedPath = "/PortalClientes/Acceso";
        // Sesión corta y sin deslizamiento: es un portal de consulta al que se entra
        // por un rato, no una herramienta de trabajo diaria.
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// 🔹 Middleware pipeline — el orden es importante
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // sirve ~/Scripts, ~/content/css, etc.

app.UseRouting();

app.UseSession(); // después de UseRouting, antes de MapControllers

// 🔹 Captura de errores → ticket automático.
//
// El orden importa en las dos direcciones:
//   * DESPUÉS de UseSession, porque lee UsuarioId/Empresa/Sucursal de la sesión.
//     Registrado antes, la sesión viene vacía y se pierde a quién le falló.
//   * DESPUÉS de UseRouting, para poder nombrar el módulo como "Controlador/Accion"
//     en vez de la ruta cruda.
//   * ANTES de MapControllers, que es lo que hace que envuelva a los controladores.
//
// Sólo observa: registra la excepción y la vuelve a lanzar, así que quien pinta
// la página de error sigue siendo el UseExceptionHandler de arriba.
app.UseMiddleware<CapturaErroresMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

// 🔹 Hubs — van después de UseSession/UseAuthentication porque OnConnectedAsync
// lee la sesión (empresa / correo) para agrupar cada conexión.
app.MapHub<InventarioHub>(InventarioHub.Ruta);
app.MapHub<NotificacionesHub>(NotificacionesHub.Ruta);

// 🔹 Utilities.SendNotificationInterno se llama desde controladores sin constructor
// propio y desde jobs sin request, así que el hub se deja accesible una sola vez aquí.
NotificacionesHubAccessor.Hub = app.Services.GetRequiredService<IHubContext<NotificacionesHub>>();

// 🔹 Hangfire dashboard
app.UseHangfireDashboard("/hangfire");

// 🔹 Jobs recurrentes (después de Build)
RecurringJob.AddOrUpdate<ParidadService>(
    "cargar-paridades",
    x => x.Cargar(),
    "33 9 * * *"
);
RecurringJob.AddOrUpdate<ProductoSyncService>(
    "sincronizar-productos",
    x => x.SincronizarProductos(),
    "33 9 * * *"
);
RecurringJob.AddOrUpdate<SalasEstadoService>(
    "sincronizar-estados-salas",
    x => x.Sincronizar(),
    Cron.Minutely
);

// 🔹 Poda del detalle de errores automáticos.
// De madrugada y fuera de la hora punta de los otros recurrentes (todos a las 9:33):
// borra por lotes grandes y no tiene por qué competir con las sincronizaciones.
RecurringJob.AddOrUpdate<ErroresPodaService>(
    "podar-detalle-errores",
    x => x.Podar(),
    "20 3 * * *"
);

// 🔹 Drenaje de los errores que se quedaron en disco porque la base no respondía.
// Cada 5 minutos: son los errores más graves que registra el sistema y no tiene
// sentido que esperen a la noche. Con la carpeta vacía -el caso normal- el job
// no toca la base siquiera.
RecurringJob.AddOrUpdate<ErroresDrenajeService>(
    "drenar-errores-pendientes",
    x => x.Drenar(),
    "*/5 * * * *"
);

RotativaConfiguration.Setup(app.Environment.ContentRootPath, "Rotativa");

app.Run();
