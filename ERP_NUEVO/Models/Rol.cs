// Models/Rol.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("roles")]
    public class Rol
    {
        [Column("rolid")]
        public int RolId { get; set; }

        [Required]
        [StringLength(50)]
        [Column("nombre")]
        public string Nombre { get; set; }

        [StringLength(255)]
        [Column("descripcion")]
        public string Descripcion { get; set; }

        // Propiedad de navegación inversa
        public virtual ICollection<Usuario> Usuarios { get; set; }
    }
}