// Services/Refacturacion/IRefacturacionTypeHandler.cs
using Npgsql;

namespace BOS_ERP.Services.Refacturacion
{
    public class ValidacionResultado
    {
        public bool EsValido { get; set; }
        public List<string> Errores { get; set; } = new();
        public bool RequiereCancelacion { get; set; } = true;
        public bool RequiereTimbradoNuevo { get; set; } = true; // false = solo Adenda "solo-corregir"
        // true = el tipo cambia partidas y hay que ajustar inventario (revertir el del original +
        // re-descontar el nuevo). Lo activa ConceptosTypeHandler; el orquestador lo ejecuta.
        public bool AjustaInventario { get; set; } = false;
    }

    public interface IRefacturacionTypeHandler
    {
        string TipoId { get; }
        Task<ValidacionResultado> ValidarAsync(int encabezadoIdOriginal, Dictionary<string, object> cambios);

        // Devuelve la Factura reconstruida + el XML sin timbrar, para que el
        // orquestador timbre y luego persista con todas sus columnas.
        Task<ResultadoConstruccionCfdi> ConstruirXmlNuevoAsync(int encabezadoIdOriginal, Dictionary<string, object> cambios, string motivoCancelacion, string tipoRelacion, string uuidRelacionado);

        // Solo lo implementa Adenda en modo "solo-corregir": regenera la Addenda sin tocar el timbrado.
        // Recibe conn/tx para participar en la transacción del orquestador.
        Task<string> RegenerarSinTimbrarAsync(int encabezadoIdOriginal, Dictionary<string, object> cambios,
            NpgsqlConnection conn, NpgsqlTransaction tx) => Task.FromResult<string>(null);
    }
}
