using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("empresas")]
    public class Empresa
    {
        [Key]
        [Column("empresaid")]
        public int EmpresaId { get; set; }

        [Required]
        [StringLength(255)]
        [Column("nombre")]
        public string Nombre { get; set; }
        [Required]
        [StringLength(255)]
        [Column("rfc")]
        public string RFC { get; set; }

        // Relación: una empresa puede tener muchas licencias
        public virtual ICollection<Licencia> Licencias { get; set; }

        // Relación: una empresa puede tener muchos usuarios
        public virtual ICollection<Usuario> Usuarios { get; set; }
    }
}
