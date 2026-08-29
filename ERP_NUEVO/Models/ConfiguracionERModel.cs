using System;
using System.Collections.Generic;

public class ConfiguracionER
{
    public int? Id { get; set; }
    public string Nombre { get; set; }
    public DateTime Fecha { get; set; }
    public List<SeccionER> Secciones { get; set; }
}

public class SeccionER
{
    public string Nombre { get; set; }
    public List<FilaER> Filas { get; set; }
}

public class FilaER
{
    public string Etiqueta { get; set; }
    public string CuentaIni { get; set; }
    public string CuentaFin { get; set; }
    public string Signo { get; set; } // A / D
    public bool Mostrar { get; set; }
}