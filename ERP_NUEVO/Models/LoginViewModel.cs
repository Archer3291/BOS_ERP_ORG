// Models/LoginViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
        [Display(Name = "Nombre de Usuario")]
        public string NombreUsuario { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Contrasena { get; set; }

        [Display(Name = "Recordarme")]
        public bool Recordarme { get; set; }
    }
}