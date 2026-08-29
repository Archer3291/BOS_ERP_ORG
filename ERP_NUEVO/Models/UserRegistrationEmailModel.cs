using System;

namespace BOS_ERP.Models
{
    public class UserRegistrationEmailModel
    {
        public string NombreUsuario { get; set; }
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Email { get; set; }
        public string SuperiorEmail { get; set; }
        public string EmpresaNombre { get; set; }
        public int RolId { get; set; }
        public string RolNombre => RolId == 1 ? "Administrador" : "Usuario";
        public string UrlActivacion { get; set; }
        public int CurrentYear => DateTime.Now.Year;
    }
}