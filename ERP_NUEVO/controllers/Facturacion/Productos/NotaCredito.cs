using BOS_ERP.Models;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Npgsql;
using Rotativa.AspNetCore;
using System.Data;
using System.Text;

namespace BOS_ERP.Controllers.Facturacion.Productos
{
    public class NotaCreditoController : Utilities
    {
        private readonly IConfiguration _configuration;
        private BOS_ERP.Services.EmailSender _emailSender;
        public readonly IWebHostEnvironment _env;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly XmlBuilderService _xmlBuilderService;
        private readonly IFacturaRepository? _facturaRepository;
        private readonly ITimbradoWorkflow? _timbradoWorkflow;

        public NotaCreditoController(
            IConfiguration configuration,
            BOS_ERP.Services.EmailSender emailSender,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            XmlBuilderService xmlBuilder,
            IFacturaRepository? facturaRepository = null,
            ITimbradoWorkflow? timbradoWorkflow = null)
        {
            _configuration = configuration;
            _emailSender = emailSender;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _xmlBuilderService = xmlBuilder;
            _facturaRepository = facturaRepository;
            _timbradoWorkflow = timbradoWorkflow;
        }

        private IFacturaRepository FacturaRepository => _facturaRepository
            ?? HttpContext.RequestServices.GetRequiredService<IFacturaRepository>();
        private ITimbradoWorkflow TimbradoWorkflow => _timbradoWorkflow
            ?? HttpContext.RequestServices.GetRequiredService<ITimbradoWorkflow>();

        // ──────────────────────────────────────────────────────────────
        // Helpers de rutas — mismas rutas físicas que Venta/VentaIN/Predial
        // ──────────────────────────────────────────────────────────────
        private string FacturacionPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion");
        private string XmlTimbradosPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados");
        private string QrCodesPath => Path.Combine(_env.WebRootPath, "content", "qrcodes");
        private string ContentPdfPath => Path.Combine(_env.WebRootPath, "content", "pdf");
        private string FacturasPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas");

        // ══════════════════════════════════════════════════════════════════════
        //  RESULTADO DEL TIMBRADO
        // ══════════════════════════════════════════════════════════════════════

        //public class TimbradoResult
        //{
        //    public bool Success { get; set; }
        //    public string Message { get; set; }
        //    public string UUID { get; set; }
        //    public string FolioNC { get; set; }
        //}

        // ══════════════════════════════════════════════════════════════════════
        //  GENERACIÓN DEL XML CFDI 4.0  —  TipoDeComprobante = "E"
        //  Estructura idéntica a FacturacionVentaController.GenerarXml(),
        //  pero con "E" en TipoDeComprobante y el nodo CfdiRelacionados con "01".
        // ══════════════════════════════════════════════════════════════════════

        // nota de credito
        public TimbradoResult GenerarXml(Factura nc)
        {
            try
            {
                XmlResult xml = _xmlBuilderService.GenerateXmlAsync(nc, FacturacionPath);

                return Timbrar(xml.factura, xml.uuid);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "NotaCredito/GenerarXml");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al generar XML de NC: " + ex.Message
                };
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TIMBRADO CENTRALIZADO
        // ══════════════════════════════════════════════════════════════════════

        public TimbradoResult Timbrar(Factura nc, string tempId)
        {
            try
            {
                var resultado = TimbradoWorkflow.EjecutarAsync(
                    nc, Path.Combine(FacturacionPath, $"{tempId}.xml"),
                    XmlTimbradosPath, QrCodesPath, "/content/qrcodes").GetAwaiter().GetResult();
                resultado.FolioNC = nc.FolioCorto;
                if (resultado.Success)
                {
                    FacturaRepository.GuardarNotaCredito(nc);
                    GenerarPdfNC(nc).GetAwaiter().GetResult();
                    resultado.Message = "Nota de Cr\u00e9dito timbrada correctamente";
                }
                return resultado;

            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "NotaCredito/Timbrar");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error en el servicio de timbrado: " + ex.Message
                };
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PERSISTENCIA EN BASE DE DATOS
        // ══════════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════════
        //  RENDER DE VISTA A STRING — reemplaza _emailSender.RenderViewToStringAsync
        //  Usa IRazorViewEngine, igual que FacturacionVentaController
        // ══════════════════════════════════════════════════════════════════════

        private async Task<string> RenderViewToStringAsync(string viewName, object model)
        {
            var actionContext = new ActionContext(HttpContext, RouteData, ControllerContext.ActionDescriptor);

            var viewResult = _viewEngine.FindView(actionContext, viewName, isMainPage: false);

            if (viewResult?.View == null)
                throw new InvalidOperationException($"No se encontró la vista: {viewName}");

            using var sw = new StringWriter();

            var viewDataDictionary = new ViewDataDictionary(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
            {
                Model = model
            };

            var tempData = new TempDataDictionary(HttpContext, _tempDataProvider);

            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewDataDictionary,
                tempData,
                sw,
                new HtmlHelperOptions()
            );

            await viewResult.View.RenderAsync(viewContext);

            return sw.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PDF — misma estructura que GenerarFactura() en FacturacionVentaController
        //  Necesitas crear la vista: Views/NotaCredito/NotaCreditoPdf.cshtml
        //  Puedes copiar FacturaPdf.cshtml y ajustar el título y el encabezado.
        // ══════════════════════════════════════════════════════════════════════

        private async Task GenerarPdfNC(Factura nc)
        {
            if (string.IsNullOrWhiteSpace(nc.UUID)) return;

            string contentPdfPath = ContentPdfPath;
            string facturasPath = FacturasPath;
            Directory.CreateDirectory(contentPdfPath);
            Directory.CreateDirectory(facturasPath);

            string headerHtml = await RenderViewToStringAsync("Header", nc);
            string headerPath = Path.Combine(contentPdfPath, $"header_{nc.UUID}.html");

            await System.IO.File.WriteAllTextAsync(headerPath, headerHtml, Encoding.UTF8);

            var pdf = new ViewAsPdf("NotaCreditoPdf", nc)
            {
                PageSize = Rotativa.AspNetCore.Options.Size.A4,
                PageMargins = new Rotativa.AspNetCore.Options.Margins(45, 10, 20, 10),
                FileName = $"NC_{nc.UUID}.pdf",
                CustomSwitches =
                    $"--encoding utf-8 --header-html \"{headerPath}\" " +
                    "--header-spacing 5 " +
                    "--footer-center \"Página [page] de [toPage]\" " +
                    "--footer-line --footer-font-size 10"
            };

            string rutaPDF = Path.Combine(facturasPath, $"{nc.UUID}.pdf");

            byte[] pdfBytes = await pdf.BuildFile(ControllerContext);
            await System.IO.File.WriteAllBytesAsync(rutaPDF, pdfBytes);

            System.IO.File.Delete(headerPath);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════

        public List<string> ObtenerUUIDsDeFacturas(List<int> ids)
        {
            var uuids = new List<string>();
            if (ids == null || ids.Count == 0) return uuids;

            foreach (int id in ids)
            {
                var rows = RunQuery(
                    "SELECT uuid FROM factura WHERE encabezado_id = @eid AND uuid IS NOT NULL LIMIT 1",
                    new Dictionary<string, object> { ["eid"] = id });

                if (rows != null && rows.Count > 0)
                {
                    string u = rows[0]["uuid"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(u)) uuids.Add(u);
                }
            }
            return uuids;
        }

        public List<string> ObtenerUUIDsComplementosDePago(
    List<int> encabezadoIds,
    NpgsqlConnection conn,
    NpgsqlTransaction tx)
        {
            var uuids = new List<string>();

            if (encabezadoIds == null || encabezadoIds.Count == 0)
                return uuids;

            try
            {
                // Los complementos de pago son CFDIs de tipo "P" que referencian
                // la factura origen a través de la tabla factura_relacion.
                // La columna tipo_relacion = '04' identifica la relación
                // factura PPD → complemento de pago según catálogo SAT.
                var rows = RunQuery(@"
            SELECT DISTINCT
                f_comp.uuid
            FROM factura f_orig
            INNER JOIN factura_relacion fr
                ON fr.factura_origen_id = f_orig.id
            INNER JOIN factura f_comp
                ON f_comp.id            = fr.factura_relacionada_id
               AND f_comp.tipo          = 'P'
               AND f_comp.statusfactura != 'CANCELADA'
               AND f_comp.uuid          IS NOT NULL
            WHERE f_orig.encabezado_id = ANY(@ids)
              AND f_orig.statusfactura != 'CANCELADA'",
                    new Dictionary<string, object>
                    {
                        ["ids"] = encabezadoIds.ToArray()
                    },
                    false, conn, tx);

                foreach (var row in rows)
                {
                    string u = row["uuid"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(u))
                        uuids.Add(u);
                }
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(
                    "NotaCreditoController",
                    "ObtenerUUIDsComplementosDePago",
                    $"Error al obtener complementos de pago para encabezados " +
                    $"[{string.Join(",", encabezadoIds)}]: {ex.Message}",
                    nivel: "ERROR");
                // No relanzamos — si falla la búsqueda de complementos,
                // la NC se emite con TipoRelacion "01" (factura origen solamente)
                // en lugar de bloquearse completamente.
            }

            return uuids;
        }

        private string ObtenerTasaIEPS(DataTable partidas)
        {
            decimal tasa = 0m;
            if (partidas == null || partidas.Rows.Count == 0) return "0.000000";

            foreach (DataRow row in partidas.Rows)
            {
                decimal t = Convert.ToDecimal(row["ieps"]);
                if (t > tasa) tasa = t;
            }
            return (tasa / 100m).ToString("0.000000");
        }

        private static int ObtenerDecimalesSignificativos(
    decimal valor, int minDecimals = 2, int maxDecimals = 6)
        {
            // Trabajar con la representación del número sin trailing zeros
            string s = valor.ToString("0.######").TrimEnd('0');
            int dotPos = s.IndexOf('.');
            if (dotPos < 0) return minDecimals;           // número entero
            int decimalesReales = s.Length - dotPos - 1;
            return Math.Max(minDecimals, Math.Min(decimalesReales, maxDecimals));
        }
    }


}
