using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.Core.Objects.DataClasses;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Models
{

    [Table("clientes", Schema = "srs")]
    public class Cliente
    {
        [Column("clienteid")]
        public int ClienteId { get; set; }

        [Required(ErrorMessage = "La cédula es requerida")]
        [StringLength(20, ErrorMessage = "La cédula no puede exceder 20 caracteres")]
        [Remote("VerificarCedula", "Clientes", AdditionalFields = "ClienteId", ErrorMessage = "Esta cédula ya está registrada")]
        [Column("cedula")]
        public string Cedula { get; set; }

        [Column("nombre")]
        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
        public string Nombre { get; set; }

        [Column("apellido")]
        [Required(ErrorMessage = "El apellido es requerido")]
        [StringLength(100, ErrorMessage = "El apellido no puede exceder 100 caracteres")]
        public string Apellido { get; set; }

        [Column("direccion")]
        [StringLength(255, ErrorMessage = "La dirección no puede exceder 255 caracteres")]
        public string Direccion { get; set; }

        [Column("telefono")]
        [StringLength(20, ErrorMessage = "El teléfono no puede exceder 20 caracteres")]
        public string Telefono { get; set; }

        [Column("email")]
        [EmailAddress(ErrorMessage = "Ingrese un email válido")]
        [StringLength(100, ErrorMessage = "El email no puede exceder 100 caracteres")]
        public string Email { get; set; }

        [Column("fecharegistro")]
        public DateTime FechaRegistro { get; set; }

        [Column("estado")]
        public bool Estado { get; set; }
    }
}