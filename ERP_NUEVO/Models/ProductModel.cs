
namespace BOS_ERP.Models
{
    public class ProductApi
    {
        //Datos del producto
        public int Id { get; set; }
        public string Clave { get; set; }
        public string Descripcion { get; set; }
        public string Linea { get; set; }
        public string Tipo { get; set; }
        public string Grupo { get; set; }
        public string Naturaleza { get; set; }
        public string Unidad { get; set; }

        // Datos Sat
        public string ProductoSat {  get; set; }
        public string UnidadSat { get; set; }
        public string ObjetoImpuesto { get; set; }

        //Para el request 
        public decimal? Cantidad { get; set; }
    }
}