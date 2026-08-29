using System.Collections.Generic;

namespace BOS_ERP.Models.CuentasContables
{
    public class AplicarCobros
    {
        public int ClienteId { get; set; }

        public List<int> CobrosIds { get; set; } = new List<int>();

        public List<int> CarteraIds { get; set; } = new List<int>();

        public int UsuarioId { get; set; }
    }
}