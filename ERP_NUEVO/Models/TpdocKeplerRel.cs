// Models/Linea.cs
using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class TpdocKeplerRelModel
    {
        public int Idrel { get; set; }
        public int Idtpdoc { get; set; }
        public int IdDoc { get; set; }
        public DateTime FechaCreacion { get; set; }
    }
}