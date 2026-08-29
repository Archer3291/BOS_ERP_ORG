using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Importación de clientes desde la base de datos externa (kdud).
    /// La empresa origen la elige el usuario en el modal de sincronización
    /// y determina qué cadena de conexión se usa para leer.
    /// </summary>
    public partial class ClientesController : Utilities
    {
        /// <summary>
        /// Marca cuáles de los clientes externos ya existen en el ERP.
        /// El front lo usa para señalarlos y para impedir seleccionarlos.
        ///
        /// Va en una consulta aparte porque los clientes externos viven en
        /// otra base de datos: no se pueden cruzar en un solo JOIN.
        /// </summary>
        private void MarcarYaSincronizados(List<Dictionary<string, object>> clientesExternos)
        {
            if (clientesExternos.Count == 0) return;

            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var registrados = RunQuery("SELECT cve_cli FROM catclientes WHERE empresa_id = @empresa_id", parameters)
                .Select(fila => fila["cve_cli"]?.ToString()?.Trim())
                .Where(cve => !string.IsNullOrEmpty(cve))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var cliente in clientesExternos)
            {
                var cve = cliente["cve_cli"]?.ToString()?.Trim();
                cliente["ya_sincronizado"] = cve != null && registrados.Contains(cve);
            }
        }

        /// <summary>
        /// Página de clientes disponibles en el sistema externo de la
        /// empresa indicada.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetClientesSync(string nombre, string empresa, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("nombre", nombre);

            string where = "";
            if (!string.IsNullOrWhiteSpace(nombre))
                where += " AND (c2 ILIKE '%' || @nombre || '%' OR c3 ILIKE '%' || @nombre || '%') ";

            string query = "SELECT c2 cve_cli, c3 n_cli, c15 lim_crd, c10 rfc, c51 stat " +
                "FROM kdud " +
                $"WHERE 1=1 {where} " +
                $"ORDER BY c2 ASC " +
                $"OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var prods = RunQuery(query, parameters, false, null, null, empresa);

            query = $"SELECT COUNT(*) FROM kdud WHERE 1=1 {where}";
            var total = RunScalar(query, parameters, false, null, null, empresa);

            MarcarYaSincronizados(prods);

            return Json(new { data = prods, total });
        }

        /// <summary>
        /// Importa al ERP los clientes seleccionados. Todo el lote va en
        /// una sola transacción: si uno falla, no se inserta ninguno.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SincronizarClientes(List<string> ids, string empresa)
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
                            if (string.IsNullOrEmpty(empresa))
                                throw new Exception("EmpresaFactura no definida.");

                            var connString = utils._configuration.GetConnectionString(empresa);
                            if (string.IsNullOrWhiteSpace(connString))
                                throw new Exception($"No se encontró la cadena de conexión '{empresa}'.");

                            int? empresaId = GetInt(HttpContext.Session.GetInt32("Empresa"));
                            if (!empresaId.HasValue)
                                throw new Exception("No se encontró la empresa en la sesión.");

                            _clientSyncService.SincronizarCliente(empresaId.Value, connString, ids, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Los clientes han sido sincronizado exitosamente", html = "Por favor, corrobore la informacion de los registros insertados" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrió un error inesperado", html = ex.Message });
            }
        }
    }
}
