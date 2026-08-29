using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class AgencyModel
    {
        [Required(ErrorMessage = "La clave es requerida")]
        [StringLength(7, ErrorMessage = "La clave no puede exceder 7 caracteres")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }

        [Required(ErrorMessage = "La descripción es requerida")]
        [StringLength(30, ErrorMessage = "La descripción no puede exceder 30 caracteres")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "La calle es requerida")]
        [StringLength(40, ErrorMessage = "La calle no puede exceder 40 caracteres")]
        public string Calle { get; set; }

        [Required(ErrorMessage = "La colonia es requerida")]
        [StringLength(40, ErrorMessage = "La colonia no puede exceder 40 caracteres")]
        public string Colonia { get; set; }

        [Required(ErrorMessage = "La población es requerida")]
        [StringLength(40, ErrorMessage = "La población no puede exceder 40 caracteres")]
        public string Poblacion { get; set; }
        
        [StringLength(14, ErrorMessage = "El teléfono no puede exceder 14 caracteres")]
        public string Telefono { get; set; }
       
        [StringLength(14, ErrorMessage = "El teléfono no puede exceder 14 caracteres")]
        public string Telefono2 { get; set; }
        
        [StringLength(14, ErrorMessage = "El teléfono no puede exceder 14 caracteres")]
        public string Fax { get; set; }
    }
}