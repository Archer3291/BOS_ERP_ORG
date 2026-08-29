namespace BOS_ERP.Models
{
    public class TipoPolizaModel
    {
        public int Id { get; set; }

        public int IdCategoria { get; set; }

        public string Nombre { get; set; }

        public string Descripcion { get; set; }

        public bool Activo { get; set; }

        public int? Clasificacion { get; set; }

        public int EmpresaId { get; set; }
    }
}