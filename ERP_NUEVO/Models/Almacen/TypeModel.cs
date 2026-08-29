using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class TypeModel
    {
        [Required(ErrorMessage = "La clave es requerida")]
        [StringLength(2, ErrorMessage = "La clave no puede exceder 2 caracteres")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }

        [Required(ErrorMessage = "La descripción es requerida")]
        [StringLength(30, ErrorMessage = "La descripción no puede exceder 30 caracteres")]
        public string Description { get; set; }

        [Required(ErrorMessage = "El estado es requerido")]
        public int Status { get; set; }
    }
}