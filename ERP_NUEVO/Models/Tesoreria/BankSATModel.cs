using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class BankSATModel
    {
        [Required(ErrorMessage = "La clave es requerida")]
        [StringLength(3, ErrorMessage = "La clave no puede exceder 3 caracteres")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }

        [Required(ErrorMessage = "El nombre es requerida")]
        [StringLength(50, ErrorMessage = "El nombre no puede exceder 50 caracteres")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "La Razón Social es requerida")]
        [StringLength(200, ErrorMessage = "La Razón Social no puede exceder 200 caracteres")]
        public string RazonSocial { get; set; }
    }
}