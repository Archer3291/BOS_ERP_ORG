// Models/Linea.cs
using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("areas")]
    public class Area
    {
        [Key]
        [Column("areaid")]
        public int Id_area { get; set; }

        [Column("nombre")]
        public string Nombre { get; set; }

        [StringLength(200)]
        [Column("descripcion")]
        public string Descripcion { get; set; }

        [Column("abreviatura")]
        public string Abreviatura { get; set; }

        // Propiedad calculada para mostrar en el dropdown
        [NotMapped]
        public string Nomb
        {
            get { return $"{Id_area} - {Descripcion}"; }
        }
    }
}