// Models/Incoterm.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class DepartmentModel
    {
        public int id { get; set; }
        public string nombre { get; set; }
        public string descripcion { get; set; }
        public string abreviatura { get; set; }
    }
}