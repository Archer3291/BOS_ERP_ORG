// handlers/Refacturacion/ConceptosTypeHandler.cs
using System.Data;
using System.Globalization;
using System.Text.Json;
using BOS_ERP.Controllers;
using BOS_ERP.Services.Refacturacion;

// Refacturación por ERROR EN CONCEPTOS / PARCIAL (fusionadas): el usuario edita las partidas
// del CFDI (agrega, quita, corrige cantidad/precio/producto/descripción) y se emite un CFDI
// sustituto con las partidas corregidas. Como cambia lo facturado, TIENE impacto en inventario:
// el ajuste de existencias (revertir el del original + re-descontar el nuevo) lo hace el
// orquestador cuando `AjustaInventario` = true (ver RefacturacionOrchestrator).
//
// Contrato con el front (conceptos.js → collectChanges):
//   partidas: [ { claveProdServ, descripcion, claveUnidad, cantidad, precioUnit,
//                 descuentoImporte, iva('0.16'|'0'|'exento'), numero, idProducto } ]
public class ConceptosTypeHandler : IRefacturacionTypeHandler
{
    private readonly Utilities _utils;
    public string TipoId => "conceptos";

    public ConceptosTypeHandler(Utilities utils) => _utils = utils;

    public async Task<ValidacionResultado> ValidarAsync(int encabezadoId, Dictionary<string, object> cambios)
    {
        var r = new ValidacionResultado { EsValido = true };

        var partidas = LeerPartidas(cambios);
        if (partidas.Count == 0)
            r.Errores.Add("Debes capturar al menos un concepto.");

        for (int i = 0; i < partidas.Count; i++)
        {
            var p = partidas[i];
            string et = $"Concepto {i + 1}";
            if (string.IsNullOrWhiteSpace(p.ClaveProdServ))
                r.Errores.Add($"{et}: la ClaveProdServ es obligatoria.");
            if (string.IsNullOrWhiteSpace(p.Descripcion))
                r.Errores.Add($"{et}: la descripción es obligatoria.");
            if (string.IsNullOrWhiteSpace(p.ClaveUnidad))
                r.Errores.Add($"{et}: la ClaveUnidad es obligatoria.");
            if (p.Cantidad <= 0)
                r.Errores.Add($"{et}: la cantidad debe ser mayor a 0.");
            if (p.PrecioUnit < 0)
                r.Errores.Add($"{et}: el precio unitario no puede ser negativo.");
            decimal bruto = Math.Round(p.Cantidad * p.PrecioUnit, 2);
            if (p.DescuentoImporte < 0 || p.DescuentoImporte > bruto)
                r.Errores.Add($"{et}: el descuento no puede ser negativo ni superar el importe.");
        }

        // Validaciones comunes de la cadena documental (complementos, notas, sustituto, etc.).
        r.Errores.AddRange(AnalizadorDocumentos.Bloqueos(_utils, encabezadoId, TipoId));

        // El tipo cambia partidas → el orquestador debe ajustar inventario (revertir original +
        // re-descontar el nuevo, con verificación de stock por producto).
        r.AjustaInventario = true;

        r.EsValido = r.Errores.Count == 0;
        return r;
    }

    public async Task<ResultadoConstruccionCfdi> ConstruirXmlNuevoAsync(int encabezadoId, Dictionary<string, object> cambios,
        string motivo, string tipoRelacion, string uuidRelacionado)
    {
        // Reconstruye la factura original y REEMPLAZA sus conceptos por los capturados en el wizard.
        var factura = FacturaBuilder.DesdeEncabezado(_utils, encabezadoId);
        factura.Tproductos = ConstruirTproductos(LeerPartidas(cambios));

        // XmlCfdiBuilder recalcula Subtotal/IVA/Total desde los conceptos nuevos.
        return new ResultadoConstruccionCfdi
        {
            Factura = factura,
            Xml = XmlCfdiBuilder.Construir(factura, tipoRelacion, uuidRelacionado, _utils)
        };
    }

    // ── DataTable con las MISMAS columnas que FacturaBuilder.ParseConceptosDesdeXml ──
    private static DataTable ConstruirTproductos(List<PartidaCfdi> partidas)
    {
        var t = new DataTable();
        t.Columns.Add("cantidad", typeof(decimal));
        t.Columns.Add("precioUnit", typeof(decimal));
        t.Columns.Add("descuento", typeof(decimal));   // % (XmlCfdiBuilder lo interpreta como porcentaje)
        t.Columns.Add("objetoImp", typeof(string));
        t.Columns.Add("descripcion", typeof(string));
        t.Columns.Add("comentario", typeof(string));
        t.Columns.Add("claveProdServ", typeof(string));
        t.Columns.Add("numero", typeof(string));
        t.Columns.Add("claveUnidad", typeof(string));
        t.Columns.Add("unidad", typeof(string));
        t.Columns.Add("importe", typeof(decimal));
        t.Columns.Add("iva", typeof(decimal));
        t.Columns.Add("tasaCuota", typeof(decimal));
        t.Columns.Add("pedimento", typeof(string));
        t.Columns.Add("clave_cliente", typeof(string));
        t.Columns.Add("idProducto", typeof(int));   // lo usa el orquestador para el ajuste de inventario

        foreach (var p in partidas)
        {
            decimal bruto = Math.Round(p.Cantidad * p.PrecioUnit, 2);
            // El front captura el descuento como IMPORTE; XmlCfdiBuilder lo usa como PORCENTAJE.
            decimal descPct = bruto > 0 ? Math.Round(p.DescuentoImporte / bruto * 100m, 4) : 0m;

            // objetoImp/tasa según la selección de IVA: 16%/0% → gravado '02'; exento → '01'.
            string objetoImp = p.IvaSel == "exento" ? "01" : "02";
            decimal tasa = p.IvaSel == "0.16" ? 0.16m : 0m;
            decimal neto = bruto - p.DescuentoImporte;
            decimal ivaConcepto = objetoImp == "02" ? Math.Round(neto * tasa, 2) : 0m;

            var fila = t.NewRow();
            fila["cantidad"] = p.Cantidad;
            fila["precioUnit"] = p.PrecioUnit;
            fila["descuento"] = descPct;
            fila["objetoImp"] = objetoImp;
            fila["descripcion"] = p.Descripcion ?? "";
            fila["comentario"] = "";
            fila["claveProdServ"] = p.ClaveProdServ ?? "";
            fila["numero"] = p.Numero ?? "";
            fila["claveUnidad"] = p.ClaveUnidad ?? "";
            fila["unidad"] = UnidadDesde(p.ClaveUnidad);
            fila["importe"] = bruto;
            fila["iva"] = ivaConcepto;
            fila["tasaCuota"] = tasa;
            fila["pedimento"] = "";
            fila["clave_cliente"] = "";
            fila["idProducto"] = p.IdProducto;
            t.Rows.Add(fila);
        }

        return t;
    }

    private static string UnidadDesde(string claveUnidad) => (claveUnidad ?? "").ToUpper() switch
    {
        "H87" => "Pieza",
        "E48" => "Servicio",
        "KGM" => "Kilogramo",
        "LTR" => "Litro",
        "MTR" => "Metro",
        "XBX" => "Caja",
        "ACT" => "Actividad",
        "MON" => "Mes",
        _ => "Pieza"
    };

    // ── Parseo del payload (llega como JsonElement por [FromBody]) ──
    private static List<PartidaCfdi> LeerPartidas(Dictionary<string, object> cambios)
    {
        var lista = new List<PartidaCfdi>();
        if (!cambios.TryGetValue("partidas", out var raw) || raw == null) return lista;
        if (raw is not JsonElement je || je.ValueKind != JsonValueKind.Array) return lista;

        foreach (var item in je.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            lista.Add(new PartidaCfdi
            {
                ClaveProdServ = Str(item, "claveProdServ"),
                Descripcion = Str(item, "descripcion"),
                ClaveUnidad = Str(item, "claveUnidad"),
                Numero = Str(item, "numero"),
                IvaSel = Str(item, "iva"),
                IdProducto = IntJson(item, "idProducto"),
                Cantidad = DecJson(item, "cantidad"),
                PrecioUnit = DecJson(item, "precioUnit"),
                DescuentoImporte = DecJson(item, "descuentoImporte"),
            });
        }
        return lista;
    }

    private static string Str(JsonElement o, string k)
        => o.TryGetProperty(k, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    private static decimal DecJson(JsonElement o, string k)
    {
        if (!o.TryGetProperty(k, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0m;
    }

    private static int IntJson(JsonElement o, string k)
    {
        if (!o.TryGetProperty(k, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        return int.TryParse(v.ToString(), out var p) ? p : 0;
    }

    private class PartidaCfdi
    {
        public string ClaveProdServ { get; set; }
        public string Descripcion { get; set; }
        public string ClaveUnidad { get; set; }
        public string Numero { get; set; }
        public string IvaSel { get; set; }
        public int IdProducto { get; set; }
        public decimal Cantidad { get; set; }
        public decimal PrecioUnit { get; set; }
        public decimal DescuentoImporte { get; set; }
    }
}
