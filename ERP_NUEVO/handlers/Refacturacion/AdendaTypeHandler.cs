using BOS_ERP.Controllers;
using BOS_ERP.Services.Refacturacion;
using Npgsql;
using System.Text.Json;
using System.Xml.Linq;

public class AdendaRequestData
{
    public string Modo { get; set; }              // "solo-corregir" | "cancelar-reemitir"
    public int IdAddenda { get; set; }
    public Dictionary<string, string> Valores { get; set; } = new();
}

public class AdendaTypeHandler : IRefacturacionTypeHandler
{
    private readonly Utilities _utils;
    private readonly IWebHostEnvironment _env;

    public string TipoId => "adenda";

    public AdendaTypeHandler(Utilities utils, IWebHostEnvironment env)
    {
        _utils = utils;
        _env = env;
    }

    private AdendaRequestData Parse(Dictionary<string, object> cambios)
    {
        return new AdendaRequestData
        {
            Modo = LeerString(cambios, "modo") ?? "solo-corregir",
            IdAddenda = LeerInt(cambios, "idAddenda"),
            Valores = LeerDiccionario(cambios, "valores")
        };
    }

    // Los valores de 'cambios' llegan como JsonElement cuando el request viene por [FromBody]
    // (System.Text.Json). Estos helpers los normalizan sin importar el origen.
    private static string LeerString(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        if (v is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return v.ToString();
    }

    private static int LeerInt(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return 0;
        if (v is JsonElement je)
            return je.ValueKind == JsonValueKind.Number ? je.GetInt32()
                 : int.TryParse(je.ToString(), out var n) ? n : 0;
        return int.TryParse(v.ToString(), out var m) ? m : 0;
    }

    private static Dictionary<string, string> LeerDiccionario(Dictionary<string, object> d, string key)
    {
        var result = new Dictionary<string, string>();
        if (!d.TryGetValue(key, out var v) || v == null) return result;

        if (v is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in je.EnumerateObject())
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();
        }
        else if (v is Dictionary<string, object> dict)
        {
            foreach (var kv in dict)
                result[kv.Key] = kv.Value?.ToString() ?? "";
        }
        return result;
    }

    public async Task<ValidacionResultado> ValidarAsync(int encabezadoId, Dictionary<string, object> cambios)
    {
        var r = new ValidacionResultado { EsValido = true };
        var data = Parse(cambios);

        if (data.IdAddenda == 0)
        {
            r.EsValido = false;
            r.Errores.Add("Debes seleccionar una plantilla de Adenda.");
            return r;
        }

        // Verificar que la Adenda pertenece al cliente de esta factura y sigue activa
        var addendaDef = _utils.RunQuery(
            @"SELECT ad.id_addenda, ad.nombre, ad.data_template
              FROM cfdi_addenda_def ad
              INNER JOIN factura fa ON fa.idcliente = ad.id_cliente
              WHERE fa.encabezado_id = @encId AND ad.id_addenda = @idAddenda AND ad.activo = true
              LIMIT 1",
            new Dictionary<string, object> { { "encId", encabezadoId }, { "idAddenda", data.IdAddenda } });

        if (addendaDef.Count == 0)
        {
            r.EsValido = false;
            r.Errores.Add("La Adenda seleccionada no está activa o no corresponde al cliente de este CFDI.");
            return r;
        }

        // Validar campos requeridos según el template (misma lógica que flattenObject/renderAdendaCampos)
        var template = addendaDef[0]["data_template"];
        var campos = FlattenAddendaTemplate(template);
        foreach (var c in campos.Where(c => c.Required))
        {
            if (!data.Valores.TryGetValue(c.Key, out var val) || string.IsNullOrWhiteSpace(val))
                r.Errores.Add($"El campo '{c.Label}' de la Adenda es obligatorio.");
        }
        if (r.Errores.Count > 0) { r.EsValido = false; return r; }

        r.RequiereCancelacion = data.Modo == "cancelar-reemitir";
        r.RequiereTimbradoNuevo = data.Modo == "cancelar-reemitir";

        // Solo cuando se va a cancelar+reemitir aplica la validación de la cadena documental
        // (en "solo-corregir" no se cancela ni sustituye el CFDI, así que no aplica).
        if (r.RequiereCancelacion)
        {
            var bloqueos = AnalizadorDocumentos.Bloqueos(_utils, encabezadoId, TipoId);
            if (bloqueos.Count > 0)
            {
                r.EsValido = false;
                r.Errores.AddRange(bloqueos);
            }
        }

        return r;
    }

    // ── Camino A: cancelar-reemitir → construye XML completo con Addenda embebida ──
    public async Task<ResultadoConstruccionCfdi> ConstruirXmlNuevoAsync(int encabezadoId, Dictionary<string, object> cambios,
        string motivo, string tipoRelacion, string uuidRelacionado)
    {
        var data = Parse(cambios);
        var factura = FacturaBuilder.DesdeEncabezado(_utils, encabezadoId);

        var addendaRow = _utils.RunQuery(
            "SELECT nombre, xml_namespace, xml_schema, xml_prefix, usar_conceptos, data_template FROM cfdi_addenda_def WHERE id_addenda = @id",
            new Dictionary<string, object> { { "id", data.IdAddenda } })[0];

        factura.Addenda = new Addenda
        {
            Namespace = addendaRow["xml_namespace"].ToString(),
            SchemaLocation = addendaRow["xml_schema"]?.ToString(),
            Prefix = addendaRow["xml_prefix"]?.ToString() ?? "add",
            Options = new AddendaOptions(),
            // Mezclamos el template base con los valores corregidos capturados en el wizard
            DatosTemplate = MergeTemplateConValores(addendaRow["data_template"], data.Valores)
        };

        // El timbrado real (llamada al PAC) lo hace el orquestador con IPacService, como los demás tipos.
        return new ResultadoConstruccionCfdi
        {
            Factura = factura,
            Xml = XmlCfdiBuilder.Construir(factura, tipoRelacion, uuidRelacionado, _utils)
        };
    }

    // ── Camino B: solo-corregir → NO se toca el timbrado, solo se reescribe el nodo Addenda ──
    public async Task<string> RegenerarSinTimbrarAsync(int encabezadoId, Dictionary<string, object> cambios,
        NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var data = Parse(cambios);

        var facturaRow = _utils.RunQuery(
            "SELECT uuid, textfactura FROM factura WHERE encabezado_id = @encId AND statusfactura = 'Timbrada' LIMIT 1",
            new Dictionary<string, object> { { "encId", encabezadoId } });

        if (facturaRow.Count == 0)
            throw new InvalidOperationException("No se encontró el XML timbrado original.");

        string uuid = facturaRow[0]["uuid"].ToString();
        string xmlOriginal = facturaRow[0]["textfactura"]?.ToString() ?? "";

        var addendaRow = _utils.RunQuery(
            "SELECT xml_namespace, xml_schema, xml_prefix, data_template FROM cfdi_addenda_def WHERE id_addenda = @id",
            new Dictionary<string, object> { { "id", data.IdAddenda } })[0];

        var addenda = new Addenda
        {
            Namespace = addendaRow["xml_namespace"].ToString(),
            SchemaLocation = addendaRow["xml_schema"]?.ToString(),
            Prefix = addendaRow["xml_prefix"]?.ToString() ?? "add",
            Options = new AddendaOptions(),
            DatosTemplate = MergeTemplateConValores(addendaRow["data_template"], data.Valores)
        };

        // Reemplaza (o inserta si no existía) el nodo <cfdi:Addenda> preservando el resto sellado
        var xdoc = XDocument.Parse(xmlOriginal);
        XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";
        xdoc.Descendants(cfdi + "Addenda").FirstOrDefault()?.Remove();

        var factura = FacturaBuilder.DesdeEncabezado(_utils, encabezadoId);
        factura.Addenda = addenda;
        var nuevoNodoAddenda = AddendaXmlWriter.ConstruirNodo(factura); // extraído de EscribirAddenda()
        xdoc.Root.Add(nuevoNodoAddenda);

        string xmlActualizado = xdoc.ToString();

        // Guardamos el nuevo XML pisando el mismo UUID (mismo timbrado, solo cambia Addenda)
        string xmlPath = Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados", $"{uuid}.xml");
        await File.WriteAllTextAsync(xmlPath, xmlActualizado);

        _utils.RunUpdate(
            "UPDATE factura SET textfactura = @xml WHERE uuid = @uuid",
            new Dictionary<string, object> { { "xml", xmlActualizado }, { "uuid", Guid.Parse(uuid) } },
            false, conn, tx);

        return uuid; // regresamos el mismo UUID, no hay uno nuevo
    }

    private List<(string Key, string Label, bool Required)> FlattenAddendaTemplate(object template)
    {
        // misma lógica que flattenObject() del JS
        var json = template is string s ? System.Text.Json.JsonDocument.Parse(s).RootElement
                                          : System.Text.Json.JsonSerializer.SerializeToElement(template);
        var result = new List<(string, string, bool)>();
        void Walk(System.Text.Json.JsonElement el, string prefix)
        {
            foreach (var prop in el.EnumerateObject())
            {
                string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                    Walk(prop.Value, key);
                else
                    result.Add((key, key, false)); // required=false igual que el JS (no lo define el template hoy)
            }
        }
        Walk(json, "");
        return result;
    }

    private Dictionary<string, string> MergeTemplateConValores(object template, Dictionary<string, string> valoresCorregidos)
    {
        var baseVals = FlattenAddendaTemplate(template).ToDictionary(c => c.Key, c => "");
        foreach (var kv in valoresCorregidos)
            baseVals[kv.Key] = kv.Value;
        return baseVals;
    }
}