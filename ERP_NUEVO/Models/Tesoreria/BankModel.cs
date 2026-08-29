using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace BOS_ERP.Models
{
    public class BankModel
    {
        [Required(ErrorMessage = "La clave del banco es requerida")]
        [StringLength(7, ErrorMessage = "La clave no puede exceder 7 caracteres")]
        [Display(Name = "Clave")]
        public string Clave { get; set; }

        [Required(ErrorMessage = "El nombre del banco es requerido")]
        [StringLength(30, ErrorMessage = "El nombre no puede exceder 30 caracteres")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "La cuenta del banco es requerida")]
        [StringLength(30, ErrorMessage = "La cuenta no puede exceder 30 caracteres")]
        public string CuentaBancaria { get; set; }

        [Required(ErrorMessage = "La moneda es requerida")]
        [Display(Name = "Moneda")]
        public string Moneda { get; set; }  // Almacenará c1 de kdmy
        // Propiedad para el dropdown
        public List<SelectListItem> Monedas { get; set; }

        [Required(ErrorMessage = "El número de cuenta es requerido")]
        [StringLength(20, ErrorMessage = "El número de cuenta no puede exceder 20 caracteres")]
        public string NumeroCuenta { get; set; }

        [Required(ErrorMessage = "El RFC es requerido")]
        [StringLength(13, ErrorMessage = "El RFC no puede exceder 13 caracteres")]
        public string RFC { get; set; }

        [StringLength(1)]
        public string Saldo {  get; set; }
    }
}