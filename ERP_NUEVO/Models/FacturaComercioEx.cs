using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace BOS_ERP.Models
{
    public class FacturaComercioEx
    {
        public int IdFactura { get; set; } = 0;
        public string RfcCliente { get; set; } = string.Empty;
        public string Serie { get; set; } = string.Empty;
        public string Folio { get; set; } = string.Empty;
        public string RsoCliente { get; set; } = string.Empty;
        public string EmlCliente { get; set; } = string.Empty;
        public int IdEmisor { get; set; } = 0;
        public string RfcEmisor { get; set; } = string.Empty;
        public string RsoEmisor { get; set; } = string.Empty;
        public int IdExpedicion { get; set; } = 0;
        public int IdUsuario { get; set; } = 0;
        public int IdCliente { get; set; } = 0;
        public DateTime Fecha { get; set; } = DateTime.Now;
        public string FechaTimbrado { get; set; } = string.Empty;
        public int StatusFactura { get; set; } = 0;
        public string MdpFactura { get; set; } = "SIN IDENTIFICAR";
        public string XmlFactura { get; set; } = string.Empty;
        public int IdLugarExp { get; set; } = 0;
        public string IdTipoPago { get; set; } = string.Empty;
        public int IdTipoFactura { get; set; } = 0;
        public string Moneda { get; set; } = string.Empty;
        public string UUID { get; set; } = string.Empty;
        public bool Completa { get; set; } = false;
        public double Importe { get; set; } = 0.0;
        public double Descuento { get; set; } = 0.0;
        public double Subtotal { get; set; } = 0.0;
        public double IVA { get; set; } = 0.0;
        public double IepsF { get; set; } = 0.0;
        public double Total { get; set; } = 0.0;
        public double Saldo { get; set; } = 0.0;
        public int IdPedido { get; set; } = 0;
        public double RetISR { get; set; } = 0.0;
        public double RetIVA { get; set; } = 0.0;
        public double TipoCambio { get; set; } = 0.0;
        public string UsoCFDI { get; set; } = string.Empty;
        public string IdUsoCFDI { get; set; } = string.Empty;
        public int IdVendedor { get; set; } = 0;
        public bool Resultado { get; set; } = false;
        public string Observaciones { get; set; } = string.Empty;

        public string SelloSAT { get; set; } = string.Empty;
        public string SelloCFDI { get; set; } = string.Empty;
        public string CadenaOriginal { get; set; } = string.Empty;
        public int Origen { get; set; } = 1;
        public string FormaPago { get; set; } = string.Empty;
        public string TipoFactura { get; set; } = string.Empty;

        // DATOS DE PRODUCTOS PARA REPORTES
        public string Descripcion { get; set; } = string.Empty;
        public string Clave { get; set; } = string.Empty;
        public double Cantidad { get; set; } = 0.0;
        public double Tdc { get; set; } = 0.0;
        public double Precio { get; set; } = 0.0;
        public double ImporteP { get; set; } = 0.0;
        public double DescP { get; set; } = 0.0;
        public double SubtP { get; set; } = 0.0;
        public double Tndc { get; set; } = 0.0;
        public double IvaP { get; set; } = 0.0;
        public double IepsP { get; set; } = 0.0;
        public string Udm { get; set; } = string.Empty;

        public bool Dependencia { get; set; } = false;
        public int IdDependencia { get; set; } = 0;
        public int Globall { get; set; } = 0;
        public string Oc { get; set; } = string.Empty; // Orden de compra (no obligatorio)
        public bool Anticipo { get; set; } = false;
        public DataTable Pagos { get; set; } = new DataTable();
        public int Addend { get; set; } = 0;

        // PARA CBB
        public double Tasa0 { get; set; } = 0.00;
        public double Tasa8 { get; set; } = 0.00;
        public double Tasa16 { get; set; } = 0.00;
        public string Cbb { get; set; } = string.Empty;
        public DataTable DtP { get; set; } = new DataTable();
        public string Caja { get; set; } = string.Empty;
        public int Torigen { get; set; } = 0;
        public int Pgeneral { get; set; } = 0;
        public int IdFCliente { get; set; } = 0;
        public string Xml { get; set; } = string.Empty;

        // NUEVOS CAMPOS PARA MOTIVOS DE CANCELACIÓN
        public string FCancelacion { get; set; } = string.Empty;
        public int IdUCancelacion { get; set; } = 0;
        public string MCancelacion { get; set; } = string.Empty;
        public string NuevoFolio { get; set; } = string.Empty;

        // PARA REPORTE EN CFDI 4.0
        public string Rege { get; set; } = string.Empty; // Régimen fiscal del emisor
        public string Regc { get; set; } = string.Empty; // Régimen fiscal del receptor

        // VARIABLES NUEVAS PARA CFDI 4.0
        public string Periodicidad { get; set; } = "01";
        public string Exportacion { get; set; } = "02";
        public string RegFisE { get; set; } = string.Empty;
        public string RegFisR { get; set; } = string.Empty;
        public string CpR { get; set; } = string.Empty; // CP del receptor
        public string CpE { get; set; } = string.Empty; // CP del Emisor

        public string UuidRel { get; set; } = "-";
        public string DesRel { get; set; } = string.Empty;
        public string CveRel { get; set; } = string.Empty;

        // AGREGANDO TABLA PARA DOCUMENTOS RELACIONADOS
        public DataTable Dtfrel { get; set; } = new DataTable();
        public double MtoEgreso { get; set; } = 0.00;
        public DataTable Tproductos { get; set; } = new DataTable();

        //Comercio exterior
        public string MotivoTraslado { get; set; } = string.Empty;
        public string ClavePedimento { get; set; } = string.Empty;
        public string Incoterm { get; set; } = string.Empty;
        public double TipoCambioUSD { get; set; } = 0.0;
        public double TotalUSD { get; set; } = 0.0;
        public string NumRegIdTribDestinatario { get; set; } = string.Empty;
        public string NombreDestinatario { get; set; } = string.Empty;
        public string CalleDestinatario { get; set; } = string.Empty;
        public string PaisDestinatario { get; set; } = string.Empty;
        public void CalculaTotal()
        {
            Subtotal = 0;
            IVA = 0;
            Total = 0;

            foreach (DataRow fila in Tproductos.Rows)
            {
                if (fila["importe"] != DBNull.Value)
                    Subtotal += Convert.ToDouble(fila["importe"]);

                if (fila["iva"] != DBNull.Value)
                    IVA += Convert.ToDouble(fila["iva"]);
            }

            Total = Subtotal + IVA;

            if (IdTipoPago == "99")
            {
                Saldo = Total;
            }
        }
    }
}