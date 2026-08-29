using System;
using System.Collections.Generic;

namespace BOS_ERP.Models
{
    // Models/ModulaAuditoria.cs
    public class ModulaAuditoria
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string TipoAccion { get; set; }
        public string Origen { get; set; }
        public string CodigoArticulo { get; set; }
        public string OrdenNumero { get; set; }
        public decimal? Cantidad { get; set; }
        public string PayloadJson { get; set; }
        public string WsStatus { get; set; }
        public string WsRespuesta { get; set; }
        public int TotalRegistros { get; set; }
    }

    // Models/ModulaAuditoriaFiltro.cs  (para la pantalla)
    public class ModulaAuditoriaFiltro
    {
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string Usuario { get; set; }
        public string TipoAccion { get; set; }
        public string WsStatus { get; set; }
        public List<ModulaAuditoria> Registros { get; set; } = new List<ModulaAuditoria>();
    }
}
