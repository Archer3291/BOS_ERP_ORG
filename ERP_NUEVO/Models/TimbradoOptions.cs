// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// Models/Options/AppOptions.cs
namespace BOS_ERP.Models.Options
{
    public class TimbradoOptions
    {
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class ModulaOptions
    {
        public string Usuario { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    // Models/AutofacturacionOptions.cs
    public class AutofacturacionOptions
    {
        public string SigningKey { get; set; }
        public Dictionary<string, PuntoAutofacturacion> Puntos { get; set; } = new();
    }

    public class PuntoAutofacturacion
    {
        public int EmpresaId { get; set; }
        public int SucursalId { get; set; }
        public string PerfilEmisor { get; set; }
    }

    public class EmisorOptions
    {
        public string CpE { get; set; } = string.Empty;
        public string Rfc { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Regimen { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string? Correo { get; set; }
        public string? Direccion { get; set; }
    }
}


