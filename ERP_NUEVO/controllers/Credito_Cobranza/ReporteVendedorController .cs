using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    [RightAuthorize(new[] { "facturacion_especial", "facturacion_normal", "facturacion_global", "complemento_pago" })]
    public class ReporteVendedorController : Utilities
    {
        // ============================================
        // VISTA PRINCIPAL
        // ============================================

        public IActionResult Index()
        {
            return View();
        }

        // ============================================
        // HELPERS DE PERMISOS Y SUCURSAL
        // (copiados de FacturaConsultaController para
        //  mantener la misma lógica de seguridad)
        // ============================================

        private string GetFiltroTipoFactura()
        {
            var tiposPermitidos = new List<string>();

            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_especial_internacional"))
                tiposPermitidos.Add("'VIS'");
            if (Utilities.DoesUserHasRight(User.Identity.Name, "factura_arrendamiento"))
                tiposPermitidos.Add("'FAR'");
            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
                tiposPermitidos.Add("'VI', 'VS'");
            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_global"))
                tiposPermitidos.Add("'G'");
            if (Utilities.DoesUserHasRight(User.Identity.Name, "complemento_pago"))
                tiposPermitidos.Add("'CC'");

            return tiposPermitidos.Count > 0
                ? $"AND fa.serie IN ({string.Join(",", tiposPermitidos)})"
                : "AND 1=0";
        }

        private string GetFiltroSucursal()
        {
            return "AND em.suc = @suc";
        }

        // ============================================
        // OBTENER REPORTE POR VENDEDOR
        // ============================================

        [HttpGet]
        public JsonResult ObtenerReporteVendedor(
            string fechaInicio = "",
            string fechaFin = "",
            string vendedorId = "",
            string statusFac = "",
            string moneda = "")
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            string filtroTipo = GetFiltroTipoFactura();
            string filtroSuc = GetFiltroSucursal();

            var conditions = new List<string>();
            var parameters = new Dictionary<string, object>
            {
                { "suc", suc }
            };

            if (!string.IsNullOrWhiteSpace(fechaInicio))
            {
                conditions.Add("fa.fecha >= @fechaInicio");
                parameters["fechaInicio"] = DateTime.Parse(fechaInicio);
            }
            if (!string.IsNullOrWhiteSpace(fechaFin))
            {
                conditions.Add("fa.fecha <= @fechaFin");
                parameters["fechaFin"] = DateTime.Parse(fechaFin + " 23:59:59");
            }
            if (!string.IsNullOrWhiteSpace(vendedorId))
            {
                conditions.Add("em.vdr_cpr = @vendedorId");
                parameters["vendedorId"] = vendedorId;
            }
            if (!string.IsNullOrWhiteSpace(statusFac))
            {
                conditions.Add("LOWER(fa.statusfactura) = LOWER(@statusFac)");
                parameters["statusFac"] = statusFac;
            }
            if (!string.IsNullOrWhiteSpace(moneda))
            {
                conditions.Add("UPPER(fa.moneda) = UPPER(@moneda)");
                parameters["moneda"] = moneda;
            }

            string extraWhere = conditions.Count > 0
                ? "AND " + string.Join(" AND ", conditions)
                : "";

            // ── Resumen por vendedor ───────────────────────────────────────────
            string queryResumen = $@"
        SELECT
            COALESCE(v.id::text, 'SIN_VENDEDOR')              AS vendedor_id,
            COALESCE(v.nombre, 'Sin vendedor asignado')        AS vendedor_nombre,
            COALESCE(v.clave_vendedor, '—')                    AS clave_vendedor,
            COALESCE(v.correo1, '')                            AS correo_vendedor,

            COUNT(DISTINCT fa.id)                              AS total_facturas,
            COUNT(DISTINCT CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.id END)                                AS facturas_vigentes,
            COUNT(DISTINCT CASE
                WHEN fa.statusfactura IN ('Cancelada','Error al Cancelar')
                THEN fa.id END)                                AS facturas_canceladas,

            COALESCE(SUM(CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.subtotal ELSE 0 END), 0)               AS suma_subtotal,
            COALESCE(SUM(CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.iva ELSE 0 END), 0)                    AS suma_iva,
            COALESCE(SUM(CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.total ELSE 0 END), 0)                  AS suma_total,
            COALESCE(SUM(CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.descuento ELSE 0 END), 0)              AS suma_descuento,

            COALESCE(SUM(CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN COALESCE(cc.saldo_pendiente, 0) ELSE 0 END), 0) AS saldo_total,

            COUNT(DISTINCT fa.idcliente)                       AS clientes_distintos,

            CASE
                WHEN COUNT(DISTINCT CASE
                        WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                        THEN fa.id END) > 0
                THEN SUM(CASE
                        WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                        THEN fa.total ELSE 0 END)
                     / COUNT(DISTINCT CASE
                        WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                        THEN fa.id END)
                ELSE 0
            END                                                AS ticket_promedio,

            MAX(fa.fecha)                                      AS ultima_factura,

            CASE
                WHEN SUM(CASE
                        WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                        THEN fa.total ELSE 0 END) > 0
                THEN ROUND(
                    (1 - SUM(CASE
                                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                                THEN COALESCE(cc.saldo_pendiente, 0) ELSE 0 END)
                         / SUM(CASE
                                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                                THEN fa.total ELSE 0 END)) * 100, 1)
                ELSE 0
            END                                                AS pct_cobrado

        FROM factura fa
        INNER JOIN encabezadomov em    ON em.id_encabezado = fa.encabezado_id
        LEFT  JOIN vendedores v        ON v.clave_vendedor  = em.vdr_cpr
        LEFT  JOIN cartera_clientes cc ON cc.encabezado_id  = fa.encabezado_id
        WHERE em.variacion = 0
          {filtroSuc}
          {filtroTipo}
          {extraWhere}
        GROUP BY v.id, v.nombre, v.clave_vendedor, v.correo1
        ORDER BY suma_total DESC";

            var resumen = RunQuery(queryResumen, parameters);

            // ── Resumen mensual (para gráficas) ───────────────────────────────
            string queryMensual = $@"
        SELECT
            TO_CHAR(fa.fecha, 'YYYY-MM')                       AS periodo,
            TO_CHAR(fa.fecha, 'Mon YYYY')                      AS periodo_label,
            COALESCE(v.id::text, 'SIN_VENDEDOR')               AS vendedor_id,
            COALESCE(v.nombre, 'Sin vendedor asignado')        AS vendedor_nombre,
            COALESCE(v.clave_vendedor, '—')                    AS clave_vendedor,
            COUNT(DISTINCT CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.id END)                                AS total_facturas,
            COALESCE(SUM(CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.total ELSE 0 END), 0)                 AS suma_total,
            COALESCE(SUM(CASE
                WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN COALESCE(cc.saldo_pendiente, 0) ELSE 0 END), 0) AS saldo_total,
            COUNT(DISTINCT fa.idcliente)                       AS clientes_distintos
        FROM factura fa
        INNER JOIN encabezadomov em    ON em.id_encabezado = fa.encabezado_id
        LEFT  JOIN vendedores v        ON v.clave_vendedor  = em.vdr_cpr
        LEFT  JOIN cartera_clientes cc ON cc.encabezado_id  = fa.encabezado_id
        WHERE em.variacion = 0
          {filtroSuc}
          {filtroTipo}
          {extraWhere}
        GROUP BY
            TO_CHAR(fa.fecha, 'YYYY-MM'),
            TO_CHAR(fa.fecha, 'Mon YYYY'),
            v.id, v.nombre, v.clave_vendedor
        ORDER BY periodo ASC, suma_total DESC";

            var resumenMensual = RunQuery(queryMensual, parameters);

            // ── Totales generales ──────────────────────────────────────────────
            string queryTotales = $@"
        SELECT
            COUNT(DISTINCT fa.id)   AS total_facturas,
            SUM(CASE WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN fa.total  ELSE 0 END) AS gran_total,
            SUM(CASE WHEN fa.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                THEN COALESCE(cc.saldo_pendiente,0) ELSE 0 END) AS gran_saldo,
            COUNT(DISTINCT fa.idcliente) AS total_clientes
        FROM factura fa
        INNER JOIN encabezadomov em    ON em.id_encabezado = fa.encabezado_id
        LEFT  JOIN vendedores v        ON v.clave_vendedor  = em.vdr_cpr
        LEFT  JOIN cartera_clientes cc ON cc.encabezado_id  = fa.encabezado_id
        WHERE em.variacion = 0
          {filtroSuc}
          {filtroTipo}
          {extraWhere}";

            var totalesRows = RunQuery(queryTotales, parameters);
            var totales = totalesRows.Count > 0 ? totalesRows[0] : null;

            // ── Lista de vendedores activos (para el filtro) ───────────────────
            string queryVendedores = @"
        SELECT id, nombre, clave_vendedor
        FROM   vendedores
        WHERE  activo = true
          AND  sucursal_id = @suc_str
        ORDER BY nombre";

            var vendedores = RunQuery(queryVendedores, new Dictionary<string, object>
            {
                { "suc_str", suc.ToString() }
            });

            return Json(new
            {
                resumen,
                resumenMensual,
                totales,
                vendedores
            });
        }

        // ============================================
        // OBTENER DETALLE DE FACTURAS POR VENDEDOR
        // ============================================

        [HttpGet]
        public JsonResult ObtenerDetalleVendedor(
            string claveVendedor,
            string fechaInicio = "",
            string fechaFin = "",
            int page = 1,
            int pageSize = 50)
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            string filtroTipo = GetFiltroTipoFactura();
            string filtroSuc = GetFiltroSucursal();

            var parameters = new Dictionary<string, object>
            {
                { "suc",           suc },
                { "claveVendedor", claveVendedor },
                { "offset",        (page - 1) * pageSize },
                { "pageSize",      pageSize }
            };

            var conditions = new List<string> { "em.vdr_cpr = @claveVendedor" };

            if (!string.IsNullOrWhiteSpace(fechaInicio))
            {
                conditions.Add("fa.fecha >= @fechaInicio");
                parameters["fechaInicio"] = DateTime.Parse(fechaInicio);
            }
            if (!string.IsNullOrWhiteSpace(fechaFin))
            {
                conditions.Add("fa.fecha <= @fechaFin");
                parameters["fechaFin"] = DateTime.Parse(fechaFin + " 23:59:59");
            }

            string extraWhere = "AND " + string.Join(" AND ", conditions);

            string query = $@"
        SELECT
            em.folio || CASE WHEN em.variacion > 0
                THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio,
            fa.serie,
            fa.folio         AS fac_folio,
            fa.fecha,
            fa.rfccliente,
            fa.rsocliente,
            fa.subtotal,
            fa.iva,
            fa.total,
            fa.descuento,
            fa.moneda,
            fa.statusfactura,
            fa.mdpfactura,
            fa.uuid,
            fa.tipo,
            COALESCE(cc.saldo_pendiente, 0) AS saldo
        FROM factura fa
        INNER JOIN encabezadomov em    ON em.id_encabezado = fa.encabezado_id
        LEFT  JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id
        WHERE em.variacion = 0
          {filtroSuc}
          {filtroTipo}
          {extraWhere}
        ORDER BY fa.fecha DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var detalle = RunQuery(query, parameters);

            string queryCount = $@"
        SELECT COUNT(*) FROM factura fa
        INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
        WHERE em.variacion = 0
          {filtroSuc}
          {filtroTipo}
          {extraWhere}";

            var total = RunScalar(queryCount, parameters);

            return Json(new { detalle, total });
        }

        // ============================================
        // DATOS PARA SELECTS (vendedores / monedas)
        // ============================================

        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryMoneda = "SELECT clave as id, nombre as nombre FROM cat_monedas ORDER BY clave DESC";
            string queryVendedor = "SELECT c2 as id, c3 as nombre FROM sellosop.kduv ORDER BY c1 DESC";

            result.Add("monedas", RunQuery(queryMoneda));
            result.Add("vendedores", RunQuery(queryVendedor));

            return Json(result);
        }
    }
}