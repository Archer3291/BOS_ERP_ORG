// Services/Refacturacion/RefacturacionModels.cs
using BOS_ERP.Models;

namespace BOS_ERP.Services.Refacturacion
{
    /// <summary>
    /// Lo que un handler entrega al orquestador tras armar el CFDI nuevo:
    /// el objeto Factura completo (para persistirlo con todas sus columnas)
    /// y el XML sin timbrar (para enviarlo al PAC).
    /// </summary>
    public class ResultadoConstruccionCfdi
    {
        public Factura Factura { get; set; }
        public string Xml { get; set; }
    }

    /// <summary>
    /// DTO que viaja desde el front (step4.js / step5.js) hacia el orquestador.
    /// Debe mapear 1:1 con el JSON que arma buildSummary/execSteps en el wizard.
    /// </summary>
    public class RefacturacionRequest
    {
        public string TipoId { get; set; }                 // 'datos-fiscales', 'metodo-pago', 'adenda', etc.
        public int EncabezadoIdOriginal { get; set; }
        public string UuidOriginal { get; set; }
        public string RfcEmisor { get; set; }
        public string Motivo { get; set; }                  // motivo de cancelación SAT (01,02,03,04)
        public string TipoRelacion { get; set; }             // "04" sustitución, etc.
        public Dictionary<string, object> Cambios { get; set; } = new();
    }

    /// <summary>
    /// Resultado uniforme que regresa el orquestador al controller.
    /// </summary>
    public class RefacturacionResultado
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string UuidNuevo { get; set; }

        // La factura nueva persistida (solo en el camino de sustitución) para que el
        // controlador genere el PDF con Rotativa después del commit.
        public Factura FacturaNueva { get; set; }

        public static RefacturacionResultado Exitoso(string uuidNuevo) =>
            new RefacturacionResultado { Success = true, Message = "Operación completada.", UuidNuevo = uuidNuevo };

        public static RefacturacionResultado Fallido(string mensaje) =>
            new RefacturacionResultado { Success = false, Message = mensaje };
    }
}