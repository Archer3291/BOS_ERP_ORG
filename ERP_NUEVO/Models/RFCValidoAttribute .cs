using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

public class RFCValidoAttribute : ValidationAttribute
{
    // Expresiones regulares para validar el RFC
    private readonly string _rfcPatternMoral = @"^(([A-ZÑ&]{3})([0-9]{2})([0][13578]|[1][02])(([0][1-9]|[12][\d])|[3][01])([A-Z0-9]{3}))|(([A-ZÑ&]{3})([0-9]{2})([0][13456789]|[1][012])(([0][1-9]|[12][\d])|[3][0])([A-Z0-9]{3}))|(([A-ZÑ&]{3})([02468][048]|[13579][26])[0][2]([0][1-9]|[12][\d])([A-Z0-9]{3}))|(([A-ZÑ&]{3})([0-9]{2})[0][2]([0][1-9]|[1][0-9]|[2][0-8])([A-Z0-9]{3}))$";
    private readonly string _rfcPatternFisico = @"^(([A-ZÑ&]{4})([0-9]{2})([0][13578]|[1][02])(([0][1-9]|[12][\d])|[3][01])([A-Z0-9]{3}))|(([A-ZÑ&]{4})([0-9]{2})([0][13456789]|[1][012])(([0][1-9]|[12][\d])|[3][0])([A-Z0-9]{3}))|(([A-ZÑ&]{4})([02468][048]|[13579][26])[0][2]([0][1-9]|[12][\d])([A-Z0-9]{3}))|(([A-ZÑ&]{4})([0-9]{2})[0][2]([0][1-9]|[1][0-9]|[2][0-8])([A-Z0-9]{3}))$";

    public RFCValidoAttribute() : base("El RFC no es válido.") { }

    public override bool IsValid(object value)
    {
        if (value == null)
            return false;

        string rfc = value.ToString().ToUpper();

        // Validar el RFC contra los dos patrones
        return Regex.IsMatch(rfc, _rfcPatternFisico) || Regex.IsMatch(rfc, _rfcPatternMoral);
    }
}
