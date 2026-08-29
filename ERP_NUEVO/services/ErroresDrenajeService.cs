using System.Text.Json;
using BOS_ERP.Models;

namespace BOS_ERP.Services
{
    /// <summary>
    /// Mete a la base los errores que se quedaron en disco.
    ///
    /// EL PROBLEMA QUE CIERRA
    /// ErrorTicketService escribe en la misma base que puede haberse caído. Cuando
    /// eso pasa, cae a un NDJSON en App_Data/errores-pendientes/ y ahí se queda. Sin
    /// este job, las caídas de infraestructura -las más graves- serían las únicas
    /// que no dejan ticket.
    ///
    /// SÍ, HANGFIRE TAMBIÉN USA ESA BASE
    /// Así que durante la caída este job tampoco corre. No importa: el objetivo no
    /// es drenar durante el incidente sino después, y el archivo aguanta mientras
    /// tanto. Al volver el servicio, la deduplicación colapsa las miles de líneas en
    /// unas pocas huellas y el cortacircuitos limita los tickets, así que una caída
    /// de dos horas termina en uno o dos tickets con el contador alto — que es
    /// exactamente lo que se quiere leer.
    /// </summary>
    public sealed class ErroresDrenajeService
    {
        /// <summary>
        /// Tope de líneas por corrida. Una caída larga puede dejar un archivo
        /// enorme; conviene que el job termine y siga en la siguiente vuelta antes
        /// que ocupar un worker media hora.
        /// </summary>
        private const int MaxPorCorrida = 5000;

        /// <summary>
        /// Dónde viven los respaldos. La escriben ErrorTicketService y la lee este
        /// job, así que la ruta se resuelve en un solo sitio.
        ///
        /// NO cuelga de AppContext.BaseDirectory. Ésa es la carpeta de salida
        /// (bin/Debug/net8.0), y un rebuild o un despliegue la reemplaza entera: se
        /// perderían justo los errores del incidente que motivó el respaldo. Por
        /// defecto va al content root, y CapturaErrores:CarpetaRespaldo permite
        /// apuntarla a una ruta estable fuera del directorio de la aplicación.
        /// </summary>
        public static string RutaCarpeta(IConfiguration cfg)
        {
            string configurada = cfg?["CapturaErrores:CarpetaRespaldo"];

            if (!string.IsNullOrWhiteSpace(configurada))
                return configurada;

            return Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "errores-pendientes");
        }

        private readonly IConfiguration _cfg;

        public ErroresDrenajeService(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        private string Carpeta => RutaCarpeta(_cfg);

        public void Drenar()
        {
            try
            {
                if (!Directory.Exists(Carpeta)) return;

                // .ndjson son los que están vivos; .procesando son los de una corrida
                // anterior que murió a medias y hay que retomar.
                foreach (string archivo in Directory.GetFiles(Carpeta, "*.procesando"))
                    Procesar(archivo);

                foreach (string archivo in Directory.GetFiles(Carpeta, "*.ndjson"))
                {
                    // Renombrar antes de leer es lo que evita la carrera con el
                    // proceso que sigue escribiendo: tras el rename, el siguiente
                    // AppendAllText crea un archivo nuevo y este queda congelado.
                    string reservado = archivo + ".procesando";

                    try
                    {
                        File.Move(archivo, reservado);
                    }
                    catch (IOException)
                    {
                        // Alguien lo tenía abierto justo en ese instante. Se intenta
                        // en la siguiente vuelta; no se pierde nada.
                        continue;
                    }

                    Procesar(reservado);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CapturaErrores] Falló el drenaje: {ex.Message}");
            }
        }

        private void Procesar(string archivo)
        {
            var servicio = new ErrorTicketService();
            int leidas = 0, registradas = 0, malas = 0;

            try
            {
                var pendientes = new List<string>();
                bool corto = false;

                foreach (string linea in File.ReadLines(archivo))
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;

                    // Pasado el tope, el resto se guarda para la próxima corrida.
                    if (leidas >= MaxPorCorrida) { pendientes.Add(linea); corto = true; continue; }

                    leidas++;
                    if (Registrar(servicio, linea)) registradas++; else malas++;
                }

                if (corto)
                {
                    // Se reescribe sólo lo que faltó. Si el proceso muere aquí, el
                    // archivo sigue siendo .procesando y la siguiente corrida lo
                    // retoma; el costo de ese fallo es reprocesar líneas, que la
                    // deduplicación absorbe sin crear tickets de más.
                    File.WriteAllLines(archivo, pendientes);
                }
                else
                {
                    File.Delete(archivo);
                }

                Console.WriteLine(
                    $"[CapturaErrores] Drenaje de {Path.GetFileName(archivo)}: " +
                    $"{registradas} registradas, {malas} ilegibles" +
                    (corto ? $", {pendientes.Count} para la próxima vuelta." : "."));
            }
            catch (Exception ex)
            {
                // El archivo se queda como .procesando a propósito: es la señal de
                // que hay que retomarlo. Si la base sigue caída, esto vuelve a pasar
                // en la siguiente vuelta hasta que se recupere.
                Console.WriteLine(
                    $"[CapturaErrores] Drenaje interrumpido en {Path.GetFileName(archivo)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Reconstruye el error de una línea y lo registra. La huella vuelve a
        /// calcularse a partir de tipo + mensaje + origen, los tres guardados, así
        /// que sale la misma que habría salido en su momento y se agrupa con las
        /// ocurrencias que sí llegaron a la base.
        /// </summary>
        private static bool Registrar(ErrorTicketService servicio, string linea)
        {
            try
            {
                using var doc = JsonDocument.Parse(linea);
                var raiz = doc.RootElement;

                string Texto(string campo) =>
                    raiz.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() : null;

                int? Entero(string campo) =>
                    raiz.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number
                        ? v.GetInt32() : null;

                var e = new ErrorCapturado
                {
                    Capa = Texto("capa") ?? "middleware",
                    Modulo = Texto("modulo"),
                    TipoExc = Texto("tipoExc"),
                    Mensaje = Texto("mensaje"),
                    Stack = Texto("stack"),
                    Origen = Texto("origen"),
                    Ruta = Texto("ruta"),
                    Metodo = Texto("metodo"),
                    UsuarioId = Entero("idUsr"),
                    EmpresaId = Entero("empresaId"),
                    SucursalId = Entero("sucursalId")
                };

                // Queda constancia de que esta ocurrencia viene de disco: su fecha de
                // registro será la del drenaje, no la del error, y sin esto nadie
                // podría explicarse el desfase al leer el ticket.
                e.Extra["respaldo"] = new
                {
                    origen = "App_Data/errores-pendientes",
                    fchOriginal = Texto("fch"),
                    falloAlGuardar = Texto("falloAlGuardar")
                };

                servicio.Registrar(e);
                return true;
            }
            catch
            {
                // Una línea truncada -típico si el proceso murió a media escritura-
                // se descarta. Perder una línea no puede costar las otras 4 999.
                return false;
            }
        }
    }
}
