// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class AcuseCancelacion
    {
        public string Uuid { get; set; }
        public string RfcEmisor { get; set; }
        public string RfcReceptor { get; set; }
        public string Motivo { get; set; }
        public string FolioSustitucion { get; set; }
        public DateTime FechaCancelacion { get; set; }
        public string Estatus { get; set; }
        public string SelloSAT { get; set; }
        public string NoCertificadoSAT { get; set; } // ← nuevo
        public string RfcProvCertif { get; set; }
        public string RutaQr { get; set; }
        public string XmlAcuseRaw { get; set; }
    }
}