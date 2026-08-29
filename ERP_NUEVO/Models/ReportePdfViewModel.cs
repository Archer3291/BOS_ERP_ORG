using BOS_ERP.Controllers;
using System;
using System.Collections.Generic;

namespace BOS_ERP.Models
{
    public class ReportePdfViewModel
    {
        public string Titulo { get; set; }
        public DateTime FechaGen { get; set; }
        public string Modo { get; set; }   // "facturas" | "clientes"
        public string FiltrosAplicados { get; set; }
        public ReporteKpis KpisGlobal { get; set; }
        public List<ReporteSeccion> Secciones { get; set; }

        // Columnas seleccionadas por el usuario — la vista itera esta lista
        public List<CarterasController.ReporteColumnDef> ColDefs { get; set; }
    }

}