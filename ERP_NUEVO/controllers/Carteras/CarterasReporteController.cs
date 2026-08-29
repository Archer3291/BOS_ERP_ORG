using ClosedXML.Excel;
using System.Collections.Specialized;
using System.Configuration;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace BOS_ERP.Controllers
{
    public partial class CarterasController : Utilities
    {
        public IConfiguration _configuration;
        public string _defaultConnectionName = "ERP_SRS";

        public CarterasController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        #region Funciones del reporte
        // ══════════════════════════════════════════════════════════════════
        //  COMBOS PARA FILTROS
        // ══════════════════════════════════════════════════════════════════

        public JsonResult GetVendedoresReporte()
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                string query = @"
                    SELECT DISTINCT c.cve_vdr, COALESCE(v.nombre, c.cve_vdr) AS nombre
                    FROM catclientes c
                    LEFT JOIN vendedores v ON v.clave_vendedor = c.cve_vdr
                    WHERE c.empresa_id = @empresa_id
                        AND c.cve_vdr IS NOT NULL
                        AND c.cve_vdr <> ''
                    ORDER BY nombre";

                var parameters = new Dictionary<string, object> { { "empresa_id", empresaId } };
                return Json(RunQuery(query, parameters));
            }
            catch { return Json(new List<object>()); }
        }

        public JsonResult GetSucursalesReporte()
        {
            int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

            string query = "SELECT id_sucursal, cve_sucursal, descripcion " +
                "FROM catsucursales " +
                "WHERE empresa_id = @empresa";
            var parameters = new Dictionary<string, object> { { "empresa", empresaId } };

            return Json(RunQuery(query, parameters));

        }

        // ══════════════════════════════════════════════════════════════════
        //  DATOS DEL REPORTE — tabla principal + KPIs
        // ══════════════════════════════════════════════════════════════════

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetReporteCartera(IFormCollection fc)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int? page = GetInt(fc["page"].ToString(), 1);
                int? pageSize = GetInt(fc["pageSize"].ToString(), 10);
                string nombre = GetString(fc["nombre"].ToString());
                string sortColumn = GetString(fc["sortColumn"].ToString());
                string sortDir = GetString(fc["sortDir"].ToString());

                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id", empresaId },
                    { "offset",     (page - 1) * pageSize },
                    { "pageSize",   pageSize }
                };

                string where = BuildReporteWhere(fc, parameters);

                if (!string.IsNullOrWhiteSpace(nombre))
                {
                    parameters.Add("nombre", nombre);

                    where += " AND (" +
                        "   e.tipo_proceso ILIKE '%' || @nombre || '%' OR " +
                        "   e.folio ILIKE '%' || @nombre || '%' OR " +
                        "   cc.estado ILIKE '%' || @nombre || '%' OR " +
                        "   c.cve_cli ILIKE '%' || @nombre || '%' OR " +
                        "   c.n_cli ILIKE '%' || @nombre || '%' OR " +
                        "   v.nombre ILIKE '%' || @nombre || '%' " +
                        ") ";
                }

                var allowedColumns = new HashSet<string> {
                    "n_cli", "cve_cli", "rfc", "clave_vendedor",
                    "nombre_vendedor", "folio", "nat", "tipo_proceso",
                    "fecha_emision", "fecha_vencimiento", "dias_vencido",
                    "monto_total", "cobrado", "saldo_pendiente",
                    "estado", "saldo_favor", "moneda", "dias_cobro", "factura_uuid",
                };

                if (!allowedColumns.Contains(sortColumn))
                    sortColumn = "n_cli";

                string query = $@"
                    SELECT
                        cc.id_cartera_cliente,
                        c.id_cliente,
                        c.cve_cli,
                        c.n_cli,
                        c.rfc,
                        c.cve_zona,
                        v.clave_vendedor,
                        v.nombre AS nombre_vendedor,
                        e.nat,
                        UPPER(REPLACE(e.tipo_proceso, '_', ' ')) AS tipo_proceso,
                        e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio,
                        cc.fecha_emision,
                        cc.fecha_vencimiento,
                        CASE 
                            WHEN cc.saldo_pendiente = 0 THEN 0
                            ELSE GREATEST(0, DATE_PART('day', NOW() - cc.fecha_vencimiento)::int)
                        END AS dias_vencido,
                        cc.monto_total,
                        cc.monto_total - cc.saldo_pendiente AS cobrado,
                        cc.saldo_pendiente,
                        UPPER(cc.estado) AS estado,
                        cc.moneda,
                        f.uuid AS factura_uuid,
                        COALESCE(cob.saldo_favor, 0) AS saldo_favor,
                        COALESCE(dso.dias_cobro, 0)  AS dias_cobro
                    FROM cartera_clientes cc
                    INNER JOIN catclientes   c   ON c.id_cliente    = cc.cliente_id
                    INNER JOIN encabezadomov e   ON e.id_encabezado = cc.encabezado_id
                    LEFT  JOIN factura       f   ON f.encabezado_id = cc.encabezado_id
                    LEFT JOIN vendedores v ON v.clave_vendedor = COALESCE(e.vdr_cpr, c.cve_vdr )
                    LEFT  JOIN (
                        SELECT cliente_id, SUM(saldo_disponible) AS saldo_favor
                        FROM cobros_cliente WHERE tipo_cobro != 'anticipo'
                        GROUP BY cliente_id
                    ) cob ON cob.cliente_id = cc.cliente_id
                    LEFT  JOIN (
                        SELECT acc.cartera_id,
                            AVG(DATE_PART('day', co.fecha_cobro - ca.fecha_emision)) AS dias_cobro
                        FROM aplicaciones_cobro_cliente acc
                        INNER JOIN cobros_cliente   co ON co.id_cobro          = acc.cobro_id
                        INNER JOIN cartera_clientes ca ON ca.id_cartera_cliente = acc.cartera_id 
                        GROUP BY acc.cartera_id
                    ) dso ON dso.cartera_id = cc.id_cartera_cliente
                    WHERE cc.empresa_id = @empresa_id
                        AND cc.cancelada  = false
                        AND e.nat IN ('VNFAC','VIFAC','VSFAC','VINFAC','CXC','RICD','FACLIB')
                        {where}
                    ORDER BY {sortColumn} {sortDir}
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);

                // Parámetros sin paginación para count y KPIs
                var paramsCount = new Dictionary<string, object>(parameters);
                paramsCount.Remove("offset");
                paramsCount.Remove("pageSize");

                string queryTotal = $@"
                    SELECT COUNT(cc.id_cartera_cliente)
                    FROM cartera_clientes cc
                    INNER JOIN catclientes   c ON c.id_cliente    = cc.cliente_id
                    INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id
                    LEFT  JOIN factura       f ON f.encabezado_id = cc.encabezado_id
                    LEFT JOIN vendedores v ON v.clave_vendedor = COALESCE(e.vdr_cpr, c.cve_vdr )
                    WHERE cc.empresa_id = @empresa_id AND cc.cancelada  = false AND e.nat IN ('VNFAC','VIFAC','VSFAC','VINFAC','CXC','RICD','FACLIB') {where}";

                long total = Convert.ToInt64(RunScalar(queryTotal, paramsCount));

                string queryKpis = $@"
                    SELECT
                    	COUNT(*) AS total_facturas,
                    	COALESCE(SUM(cc.saldo_pendiente), 0) AS total_pendiente,
                    	COALESCE(SUM(cc.monto_total - cc.saldo_pendiente), 0) AS total_cobrado,
                    	COUNT(*) FILTER (
                    	WHERE cc.fecha_vencimiento < NOW()
                    	AND cc.saldo_pendiente > 0) AS facturas_vencidas,
                    	COALESCE(SUM(cc.saldo_pendiente)
                            FILTER (WHERE cc.fecha_vencimiento < NOW()
                                AND cc.saldo_pendiente > 0), 0) AS monto_vencido,
                    	COALESCE(AVG(
                        CASE 
                            WHEN cc.saldo_pendiente = 0
                            THEN (cc.fecha_vencimiento - cc.fecha_emision)
                        END
                    	), 0) AS dso
                    FROM cartera_clientes cc
                    INNER JOIN catclientes c ON c.id_cliente = cc.cliente_id
                    INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id
                    LEFT JOIN factura f ON f.encabezado_id = cc.encabezado_id
                    LEFT JOIN vendedores v ON v.clave_vendedor = COALESCE(e.vdr_cpr, c.cve_vdr )
                    WHERE cc.empresa_id = @empresa_id AND cc.cancelada = FALSE AND e.nat IN ('VNFAC','VIFAC','VSFAC','VINFAC','CXC','RICD','FACLIB') {where}";

                var kpisRaw = RunQuery(queryKpis, paramsCount);
                var kpis = kpisRaw?.Count > 0 ? kpisRaw[0] : new Dictionary<string, object>();

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { data, total, kpis });
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", message = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  EXPORTACIÓN — punto de entrada único desde la vista JS
        // ══════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult ExportarReporte(
            string formato,
            string modo,
            string columnas,
            string desde = null,
            string hasta = null,
            string estado = null,
            string ageing = null,
            string tipo_doc = null,
            string vendedor = null,
            string cliente = null,
            string monto_min = null,
            string monto_max = null,
            string zona = null,
            string agrupar = null)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                // Reutilizar BuildReporteWhere empaquetando los parámetros GET en un IFormCollection
                var formData = new Dictionary<string, StringValues>
                {
                    ["desde"] = desde ?? "",
                    ["hasta"] = hasta ?? "",
                    ["estado"] = estado ?? "",
                    ["ageing"] = ageing ?? "",
                    ["tipo_doc"] = tipo_doc ?? "",
                    ["vendedor"] = vendedor ?? "",
                    ["cliente"] = cliente ?? "",
                    ["monto_min"] = monto_min ?? "",
                    ["monto_max"] = monto_max ?? "",
                    ["zona"] = zona ?? "",
                    ["agrupar"] = agrupar ?? ""
                };

                var form = new FormCollection(formData);

                var parameters = new Dictionary<string, object> { { "empresa_id", empresaId } };
                string where = BuildReporteWhere(form, parameters);

                var columnasList = (columnas ?? "")
                    .Split(',')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                // Traer TODOS los datos sin paginar
                string query = $@"
                    SELECT
                        cc.id_cartera_cliente,
                        c.id_cliente,
                        c.cve_cli,
                        c.n_cli,
                        c.rfc,
                        c.cve_zona,
                        v.clave_vendedor,
                        v.nombre AS nombre_vendedor,
                        e.nat,
                        UPPER(REPLACE(e.tipo_proceso, '_', ' ')) AS tipo_proceso,
                        e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio,
                        cc.fecha_emision,
                        cc.fecha_vencimiento,
                        GREATEST(0, DATE_PART('day', NOW() - cc.fecha_vencimiento)::int) AS dias_vencido,
                        cc.monto_total,
                        cc.monto_total - cc.saldo_pendiente AS cobrado,
                        cc.saldo_pendiente,
                        UPPER(cc.estado) AS estado,
                        cc.moneda,
                        f.uuid AS factura_uuid,
                        COALESCE(cob.saldo_favor, 0) AS saldo_favor
                    FROM cartera_clientes cc
                    INNER JOIN catclientes   c   ON c.id_cliente    = cc.cliente_id
                    INNER JOIN encabezadomov e   ON e.id_encabezado = cc.encabezado_id
                    LEFT  JOIN factura       f   ON f.encabezado_id = cc.encabezado_id
                    LEFT JOIN vendedores v ON v.clave_vendedor = COALESCE(e.vdr_cpr, c.cve_vdr )
                    LEFT  JOIN (
                        SELECT cliente_id, SUM(saldo_disponible) AS saldo_favor
                        FROM cobros_cliente WHERE tipo_cobro != 'anticipo'
                        GROUP BY cliente_id
                    ) cob ON cob.cliente_id = cc.cliente_id
                    WHERE cc.empresa_id = @empresa_id
                        AND cc.cancelada  = false
                        AND e.nat IN ('VNFAC','VIFAC','VSFAC','VINFAC','CXC','RICD','FACLIB')
                        {where}
                    ORDER BY c.n_cli ASC, cc.fecha_emision DESC";

                var rows = RunQuery(query, parameters);
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmm");

                return ExportarExcel(rows, columnasList, modo, agrupar, ts);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { message = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PDF  ·  Rotativa → ViewAsPdf("ReporteCarteraPdf", viewModel)
        //
        //  Rotativa toma la vista Razor ~/Views/Carteras/ReporteCarteraPdf.cshtml,
        //  la renderiza con el ViewModel y la convierte a PDF usando wkhtmltopdf.
        //  Todo el diseño va en esa vista; aquí solo preparamos los datos.
        // ══════════════════════════════════════════════════════════════════

        [HttpGet]
        public JsonResult ExportarPdf(
            string formato,
            string modo,
            string columnas,
            string desde = null,
            string hasta = null,
            string estado = null,
            string ageing = null,
            string tipo_doc = null,
            string vendedor = null,
            string cliente = null,
            string monto_min = null,
            string monto_max = null,
            string zona = null,
            string agrupar = null)
        {
            int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

            // Reutilizar BuildReporteWhere empaquetando los parámetros GET en un IFormCollection
            var formData = new Dictionary<string, StringValues>
            {
                ["desde"] = desde ?? "",
                ["hasta"] = hasta ?? "",
                ["estado"] = estado ?? "",
                ["ageing"] = ageing ?? "",
                ["tipo_doc"] = tipo_doc ?? "",
                ["vendedor"] = vendedor ?? "",
                ["cliente"] = cliente ?? "",
                ["monto_min"] = monto_min ?? "",
                ["monto_max"] = monto_max ?? "",
                ["zona"] = zona ?? "",
                ["agrupar"] = agrupar ?? ""
            };

            var form = new FormCollection(formData);

            var parameters = new Dictionary<string, object> { { "empresa_id", empresaId } };
            string where = BuildReporteWhere(form, parameters);

            var columnasList = (columnas ?? "")
                .Split(',')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            // Traer TODOS los datos sin paginar
            string query = $@"
                    SELECT
                        cc.id_cartera_cliente,
                        c.id_cliente,
                        c.cve_cli,
                        c.n_cli,
                        c.rfc,
                        c.cve_zona,
                        v.clave_vendedor,
                        v.nombre AS nombre_vendedor,
                        e.nat,
                        UPPER(REPLACE(e.tipo_proceso, '_', ' ')) AS tipo_proceso,
                        e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio,
                        cc.fecha_emision,
                        cc.fecha_vencimiento,
                        GREATEST(0, DATE_PART('day', NOW() - cc.fecha_vencimiento)::int) AS dias_vencido,
                        cc.monto_total,
                        cc.monto_total - cc.saldo_pendiente AS cobrado,
                        cc.saldo_pendiente,
                        UPPER(cc.estado) AS estado,
                        cc.moneda,
                        f.uuid AS factura_uuid,
                        COALESCE(cob.saldo_favor, 0) AS saldo_favor
                    FROM cartera_clientes cc
                    INNER JOIN catclientes   c   ON c.id_cliente    = cc.cliente_id
                    INNER JOIN encabezadomov e   ON e.id_encabezado = cc.encabezado_id
                    LEFT  JOIN factura       f   ON f.encabezado_id = cc.encabezado_id
                    LEFT JOIN vendedores v ON v.clave_vendedor = COALESCE(e.vdr_cpr, c.cve_vdr )
                    LEFT  JOIN (
                        SELECT cliente_id, SUM(saldo_disponible) AS saldo_favor
                        FROM cobros_cliente WHERE tipo_cobro = 'anticipo'
                        GROUP BY cliente_id
                    ) cob ON cob.cliente_id = cc.cliente_id
                    WHERE cc.empresa_id = @empresa_id
                        AND cc.cancelada  = false
                        AND e.nat IN ('VNFAC','VIFAC','VSFAC','VINFAC','CXC','RICD','FACLIB')
                        {where}
                    ORDER BY c.n_cli ASC, cc.fecha_emision DESC";

            var rows = RunQuery(query, parameters);

            return Json(rows);
        }

        // ══════════════════════════════════════════════════════════════════
        //  EXCEL  ·  ClosedXML
        // ══════════════════════════════════════════════════════════════════

        private FileResult ExportarExcel(
            List<Dictionary<string, object>> rows,
            List<string> columnas,
            string modo,
            string agrupar,
            string timestamp)
        {
            using (var wb = new XLWorkbook())
            {
                var colorHeader = XLColor.FromHtml("#1e3a5f");
                var colorAlterA = XLColor.FromHtml("#f8fafc");
                var colorAlterB = XLColor.White;
                var colorBorder = XLColor.FromHtml("#e2e8f0");
                var colorTotal = XLColor.FromHtml("#fef3c7");
                var colorKpi = XLColor.FromHtml("#eff6ff");

                var colDefs = GetColumnDefs(columnas);

                if (modo == "clientes")
                {
                    // Pestaña de resumen primero
                    BuildExcelResumenSheet(wb.Worksheets.Add("Resumen"), rows,
                        colorHeader, colorAlterA, colorAlterB, colorBorder, colorTotal, colorKpi);

                    // Una pestaña por cliente
                    foreach (var grupo in rows.GroupBy(r => r.GetValueOrDefault("id_cliente")?.ToString()))
                    {
                        var nombre = grupo.First().GetValueOrDefault("n_cli")?.ToString() ?? "Cliente";
                        BuildExcelDataSheet(
                            wb.Worksheets.Add(SanitizeSheetName(nombre, 28)),
                            grupo.ToList(), colDefs, nombre,
                            colorHeader, colorAlterA, colorAlterB, colorBorder, colorTotal, colorKpi);
                    }
                }
                else
                {
                    BuildExcelDataSheet(wb.Worksheets.Add("Cartera"), rows, colDefs,
                        "Reporte de Cartera",
                        colorHeader, colorAlterA, colorAlterB, colorBorder, colorTotal, colorKpi);

                    if (!string.IsNullOrEmpty(agrupar))
                        BuildExcelPivotSheet(wb.Worksheets.Add($"Por {agrupar}"), rows, agrupar,
                            colorHeader, colorAlterA, colorAlterB, colorBorder, colorTotal);
                }

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return File(ms.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"reporte_cartera_{timestamp}.xlsx");
                }
            }
        }

        // ── Hoja de datos principal ──────────────────────────────────────

        private void BuildExcelDataSheet(
            IXLWorksheet ws,
            List<Dictionary<string, object>> rows,
            List<ReporteColumnDef> colDefs,
            string titulo,
            XLColor colorHeader, XLColor colorAlterA, XLColor colorAlterB,
            XLColor colorBorder, XLColor colorTotal, XLColor colorKpi)
        {
            int row = 1;

            // Título
            ws.Cell(row, 1).Value = titulo;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 14;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a5f");
            ws.Range(row, 1, row, colDefs.Count).Merge();
            row++;

            ws.Cell(row, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}  —  {rows.Count} registros";
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");
            ws.Cell(row, 1).Style.Font.FontSize = 9;
            ws.Range(row, 1, row, colDefs.Count).Merge();
            row += 2;

            // KPIs en las primeras celdas
            decimal totalMonto = rows.Sum(r => ToDecimal(r, "monto_total"));
            decimal totalCobrado = rows.Sum(r => ToDecimal(r, "cobrado"));
            decimal totalPendiente = rows.Sum(r => ToDecimal(r, "saldo_pendiente"));
            decimal pctCobro = totalMonto > 0 ? Math.Round(totalCobrado / totalMonto * 100, 1) : 0m;
            int vencidas = rows.Count(r =>
                ToDecimal(r, "saldo_pendiente") > 0 &&
                r.GetValueOrDefault("fecha_vencimiento") is DateTime fv && fv < DateTime.Now);

            string[] kpiLabels = { "Facturas", "Monto Total", "Cobrado", "Pendiente", "% Cobro", "Vencidas" };
            string[] kpiValues =
            {
                rows.Count.ToString(),
                totalMonto.ToString("C2"),
                totalCobrado.ToString("C2"),
                totalPendiente.ToString("C2"),
                $"{pctCobro}%",
                vencidas.ToString()
            };

            for (int k = 0; k < kpiLabels.Length; k++)
            {
                ws.Cell(row, k + 1).Value = kpiLabels[k];
                ws.Cell(row, k + 1).Style.Font.Bold = true;
                ws.Cell(row, k + 1).Style.Font.FontSize = 8;
                ws.Cell(row, k + 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");
                ws.Cell(row, k + 1).Style.Fill.BackgroundColor = colorKpi;
                ws.Cell(row, k + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row++;

            for (int k = 0; k < kpiValues.Length; k++)
            {
                ws.Cell(row, k + 1).Value = kpiValues[k];
                ws.Cell(row, k + 1).Style.Font.Bold = true;
                ws.Cell(row, k + 1).Style.Font.FontSize = 11;
                ws.Cell(row, k + 1).Style.Fill.BackgroundColor = colorKpi;
                ws.Cell(row, k + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row += 2;

            // Encabezados de columna
            int headerRow = row;
            for (int col = 0; col < colDefs.Count; col++)
            {
                var cell = ws.Cell(row, col + 1);
                cell.Value = colDefs[col].Title;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.FontSize = 9;
                cell.Style.Fill.BackgroundColor = colorHeader;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = colorBorder;
            }
            row++;

            // Datos
            int dataStartRow = row;
            int rowIdx = 0;
            foreach (var r in rows)
            {
                var fill = rowIdx % 2 == 0 ? colorAlterA : colorAlterB;

                for (int col = 0; col < colDefs.Count; col++)
                {
                    var cell = ws.Cell(row, col + 1);
                    var cd = colDefs[col];
                    var valor = cd.DataField != null ? r.GetValueOrDefault(cd.DataField) : null;

                    switch (cd.DataType)
                    {
                        case "currency":
                            cell.Value = ToDecimal(r, cd.DataField);
                            cell.Style.NumberFormat.Format = "$#,##0.00";
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            break;
                        case "date":
                            if (valor is DateTime dt) { cell.Value = dt; cell.Style.NumberFormat.Format = "dd/MM/yyyy"; }
                            else cell.Value = valor?.ToString() ?? "";
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            break;
                        case "int":
                            cell.Value = valor != null ? Convert.ToInt32(valor) : 0;
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            break;
                        case "pct":
                            decimal t = ToDecimal(r, "monto_total");
                            decimal c = ToDecimal(r, "cobrado");
                            cell.Value = t > 0 ? Math.Round(c / t * 100, 1) : 0m;
                            cell.Style.NumberFormat.Format = "0.0\"%\"";
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            break;
                        default:
                            cell.Value = valor?.ToString() ?? "";
                            break;
                    }

                    cell.Style.Fill.BackgroundColor = fill;
                    cell.Style.Font.FontSize = 9;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = colorBorder;
                }
                row++;
                rowIdx++;
            }

            // Fila de totales con fórmulas de Excel (no hardcodeadas)
            if (rows.Any())
            {
                for (int col = 0; col < colDefs.Count; col++)
                {
                    var cell = ws.Cell(row, col + 1);
                    var cd = colDefs[col];

                    cell.Style.Fill.BackgroundColor = colorTotal;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 9;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#d97706");

                    if (col == 0)
                        cell.Value = "TOTALES";
                    else if (cd.DataType == "currency")
                    {
                        string colLetter = ws.Column(col + 1).ColumnLetter();
                        cell.FormulaA1 = $"=SUM({colLetter}{dataStartRow}:{colLetter}{row - 1})";
                        cell.Style.NumberFormat.Format = "$#,##0.00";
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    }
                }
            }

            // Anchos de columna
            for (int col = 0; col < colDefs.Count; col++)
                ws.Column(col + 1).Width = colDefs[col].Width;

            ws.SheetView.FreezeRows(headerRow);
            if (rows.Any())
                ws.Range(headerRow, 1, row, colDefs.Count).SetAutoFilter();
        }

        // ── Hoja de resumen por cliente ──────────────────────────────────

        private void BuildExcelResumenSheet(
            IXLWorksheet ws,
            List<Dictionary<string, object>> rows,
            XLColor colorHeader, XLColor colorAlterA, XLColor colorAlterB,
            XLColor colorBorder, XLColor colorTotal, XLColor colorKpi)
        {
            int row = 1;

            ws.Cell(row, 1).Value = "RESUMEN POR CLIENTE";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 14;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a5f");
            ws.Range(row, 1, row, 8).Merge();
            row += 2;

            string[] hdrs = { "Cliente", "Código", "RFC", "Facturas", "Monto Total", "Cobrado", "Pendiente", "% Cobro" };
            for (int i = 0; i < hdrs.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = hdrs[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = colorHeader;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Font.FontSize = 9;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = colorBorder;
            }

            int headerRow = row;
            int dataStart = ++row;
            int idx = 0;

            foreach (var grupo in rows.GroupBy(r => r.GetValueOrDefault("id_cliente")?.ToString()))
            {
                var g = grupo.ToList();
                var total = g.Sum(r => ToDecimal(r, "monto_total"));
                var cobrado = g.Sum(r => ToDecimal(r, "cobrado"));
                var pend = g.Sum(r => ToDecimal(r, "saldo_pendiente"));
                var pct = total > 0 ? Math.Round(cobrado / total * 100, 1) : 0m;
                var fill = idx % 2 == 0 ? colorAlterA : colorAlterB;

                object[] vals =
                {
                    g.First().GetValueOrDefault("n_cli")?.ToString()   ?? "-",
                    g.First().GetValueOrDefault("cve_cli")?.ToString() ?? "-",
                    g.First().GetValueOrDefault("rfc")?.ToString()     ?? "-",
                    g.Count, total, cobrado, pend, pct
                };

                for (int i = 0; i < vals.Length; i++)
                {
                    var cell = ws.Cell(row, i + 1);
                    cell.Style.Fill.BackgroundColor = fill;
                    cell.Style.Font.FontSize = 9;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = colorBorder;

                    if (i == 4 || i == 5 || i == 6)
                    { cell.Value = (decimal)vals[i]; cell.Style.NumberFormat.Format = "$#,##0.00"; cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right; }
                    else if (i == 7)
                    { cell.Value = (decimal)vals[i]; cell.Style.NumberFormat.Format = "0.0\"%\""; cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; }
                    else if (i == 3)
                    { cell.Value = (int)vals[i]; cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; }
                    else
                        cell.Value = vals[i]?.ToString();
                }
                row++;
                idx++;
            }

            // Totales con fórmulas
            if (idx > 0)
            {
                ws.Cell(row, 1).Value = "TOTALES";
                ws.Cell(row, 1).Style.Font.Bold = true;

                foreach (var (ci, cl) in new[] { (5, "E"), (6, "F"), (7, "G") })
                {
                    ws.Cell(row, ci).FormulaA1 = $"=SUM({cl}{dataStart}:{cl}{row - 1})";
                    ws.Cell(row, ci).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(row, ci).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    ws.Cell(row, ci).Style.Font.Bold = true;
                }

                for (int i = 1; i <= 8; i++)
                {
                    ws.Cell(row, i).Style.Fill.BackgroundColor = colorTotal;
                    ws.Cell(row, i).Style.Font.FontSize = 9;
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#d97706");
                }
            }

            ws.Column(1).Width = 36; ws.Column(2).Width = 12; ws.Column(3).Width = 15; ws.Column(4).Width = 10;
            for (int i = 5; i <= 8; i++) ws.Column(i).Width = 16;
            ws.SheetView.FreezeRows(headerRow);
            if (idx > 0) ws.Range(headerRow, 1, row, 8).SetAutoFilter();
        }

        // ── Hoja pivot por agrupación ────────────────────────────────────

        private void BuildExcelPivotSheet(
            IXLWorksheet ws,
            List<Dictionary<string, object>> rows,
            string agrupar,
            XLColor colorHeader, XLColor colorAlterA, XLColor colorAlterB,
            XLColor colorBorder, XLColor colorTotal)
        {
            string groupField;
            switch (agrupar)
            {
                case "cliente":
                    groupField = "n_cli";
                    break;
                case "vendedor":
                    groupField = "clave_vendedor";
                    break;
                case "zona":
                    groupField = "cve_zona";
                    break;
                case "estado":
                    groupField = "estado";
                    break;
                case "tipo_doc":
                    groupField = "nat";
                    break;
                default:
                    groupField = "n_cli";
                    break;
            }

            int row = 1;
            ws.Cell(row, 1).Value = $"Agrupado por: {agrupar.ToUpper()}";
            ws.Cell(row, 1).Style.Font.Bold = true; ws.Cell(row, 1).Style.Font.FontSize = 12;
            ws.Range(row, 1, row, 6).Merge();
            row += 2;

            string[] hdrs = { char.ToUpper(agrupar[0]) + agrupar.Substring(1), "Facturas", "Monto Total", "Cobrado", "Pendiente", "% Cobro" };
            for (int i = 0; i < hdrs.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = hdrs[i];
                cell.Style.Font.Bold = true; cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = colorHeader;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Font.FontSize = 9;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = colorBorder;
            }

            int headerRow = row;
            int dataStart = ++row;
            int idx = 0;

            foreach (var grupo in rows.GroupBy(r => r.GetValueOrDefault(groupField)?.ToString() ?? "—"))
            {
                var g = grupo.ToList();
                var total = g.Sum(r => ToDecimal(r, "monto_total"));
                var cobrado = g.Sum(r => ToDecimal(r, "cobrado"));
                var pend = g.Sum(r => ToDecimal(r, "saldo_pendiente"));
                var pct = total > 0 ? Math.Round(cobrado / total * 100, 1) : 0m;
                var fill = idx % 2 == 0 ? colorAlterA : colorAlterB;

                ws.Cell(row, 1).Value = grupo.Key;
                ws.Cell(row, 2).Value = g.Count; ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = total; ws.Cell(row, 3).Style.NumberFormat.Format = "$#,##0.00"; ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(row, 4).Value = cobrado; ws.Cell(row, 4).Style.NumberFormat.Format = "$#,##0.00"; ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(row, 5).Value = pend; ws.Cell(row, 5).Style.NumberFormat.Format = "$#,##0.00"; ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(row, 6).Value = pct; ws.Cell(row, 6).Style.NumberFormat.Format = "0.0\"%\""; ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                for (int i = 1; i <= 6; i++)
                {
                    ws.Cell(row, i).Style.Fill.BackgroundColor = fill;
                    ws.Cell(row, i).Style.Font.FontSize = 9;
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = colorBorder;
                }
                row++; idx++;
            }

            if (idx > 0)
            {
                ws.Cell(row, 1).Value = "TOTALES"; ws.Cell(row, 1).Style.Font.Bold = true;
                foreach (var (ci, cl) in new[] { (3, "C"), (4, "D"), (5, "E") })
                {
                    ws.Cell(row, ci).FormulaA1 = $"=SUM({cl}{dataStart}:{cl}{row - 1})";
                    ws.Cell(row, ci).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(row, ci).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    ws.Cell(row, ci).Style.Font.Bold = true;
                }
                for (int i = 1; i <= 6; i++)
                {
                    ws.Cell(row, i).Style.Fill.BackgroundColor = colorTotal;
                    ws.Cell(row, i).Style.Font.FontSize = 9;
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#d97706");
                }
            }

            ws.Column(1).Width = 30;
            for (int i = 2; i <= 6; i++) ws.Column(i).Width = 16;
            ws.SheetView.FreezeRows(headerRow);
            if (idx > 0) ws.Range(headerRow, 1, row, 6).SetAutoFilter();
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        private string BuildReporteWhere(IFormCollection fc, Dictionary<string, object> parameters)
        {
            string where = "";

            if (!string.IsNullOrWhiteSpace(fc["desde"].ToString()))
            { parameters["fecha_desde"] = DateTime.Parse(fc["desde"].ToString()); where += " AND cc.fecha_emision >= @fecha_desde "; }

            if (!string.IsNullOrWhiteSpace(fc["hasta"].ToString()))
            { parameters["fecha_hasta"] = DateTime.Parse(fc["hasta"].ToString()).AddDays(1).AddSeconds(-1); where += " AND cc.fecha_emision <= @fecha_hasta "; }

            if (!string.IsNullOrWhiteSpace(fc["estado"].ToString()))
            { parameters["estado"] = fc["estado"].ToString(); where += " AND cc.estado = @estado "; }

            if (!string.IsNullOrWhiteSpace(fc["tipo_doc"].ToString()))
            { parameters["nat"] = fc["tipo_doc"].ToString(); where += " AND e.nat = @nat "; }

            if (!string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
            { parameters["cve_vdr"] = fc["vendedor"].ToString(); where += " AND v.clave_vendedor = @cve_vdr "; }

            if (!string.IsNullOrWhiteSpace(fc["sucursal"].ToString()))
            { parameters["suc"] = Convert.ToInt32(fc["sucursal"].ToString()); where += " AND e.suc = @suc"; }

            if (!string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
            { parameters["cliente_nombre"] = $"%{fc["cliente"].ToString()}%"; where += " AND (c.n_cli ILIKE @cliente_nombre OR c.cve_cli ILIKE @cliente_nombre) "; }

            if (decimal.TryParse(fc["monto_min"].ToString(), out decimal montoMin))
            { parameters["monto_min"] = montoMin; where += " AND cc.saldo_pendiente >= @monto_min "; }

            if (decimal.TryParse(fc["monto_max"].ToString(), out decimal montoMax))
            { parameters["monto_max"] = montoMax; where += " AND cc.saldo_pendiente <= @monto_max "; }

            if (!string.IsNullOrWhiteSpace(fc["ageing"].ToString()))
                switch (fc["ageing"].ToString())
                {
                    case "0-30":
                        where += " AND DATE_PART('day', NOW() - cc.fecha_vencimiento) BETWEEN 0  AND 30 ";
                        break;
                    case "31-60":
                        where += " AND DATE_PART('day', NOW() - cc.fecha_vencimiento) BETWEEN 31 AND 60 ";
                        break;
                    case "61-90":
                        where += " AND DATE_PART('day', NOW() - cc.fecha_vencimiento) BETWEEN 61 AND 90 ";
                        break;
                    case "90+":
                        where += " AND DATE_PART('day', NOW() - cc.fecha_vencimiento) > 90 ";
                        break;
                    default:
                        where += "";
                        break;
                }
            ;

            return where;
        }

        private static decimal ToDecimal(Dictionary<string, object> row, string key)
        {
            if (row != null && row.TryGetValue(key, out var val) && val != null)
                try { return Convert.ToDecimal(val); } catch { }
            return 0m;
        }

        private static string SanitizeSheetName(string name, int maxLen)
        {
            foreach (var c in new[] { '/', '\\', '*', '[', ']', ':', '?' })
                name = name.Replace(c, '-');
            return name.Length > maxLen ? name.Substring(0, maxLen).TrimEnd() : name;
        }

        // ── Catálogo de columnas (compartido entre Excel y PDF) ──────────

        public class ReporteColumnDef
        {
            public string Title { get; set; }
            public string DataField { get; set; }
            public string DataType { get; set; } = "text";   // text | currency | date | int | pct
            public double Width { get; set; } = 16;       // ancho Excel
        }

        private List<ReporteColumnDef> GetColumnDefs(List<string> keys)
        {
            var catalog = new Dictionary<string, ReporteColumnDef>
            {
                ["cliente"] = new ReporteColumnDef { Title = "Cliente", DataField = "n_cli", DataType = "text", Width = 32 },
                ["cve_cli"] = new ReporteColumnDef { Title = "Código", DataField = "cve_cli", DataType = "text", Width = 12 },
                ["rfc"] = new ReporteColumnDef { Title = "RFC", DataField = "rfc", DataType = "text", Width = 15 },
                ["zona"] = new ReporteColumnDef { Title = "Zona", DataField = "cve_zona", DataType = "text", Width = 12 },
                ["vendedor"] = new ReporteColumnDef { Title = "Vendedor", DataField = "vendedor", DataType = "text", Width = 14 },
                ["folio"] = new ReporteColumnDef { Title = "Folio", DataField = "folio", DataType = "text", Width = 16 },
                ["tipo_doc"] = new ReporteColumnDef { Title = "Tipo Doc", DataField = "nat", DataType = "text", Width = 11 },
                ["tipo_proceso"] = new ReporteColumnDef { Title = "Tipo Proceso", DataField = "tipo_proceso", DataType = "text", Width = 16 },
                ["fecha_emision"] = new ReporteColumnDef { Title = "Fecha Emisión", DataField = "fecha_emision", DataType = "date", Width = 14 },
                ["fecha_venc"] = new ReporteColumnDef { Title = "Fecha Vencim.", DataField = "fecha_vencimiento", DataType = "date", Width = 14 },
                ["dias_vencido"] = new ReporteColumnDef { Title = "Días Vencido", DataField = "dias_vencido", DataType = "int", Width = 12 },
                ["monto_total"] = new ReporteColumnDef { Title = "Monto Total", DataField = "monto_total", DataType = "currency", Width = 16 },
                ["cobrado"] = new ReporteColumnDef { Title = "Cobrado", DataField = "cobrado", DataType = "currency", Width = 16 },
                ["saldo_pend"] = new ReporteColumnDef { Title = "Saldo Pendiente", DataField = "saldo_pendiente", DataType = "currency", Width = 16 },
                ["pct_cobro"] = new ReporteColumnDef { Title = "% Cobro", DataField = null, DataType = "pct", Width = 10 },
                ["estado"] = new ReporteColumnDef { Title = "Estado", DataField = "estado", DataType = "text", Width = 12 },
                ["saldo_favor"] = new ReporteColumnDef { Title = "Saldo a Favor", DataField = "saldo_favor", DataType = "currency", Width = 16 },
                ["moneda"] = new ReporteColumnDef { Title = "Moneda", DataField = "moneda", DataType = "text", Width = 10 },
                ["dias_cobro"] = new ReporteColumnDef { Title = "Días Prom. Cobro", DataField = "dias_cobro", DataType = "int", Width = 14 },
                ["uuid"] = new ReporteColumnDef { Title = "UUID Factura", DataField = "factura_uuid", DataType = "text", Width = 38 },
            };

            if (!keys.Any())
                keys = new List<string>
                {
                    "cliente", "folio", "tipo_doc", "fecha_emision", "fecha_venc",
                    "monto_total", "cobrado", "saldo_pend", "pct_cobro", "estado", "dias_vencido"
                };

            return keys.Where(k => catalog.ContainsKey(k)).Select(k => catalog[k]).ToList();
        }
        public JsonResult GetDatosEmpresa()
        {
            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            return Json(datosEmpresa);
        }
        #endregion

        #region Funciones de graficas
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetGraficasCartera(IFormCollection fc)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                var parameters = new Dictionary<string, object> { { "empresa_id", empresaId } };
                string where = BuildReporteWhere(fc, parameters);

                string baseJoin = $@"
                    FROM cartera_clientes cc
                    INNER JOIN catclientes   c ON c.id_cliente    = cc.cliente_id
                    INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id
                    LEFT  JOIN vendedores    v ON v.clave_vendedor = COALESCE(e.vdr_cpr, c.cve_vdr)
                    WHERE cc.empresa_id = @empresa_id
                        AND cc.cancelada  = false
                        AND e.nat IN ('VNFAC','VIFAC','VSFAC','VINFAC','CXC','RICD','FACLIB')
                        {where}";

                // ── 1. Top vendedores por saldo vencido ──────────────────────────
                string qVendedores = $@"
                    SELECT
                        COALESCE(v.nombre, c.cve_vdr, 'Sin vendedor') AS vendedor,
                        COUNT(*) FILTER (WHERE cc.fecha_vencimiento < NOW() AND cc.saldo_pendiente > 0) AS facturas_vencidas,
                        COUNT(*) FILTER (WHERE cc.fecha_vencimiento > NOW() AND cc.saldo_pendiente > 0) AS facturas_vigentes,
                        COUNT(*) AS facturas_totales,
                        COALESCE(SUM(cc.saldo_pendiente) FILTER (WHERE cc.fecha_vencimiento < NOW() AND cc.saldo_pendiente > 0), 0) AS monto_vencido,
                        COALESCE(SUM(cc.saldo_pendiente) FILTER (WHERE cc.fecha_vencimiento > NOW() AND cc.saldo_pendiente > 0), 0) AS monto_vigente,
                        COALESCE(SUM(cc.monto_total), 0) AS monto_total
                    {baseJoin}
                    GROUP BY COALESCE(v.nombre, c.cve_vdr, 'Sin vendedor')
                    HAVING SUM(cc.saldo_pendiente) > 0
                    ORDER BY monto_vencido DESC
                    LIMIT 10";

                // ── 2. Top clientes por saldo pendiente ──────────────────────────
                string qClientes = $@"
                    SELECT
	                    c.n_cli AS cliente,
	                    c.cve_cli,
	                    COUNT(*) AS facturas,
	                    COUNT(*) FILTER (WHERE cc.fecha_vencimiento < NOW() AND cc.saldo_pendiente > 0) AS facturas_vencidas,
	                    COUNT(*) FILTER (WHERE cc.fecha_vencimiento > NOW() AND cc.saldo_pendiente > 0) AS facturas_vigentes,
	                    COALESCE(SUM(cc.saldo_pendiente) FILTER (WHERE cc.fecha_vencimiento < NOW()), 0) AS monto_vencido,
	                    COALESCE(SUM(cc.saldo_pendiente) FILTER (WHERE cc.fecha_vencimiento > NOW()), 0) AS monto_vigente,
	                    COALESCE(SUM(cc.saldo_pendiente), 0) AS saldo_total,
	                    COALESCE(MAX(GREATEST(0, DATE_PART('day', NOW() - cc.fecha_vencimiento)::int)) FILTER (WHERE cc.saldo_pendiente > 0), 0) AS max_dias_vencido
                    {baseJoin}
                        AND cc.saldo_pendiente > 0
                    GROUP BY c.id_cliente, c.n_cli, c.cve_cli
                    LIMIT 10";

                // ── 3. Distribución ageing ────────────────────────────────────────
                string qAgeing = $@"
                    SELECT
                        CASE
                            WHEN cc.saldo_pendiente <= 0                                         THEN 'Cobrado'
                            WHEN cc.fecha_vencimiento >= NOW()                                   THEN 'Por vencer'
                            WHEN DATE_PART('day', NOW()-cc.fecha_vencimiento) BETWEEN 1  AND 30  THEN '1–30 días'
                            WHEN DATE_PART('day', NOW()-cc.fecha_vencimiento) BETWEEN 31 AND 60  THEN '31–60 días'
                            WHEN DATE_PART('day', NOW()-cc.fecha_vencimiento) BETWEEN 61 AND 90  THEN '61–90 días'
                            ELSE '+90 días'
                        END AS tramo,
                        COUNT(*)  AS facturas,
                        COALESCE(SUM(cc.saldo_pendiente), 0) AS monto
                    {baseJoin}
                    GROUP BY 1
                    ORDER BY MIN(cc.fecha_vencimiento)";

                // ── 4. Tendencia mensual (últimos 12 meses) ───────────────────────
                string qTendencia = $@"
                    SELECT
                        TO_CHAR(DATE_TRUNC('month', cc.fecha_emision), 'Mon YY') AS mes,
                        DATE_TRUNC('month', cc.fecha_emision) AS mes_orden,
                        COALESCE(SUM(cc.monto_total), 0)      AS emitido,
                        COALESCE(SUM(cc.monto_total - cc.saldo_pendiente), 0) AS cobrado
                    {baseJoin}
                        AND cc.fecha_emision >= DATE_TRUNC('month', NOW()) - INTERVAL '11 months'
                    GROUP BY 1, 2
                    ORDER BY 2";

                        // ── 5. Cartera por estado ─────────────────────────────────────────
                        string qEstados = $@"
                    SELECT
                        UPPER(cc.estado) AS estado,
                        COUNT(*)  AS facturas,
                        COALESCE(SUM(cc.saldo_pendiente), 0) AS monto
                    {baseJoin}
                    GROUP BY cc.estado
                    ORDER BY monto DESC";

                var vendedores = RunQuery(qVendedores, parameters);
                var clientes = RunQuery(qClientes, parameters);
                var ageing = RunQuery(qAgeing, parameters);
                var tendencia = RunQuery(qTendencia, parameters);
                var estados = RunQuery(qEstados, parameters);

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { vendedores, clientes, ageing, tendencia, estados });
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", message = ex.Message });
            }
        }
        #endregion
    }

    // ══════════════════════════════════════════════════════════════════════
    //  VIEWMODELS  — los recibe la vista Razor ReporteCarteraPdf.cshtml
    // ══════════════════════════════════════════════════════════════════════

    public class ReporteSeccion
    {
        public string Titulo { get; set; }
        public List<Dictionary<string, object>> Rows { get; set; }
        public ReporteKpis Kpis { get; set; }
    }

    public class ReporteKpis
    {
        public int TotalFacturas { get; set; }
        public decimal TotalMonto { get; set; }
        public decimal TotalCobrado { get; set; }
        public decimal TotalPendiente { get; set; }
        public int FacturasVencidas { get; set; }
        public decimal MontoVencido { get; set; }
        public decimal PctCobro => TotalMonto > 0
            ? Math.Round(TotalCobrado / TotalMonto * 100, 1)
            : 0m;
    }
}