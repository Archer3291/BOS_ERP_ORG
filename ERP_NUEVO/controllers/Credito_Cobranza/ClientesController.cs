using BOS_ERP.Services;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Controlador de clientes (Crédito y Cobranza).
    ///
    /// Está partido en varios archivos dentro de Credito_Cobranza/Clientes/,
    /// uno por responsabilidad:
    ///
    ///   ClientesConsultaController         listado y detalle
    ///   ClientesCatalogosController        países, estados, colonias, datos fiscales…
    ///   ClientesNacionalesController       alta de cliente nacional
    ///   ClientesInternacionalesController  alta de cliente internacional
    ///   ClientesActualizarController       actualización completa del cliente
    ///   ClientesAddendasController         CRUD de addendas CFDI
    ///   ClientesSincronizacionController   importación desde el sistema externo
    /// </summary>
    [RightAuthorize("gestion_clientes")]
    public partial class ClientesController : Utilities
    {
        private readonly ClientSyncService _clientSyncService;

        public ClientesController(ClientSyncService clientSyncService)
        {
            _clientSyncService = clientSyncService;
        }
    }
}
