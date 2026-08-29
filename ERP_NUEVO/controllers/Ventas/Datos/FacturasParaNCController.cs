using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public partial class DatosGeneralesController : Utilities
    {

        public IActionResult BuscarFacturasActivas(string nombre, string rfc = "", int page = 1, int pageSize = 50)
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
            f.rfccliente as rfc,
            cc.n_cli
        FROM encabezadomov em
        INNER JOIN catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        INNER JOIN factura f on f.encabezado_id = em.id_encabezado and f.serie != 'NC'
        INNER JOIN rmd_reporte_detalle rrd ON rrd.encabezado_id = em.id_encabezado     
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.suc =  @suc
        AND (@rfc = '' OR LOWER(cc.rfc) = LOWER(@rfc))
        AND rrd.estatus_detalle = 'DOC_GENERADO'
AND NOT EXISTS (
    SELECT 1
    FROM nc_solicitud s,
         jsonb_array_elements_text(s.encabezado_ids_json) AS elem
    WHERE 
        s.empresa_id = @empresa_id
        AND s.estatus_actual = 'PENDIENTE'
        AND elem::int = em.id_encabezado
)
       -- AND em.estatus_id = 11
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("rfc", rfc ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
    SELECT COUNT(*) AS total
    FROM encabezadomov em
    INNER JOIN catclientes cc 
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    INNER JOIN factura f ON f.encabezado_id = em.id_encabezado
    INNER JOIN rmd_reporte_detalle rrd ON rrd.encabezado_id = em.id_encabezado
    WHERE (
           LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.gen) LIKE LOWER(@nombre)
        OR LOWER(em.nat) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
    )

    AND em.suc = @suc
    AND (@rfc = '' OR LOWER(cc.rfc) = LOWER(@rfc))
    AND em.estatus_id = 1";

            var totalParams = new Dictionary<string, object>
            {
                { "nombre", $"%{nombre}%" },
                { "rfc", rfc ?? "" },
                { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            var totalResult = RunQuery(queryTotal, totalParams);
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarFacturasSolicitud(string nombre, string rfc = "", int page = 1, int pageSize = 50)
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
            cc.rfc,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        --INNER join factura f on f.encabezado_id = em.id_encabezado
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.suc =  @suc
        AND (@rfc = '' OR LOWER(cc.rfc) = LOWER(@rfc))
        AND em.estatus_id IN (28,29,30,31,32,33)
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("rfc", rfc ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
    SELECT COUNT(*) AS total
    FROM encabezadomov em
    INNER JOIN catclientes cc 
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    INNER JOIN factura f ON f.encabezado_id = em.id_encabezado
    WHERE (
           LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.gen) LIKE LOWER(@nombre)
        OR LOWER(em.nat) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
    )
    AND em.suc = @suc
    AND (@rfc = '' OR LOWER(cc.rfc) = LOWER(@rfc))
    AND em.estatus_id = 1";

            var totalParams = new Dictionary<string, object>
            {
                { "nombre", $"%{nombre}%" },
                { "rfc", rfc ?? "" },
                { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            var totalResult = RunQuery(queryTotal, totalParams);
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarFacturaDetalleNC(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "id", id }
        };

                // 🔹 Consulta del encabezado
                string queryEncabezado = @"
SELECT  
    -- FOLIO ARMADO
    em.folio ||
        CASE WHEN em.variacion > 0  
             THEN '-' || num_to_letters(em.variacion)  
             ELSE '' END AS folio,

    -- DATOS PRINCIPALES
    em.id_encabezado,
    em.encabezados_padre,
    em.suc,
    em.alm,
    em.gen,
    em.nat,
    em.usr0,
    em.fch0,
    em.cli_prov,
    em.coment1,
    em.coment_aut,
    em.ccy,
    em.vdr_cpr,
    em.flete,
    em.incoterm,
    em.mdp,
    em.f_pago,
    em.cfdi,
    cc.rfc,
    em.par,
    cc.lim_crd,
    cc.n_cli,
    cc.pl_crd,
    cc.dir,

    -- CFDI
    f.uuid,
    f.total,

    -- Datos fiscales cliente
    df.forma_pago, 
    df.uso_sugerido,
    df.regimen_fiscal,
    df.calle, df.no_exterior, df.no_interior, 
    df.colonia, df.localidad, df.municipio, df.estado, df.pais, df.codigo_postal,

    -- Información cliente unificada
    cc.dir || CHR(10) ||
    cc.col || CHR(10) ||
    cc.pob || CHR(10) ||
    cc.cp AS info_cli,

    -- CARTERA ACTUAL
    ccx.id_cartera_cliente,
    ccx.fecha_emision,
    ccx.fecha_vencimiento,
    ccx.monto_total,

    em.ccy AS moneda_cartera,
    ccx.estado AS estado_cartera,
    ccx.fecha_vencimiento,

    -- ⭐ SALDO PENDIENTE REAL (SUMADO CON HIJOS)
    ccx.saldo_pendiente as saldo_pendiente_real

FROM encabezadomov em
INNER JOIN catclientes cc  ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id

LEFT JOIN direcciones_facturacion df 
    ON df.entidad_clave = cc.cve_cli

LEFT JOIN factura f 
    ON f.encabezado_id = em.id_encabezado

LEFT JOIN cartera_clientes ccx
    ON ccx.encabezado_id = em.id_encabezado
WHERE em.id_encabezado = @id;
";

                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encabezadoResult.First();

                // 🔹 Parámetro de sucursal (lo obtenemos del encabezado)
                int sucursal = Convert.ToInt32(encabezado["suc"]);
                parameters.Add("sucursal", sucursal);

                // 🔹 Consulta de partidas con EXISTENCIAS
                string queryPartidas = @"
            SELECT 
                pd.id_partidas AS id,
                pd.cve_prod AS producto_id,
                pd.descr_prod AS descripcion,
                pd.cant_ud AS cantidad,
                pd.pv_prod AS precio,
                pd.dto1 AS descuento,
                pd.ud AS unidad,
                pd.imp_part AS importe,
                pd.iva,
                pd.ieps,
                pd.fch AS fecha,
                pd.cve_alm AS almacen,
                pd.cto_vta_part AS costo,
                pd.ccy AS moneda,
                COALESCE(stk.cantidadStock, 0) AS existencia
            FROM partidasdoc pd
LEFT JOIN encabezadomov em 
    ON em.id_encabezado = pd.encabezado_id
LEFT JOIN (
    SELECT 
        tp.producto_id, 
        cr.almacen_id, 
        cs.id_sucursal, 
        SUM(tp.cantidad) AS cantidadStock
    FROM tarima_productos tp
    INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
    INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
    INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
    INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
    INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
    INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
    WHERE ca.tipo = 'Stock'
    GROUP BY tp.producto_id, cr.almacen_id, cs.id_sucursal
) stk  
                ON stk.producto_id = pd.producto_id 
               AND stk.id_sucursal = @sucursal
            WHERE pd.encabezado_id = @id
AND EXISTS (
    SELECT 1
    FROM rmd_partida_danada rpd
    WHERE rpd.encabezado_id = pd.encabezado_id
      AND rpd.producto_id = pd.cve_prod
)
            ORDER BY pd.nro_part;
        ";

                var partidasResult = RunQuery(queryPartidas, parameters);

                // 🔹 Unimos encabezado + productos
                encabezado["productos"] = partidasResult;

                // ✅ Envolvemos en lista para mantener el formato con índice 0
                var result = new List<Dictionary<string, object>> { encabezado };

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "FacturasParaNC/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult BuscarAnticipoDeFact(int encabezadoId)
        {
            var parameters = new Dictionary<string, object>
            {
                ["enc_id"] = encabezadoId,
                ["empresa_id"] = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"))
            };

            var anticipos = RunQuery(@"
        SELECT 
            fa.id                        AS fa_id,
            fa.id_factura_anticipo       AS anticipo_id,
            fa.monto_aplicado,
            f_ant.folio                  AS anticipo_folio,
            f_ant.fecha                  AS anticipo_fecha,
            f_ant.saldo                  AS saldo_anticipo_actual,
            f_ant.total                  AS total_anticipo,
            f_orig.total                 AS total_factura_orig,
            f_orig.saldo                 AS saldo_factura_orig
        FROM factura_anticipos fa
        JOIN factura f_ant  ON f_ant.id  = fa.id_factura_anticipo
        JOIN factura f_orig ON f_orig.id = fa.id_factura_principal
        WHERE f_orig.encabezado_id = @enc_id
          AND fa.monto_aplicado    > 0
        ORDER BY fa.fecha_aplicacion;",
                parameters);

            if (anticipos == null || anticipos.Count == 0)
                return Json(new { tieneAnticipos = false, anticipos = new object[0] });

            return Json(new
            {
                tieneAnticipos = true,
                anticipos = anticipos.Select(a => new
                {
                    faId = a["fa_id"],
                    anticipoId = a["anticipo_id"],
                    anticipoFolio = a["anticipo_folio"],
                    anticipoFecha = a["anticipo_fecha"],
                    montoAplicado = a["monto_aplicado"],
                    saldoActual = a["saldo_anticipo_actual"],
                    totalFacturaOrig = a["total_factura_orig"],
                    // Máximo recuperable = lo que se aplicó en esta relación específica
                    // (si hubiera NCs previas que ya lo recuperaron parcialmente,
                    //  aquí descontarías esa suma — por ahora es el monto íntegro)
                    maxRecuperable = Convert.ToDecimal(a["monto_aplicado"])
                })
            });
        }
    }
}