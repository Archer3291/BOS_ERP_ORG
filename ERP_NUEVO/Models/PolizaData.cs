using System;
using System.Collections.Generic;

namespace BOS_ERP.Models
{
    public class PolizaData
    {
        public int? Tipo { get; set; }
        public string Estado { get; set; }
        public string Descripcion { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public int? IdEncabezado { get; set; }
        public bool EsManual { get; set; } = false;
        public int Categoria { get; set; }
        public List<PolizaDetalle> Detalles { get; set; } = new List<PolizaDetalle>();
    }

}