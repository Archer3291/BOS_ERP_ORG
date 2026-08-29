// Services/Refacturacion/ValidadorFiscal.cs
using System.Text.RegularExpressions;

namespace BOS_ERP.Services.Refacturacion
{
    public static class ValidadorFiscal
    {
        // Persona moral: 3 letras + 6 dígitos (fecha) + 3 alfanuméricos (homoclave)
        // Persona física: 4 letras + 6 dígitos (fecha) + 3 alfanuméricos (homoclave)
        // RFC genérico para público en general: XAXX010101000
        private static readonly Regex RegexRfc = new Regex(
            @"^([A-ZÑ&]{3,4})(\d{2})(\d{2})(\d{2})([A-Z0-9]{3})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool EsRfcValido(string rfc)
        {
            if (string.IsNullOrWhiteSpace(rfc))
                return false;

            rfc = rfc.Trim().ToUpperInvariant();

            // RFC genérico usado para público en general
            if (rfc == "XAXX010101000" || rfc == "XEXX010101000")
                return true;

            var match = RegexRfc.Match(rfc);
            if (!match.Success)
                return false;

            // Validar que la fecha embebida (AAMMDD) sea una fecha real
            int anio = int.Parse(match.Groups[2].Value);
            int mes = int.Parse(match.Groups[3].Value);
            int dia = int.Parse(match.Groups[4].Value);

            if (mes < 1 || mes > 12) return false;

            int diasEnMes = mes switch
            {
                2 => (anio % 4 == 0 && (anio % 100 != 0 || anio % 400 == 0)) ? 29 : 28,
                4 or 6 or 9 or 11 => 30,
                _ => 31
            };

            return dia >= 1 && dia <= diasEnMes;
        }
    }
}