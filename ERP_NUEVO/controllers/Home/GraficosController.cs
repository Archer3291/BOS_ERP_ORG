using BOS_ERP.Controllers;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.controllers
{
    public partial class HomeController : Utilities
    {
        public JsonResult GetVentasDia()
        {
            string query = "SELECT f.folio, f.rsocliente, f.fecha, f.total, f.moneda, f.observaciones, f.tipo, c.descripcion " +
                "FROM factura f " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= CURRENT_DATE AND f.fecha < CURRENT_DATE + INTERVAL '1 day' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult GetTotalVentasDia()
        {
            string query = "SELECT COUNT(*) total_facturas, COALESCE(SUM(f.total), 0) monto_total, c.descripcion " +
                "FROM factura f " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= CURRENT_DATE AND f.fecha < CURRENT_DATE + INTERVAL '1 day' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "GROUP BY c.descripcion;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult GetFacturasVencidas()
        {
            string query = "SELECT f.folio, f.rsocliente, f.fecha, f.moneda, f.observaciones, f.tipo, f.total, cc.saldo_pendiente, f.fechatimbrado, c.descripcion " +
                "FROM factura f " +
                "INNER JOIN cartera_clientes cc ON cc.encabezado_id = f.encabezado_id " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE cc.fecha_vencimiento >= CURRENT_DATE AND cc.fecha_vencimiento < CURRENT_DATE + INTERVAL '1 day' AND f.tipo = 'CREDITO' AND cc.saldo_pendiente > 0 AND c.empresa_id = @empresa " +
                "ORDER BY cc.saldo_pendiente DESC;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult MontoRecaudadoDia()
        {
            string query = "SELECT COUNT(*) total_cobros, COALESCE(SUM(cc.monto), 0) total_cobrado, c.descripcion " +
                "FROM cobros_cliente cc " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE cc.fecha_cobro >= CURRENT_DATE AND cc.fecha_cobro < CURRENT_DATE + INTERVAL '1 day' AND cc.cancelado = FALSE AND cc.tipo_cobro IN ('normal', 'complemento') AND c.empresa_id = @empresa " +
                "GROUP BY c.descripcion;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult TopDiezProductosMasVendidos()
        {
            string query = "SELECT SUM(pd.cant_ud) total, pd.cve_prod, pd.descr_prod, pd.ud, c.descripcion " +
                "FROM factura f " +
                "INNER JOIN partidasdoc pd ON pd.encabezado_id = f.encabezado_id " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= CURRENT_DATE - INTERVAL '60 days' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "GROUP BY pd.cve_prod, pd.ud, c.descripcion, pd.descr_prod " +
                "ORDER BY total DESC " +
                "LIMIT 10;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult TopDiezProductosMenosVendidos()
        {
            string query = "SELECT SUM(pd.cant_ud) total, pd.cve_prod, pd.descr_prod, pd.ud, c.descripcion " +
                "FROM factura f " +
                "INNER JOIN partidasdoc pd ON pd.encabezado_id = f.encabezado_id " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= CURRENT_DATE - INTERVAL '60 days' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "GROUP BY pd.cve_prod, pd.ud, c.descripcion, pd.descr_prod " +
                "ORDER BY total ASC " +
                "LIMIT 10;";
            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult TopDiezClientesMasCompran()
        {
            string query = "SELECT SUM(f.total) total, f.rsocliente, c.descripcion " +
                "FROM factura f " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= CURRENT_DATE - INTERVAL '60 days' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "GROUP BY f.rsocliente, c.descripcion " +
                "ORDER BY total DESC " +
                "LIMIT 10;";
            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult TopDiezClientesMenosCompran()
        {
            string query = "SELECT SUM(f.total) total, f.rsocliente, c.descripcion " +
                "FROM factura f " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= CURRENT_DATE - INTERVAL '60 days' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "GROUP BY f.rsocliente, c.descripcion " +
                "ORDER BY total ASC " +
                "LIMIT 10;";
            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult TendenciaTreintaDias()
        {
            string query = "SELECT x.dia, x.descripcion, " +
                "SUM(x.facturado) facturado, SUM(x.facturas) facturas, SUM(x.cobrado) cobrado " +
                "FROM ( " +
                "    SELECT f.fecha::date dia, c.descripcion, f.total facturado, 1 facturas, 0::numeric cobrado " +
                "    FROM factura f " +
                "    INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "    INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "    WHERE f.fecha >= CURRENT_DATE - INTERVAL '29 days' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "    UNION ALL " +
                "    SELECT cc.fecha_cobro::date, c.descripcion, 0::numeric, 0, cc.monto " +
                "    FROM cobros_cliente cc " +
                "    INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "    INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "    WHERE cc.fecha_cobro >= CURRENT_DATE - INTERVAL '29 days' " +
                "      AND cc.cancelado = FALSE AND cc.tipo_cobro IN ('normal', 'complemento') AND c.empresa_id = @empresa " +
                ") x " +
                "GROUP BY x.dia, x.descripcion " +
                "ORDER BY x.dia;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult AntiguedadSaldos()
        {
            string query = "SELECT c.descripcion, " +
                "  CASE WHEN cc.fecha_vencimiento::date >= CURRENT_DATE THEN 'Por vencer' " +
                "       WHEN CURRENT_DATE - cc.fecha_vencimiento::date <= 30 THEN '1 a 30 días' " +
                "       WHEN CURRENT_DATE - cc.fecha_vencimiento::date <= 60 THEN '31 a 60 días' " +
                "       WHEN CURRENT_DATE - cc.fecha_vencimiento::date <= 90 THEN '61 a 90 días' " +
                "       ELSE 'Más de 90 días' END AS rango, " +
                "  CASE WHEN cc.fecha_vencimiento::date >= CURRENT_DATE THEN 0 " +
                "       WHEN CURRENT_DATE - cc.fecha_vencimiento::date <= 30 THEN 1 " +
                "       WHEN CURRENT_DATE - cc.fecha_vencimiento::date <= 60 THEN 2 " +
                "       WHEN CURRENT_DATE - cc.fecha_vencimiento::date <= 90 THEN 3 " +
                "       ELSE 4 END AS orden, " +
                "  COUNT(*) facturas, SUM(cc.saldo_pendiente) saldo " +
                "FROM cartera_clientes cc " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE cc.saldo_pendiente > 0 AND c.empresa_id = @empresa " +
                "GROUP BY c.descripcion, 2, 3 " +
                "ORDER BY c.descripcion, 3;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult ProductosPorImporte()
        {
            string baseQuery = "SELECT '{0}' orden, pd.cve_prod, pd.descr_prod, pd.ud, c.descripcion, " +
                "SUM(pd.cant_ud) unidades, SUM(pd.imp_part) importe " +
                "FROM factura f " +
                "INNER JOIN partidasdoc pd ON pd.encabezado_id = f.encabezado_id " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= CURRENT_DATE - INTERVAL '60 days' AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "GROUP BY pd.cve_prod, pd.descr_prod, pd.ud, c.descripcion " +
                "ORDER BY importe {1} " +
                "LIMIT 10";

            string query = "(" + string.Format(baseQuery, "mas", "DESC") + ") " +
                "UNION ALL " +
                "(" + string.Format(baseQuery, "menos", "ASC") + ");";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult VentasMesComparativo()
        {
            string query = "SELECT date_trunc('month', f.fecha)::date mes, c.descripcion, " +
                "COUNT(*) facturas, SUM(f.total) monto " +
                "FROM factura f " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= date_trunc('month', CURRENT_DATE - INTERVAL '1 month') " +
                "  AND f.tipo IN('CREDITO', 'CONTADO') " +
                "  AND c.empresa_id = @empresa " +
                "GROUP BY 1, 2 " +
                "ORDER BY 1;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult ClientesRiesgo()
        {
            string query = "SELECT f.rsocliente, c.descripcion, COUNT(*) facturas, " +
                "SUM(cc.saldo_pendiente) saldo_vencido, " +
                "MAX(CURRENT_DATE - cc.fecha_vencimiento::date) dias_atraso " +
                "FROM cartera_clientes cc " +
                "INNER JOIN factura f ON f.encabezado_id = cc.encabezado_id " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE cc.saldo_pendiente > 0 AND cc.fecha_vencimiento::date < CURRENT_DATE AND c.empresa_id = @empresa " +
                "GROUP BY f.rsocliente, c.descripcion " +
                "ORDER BY saldo_vencido DESC " +
                "LIMIT 20;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult FacturacionFormaPago()
        {
            string query = "SELECT COALESCE(NULLIF(TRIM(f.idtipopago::text), ''), 'ND') forma_pago, " +
                "COALESCE(NULLIF(TRIM(f.mdpfactura::text), ''), 'ND') metodo, " +
                "c.descripcion, COUNT(*) facturas, SUM(f.total) monto " +
                "FROM factura f " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
                "INNER JOIN catsucursales c ON c.id_sucursal = e.suc " +
                "WHERE f.fecha >= date_trunc('month', CURRENT_DATE) AND f.tipo IN('CREDITO', 'CONTADO') AND c.empresa_id = @empresa " +
                "GROUP BY 1, 2, 3 " +
                "ORDER BY monto DESC;";

            var parameters = new Dictionary<string, object>();
            parameters.Add("@empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult TodosCortes()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT tpc.id_corte, tpc.folio, cp.cve_prod, cp.descr_prod, tpc.longitud, tp.cantidad, tpc.cantidad cantidad_cortes, " +
                "   SUM((tpc.longitud * tpc.cantidad)) suma_cortes, tpc.activo, tpc.fecha_creacion, tpc.usuario_creacion, tpc.comentario, cn.ulocation " +
                "FROM tarima_productos_cortes tpc " +
                "INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id AND tp.cantidad > 0 " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id " +
                "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "WHERE tpc.cantidad > 0 AND cp.empresa_id = @empresa_id " +
                "GROUP BY tpc.id_corte, tpc.folio, cp.cve_prod, cp.descr_prod, tpc.longitud, tp.cantidad, tpc.cantidad, tpc.activo, tpc.fecha_creacion, " +
                "   tpc.usuario_creacion, tpc.comentario, cn.ulocation " +
                "ORDER BY cp.cve_prod, suma_cortes";

            var data = RunQuery(query, parameters);
            return Json(data);
        }

        public JsonResult CantidadCortes()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT cp.cve_prod, cp.descr_prod, SUM((tpc.longitud * tpc.cantidad)) longitud, tp.cantidad metros_totales, SUM(tpc.cantidad) cantidad_cortes " +
                "FROM tarima_productos_cortes tpc " +
                "INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id AND tp.cantidad > 0 " +
                "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id " +
                "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "WHERE tpc.cantidad > 0 AND cp.empresa_id = @empresa_id " +
                "GROUP BY cp.cve_prod, cp.descr_prod, tp.cantidad";

            var data = RunQuery(query, parameters);
            return Json(data);
        }
    }
}
