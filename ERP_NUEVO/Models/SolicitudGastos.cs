using System;
using System.Collections.Generic;

public class SolicitudGastosViewModel
{
    public string Categoria { get; set; }
    public string Subcategoria { get; set; }
    public string Fecha { get; set; }
    public string Moneda { get; set; }
    public string Proveedor { get; set; }
    public string Concepto { get; set; }
    public string Justificacion { get; set; }
    public decimal Monto { get; set; }
    public string CentroCosto { get; set; }
    public string Aprobador { get; set; }
    public string Urgencia { get; set; }
    public string NotasAdicionales { get; set; }
    public string NotificarEmail { get; set; }
    public string GenerarPDF { get; set; }
    // En SolicitudGastosViewModel
    public string CamposDinamicos { get; set; } 
}