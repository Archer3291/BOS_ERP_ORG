// Models/Linea.cs
using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Models
{
    public class Linea
    {
        public int idlinea { get; set; }

        [Required(ErrorMessage = "La clave de línea es requerida")]
        [StringLength(4, ErrorMessage = "La clave no puede exceder 4 caracteres")]
        [Remote("ValidarClaveUnica", "Lineas", AdditionalFields = "idlinea", ErrorMessage = "Esta clave ya está en uso")]
        public string clavelinea { get; set; }

        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
        [Remote("ValidarNombreUnico", "Lineas", AdditionalFields = "idlinea", ErrorMessage = "Este nombre ya está en uso")]
        public string nombre { get; set; }

        public string descripcion { get; set; }

        [Required(ErrorMessage = "El estado activo es requerido")]
        public short activo { get; set; }
    }
}