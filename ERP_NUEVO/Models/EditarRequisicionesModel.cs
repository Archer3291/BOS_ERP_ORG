using BOS_ERP.Models;
using System.Collections.Generic;

public class EditarRequisicionesModel
{
    public int IdEncabezado { get; set; }

    public string Observaciones { get; set; } = string.Empty;

    public string Materiales { get; set; } = "[]";
    
    public string Impuestos { get; set; } = "[]";

    // El front reintenta con este flag cuando el documento ya genero hijos
    // y el usuario acepta que se cancelen junto con la edicion.
    public bool ConfirmarCancelacionHijos { get; set; }
}
