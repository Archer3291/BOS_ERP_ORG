using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Npgsql;
using Rotativa.AspNetCore;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BOS_ERP.Controllers.Facturacion.Productos
{
    public class FacturacionVentaINController : Utilities
    {
        private readonly TimbradoOptions _timbrado;
        private BOS_ERP.Services.EmailSender _emailSender;
        private readonly IWebHostEnvironment _env;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly XmlBuilderService _xmlBuilder;
        private readonly IFacturaRepository? _facturaRepository;
        private readonly ITimbradoWorkflow? _timbradoWorkflow;

        public FacturacionVentaINController(
            BOS_ERP.Services.EmailSender emailSender,
            IOptions<TimbradoOptions> timbradoOptions,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            XmlBuilderService xmlBuilder,
            IFacturaRepository? facturaRepository = null,
            ITimbradoWorkflow? timbradoWorkflow = null)
        {
            this._emailSender = emailSender;
            _timbrado = timbradoOptions.Value;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
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
        // resuelve en ASP.NET Core)
        // ──────────────────────────────────────────────────────────────
        private string FacturacionPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion");
        private string XmlTimbradosPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados");
        private string QrCodesPath => Path.Combine(_env.WebRootPath, "content", "qrcodes");
        private string ContentPdfPath => Path.Combine(_env.WebRootPath, "content", "pdf");
        private string FacturasPath => Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas");

        [HttpPost]
        public TimbradoResult GenerarXml(Factura factura, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            try
            {
                XmlResult xmlResult = _xmlBuilder.GenerateXmlAsync(factura, FacturacionPath);

                return Timbrado(factura, xmlResult.uuid);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionVentaIN/GenerarXml");
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
                XmlResult xmlResult = _xmlBuilder.GenerateXmlAsync(factura, FacturacionPath);

                // ✅ Mandar a timbrar
                return TimbradoAnticipo(factura, xmlResult.uuid);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionVentaIN/GenerarXmlAnticipo");
                //BorradoFacturasIncorrectas(factura.EncabezadoId);
                return new TimbradoResult
                {
                    Success = false,
                    Message = "Error al generar el archivo XML de anticipo: " + ex.Message
                };
            }
        }

        // Método ficticio para calcular el total (puedes adaptarlo a tus necesidades)
        public int GuardarFactura(Factura factura, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            return FacturaRepository.GuardarEncabezadoConDetalles(factura, null, conn, tx);

        }

        public TimbradoResult Timbrado(Factura factura, string xmla, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            try
            {
                return TimbradoWorkflow.EjecutarAsync(
                    factura, Path.Combine(FacturacionPath, $"{xmla}.xml"),
                    XmlTimbradosPath, QrCodesPath, "/content/qrcodes").GetAwaiter().GetResult();

            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturacionVentaIN/Timbrado");
                //BorradoFacturasIncorrectas(factura.EncabezadoId);
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
                RegistrarErrorParaTicket(ex, "FacturacionVentaIN/TimbradoAnticipo");
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
                return Content("Factura no válida o UUID vacío.");

            string contentPdfPath = ContentPdfPath;
            string facturasPath = FacturasPath;

            Directory.CreateDirectory(contentPdfPath);
            Directory.CreateDirectory(facturasPath);

            // ── PDF en ESPAÑOL ────────────────────────────────────────
            string headerHtmlES = await RenderViewToStringAsync("Header", factura);
            string headerPathES = Path.Combine(contentPdfPath, $"header_{factura.UUID}.html");

            // 2. Guardar el archivo HTML
            await System.IO.File.WriteAllTextAsync(headerPathES, headerHtmlES, Encoding.UTF8);

            var pdfES = new ViewAsPdf("FacturaPdf", factura)
            {
                PageSize = Rotativa.AspNetCore.Options.Size.A4,
                PageMargins = new Rotativa.AspNetCore.Options.Margins(45, 10, 20, 10),
                FileName = $"Factura_{factura.UUID}.pdf",
                CustomSwitches =
                    $"--encoding utf-8 --header-html \"{headerPathES}\" " +
                    "--header-spacing 5 " +
                    "--footer-center \"Página [page] de [toPage]\" " +
                    "--footer-line --footer-font-size 10"
            };

            string rutaPDF_ES = Path.Combine(facturasPath, $"{factura.UUID}.pdf");

            byte[] pdfBytesES = await pdfES.BuildFile(ControllerContext);
            await System.IO.File.WriteAllBytesAsync(rutaPDF_ES, pdfBytesES);
            System.IO.File.Delete(headerPathES); // limpieza header temporal ES

            // ── PDF en INGLÉS ─────────────────────────────────────────
            string headerHtmlEN = await RenderViewToStringAsync("Header_EN", factura);
            string headerPathEN = Path.Combine(contentPdfPath, $"header_{factura.UUID}_en.html");
            await System.IO.File.WriteAllTextAsync(headerPathEN, headerHtmlEN, Encoding.UTF8);

            var pdfEN = new ViewAsPdf("FacturaPdf_EN", factura)
            {
                PageSize = Rotativa.AspNetCore.Options.Size.A4,
                PageMargins = new Rotativa.AspNetCore.Options.Margins(45, 10, 20, 10),
                FileName = $"Factura_{factura.UUID}.pdf",
                CustomSwitches =
                    $"--encoding utf-8 --header-html \"{headerPathEN}\" " +
                    "--header-spacing 5 " +
                    "--footer-center \"Página [page] de [toPage]\" " +
                    "--footer-line --footer-font-size 10"
            };

            string rutaPDF_EN = Path.Combine(facturasPath, $"{factura.UUID}_EN.pdf");
            byte[] pdfBytesEN = await pdfEN.BuildFile(ControllerContext);
            await System.IO.File.WriteAllBytesAsync(rutaPDF_EN, pdfBytesEN);

            System.IO.File.Delete(headerPathEN); // limpieza header temporal EN

            return Content(
                $"PDFs generados correctamente:\n" +
                $" → {rutaPDF_ES}"
            );

            //    return Content(
            //    $"PDFs generados correctamente:\n" +
            //    $"  ES → {rutaPDF_ES}\n" +
            //    $"  EN → {rutaPDF_EN}"
            //);
        }


        public async Task<IActionResult> GenerarFacturaAnticipo(Factura factura)
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

            // 1. Renderizar el header dinámico como HTML
            string headerHtml = await RenderViewToStringAsync("Header", factura);
            string headerPath = Path.Combine(contentPdfPath, $"header_{factura.UUID}.html");

            // 2. Guardar el archivo HTML
            await System.IO.File.WriteAllTextAsync(headerPath, headerHtml, Encoding.UTF8);

            // 3. Configurar el PDF
            var pdf = new ViewAsPdf("FacturaAnticipoPdf", factura)
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

            // 🔹 Limpieza del header temporal
            System.IO.File.Delete(headerPath);

            return Content($"Factura PDF de anticipo generada correctamente en: {rutaPDF}");
        }

        // Método para convertir Base64 a un archivo PDF y guardarlo
        string NormalizarPedimento(string pedimento)
        {
            if (string.IsNullOrWhiteSpace(pedimento))
                return null;

            // Quitar cualquier espacio raro
            pedimento = pedimento
                .Replace('\u00A0', ' ') // NBSP
                .Replace('\t', ' ')
                .Replace('\n', ' ')
                .Replace('\r', ' ');

            // Colapsar espacios múltiples
            pedimento = Regex.Replace(pedimento, @"\s+", " ");

            return pedimento.Trim();
        }

        bool PedimentoValido(string pedimento)
        {
            return Regex.IsMatch(pedimento, @"^\d{2} \d{2} \d{4} \d{7}$");
        }
    }
}
