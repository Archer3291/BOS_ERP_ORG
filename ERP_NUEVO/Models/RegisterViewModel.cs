using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CompareAttribute = System.ComponentModel.DataAnnotations.CompareAttribute;
namespace BOS_ERP.Models
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
        [Display(Name = "Nombre de usuario")]
        public string NombreUsuario { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Contrasena { get; set; }

        [Required(ErrorMessage = "Confirme su contraseña")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        [Compare("Contrasena", ErrorMessage = "Las contraseñas no coinciden")]
        public string ConfirmarContrasena { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "El apellido es obligatorio")]
        [Display(Name = "Apellido")]
        public string Apellido { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio")]
        [EmailAddress(ErrorMessage = "El correo electrónico no es válido")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio")]
        [EmailAddress(ErrorMessage = "El correo electrónico no es válido")]
        [Display(Name = "SuperiorEmail")]
        public string SuperiorEmail { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un rol")]
        [Display(Name = "Rol")]
        public int RolId { get; set; }

        // ====== CAMPOS DEL FORM ======

        [Required(ErrorMessage = "Debe seleccionar una empresa")]
        [Display(Name = "Empresa")]
        public int EmpresaId { get; set; }

        [Required(ErrorMessage = "Debe seleccionar una sucursal")]
        [Display(Name = "Sucursal")]
        public int Id_sucursal { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un area")]
        [Display(Name = "Area")]
        public int Id_Area { get; set; }

        // ====== LISTAS PARA LOS SELECT ======

        [ValidateNever]
        public List<SelectListItem> Empresas { get; set; } = [];

        [ValidateNever]
        public List<SelectListItem> Sucursales { get; set; } = [];

        [ValidateNever]
        public List<SelectListItem> Areas { get; set; } = [];



    }
}
