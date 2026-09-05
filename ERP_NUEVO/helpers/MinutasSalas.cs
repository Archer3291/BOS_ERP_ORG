using BOS_ERP.Controllers;
using Npgsql;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Guardado y validación de las minutas de una reservación de sala.
    ///
    /// La minuta es el acta o el plan que salió de la junta. Se sube cuando la
    /// reservación ya terminó, y se limita a tres PDFs por reservación: si hacen
    /// falta más, es que se está usando esto como repositorio de documentos y no
    /// como cierre de una reunión.
    ///
    /// La carpeta vive fuera de wwwroot a propósito, igual que la de
    /// <see cref="SoporteAdjuntos"/>: una minuta lleva información interna y no
    /// debe servirse como estático con una URL adivinable. Se entrega por
    /// RecursosHumanosController.DescargarMinuta, que valida permisos.
    /// </summary>
    public static class MinutasSalas
    {
        /// <summary>Carpeta de minutas, relativa al ContentRoot.</summary>
        public const string Carpeta = "Minutas";

        /// <summary>Sólo PDF. El "accept" del input y el CHECK de la tabla dicen lo mismo.</summary>
        public static readonly string[] ExtensionesPermitidas = { ".pdf" };

        public const long TamanoMaximoBytes = 10 * 1024 * 1024;   // 10 MB
        public const int MaximoArchivos = 3;

        /// <summary>
        /// Tope de la petición completa, con holgura para el sobre multipart.
        ///
        /// Kestrel corta en 30 MB por omisión y el lote máximo -tres PDFs de 10 MB- se
        /// pasa por poco: sin esto la subida moría en un 413 sin cuerpo, que el
        /// calendario mostraba como un error de red en vez del mensaje de validación.
        /// </summary>
        public const long TamanoMaximoPeticionBytes = MaximoArchivos * TamanoMaximoBytes + (2 * 1024 * 1024);

        /// <summary>Para el atributo accept del input file.</summary>
        public static string AcceptHtml => string.Join(",", ExtensionesPermitidas);

        /// <summary>
        /// Revisa el lote completo antes de escribir nada. Devuelve null si está bien,
        /// o el mensaje de error.
        /// </summary>
        /// <param name="yaGuardados">
        /// Minutas que la reservación ya tiene. El límite es de 3 en total, no de 3
        /// por subida: sin esto se podrían acumular de tres en tres.
        /// </param>
        public static string Validar(List<IFormFile> archivos, int yaGuardados = 0)
        {
            var reales = (archivos ?? new List<IFormFile>()).Where(a => a != null && a.Length > 0).ToList();

            if (reales.Count == 0)
                return "Selecciona al menos un archivo PDF.";

            if (yaGuardados + reales.Count > MaximoArchivos)
                return yaGuardados == 0
                    ? $"Puedes subir como máximo {MaximoArchivos} minutas por reservación."
                    : $"Esta reservación ya tiene {yaGuardados} de {MaximoArchivos} minutas: sólo puedes subir {MaximoArchivos - yaGuardados} más.";

            foreach (var archivo in reales)
            {
                if (archivo.Length > TamanoMaximoBytes)
                    return $"El archivo '{Path.GetFileName(archivo.FileName)}' supera el límite de {TamanoMaximoBytes / (1024 * 1024)} MB.";

                string extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";
                if (!ExtensionesPermitidas.Contains(extension))
                    return $"'{Path.GetFileName(archivo.FileName)}' no es un PDF. La minuta debe subirse en PDF.";
            }

            return null;
        }

        /// <summary>
        /// Guarda el lote en disco y lo registra en reservaciones_archivos.
        ///
        /// Debe llamarse después de <see cref="Validar"/>. Si se pasa una transacción,
        /// el registro en base va dentro de ella; los archivos en disco no son
        /// transaccionales, así que si la transacción se revierte quedan huérfanos:
        /// son inertes, porque sin fila en reservaciones_archivos nadie puede pedirlos.
        /// </summary>
        /// <returns>Cuántas minutas se guardaron.</returns>
        public static async Task<int> GuardarAsync(
            Utilities db,
            string contentRootPath,
            List<IFormFile> archivos,
            int salaReservadaId,
            int subidoPor,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null)
        {
            var reales = (archivos ?? new List<IFormFile>()).Where(a => a != null && a.Length > 0).ToList();
            if (reales.Count == 0) return 0;

            string rutaCarpeta = Path.Combine(contentRootPath, Carpeta);
            Directory.CreateDirectory(rutaCarpeta);   // no-op si ya existe

            foreach (var archivo in reales)
            {
                string extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";
                string nombreOriginal = Path.GetFileName(archivo.FileName);
                Guid uid = Guid.NewGuid();
                string rutaCompleta = Path.Combine(rutaCarpeta, uid + extension);

                using (var stream = new FileStream(rutaCompleta, FileMode.Create))
                {
                    await archivo.CopyToAsync(stream);
                }

                db.RunUpdate(
                    "INSERT INTO reservaciones_archivos " +
                    "   (sala_reservada_id, path, nombre_original, uuid, extension, fecha, subido_por) " +
                    "VALUES " +
                    "   (@sala_reservada_id, @path, @nombre_original, @uuid, @extension, NOW(), @subido_por)",
                    new Dictionary<string, object>
                    {
                        { "sala_reservada_id", salaReservadaId },
                        // Ruta relativa a la carpeta de minutas. No es una URL servible:
                        // la descarga siempre pasa por el controlador.
                        { "path", Path.Combine(Carpeta, uid + extension).Replace("\\", "/") },
                        { "nombre_original", nombreOriginal },
                        { "uuid", uid },
                        { "extension", extension },
                        { "subido_por", subidoPor }
                    },
                    false, conn, tx);
            }

            return reales.Count;
        }
    }
}
