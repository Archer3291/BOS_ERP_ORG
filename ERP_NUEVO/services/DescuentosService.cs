using BOS_ERP.Models;

namespace BOS_ERP.Services
{
    public readonly struct TotalesDocumento
    {
        public decimal Subtotal { get; init; }
        public decimal Descuento { get; init; }
        public decimal Base => Subtotal - Descuento;
    }

    public static class DescuentosService
    {
        public static decimal Porcentaje(decimal? dto1)
        {
            decimal pct = dto1 ?? 0m;
            if (pct < 0m) return 0m;
            if (pct > 100m) return 100m;
            return pct;
        }

        public static decimal Bruto(PartidaDocumento partida)
            => Redondear((partida.CantUd ?? 0m) * (partida.PvProd ?? 0m));

        public static decimal Bruto(decimal? cantidad, decimal? precio)
            => Redondear((cantidad ?? 0m) * (precio ?? 0m));

        public static decimal Descuento(PartidaDocumento partida)
            => Redondear(Bruto(partida) * Porcentaje(partida.Dto1) / 100m);

        public static decimal Descuento(decimal? cantidad, decimal? precio, decimal? dto1)
            => Redondear(Bruto(cantidad, precio) * Porcentaje(dto1) / 100m);

        public static decimal Neto(PartidaDocumento partida)
            => Bruto(partida) - Descuento(partida);

        public static decimal Neto(decimal? cantidad, decimal? precio, decimal? dto1)
            => Bruto(cantidad, precio) - Descuento(cantidad, precio, dto1);

        public static TotalesDocumento Totalizar(IEnumerable<PartidaDocumento> partidas)
        {
            decimal subtotal = 0m, descuento = 0m;

            foreach (var partida in partidas ?? Enumerable.Empty<PartidaDocumento>())
            {
                subtotal += Bruto(partida);
                descuento += Descuento(partida);
            }

            return new TotalesDocumento { Subtotal = Redondear(subtotal), Descuento = Redondear(descuento) };
        }

        public static TotalesDocumento Normalizar(List<PartidaDocumento> partidas)
        {
            int nro = 1;

            foreach (var partida in partidas ?? new List<PartidaDocumento>())
            {
                partida.NroPart = nro++;
                partida.Dto1 = Porcentaje(partida.Dto1);
                partida.ImpPart = Bruto(partida);
                if (string.IsNullOrWhiteSpace(partida.Ud)) partida.Ud = "PZA";
            }

            return Totalizar(partidas);
        }

        public static decimal AplicarImpuestos(decimal baseGravable, IEnumerable<(decimal Tasa, bool EsRetencion)> impuestos)
        {
            decimal total = baseGravable;

            foreach (var (tasa, esRetencion) in impuestos ?? Enumerable.Empty<(decimal, bool)>())
            {
                decimal importe = Redondear(baseGravable * tasa / 100m);
                total += esRetencion ? -importe : importe;
            }

            return Redondear(total);
        }

        public static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }
}
