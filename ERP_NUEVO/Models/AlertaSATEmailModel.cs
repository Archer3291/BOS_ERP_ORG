// Models/Rol.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class AlertaSATEmailModel
    {
        public string Folio { get; set; }
        public string Cliente { get; set; }
        public string UsuarioQueValido { get; set; }
        public string FechaValidacion { get; set; }
        public List<ProductoSATInvalido> ProductosInvalidos { get; set; }
            = new List<ProductoSATInvalido>();
    }

    public class ProductoSATInvalido
    {
        public string ProductoId { get; set; }
        public string Descripcion { get; set; }
        public List<string> CamposFaltantes { get; set; }
            = new List<string>();
    }
}