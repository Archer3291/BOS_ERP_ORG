// services/Facturacion/FacturaPdfService.cs
using BOS_ERP.Controllers;
using BOS_ERP.Models;
using BOS_ERP.Services.Refacturacion; // FacturaBuilder: reconstructor genérico desde el XML timbrado

namespace BOS_ERP.Services.Facturacion
{
    /// <summary>
    /// Servicio general (NO exclusivo de refacturación) para reconstruir una Factura a partir
    /// de su CFDI timbrado y así poder generar su representación impresa (PDF).
    ///
    /// El XML timbrado se obtiene con una estrategia de respaldo:
    ///   1) columna `factura.textfactura`;
    ///   2) si viene vacía, el archivo `wwwroot/Facturacion/xml_timbrados/{uuid}.xml`.
    /// </summary>
    public interface IFacturaPdfService
    {
        Factura ReconstruirPorUuid(string uuid);
        Factura ReconstruirPorFacturaId(int facturaId);
        string ObtenerXml(string uuid);
    }

    public class FacturaPdfService : IFacturaPdfService
    {
        private readonly Utilities _utils;
        private readonly IWebHostEnvironment _env;

        public FacturaPdfService(Utilities utils, IWebHostEnvironment env)
        {
            _utils = utils;
            _env = env;
        }

        // Reconstruye la Factura (comprobante, receptor, conceptos, sellos, relacionados)
        // desde el CFDI timbrado, localizando la factura por su UUID.
        public Factura ReconstruirPorUuid(string uuid)
        {
            if (!Guid.TryParse(uuid, out var uuidGuid)) return null;

            var rows = _utils.RunQuery(
                "SELECT encabezado_id, textfactura FROM factura WHERE uuid = @uuid LIMIT 1",
                new Dictionary<string, object> { { "uuid", uuidGuid } });

            if (rows.Count == 0) return null;

            int encId = Convert.ToInt32(rows[0]["encabezado_id"]);
            string xml = ResolverXml(uuid, rows[0]["textfactura"]?.ToString());
            if (string.IsNullOrWhiteSpace(xml)) return null;

            return FacturaBuilder.DesdeEncabezado(_utils, encId, xml);
        }

        // Igual que ReconstruirPorUuid pero localizando por el id numérico de la tabla factura.
        public Factura ReconstruirPorFacturaId(int facturaId)
        {
            var rows = _utils.RunQuery(
                "SELECT encabezado_id, uuid, textfactura FROM factura WHERE id = @id LIMIT 1",
                new Dictionary<string, object> { { "id", facturaId } });

            if (rows.Count == 0) return null;

            int encId = Convert.ToInt32(rows[0]["encabezado_id"]);
            string uuid = rows[0]["uuid"]?.ToString();
            string xml = ResolverXml(uuid, rows[0]["textfactura"]?.ToString());
            if (string.IsNullOrWhiteSpace(xml)) return null;

            return FacturaBuilder.DesdeEncabezado(_utils, encId, xml);
        }

        // Devuelve el XML timbrado (columna o archivo). null si no se encuentra.
        public string ObtenerXml(string uuid)
        {
            if (!Guid.TryParse(uuid, out var uuidGuid)) return null;

            var rows = _utils.RunQuery(
                "SELECT textfactura FROM factura WHERE uuid = @uuid LIMIT 1",
                new Dictionary<string, object> { { "uuid", uuidGuid } });

            string textfacturaDb = rows.Count > 0 ? rows[0]["textfactura"]?.ToString() : null;
            return ResolverXml(uuid, textfacturaDb);
        }

        // Estrategia de obtención del XML: columna textfactura → archivo en disco.
        private string ResolverXml(string uuid, string textfacturaDb)
        {
            if (!string.IsNullOrWhiteSpace(textfacturaDb))
                return textfacturaDb;

            try
            {
                string path = Path.Combine(
                    _env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados", $"{uuid}.xml");
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }
            catch { /* si el archivo no se puede leer, se devuelve null abajo */ }

            return null;
        }
    }
}
