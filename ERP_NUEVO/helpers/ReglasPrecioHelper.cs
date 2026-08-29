using BOS_ERP.Controllers;
using BOS_ERP.Models;
using Npgsql;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Resultado de validar las partidas de un documento contra las reglas de precio.
    /// </summary>
    public class ResultadoReglaPrecio
    {
        /// <summary>False solo cuando el documento debe rechazarse.</summary>
        public bool Permitido { get; set; } = true;

        public string Mensaje { get; set; } = "";

        /// <summary>Clave del producto que provocó el rechazo, para señalarlo en el front.</summary>
        public string CveProd { get; set; } = "";
    }

    /// <summary>
    /// Validación de precios y descuentos contra reglas_precio, compartida por todos los
    /// documentos de venta. Antes vivía duplicada en VICotizacion y VTCotizacion, y por eso
    /// pedido, remisión y factura no validaban nada: bastaba capturar el pedido directo para
    /// saltarse la regla entera.
    ///
    /// Contrato de obtener_precio_final (ver sql/obtener_precio_final.sql):
    ///   precio_final     → valor con el que arranca el input de precio
    ///   precio_minimo    → piso permitido; 0 = sin piso (producto sin regla → precio libre)
    ///   descuento_maximo → tope; 0 = no se permite descuento; solo NULL = sin configurar
    /// </summary>
    public static class ReglasPrecioHelper
    {
        private const decimal Tolerancia = 0.01m;

        /// <summary>
        /// Valida cada partida contra la regla vigente del producto para ese cliente.
        ///
        /// Los tokens de autorización levantan candados distintos y no son intercambiables:
        /// el de PRECIO levanta el piso del precio, el de DESCUENTO levanta el tope de
        /// descuento. Ninguno de los dos permite valores negativos.
        /// </summary>
        /// <param name="clienteId">
        /// Cliente del documento que se está guardando. Determina la lista de precios
        /// (catclientes.cod_ant) y qué reglas por cliente aplican, así que tiene que ser el
        /// del documento y no el que quedó en sesión.
        /// </param>
        public static ResultadoReglaPrecio ValidarPartidas(
            this Utilities utils,
            int empresaId,
            int clienteId,
            List<Dictionary<string, string>> productos,
            bool precioAutorizado,
            bool descuentoAutorizado,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null)
        {
            if (productos == null || productos.Count == 0)
                return new ResultadoReglaPrecio();

            var basicos = ValidarValoresBasicos(productos);
            if (!basicos.Permitido) return basicos;

            // Un documento puede repetir el mismo producto en varias partidas; sin caché
            // eso era una consulta por renglón.
            var cache = new Dictionary<string, ReglaProductoInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in productos)
            {
                string cveProd = Leer(p, "productoId");
                decimal precio = LeerDecimal(p, "precio");
                decimal descuento = LeerDecimal(p, "descuento");

                if (string.IsNullOrWhiteSpace(cveProd)) continue;

                if (!cache.TryGetValue(cveProd, out var regla))
                {
                    regla = ObtenerReglaProducto(utils, empresaId, clienteId, cveProd, conn, tx);
                    cache[cveProd] = regla;
                }

                if (regla == null) continue; // sin regla → precio libre (solo no negativo)

                decimal neto = precio * (1 - descuento / 100m);

                // precio_minimo = 0 significa "sin piso": producto sin regla, precio libre.
                if (regla.PrecioMinimo > 0 && precio < regla.PrecioMinimo - Tolerancia && !precioAutorizado)
                {
                    return new ResultadoReglaPrecio
                    {
                        Permitido = false,
                        CveProd = cveProd,
                        Mensaje = $"El precio de '{cveProd}' (${precio:N2}) está por debajo del " +
                                  $"mínimo permitido (${regla.PrecioMinimo:N2})."
                    };
                }

                // En PRECIO_FIJO el piso es sobre el neto: validar solo el unitario dejaba
                // que un descuento anulara el precio fijo.
                if (regla.TipoRegla == "PRECIO_FIJO" && regla.PrecioMinimo > 0
                 && neto < regla.PrecioMinimo - Tolerancia && !precioAutorizado)
                {
                    return new ResultadoReglaPrecio
                    {
                        Permitido = false,
                        CveProd = cveProd,
                        Mensaje = $"El precio neto de '{cveProd}' (${neto:N2}) queda por debajo del " +
                                  $"precio fijo de ${regla.PrecioMinimo:N2} al aplicar {descuento:N2}% de descuento."
                    };
                }

                if (descuento > regla.DescuentoMaximo + Tolerancia && !descuentoAutorizado)
                {
                    return new ResultadoReglaPrecio
                    {
                        Permitido = false,
                        CveProd = cveProd,
                        Mensaje = $"El descuento de '{cveProd}' ({descuento:N2}%) supera el máximo " +
                                  $"permitido ({regla.DescuentoMaximo:N2}%)."
                    };
                }

                // Regla automática: el precio lo fija el servidor, así que cualquier cambio
                // necesita autorización.
                if (regla.AplicarAutomatico
                 && Math.Abs(precio - regla.PrecioFinal) > Tolerancia && !precioAutorizado)
                {
                    return new ResultadoReglaPrecio
                    {
                        Permitido = false,
                        CveProd = cveProd,
                        Mensaje = $"El precio de '{cveProd}' fue modificado. La regla automática " +
                                  $"fija el precio en ${regla.PrecioFinal:N2}."
                    };
                }
            }

            return new ResultadoReglaPrecio();
        }

        /// <summary>
        /// Valores imposibles: precio negativo o descuento fuera de 0–100 (un descuento
        /// mayor deja el importe en negativo). No dependen de ninguna regla ni de tokens de
        /// autorización, así que se pueden exigir en cualquier documento, incluidos los que
        /// todavía no tienen el candado de precios en el front.
        /// </summary>
        public static ResultadoReglaPrecio ValidarValoresBasicos(List<Dictionary<string, string>> productos)
        {
            if (productos == null) return new ResultadoReglaPrecio();

            foreach (var p in productos)
            {
                decimal precio = LeerDecimal(p, "precio");
                decimal descuento = LeerDecimal(p, "descuento");

                if (precio < 0 || descuento < 0 || descuento > 100)
                {
                    string cveProd = Leer(p, "productoId");
                    return new ResultadoReglaPrecio
                    {
                        Permitido = false,
                        CveProd = cveProd,
                        Mensaje = $"Los valores de '{cveProd}' no son válidos: el precio no puede ser " +
                                  "negativo y el descuento debe estar entre 0% y 100%."
                    };
                }
            }

            return new ResultadoReglaPrecio();
        }

        /// <summary>
        /// Regla vigente para un producto y cliente. Devuelve null cuando no hay dato o la
        /// consulta falla: se deja pasar el documento a propósito para no frenar las ventas
        /// si la función SQL no está desplegada, pero se registra en consola porque un fallo
        /// aquí desactiva el control de precios por completo y antes era invisible.
        /// </summary>
        public static ReglaProductoInfo ObtenerReglaProducto(
            Utilities utils,
            int empresaId,
            int clienteId,
            string cveProd,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null)
        {
            if (string.IsNullOrWhiteSpace(cveProd)) return null;

            try
            {
                var result = utils.RunQuery(
                    @"SELECT precio_final, precio_minimo, descuento_sugerido,
                             descuento_maximo, aplicar_automatico, tipo_regla
                      FROM obtener_precio_final(@empresa_id, @cliente_id, @cve_prod)",
                    new Dictionary<string, object>
                    {
                        { "empresa_id", empresaId },
                        { "cliente_id", clienteId },
                        { "cve_prod",   cveProd   }
                    },
                    false, conn, tx);

                if (result == null || result.Count == 0) return null;

                var row = result[0];

                return new ReglaProductoInfo
                {
                    PrecioFinal = ADecimal(row, "precio_final"),
                    PrecioMinimo = ADecimal(row, "precio_minimo"),
                    DescuentoSugerido = ADecimal(row, "descuento_sugerido"),
                    // 0 es un tope real ("no se permite descuento", que es lo que devuelve
                    // PRECIO_FIJO), no "sin configurar": convertirlo en 100 dejaba anular
                    // cualquier precio fijo con un descuento del 100%.
                    DescuentoMaximo = row.ContainsKey("descuento_maximo") && row["descuento_maximo"] != null
                                          ? Convert.ToDecimal(row["descuento_maximo"])
                                          : 100m,
                    AplicarAutomatico = row.ContainsKey("aplicar_automatico") && row["aplicar_automatico"] != null
                                          && Convert.ToBoolean(row["aplicar_automatico"]),
                    TipoRegla = row.ContainsKey("tipo_regla") ? row["tipo_regla"]?.ToString() ?? "BASE" : "BASE"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReglasPrecio] No se pudo obtener la regla de '{cveProd}' " +
                                  $"(empresa {empresaId}, cliente {clienteId}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// id_cliente del documento a partir de su clave. 0 cuando no se puede resolver, que
        /// es lo que la función SQL interpreta como "sin cliente" (lista de precios pv1).
        /// </summary>
        public static int ResolverClienteId(
            this Utilities utils, string cveCli, int empresaId,
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            if (string.IsNullOrWhiteSpace(cveCli)) return 0;

            try
            {
                object id = utils.RunScalar(
                    "SELECT id_cliente FROM catclientes WHERE cve_cli = @cve_cli AND empresa_id = @empresa_id",
                    new Dictionary<string, object>
                    {
                        { "cve_cli",    cveCli    },
                        { "empresa_id", empresaId }
                    },
                    false, conn, tx);

                return id == null ? 0 : Convert.ToInt32(id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReglasPrecio] No se pudo resolver el cliente '{cveCli}': {ex.Message}");
                return 0;
            }
        }

        private static string Leer(Dictionary<string, string> fila, string clave)
            => fila != null && fila.ContainsKey(clave) ? fila[clave] ?? "" : "";

        private static decimal LeerDecimal(Dictionary<string, string> fila, string clave)
        {
            string v = Leer(fila, clave);
            return decimal.TryParse(v, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out decimal d)
                ? d
                : 0m;
        }

        private static decimal ADecimal(Dictionary<string, object> row, string clave)
            => row.ContainsKey(clave) && row[clave] != null ? Convert.ToDecimal(row[clave]) : 0m;
    }
}
