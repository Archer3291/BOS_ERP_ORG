using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Almacen
{
    /// <summary>
    /// Sincronización del catálogo contra las bases de datos externas.
    ///
    /// Hay dos modos: traer el catálogo completo de la empresa en sesión
    /// (SyncProducts) o importar sólo los productos que el usuario marcó
    /// (GetProductosSync).
    /// </summary>
    public partial class ProductosController : Utilities
    {
        /// <summary>
        /// Importa el catálogo completo desde la empresa de facturación
        /// que tenga la sesión.
        /// </summary>
        [ValidateAntiForgeryToken, HttpPost]
        public JsonResult SyncProducts()
        {
            var empresaNombre = HttpContext.Session.GetString("EmpresaFactura");

            if (string.IsNullOrEmpty(empresaNombre))
                return Json(new { icon = "error", title = "Ocurrió un error inesperado", html = "EmpresaFactura no definida en sesión" });

            return EjecutarSincronizacion(empresaNombre, new List<string>());
        }

        /// <summary>
        /// Importa únicamente los productos indicados desde la empresa
        /// que el usuario eligió en el modal.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetProductosSync(List<string> ids, string empresa)
        {
            if (string.IsNullOrEmpty(empresa))
                return Json(new { icon = "error", title = "Ocurrió un error inesperado", html = "EmpresaFactura no definida." });

            return EjecutarSincronizacion(empresa, ids);
        }

        /// <summary>
        /// Todo el lote va en una sola transacción: si un producto falla,
        /// no se inserta ninguno.
        /// </summary>
        /// <param name="ids">Vacío importa el catálogo completo.</param>
        private JsonResult EjecutarSincronizacion(string empresa, List<string> ids)
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
                            var productService = new ProductoSyncService();

                            int? empresaId = GetInt(HttpContext.Session.GetInt32("Empresa"));
                            var connString = utils._configuration.GetConnectionString(empresa);

                            productService.SincronizarEmpresa(empresaId, connString, ids, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Los productos han sido sincronizado exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrió un error inesperado", html = ex.Message });
            }
        }

        /// <summary>
        /// Página del catálogo de la base externa, para elegir qué
        /// productos importar.
        /// </summary>
        public JsonResult GetProductosEmpresa(string empresa, string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("nombre", nombre);

            string where = "";
            if (!string.IsNullOrWhiteSpace(nombre))
                where += " AND (c1 ILIKE '%' || @nombre || '%' OR c2 ILIKE '%' || @nombre || '%') ";

            string query = "SELECT c1 id, c2 descripcion, c3 lin_prod, c11 udm, c51 stat " +
                "FROM kdii " +
                $"WHERE 1=1 {where} " +
                $"OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var prods = RunQuery(query, parameters, false, null, null, empresa);

            query = $"SELECT COUNT(*) FROM kdii WHERE 1=1 {where}";
            var total = RunScalar(query, parameters, false, null, null, empresa);

            return Json(new { data = prods, total });
        }
    }
}
