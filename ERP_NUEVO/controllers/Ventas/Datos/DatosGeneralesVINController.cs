using Microsoft.AspNetCore.Authorization;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public partial class DatosGeneralesController : Utilities
    {


        public IActionResult BuscarVIND(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.folio ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VINCOT'
        AND em.suc =  @suc
        AND em.estatus_id = 1
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VINCOT'";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }


        public IActionResult BuscarDVINped(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.folio ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            em.incoterm,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VINPED'
        AND em.suc =  @suc
        AND em.estatus_id = 1
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VINPED'";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        // En DatosGeneralesController — reemplazar BuscarDVINrem existente
        public IActionResult BuscarDVINrem(
            string nombre = "",
            int page = 1,
            int pageSize = 25,
            string cliente = "",
            string modo = "documento")
        {
            var parameters = new Dictionary<string, object>();

            string filtroCliente = !string.IsNullOrWhiteSpace(cliente)
                ? "AND em.cli_prov = @cliente"
                : "";

            string filtroModo = modo == "parciales"
                ? @"AND EXISTS (
            SELECT 1 FROM remision_partidas_facturadas rpf
            WHERE rpf.encabezado_remision_id = em.id_encabezado
              AND rpf.estatus IN ('pendiente', 'parcial')
        )"
                : @"AND NOT EXISTS (
            SELECT 1 FROM remision_partidas_facturadas rpf
            WHERE rpf.encabezado_remision_id = em.id_encabezado
              AND (
                (SELECT COUNT(*) FROM remision_partidas_facturadas rpf2
                 WHERE rpf2.encabezado_remision_id = em.id_encabezado) > 0
                AND
                (SELECT COUNT(*) FROM remision_partidas_facturadas rpf3
                 WHERE rpf3.encabezado_remision_id = em.id_encabezado
                   AND rpf3.estatus != 'completa') = 0
              )
            LIMIT 1
        )";

            string filtroEstatus = modo == "parciales"
                ? "AND em.estatus_id = 41"
                : "AND em.estatus_id = 1";

            string query = $@"
    SELECT
        em.id_encabezado,
        em.folio,
        em.gen,
        em.nat,
        em.fch        AS fecha,
        em.imp,
        em.cli_prov,
        em.usr0,
        cc.n_cli,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'pendiente') AS partidas_pendientes,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'parcial')   AS partidas_parciales,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'completa')  AS partidas_completas,
        COUNT(rpf.id)                                           AS total_partidas_tracking,
        COALESCE(
            SUM((rpf.cantidad_pendiente * rpf.precio_unitario
                * (1 - rpf.descuento / 100.0))::numeric(18,2))
            FILTER (WHERE rpf.estatus IN ('pendiente','parcial')),
            0
        ) AS importe_pendiente
    FROM encabezadomov em
    INNER JOIN catclientes cc
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    LEFT JOIN remision_partidas_facturadas rpf
        ON rpf.encabezado_remision_id = em.id_encabezado
    WHERE em.nat = 'VINREM'
      AND em.suc = @suc
      AND (
           LOWER(em.gen || '-' || em.nat || '-' ||
                 EXTRACT(YEAR FROM em.fch)::text || '-' ||
                 em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov)  LIKE LOWER(@nombre)
        OR LOWER(cc.n_cli)     LIKE LOWER(@nombre)
      )
      {filtroEstatus}
      {filtroCliente}
      {filtroModo}
    GROUP BY
        em.id_encabezado, em.gen, em.nat, em.fch,
        em.fol_doc, em.variacion, em.imp, em.cli_prov, em.usr0, cc.n_cli
    ORDER BY em.fch DESC
    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            if (!string.IsNullOrWhiteSpace(cliente))
                parameters.Add("cliente", cliente);

            var items = RunQuery(query, parameters);

            string queryTotal = $@"
    SELECT COUNT(*) AS total
    FROM encabezadomov em
    INNER JOIN catclientes cc
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    WHERE em.nat = 'VINREM'
      AND em.suc = @suc
      AND em.estatus_id = 1
      AND (
           LOWER(em.gen || '-' || em.nat || '-' ||
                 EXTRACT(YEAR FROM em.fch)::text || '-' ||
                 em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
      )
      {filtroCliente}
      {filtroModo}";

            var totalParameters = new Dictionary<string, object>
    {
        { "nombre",     $"%{nombre}%" },
        { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    };
            if (!string.IsNullOrWhiteSpace(cliente))
                totalParameters.Add("cliente", cliente);

            var totalResult = RunQuery(queryTotal, totalParameters);
            int total = totalResult?.Count > 0
                ? Convert.ToInt32(totalResult[0]["total"])
                : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarDVINespecial(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.folio ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            em.incoterm,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'AIEINV'
        AND (em.estatus_id != 11 AND em.estatus_id != 27)
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VINREM' AND em.estatus_id != 11";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public JsonResult ConsultarCotizacionesKepler(DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                if (!fechaInicio.HasValue) fechaInicio = DateTime.Now.AddDays(-30);
                if (!fechaFin.HasValue) fechaFin = DateTime.Now;

                string connection = HttpContext.Session.GetString("EmpresaFactura");

                var parameters = new Dictionary<string, object>
        {
            { "@fechaInicio", fechaInicio.Value },
            { "@fechaFin", fechaFin.Value }
        };

                string query = @"
            SELECT 
                kd.c10 as usuario,
                kd.c6 as folio,
                kd.c68 as fecha_captura,
                kd.c38 as tipo_documento,
                kd.c90 as forma_pago,
                (kd.c16 - kd.c14) as subtotal,
                kd.c13 as descuento,
                kd.c7 as moneda,
                kd.c40 as tipo_cambio,
                kd.c16 as total,
                kd.c12 as vendedor,
                kd.c30 as plazo,
                kd.c35 as cp_receptor,
                kd.c22 as rfc_receptor,
                kd.c32 as razon_receptor,
                kd.c84 as uso_cfdi,
                kd.c14 as total_impuestos_traslado,
                kd.c9 || kd.c10 || kd.c11 || kd.c12 || '-' || kd.c13 as folio_kepler,
                kd.c43 as estatus,
                kd.c6::text as id_kepler
            FROM kdm1 kd
            WHERE kd.c2 = 'U'
              AND kd.c3 = 'D'
              AND kd.c4 = 37
              AND kd.c5 = '02'
              AND kd.c9 BETWEEN @fechaInicio AND @fechaFin
            ORDER BY kd.c9 DESC, kd.c6 ASC";

                var rows = RunQuery(query, parameters, false, null, null, connection);

                if (rows.Count == 0)
                    return Json(new { success = false, message = "No se encontraron cotizaciones en el rango de fechas." });

                return Json(new { success = true, cotizaciones = rows });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesVIN/ConsultarCotizacionesKepler");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public JsonResult ObtenerPartidasCotizacionKepler(string folio)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folio))
                    return Json(new { success = false, message = "Folio requerido." });

                string connection = HttpContext.Session.GetString("EmpresaFactura");

                var parameters = new Dictionary<string, object>
        {
            { "@folio", folio }
        };

                string query = @"
            SELECT  
                kd.c8 as clave,
                kd.c10 as descripcion, 
                kd.c9 as cantidad,
                kd.c11 as unidad,
                kd.c12 as precio,
                kd.c14 as descuento,
                kd.c13 as importe	
            FROM sellosop.kdm2 kd
            WHERE kd.c6 = @folio
              AND kd.c1 = '02'
              AND kd.c2 = 'U'
              AND kd.c3 = 'D'
              AND kd.c4 = 37
              AND kd.c5 = '02' ";

                var rows = RunQuery(query, parameters, false, null, null, connection);

                return Json(new { success = true, partidas = rows });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesVIN/ObtenerPartidasCotizacionKepler");
                return Json(new { success = false, message = ex.Message });
            }
        }

    }
}