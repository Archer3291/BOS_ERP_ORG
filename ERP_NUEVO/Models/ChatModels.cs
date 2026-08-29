// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BOS_ERP.Models
{

    public class Conversacion
    {
        public int ConversacionId { get; set; }
        public int UsuarioId { get; set; }
        public string? Titulo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaUltimaActividad { get; set; }
        public bool Activa { get; set; }
    }

    public class MensajeChat
    {
        public long MensajeId { get; set; }
        public int ConversacionId { get; set; }
        public int UsuarioId { get; set; }
        public string Rol { get; set; } = string.Empty; // user | assistant | system
        public string Contenido { get; set; } = string.Empty;
        public DateTime FechaCreacion { get; set; }
        public bool MarcadoRevision { get; set; }
    }

    // DTO que llega desde el frontend
    public class ConsultaRequest
    {
        public string Pregunta { get; set; } = string.Empty;
        public int? ConversacionId { get; set; } // null = crea una nueva
    }

    // DTO de usuario resuelto desde usuarios
    public class UsuarioActual
    {
        public int UsuarioId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string NombreUsuario { get; set; } = string.Empty;
        public int RolId { get; set; }
    }

}


