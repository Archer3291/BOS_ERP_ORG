using Npgsql;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Operaciones internas que comparten el alta y la actualización de
    /// clientes: resolución de la dirección a partir del catálogo y
    /// creación de la cuenta contable del cliente.
    /// </summary>
    public partial class ClientesController : Utilities
    {
        /// <summary>
        /// Expande una colonia del catálogo a estado, municipio, cp y clave de estado.
        /// </summary>
        /// <param name="idColonia">id_colonia seleccionado en el formulario.</param>
        /// <param name="municipioComoNumero">
        /// true devuelve numero_municipio (lo que espera el CFDI),
        /// false devuelve el nombre del municipio.
        /// </param>
        private Dictionary<string, object> ResolverDireccionNacional(
            int idColonia, bool municipioComoNumero, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string campoMunicipio = municipioComoNumero ? "m.numero_municipio" : "m.nombre";

            string query = $"SELECT e.nombre estado, {campoMunicipio} municipio, c.cp, c.nombre colonia, e.cve_estado " +
                "FROM colonias c " +
                "INNER JOIN municipios m ON m.id_municipio = c.municipio_id " +
                "INNER JOIN estados e ON e.id_estado = m.estado_id " +
                "WHERE c.id_colonia = @id_colonia";

            var parameters = new Dictionary<string, object>();
            parameters.Add("id_colonia", idColonia);

            return RunQuery(query, parameters, false, conn, tx)[0];
        }

        /// <summary>
        /// Expande un estado extranjero a país, estado y —si se indicó—
        /// municipio. El municipio es opcional en clientes internacionales.
        /// </summary>
        private Dictionary<string, object> ResolverDireccionInternacional(
            string idEstado, string idMunicipio, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var parameters = new Dictionary<string, object>();
            string where = "";

            if (!string.IsNullOrEmpty(idMunicipio))
            {
                where = " AND m.id_municipio = @municipio ";
                parameters.Add("municipio", Convert.ToInt32(idMunicipio));
            }

            string query = "SELECT p.cve_iso pais, e.nombre estado, m.nombre municipio " +
                "FROM estados e " +
                "INNER JOIN paises p ON p.id_pais = e.pais_id " +
                "LEFT JOIN municipios m ON m.estado_id = e.id_estado " +
                $"WHERE e.id_estado = @id_estado {where} ";

            parameters.Add("id_estado", Convert.ToInt32(idEstado));

            return RunQuery(query, parameters, false, conn, tx)[0];
        }

        /// <summary>
        /// Da de alta la cuenta contable 1-1-05-{cve_cli} del cliente.
        /// Si ya existe no hace nada.
        /// </summary>
        private void CrearCuentaContableClienteSiNoExiste(
            string cveCli, string nombre, int idCliente, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string codigo = $"1-1-05-{cveCli}";

            var parameters = new Dictionary<string, object>();
            parameters.Add("codigo", codigo);

            string query = "SELECT COUNT(*) FROM cuentas_finanzas WHERE codigo = @codigo";
            int existentes = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

            if (existentes > 0) return;

            query = "INSERT INTO cuentas_finanzas (codigo, nombre, creada_por, cliente_id, empresa_id, naturaleza) " +
                "VALUES (@codigo, @nombre, @creada_por, @cliente_id, @empresa, 'deudor')";

            parameters = new Dictionary<string, object>();
            parameters.Add("codigo", codigo);
            parameters.Add("nombre", nombre);
            parameters.Add("creada_por", GetUserId(User.Identity.Name));
            parameters.Add("cliente_id", idCliente);
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

            RunUpdate(query, parameters, false, conn, tx);
        }
    }
}
