using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Npgsql;
using Rotativa.AspNetCore;
using System.Data;
using System.Text;
using System.Xml.Linq;

namespace BOS_ERP.Controllers.Facturacion.Servicios
{
    public class PredialController : Utilities
    {
        private readonly TimbradoOptions _timbrado;
        private EmailSender _emailSender;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IWebHostEnvironment _env;
        private readonly XmlBuilderService _xmlBuilder;
        private readonly IFacturaRepository? _facturaRepository;
        private readonly ITimbradoWorkflow? _timbradoWorkflow;

        public PredialController(EmailSender emailSender, IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            XmlBuilderService xmlBuilder,
            IFacturaRepository? facturaRepository = null,
            ITimbradoWorkflow? timbradoWorkflow = null)
        {
            _emailSender = emailSender;
            _timbrado = timbradoOptions.Value;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _env = env;
            _xmlBuilder = xmlBuilder;
            _facturaRepository = facturaRepository;
            _timbradoWorkflow = timbradoWorkflow;
        }

        private IFacturaRepository FacturaRepository => _facturaRepository
            ?? HttpContext.RequestServices.GetRequiredService<IFacturaRepository>();
        private ITimbradoWorkflow TimbradoWorkflow => _timbradoWorkflow
            ?? HttpContext.RequestServices.GetRequiredService<ITimbradoWorkflow>();

        // ──────────────────────────────────────────────────────────────
        // Helpers de rutas (sustituyen "~/Facturacion", que NO se
        // resuelve en ASP.NET Core) — mismas rutas que Venta/VentaIN
        // ──────────────────────────────────────────────────────────────
        private string FacturacionPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion");
        private string XmlTimbradosPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados");
        private string QrCodesPath => Path.Combine(_env.WebRootPath, "content", "qrcodes");
        private string ContentPdfPath => Path.Combine(_env.WebRootPath, "content", "pdf");
        private string FacturasPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas");

        // ──────────────────────────────────────────────────────────────
        // Genera el XML y lo manda a timbrar. NO toca la base de datos:
        // el guardado vive en la transacción del controller y el PDF se
        // genera después del commit (ver FacturacionPredialController).
        // ──────────────────────────────────────────────────────────────
        [HttpPost]
        public TimbradoResult GenerarXml(Factura factura)
        {
            try
            {
                XmlResult xml = _xmlBuilder.GenerateXmlAsync(factura, FacturacionPath);

                // Mandar a timbrar
                return Timbrado(xml.factura, xml.uuid);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Predial/GenerarXml");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al generar el archivo XML: " + ex.Message
                };
            }
        }

        public int GuardarFactura(Factura factura, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            return FacturaRepository.GuardarEncabezadoConDetalles(factura, null, conn, tx);

        }


        public TimbradoResult Timbrado(Factura factura, string xmla)
        {
            try
            {
                return TimbradoWorkflow.EjecutarAsync(
                    factura, Path.Combine(FacturacionPath, $"{xmla}.xml"),
                    XmlTimbradosPath, QrCodesPath, "/content/qrcodes").GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Predial/Timbrado");
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al conectar con el servicio de timbrado: " + ex.Message
                };
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Render de vista a string (reemplaza _emailSender.RenderViewToStringAsync)
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

            // 🔹 Crear carpetas necesarias
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
                PageSize = Rotativa.AspNetCore.Options.Size.A4,
                PageMargins = new Rotativa.AspNetCore.Options.Margins(45, 10, 20, 10),
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
