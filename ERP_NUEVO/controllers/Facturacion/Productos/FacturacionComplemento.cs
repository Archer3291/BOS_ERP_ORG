using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Rotativa.AspNetCore;
using Rotativa.AspNetCore.Options;
using System.Data;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BOS_ERP.Controllers.Facturacion.Productos
{
    public class FacturacionComplementoController : Utilities
    {
        private readonly TimbradoOptions _timbrado;
        private readonly BOS_ERP.Services.EmailSender _emailSender;
        private readonly IWebHostEnvironment _env;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly XmlBuilderService _XmlBuilder;
        private readonly IFacturaRepository? _facturaRepository;
        private readonly ITimbradoWorkflow? _timbradoWorkflow;

        public FacturacionComplementoController(
            BOS_ERP.Services.EmailSender emailSender,
            IOptions<TimbradoOptions> timbradoOptions,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            XmlBuilderService xmlBuilder,
            IFacturaRepository? facturaRepository = null,
            ITimbradoWorkflow? timbradoWorkflow = null)
        {
            _emailSender = emailSender;
            _timbrado = timbradoOptions.Value;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _XmlBuilder = xmlBuilder;
            _facturaRepository = facturaRepository;
            _timbradoWorkflow = timbradoWorkflow;
        }

        private IFacturaRepository FacturaRepository => _facturaRepository
            ?? HttpContext.RequestServices.GetRequiredService<IFacturaRepository>();
        private ITimbradoWorkflow TimbradoWorkflow => _timbradoWorkflow
            ?? HttpContext.RequestServices.GetRequiredService<ITimbradoWorkflow>();

        // ──────────────────────────────────────────────────────────────
        // Helpers de rutas (sustituyen Directory.GetCurrentDirectory)
        // ──────────────────────────────────────────────────────────────
        private string FacturacionPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion");
        private string XmlTimbradosPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados");
        private string QrCodesPath => Path.Combine(_env.WebRootPath, "content", "qrcodes");
        private string ContentPdfPath => Path.Combine(_env.WebRootPath, "content", "pdf");
        private string FacturasPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas");

        public TimbradoResult GenerarXmlComplementoPago(Factura factura, List<PagoComplemento> pagos)
        {
            try
            {
                XmlResult xmlResult = _XmlBuilder.GenerateXmlAsync(factura, FacturacionPath, pagos);

                return Timbrado(factura, xmlResult.uuid);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionComplemento/GenerarXmlComplementoPago");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al generar el XML para complemento de pago: " + ex.Message
                };
            }
        }

        public int GuardarFactura(Factura factura)
        {
            return FacturaRepository.GuardarEncabezadoConDetalles(factura);

        }

        public TimbradoResult Timbrado(Factura factura, string xmla)
        {
            try
            {
                var resultado = TimbradoWorkflow.EjecutarAsync(
                    factura, Path.Combine(FacturacionPath, $"{xmla}.xml"),
                    XmlTimbradosPath, QrCodesPath, "/content/qrcodes").GetAwaiter().GetResult();
                if (resultado.Success)
                {
                    GuardarFactura(factura);
                    resultado.PdfGenerado = GenerarFacturaSafe(factura);
                }
                return resultado;

            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionComplemento/Timbrado");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al conectar con el servicio de timbrado: " + ex.Message
                };
            }
        }

        // Método que NO lanza excepciones, solo registra si falla el PDF
        private bool GenerarFacturaSafe(Factura factura)
        {
            try
            {
                GenerarFactura(factura).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionComplemento/GenerarFacturaSafe");
                System.Diagnostics.Debug.WriteLine($"Error al generar PDF para UUID {factura.UUID}: {ex.Message}");
                // Opcionalmente, guardar en tabla de errores
                // RegistrarErrorGeneracionPdf(factura.UUID, ex.Message);
                return false;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Render de vista a string (reemplazo de RenderViewToString)
        // Usa IRazorViewEngine, igual que FacturacionVentaController
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

        // Método para convertir Base64 a un archivo PDF y guardarlo
    }
}
