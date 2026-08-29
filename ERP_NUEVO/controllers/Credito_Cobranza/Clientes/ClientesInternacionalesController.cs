using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Alta de clientes INTERNACIONALES: consecutivo de clave (serie 02-)
    /// y creación del registro junto con su cuenta contable.
    /// El RFC queda fijo en el genérico de extranjeros; la identificación
    /// real del cliente es su Tax ID.
    /// </summary>
    public partial class ClientesController : Utilities
    {
        /// <summary>
        /// Siguiente clave libre de la serie internacional (02-NNNN).
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetSiguienteCodigoInternacional()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = "SELECT CASE WHEN SPLIT_PART(cve_cli, '-', 2)::int >= 9999 THEN " +
                "       LPAD((SPLIT_PART(cve_cli, '-', 1)::int + 1)::text, 2, '0') || '-0001' " +
                "   ELSE " +
                "       LPAD(SPLIT_PART(cve_cli, '-', 1), 2, '0') || '-' || " +
                "       LPAD((SPLIT_PART(cve_cli, '-', 2)::int + 1)::text, 4, '0') " +
                "   END AS siguiente_cve_cli " +
                "FROM catclientes " +
                "WHERE cve_cli ~ '^02-[0-9]{4}$' AND empresa_id = @empresa_id " +
                "ORDER BY SPLIT_PART(cve_cli, '-', 1)::int DESC, SPLIT_PART(cve_cli, '-', 2)::int DESC " +
                "LIMIT 1;";
            string nvo_cve_cli = RunScalar(query, parameters).ToString();

            return Json(nvo_cve_cli);
        }

        /// <summary>
        /// Crea el cliente internacional y su cuenta contable en una sola
        /// transacción.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult CrearClienteInternacional(IFormCollection fc)
        {
            try
            {
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            int idCliente = InsertarClienteInternacional(fc, conn, tx);

                            CrearCuentaContableClienteSiNoExiste(
                                fc["cve_cli"].ToString(), fc["n_cli"].ToString(), idCliente, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Cliente creado exitosamente", html = "Puedes agregar formas de contacto desde los detalles del usuario." });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "ocurrio un error al crear al cliente", html = ex.Message });
            }
        }

        /// <summary>Inserta en catclientes y devuelve el id generado.</summary>
        private int InsertarClienteInternacional(IFormCollection fc, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var direcciones = ResolverDireccionInternacional(
                fc["estado"].ToString(), fc["municipio"].ToString(), conn, tx);

            var parameters = new Dictionary<string, object>();
            parameters.Add("cve_cli", fc["cve_cli"].ToString());
            parameters.Add("n_cli", fc["n_cli"].ToString());
            parameters.Add("curp_cli", fc["tax_id"].ToString());
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("pob", direcciones["estado"].ToString());
            parameters.Add("municipio", GetString(direcciones["municipio"]));
            parameters.Add("cve_pais", GetString(direcciones["pais"]));
            parameters.Add("col", fc["colonia"].ToString());
            parameters.Add("cp", fc["cp"].ToString());
            parameters.Add("dir", fc["dir"].ToString());
            parameters.Add("numero_int_domicilio", GetInt(fc["numero_int"].ToString(), 0));
            parameters.Add("numero_ext_domiclio", fc["numero_ext"].ToString() == "" ? 0 : GetInt(fc["numero_ext"].ToString()));
            parameters.Add("cve_vdr", fc["cve_vdr"].ToString());
            parameters.Add("cve_gpo", fc["cve_gpo"].ToString());
            parameters.Add("lim_crd", GetDecimal(fc["lim_crd"].ToString()));
            parameters.Add("pl_crd", GetInt(fc["pl_dias"].ToString()));
            parameters.Add("fch_in", DateTime.Now);
            parameters.Add("usuario", GetUserId(User.Identity.Name));

            string query = "INSERT INTO catclientes (cve_cli, n_cli, rfc, curp_cli, pob, municipio, col, cp, dir, numero_int_domicilio, numero_ext_domiclio, " +
                "   cve_vdr, cve_gpo, lim_crd, pl_crd, fch_in, stat_cli, cve_pais, es_internacional, empresa_id, creado_por) " +
                "VALUES (@cve_cli, @n_cli, 'XEXX010101000', @curp_cli, @pob, @municipio, @col, @cp, @dir, @numero_int_domicilio, @numero_ext_domiclio, " +
                "   @cve_vdr, @cve_gpo, @lim_crd, @pl_crd, @fch_in, 'A', @cve_pais, true, @empresa_id, @usuario) RETURNING id_cliente ";

            return Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
        }
    }
}
