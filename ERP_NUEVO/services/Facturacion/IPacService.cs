namespace BOS_ERP.services.Facturacion
{
    public class TimbradoPacResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string UUID { get; set; }
        public string XmlTimbrado { get; set; }
        public string RutaXmlLocal { get; set; }
        public string RutaQr { get; set; }
    }

    public class CancelacionPacResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string AcuseXml { get; set; }
        public string RutaAcuse { get; set; }
    }

    public interface IPacService
    {
        // Recibe el XML ya armado (sin timbrar) y regresa el resultado del PAC
        Task<TimbradoPacResult> TimbrarAsync(string xmlSinTimbrar, string uuidTemporal);

        Task<CancelacionPacResult> CancelarAsync(string rfcEmisor, string uuid, string motivo, string folioSustitucion = "");
    }
}