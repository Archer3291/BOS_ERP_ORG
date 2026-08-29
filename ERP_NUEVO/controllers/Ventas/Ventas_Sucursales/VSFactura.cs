using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace BOS_ERP.Controllers.Ventas.Ventas_Sucursales
{
    /// <summary>
    /// Factura de Ventas Sucursales.
    ///
    /// Antes tenía su propio Guardar/GenerarFacturaVentas, una versión anterior del flujo
    /// industrial: sin facturación parcial ni múltiple desde remisiones, sin aplicación de
    /// anticipos con su nota de crédito y sin validación de crédito ni de reglas de precio.
    /// Ahora comparte el pipeline con Industriales (VentasFacturaBaseController) y aquí solo
    /// queda la identidad del canal.
    ///
    /// El nat vuelve a ser VSFAC: el controlador anterior generaba los encabezados con
    /// TpMov = "VIFAC" (copia del industrial), de modo que las facturas de sucursal quedaban
    /// registradas como industriales y no aparecían en las consultas que filtran por VSFAC
    /// (cartera, portal de clientes, historial de facturas).
    /// </summary>
    [Authorize]
    public class VSFacturaController : VentasFacturaBaseController
    {
        public VSFacturaController(
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            IConfiguration configuration,
            BOS_ERP.Services.EmailSender emailSender,
            BOS_ERP.Helpers.CorreoHelper correoHelper,
            XmlBuilderService xmlService)
            : base(timbradoOptions, viewEngine, tempDataProvider, env,
                   configuration, emailSender, correoHelper, xmlService)
        {
        }

        protected override string FacturaLogTag => "VSFacturaController";
        protected override int FacturaIdArea => 14;
        protected override int FacturaIdTpDoc => 52;
        protected override string FacturaTpMov => "VSFAC";
        protected override string FacturaSerie => "VS";

        [Route("VSFactura/ProcesarDocumentosAsync")]
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Creacion de factura")]
        public Task<JsonResult> ProcesarDocumentosAsync(IFormCollection IFormCollection)
            => ProcesarFacturaAsync(IFormCollection);

        [HttpPost]
        [Route("VSFactura/ValidarPartidsSAT")]
        public Task<JsonResult> ValidarPartidasSAT(IFormCollection IFormCollection)
            => ValidarPartidasSATCore(IFormCollection);

        [HttpPost]
        [Route("VSFactura/EnviarAlertaSAT")]
        public Task<ActionResult> EnviarAlertaSAT() => EnviarAlertaSATCore();
    }
}
