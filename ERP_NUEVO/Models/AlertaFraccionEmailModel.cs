// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class AlertaFraccionEmailModel
    {
        public string Folio { get; set; }
        public string Cliente { get; set; }
        public string UsuarioQueValido { get; set; }
        public string FechaValidacion { get; set; }
        public List<ProductoFraccionInvalido> ProductosSinFraccion { get; set; }
    }

    public class ProductoFraccionInvalido
    {
        public string ProductoId { get; set; }
        public string Descripcion { get; set; }
        // Puedes agregar más campos si necesitas mostrarlos en el correo
    }

}