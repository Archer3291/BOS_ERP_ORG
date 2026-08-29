using BOS_ERP.Helpers;
using MySql.Data.MySqlClient;

namespace BOS_ERP.Services
{
    /// <summary>Puntos y membresía de un cliente en Socio Tiburón.</summary>
    public sealed class PuntosSocioTiburon
    {
        public bool Encontrado { get; init; }
        public int Puntos { get; init; }
        public string Membresia { get; init; } = "";
        public int Canjes { get; init; }
        public string Url { get; init; } = "https://sociotiburon.com/";
    }

    /// <summary>
    /// Lee el programa de puntos, que vive en un MySQL externo (sociotiburon.com).
    ///
    /// Tres cuidados por ser un servidor de terceros:
    ///
    ///  1. Timeouts cortos. La cadena de conexión trae 5 s para conectar y 8 s por
    ///     consulta: si el sitio está lento, el portal no se cuelga esperándolo.
    ///
    ///  2. Nunca propaga excepciones. Un fallo devuelve "no encontrado" y se anota en la
    ///     bitácora. Que el programa de puntos esté caído no puede impedirle a un cliente
    ///     ver sus facturas, que es a lo que vino.
    ///
    ///  3. Sólo aplica a la empresa configurada. El programa hoy es exclusivo de la
    ///     empresa 1; para el resto ni siquiera se abre la conexión.
    ///
    /// El vínculo con el ERP es la clave del cliente en el mismo formato que
    /// catclientes.cve_cli (01-0096), que allá aparece con tres nombres según la tabla:
    /// `clientes.cliente`, `usuarios.idkep`, `cliente_membresia.idkep` y
    /// `puntos.idcliente`. Son la misma cosa.
    /// </summary>
    public class SocioTiburonService
    {
        private readonly string _cadena;
        private readonly int _empresaId;
        private readonly string _url;

        private const string LogTag = "SocioTiburon";

        public SocioTiburonService(IConfiguration config)
        {
            _cadena = config.GetConnectionString("SocioTiburon") ?? "";
            _empresaId = int.TryParse(config["SocioTiburon:EmpresaId"], out int e) ? e : 1;
            _url = config["SocioTiburon:Url"] ?? "https://sociotiburon.com/";
        }

        /// <summary>Empresas que participan en el programa.</summary>
        public bool AplicaParaEmpresa(int empresaId) => empresaId == _empresaId;

        /// <summary>
        /// Puntos del cliente por su clave (cve_cli). Devuelve Encontrado=false cuando el
        /// cliente no está inscrito, cuando la empresa no participa o cuando el servicio
        /// externo no responde: los tres casos se resuelven igual en pantalla, ocultando
        /// la tarjeta.
        /// </summary>
        public async Task<PuntosSocioTiburon> ObtenerPuntosAsync(string claveCliente, int empresaId)
        {
            var vacio = new PuntosSocioTiburon { Encontrado = false, Url = _url };

            if (!AplicaParaEmpresa(empresaId)) return vacio;
            if (string.IsNullOrWhiteSpace(claveCliente)) return vacio;
            if (string.IsNullOrWhiteSpace(_cadena)) return vacio;

            try
            {
                await using var conn = new MySqlConnection(_cadena);
                await conn.OpenAsync();

                // Cada dato vive ahora en su propia tabla y se pide por separado, no con
                // JOIN, porque no cubren a la misma población: un cliente puede tener
                // saldo sin cuenta de portal, cuenta sin nivel asignado, o nivel sin
                // haber comprado nunca. Un INNER JOIN dejaría fuera a los tres casos.
                //
                //   puntos            el saldo, una fila por cliente (idcliente es único)
                //   clientes          el padrón; basta para considerarlo inscrito
                //   cliente_membresia el nivel asignado, con FK al catálogo `membresia`
                //   entregaregalos    un renglón por regalo canjeado
                //
                // Los canjes ya no son una columna: son las entregas de regalo del
                // cliente. Se cuentan TODAS, entregadas o no —así lo define el programa—,
                // y la tabla tiene índice por idkep, así que el conteo no cuesta.
                await using var cmd = new MySqlCommand(@"
                    SELECT
                        (SELECT p.puntos
                           FROM puntos p
                          WHERE p.idcliente = @clave) AS puntos,
                        (SELECT m.nombre
                           FROM cliente_membresia cm
                           JOIN membresia m ON m.idmembresia = cm.idmembresia
                          WHERE cm.idkep = @clave) AS membresia,
                        (SELECT COUNT(*)
                           FROM entregaregalos e
                          WHERE e.idkep = @clave) AS canjes,
                        (SELECT COUNT(*)
                           FROM clientes c
                          WHERE c.cliente = @clave) AS en_padron", conn);

                cmd.Parameters.AddWithValue("@clave", claveCliente.Trim());

                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync()) return vacio;

                bool tienePuntos = reader["puntos"] is not null and not DBNull;
                bool tieneMembresia = reader["membresia"] is not null and not DBNull;
                bool enPadron = Convert.ToInt64(reader["en_padron"]) > 0;

                // Estar en el padrón ya cuenta como inscrito, aunque todavía no tenga ni
                // saldo ni nivel: es el caso de un cliente recién dado de alta, y ocultarle
                // la tarjeta hasta su primera compra lo dejaría sin saber que ya es socio.
                if (!tienePuntos && !tieneMembresia && !enPadron) return vacio;

                return new PuntosSocioTiburon
                {
                    Encontrado = true,
                    Puntos = ANumero(reader["puntos"]),
                    Canjes = ANumero(reader["canjes"]),
                    Membresia = reader["membresia"]?.ToString()?.Trim() ?? "",
                    Url = _url
                };
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, claveCliente ?? "",
                    $"No se pudieron leer los puntos: {ex.Message}", nivel: "WARN");

                return vacio;
            }
        }

        private static int ANumero(object valor)
        {
            if (valor is null || valor is DBNull) return 0;
            return int.TryParse(valor.ToString()?.Trim(), out int n) ? n : 0;
        }
    }
}
