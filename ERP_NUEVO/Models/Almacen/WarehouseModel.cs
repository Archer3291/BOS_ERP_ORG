using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class WarehouseModel
    {
        [Required(ErrorMessage = "La clave es requerida")]
        [StringLength(10, ErrorMessage = "La clave no puede exceder 10 caracteres")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }
        [Required(ErrorMessage = "La sucursal es requerida")]
        [StringLength(10, ErrorMessage = "La sucursal no puede exceder 10 caracteres")]
        [Display(Name = "Sucursal")]
        public string Sucursal { get; set; }

        [Required(ErrorMessage = "La descripción es requerida")]
        [StringLength(30, ErrorMessage = "La descripción no puede exceder 30 caracteres")]
        public string Description { get; set; }
    }
}