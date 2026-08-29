using BOS_ERP.Controllers;
using Npgsql;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Guardado y validación de los archivos adjuntos de un ticket.
    ///
    /// Centraliza lo que antes estaba disperso y desalineado entre tres sitios:
    ///
    ///  - El alta de ticket escribía en {ContentRoot}/Adjuntos, pero la descarga leía de
    ///    wwwroot/Adjuntos, así que ningún adjunto se podía descargar jamás.
    ///  - El input del formulario se llamaba "adjuntos" y la acción recibía
    ///    "archivosAdjuntos", de modo que la lista siempre llegaba vacía.
    ///  - El "accept" del input permitía .doc y .xls, que el servidor rechazaba.
    ///
    /// La carpeta vive fuera de wwwroot a propósito: los adjuntos llevan información
    /// interna y no deben servirse como estáticos con una URL adivinable. Se entregan
    /// por SoporteController.DescargarAdjunto, que valida permisos.
    /// </summary>
    public static class SoporteAdjuntos
    {
        /// <summary>Carpeta de adjuntos, relativa al ContentRoot.</summary>
        public const string Carpeta = "Adjuntos";

        /// <summary>Extensiones aceptadas. El "accept" del input debe reflejar esta lista.</summary>
        public static readonly string[] ExtensionesPermitidas =
            { ".pdf", ".jpg", ".jpeg", ".png", ".docx", ".xlsx", ".txt" };

        public const long TamanoMaximoBytes = 5 * 1024 * 1024;   // 5 MB
        public const int MaximoArchivos = 5;

        /// <summary>Para el atributo accept del input file, en el mismo orden.</summary>
        public static string AcceptHtml => string.Join(",", ExtensionesPermitidas);

        /// <summary>
        /// Revisa todo el lote. Devuelve null si está bien, o el mensaje de error.
        ///
        /// Se valida el lote COMPLETO antes de escribir nada: la versión anterior
        /// validaba dentro del bucle de guardado, así que un archivo inválido en la
        /// tercera posición dejaba los dos primeros ya escritos en disco y registrados.
        /// </summary>
        public static string Validar(List<IFormFile> archivos)
        {
            if (archivos == null) return null;

            var reales = archivos.Where(a => a != null && a.Length > 0).ToList();
            if (reales.Count == 0) return null;

            if (reales.Count > MaximoArchivos)
                return $"Puedes adjuntar como máximo {MaximoArchivos} archivos.";

            foreach (var archivo in reales)
            {
                if (archivo.Length > TamanoMaximoBytes)
                    return $"El archivo '{Path.GetFileName(archivo.FileName)}' supera el límite de {TamanoMaximoBytes / (1024 * 1024)} MB.";

                string extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";
                if (!ExtensionesPermitidas.Contains(extension))
                    return $"Tipo de archivo '{extension}' no permitido. Se aceptan: {string.Join(", ", ExtensionesPermitidas)}.";
            }

            return null;
        }

        /// <summary>
        /// Guarda el lote en disco y lo registra en tkts_adj.
        ///
        /// Debe llamarse después de <see cref="Validar"/>. Si se pasa una transacción, el
        /// registro en base va dentro de ella; los archivos en disco no son
        /// transaccionales, así que si la transacción se revierte quedan huérfanos: son
        /// inertes, porque sin fila en tkts_adj no hay forma de pedirlos.
        /// </summary>
        /// <param name="idSeguimiento">
        /// null para los adjuntos del ticket original; el id del seguimiento cuando el
        /// archivo viene de una respuesta del hilo.
        /// </param>
        public static async Task GuardarAsync(
            Utilities db,
            string contentRootPath,
            List<IFormFile> archivos,
            int idTicket,
            int? idSeguimiento = null,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null)
        {
            if (archivos == null) return;

            var reales = archivos.Where(a => a != null && a.Length > 0).ToList();
            if (reales.Count == 0) return;

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
                    "INSERT INTO tkts_adj (n_arch_orig, n_arch_uid, ext, rt_arch, fechacarga, id_tkts, id_seg_tkts) " +
                    "VALUES (@n_arch_orig, @n_arch_uid, @ext, @rt_arch, CURRENT_TIMESTAMP, @id_tkts, @id_seg_tkts)",
                    new Dictionary<string, object>
                    {
                        { "n_arch_orig", nombreOriginal },
                        { "n_arch_uid", uid },
                        { "ext", extension },
                        // Ruta relativa a la carpeta de adjuntos. No es una URL servible:
                        // la descarga siempre pasa por el controlador.
                        { "rt_arch", Path.Combine(Carpeta, uid + extension).Replace("\\", "/") },
                        { "id_tkts", idTicket },
                        { "id_seg_tkts", idSeguimiento.HasValue ? (object)idSeguimiento.Value : DBNull.Value }
                    },
                    false, conn, tx);
            }
        }
    }
}
