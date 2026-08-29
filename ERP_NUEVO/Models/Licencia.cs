using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("licencias")]
    public class Licencia
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("licenciaid")]
        public int LicenciaId { get; set; }

        [Column("usuarioid")]
        public int UsuarioId { get; set; }

        [StringLength(50)]
        [Column("licenciacodigo")]
        public string LicenciaCodigo { get; set; }

        [Required]
        [StringLength(100)]
        [Column("tipolicencia")]
        public string TipoLicencia { get; set; }

        [Required]
        [Column("fechainicio")]
        public DateTime FechaInicio { get; set; }

        [Required]
        [Column("fechaexpiracion")]
        public DateTime FechaExpiracion { get; set; }

        [Required]
        [Column("activa")]
        public bool Activa { get; set; } = true;
        [Column("maximofacturaspermitidas")]

        public int? MaximoFacturasPermitidas { get; set; }
        [Column("facturasusadas")]
        public int FacturasUsadas { get; set; } = 0;

        [StringLength(13)]
        [Column("rfc")]
        public string RFC { get; set; }

        [StringLength(255)]
        [Column("razonsocial")]
        public string RazonSocial { get; set; }

        [StringLength(500)]
        [Column("direccionfiscal")]
        public string DireccionFiscal { get; set; }

        [StringLength(100)]
        [Column("pais")]
        public string Pais { get; set; }

        [StringLength(100)]
        [Column("estado")]
        public string Estado { get; set; }

        [StringLength(100)]
        [Column("ciudad")]
        public string Ciudad { get; set; }

        [StringLength(100)]
        [Column("regimen")]
        public string Regimen { get; set; }


        [Required]
        [Column("empresaid")]
        public int EmpresaId { get; set; }

        [ForeignKey("EmpresaId")]
        public virtual Empresa Empresa { get; set; }
    }
}
