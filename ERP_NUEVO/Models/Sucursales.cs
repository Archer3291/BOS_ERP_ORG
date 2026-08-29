// Models/Usuario.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("catsucursales")]
    public class Sucursales
    {
        [Key]
        [Column("id_sucursal")]
        public int Id_sucursal { get; set; }
       
        [Column("empresa_id")]
        public int EmpresaId { get; set; }
        [Column("cve_sucursal")]
        public string Cve_sucursal { get; set; }

        [StringLength(200)]
        [Column("descripcion")]
        public string Descripcion { get; set; }

        [Column("tipo")]
        public string? Tipo { get; set; }

        // Propiedad calculada para mostrar en el dropdown
        [NotMapped]
        public string Nombre
        {
            get { return $"{Id_sucursal} - {Descripcion}"; }
        }
        // Relación con Empresa
        [ForeignKey("EmpresaId")]
        public virtual Empresa Empresa { get; set; }

    }
}