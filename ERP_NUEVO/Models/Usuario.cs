// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("usuarios")]
    public class Usuario
    {
        [Key]
        [Column("usuarioid")] // ← nombre real en la base de datos
        public int UsuarioId { get; set; }

        [Column("nombre")]
        public string Nombre { get; set; }

        [Required]
        [Column("email")]
        public string Email { get; set; }

        [Column("apellido")]
        public string Apellido { get; set; }

        [StringLength(20)]
        [Column("telefono")]
        public string? Telefono { get; set; }

        [Required]
        [StringLength(50)]
        [Column("nombreusuario")]
        public string NombreUsuario { get; set; }

        [DataType(DataType.Password)]
        [Column("contrasena")]
        public string Contrasena { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("fechacreacion")]
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        [Column("fechaultimamodificacion")]
        public DateTime? FechaUltimaModificacion { get; set; }

        // Clave foránea
        [Column("rolid")]
        public int RolId { get; set; }

        // Propiedad de navegación
        [ForeignKey("RolId")]
        public virtual Rol Rol { get; set; }

        [Column("fecha")]
        public DateTime? Fecha { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        [Column("licencia")]
        public string? Licencia { get; set; }

        [StringLength(50)]
        [Column("superior_email")]
        public string SuperiorEmail { get; set; }

        [Required]
        [Column("empresaid")]
        public int EmpresaId { get; set; }

        [ForeignKey("EmpresaId")]
        public virtual Empresa Empresa { get; set; }

        [Column("firma")]
        public string? Firma { get; set; }

        [Column("sucursal_id")]
        public int SucursalId { get; set; }

        [Column("areaid")]
        public int AreaId { get; set; }

        public virtual ICollection<Permisos> Permisos { get; set; }
    }
}