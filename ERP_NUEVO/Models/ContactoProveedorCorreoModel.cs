using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Models
{
    public class ContactoProveedorCorreoModel
    {
            public int IdCorreo { get; set; }
            public int IdContactosProv { get; set; }
            [Required]
            public string Correo { get; set; }
        
    }
}