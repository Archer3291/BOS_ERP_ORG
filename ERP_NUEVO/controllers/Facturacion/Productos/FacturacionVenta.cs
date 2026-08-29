using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Npgsql;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Rotativa.AspNetCore;
using Rotativa.AspNetCore.Options;
using System.Data;
using System.Text;
using System.Xml.Linq;
using BOS_ERP.services.Facturacion;

namespace BOS_ERP.Controllers.Facturacion.Productos
{
    public partial class FacturacionVentaController : Utilities
    {
        private readonly TimbradoOptions _timbrado;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IWebHostEnvironment _env;
        private readonly XmlBuilderService? _xmlBuilderService;
        private readonly IFacturaRepository? _facturaRepository;
        private readonly ITimbradoWorkflow? _timbradoWorkflow;

        public FacturacionVentaController(
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            XmlBuilderService xmlBuilderService,
            IFacturaRepository? facturaRepository = null,
            ITimbradoWorkflow? timbradoWorkflow = null)
        {
            _timbrado = timbradoOptions.Value;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _env = env;
            _xmlBuilderService = xmlBuilderService;
            _facturaRepository = facturaRepository;
            _timbradoWorkflow = timbradoWorkflow;
        }

        private IFacturaRepository FacturaRepository => _facturaRepository
            ?? HttpContext.RequestServices.GetRequiredService<IFacturaRepository>();
        private ITimbradoWorkflow TimbradoWorkflow => _timbradoWorkflow
            ?? HttpContext.RequestServices.GetRequiredService<ITimbradoWorkflow>();

        // ──────────────────────────────────────────────────────────────
        // Helpers de rutas (sustituyen Server.MapPath / "~/...")
        // ──────────────────────────────────────────────────────────────
        private string FacturacionPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion");
        private string XmlTimbradosPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados");
        private string QrCodesPath => Path.Combine(_env.WebRootPath, "content", "qrcodes");
        private string ContentPdfPath => Path.Combine(_env.WebRootPath, "content", "pdf");
        private string FacturasPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas");

        [HttpPost]
        /// <summary>
        /// Genera el XML, lo manda a timbrar y —si el PAC responde bien— guarda la factura.
        /// Acepta conn/tx para poder ejecutarse dentro de la transacción que creó los
        /// documentos: así, si el PAC rechaza el comprobante, el rollback se lleva también
        /// los encabezados y no quedan documentos huérfanos. Sin conn/tx se comporta igual
        /// que antes (conexión propia), para no tocar a los demás llamadores.
        /// </summary>
        public TimbradoResult GenerarXml(Factura factura, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            try
            {
                XmlResult xml = _xmlBuilderService.GenerateXmlAsync(factura, FacturacionPath);

                return Timbrado(xml.factura, xml.uuid, conn, tx);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionVenta/GenerarXml");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al generar el archivo XML: " + ex.Message
                };
            }
        }

        [HttpPost]
        public TimbradoResult GenerarXmlAnticipo(Factura factura)
        {
            try
            {
                XmlResult xml = _xmlBuilderService.GenerateXmlAsync(factura, FacturacionPath);

                return TimbradoAnticipo(xml.factura, xml.uuid);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionVenta/GenerarXmlAnticipo");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al generar el archivo XML de anticipo: " + ex.Message
                };
            }
        }

        public int GuardarFactura(Factura factura, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            return FacturaRepository.GuardarEncabezadoConDetalles(factura, null, conn, tx);

        }

        public TimbradoResult Timbrado(Factura factura, string xmla, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            try
            {
                var resultado = TimbradoWorkflow.EjecutarAsync(
                    factura, Path.Combine(FacturacionPath, $"{xmla}.xml"),
                    XmlTimbradosPath, QrCodesPath, "/content/qrcodes").GetAwaiter().GetResult();
                if (resultado.Success)
                {
                    GuardarFactura(factura, conn, tx);
                    GenerarFactura(factura).GetAwaiter().GetResult();
                }
                return resultado;

            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionVenta/Timbrado");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al conectar con el servicio de timbrado: " + ex.Message
                };
            }
        }

        public TimbradoResult TimbradoAnticipo(Factura factura, string xmla)
        {
            try
            {
                var resultado = TimbradoWorkflow.EjecutarAsync(
                    factura, Path.Combine(FacturacionPath, $"{xmla}.xml"),
                    XmlTimbradosPath, QrCodesPath, "/content/qrcodes").GetAwaiter().GetResult();
                if (resultado.Success)
                {
                    GuardarFactura(factura);
                    GenerarFacturaAnticipo(factura).GetAwaiter().GetResult();
                }
                return resultado;

            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionVenta/TimbradoAnticipo");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al conectar con el servicio de timbrado: " + ex.Message
                };
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Render de vista a string (reemplazo de RenderViewToString)
        // ──────────────────────────────────────────────────────────────
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

        public async Task<IActionResult> GenerarFactura(Factura factura)
        {
            if (factura == null || string.IsNullOrWhiteSpace(factura.UUID))
            {
                return Content("Factura no válida o UUID vacío.");
            }

            string contentPdfPath = ContentPdfPath;
            string facturasPath = FacturasPath;

            Directory.CreateDirectory(contentPdfPath);
            Directory.CreateDirectory(facturasPath);

            // 1. Renderizar el header dinámico como HTML (nombre único por UUID)
            string headerHtml = await RenderViewToStringAsync("Header", factura);
            string headerPath = Path.Combine(contentPdfPath, $"header_{factura.UUID}.html");

            // 2. Guardar el archivo HTML
            await System.IO.File.WriteAllTextAsync(headerPath, headerHtml, Encoding.UTF8);

            // 3. Configurar el PDF
            var pdf = new ViewAsPdf("FacturaPdf", factura)
            {
                PageSize = Size.A4,
                PageMargins = new Margins(45, 10, 20, 10),
                FileName = $"Factura_{factura.UUID}.pdf",
                CustomSwitches =
                    $"--encoding utf-8 --header-html \"{headerPath}\" " +
                    "--header-spacing 5 " +
                    "--footer-center \"Página [page] de [toPage]\" " +
                    "--footer-line --footer-font-size 10"
            };

            // 4. Generar y guardar PDF
            string rutaPDF = Path.Combine(facturasPath, $"{factura.UUID}.pdf");

            byte[] pdfBytes = await pdf.BuildFile(ControllerContext);
            await System.IO.File.WriteAllBytesAsync(rutaPDF, pdfBytes);

            // 5. Limpieza del header temporal
            System.IO.File.Delete(headerPath);

            return Content($"Factura PDF generada correctamente en: {rutaPDF}");
        }

        public async Task<IActionResult> GenerarFacturaAnticipo(Factura factura)
        {
            if (factura == null || string.IsNullOrWhiteSpace(factura.UUID))
            {
                return Content("Factura no válida o UUID vacío.");
            }

            string contentPdfPath = ContentPdfPath;
            string facturasPath = FacturasPath;

            Directory.CreateDirectory(contentPdfPath);
            Directory.CreateDirectory(facturasPath);

            // 1. Renderizar el header dinámico como HTML
            string headerHtml = await RenderViewToStringAsync("Header", factura);
            string headerPath = Path.Combine(contentPdfPath, $"header_{factura.UUID}.html");

            // 2. Guardar el archivo HTML
            await System.IO.File.WriteAllTextAsync(headerPath, headerHtml, Encoding.UTF8);

            // 3. Configurar el PDF
            var pdf = new ViewAsPdf("FacturaAnticipoPdf", factura)
            {
                PageSize = Size.A4,
                PageMargins = new Margins(45, 10, 20, 10),
                FileName = $"Factura_{factura.UUID}.pdf",
                CustomSwitches =
                    $"--encoding utf-8 --header-html \"{headerPath}\" " +
                    "--header-spacing 5 " +
                    "--footer-center \"Página [page] de [toPage]\" " +
                    "--footer-line --footer-font-size 10"
            };

            // 4. Generar y guardar PDF
            string rutaPDF = Path.Combine(facturasPath, $"{factura.UUID}.pdf");

            byte[] pdfBytes = await pdf.BuildFile(ControllerContext);
            await System.IO.File.WriteAllBytesAsync(rutaPDF, pdfBytes);

            // 🔹 Limpieza del header temporal
            System.IO.File.Delete(headerPath);

            return Content($"Factura PDF de anticipo generada correctamente en: {rutaPDF}");
        }

    }
}
