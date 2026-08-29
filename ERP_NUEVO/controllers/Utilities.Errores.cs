using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers
{
    /// <summary>
    /// Capa 2 de la captura de errores: los catch que devuelven JSON.
    ///
    /// POR QUÉ VIVE EN Utilities
    /// 184 controladores heredan de esta clase, así que ponerlo aquí lo deja
    /// disponible en todos sin tocar ninguno. Y como Utilities es partial, el
    /// helper cabe en un archivo nuevo sin meter mano en sus 2 800 líneas.
    ///
    /// QUÉ PROBLEMA RESUELVE
    /// El proyecto tiene ~300 catch con esta forma:
    ///
    ///     catch (Exception ex)
    ///     {
    ///         return Json(new { success = false, message = "Error: " + ex.Message });
    ///     }
    ///
    /// Responden HTTP 200, así que CapturaErroresMiddleware nunca las ve: para el
    /// pipeline la petición fue exitosa. Además le enseñan al usuario el mensaje
    /// crudo de la excepción, que no le sirve y a veces filtra nombres de tablas.
    /// </summary>
    public partial class Utilities
    {
        /// <summary>Lo que se le dice al usuario cuando el ticket sí se creó.</summary>
        private const string AvisoConTicket =
            "Se levantó un ticket de soporte y Sistemas ya está trabajando en solucionarlo.";

        /// <summary>
        /// Y cuando no. No puede prometer que alguien lo está viendo: si el
        /// cortacircuitos está abierto o la base caída, nadie lo está viendo.
        /// </summary>
        private const string AvisoSinTicket =
            "Vuelve a intentarlo; si sigue pasando, repórtalo a Sistemas.";

        /// <summary>
        /// Registra la excepción, levanta el ticket y devuelve la respuesta que el
        /// front ya sabe leer.
        ///
        /// Reemplaza al catch de arriba así:
        ///
        ///     catch (Exception ex)
        ///     {
        ///         return ErrorConTicket(ex, "Ventas/PuntoDeVenta", "No se pudo registrar la venta");
        ///     }
        ///
        /// El mensaje de la excepción NO viaja al usuario: ya quedó completo, con
        /// stack y datos de Postgres, en el ticket.
        /// </summary>
        /// <param name="ex">La excepción capturada.</param>
        /// <param name="modulo">Cómo identificar el módulo, p. ej. "Ventas/PuntoDeVenta".</param>
        /// <param name="mensajeUsuario">Qué salió mal, en términos del usuario. Sin punto final.</param>
        protected JsonResult ErrorConTicket(Exception ex, string modulo, string mensajeUsuario = null)
        {
            string folio = RegistrarErrorParaTicket(ex, modulo);

            string texto = string.IsNullOrWhiteSpace(mensajeUsuario)
                ? "Ocurrió un error en el sistema"
                : mensajeUsuario.TrimEnd('.', ' ');

            string completo = $"{texto}. {(folio != null ? AvisoConTicket : AvisoSinTicket)}";

            return Json(new
            {
                success = false,
                // El mismo texto en los dos nombres: el front lee "message" en unos
                // lados y "error" en otros (281 contra 64 usos en el proyecto), y el
                // helper no puede saber cuál espera la vista que llamó.
                error = completo,
                message = completo,
                // Marca de falla TÉCNICA. Es lo que distingue esto de una validación
                // de negocio -"Todos los campos son obligatorios"- que también
                // responde success:false. Sin esta marca, la red del navegador
                // levantaría un ticket cada vez que alguien deja un campo vacío.
                esError = true,
                ticketGenerado = folio != null
            });
        }

        /// <summary>
        /// La variante para cuando el catch tiene que devolver campos extra propios.
        /// Registra igual y devuelve el folio (o null) para que el llamador arme su
        /// respuesta como la necesite.
        /// </summary>
        protected string RegistrarErrorParaTicket(Exception ex, string modulo)
            => RegistrarError(ex, modulo, HttpContext);

        /// <summary>
        /// La misma captura, para controladores que NO heredan de Utilities.
        ///
        /// No todos lo hacen: AutoFacturacionPublicaController, por ejemplo, deriva
        /// de Controller a secas porque es el portal público de autofacturación y no
        /// necesita los helpers de datos. Sin esta versión estática habría que
        /// cambiarle la clase base -tocando la jerarquía de un controlador expuesto a
        /// internet- sólo para poder registrar un error.
        /// </summary>
        /// <summary>
        /// Marca que deja una excepción ya registrada, para que no se cuente dos veces.
        ///
        /// EL PROBLEMA QUE RESUELVE
        /// Un error de SQL dentro de un módulo migrado recorre dos registros: primero
        /// la capa de datos lo atrapa en RunQuery y lo relanza, después el catch del
        /// controlador lo captura y vuelve a registrarlo. Eso producía DOS huellas
        /// -distinta capa, distinto origen- para un solo incidente: dos tickets, dos
        /// contadores y el cortacircuitos gastándose al doble de velocidad.
        ///
        /// La marca viaja pegada a la excepción, así que funciona sin importar cuántos
        /// niveles suba ni quién la termine atrapando.
        /// </summary>
        private const string MarcaRegistrado = "BOS_ErrorYaRegistrado";

        /// <summary>¿Ya la registró alguien más abajo? Se revisa la cadena completa.</summary>
        public static bool YaRegistrado(Exception ex)
        {
            int nivel = 0;
            while (ex != null && nivel < 8)
            {
                try
                {
                    if (ex.Data != null && ex.Data.Contains(MarcaRegistrado)) return true;
                }
                catch { }

                ex = ex.InnerException;
                nivel++;
            }
            return false;
        }

        /// <summary>Deja la marca. Data puede ser de sólo lectura en algunos tipos.</summary>
        public static void MarcarRegistrado(Exception ex)
        {
            try
            {
                if (ex?.Data != null && !ex.Data.IsReadOnly) ex.Data[MarcaRegistrado] = true;
            }
            catch { }
        }

        /// <param name="capturarParametros">
        /// false en endpoints PÚBLICOS. La sanitización tapa por nombre de campo
        /// (contraseñas, tokens, CSD), pero no puede saber que en el portal de
        /// autofacturación el "rfc", la "razon" y el "email" son datos de una persona
        /// que ni siquiera tiene cuenta en el ERP. Guardarlos los dejaría a la vista
        /// de todo el staff de soporte en cada error, así que ahí no se capturan: la
        /// ruta y la excepción bastan para diagnosticar.
        /// </param>
        public static string RegistrarError(
            Exception ex, string modulo, HttpContext ctx, bool capturarParametros = true)
        {
            try
            {
                // Ya la registró la capa de datos al pasar por RunQuery/RunScalar, con
                // más detalle del que hay aquí arriba (SqlState, tabla, constraint).
                if (YaRegistrado(ex)) return null;

                var e = ErrorCapturado.Desde(ex, "controlador", modulo);

                if (ctx != null)
                {
                    e.Ruta = ctx.Request.Path;   // sin QueryString: puede llevar los datos
                    e.Metodo = ctx.Request.Method;
                    e.TraceId = ctx.TraceIdentifier;
                    e.Ip = ctx.Connection.RemoteIpAddress?.ToString();
                    e.UserAgent = ctx.Request.Headers.UserAgent.ToString();

                    LeerSesion(e, ctx);

                    if (capturarParametros)
                    {
                        e.Ruta = ctx.Request.Path + ctx.Request.QueryString;
                        LeerParametros(e, ctx);
                    }
                }

                MarcarRegistrado(ex);
                return new ErrorTicketService().Registrar(e);
            }
            catch
            {
                // El catch original ya está manejando un error: éste no puede añadir
                // otro encima. Se pierde el ticket, no la respuesta al usuario.
                return null;
            }
        }

        // ====================================================================
        // Rechazos del PAC / SAT
        // ====================================================================

        /// <summary>
        /// Registra un rechazo del PAC como error del sistema.
        ///
        /// NO ES UNA EXCEPCIÓN, y por eso no lo veía ninguna otra capa: el timbrado
        /// devuelve un TimbradoResult con Success = false y el controlador responde
        /// un JSON normal. Nada revienta.
        ///
        /// POR QUÉ REGISTRARLO DE TODOS MODOS
        /// Un rechazo suelto suele ser un dato mal capturado que el usuario corrige.
        /// Pero agrupado por huella deja de ser anécdota: "este rechazo pasó 40 veces
        /// esta semana" apunta a un problema de configuración o de captura que vale
        /// la pena arreglar de raíz. Esa lectura es justo lo que da el tablero.
        ///
        /// Entra con prioridad media a propósito: importa, pero no debe competir con
        /// una excepción que dejó al ERP a medias.
        /// </summary>
        /// <param name="mensajePac">El texto tal cual lo devolvió el PAC.</param>
        /// <param name="modulo">Desde dónde se timbró, p. ej. "Ventas/Industrial".</param>
        protected void RegistrarRechazoPac(string mensajePac, string modulo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mensajePac)) return;

                var e = new ErrorCapturado
                {
                    Capa = "pac",
                    Modulo = modulo,
                    TipoExc = "PAC.Rechazo",
                    Mensaje = mensajePac.Length > 2000 ? mensajePac.Substring(0, 2000) : mensajePac,
                    // El código del SAT (CFDI40147, CRP20105...) es lo que de verdad
                    // identifica el tipo de rechazo, así que es lo que va al origen:
                    // así todas las veces que falle por el mismo motivo caen en una
                    // sola huella aunque cambien folio, RFC e importes.
                    Origen = CodigoPac(mensajePac) ?? modulo
                };

                if (HttpContext != null)
                {
                    e.Ruta = HttpContext.Request.Path + HttpContext.Request.QueryString;
                    e.Metodo = HttpContext.Request.Method;
                    e.TraceId = HttpContext.TraceIdentifier;
                    LeerSesionParaError(e);
                    LeerParametrosParaError(e);
                }

                new ErrorTicketService().Registrar(e);
            }
            catch
            {
                // El rechazo ya se le devolvió al usuario: registrarlo es extra.
            }
        }

        /// <summary>
        /// Extrae el código de error del SAT del mensaje, si lo trae. Son de la forma
        /// CFDI40147, CRP20105, o cuatro dígitos sueltos según el PAC.
        /// </summary>
        private static string CodigoPac(string mensaje)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                mensaje, @"\b(CFDI|CRP|CP|NOM|PAG)\d{3,6}\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return m.Success ? "PAC:" + m.Value.ToUpperInvariant() : null;
        }

        // ====================================================================
        // Instrumentación de la capa de datos
        // ====================================================================

        /// <summary>
        /// Impide que el registro de un error provoque otro.
        ///
        /// ErrorTicketService consulta la base para deduplicar y crear el ticket, y
        /// esas consultas pasan por RunQuery. Si una de ellas fallara, intentaría
        /// registrarse a sí misma, y esa nueva consulta también fallaría: bucle
        /// infinito que se lleva el worker por delante. La bandera corta el ciclo
        /// en la primera vuelta.
        ///
        /// AsyncLocal y no ThreadStatic: el pipeline de ASP.NET salta de hilo entre
        /// awaits, y con ThreadStatic la bandera se perdería justo a la mitad.
        /// </summary>
        private static readonly AsyncLocal<bool> _registrandoError = new();

        /// <summary>
        /// Suprime el registro dentro de un bloque, para los errores de SQL que el
        /// llamador provoca A PROPÓSITO y ya maneja.
        ///
        /// El caso real es TicketFactory: genera un folio al azar y usa la violación
        /// de índice único (23505) como señal para reintentar con otro. Sin esto,
        /// cada colisión de folio -comportamiento esperado y correcto- ensuciaría la
        /// telemetría con un error que no lo es.
        /// </summary>
        public static IDisposable SinRegistroDeErrores() => new SupresionErrores();

        private sealed class SupresionErrores : IDisposable
        {
            private readonly bool _previo;

            public SupresionErrores()
            {
                _previo = _registrandoError.Value;
                _registrandoError.Value = true;
            }

            public void Dispose() => _registrandoError.Value = _previo;
        }

        /// <summary>
        /// Registra un error de Postgres desde la capa de datos.
        ///
        /// POR QUÉ AQUÍ Y NO EN CADA CATCH
        /// Las 2 320 llamadas a RunQuery/RunUpdate/RunScalar de los 209 archivos del
        /// ERP pasan por este punto. Registrar aquí captura CUALQUIER error de SQL
        /// con SqlState, tabla y constraint, sin importar quién lo atrape después ni
        /// cómo lo devuelva al usuario.
        ///
        /// Y es el único sitio donde la excepción llega entera. Más arriba se pierde:
        /// GenerarDatosPoliza, por ejemplo, hace "throw new Exception(ex.Message)", y
        /// a partir de ahí ya no hay ni tipo ni stack ni SqlState que registrar.
        ///
        /// Nunca lanza y nunca cambia el comportamiento: quien llamó sigue recibiendo
        /// su excepción original, con su stack intacto.
        /// </summary>
        protected void RegistrarErrorDeDatos(PostgresException ex, string query)
        {
            if (_registrandoError.Value) return;

            try
            {
                _registrandoError.Value = true;

                var e = ErrorCapturado.Desde(ex, "datos", ModuloDesdeRuta());

                // El origen sale de la PILA VIVA y no de ex.StackTrace: atrapada aquí,
                // la excepción aún no ha propagado al controlador, así que su stack
                // termina en RunQuery/RunScalar. Sin esto, TODOS los errores de SQL
                // del ERP se identificarían con el mismo renglón de Utilities.cs y el
                // ticket no diría a quién le toca arreglarlo.
                e.Origen = Helpers.ErrorFingerprint.OrigenDesdePila(
                    new System.Diagnostics.StackTrace(true));

                // El SQL va tal cual porque está parametrizado: lleva @nombre, no los
                // valores. Sin él, un "no existe la columna" no dice cuál consulta.
                e.Extra["sql"] = query != null && query.Length > 2000
                    ? query.Substring(0, 2000) + "…"
                    : query;

                if (HttpContext != null)
                {
                    e.Ruta = HttpContext.Request.Path + HttpContext.Request.QueryString;
                    e.Metodo = HttpContext.Request.Method;
                    e.TraceId = HttpContext.TraceIdentifier;
                    e.Ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    LeerSesionParaError(e);
                }

                new ErrorTicketService().Registrar(e);

                // Se marca DESPUES de registrar: si algo falla arriba, el catch del
                // controlador todavia puede intentarlo por su cuenta.
                MarcarRegistrado(ex);
            }
            catch
            {
                // Un fallo registrando no puede tocar la excepción que va de subida.
            }
            finally
            {
                _registrandoError.Value = false;
            }
        }

        /// <summary>
        /// "Ventas/Industrial" a partir de la petición en curso. Sin HttpContext
        /// -jobs de Hangfire, helpers estáticos- se marca como tal para que el ticket
        /// no mienta sobre su procedencia.
        /// </summary>
        private string ModuloDesdeRuta()
        {
            try
            {
                var valores = HttpContext?.GetRouteData()?.Values;
                string controlador = valores?["controller"]?.ToString();
                string accion = valores?["action"]?.ToString();

                if (!string.IsNullOrWhiteSpace(controlador))
                    return string.IsNullOrWhiteSpace(accion) ? controlador : $"{controlador}/{accion}";
            }
            catch { }

            return "SQL (sin petición)";
        }

        private void LeerSesionParaError(ErrorCapturado e) => LeerSesion(e, HttpContext);

        private static void LeerSesion(ErrorCapturado e, HttpContext ctx)
        {
            try
            {
                if (ctx?.Session == null || !ctx.Session.IsAvailable) return;

                e.UsuarioId = ctx.Session.GetInt32("UsuarioId");
                e.EmpresaId = ctx.Session.GetInt32("Empresa");
                e.SucursalId = ctx.Session.GetInt32("Sucursal");
            }
            catch { }
        }

        /// <summary>
        /// Query y formulario, sanitizados en ErrorCapturado. En esta capa el
        /// formulario YA fue leído por el binding del controlador, así que acceder a
        /// .Form no rebobina nada ni consume el cuerpo.
        /// </summary>
        private void LeerParametrosParaError(ErrorCapturado e) => LeerParametros(e, HttpContext);

        private static void LeerParametros(ErrorCapturado e, HttpContext ctx)
        {
            try
            {
                if (ctx == null) return;

                if (ctx.Request.Query.Count > 0)
                {
                    e.AgregarParametros("query", ctx.Request.Query
                        .Select(q => new KeyValuePair<string, string>(q.Key, q.Value.ToString())));
                }

                if (ctx.Request.HasFormContentType && ctx.Request.Form.Count > 0)
                {
                    e.AgregarParametros("form", ctx.Request.Form
                        .Select(f => new KeyValuePair<string, string>(f.Key, f.Value.ToString())));
                }
            }
            catch { }
        }
    }
}
