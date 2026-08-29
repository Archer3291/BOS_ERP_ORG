using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class FilaProcesada
    {
        public List<string> General { get; set; }
        public List<string> Proveedor { get; set; }
        public List<string> ProductoSAT { get; set; }
    }

    public class VistaExcelViewModel
    {
        public List<string> EncabezadosGeneral { get; set; }
        public List<string> EncabezadosProveedor { get; set; }
        public List<string> EncabezadosProductoSat { get; set; }

        public List<List<string>> General { get; set; }
        public List<List<string>> Proveedor { get; set; }
        public List<List<string>> ProductoSAT { get; set; }

        public List<FilaProcesada> ProductosNuevos { get; set; }
        public List<FilaProcesada> ProductosConProveedorInexistente { get; set; }
        public List<FilaProcesada> ProductosYProveedoresExistentes { get; set; }
        public List<List<string>> ProveedoresNuevos { get; set; }
    }


}
