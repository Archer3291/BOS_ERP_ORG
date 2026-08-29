using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Models
{
    public class UsuarioEditVM
    {
        public int UsuarioId { get; set; }
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Email { get; set; }
        public string NombreUsuario { get; set; }
        public string Telefono { get; set; }
        public bool Activo { get; set; }
        public int RolId { get; set; }   
        public int EmpresaId { get; set; }
        public int SucursalId { get; set; }
        public int Area { get; set; }

        public List<int> Permisos { get; set; }
    }
}