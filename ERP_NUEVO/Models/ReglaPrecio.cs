// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class ReglaPrecio
    {
        public int id { get; set; }
        public int empresa_id { get; set; }
        public int? cliente_id { get; set; }
        public string cve_prod { get; set; }
        public string lin_prod { get; set; }
        public string gpo { get; set; }
        public string tp { get; set; }
        public string tipo_regla { get; set; }   // 'DESCUENTO' | 'PRECIO_FIJO'
        public decimal valor { get; set; }
        public decimal? max_descuento { get; set; }
        public int prioridad { get; set; } = 10;
        public bool activo { get; set; } = true;
        public DateTime? fecha_inicio { get; set; }
        public DateTime? fecha_fin { get; set; }
        public string nombre_promocion { get; set; }
        public bool es_promocion { get; set; }
        public bool aplicar_automatico { get; set; } = true;
    }
}