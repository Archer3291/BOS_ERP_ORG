// controllers/Facturacion/FacturaPdfController.cs
using BOS_ERP.services.Facturacion;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Services.Facturacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Rotativa.AspNetCore;
using Rotativa.AspNetCore.Options;
using System.Text;

namespace BOS_ERP.Controllers.Facturacion
{
    /// <summary>
    /// Módulo GENERAL para generar la representación impresa (PDF) de cualquier factura ya
    /// timbrada guardada en la BD, a partir de su CFDI (columna textfactura o archivo en disco).
    /// No es específico de la refacturación; sirve para todas las facturas.
    /// </summary>
    public class FacturaPdfController : Utilities
    {
        private readonly IFacturaPdfService _facturaPdf;
        private readonly IWebHostEnvironment _env;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IComprobanteFiscalService _comprobanteFiscal;

        public FacturaPdfController(
            IFacturaPdfService facturaPdf,
            IConfiguration configuration,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IComprobanteFiscalService comprobanteFiscal)
        {
            _facturaPdf = facturaPdf;
            _configuration = configuration;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _comprobanteFiscal = comprobanteFiscal;
        }

        // ============================================================
        // VISTA
        // ============================================================
        [HttpGet]
        public IActionResult Index() => View();

        // ============================================================
        // BÚSQUEDA DE FACTURAS (para elegir en la vista)
        // ============================================================
        [HttpGet]
        public IActionResult Buscar(string q = "", int page = 1, int pageSize = 25)
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            var parameters = new Dictionary<string, object>
            {
                { "q",        q ?? "" },
                { "offset",   (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "suc",      suc }
            };

            string query = @"
                SELECT fa.id, fa.serie, fa.folio, fa.uuid::text AS uuid,
                       fa.rfccliente, fa.rsocliente, fa.rfcemisor, fa.rsoemisor,
                       fa.total, fa.moneda, fa.fecha, fa.statusfactura,
                       (fa.textfactura IS NOT NULL AND length(fa.textfactura) > 0) AS tiene_xml
                FROM factura fa
                INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
                WHERE em.suc = @suc
                  AND fa.uuid IS NOT NULL
                  AND (
                        @q = '' OR
                        LOWER(fa.folio)      LIKE LOWER('%' || @q || '%') OR
                        LOWER(fa.serie)      LIKE LOWER('%' || @q || '%') OR
                        LOWER(fa.rfccliente) LIKE LOWER('%' || @q || '%') OR
                        LOWER(fa.rsocliente) LIKE LOWER('%' || @q || '%') OR
                        fa.uuid::text        ILIKE '%' || @q || '%'
                      )
                ORDER BY fa.id DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);
            return Json(new { data });
        }

        // ============================================================
        // GENERAR / VER PDF
        //   descargar=false → inline (previsualizar en iframe)
        //   descargar=true  → attachment (forzar descarga)
        //   regenerar=true  → ignora el PDF cacheado y lo vuelve a construir
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Generar(string uuid, bool descargar = false, bool regenerar = false, bool en = false)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return BadRequest("UUID requerido.");

            // El PDF en inglés se archiva aparte como {uuid}_EN.pdf (igual que el flujo internacional).
            string sufijo = en ? "_EN" : "";
            string pdfPath = Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas", $"{uuid}{sufijo}.pdf");

            if (regenerar || !System.IO.File.Exists(pdfPath))
            {
                var factura = _facturaPdf.ReconstruirPorUuid(uuid);
                if (factura == null)
                    return NotFound("No se encontró la factura ni su XML timbrado (columna textfactura ni archivo en disco).");

                var ruta = await GenerarPdfFactura(factura, en);
                if (ruta == null) return StatusCode(500, "No se pudo generar el PDF.");
            }

            if (!System.IO.File.Exists(pdfPath)) return StatusCode(500, "No se pudo generar el PDF.");

            byte[] bytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
            string dispo = descargar ? "attachment" : "inline";
            Response.Headers["Content-Disposition"] = $"{dispo}; filename=\"Factura_{uuid}{sufijo}.pdf\"";
            return File(bytes, "application/pdf");
        }

        // ============================================================
        // DESCARGAR XML TIMBRADO (columna o archivo)
        // ============================================================
        [HttpGet]
        public IActionResult DescargarXml(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return BadRequest("UUID requerido.");
            string xml = _facturaPdf.ObtenerXml(uuid);
            if (string.IsNullOrWhiteSpace(xml)) return NotFound("XML no encontrado.");
            return File(Encoding.UTF8.GetBytes(xml), "application/xml", $"{uuid}.xml");
        }

        // ============================================================
        // GENERACIÓN DEL PDF (mismo molde/diseño que FacturacionVenta)
        // ============================================================
        private async Task<string> GenerarPdfFactura(Factura factura, bool en = false)
        {
            string headerPath = null;
            string rutaQrFisica = null;
            string empresaAnterior = HttpContext.Session.GetString("EmpresaFactura");

            // Molde/header según idioma. El internacional en inglés se archiva como {uuid}_EN.pdf.
            string sufijo = en ? "_EN" : "";
            string vistaDoc = en ? "/Views/FacturaPdf/Documento_EN.cshtml" : "/Views/FacturaPdf/Documento.cshtml";
            string vistaHeader = en ? "/Views/FacturaPdf/Header_EN.cshtml" : "/Views/FacturaPdf/Header.cshtml";
            string pieDerecha = en ? "Page [page] of [toPage]" : "Página [page] de [toPage]";

            try
            {
                // Deriva el perfil emisor (SRS/ITF/AHN/SRV) desde el RFC del emisor del CFDI para
                // que el logo del header y el pagaré correspondan a la empresa de ESTA factura,
                // sin depender de la empresa con la que el usuario inició sesión. Se restaura al final.
                string perfil = ResolverPerfilConLogo(factura.RfcEmisor, empresaAnterior);
                HttpContext.Session.SetString("EmpresaFactura", perfil);

                // PDF final archivado en wwwroot/Facturacion/facturas (excluido del watch en el .csproj).
                string facturasPath = Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas");
                Directory.CreateDirectory(facturasPath);

                // Temporales (QR + header HTML) en el TEMP del sistema, FUERA del árbol del proyecto,
                // para que el Hot Reload de Visual Studio no dispare un refresh del navegador.
                string tempDir = Path.Combine(Path.GetTempPath(), "factura_pdf");
                Directory.CreateDirectory(tempDir);

                // QR como base64 (sin red): wkhtmltopdf lo pinta directo desde Model.RutaQr.
                rutaQrFisica = Path.Combine(tempDir, $"qr_{factura.UUID}.png");
                _comprobanteFiscal.GenerarQr(factura, tempDir);
                factura.RutaQr = "data:image/png;base64," + Convert.ToBase64String(System.IO.File.ReadAllBytes(rutaQrFisica));

                // Header dinámico (emisor + logo) como HTML temporal
                string headerHtml = await RenderViewToStringAsync(vistaHeader, factura);
                headerPath = Path.Combine(tempDir, $"header_{factura.UUID}{sufijo}.html");
                await System.IO.File.WriteAllTextAsync(headerPath, headerHtml, Encoding.UTF8);

                var pdf = new ViewAsPdf(vistaDoc, factura)
                {
                    PageSize = Size.A4,
                    PageMargins = new Margins(45, 10, 20, 10),
                    FileName = $"Factura_{factura.UUID}{sufijo}.pdf",
                    CustomSwitches =
                        $"--encoding utf-8 --header-html \"{headerPath}\" " +
                        "--header-spacing 5 " +
                        $"--footer-center \"{pieDerecha}\" " +
                        "--footer-line --footer-font-size 8"
                };

                byte[] pdfBytes = await pdf.BuildFile(ControllerContext);
                string rutaPdf = Path.Combine(facturasPath, $"{factura.UUID}{sufijo}.pdf");
                await System.IO.File.WriteAllBytesAsync(rutaPdf, pdfBytes);

                return rutaPdf;
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog("FacturaPdf_Generar", factura?.UUID ?? "", ex.ToString(), User.Identity?.Name);
                return null;
            }
            finally
            {
                // Restaura la empresa de sesión del usuario.
                if (empresaAnterior != null) HttpContext.Session.SetString("EmpresaFactura", empresaAnterior);
                try { if (headerPath != null && System.IO.File.Exists(headerPath)) System.IO.File.Delete(headerPath); } catch { /* no crítico */ }
                try { if (rutaQrFisica != null && System.IO.File.Exists(rutaQrFisica)) System.IO.File.Delete(rutaQrFisica); } catch { /* no crítico */ }
            }
        }

        // Mapea el RFC del emisor del CFDI a su perfil en la sección "Emisores" de la config.
        private string ResolverPerfilEmisor(string rfcEmisor)
        {
            if (string.IsNullOrWhiteSpace(rfcEmisor)) return null;
            foreach (var e in _configuration.GetSection("Emisores").GetChildren())
                if (string.Equals(e["Rfc"], rfcEmisor, StringComparison.OrdinalIgnoreCase))
                    return e.Key;
            return null;
        }

        // Elige el perfil cuyo logo se usará, con fallback para que SIEMPRE haya logo:
        //   1) el del emisor del CFDI (si su carpeta content/img/{perfil}/logo.png existe),
        //   2) la empresa de la sesión del usuario (como el header estándar),
        //   3) lo que haya (aunque no tenga logo, para no romper el pagaré/razón social).
        // Evita el hueco de emisores sin carpeta de logo (p. ej. el RFC de pruebas EKU9003173C9 → "pruebas").
        private string ResolverPerfilConLogo(string rfcEmisor, string empresaSesion)
        {
            string emisor = ResolverPerfilEmisor(rfcEmisor);
            if (LogoExiste(emisor)) return emisor;
            if (LogoExiste(empresaSesion)) return empresaSesion;
            return emisor ?? empresaSesion ?? "";
        }

        private bool LogoExiste(string perfil)
        {
            if (string.IsNullOrWhiteSpace(perfil)) return false;
            string webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            return System.IO.File.Exists(Path.Combine(webRoot, "content", "img", perfil, "logo.png"));
        }

        // Renderiza una vista Razor a string (para el --header-html). Acepta ruta absoluta
        // ("/Views/...") vía GetView, o nombre simple vía FindView.
        private async Task<string> RenderViewToStringAsync(string viewName, object model)
        {
            var actionContext = new ActionContext(HttpContext, RouteData, ControllerContext.ActionDescriptor);

            var viewResult = (viewName.StartsWith("/") || viewName.StartsWith("~"))
                ? _viewEngine.GetView(executingFilePath: null, viewPath: viewName, isMainPage: false)
                : _viewEngine.FindView(actionContext, viewName, isMainPage: false);

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
                new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }
    }
}
