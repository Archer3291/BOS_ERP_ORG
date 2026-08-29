using System.Text;
using System.Text.Json;
using Npgsql;

namespace BOS_ERP.Models
{
    /// <summary>
    /// Una falla del sistema, ya normalizada, lista para que
    /// <c>ErrorTicketService</c> la deduplique y le levante ticket.
    ///
    /// Las tres capas de captura (middleware, helper de Utilities y red del
    /// navegador) producen este mismo objeto: es lo que permite que el servicio
    /// no sepa ni le importe de dónde vino el error.
    /// </summary>
    public sealed class ErrorCapturado
    {
        /// <summary>middleware | controlador | navegador | job. Coincide con tkt_error_detalle.capa.</summary>
        public string Capa { get; set; } = "middleware";

        /// <summary>Módulo al que se le atribuye, p. ej. "Ventas/PuntoDeVenta". Sale al título del ticket.</summary>
        public string Modulo { get; set; }

        public string TipoExc { get; set; }
        public string Mensaje { get; set; }
        public string Stack { get; set; }

        /// <summary>Primer frame del stack que pertenece a BOS_ERP: "Archivo.cs:123". Parte de la huella.</summary>
        public string Origen { get; set; }

        public string Ruta { get; set; }
        public string Metodo { get; set; }
        public string TraceId { get; set; }

        public int? UsuarioId { get; set; }
        public int? EmpresaId { get; set; }
        public int? SucursalId { get; set; }

        public string Ip { get; set; }
        public string UserAgent { get; set; }

        /// <summary>
        /// Todo lo que no cabe en una columna: cadena de InnerException, datos de
        /// PostgresException, parámetros del request. Va a payload (jsonb).
        /// </summary>
        public Dictionary<string, object> Extra { get; } = new();

        // --------------------------------------------------------------------
        // Sanitización
        // --------------------------------------------------------------------

        /// <summary>
        /// Fragmentos de nombre de campo que NUNCA se guardan.
        ///
        /// Un log de errores es el lugar clásico donde se filtran credenciales, y
        /// aquí el log queda visible para todo el staff de soporte. La comparación
        /// es por "contiene", sin distinguir mayúsculas: es deliberadamente amplia
        /// porque el costo de tapar un campo de más es cero y el de filtrar una
        /// contraseña, no.
        /// </summary>
        private static readonly string[] CamposProhibidos =
        {
            "password", "contrasena", "contraseña", "passwd", "pwd",
            "token", "secret", "apikey", "api_key", "authorization",
            "requestverificationtoken", "csd", "pfx", "privatekey",
            "certificado", "llaveprivada",
            "firma", "sello", "tarjeta", "cvv", "clabe"
        };
        // Ojo al ampliar esta lista: se compara por "contiene", así que un
        // fragmento corto tapa campos legítimos. "cer" taparía "tercero" y "key"
        // taparía cualquier cosa que lo lleve dentro; por eso van completos.

        public const string Tapado = "***";

        /// <summary>¿Este nombre de campo se guarda, o se tapa?</summary>
        public static bool EsCampoSensible(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return false;
            string n = nombre.ToLowerInvariant();
            return CamposProhibidos.Any(p => n.Contains(p));
        }

        /// <summary>Agrega un diccionario de parámetros al Extra, tapando lo sensible.</summary>
        public void AgregarParametros(string grupo, IEnumerable<KeyValuePair<string, string>> pares)
        {
            if (pares == null) return;

            var limpio = new Dictionary<string, string>();
            foreach (var par in pares)
            {
                if (string.IsNullOrWhiteSpace(par.Key)) continue;

                // Un valor larguísimo suele ser un XML o un base64 y no aporta al
                // diagnóstico, pero sí infla el jsonb de forma desagradable.
                string valor = EsCampoSensible(par.Key)
                    ? Tapado
                    : Truncar(par.Value, 500);

                limpio[par.Key] = valor;
            }

            if (limpio.Count > 0) Extra[grupo] = limpio;
        }

        // --------------------------------------------------------------------
        // Construcción desde una excepción
        // --------------------------------------------------------------------

        /// <summary>
        /// Arma el objeto a partir de una excepción, desdoblando la cadena completa
        /// de InnerException y, si es de Postgres, los datos que de verdad cierran
        /// un diagnóstico (SqlState, tabla, constraint).
        /// </summary>
        public static ErrorCapturado Desde(Exception ex, string capa, string modulo)
        {
            var e = new ErrorCapturado
            {
                Capa = capa,
                Modulo = modulo,
                TipoExc = ex.GetType().FullName,
                Mensaje = Truncar(ex.Message, 2000),
                Stack = Truncar(ex.StackTrace, 8000)
            };

            // Cadena de InnerException. El error útil casi siempre está al fondo:
            // "Error al guardar" envuelve a "violación de llave foránea en catclientes".
            var cadena = new List<object>();
            var inner = ex.InnerException;
            int nivel = 0;
            while (inner != null && nivel < 6)
            {
                cadena.Add(new
                {
                    tipo = inner.GetType().FullName,
                    mensaje = Truncar(inner.Message, 1000),
                    stack = Truncar(inner.StackTrace, 2000)
                });
                inner = inner.InnerException;
                nivel++;
            }
            if (cadena.Count > 0) e.Extra["inner"] = cadena;

            // Lo de Postgres va aparte porque es lo primero que se mira.
            var pg = BuscarPostgres(ex);
            if (pg != null)
            {
                e.Extra["postgres"] = new
                {
                    sqlState = pg.SqlState,
                    tabla = pg.TableName,
                    columna = pg.ColumnName,
                    constraint = pg.ConstraintName,
                    rutina = pg.Routine,
                    detalle = Truncar(pg.Detail, 1000),
                    pista = Truncar(pg.Hint, 500)
                };
            }

            return e;
        }

        /// <summary>La PostgresException puede venir envuelta varios niveles adentro.</summary>
        private static PostgresException BuscarPostgres(Exception ex)
        {
            int nivel = 0;
            while (ex != null && nivel < 8)
            {
                if (ex is PostgresException pg) return pg;
                ex = ex.InnerException;
                nivel++;
            }
            return null;
        }

        /// <summary>El payload completo, serializado para la columna jsonb.</summary>
        public string PayloadJson()
        {
            try
            {
                var doc = new Dictionary<string, object>(Extra)
                {
                    ["tipoExc"] = TipoExc,
                    ["mensaje"] = Mensaje,
                    ["stack"] = Stack,
                    ["modulo"] = Modulo,
                    ["origen"] = Origen
                };

                return JsonSerializer.Serialize(doc, new JsonSerializerOptions
                {
                    // Sin esto los acentos salen como \u00XX y el payload se vuelve
                    // ilegible justo cuando alguien lo está leyendo para diagnosticar.
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                });
            }
            catch
            {
                // Un objeto que no serializa no puede costar el registro del error.
                return JsonSerializer.Serialize(new
                {
                    tipoExc = TipoExc,
                    mensaje = Mensaje,
                    nota = "El payload completo no se pudo serializar."
                });
            }
        }

        private static string Truncar(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
