using System;
using System.Collections.Generic;

namespace BOS_ERP.Models
{
    public class CuentaResumen
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public decimal SaldoInicial { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
        public decimal SaldoFinal { get; set; }
        public bool EsPadre { get; set; }

        public List<CuentaDetalle> Detalle { get; set; } = new List<CuentaDetalle>();
    }

    public class CuentaDetalle
    {
        public int CuentaId { get; set; }
        public string Poliza { get; set; }
        public int? PolizaId { get; set; }
        public string Documento { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
        public string Descripcion { get; set; }
        public string Clasificacion { get; set; }
        public DateTime Fecha { get; set; }
    }
}