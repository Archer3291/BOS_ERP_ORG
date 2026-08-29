using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    /// <summary>
    /// Factura de Ventas Industriales.
    ///
    /// Todo el proceso (guardar → timbrar → NC de anticipos → commit) vive en
    /// VentasFacturaBaseController, compartido con Sucursales. Aquí solo queda la
    /// identidad del canal y los puntos de entrada HTTP con sus rutas históricas.
    /// </summary>
    [Authorize]
    public class VIFacturaController : VentasFacturaBaseController
    {
        public VIFacturaController(
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

        protected override string FacturaLogTag => "VIFacturaController";
        protected override int FacturaIdArea => 13;
        protected override int FacturaIdTpDoc => 48;
        protected override string FacturaTpMov => "VIFAC";
        protected override string FacturaSerie => "VI";

        [Route("VIFactura/ProcesarDocumentosAsync")]
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industriales", Accion = "Creacion de factura")]
        public Task<JsonResult> ProcesarDocumentosAsync_V2(IFormCollection IFormCollection)
            => ProcesarFacturaAsync(IFormCollection);

        [HttpPost]
        [Route("VIFactura/ValidarPartidsSAT")]
        public Task<JsonResult> ValidarPartidasSAT(IFormCollection IFormCollection)
            => ValidarPartidasSATCore(IFormCollection);

        [HttpPost]
        [Route("VIFactura/EnviarAlertaSAT")]
        public Task<ActionResult> EnviarAlertaSAT() => EnviarAlertaSATCore();
    }
}
