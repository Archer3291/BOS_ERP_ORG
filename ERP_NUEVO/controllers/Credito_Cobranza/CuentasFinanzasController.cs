using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Credito_Cobranza
{
    public class CuentasFinanzasController : Utilities
    {
        #region obtener datos

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetCuentasFinanzas(IFormCollection fc)
        {
            int page = int.TryParse(fc["page"].ToString(), out int p) ? p : 1;
            int pageSize = int.TryParse(fc["pageSize"].ToString(), out int ps) ? ps : 10;
            string where = "";

            var parameters = new Dictionary<string, object>
            {
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            if (!string.IsNullOrWhiteSpace(fc["nombre"].ToString()))
            {
                parameters.Add("nombre", $"%{fc["nombre"].ToString()}%");
                where += " AND (cf.nombre ILIKE @nombre OR cf.codigo ILIKE @nombre) ";
            }

            // Excluir cuentas de banco (banco_id IS NULL) — los bancos se gestionan en otra vista
            string query = @"
                SELECT
                    cf.codigo,
                    cf.nombre,
                    cf.id_cuenta_contable,
                    cf.naturaleza,
                    cf.cliente_id,
                    cf.proveedor_id,
                    CASE
                        WHEN cf.cliente_id   IS NOT NULL THEN 'cliente'
                        WHEN cf.proveedor_id IS NOT NULL THEN 'proveedor'
                        ELSE 'contable'
                    END AS tipo,
                    COALESCE(c.n_cli, p.n_prov, '') AS titular
                FROM cuentas_finanzas cf
                LEFT JOIN catclientes    c ON c.id_cliente = cf.cliente_id
                LEFT JOIN catproveedores p ON p.id_prov    = cf.proveedor_id
                WHERE cf.empresa_id = @id_empresa
                    AND cf.banco_id IS NULL
                " + where + @"
                ORDER BY cf.codigo ASC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            string countQuery = @"
                SELECT COUNT(*)
                FROM cuentas_finanzas cf
                WHERE cf.empresa_id = @id_empresa
                    AND cf.banco_id IS NULL
                " + where;

            var total = RunScalar(countQuery, parameters);

            return Json(new { data, total });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetClientes(IFormCollection fc)
        {
            string param = GetString(fc["param"].ToString());
            string where = "";

            var parameters = new Dictionary<string, object>
            {
                { "empresa", HttpContext.Session.GetInt32("Empresa") }
            };

            if (!string.IsNullOrEmpty(param))
            {
                parameters.Add("param", param);
                where = " AND (c.cve_cli ILIKE '%' || @param || '%' OR c.n_cli ILIKE '%' || @param || '%') ";
            }

            string query = @"
                SELECT c.cve_cli, c.n_cli, c.id_cliente
                FROM catclientes c
                LEFT JOIN cuentas_finanzas cf ON cf.cliente_id = c.id_cliente AND cf.empresa_id = c.empresa_id
                WHERE c.empresa_id = @empresa
                    AND cf.cliente_id IS NULL
                " + where + @"
                LIMIT 50";

            var clientes = RunQuery(query, parameters);
            return Json(clientes);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetProveedores(IFormCollection fc)
        {
            string param = GetString(fc["param"].ToString());
            string where = "";

            var parameters = new Dictionary<string, object>
            {
                { "empresa", HttpContext.Session.GetInt32("Empresa") }
            };

            if (!string.IsNullOrEmpty(param))
            {
                parameters.Add("param", param);
                where = " AND (p.cve_prov ILIKE '%' || @param || '%' OR p.n_prov ILIKE '%' || @param || '%') ";
            }

            string query = @"
                SELECT p.cve_prov, p.n_prov, p.id_prov
                FROM catproveedores p
                LEFT JOIN cuentas_finanzas cf ON cf.proveedor_id = p.id_prov AND cf.empresa_id = p.id_empresa
                WHERE p.id_empresa = @empresa
                    AND cf.proveedor_id IS NULL
                " + where + @"
                LIMIT 50";

            var proveedores = RunQuery(query, parameters);
            return Json(proveedores);
        }

        #endregion

        #region acciones a cuentas

        public JsonResult CrearCuentaCliente(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "cliente", GetInt(fc["clienteId"].ToString()) }
                };

                string query = "SELECT COUNT(*) FROM cuentas_finanzas WHERE cliente_id = @cliente";
                int qty = Convert.ToInt32(RunScalar(query, parameters));

                if (qty > 0)
                    return Json(new { icon = "error", title = "Error al crear la cuenta", html = "Ya existe una cuenta relacionada a este cliente." });

                if (string.IsNullOrWhiteSpace(GetString(fc["nombre"].ToString())) || string.IsNullOrWhiteSpace(GetString(fc["cuentaContable"].ToString())))
                    return Json(new { icon = "error", title = "Error al crear la cuenta", html = "No se pudo recuperar el nombre o la cuenta del cliente." });

                query = @"INSERT INTO cuentas_finanzas (codigo, nombre, creada_por, cliente_id, empresa_id, naturaleza)
                    VALUES (@codigo, @nombre, @creador, @cliente, @empresa, 'D')";

                parameters.Add("codigo", GetString(fc["cuentaContable"].ToString()));
                parameters.Add("nombre", GetString(fc["nombre"].ToString()));
                parameters.Add("creador", GetUserId(User.Identity.Name));
                parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

                RunUpdate(query, parameters);

                return Json(new { icon = "success", title = "Cuenta creada exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error inesperado", html = ex.Message });
            }
        }

        public JsonResult CrearCuentaProveedor(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "proveedor", Convert.ToInt32(fc["proveedorId"].ToString()) }
                };

                string query = "SELECT COUNT(*) qty FROM cuentas_finanzas WHERE proveedor_id = @proveedor";
                int qty = Convert.ToInt32(RunScalar(query, parameters));

                if (qty > 0)
                    return Json(new { icon = "error", title = "Error al crear la cuenta", html = "Ya existe una cuenta relacionada a este proveedor." });

                if (string.IsNullOrWhiteSpace(GetString(fc["nombre"].ToString())) || string.IsNullOrWhiteSpace(GetString(fc["cuentaContable"].ToString())))
                    return Json(new { icon = "error", title = "Error al crear la cuenta", html = "No se pudo recuperar el nombre o la cuenta del proveedor." });

                query = @"INSERT INTO cuentas_finanzas (codigo, nombre, creada_por, proveedor_id, empresa_id, naturaleza)
                    VALUES (@codigo, @nombre, @creador, @proveedor, @empresa, 'A')";

                parameters.Add("codigo", GetString(fc["cuentaContable"].ToString()));
                parameters.Add("nombre", GetString(fc["nombre"].ToString()));
                parameters.Add("creador", GetUserId(User.Identity.Name));
                parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

                RunUpdate(query, parameters);

                return Json(new { icon = "success", title = "Cuenta creada exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error inesperado", html = ex.Message });
            }
        }

        public JsonResult CrearCuentaContable(IFormCollection fc)
        {
            try
            {
                string codigo = GetString(fc["codigo"].ToString());
                string nombre = GetString(fc["nombre"].ToString());
                string naturaleza = GetString(fc["naturaleza"].ToString());

                if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(naturaleza))
                    return Json(new { icon = "error", title = "Error al crear la cuenta", html = "El código, nombre y naturaleza son obligatorios." });

                var parameters = new Dictionary<string, object>
                {
                    { "codigo", codigo },
                    { "empresa", HttpContext.Session.GetInt32("Empresa") }
                };

                // Verificar que no exista ya ese código en la empresa
                string query = "SELECT COUNT(*) FROM cuentas_finanzas WHERE codigo = @codigo AND empresa_id = @empresa";
                int qty = Convert.ToInt32(RunScalar(query, parameters));

                if (qty > 0)
                    return Json(new { icon = "error", title = "Error al crear la cuenta", html = "Ya existe una cuenta con ese código." });

                query = @"INSERT INTO cuentas_finanzas (codigo, nombre, naturaleza, creada_por, empresa_id)
                    VALUES (@codigo, @nombre, @naturaleza, @creador, @empresa)";

                parameters.Add("nombre", nombre);
                parameters.Add("naturaleza", naturaleza);
                parameters.Add("creador", GetUserId(User.Identity.Name));

                RunUpdate(query, parameters);

                return Json(new { icon = "success", title = "Cuenta contable creada exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error inesperado", html = ex.Message });
            }
        }

        public JsonResult EditarCuenta(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "cuenta_id", GetInt(fc["id"].ToString()) }
                };

                string nombre = GetString(fc["nombre"].ToString());
                string clave = GetString(fc["clave"].ToString());
                string naturaleza = GetString(fc["naturaleza"].ToString());

                if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(clave))
                    return Json(new { icon = "error", title = "Error al actualizar la cuenta", html = "El nombre y la cuenta son obligatorios." });

                string query = "SELECT COUNT(*) FROM cuentas_finanzas WHERE id_cuenta_contable = @cuenta_id";
                int qty = Convert.ToInt32(RunScalar(query, parameters));

                if (qty == 0)
                    return Json(new { icon = "error", title = "Error al actualizar la cuenta", html = "No se encontró la cuenta seleccionada." });

                // Actualizar nombre, código y naturaleza (naturaleza solo aplica a contables; para cliente/proveedor llega vacío y no se sobreescribe si se filtra)
                if (!string.IsNullOrWhiteSpace(naturaleza))
                {
                    query = "UPDATE cuentas_finanzas SET nombre = @nombre, codigo = @clave, naturaleza = @naturaleza WHERE id_cuenta_contable = @cuenta_id";
                    parameters.Add("naturaleza", naturaleza);
                }
                else
                {
                    query = "UPDATE cuentas_finanzas SET nombre = @nombre, codigo = @clave WHERE id_cuenta_contable = @cuenta_id";
                }

                parameters.Add("nombre", nombre);
                parameters.Add("clave", clave);

                RunUpdate(query, parameters);

                return Json(new { icon = "success", title = "Cuenta actualizada exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error inesperado", html = ex.Message });
            }
        }

        #endregion
    }
}
