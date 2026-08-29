using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace BOS_ERP.Models
{
    public class SolicitudCreditoEmailModel
    {
        public string UsuarioSolicitante { get; set; }
        public string Cliente { get; set; }
        public string Folio { get; set; }

        public decimal Limite { get; set; }
        public decimal Usado { get; set; }
        public decimal Disponible { get; set; }
        public decimal Total { get; set; }

        public List<ProductoEmail> Productos { get; set; }

        public DateTime Fecha { get; set; }
        public string Token { get; set; }
        public string UrlAutorizacion { get; set; }
    }

    public class ProductoEmail
    {
        public string Descripcion { get; set; }
        public string Cantidad { get; set; }
        public string Precio { get; set; }
        public decimal Importe { get; set; }
    }
    public class SolicitudCreditoVM
    {
        public int Id { get; set; }
        public int PedidoId { get; set; }
        public int ClienteId { get; set; }
        public string NombreCliente { get; set; }
        public string Folio { get; set; }
        public decimal CreditoLimite { get; set; }
        public decimal CreditoUsado { get; set; }
        public decimal MontoPedido { get; set; }
        public decimal Excedente { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public int SolicitadoPor { get; set; }
        public string Token { get; set; }

        /// <summary>
        /// Controlador que resuelve la autorización, para que la vista compartida sirva a
        /// cualquier canal de venta (VIPedido, VNPedido…). Por defecto, Industrial.
        /// </summary>
        public string UrlConfirmacion { get; set; } = "/VIPedido/ConfirmarAutorizacion";
    }
}
