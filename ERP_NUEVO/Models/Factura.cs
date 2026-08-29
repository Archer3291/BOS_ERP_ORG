using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace BOS_ERP.Models
{
    public class Factura
    {
        public int IdFactura { get; set; } = 0;
        public int EncabezadoId { get; set; } = 0;
        public string RfcCliente { get; set; } = string.Empty;
        public string Serie { get; set; } = string.Empty;
        public string Folio { get; set; } = string.Empty;
        public string FolioCorto { get; set; } = string.Empty;
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
        public string StatusFactura { get; set; } = "TIMBRADA";
        public string MdpFactura { get; set; } = string.Empty;
        public string XmlFactura { get; set; } = string.Empty;
        public int IdLugarExp { get; set; } = 0;
        public string IdTipoPago { get; set; } = string.Empty;
        public int IdTipoFactura { get; set; } = 0;
        public string Moneda { get; set; } = string.Empty;
        public string UUID { get; set; } = string.Empty;
        public bool Completa { get; set; } = false;
        public decimal Importe { get; set; } = 0.0m;
        public decimal Descuento { get; set; } = 0.0m;
        public decimal Subtotal { get; set; } = 0.0m;
        public decimal Subtotal2 { get; set; } = 0.0m;
        public decimal IVA { get; set; } = 0.0m;
        public decimal IepsF { get; set; } = 0.0m;
        public decimal Total { get; set; } = 0.0m;
        public decimal Saldo { get; set; } = 0.0m;
        public int IdPedido { get; set; } = 0;
        public decimal RetISR { get; set; } = 0.0m;
        public decimal RetIVA { get; set; } = 0.0m;
        public decimal TipoCambio { get; set; } = 0.0m;
        public decimal Flete { get; set; } = 0.0m;
        public string UsoCFDI { get; set; } = string.Empty;
        public string IdUsoCFDI { get; set; } = string.Empty;
        public int IdVendedor { get; set; } = 0;
        public bool Resultado { get; set; } = false;
        public string Observaciones { get; set; } = string.Empty;

        public string SelloSAT { get; set; } = string.Empty;
        public string SelloCFDI { get; set; } = string.Empty;
        public string CadenaOriginal { get; set; } = string.Empty;
        public string Certificado { get; set; } = string.Empty;
        public int Origen { get; set; } = 1;
        public string FormaPago { get; set; } = string.Empty;
        public string TipoFactura { get; set; } = string.Empty;

        // DATOS DE PRODUCTOS PARA REPORTES
        public string Descripcion { get; set; } = string.Empty;
        public string Clave { get; set; } = string.Empty;
        public decimal Cantidad { get; set; } = 0.0m;
        public decimal Tdc { get; set; } = 0.0m;
        public decimal Precio { get; set; } = 0.0m;
        public decimal ImporteP { get; set; } = 0.0m;
        public decimal DescP { get; set; } = 0.0m;
        public decimal SubtP { get; set; } = 0.0m;
        public decimal Tndc { get; set; } = 0.0m;
        public decimal IvaP { get; set; } = 0.0m;
        public decimal IepsP { get; set; } = 0.0m;
        public decimal MontoAnticipo { get; set; } = 0.0m;
        public string Udm { get; set; } = string.Empty;

        public bool Dependencia { get; set; } = false;
        public int IdDependencia { get; set; } = 0;
        public int Globall { get; set; } = 0;
        public string Oc { get; set; } = string.Empty; // Orden de compra (no obligatorio)
        public string TipoFacturacion { get; set; } = string.Empty;
        public bool Anticipo { get; set; } = false;
        public DataTable Pagos { get; set; } = new DataTable();
        public int Addend { get; set; } = 0;

        // PARA CBB
        public decimal Tasa0 { get; set; } = 0.00m;
        public decimal Tasa8 { get; set; } = 0.00m;
        public decimal Tasa16 { get; set; } = 0.00m;
        public string Cbb { get; set; } = string.Empty;
        public DataTable DtP { get; set; } = new DataTable();
        public string Caja { get; set; } = string.Empty;
        public int Torigen { get; set; } = 0;
        public int Pgeneral { get; set; } = 0;
        public int IdFCliente { get; set; } = 0;
        public string Xml { get; set; } = string.Empty;

        // NUEVOS CAMPOS PARA MOTIVOS DE CANCELACI�N
        public string FCancelacion { get; set; } = string.Empty;
        public int IdUCancelacion { get; set; } = 0;
        public string MCancelacion { get; set; } = string.Empty;
        public string NuevoFolio { get; set; } = string.Empty;

        // PARA REPORTE EN CFDI 4.0
        public string Rege { get; set; } = string.Empty; // R�gimen fiscal del emisor
        public string Regc { get; set; } = string.Empty; // R�gimen fiscal del receptor

        // VARIABLES NUEVAS PARA CFDI 4.0
        // Periodicidad aplica a la factura global (TipoFacturacion = "global"):
        // 01=Diario, 02=Semanal, 03=Quincenal, 04=Mensual, 05=Bimestral.
        public string Periodicidad { get; set; } = "01";
        public string Exportacion { get; set; } = "01";
        public string RegFisE { get; set; } = string.Empty;
        public string RegFisR { get; set; } = string.Empty;
        public string CpR { get; set; } = string.Empty; // CP del receptor
        public string CpE { get; set; } = string.Empty; // CP del Emisor
        public string RutaQr { get; set; }

        public string UuidRel { get; set; } = "-";
        public string DesRel { get; set; } = string.Empty;
        public string CveRel { get; set; } = string.Empty;
        public string TipoDeComprobante { get; set; } = string.Empty;
        public string LugarExpedicion { get; set; } = string.Empty;
        public string formaPagoTexto { get; set; } = string.Empty;
        public string metodoPagoTexto { get; set; } = string.Empty;
        public string regimenEText { get; set; } = string.Empty;
        public string CFDIText { get; set; } = string.Empty;
        public string NoCertificado { get; set; } = string.Empty;
        public string NoCertificadoSAT { get; set; } = string.Empty;
        public string TotalTexto { get; set; } = string.Empty;
        public string OrdenCompra { get; set; } = string.Empty;
        public string TipoRelacion { get; set; } = string.Empty; // Ejemplo: "07" - Aplicaci�n de anticipos
        public string UUIDsRelacionados { get; set; } = string.Empty; // Lista separada por comas de UUIDs relacionados

        // Direcci�n del receptor para PDF
        public string DireccionReceptor { get; set; }

        // AGREGANDO TABLA PARA DOCUMENTOS RELACIONADOS
        public DataTable Dtfrel { get; set; } = new DataTable();
        public decimal MtoEgreso { get; set; } = 0.00m;
        public DataTable Tproductos { get; set; } = new DataTable();
        public DataTable TAnticipos { get; set; } = new DataTable();

        public Addenda Addenda { get; set; } = new Addenda();
        public void CalculaTotal()
        {
            Subtotal = 0;
            IVA = 0;
            Total = 0;

            foreach (DataRow fila in Tproductos.Rows)
            {
                if (fila["importe"] != DBNull.Value)
                    Subtotal += Convert.ToDecimal(fila["importe"]);

                if (fila["iva"] != DBNull.Value)
                    IVA += Convert.ToDecimal(fila["iva"]);
            }

            Total = Subtotal + IVA;

            if (IdTipoPago == "99")
            {
                Saldo = Total;
            }
        }

        public void CalculaTotalPredial()
        {
            Subtotal = 0;
            IVA = 0;
            Total = 0;

            foreach (DataRow fila in Tproductos.Rows)
            {
                decimal importe = fila["importe"] != DBNull.Value ? Convert.ToDecimal(fila["importe"]) : 0;
                Subtotal += importe;

                // Calcular IVA solo si el objetoImp no es "01 - Exento"
                string objeto = fila["objetoImp"]?.ToString() ?? "";
                if (!objeto.StartsWith("01")) // exento
                {
                    IVA += Math.Round(importe * 0.16m, 2); // 16% de IVA
                }
            }

            Total = Subtotal + IVA;

            if (IdTipoPago == "99")
            {
                Saldo = Total;
            }
        }
        public ComercioExterior ComercioExterior { get; set; } = null;

        // XML crudo del nodo cce20:ComercioExterior tal cual venía en el CFDI original timbrado.
        // Se reusa VERBATIM al reconstruir el XML para refacturar (evita perder atributos/subnodos
        // que un re-armado campo por campo podría omitir). Lo puebla FacturaBuilder.
        public string ComercioExteriorXml { get; set; }

    }
}
// Agrega estos modelos (o reemplaza los existentes) en tu namespace Modelo
public class ComercioExterior
{
    // Atributos generales del complemento (V2.0)
    public string Version { get; set; } = "2.0";
    public string MotivoTraslado { get; set; } = string.Empty; // opcional/condicional seg�n caso
    public string ClaveDePedimento { get; set; } = string.Empty;
    public string CertificadoOrigen { get; set; } = "0"; // 0 = no
    public string NumCertificadoOrigen { get; set; } = string.Empty;
    public string NumeroExportadorConfiable { get; set; } = string.Empty;
    public string Incoterm { get; set; } = string.Empty; // obligatorio en 2.0 (cat�logo INCOTERM)
    public decimal TipoCambioUSD { get; set; } = 0.0m; // Tipo de cambio usado para USD
    public decimal TotalUSD { get; set; } = 0; // Total en USD (obligatorio en 2.0)

    // Domicilio del emisor (puedes ampliar con m�s atributos del XSD si necesitas)
    public DomicilioComExt DomicilioEmisor { get; set; } = new DomicilioComExt();

    // Datos del destinatario/extranjero
    public string NumRegIdTrib { get; set; } = string.Empty; // registro fiscal del receptor
    public DomicilioComExt DomicilioDestinatario { get; set; } = new DomicilioComExt();

    // Lista de mercanc�as exportadas
    public List<MercanciaExportada> Mercancias { get; set; } = new List<MercanciaExportada>();
    public List<Propietario> Propietarios { get; set; } = new List<Propietario>();

}

public class DomicilioComExt
{
    public string Calle { get; set; } = string.Empty;
    public string NumeroExterior { get; set; } = string.Empty;
    public string Colonia { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Municipio { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty; // ISO 3166-2 (o clave cat�logo)
    public string Pais { get; set; } = string.Empty; // ISO 3166-1 (ej. "USA")
    public string CodigoPostal { get; set; } = string.Empty;
}

public class MercanciaExportada
{
    public string NoIdentificacion { get; set; } = string.Empty;
    public string FraccionArancelaria { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal CantidadAduana { get; set; } = 0m;
    public string UnidadAduana { get; set; } = "01"; // cat�logo de unidades
    public decimal ValorUnitarioAduana { get; set; } = 0m;
    public decimal ValorDolares { get; set; } = 0m;
    public string Unidad { get; set; } = string.Empty; // unidad comercial si la tuvieras
    public decimal Cantidad { get; set; } = 0m; // cantidad comercial
    public List<Pedimento> Pedimentos { get; set; } = new List<Pedimento>();
}
public class Pedimento
{
    // N�mero completo del pedimento (18 d�gitos con espacios)
    // Ejemplo: "25 48 3000 0001234"
    public string Numero { get; set; } = string.Empty;
}
public class Propietario
{
    public string NumRegIdTrib { get; set; }
    public string ResidenciaFiscal { get; set; }
}

public class PagoComplemento
{
    public DateTime FechaPago { get; set; }
    public string FormaPago { get; set; } // 03, 01, 28, etc.
    public string Moneda { get; set; }
    public decimal Monto { get; set; }
    public decimal TipoCambio { get; set; }

    public List<DocumentoPagado> Documentos { get; set; } = new List<DocumentoPagado>();

}

public class DocumentoPagado
{
    public string IdDocumento { get; set; }      // UUID CFDI relacionado
    public string Serie { get; set; }
    public string Folio { get; set; }
    public string MonedaDR { get; set; }
    public string ObjetoImpDR { get; set; } = "01";
    public decimal ImpSaldoAnt { get; set; }
    public decimal ImpPagado { get; set; }
    public decimal? ImpSaldoInsoluto { get; set; }
    public string MetodoPagoDR { get; set; } = "PPD";
}

public class AddendaNode
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    public List<AddendaNode> Children { get; set; } = new List<AddendaNode>();
    public string Value { get; set; } = null;
    public bool IsList { get; set; } = false;
    public string Source { get; set; } = "FIXED"; 
}

public class Addenda
{
    public string Tipo { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string SchemaLocation { get; set; } = "";
    public string Prefix { get; set; } = "add";

    public Dictionary<string, string> DatosTemplate { get; set; } = new Dictionary<string, string>();

    public AddendaOptions Options { get; set; } = new AddendaOptions();
}

public class AddendaOptions
{
    public bool UsarConceptosCFDI { get; set; } = true;
    public bool ExcluirFlete { get; set; } = true;
    public bool ExcluirAnticipos { get; set; } = true;
}
