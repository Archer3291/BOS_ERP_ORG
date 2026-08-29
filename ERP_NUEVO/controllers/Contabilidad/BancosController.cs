using Npgsql;
using System.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class ContabilidadController : Utilities
    {
        public JsonResult GetBancos(IFormCollection fc)
        {
            int page = Convert.ToInt32(fc["page"].ToString());
            int pageSize = Convert.ToInt32(fc["pageSize"].ToString());
            var parameters = new Dictionary<string, object>();
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string where = "";

            if (!string.IsNullOrWhiteSpace(fc["nombre"].ToString()))
            {
                parameters.Add("nombre", $"%{fc["nombre"].ToString()}%");
                where = " AND (ct.nombre ILIKE @nombre OR ct.cuenta_contable ILIKE @nombre OR ct.cuenta_banco ILIKE @nombre) ";
            }

            string query = "SELECT ct.id_catbanco, ct.nombre, ct.cuenta_banco, ct.cuenta_contable, ct.rfc, ct.tipo, ct.saldo_inicial, ct.cuenta_clave, ct.razon_social, cm.clave " +
                "FROM catbancos ct " +
                "INNER JOIN cat_monedas cm ON cm.id = ct.moneda_id " +
                $"WHERE 1=1 {where} " +
                "ORDER BY ct.cuenta_contable ASC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM catbancos ct " +
                $"WHERE 1=1 {where}";
            int total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new {data, total});
        }

        public JsonResult GetMonedas()
        {
            string query = "SELECT id, clave FROM cat_monedas";
            var monedas = RunQuery(query);

            return Json(monedas);
        }

        public JsonResult ActualizarBanco(IFormCollection fc)
        {
            try
            {
                var util = new Utilities(true);
                string connectionString = util._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var parameters = new Dictionary<string, object>();
                            parameters.Add("nombre", GetString(fc["nombre"].ToString()));
                            parameters.Add("razon_social", GetString(fc["razon_social"].ToString()));
                            parameters.Add("rfc", GetString(fc["rfc"].ToString()));
                            parameters.Add("moneda_id", GetInt(fc["moneda_id"].ToString()));
                            parameters.Add("cuenta_banco", GetString(fc["cuenta_banco"].ToString()));
                            parameters.Add("cuenta_contable", GetString(fc["cuenta_contable"].ToString()));
                            parameters.Add("cuenta_clave", GetString(fc["cuenta_clave"].ToString()));
                            parameters.Add("saldo_inicial", GetDecimal(fc["saldo_inicial"].ToString()));
                            parameters.Add("tipo", GetString(fc["tipo"].ToString()));
                            parameters.Add("id_catbanco", GetInt(fc["id_catbanco"].ToString()));

                            string query = "UPDATE catbancos SET nombre = @nombre, cuenta_banco = @cuenta_banco, cuenta_contable = @cuenta_contable, rfc = @rfc, " +
                                "   tipo = @tipo, moneda_id = @moneda_id, saldo_inicial = @saldo_inicial, fecha_saldo = NOW(), cuenta_clave = @cuenta_clave, razon_social = @razon_social " +
                                "WHERE id_catbanco = @id_catbanco;";

                            RunUpdate(query, parameters, false, conn, tx);

                            query = "UPDATE cuentas_finanzas SET banco_id = @id_catbanco WHERE codigo = @cuenta_contable";
                            RunUpdate(query, parameters, false, conn, tx);
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Banco actualizado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error al actualizar el banco", html = ex.Message });
            }
        }

        public JsonResult CrearBanco(IFormCollection fc)
        {
            try
            {
                var util = new Utilities(true);
                string connectionString = util._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var parameters = new Dictionary<string, object>();
                            parameters.Add("nombre", GetString(fc["nombre"].ToString()));
                            parameters.Add("razon_social", GetString(fc["razon_social"].ToString()));
                            parameters.Add("rfc", GetString(fc["rfc"].ToString()));
                            parameters.Add("moneda_id", GetInt(fc["moneda_id"].ToString()));
                            parameters.Add("cuenta_banco", GetString(fc["cuenta_banco"].ToString()));
                            parameters.Add("cuenta_contable", GetString(fc["cuenta_contable"].ToString()));
                            parameters.Add("cuenta_clave", GetString(fc["cuenta_clave"].ToString()));
                            parameters.Add("saldo_inicial", GetDecimal(fc["saldo_inicial"].ToString()));
                            parameters.Add("tipo", GetString(fc["tipo"].ToString()));
                            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
                            parameters.Add("usuario", GetUserId(User.Identity.Name));

                            string query = "INSERT INTO catbancos " +
                                "(nombre, cuenta_banco, cuenta_contable, rfc, tipo, moneda_id, saldo_inicial, fecha_saldo, cuenta_clave, razon_social) " +
                                "VALUES " +
                                "(@nombre, @cuenta_banco, @cuenta_contable, @rfc, @tipo, @moneda_id, @saldo_inicial, NOW(), @cuenta_clave, @razon_social) " +
                                "RETURNING id_catbanco";

                            int ctaBanco = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
                            parameters.Add("banco", ctaBanco);

                            query = "SELECT COUNT(*) qty FROM cuentas_finanzas WHERE codigo = @cuenta_contable AND empresa_id = @empresa";
                            int qty = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                            if (qty == 0)
                            {
                                query = "INSERT INTO cuentas_finanzas (codigo, nombre, naturaleza, creada_por, empresa_id, banco_id) " +
                                    "VALUES (@cuenta_contable, @nombre, 'D', @usuario, @empresa, @banco)";
                                RunUpdate(query, parameters, false, conn, tx);
                            }
                            else
                            {
                                query = "UPDATE cuentas_finanzas SET banco_id = @banco WHERE codigo = @cuenta_contable AND empresa_id = @empresa";
                                RunUpdate(query, parameters, false, conn, tx);
                            }
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Banco creado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error al crear el banco.", html = ex.Message });
            }
        }

        public JsonResult GetCuentaSiguiente(IFormCollection fc)
        {
            string tipo = GetString(fc["tipo"].ToString());
            var parameters = new Dictionary<string, object>();
            string query = "";

            if (tipo == "internacional")
            {
                query = "SELECT '1-1-02-02-' || LPAD((COALESCE(MAX(CAST(split_part(codigo, '-', 5) AS INTEGER)), 0) + 1)::text, 4, '0')  AS nueva_cuenta " +
                    "FROM cuentas_finanzas " +
                    "WHERE codigo LIKE '1-1-02-02-%';";
            }
            else if (tipo == "nacional")
            {
                query = "SELECT '1-1-02-01-' || LPAD((COALESCE(MAX(CAST(split_part(codigo, '-', 5) AS INTEGER)), 0) + 1)::text, 4, '0')  AS nueva_cuenta " +
                    "FROM cuentas_finanzas " +
                    "WHERE codigo LIKE '1-1-02-01-%';";
            } else if (tipo == "inversion")
            {
                query = "SELECT '1-1-02-03-' || LPAD((COALESCE(MAX(CAST(split_part(codigo, '-', 5) AS INTEGER)), 0) + 1)::text, 4, '0')  AS nueva_cuenta " +
                    "FROM cuentas_finanzas " +
                    "WHERE codigo LIKE '1-1-02-03-%';";
            }

            var cuenta = RunScalar(query, parameters);

            return Json(new { cuenta });
        }
    }
}