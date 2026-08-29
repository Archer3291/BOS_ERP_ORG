using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class UsuarioDto
    {
        public int Id { get; set; } 
        public string NombreUsuario { get; set; }
        public string Nombre { get; set; }
        public string Email { get; set; }
        public string Rol { get; set; }
    }
}
