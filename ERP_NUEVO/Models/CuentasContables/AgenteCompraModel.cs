using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class AgenteCompraModel
    {
        [Required(ErrorMessage = "La clave es requerida")]
        [StringLength(5, ErrorMessage = "La clave no puede exceder 5 caracteres")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }

        [Required(ErrorMessage = "La descripción es requerida")]
        [StringLength(30, ErrorMessage = "La descripción no puede exceder 30 caracteres")]
        public string Descripcion { get; set; }
    }
}