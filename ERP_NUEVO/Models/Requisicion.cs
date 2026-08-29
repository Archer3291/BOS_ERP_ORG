using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BOS_ERP.Models
{
    public class Requisicion
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El campo Solicitante es requerido")]
        [Display(Name = "Solicitante")]
        public string Solicitante { get; set; }

        [Required(ErrorMessage = "El campo Departamento es requerido")]
        [Display(Name = "Departamento")]
        public string Departamento { get; set; }

        [Required(ErrorMessage = "El campo Fecha es requerido")]
        [Display(Name = "Fecha de Solicitud")]
        [DataType(DataType.Date)]
        public DateTime FechaSolicitud { get; set; } = DateTime.Now;

        [Display(Name = "Proyecto")]
        public string Proyecto { get; set; }

        [Display(Name = "Centro de Costos")]
        public string CentroCostos { get; set; }

        [Display(Name = "Justificación")]
        [DataType(DataType.MultilineText)]
        public string Justificacion { get; set; }

        public decimal Total { get; set; }

        public List<DetalleRequisicion> Detalles { get; set; } = new List<DetalleRequisicion>();
    }

    public class DetalleRequisicion
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La descripción es requerida")]
        [Display(Name = "Descripción")]
        public string Descripcion { get; set; }

        [Required(ErrorMessage = "La cantidad es requerida")]
        [Display(Name = "Cantidad")]
        [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0")]
        public int Cantidad { get; set; } = 1;

        [Required(ErrorMessage = "La unidad de medida es requerida")]
        [Display(Name = "Unidad de Medida")]
        public string UnidadMedida { get; set; }

        [Required(ErrorMessage = "El costo unitario es requerido")]
        [Display(Name = "Costo Unitario")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El costo debe ser mayor a 0")]
        public decimal CostoUnitario { get; set; }

        [Display(Name = "Subtotal")]
        public decimal Subtotal { get { return Cantidad * CostoUnitario; } }
    }
}