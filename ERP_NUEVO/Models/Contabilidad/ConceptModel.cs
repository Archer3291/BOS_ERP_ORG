using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class ConceptModel
    {
        [Required(ErrorMessage = "La clave es requerida")]
        [StringLength(10, ErrorMessage = "La clave no puede exceder 10 caracteres")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }

        [Required(ErrorMessage = "La descripción es requerida")]
        [StringLength(30, ErrorMessage = "La descripción no puede exceder 30 caracteres")]
        public string Descripcion { get; set; }

        [Required(ErrorMessage = "El número de cuenta es requerido")]
        [StringLength(20, ErrorMessage = "El número de cuenta no puede exceder 20 caracteres")]
        public string NumeroCuenta { get; set; }
    }
}