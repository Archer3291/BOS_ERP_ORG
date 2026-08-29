// Models/Incoterm.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class IncotermModel
    {
        [Required]
        [StringLength(5, ErrorMessage = "La clave no puede exceder 5 caracteres.")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "La descripción no puede exceder 100 caracteres.")]
        [Display(Name = "Descripción")]
        public string Descripcion { get; set; }

        [Display(Name = "Fecha")]
        public DateTime c3 { get; set; }
    }
}