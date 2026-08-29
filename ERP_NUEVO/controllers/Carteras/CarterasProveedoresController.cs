using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class CarterasController : Utilities
    {
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetProveedores(IFormCollection fc)
        {
            try
            {
                int page = Convert.ToInt32(fc["page"].ToString());
                int pageSize = Convert.ToInt32(fc["pageSize"].ToString());
                var parameters = new Dictionary<string, object>();
                parameters.Add("offset", (page - 1) * pageSize);
                parameters.Add("pageSize", pageSize);
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                string where = "";

                if (!string.IsNullOrWhiteSpace(fc["nombre"].ToString()))
                {
                    parameters.Add("nombre", $"%{fc["nombre"].ToString()}%");
                    where = " AND (c.n_prov ILIKE @nombre OR cf.codigo ILIKE @nombre) ";
                }

                string query = "WITH cartera AS ( " +
                    "   SELECT proveedor_id, SUM(saldo_pendiente) AS saldo " +
                    "   FROM cartera_proveedores " +
                    "   WHERE empresa_id = @id_empresa " +
                    "   GROUP BY proveedor_id), " +
                    "pagos AS ( " +
                    "   SELECT proveedor_id, SUM(saldo_disponible) AS saldo_favor " +
                    "   FROM pagos_proveedor " +
                    "   GROUP BY proveedor_id) " +
                    "SELECT c.id_prov, c.cve_prov, c.n_prov, c.tel, c.rfc, cf.codigo, COALESCE(car.saldo, 0) AS saldo, " +
                    "   COALESCE(pag.saldo_favor, 0) AS saldo_favor " +
                    "FROM catproveedores c " +
                    "INNER JOIN cuentas_finanzas cf ON cf.proveedor_id = c.id_prov " +
                    "LEFT JOIN cartera car ON car.proveedor_id = c.id_prov " +
                    "LEFT JOIN pagos pag ON pag.proveedor_id = c.id_prov " +
                    "WHERE c.id_empresa = @id_empresa AND (" +
                    "       COALESCE(car.saldo, 0) > 0 " +
                    $"      OR COALESCE(pag.saldo_favor, 0) > 0) {where} " +
                    "ORDER BY saldo DESC " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);

                query = "WITH cartera AS (" +
                    "   SELECT proveedor_id, SUM(saldo_pendiente) AS saldo " +
                    "   FROM cartera_proveedores " +
                    "   WHERE empresa_id = @id_empresa " +
                    "   GROUP BY proveedor_id), " +
                    "pagos AS (" +
                    "   SELECT proveedor_id, SUM(saldo_disponible) AS saldo_favor " +
                    "   FROM pagos_proveedor " +
                    "   GROUP BY proveedor_id) " +
                    "SELECT COUNT(*) " +
                    "FROM catproveedores c " +
                    "INNER JOIN cuentas_finanzas cf ON cf.proveedor_id = c.id_prov " +
                    "LEFT JOIN cartera car ON car.proveedor_id = c.id_prov " +
                    "LEFT JOIN pagos pag ON pag.proveedor_id = c.id_prov " +
                    "WHERE c.id_empresa = @id_empresa AND (" +
                    "       COALESCE(car.saldo, 0) > 0 " +
                    $"      OR COALESCE(pag.saldo_favor, 0) > 0) {where}";
                var total = RunScalar(query, parameters);

                query = "SELECT COALESCE(SUM(car.saldo), 0) AS saldo_pagar, COALESCE(SUM(pag.saldo_favor), 0) AS saldo_favor " +
                    "FROM catproveedores c " +
                    "INNER JOIN cuentas_finanzas cf ON cf.proveedor_id = c.id_prov " +
                    "LEFT JOIN (" +
                    "   SELECT proveedor_id, SUM(saldo_pendiente) AS saldo " +
                    "   FROM cartera_proveedores " +
                    "   WHERE empresa_id = @id_empresa " +
                    "   GROUP BY proveedor_id) car ON car.proveedor_id = c.id_prov " +
                    "LEFT JOIN (" +
                    "   SELECT proveedor_id, SUM(saldo_disponible) AS saldo_favor " +
                    "   FROM pagos_proveedor " +
                    "   GROUP BY proveedor_id) pag ON pag.proveedor_id = c.id_prov " +
                    $"WHERE c.id_empresa = @id_empresa {where}";
                var saldoPagar = RunScalar(query, parameters);

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { data, total, saldoPagar, icon = "success" });
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", text = ex.Message });
            }
        }

        public JsonResult GetCarteraProveedores(IFormCollection fc)
        {
            int provId = int.TryParse(fc["provId"].ToString(), out int c) ? c : 0;
            int page = int.TryParse(fc["page"].ToString(), out int p) ? p : 1;
            int pageSize = int.TryParse(fc["pageSize"].ToString(), out int ps) ? ps : 10;
            string nombre = fc["nombre"].ToString();
            string where = "";

            var parameters = new Dictionary<string, object>
            {
                { "provId", provId },
                { "nombre", $"%{nombre}%" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            if (!string.IsNullOrEmpty(nombre))
            {
                where += " AND (LOWER(cp.uuid::text) LIKE LOWER(@nombre) OR LOWER(em.uuid::text) LIKE LOWER(@nombre)" +
                " OR LOWER(em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%')) ";
            }

            string query = "SELECT em.tipo_proceso, em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END folio, " +
                "   cp.id_cartera_proveedor AS factura_original, cp.monto_total, cp.saldo_pendiente, " +
                "   cp.poliza_id, c.n_prov, cp.uuid cartera_uuid, cp.fecha_emision " +
                "FROM cartera_proveedores cp " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = cp.encabezado_id " +
                "INNER JOIN catproveedores c ON c.id_prov = cp.proveedor_id  AND c.id_empresa = @id_empresa " +
                $"WHERE cp.proveedor_id = @provId AND cp.cancelada = false {where} " +
                "ORDER BY CASE WHEN cp.saldo_pendiente > 0 THEN 1 ELSE 0 END DESC, cp.fecha_emision ASC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM cartera_proveedores cp " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = cp.encabezado_id " +
                "INNER JOIN catproveedores c ON c.id_prov = cp.proveedor_id  AND c.id_empresa = @id_empresa " +
                $"WHERE cp.proveedor_id = @provId AND cp.cancelada = false {where} ";
            var total = RunScalar(query, parameters);

            parameters = new Dictionary<string, object>();
            parameters.Add("id_proveedor", provId);
            query = "SELECT COALESCE(SUM(cc.saldo_disponible), 0) FROM pagos_proveedor cc WHERE cc.proveedor_id = @id_proveedor";
            int saldoFavor = Convert.ToInt32(RunScalar(query, parameters));

            query = "SELECT COALESCE(SUM(cc.monto_total ), 0) FROM cartera_proveedores cc WHERE cc.proveedor_id = @id_proveedor " +
                "   AND fecha_creacion >= date_trunc('month', CURRENT_DATE) " +
                "   AND fecha_creacion < date_trunc('month', CURRENT_DATE) + INTERVAL '1 month'";
            decimal totalVendido = Convert.ToDecimal(RunScalar(query, parameters));

            query = "SELECT COALESCE(SUM(cc.saldo_pendiente ), 0) FROM cartera_proveedores cc WHERE cc.proveedor_id = @id_proveedor " +
                "   AND fecha_creacion >= date_trunc('month', CURRENT_DATE) " +
                "   AND fecha_creacion < date_trunc('month', CURRENT_DATE) + INTERVAL '1 month'";
            decimal totalPendiente = Convert.ToDecimal(RunScalar(query, parameters));

            Response.StatusCode = (int)HttpStatusCode.OK;
            return Json(new { data, total, totales = new { saldoFavor, totalVendido, totalPendiente } });
        }

        public JsonResult GetDetallesCompraProveedor(IFormCollection fc)
        {
            int clienteId = int.TryParse(fc["clienteId"].ToString(), out int c) ? c : 0;
            int page = int.TryParse(fc["page"].ToString(), out int p) ? p : 1;
            int pageSize = int.TryParse(fc["pageSize"].ToString(), out int ps) ? ps : 10;
            string nombre = fc["nombre"].ToString();
            string where = "";
            var parameters = new Dictionary<string, object>
            {
                { "clienteId", clienteId },
                { "nombre", $"%{nombre}%" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize }
            };

            if (!string.IsNullOrEmpty(nombre))
            {
                where = " AND (LOWER(em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%')) ";
            }

            string query = "SELECT em.tipo_proceso, pp.fecha_pago, em.uuid, cp.estado, cp.saldo_pendiente, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END folio, " +
                "   cp.id_cartera_proveedor AS factura_original, pp.monto, pp.fecha_pago fecha " +
                "FROM aplicaciones_pago_proveedor app " +
                "INNER JOIN pagos_proveedor pp ON pp.id_pago = app.pago_id " +
                "INNER JOIN cartera_proveedores cp ON cp.id_cartera_proveedor = app.cartera_id " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = app.encabezado_id " +
                $"WHERE cp.id_cartera_proveedor = @poliza_padre {where} " +
                "ORDER BY cp.fecha_emision ASC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            parameters.Add("poliza_padre", Convert.ToInt32(fc["id_factura_padre"].ToString()));
            var pagos = RunQuery(query, parameters);

            return Json(pagos);
        }
    }
}