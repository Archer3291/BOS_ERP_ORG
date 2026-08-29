using Microsoft.AspNetCore.Authorization;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public partial class DatosGeneralesController : Utilities
    {
        public IActionResult BuscarDocumentoConCortes(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "id", id },
            { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
        };

                // 🔹 Consulta del encabezado
                string queryEncabezado = @"
            SELECT  
                em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0  
                         THEN '-' || num_to_letters(em.variacion)  
                         ELSE '' END AS folio,
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
                cfp.cve_sat as f_pago,
                em.cfdi,
                cc.rfc,
                em.par,
                cc.lim_crd,
                cc.n_cli,
                cc.pl_crd,
                cc.dir,
                cc.id_cliente,
                f.uuid,
                df.forma_pago, 
                df.uso_sugerido, 
                df.regimen_fiscal, 
                df.calle, 
                df.no_exterior, 
                df.no_interior, 
                df.colonia, 
                df.localidad, 
                df.municipio, 
                df.estado,                
                em.orden_compra as ordenCompra, 
                df.pais, 
                df.codigo_postal,
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli,
                cc.estatus_cliente::text AS estatus_cliente,
                cc.clasificacion::text AS clasificacion,
                COALESCE((
                    SELECT SUM(ca.saldo_pendiente)
                    FROM cartera_clientes ca
                    WHERE ca.cliente_id = cc.id_cliente
                    AND ca.cancelada = false
                ), 0) AS credito_usado
            FROM encabezadomov em
            LEFT JOIN catclientes cc  
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
            LEFT JOIN factura f ON f.encabezado_id = em.id_encabezado
            LEFT JOIN cat_f_pago cfp ON cfp.id_f_pago = em.f_pago
            WHERE em.id_encabezado = @id;
        ";

                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encabezadoResult.First();

                decimal limite = 0;
                decimal usado = 0;
                decimal disponible = 0;
                decimal porcentajeUso = 0;
                string estatusCredito = "SIN_LIMITE";
                string estatus_cliente = encabezado["estatus_cliente"].ToString();
                string clasificacion = encabezado["clasificacion"].ToString();

                // 🔹 Obtener valores seguros
                if (encabezado.ContainsKey("lim_crd") && encabezado["lim_crd"] != null)
                    limite = Convert.ToDecimal(encabezado["lim_crd"]);

                if (encabezado.ContainsKey("credito_usado") && encabezado["credito_usado"] != null)
                    usado = Convert.ToDecimal(encabezado["credito_usado"]);

                // 🔹 Calcular disponible
                disponible = limite - usado;

                // 🔹 Evaluar estatus
                if (limite <= 0)
                {
                    estatusCredito = "SIN_LIMITE";
                }
                else
                {
                    porcentajeUso = (usado / limite) * 100;

                    if (usado >= limite)
                        estatusCredito = "EXCEDIDO";
                    else if (porcentajeUso >= 80)
                        estatusCredito = "POR_VENCER";
                    else
                        estatusCredito = "DISPONIBLE";
                }

                // 🔹 Agregar al resultado
                encabezado["credito_usado"] = usado;
                encabezado["credito_disponible"] = disponible;
                encabezado["porcentaje_credito"] = porcentajeUso;
                encabezado["estatus_credito"] = estatusCredito;
                encabezado["estatus_cliente"] = estatus_cliente;
                encabezado["clasificacion"] = clasificacion;

                // 🔹 Obtener TODAS las adendas del cliente
                if (encabezado.ContainsKey("id_cliente") && encabezado["id_cliente"] != null)
                {
                    var clienteId = encabezado["id_cliente"];
                    var parametersAdendas = new Dictionary<string, object>
            {
                { "id_cliente", clienteId }
            };

                    string queryAdendas = @"
                SELECT 
                    id_addenda, 
                    nombre, 
                    xml_namespace, 
                    xml_prefix, 
                    version, 
                    data_template,
                    usar_conceptos,
                    created_at,
                    updated_at
                FROM cfdi_addenda_def 
                WHERE id_cliente = @id_cliente 
                  AND activo = true
                ORDER BY nombre";

                    var adendasResult = RunQuery(queryAdendas, parametersAdendas);
                    encabezado["adendas"] = adendasResult;
                }

                // 🔹 Parámetro de sucursal
                int sucursal = Convert.ToInt32(encabezado["suc"]);
                parameters.Add("sucursal", sucursal);

                // 🔹 Partidas + EXISTENCIAS + CORTES CONFIGURADOS
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
        pd.pedimento,  
        pd.tp_doc_ant AS comentario,
        COALESCE(stk.cantidadStock, 0) AS existencia,
        COALESCE(peds.pedimentos, '[]') AS pedimentos,
        -- ★ NUEVO: cortes configurados para esta partida, con sus asignaciones
        COALESCE(cortes.cortesJSON, '[]') AS cortes
    FROM partidasdoc pd
    LEFT JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
    LEFT JOIN LATERAL (
        SELECT json_agg(
            json_build_object('pedimento', rc.pedimento, 'cantidad', ABS(rc.cantidad))
        ) AS pedimentos
        FROM registro_compras rc
        WHERE rc.producto_id = pd.producto_id
          AND rc.encabezado_venta = pd.encabezado_id
    ) peds ON true
    LEFT JOIN (
        SELECT tp.producto_id, cs.id_sucursal, SUM(tp.cantidad) AS cantidadStock
        FROM tarima_productos tp
        INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
        INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
        INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
        INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
        INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
        WHERE ca.tipo = 'Stock'
        GROUP BY tp.producto_id, cs.id_sucursal
    ) stk ON stk.producto_id = pd.producto_id AND stk.id_sucursal = @sucursal
    -- ★ NUEVO: cortes lógicos + sus asignaciones, agregados como JSON
    LEFT JOIN LATERAL (
        SELECT json_agg(
            json_build_object(
                'id', c.id,
                'longitud', c.longitud,
                'cantidad', c.cantidad,
                'comentario', c.comentario,
                'precio', c.precio,
                'piezasUsadas', COALESCE((
                    SELECT json_agg(
                        json_build_object(
                            'folio', tpc.folio,
                            'idCorte', tpc.id_corte,
                            'longitud', a.longitud_origen,
                            'cantidad', a.cantidad_asignada,
                            'sobrante', a.sobrante
                        )
                    )
                    FROM pedido_detalle_corte_asignacion a
                    INNER JOIN tarima_productos_cortes tpc ON tpc.id_corte = a.tarima_producto_corte_id
                    WHERE a.pedido_detalle_corte_id = c.id
                ), '[]'::json)
            )
        ) AS cortesJSON
        FROM pedido_detalle_corte c
        WHERE c.pedido_detalle_id = pd.id_partidas
    ) cortes ON true
    WHERE pd.encabezado_id = @id
    ORDER BY pd.nro_part;
";

                var partidasResult = RunQuery(queryPartidas, parameters);
                encabezado["productos"] = partidasResult;

                var result = new List<Dictionary<string, object>> { encabezado };
                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesVT/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ★ NUEVO: trae de la base SQL Server los cortes + sus asignaciones por folio
        private Dictionary<int, List<Dictionary<string, object>>> ObtenerCortesPorPartidas(List<int> partidaIds)
        {
            var resultado = new Dictionary<int, List<Dictionary<string, object>>>();
            if (partidaIds == null || partidaIds.Count == 0) return resultado;

            string queryCortes = @"
        SELECT 
            pdc.id,
            pdc.pedido_detalle_id,
            pdc.longitud,
            pdc.cantidad,
            pdc.comentario,
            a.folio_origen,
            a.longitud_origen,
            a.cantidad_asignada,
            a.sobrante
        FROM PedidoDetalleCorte pdc
        LEFT JOIN PedidoDetalleCorteAsignacion a ON a.pedido_detalle_corte_id = pdc.id
        WHERE pdc.pedido_detalle_id IN (" + string.Join(",", partidaIds) + @")
        ORDER BY pdc.id;
    ";

            var rows = RunQuery(queryCortes, new Dictionary<string, object>()); // usa tu helper para SQL Server

            foreach (var row in rows)
            {
                var partidaId = Convert.ToInt32(row["pedido_detalle_id"]);
                if (!resultado.ContainsKey(partidaId))
                    resultado[partidaId] = new List<Dictionary<string, object>>();

                // Agrupar asignaciones dentro del corte correspondiente (si ya existe, solo agrega la asignación)
                var corteId = Convert.ToInt32(row["id"]);
                var corteExistente = resultado[partidaId].FirstOrDefault(c => Convert.ToInt32(c["id"]) == corteId);

                if (corteExistente == null)
                {
                    corteExistente = new Dictionary<string, object>
                    {
                        ["id"] = corteId,
                        ["longitud"] = row["longitud"],
                        ["cantidad"] = row["cantidad"],
                        ["comentario"] = row["comentario"],
                        ["piezasUsadas"] = new List<Dictionary<string, object>>()
                    };
                    resultado[partidaId].Add(corteExistente);
                }

                if (row["folio_origen"] != null)
                {
                    ((List<Dictionary<string, object>>)corteExistente["piezasUsadas"]).Add(new Dictionary<string, object>
                    {
                        ["folio"] = row["folio_origen"],
                        ["longitud"] = row["longitud_origen"],
                        ["cantidad"] = row["cantidad_asignada"],
                        ["sobrante"] = row["sobrante"]
                    });
                }
            }

            return resultado;
        }

        [HttpGet]
        public IActionResult ObtenerPiezasDisponibles(string productoId, int sucursal)
        {
            sucursal = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "cve_prod", productoId },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },

                };
                int idP = Convert.ToInt32(RunScalar("SELECT id_catproductos FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id ", parameters));
                parameters.Add("sucursal", sucursal); 
                parameters.Add("producto_id", idP); 
    

                string query = @"
            SELECT 
                tpc.id_corte,
                tpc.folio,
                tpc.longitud,
                tpc.cantidad
            FROM tarima_productos_cortes tpc
            INNER JOIN tarima_productos tp ON tp.id_tarima_producto = tpc.tarima_producto_id
            INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
            INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
            INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
            INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
            INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
            INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
            WHERE tp.producto_id = @producto_id
              AND cs.id_sucursal = @sucursal
              AND ca.tipo = 'Stock'
              AND tpc.activo = true
              AND tpc.cantidad > 0
            ORDER BY tpc.longitud DESC;
        ";

                var piezas = RunQuery(query, parameters);
                return Json(new { success = true, piezas });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesVT/ObtenerPiezasDisponibles");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult BuscarVTD(string nombre, int page = 1, int pageSize = 50)
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
               LOWER(em.folio) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'TYBCOT'
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
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'TYBCOT'";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }


//        public IActionResult BuscarDVIped(string nombre, int page = 1, int pageSize = 50)
//        {
//            var parameters = new Dictionary<string, object>();

//            string nat = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) == 2
//                ? "VIPED"
//                : "RMP";

//            string query = $@"
//SELECT 
//    em.id_encabezado,
//    em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
//        CASE WHEN em.variacion > 0 
//             THEN '-' || num_to_letters(em.variacion) 
//             ELSE '' END AS folio,
//    em.gen,
//    em.nat,
//    em.fch,
//    em.imp,
//    em.cli_prov,
//    em.usr0,
//    em.incoterm,
//    cc.n_cli
//FROM encabezadomov em
//INNER JOIN catclientes cc 
//    ON cc.id_cliente = em.refe 
//   AND cc.empresa_id = @empresa_id
//WHERE (
//       LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
//    OR LOWER(em.gen) LIKE LOWER(@nombre)
//    OR LOWER(em.nat) LIKE LOWER(@nombre)
//    OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
//)
//AND em.nat = @nat
//AND em.suc = @suc
//AND em.estatus_id = 1
//ORDER BY em.fch DESC
//OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

//            parameters.Add("nat", nat);
//            parameters.Add("nombre", $"%{nombre}%");
//            parameters.Add("offset", (page - 1) * pageSize);
//            parameters.Add("pageSize", pageSize);
//            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
//            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

//            var items = RunQuery(query, parameters);

//            // Total de registros
//            string queryTotal = @"
//SELECT COUNT(*) AS total
//FROM encabezadomov em
//WHERE (
//       LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
//    OR LOWER(em.gen) LIKE LOWER(@nombre)
//    OR LOWER(em.nat) LIKE LOWER(@nombre)
//    OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
//)
//AND em.nat = @nat
//AND em.suc = @suc
//AND em.estatus_id = 1";

//            var totalResult = RunQuery(queryTotal, new Dictionary<string, object>
//{
//    { "nombre", $"%{nombre}%" },
//    { "nat", nat },
//    { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
//});
//            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

//            return Json(new { items, total });
//        }

//        public IActionResult BuscarDVIrem(
//        string nombre = "",
//        int page = 1,
//        int pageSize = 25,
//        string cliente = "",
//        string modo = "documento")   // "parciales" | "documento" | "multiple"
//        {
//            var parameters = new Dictionary<string, object>();

//            // ── Filtro de cliente ────────────────────────────────────
//            string filtroCliente = !string.IsNullOrWhiteSpace(cliente)
//                ? "AND em.cli_prov = @cliente"
//                : "";

//            // ── Filtros adicionales por modo ─────────────────────────
//            //
//            //  "parciales"  → solo remisiones con al menos una partida
//            //                 pendiente/parcial en el tracking
//            //
//            //  "documento"  → todas las remisiones en estatus 1,
//            //                 EXCEPTO las que ya fueron facturadas
//            //                 completamente (todas sus partidas = completa)
//            //
//            //  "multiple"   → igual que "documento", pensado para
//            //                 selección masiva sin abrir partidas
//            //
//            string filtroModo = modo == "parciales"
//                ? @"AND EXISTS (
//                SELECT 1
//                FROM remision_partidas_facturadas rpf
//                WHERE rpf.encabezado_remision_id = em.id_encabezado
//                  AND rpf.estatus IN ('pendiente', 'parcial')
//            )"
//                : @"AND NOT EXISTS (
//                SELECT 1
//                FROM remision_partidas_facturadas rpf
//                WHERE rpf.encabezado_remision_id = em.id_encabezado
//                  AND (
//                      -- si hay tracking y TODAS son 'completa' → excluir
//                      (SELECT COUNT(*) FROM remision_partidas_facturadas rpf2
//                       WHERE rpf2.encabezado_remision_id = em.id_encabezado) > 0
//                      AND
//                      (SELECT COUNT(*) FROM remision_partidas_facturadas rpf3
//                       WHERE rpf3.encabezado_remision_id = em.id_encabezado
//                         AND rpf3.estatus != 'completa') = 0
//                  )
//                LIMIT 1
//            )";

//            string filtroEstatus = modo == "parciales"
//                ? "AND em.estatus_id =41"           
//                : "AND em.estatus_id =1";

//            // Nota: si no existe tracking todavía (remisión nueva sin inicializar)
//            // la condición NOT EXISTS devuelve false → la remisión SÍ aparece. Correcto.

//            // ── Query principal ──────────────────────────────────────
//            string query = $@"
//        SELECT
//            em.id_encabezado,
//            em.folio,
//            em.gen,
//            em.nat,
//            em.fch        AS fecha,
//            em.imp,
//            em.cli_prov,
//            em.usr0,
//            cc.n_cli,
 
//            -- Conteos para el modal (útiles en modo parciales y documento)
//            COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'pendiente') AS partidas_pendientes,
//            COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'parcial')   AS partidas_parciales,
//            COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'completa')  AS partidas_completas,
//            COUNT(rpf.id)                                           AS total_partidas_tracking,
 
//COALESCE(
//    SUM(
//        (rpf.cantidad_pendiente * rpf.precio_unitario
//        * (1 - rpf.descuento / 100.0))::numeric(18,2)
//    )
//    FILTER (WHERE rpf.estatus IN ('pendiente','parcial')),
//    0
//) AS importe_pendiente
 
//        FROM encabezadomov em
//        INNER JOIN catclientes cc
//            ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
//        LEFT JOIN remision_partidas_facturadas rpf
//            ON rpf.encabezado_remision_id = em.id_encabezado
//        WHERE em.nat = 'VIREM'
//          AND em.suc = @suc
//          AND (
//               LOWER(em.gen || '-' || em.nat || '-' ||
//                     EXTRACT(YEAR FROM em.fch)::text || '-' ||
//                     em.fol_doc) LIKE LOWER(@nombre)
//            OR LOWER(em.cli_prov)               LIKE LOWER(@nombre)
//            OR LOWER(cc.n_cli)                  LIKE LOWER(@nombre)
//          )
//          {filtroEstatus}
//          {filtroCliente}
//          {filtroModo}
//        GROUP BY
//            em.id_encabezado, em.gen, em.nat, em.fch,
//            em.fol_doc, em.variacion, em.imp, em.cli_prov, em.usr0, cc.n_cli
//        ORDER BY em.fch DESC
//        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

//            parameters.Add("nombre", $"%{nombre}%");
//            parameters.Add("offset", (page - 1) * pageSize);
//            parameters.Add("pageSize", pageSize);
//            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
//            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
//            if (!string.IsNullOrWhiteSpace(cliente))
//                parameters.Add("cliente", cliente);

//            var items = RunQuery(query, parameters);

//            // ── Query total ──────────────────────────────────────────
//            string queryTotal = $@"
//        SELECT COUNT(*) AS total
//        FROM encabezadomov em
//        INNER JOIN catclientes cc
//            ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
//        WHERE em.nat = 'VIREM'
//          AND em.suc = @suc
//          AND em.estatus_id = 1
//          AND (
//               LOWER(em.gen || '-' || em.nat || '-' ||
//                     EXTRACT(YEAR FROM em.fch)::text || '-' ||
//                     em.fol_doc) LIKE LOWER(@nombre)
//            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
//            OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
//          )
//          {filtroCliente}
//          {filtroModo}";

//            var totalParameters = new Dictionary<string, object>
//    {
//        { "nombre",     $"%{nombre}%" },
//        { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
//        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
//    };
//            if (!string.IsNullOrWhiteSpace(cliente))
//                totalParameters.Add("cliente", cliente);

//            var totalResult = RunQuery(queryTotal, totalParameters);
//            int total = totalResult?.Count > 0
//                ? Convert.ToInt32(totalResult[0]["total"])
//                : 0;

//            return Json(new { items, total });
//        }


  
    //    [HttpGet]
    //    public IActionResult VerificarOrdenCompra(string oc)
    //    {
    //        if (string.IsNullOrWhiteSpace(oc))
    //            return Json(new { existe = false });

    //        var parameters = new Dictionary<string, object>
    //{
    //    { "oc", oc.Trim() },
    //    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    //};

    //        string query = @"
    //    SELECT 
    //        f.id,
    //        f.serie,
    //        f.folio,
    //        f.oc,
    //        f.fecha,
    //        f.rfccliente,
    //        f.rsocliente,
    //        f.total,
    //        f.statusfactura,
    //        f.tipo,
    //        em.folio AS folio_interno
    //    FROM factura f
    //    LEFT JOIN encabezadomov em ON em.id_encabezado = f.encabezado_id
    //    WHERE LOWER(TRIM(f.oc)) = LOWER(TRIM(@oc))
    //      AND f.statusfactura NOT IN ('CANCELADA', 'CANCELADO')
    //    ORDER BY f.fecha DESC
    //    LIMIT 5";

    //        var resultados = RunQuery(query, parameters);

    //        if (resultados == null || resultados.Count == 0)
    //            return Json(new { existe = false });

    //        var usos = resultados.Select(r => new
    //        {
    //            id = r["id"],
    //            folio = $"{r["serie"]}-{r["folio"]}",
    //            folioInterno = r["folio_interno"]?.ToString() ?? "",
    //            oc = r["oc"]?.ToString() ?? "",
    //            fecha = r["fecha"] is DateTime dt
    //                            ? dt.ToString("dd/MM/yyyy")
    //                            : r["fecha"]?.ToString() ?? "",
    //            rfc = r["rfccliente"]?.ToString() ?? "",
    //            cliente = r["rsocliente"]?.ToString() ?? "",
    //            total = Convert.ToDecimal(r["total"] ?? 0),
    //            estatus = r["statusfactura"]?.ToString() ?? "",
    //            tipo = r["tipo"]?.ToString() ?? ""
    //        }).ToList();

    //        return Json(new { existe = true, usos });
    //    }
    }
}