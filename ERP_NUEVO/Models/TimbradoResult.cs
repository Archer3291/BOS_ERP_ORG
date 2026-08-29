// Models/Rol.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class TimbradoResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string UUID { get; set; }
        public string FolioNC { get; set; }
        public bool PdfGenerado { get; set; } = false;
    }
}