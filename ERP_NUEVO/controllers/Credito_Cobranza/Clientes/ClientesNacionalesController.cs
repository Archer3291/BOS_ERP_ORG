using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Alta de clientes NACIONALES: consecutivo de clave (serie 01-)
    /// y creación del registro junto con su cuenta contable.
    /// </summary>
    public partial class ClientesController : Utilities
    {
        /// <summary>
        /// Siguiente clave libre de la serie nacional (01-NNNN).
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetSiguienteCodigo()
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
                "WHERE cve_cli ~ '^01-[0-9]{4}$'  AND empresa_id = @empresa_id " +
                "ORDER BY SPLIT_PART(cve_cli, '-', 1)::int DESC, SPLIT_PART(cve_cli, '-', 2)::int DESC " +
                "LIMIT 1;";
            string nvo_cve_cli = RunScalar(query, parameters).ToString();

            return Json(nvo_cve_cli);
        }

        /// <summary>
        /// Crea el cliente nacional y su cuenta contable en una sola
        /// transacción. Los datos fiscales se capturan después, desde
        /// el modal de edición.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult CrearCliente(IFormCollection fc)
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
                            int idCliente = InsertarClienteNacional(fc, conn, tx);

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

                return Json(new { icon = "success", title = "Cliente creado exitosamente", html = "Para agregar la razon social y demas datos fiscales tiene que ser desde la ventana de edicion de cliente." });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "ocurrio un error al crear al cliente", html = ex.Message });
            }
        }

        /// <summary>Inserta en catclientes y devuelve el id generado.</summary>
        private int InsertarClienteNacional(IFormCollection fc, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var direcciones = ResolverDireccionNacional(
                Convert.ToInt32(fc["colonia"].ToString()), false, conn, tx);

            var parameters = new Dictionary<string, object>();
            parameters.Add("cve_cli", fc["cve_cli"].ToString());
            parameters.Add("n_cli", fc["n_cli"].ToString());
            parameters.Add("rfc", fc["rfc"].ToString());
            parameters.Add("curp_cli", fc["curp_cli"].ToString());
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("pob", direcciones["estado"].ToString());
            parameters.Add("municipio", direcciones["municipio"].ToString());
            parameters.Add("col", direcciones["colonia"].ToString());
            parameters.Add("cve_est", direcciones["cve_estado"]);
            parameters.Add("cp", fc["cp"].ToString());
            parameters.Add("dir", fc["dir"].ToString());
            parameters.Add("numero_ext_domiclio", GetInt(fc["numero_ext"].ToString(), 0));
            parameters.Add("numero_int_domicilio", fc["numero_int"].ToString() == "" ? 0 : GetInt(fc["numero_int"].ToString()));
            parameters.Add("cve_vdr", fc["cve_vdr"].ToString());
            parameters.Add("cve_gpo", fc["cve_gpo"].ToString());
            parameters.Add("lim_crd", GetDecimal(fc["lim_crd"].ToString()));
            parameters.Add("pl_crd", GetInt(fc["pl_dias"].ToString()));
            parameters.Add("fch_in", DateTime.Now);
            parameters.Add("usuario", GetUserId(User.Identity.Name));

            string query = "INSERT INTO catclientes (cve_cli, n_cli, rfc, curp_cli, pob, municipio, col, cp, dir, numero_int_domicilio, numero_ext_domiclio, " +
                "   cve_vdr, cve_gpo, lim_crd, pl_crd, fch_in, stat_cli, cve_pais, cve_est, empresa_id, creado_por) " +
                "VALUES (@cve_cli, @n_cli, @rfc, @curp_cli, @pob, @municipio, @col, @cp, @dir, @numero_int_domicilio, @numero_ext_domiclio, " +
                "   @cve_vdr, @cve_gpo, @lim_crd, @pl_crd, @fch_in, 'A', 'MEX', @cve_est, @empresa_id, @usuario) RETURNING id_cliente ";

            return Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
        }
    }
}
