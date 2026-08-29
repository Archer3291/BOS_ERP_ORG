using NpgsqlTypes;

namespace BOS_ERP.Models.Carteras.Cliente
{
    public enum TipoCobroCliente
    {
        [PgName("normal")]
        Normal,
        [PgName("anticipo")]
        Anticipo,
        [PgName("complemento")]
        Complemento,
        [PgName("nota_credito")]
        NotaCredito
    }
}