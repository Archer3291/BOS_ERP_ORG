namespace BOS_ERP.Models
{
    public class PlantillaPolizaModel
    {
        public int Id { get; set; }

        public int IdTipoPoliza { get; set; }

        public int Orden { get; set; }

        public string TipoCuenta { get; set; }

        public int Cuenta { get; set; }

        public string Lado { get; set; }

        public string Origen { get; set; }

        public decimal Factor { get; set; }

        public string Descripcion { get; set; }
    }
}