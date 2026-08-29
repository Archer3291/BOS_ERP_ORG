using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Calcula la "huella" de un error: el identificador que hace que dos
    /// ocurrencias del MISMO problema se reconozcan como una sola.
    ///
    /// POR QUÉ IMPORTA
    /// Sin normalizar, "no existe el folio 4471" y "no existe el folio 4472" son
    /// cadenas distintas y producirían dos tickets. Con 40 usuarios pegándole a
    /// una consulta rota, el módulo genera 40 tickets en una hora y queda
    /// inservible. La huella es lo único que impide eso.
    /// </summary>
    public static class ErrorFingerprint
    {
        // Los patrones van del más específico al más general a propósito: si
        // {n} corriera primero, se comería los dígitos de los GUIDs y de las
        // fechas y esos patrones ya no encontrarían nada que sustituir.
        private static readonly (Regex Patron, string Marca)[] Normalizaciones =
        {
            // GUID / UUID (los de facturación aparecen en casi todo mensaje del SAT)
            (new Regex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
                       RegexOptions.IgnoreCase | RegexOptions.Compiled), "{guid}"),

            // Fechas ISO y con hora
            (new Regex(@"\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2}(\.\d+)?)?)?",
                       RegexOptions.Compiled), "{fecha}"),

            // Fechas dd/mm/aaaa
            (new Regex(@"\b\d{1,2}/\d{1,2}/\d{2,4}\b", RegexOptions.Compiled), "{fecha}"),

            // Literales entrecomillados: casi siempre son el valor concreto que
            // falló ('CL123', "VIFAC-26-4471"), no el tipo de problema.
            (new Regex(@"'[^']*'", RegexOptions.Compiled), "{s}"),
            (new Regex("\"[^\"]*\"", RegexOptions.Compiled), "{s}"),

            // Cualquier número de 3 o más dígitos: folios, ids, importes.
            // Se dejan pasar los de 1-2 dígitos porque suelen ser parte del
            // mensaje real ("se esperaban 2 partidas") y distinguen problemas.
            (new Regex(@"\b\d{3,}\b", RegexOptions.Compiled), "{n}"),
        };

        /// <summary>Namespace raíz del proyecto: define qué frame del stack es "nuestro".</summary>
        private const string NamespaceProyecto = "BOS_ERP";

        /// <summary>
        /// Archivos de infraestructura que aparecen en el stack de CASI TODOS los
        /// errores y no dicen nada sobre dónde está el problema.
        ///
        /// La primera versión no los filtraba y la prueba lo destapó: un
        /// PostgresException registró su origen como "Utilities.cs:254", que es el
        /// ExecuteReader compartido. Sin este filtro, todos los errores de SQL del
        /// ERP -de cualquier módulo- apuntarían al mismo renglón y el ticket no
        /// diría a quién le toca arreglarlo.
        /// </summary>
        private static readonly string[] ArchivosInfraestructura =
        {
            "Utilities.cs",
            "LogErrorHelper.cs",
            "TicketFactory.cs",
            "ErrorTicketService.cs",
            "ErrorFingerprint.cs",
            "CapturaErroresMiddleware.cs"
        };

        /// <summary>
        /// Deja el mensaje sin los valores concretos que cambian entre ocurrencias.
        /// </summary>
        public static string NormalizarMensaje(string mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje)) return "";

            string texto = mensaje;
            foreach (var (patron, marca) in Normalizaciones)
                texto = patron.Replace(texto, marca);

            // Espacios y saltos de línea colapsados: un mismo error formateado
            // distinto no debe contar como dos.
            texto = Regex.Replace(texto, @"\s+", " ").Trim();

            return texto.Length > 1000 ? texto.Substring(0, 1000) : texto;
        }

        /// <summary>
        /// El primer frame del stack que pertenece a BOS_ERP, como "Archivo.cs:123".
        ///
        /// Es lo que de verdad identifica DÓNDE se rompió. Sin esto, todos los
        /// NullReferenceException del ERP colapsarían en una sola huella y el
        /// ticket sería inútil.
        /// </summary>
        public static string ExtraerOrigen(string stack)
        {
            if (string.IsNullOrWhiteSpace(stack)) return "(sin stack)";

            // Dos pasadas: primero se busca el frame que de verdad ubica el problema
            // -código de negocio-, y sólo si no hay ninguno se acepta uno de
            // infraestructura. Un error de SQL debe apuntar al controlador que lanzó
            // la consulta, no al ExecuteReader compartido por todo el ERP.
            return BuscarFrame(stack, saltarInfraestructura: true)
                ?? BuscarFrame(stack, saltarInfraestructura: false)
                ?? PrimeraLinea(stack);
        }

        private static string BuscarFrame(string stack, bool saltarInfraestructura)
        {
            foreach (string linea in stack.Split('\n'))
            {
                if (linea.IndexOf(NamespaceProyecto, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (saltarInfraestructura &&
                    ArchivosInfraestructura.Any(a => linea.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                // Formato del CLR: "   en Espacio.Clase.Metodo() en C:\ruta\Archivo.cs:línea 123"
                // El número de línea sólo existe si el build trae los .pdb; si no,
                // se cae al nombre del método, que sigue siendo mucho mejor que nada.
                var conLinea = Regex.Match(linea, @"([^\\/]+\.cs):\D*(\d+)");
                if (conLinea.Success)
                    return $"{conLinea.Groups[1].Value}:{conLinea.Groups[2].Value}";

                var metodo = Regex.Match(linea, NamespaceProyecto + @"[\w\.]*\.(\w+\.\w+)\s*\(");
                if (metodo.Success)
                    return metodo.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// El origen tomado de la PILA VIVA, no del stack de la excepción.
        ///
        /// POR QUÉ HACE FALTA
        /// Cuando la instrumentación de la capa de datos atrapa un PostgresException
        /// dentro de RunQuery/RunScalar, ex.StackTrace sólo llega hasta ese método:
        /// la excepción todavía NO ha propagado al controlador que lanzó la consulta,
        /// así que esas tramas no existen aún. Usar ex.StackTrace haría que todos los
        /// errores de SQL del ERP se identificaran como "Utilities.cs:NNN".
        ///
        /// La pila viva sí tiene el camino completo -CrearProducto, InsertarProducto,
        /// RunScalar-, y de ahí se saca la primera trama que es código de negocio.
        /// </summary>
        public static string OrigenDesdePila(System.Diagnostics.StackTrace pila)
        {
            if (pila == null) return null;

            foreach (var frame in pila.GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>())
            {
                var metodo = frame.GetMethod();
                string ns = metodo?.DeclaringType?.FullName ?? "";

                if (!ns.StartsWith(NamespaceProyecto, StringComparison.OrdinalIgnoreCase))
                    continue;

                string archivo = frame.GetFileName();
                string soloArchivo = string.IsNullOrEmpty(archivo)
                    ? null
                    : archivo.Substring(archivo.LastIndexOfAny(new[] { '\\', '/' }) + 1);

                // Saltar la propia plomería: es la que aparece en todos los errores.
                if (soloArchivo != null &&
                    ArchivosInfraestructura.Any(a => soloArchivo.Equals(a, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (soloArchivo != null && frame.GetFileLineNumber() > 0)
                    return $"{soloArchivo}:{frame.GetFileLineNumber()}";

                // Sin .pdb no hay archivo ni línea, pero el método sigue ubicando.
                if (metodo != null)
                    return $"{metodo.DeclaringType?.Name}.{metodo.Name}";
            }

            return null;
        }

        /// <summary>
        /// Ningún frame nuestro: la excepción nació entera dentro de una librería.
        /// </summary>
        private static string PrimeraLinea(string stack)
        {
            string primera = stack.Split('\n').FirstOrDefault()?.Trim() ?? "(sin stack)";
            return primera.Length > 200 ? primera.Substring(0, 200) : primera;
        }

        /// <summary>
        /// La huella: sha256 en hex de tipo + mensaje normalizado + origen.
        /// 64 caracteres, que es exactamente lo que declara tkt_error_huella.huella.
        /// </summary>
        public static string Calcular(string tipoExc, string mensajeNormalizado, string origen)
        {
            string semilla = $"{tipoExc}|{mensajeNormalizado}|{origen}";

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(semilla));

            var sb = new StringBuilder(64);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
