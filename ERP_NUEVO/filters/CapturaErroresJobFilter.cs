using BOS_ERP.Models;
using BOS_ERP.Services;
using Hangfire.Server;

namespace BOS_ERP.Filters
{
    /// <summary>
    /// La captura de errores para los jobs de Hangfire.
    ///
    /// POR QUÉ HACE FALTA
    /// Los recurrentes de Program.cs -cargar-paridades, sincronizar-productos,
    /// sincronizar-estados-salas- corren en su propio hilo, fuera del pipeline
    /// HTTP. CapturaErroresMiddleware no los ve, así que sin este filtro un job
    /// nocturno puede llevar semanas fallando en silencio: es justo el tipo de
    /// error que nadie reporta porque nadie lo presencia.
    ///
    /// OJO CON LOS REINTENTOS
    /// Hangfire reintenta un job fallido varias veces. Cada intento pasa por aquí,
    /// así que llegan varias ocurrencias del mismo error; la deduplicación por
    /// huella las colapsa en un solo ticket con su contador, que es exactamente
    /// para lo que existe.
    /// </summary>
    public sealed class CapturaErroresJobFilter : IServerFilter
    {
        public void OnPerforming(PerformingContext filterContext)
        {
            // Nada que hacer antes de ejecutar: este filtro sólo observa fallas.
        }

        public void OnPerformed(PerformedContext filterContext)
        {
            try
            {
                if (filterContext?.Exception == null) return;

                var e = ErrorCapturado.Desde(filterContext.Exception, "job", Modulo(filterContext));

                // Un job no tiene ruta ni usuario. Se deja constancia de cuál era
                // para que el ticket diga algo útil en vez de quedar huérfano.
                e.Ruta = $"hangfire://{e.Modulo}";
                e.Metodo = "JOB";
                e.TraceId = filterContext.BackgroundJob?.Id;

                e.Extra["hangfire"] = new
                {
                    jobId = filterContext.BackgroundJob?.Id,
                    creado = filterContext.BackgroundJob?.CreatedAt,
                    servidor = filterContext.ServerId
                };

                new ErrorTicketService().Registrar(e);
            }
            catch
            {
                // Un fallo registrando no puede tumbar al worker de Hangfire.
            }
        }

        /// <summary>"ParidadService/Cargar", que es como se identifica el job en el ticket.</summary>
        private static string Modulo(PerformedContext ctx)
        {
            try
            {
                var job = ctx.BackgroundJob?.Job;
                if (job == null) return "Hangfire";

                return $"{job.Type?.Name}/{job.Method?.Name}";
            }
            catch
            {
                return "Hangfire";
            }
        }
    }
}
