using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Models
{
    public class ContactoProveedorModel
    {

            public int? IdContactosProv { get; set; }
            public string Nombre { get; set; }
            public string Puesto { get; set; }
            public string Comentarios { get; set; }
            public string ProvId { get; set; }
        
    }
}