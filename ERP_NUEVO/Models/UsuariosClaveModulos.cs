// Models/Rol.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using Npgsql;
namespace BOS_ERP.Models
{
    public class ClaveModulo
    {
        public int Id { get; set; }
        public string ModuloId { get; set; }
        public string Nombre { get; set; }
        public string PasswordHash { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public int TotalUsuarios { get; set; }   // calculado
    }

    public class ClaveModuloVM
    {
        public int Id { get; set; }
        [Required] public string ModuloId { get; set; }
        [Required] public string Nombre { get; set; }
        [MinLength(8)] public string Password { get; set; }
        public bool Activo { get; set; }
    }

    public class ClaveUsuarioAsignacion
    {
        public int Id { get; set; }
        public int ClaveId { get; set; }
        public int UsuarioId { get; set; }
        public string NombreUsuario { get; set; }
        public bool Activo { get; set; }
        public int IntentosFallidos { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
        public DateTime FechaAsignacion { get; set; }
    }

    public class ValidacionResultado
    {
        public bool Exito { get; }
        public string Mensaje { get; }
        public ValidacionResultado(bool exito, string mensaje)
        { Exito = exito; Mensaje = mensaje; }
    }
}

