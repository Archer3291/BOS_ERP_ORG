using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Compras
{
    public class OrdenCompraController : Utilities
    {
        public JsonResult GetOrdenesCompras(IFormCollection fc)
        {
            int pageSize = int.TryParse(fc["pageSize"].ToString(), out int ps) && ps > 0 ? ps : 10;
            int page = int.TryParse(fc["page"].ToString(), out int p) && p > 0 ? p : 1;
            string sortColumn = fc["sortColumn"].ToString() ?? "";
            string sortDir = fc["sortDir"].ToString()?.ToLowerInvariant() == "asc" ? "ASC" : "DESC";
            string nombre = (fc["nombre"].ToString() ?? "").Trim();

            var columnasOrdenables = new Dictionary<string, string>
            {
                ["folio"] = "e.folio",
                ["n_prov"] = "c.n_prov",
                ["comprador"] = "u.nombre",
                ["saldo"] = "cp.saldo_pendiente",
                ["total"] = "cp.monto_total",
                ["fch"] = "e.fch",
            };

            if (!columnasOrdenables.ContainsKey(sortColumn))
                sortColumn = "fch";

            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "empresa", HttpContext.Session.GetInt32("Empresa") },
            };

            string where = "";
            if (!string.IsNullOrEmpty(nombre))
            {
                where = " AND (" +
                    "   e.folio ILIKE '%' || @nombre || '%' " +
                    "   OR c.n_prov ILIKE '%' || @nombre || '%' " +
                    "   OR u.nombre || ' ' || u.apellido ILIKE '%' || @nombre || '%' " +
                    ") ";
            }

            string from = "FROM cartera_proveedores cp " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = cp.encabezado_id " +
                "INNER JOIN catproveedores c ON c.id_prov = e.refe " +
                "INNER JOIN usuarios u ON u.usuarioid = e.usr2 " +
                $"WHERE cp.empresa_id = @empresa {where} ";

            string query = "SELECT e.id_encabezado, cp.id_cartera_proveedor, e.folio, c.n_prov, " +
                "   cp.saldo_pendiente saldo, cp.monto_total total, COALESCE(e.sub, cp.monto_total) subtotal, " +
                "   COALESCE(e.dto, 0) descuento, u.nombre || ' ' || u.apellido comprador, e.fch " +
                from +
                $"ORDER BY {columnasOrdenables[sortColumn]} {sortDir} NULLS LAST " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var ordenes = RunQuery(query, parameters);

            var total = Convert.ToInt32(RunScalar($"SELECT COUNT(*) {from}", parameters));

            return Json(new { data = ordenes, total });
        }

        public JsonResult GetDetalleOrdenCompra(IFormCollection fc)
        {
            int carteraId = int.TryParse(fc["id"].ToString(), out int id) ? id : 0;

            // -- Encabezado de la orden
            string queryEncabezado = "SELECT cp.id_cartera_proveedor, cp.monto_total, cp.saldo_pendiente, cp.estado, cp.moneda, cp.fecha_emision, cp.fecha_vencimiento, " +
                "    cp.cancelada, e.folio, e.fch fecha_documento, e.orden_compra, c.n_prov proveedor, u.nombre || ' ' || u.apellido comprador " +
                "FROM cartera_proveedores cp " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = cp.encabezado_id " +
                "INNER JOIN catproveedores c ON c.id_prov = cp.proveedor_id " +
                "INNER JOIN usuarios u ON u.usuarioid = e.usr2 " +
                "WHERE cp.id_cartera_proveedor = @carteraId";

            // -- Historial de pagos aplicados
            string queryPagos = "SELECT ap.id_aplicacion, ap.fecha_aplicacion, ap.monto_aplicado, pp.referencia, pp.observaciones, pp.fecha_pago, pp.moneda moneda_pago, " +
                "    e.folio folio_documento, u.nombre || ' ' || u.apellido registrado_por " +
                "FROM aplicaciones_pago_proveedor ap " +
                "INNER JOIN pagos_proveedor pp ON pp.id_pago = ap.pago_id " +
                "LEFT  JOIN encabezadomov e ON e.id_encabezado = ap.encabezado_id " +
                "LEFT  JOIN usuarios u ON u.usuarioid = ap.creado_por " +
                "WHERE ap.cartera_id = @carteraId " +
                "ORDER BY ap.fecha_aplicacion DESC";

            var parameters = new Dictionary<string, object> { { "carteraId", carteraId } };

            var encabezado = RunQuery(queryEncabezado, parameters).Cast<IDictionary<string, object>>().FirstOrDefault();
            var pagos = RunQuery(queryPagos, parameters);

            return Json(new { encabezado, pagos });
        }
    }
}