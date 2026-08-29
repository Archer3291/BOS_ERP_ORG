using System;
using System.ComponentModel.DataAnnotations;

namespace Sellosop.Models
{
    public class VendedorModel
    {
        [Required]
        [StringLength(5, ErrorMessage = "La clave no puede exceder 5 caracteres.")]
        public string Clave { get; set; } // Clave del vendedor

        [Required]
        [StringLength(120, ErrorMessage = "El nombre no puede exceder 120 caracteres.")]
        public string Nombre { get; set; } // Nombre del vendedor

        [Required]
        [Range(0, 100, ErrorMessage = "La comisión sobre venta debe estar entre 0 y 100")]
        public double ComisionVenta { get; set; } // Comisión porcentual sobre venta

        [Required]
        [Range(0, 100, ErrorMessage = "La comisión sobre cobros debe estar entre 0 y 100")]
        public double ComisionCobro { get; set; } // Comisión porcentual sobre cobros

        [Required]
        [StringLength(1, ErrorMessage = "El estatus debe ser un solo carácter.")]
        public string Estatus { get; set; } // Estatus (A, I, G)

        [Required]
        [StringLength(14, ErrorMessage = "El teléfono no puede exceder 14 caracteres.")]
        public string Telefono { get; set; } // Teléfono
    }
}