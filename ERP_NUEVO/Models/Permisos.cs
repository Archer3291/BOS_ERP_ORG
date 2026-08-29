// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("permisos")]
    public class Permisos
    {
        [Key]
        [Column("id_permiso")] // Nombre real en la base de datos
        public int Id_permiso { get; set; }

        [Required]
        [StringLength(50)]
        [Column("nombre")]
        public string Nombre { get; set; }

        [StringLength(200)]
        [Column("descripcion")]
        public string Descripcion { get; set; }

        [Column("es_modulo")]
        public bool EsModulo { get; set; }

        [Column("modulo_padre")]
        public int? ModuloPadre { get; set; }

        public virtual ICollection<Usuario> Usuarios { get; set; }
    }
}