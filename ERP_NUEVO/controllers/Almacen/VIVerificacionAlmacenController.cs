using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using System.Configuration;

namespace BOS_ERP.Controllers.Almacen
{
    [Authorize]
    public class VIVerificacionAlmacenController : Utilities
    {
        [HttpGet]
        [HttpGet]
        public IActionResult ObtenerPartidasPedido(int idEncabezadoPadre)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

                // ── 1. Resolver los hijos desde documentos_relacionados ──
                var relParams = new Dictionary<string, object>
        {
            { "id_padre", idEncabezadoPadre },
            { "empresa_id", empresaId }
        };

                var relResult = RunQuery(@"
            SELECT id_encabezado_normal, id_encabezado_modula, folio_normal, folio_modula
            FROM documentos_relacionados
            WHERE id_encabezado_padre = @id_padre
              AND empresa_id          = @empresa_id
            ORDER BY id_relacion DESC
            LIMIT 1",
                    relParams);

                int? idNormal = null, idModula = null;

                if (relResult?.Count > 0)
                {
                    var rel = relResult[0];

                    // GetInt en ambos: RunQuery devuelve las columnas NULL como C# `null`, no
                    // como DBNull.Value, así que el check `!= DBNull.Value` era SIEMPRE cierto
                    // y Convert.ToInt32(null) devolvía 0. Un pedido que solo tenía Modula
                    // quedaba con idNormal = 0 y no cargaba el encabezado.
                    idNormal = GetInt(rel["id_encabezado_normal"]);
                    idModula = GetInt(rel["id_encabezado_modula"]);

                    // Un 0 tampoco es un documento: se trata como ausente.
                    if (idNormal == 0) idNormal = null;
                    if (idModula == 0) idModula = null;
                }
                else
                {
                    // Fallback: pedido viejo sin split — tratarlo como "normal" directo
                    idNormal = idEncabezadoPadre;
                }

                if (idNormal == null && idModula == null)
                    return Json(new { success = false, message = "Pedido no encontrado o sin documentos asociados." });

                // ── 2. Encabezado de referencia (folio/cliente) desde cualquiera que exista ──
                var encParams = new Dictionary<string, object>
        {
            { "id", idNormal ?? idModula },
            { "empresa_id", empresaId }
        };

                string queryEnc = @"
            SELECT
                em.id_encabezado, em.folio, em.cli_prov, em.ccy, em.par,
                em.vdr_cpr, em.coment1, em.coment_aut, em.mdp, em.fch, em.tipo_proceso,
                cc.n_cli, cc.rfc,
                COALESCE(cc.dir,'') || CHR(10) || COALESCE(cc.col,'') || CHR(10) || COALESCE(cc.pob,'') AS info_cli
            FROM encabezadomov em
            INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
            WHERE em.id_encabezado = @id";

                var encResult = RunQuery(queryEnc, encParams);
                if (encResult == null || encResult.Count == 0)
                    return Json(new { success = false, message = "Encabezado no encontrado." });

                var enc = encResult[0];

                // ── 3. Partidas de ambos orígenes vía UNION ALL, con marca de origen ──
                //     origen_almacen: 'stock' (documento normal) | 'modula' (documento MODPED)
                //     Cada rama ahora reporta la existencia de AMBOS almacenes (Stock y Modula):
                //     antes la rama contraria iba hardcodeada en 0, así que el verificador no
                //     veía que un pedido dividido tenía material en el otro almacén.
                string queryPartidas = $@"
            SELECT * FROM (

                -- ═══ PARTIDAS DE STOCK (documento normal) ═══
                SELECT
                    pd.id_partidas,
                    pd.cve_prod,
                    pd.descr_prod,
                    pd.cant_ud                          AS cantidad_pedida,
                    pd.pv_prod                          AS precio,
                    pd.dto1                             AS descuento,
                    pd.ud                                AS unidad,
                    pd.tp_doc_ant                        AS comentario,
                    cp.id_catproductos                   AS id_producto,
                    'stock'                              AS origen_almacen,
                    @id_normal::int                      AS id_encabezado_origen,

                    COALESCE((
                        SELECT SUM(vd2.cantidad_verificada)
                        FROM verificacion_detalle vd2
                        WHERE vd2.id_partida = pd.id_partidas
                          AND vd2.encabezado_verificacion_id IS NOT NULL
                    ), 0) AS cantidad_ya_verificada,

                    COALESCE((
                        SELECT vd3.cantidad_verificada
                        FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NULL
                        ORDER BY vd3.id DESC LIMIT 1
                    ), 0) AS cantidad_guardada_temp,

                    COALESCE((SELECT vd3.lote FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NULL
                        ORDER BY vd3.id DESC LIMIT 1), '') AS lote_guardado,

                    COALESCE((SELECT vd3.ubicacion FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NULL
                        ORDER BY vd3.id DESC LIMIT 1), '') AS ubicacion_guardada,

                    COALESCE((
                        SELECT SUM(tp.cantidad)
                        FROM tarima_productos tp
                        INNER JOIN cattarimas ct   ON ct.id_tarima   = tp.tarima_id
                        INNER JOIN catniveles cn   ON cn.id_nivel    = ct.nivel_id
                        INNER JOIN catcolumnas col ON col.id_columna = cn.columna_id
                        INNER JOIN catracks cr     ON cr.id_rack     = col.rack_id
                        INNER JOIN catalmacenes ca ON ca.id_almacen  = cr.almacen_id
                        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                        WHERE tp.producto_id = cp.id_catproductos
                          AND cs.id_sucursal = @sucursal
                          AND ca.tipo        = 'Stock'
                          AND tp.cantidad    > 0
                    ), 0) AS existencia_stock,

                    {SqlExistencia("cp.id_catproductos", "Modula")} AS existencia_modula,

                    COALESCE((
                        SELECT json_agg(json_build_object(
                            'cve_almacen', x.cve_almacen, 'n_almacen', x.descripcion,
                            'stock', x.stock, 'tipo', 'Stock')
                            ORDER BY x.stock DESC)
                        FROM (
                            SELECT ca.cve_almacen, ca.descripcion, SUM(tp.cantidad) AS stock
                            FROM tarima_productos tp
                            INNER JOIN cattarimas ct   ON ct.id_tarima   = tp.tarima_id
                            INNER JOIN catniveles cn   ON cn.id_nivel    = ct.nivel_id
                            INNER JOIN catcolumnas col ON col.id_columna = cn.columna_id
                            INNER JOIN catracks cr     ON cr.id_rack     = col.rack_id
                            INNER JOIN catalmacenes ca ON ca.id_almacen  = cr.almacen_id
                            INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                            WHERE tp.producto_id = cp.id_catproductos
                              AND cs.id_sucursal = @sucursal
                              AND ca.tipo = 'Stock'
                              AND tp.cantidad > 0
                            GROUP BY ca.cve_almacen, ca.descripcion
                        ) x
                    ), '[]'::json)::text AS existencia_por_almacen_stock,

                    {SqlExistenciaPorAlmacen("cp.id_catproductos", "Modula")} AS existencia_por_almacen_modula,

                    COALESCE((
                        SELECT dd.cantidad_fisica
                        FROM discrepancia_fisica dd
                        INNER JOIN verificacion_detalle vdx ON vdx.id = dd.id_verificacion_detalle
                        WHERE vdx.id_partida = pd.id_partidas
                          AND vdx.encabezado_verificacion_id IS NULL
                        ORDER BY dd.fecha_registro DESC LIMIT 1
                    ), NULL) AS cantidad_fisica_discrepancia,

                    COALESCE((
                        SELECT dd.motivo
                        FROM discrepancia_fisica dd
                        INNER JOIN verificacion_detalle vdx ON vdx.id = dd.id_verificacion_detalle
                        WHERE vdx.id_partida = pd.id_partidas
                          AND vdx.encabezado_verificacion_id IS NULL
                        ORDER BY dd.fecha_registro DESC LIMIT 1
                    ), NULL) AS motivo_discrepancia,

                    pd.nro_part

                FROM partidasdoc pd
                LEFT JOIN catproductos cp
                    ON cp.cve_prod = pd.cve_prod AND cp.empresa_id = @empresa_id
                WHERE @id_normal::int IS NOT NULL
                  AND pd.encabezado_id = @id_normal::int
                  AND COALESCE((
                        SELECT SUM(vd3.cantidad_verificada)
                        FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NOT NULL
                      ), 0) < pd.cant_ud

UNION ALL

                -- ═══ PARTIDAS DE MODULA (documento MODPED) ═══
                SELECT
                    pd.id_partidas,
                    pd.cve_prod,
                    pd.descr_prod,
                    pd.cant_ud                          AS cantidad_pedida,
                    pd.pv_prod                          AS precio,
                    pd.dto1                             AS descuento,
                    pd.ud                                AS unidad,
                    pd.tp_doc_ant                        AS comentario,
                    cp.id_catproductos                   AS id_producto,
                    'modula'                             AS origen_almacen,
                    @id_modula::int                      AS id_encabezado_origen,

                    COALESCE((
                        SELECT SUM(vd2.cantidad_verificada)
                        FROM verificacion_detalle vd2
                        WHERE vd2.id_partida = pd.id_partidas
                          AND vd2.encabezado_verificacion_id IS NOT NULL
                    ), 0) AS cantidad_ya_verificada,

                    COALESCE((
                        SELECT vd3.cantidad_verificada
                        FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NULL
                        ORDER BY vd3.id DESC LIMIT 1
                    ), 0) AS cantidad_guardada_temp,

                    COALESCE((SELECT vd3.lote FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NULL
                        ORDER BY vd3.id DESC LIMIT 1), '') AS lote_guardado,

                    COALESCE((SELECT vd3.ubicacion FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NULL
                        ORDER BY vd3.id DESC LIMIT 1), '') AS ubicacion_guardada,

                    {SqlExistencia("cp.id_catproductos", "Stock")} AS existencia_stock,

                    -- ── Existencia Modula: mismo patrón que Stock, filtrando tipo = 'Modula' ──
                    COALESCE((
                        SELECT SUM(tp.cantidad)
                        FROM tarima_productos tp
                        INNER JOIN cattarimas ct   ON ct.id_tarima   = tp.tarima_id
                        INNER JOIN catniveles cn   ON cn.id_nivel    = ct.nivel_id
                        INNER JOIN catcolumnas col ON col.id_columna = cn.columna_id
                        INNER JOIN catracks cr     ON cr.id_rack     = col.rack_id
                        INNER JOIN catalmacenes ca ON ca.id_almacen  = cr.almacen_id
                        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                        WHERE tp.producto_id = cp.id_catproductos
                          AND cs.id_sucursal = @sucursal
                          AND ca.tipo        = 'Modula'
                          AND tp.cantidad    > 0
                    ), 0) AS existencia_modula,

                    {SqlExistenciaPorAlmacen("cp.id_catproductos", "Stock")} AS existencia_por_almacen_stock,

                    -- ── Detalle por almacén Modula (JSON), mismo patrón que Stock ──
                    COALESCE((
                        SELECT json_agg(
                            json_build_object(
                                'cve_almacen', x.cve_almacen,
                                'n_almacen',   x.descripcion,
                                'stock',       x.stock,
                                'tipo',        'Modula'
                            )
                            ORDER BY x.stock DESC
                        )
                        FROM (
                            SELECT ca.cve_almacen, ca.descripcion, SUM(tp.cantidad) AS stock
                            FROM tarima_productos tp
                            INNER JOIN cattarimas ct   ON ct.id_tarima   = tp.tarima_id
                            INNER JOIN catniveles cn   ON cn.id_nivel    = ct.nivel_id
                            INNER JOIN catcolumnas col ON col.id_columna = cn.columna_id
                            INNER JOIN catracks cr     ON cr.id_rack     = col.rack_id
                            INNER JOIN catalmacenes ca ON ca.id_almacen  = cr.almacen_id
                            INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                            WHERE tp.producto_id = cp.id_catproductos
                              AND cs.id_sucursal = @sucursal
                              AND ca.tipo = 'Modula'
                              AND tp.cantidad > 0
                            GROUP BY ca.cve_almacen, ca.descripcion
                        ) x
                    ), '[]'::json)::text AS existencia_por_almacen_modula,

                    NULL::numeric AS cantidad_fisica_discrepancia,
                    NULL::text    AS motivo_discrepancia,

                    pd.nro_part

                FROM partidasdoc pd
                LEFT JOIN catproductos cp
                    ON cp.cve_prod = pd.cve_prod AND cp.empresa_id = @empresa_id
                WHERE @id_modula::int IS NOT NULL
                  AND pd.encabezado_id = @id_modula::int
                  AND COALESCE((
                        SELECT SUM(vd3.cantidad_verificada)
                        FROM verificacion_detalle vd3
                        WHERE vd3.id_partida = pd.id_partidas
                          AND vd3.encabezado_verificacion_id IS NOT NULL
                      ), 0) < pd.cant_ud

            ) todas
            ORDER BY origen_almacen, nro_part";

                var partidasParams = new Dictionary<string, object>
        {
            { "empresa_id", empresaId },
            { "sucursal",   sucursalId },
            { "id_normal",  (object)idNormal ?? DBNull.Value },
            { "id_modula",  (object)idModula ?? DBNull.Value }
        };

                var partidas = RunQuery(queryPartidas, partidasParams);

                var partidasResult = partidas.Select(p =>
                {
                    decimal cantidadPedida = Convert.ToDecimal(p["cantidad_pedida"]);
                    decimal cantidadYaVerif = Convert.ToDecimal(p["cantidad_ya_verificada"]);
                    decimal cantidadPendiente = cantidadPedida - cantidadYaVerif;
                    string unidad = p["unidad"]?.ToString() ?? "PZA";
                    bool esSrv = unidad == "SRV" || unidad == "SERVICIO";
                    string origen = p["origen_almacen"]?.ToString() ?? "stock";

                    var almacenesStock = new List<Dictionary<string, object>>();
                    var almacenesModula = new List<Dictionary<string, object>>();
                    try
                    {
                        almacenesStock = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(
                            p["existencia_por_almacen_stock"]?.ToString() ?? "[]") ?? new();
                        almacenesModula = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(
                            p["existencia_por_almacen_modula"]?.ToString() ?? "[]") ?? new();
                    }
                    catch { }

                    decimal existenciaStock = Convert.ToDecimal(p["existencia_stock"]);
                    decimal existenciaModula = Convert.ToDecimal(p["existencia_modula"]);

                    decimal? cantFisica = null;
                    if (p["cantidad_fisica_discrepancia"] != null && p["cantidad_fisica_discrepancia"] != DBNull.Value)
                        cantFisica = Convert.ToDecimal(p["cantidad_fisica_discrepancia"]);

                    return (object)new
                    {
                        id_partida = p["id_partidas"],
                        cve_prod = p["cve_prod"]?.ToString(),
                        descripcion = p["descr_prod"]?.ToString(),
                        cantidad_pedida = cantidadPendiente,
                        cantidad_total_pedida = cantidadPedida,
                        cantidad_ya_verificada = cantidadYaVerif,
                        cantidad_verificada = Convert.ToDecimal(p["cantidad_guardada_temp"]),
                        lote = p["lote_guardado"]?.ToString() ?? "",
                        ubicacion = p["ubicacion_guardada"]?.ToString() ?? "",
                        precio = p["precio"],
                        descuento = p["descuento"],
                        unidad,
                        comentario = p["comentario"]?.ToString() ?? "",
                        id_producto = Convert.ToInt32(p["id_producto"] ?? 0),
                        es_servicio = esSrv,

                        // ── NUEVO: origen para pintar badge y decidir flujo al finalizar ──
                        origen_almacen = origen,                                   // 'stock' | 'modula'
                        id_encabezado_origen = Convert.ToInt32(p["id_encabezado_origen"]),

                        existencia_stock = esSrv ? (object)"∞" : existenciaStock,
                        existencia_modula = esSrv ? (object)"∞" : existenciaModula,
                        existencia_total = esSrv ? (object)"∞" : (existenciaStock + existenciaModula),
                        existencia_por_almacen_stock = almacenesStock,
                        existencia_por_almacen_modula = almacenesModula,
                        tiene_stock = !esSrv && (origen == "stock" ? existenciaStock > 0 : existenciaModula > 0),

                        tiene_discrepancia = cantFisica.HasValue,
                        cantidad_fisica = cantFisica,
                        motivo_discrepancia = p["motivo_discrepancia"]?.ToString()
                    };
                }).ToList();

                return Json(new
                {
                    success = true,
                    id_encabezado_padre = idEncabezadoPadre,
                    id_encabezado_normal = idNormal,
                    id_encabezado_modula = idModula,
                    encabezado = new
                    {
                        id_encabezado = enc["id_encabezado"],
                        folio = enc["folio"],
                        cli_prov = enc["cli_prov"],
                        n_cli = enc["n_cli"],
                        rfc = enc["rfc"],
                        info_cli = enc["info_cli"],
                        ccy = enc["ccy"],
                        par = enc["par"],
                        vdr_cpr = enc["vdr_cpr"],
                        coment1 = enc["coment1"],
                        coment_aut = enc["coment_aut"],
                        mdp = enc["mdp"],
                        fch = enc["fch"],
                        tipo_proceso = enc["tipo_proceso"]
                    },
                    partidas = partidasResult
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        // ─── Guardar discrepancia física ─────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Registrar discrepancia física")]
        public JsonResult GuardarDiscrepanciaFisica(
            int idPartida,
            int idEncabezadoPedido,
            decimal cantidadFisica,
            string motivo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo))
                    return Json(new { success = false, message = "Debe indicar el motivo de la discrepancia." });

                if (cantidadFisica < 0)
                    return Json(new { success = false, message = "La cantidad física no puede ser negativa." });

                int usuarioId = GetUserId(User.Identity.Name);

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // Buscar o crear el registro en verificacion_detalle
                            var vdExistente = RunQuery(@"
                                SELECT id
                                FROM verificacion_detalle
                                WHERE id_partida = @id_part
                                  AND encabezado_verificacion_id IS NULL
                                ORDER BY id DESC
                                LIMIT 1",
                                new Dictionary<string, object> { { "id_part", idPartida } },
                                false, conn, tx);

                            int idVerDet;

                            if (vdExistente?.Count > 0)
                            {
                                idVerDet = Convert.ToInt32(vdExistente[0]["id"]);
                            }
                            else
                            {
                                // Crear entrada temporal (sin encabezado_verificacion_id)
                                var newVd = RunQuery(@"
                                    INSERT INTO verificacion_detalle
                                        (encabezado_verificacion_id, id_partida,
                                         cve_almacen, n_almacen,
                                         cantidad_verificada, lote, ubicacion,
                                         usuario_id, fecha_inicio_surtido, usuario_inicio_id)
                                    VALUES
                                        (NULL, @id_part,
                                         '', '',
                                         0, '', '',
                                         @usr, NOW(), @usr)
                                    RETURNING id",
                                    new Dictionary<string, object>
                                    {
                                        { "id_part", idPartida },
                                        { "usr",     usuarioId }
                                    }, false, conn, tx);

                                idVerDet = Convert.ToInt32(newVd[0]["id"]);
                            }

                            // Obtener cantidad del sistema. El tipo de almacén se decide por el
                            // documento de la partida: si viene de un MODPED la existencia de
                            // referencia es la de Modula, no la de Stock (antes siempre 'Stock',
                            // lo que daba una diferencia falsa para partidas Modula).
                            var infoPartida = RunQuery($@"
    SELECT pd.cant_ud AS cantidad_pedida,
           pd.cve_prod, pd.descr_prod,
           cp.id_catproductos AS id_producto,
           {SqlExistenciaConTipoExpr("cp.id_catproductos",
                "CASE WHEN em.tp_mov = 'MODPED' THEN 'Modula' ELSE 'Stock' END")} AS cantidad_sistema
    FROM partidasdoc pd
    LEFT JOIN encabezadomov em
        ON em.id_encabezado = pd.encabezado_id
    LEFT JOIN catproductos cp
        ON cp.cve_prod = pd.cve_prod
       AND cp.empresa_id = @empresa_id
    WHERE pd.id_partidas = @id_part",
      new Dictionary<string, object>
      {
        { "id_part",    idPartida },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
        { "sucursal",   Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
      }, false, conn, tx);

                            decimal cantSistema = infoPartida?.Count > 0
    ? Convert.ToDecimal(infoPartida[0]["cantidad_sistema"])
    : 0;

                            // Insertar/actualizar en discrepancia_fisica
                            RunQuery(@"
                                INSERT INTO discrepancia_fisica
                                    (id_verificacion_detalle, id_partida,
                                     id_encabezado_pedido,
                                     cantidad_sistema, cantidad_fisica,
                                     diferencia, motivo,
                                     usuario_id, fecha_registro)
                                VALUES
                                    (@id_vd, @id_part,
                                     @enc_ped,
                                     @cant_sis, @cant_fis,
                                     (@cant_sis - @cant_fis), @motivo,
                                     @usr, NOW())
                                ON CONFLICT (id_verificacion_detalle)
                                DO UPDATE SET
                                    cantidad_sistema = EXCLUDED.cantidad_sistema,
                                    cantidad_fisica  = EXCLUDED.cantidad_fisica,
                                    diferencia       = EXCLUDED.diferencia,
                                    motivo           = EXCLUDED.motivo,
                                    usuario_id       = EXCLUDED.usuario_id,
                                    fecha_registro   = EXCLUDED.fecha_registro",
                                new Dictionary<string, object>
                                {
                                    { "id_vd",    idVerDet },
                                    { "id_part",  idPartida },
                                    { "enc_ped",  idEncabezadoPedido },
                                    { "cant_sis", cantSistema },
                                    { "cant_fis", cantidadFisica },
                                    { "motivo",   motivo },
                                    { "usr",      usuarioId }
                                }, false, conn, tx);

                            tx.Commit();

                            return Json(new
                            {
                                success = true,
                                cantidad_fisica = cantidadFisica,
                                cantidad_sistema = cantSistema,
                                diferencia = cantSistema - cantidadFisica,
                                mensaje = $"Discrepancia registrada. Físico: {cantidadFisica}, Sistema: {cantSistema}, Diferencia: {cantSistema - cantidadFisica}"
                            });
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Guardar discrepancia física + generar/actualizar documento DISM ─────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Registrar discrepancia física con documento DISM")]
        public JsonResult GuardarDiscrepanciaConDocumento(
            int idPartida,
            int idEncabezadoPedido,
            decimal cantidadFisica,
            string motivo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo))
                    return Json(new { success = false, message = "Debe indicar el motivo de la discrepancia." });

                if (cantidadFisica < 0)
                    return Json(new { success = false, message = "La cantidad física no puede ser negativa." });

                int usuarioId = GetUserId(User.Identity.Name);

                // Cargar datos del pedido padre
                var parentResult = RunQuery(@"
            SELECT em.usr0, em.fch0, em.usr1, em.fch1,
                   em.usr2, em.fch2, em.cli_prov, em.ccy,
                   em.par, em.vdr_cpr, em.coment1, em.coment_aut,
                   em.mdp, em.f_pago, em.refe, em.flete,
                   em.pl_dias, em.fch_pg_entrega, em.cfdi,
                   em.orden_compra, em.tipo_proceso
            FROM encabezadomov em
            WHERE em.id_encabezado = @id",
                    new Dictionary<string, object> { { "id", idEncabezadoPedido } });

                if (parentResult == null || parentResult.Count == 0)
                    return Json(new { success = false, message = "Pedido padre no encontrado." });

                var parent = parentResult[0];

                // Cargar datos de la partida afectada. El tipo de almacén para la existencia de
                // referencia se decide por el documento: MODPED → Modula, resto → Stock.
                var infoPartida = RunQuery($@"
    SELECT pd.cant_ud AS cantidad_pedida,
           pd.cve_prod, pd.descr_prod,
           pd.pv_prod, pd.dto1, pd.ud, pd.tp_doc_ant,
           cp.id_catproductos AS id_producto,
           {SqlExistenciaConTipoExpr("cp.id_catproductos",
                "CASE WHEN em.tp_mov = 'MODPED' THEN 'Modula' ELSE 'Stock' END")} AS cantidad_sistema
    FROM partidasdoc pd
    LEFT JOIN encabezadomov em
        ON em.id_encabezado = pd.encabezado_id
    LEFT JOIN catproductos cp
        ON cp.cve_prod = pd.cve_prod
       AND cp.empresa_id = @empresa_id
    WHERE pd.id_partidas = @id_part",
    new Dictionary<string, object>
    {
        { "id_part",    idPartida },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
        { "sucursal",   Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
    });

                if (infoPartida == null || infoPartida.Count == 0)
                    return Json(new { success = false, message = "Partida no encontrada." });

                var partida = infoPartida[0];
                decimal cantSist = infoPartida?.Count > 0 ? Convert.ToDecimal(infoPartida[0]["cantidad_sistema"]) : 0;
                decimal diferencia = cantSist - cantidadFisica;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // ── 1. Buscar o crear registro en verificacion_detalle ──────
                            var vdExistente = RunQuery(@"
                        SELECT id
                        FROM verificacion_detalle
                        WHERE id_partida                  = @id_part
                          AND encabezado_verificacion_id IS NULL
                        ORDER BY id DESC
                        LIMIT 1",
                                new Dictionary<string, object> { { "id_part", idPartida } },
                                false, conn, tx);

                            int idVerDet;
                            if (vdExistente?.Count > 0)
                            {
                                idVerDet = Convert.ToInt32(vdExistente[0]["id"]);
                            }
                            else
                            {
                                var newVd = RunQuery(@"
                            INSERT INTO verificacion_detalle
                                (encabezado_verificacion_id, id_partida,
                                 cve_almacen, n_almacen,
                                 cantidad_verificada, lote, ubicacion,
                                 usuario_id, fecha_inicio_surtido, usuario_inicio_id)
                            VALUES
                                (NULL, @id_part,
                                 '', '',
                                 0, '', '',
                                 @usr, NOW(), @usr)
                            RETURNING id",
                                    new Dictionary<string, object>
                                    {
                                { "id_part", idPartida },
                                { "usr",     usuarioId }
                                    }, false, conn, tx);

                                idVerDet = Convert.ToInt32(newVd[0]["id"]);
                            }

                            // ── 2. Insertar / actualizar discrepancia_fisica ───────────
                            RunQuery(@"
                        INSERT INTO discrepancia_fisica
                            (id_verificacion_detalle, id_partida,
                             id_encabezado_pedido,
                             cantidad_sistema, cantidad_fisica,
                             diferencia, motivo,
                             usuario_id, fecha_registro)
                        VALUES
                            (@id_vd, @id_part,
                             @enc_ped,
                             @cant_sis, @cant_fis,
                             (@cant_sis - @cant_fis), @motivo,
                             @usr, NOW())
                        ON CONFLICT (id_verificacion_detalle)
                        DO UPDATE SET
                            cantidad_sistema = EXCLUDED.cantidad_sistema,
                            cantidad_fisica  = EXCLUDED.cantidad_fisica,
                            diferencia       = EXCLUDED.diferencia,
                            motivo           = EXCLUDED.motivo,
                            usuario_id       = EXCLUDED.usuario_id,
                            fecha_registro   = EXCLUDED.fecha_registro",
                                new Dictionary<string, object>
                                {
                            { "id_vd",    idVerDet },
                            { "id_part",  idPartida },
                            { "enc_ped",  idEncabezadoPedido },
                            { "cant_sis", cantSist },
                            { "cant_fis", cantidadFisica },
                            { "motivo",   motivo },
                            { "usr",      usuarioId }
                                }, false, conn, tx);

                            // ── 3. Buscar DISM existente para esta partida + pedido ────
                            var dismExistente = RunQuery(@"
                        SELECT em.id_encabezado, em.folio
                        FROM encabezadomov em
                        WHERE em.tp_mov          = 'DISM'
                          AND em.encabezados_padre = @enc_ped
                          AND em.estatus_id       != 0
                          AND EXISTS (
                              SELECT 1
                              FROM partidasdoc pd2
                              WHERE pd2.encabezado_id = em.id_encabezado
                                AND pd2.cve_prod      = @cve_prod
                          )
                        ORDER BY em.id_encabezado DESC
                        LIMIT 1",
                                new Dictionary<string, object>
                                {
                            { "enc_ped",  idEncabezadoPedido },
                            { "cve_prod", partida["cve_prod"]?.ToString() }
                                }, false, conn, tx);

                            string folioDism = null;
                            int idDism = 0;

                            if (diferencia != 0)
                            {
                                decimal impPartida = diferencia * Convert.ToDecimal(partida["pv_prod"]);
                                decimal dtoPartida = impPartida * (Convert.ToDecimal(partida["dto1"]) / 100m);

                                if (dismExistente?.Count > 0)
                                {
                                    // ── DISM ya existe → actualizar partida y recalcular totales ──
                                    idDism = Convert.ToInt32(dismExistente[0]["id_encabezado"]);
                                    folioDism = dismExistente[0]["folio"]?.ToString();

                                    RunQuery(@"
                                UPDATE partidasdoc
                                   SET cant_ud  = @cant,
                                       imp_part = @imp,
                                       dto1     = @dto
                                 WHERE encabezado_id = @id_dism
                                   AND cve_prod      = @cve_prod",
                                        new Dictionary<string, object>
                                        {
                                    { "id_dism",  idDism },
                                    { "cve_prod", partida["cve_prod"]?.ToString() },
                                    { "cant",     diferencia },
                                    { "imp",      impPartida },
                                    { "dto",      Convert.ToDecimal(partida["dto1"]) }
                                        }, false, conn, tx);

                                    RunQuery(@"
                                UPDATE encabezadomov
                                   SET imp        = (
                                           SELECT COALESCE(SUM(imp_part), 0)
                                           FROM partidasdoc
                                           WHERE encabezado_id = @id_dism),
                                       dto        = (
                                           SELECT COALESCE(SUM(imp_part * dto1 / 100), 0)
                                           FROM partidasdoc
                                           WHERE encabezado_id = @id_dism),
                                       coment_aut = @motivo,
                                       usr2       = @usr,
                                       fch2       = NOW()
                                 WHERE id_encabezado = @id_dism",
                                        new Dictionary<string, object>
                                        {
                                    { "id_dism", idDism },
                                    { "motivo",  $"Discrepancia física: {motivo}" },
                                    { "usr",     usuarioId }
                                        }, false, conn, tx);
                                }
                                else
                                {
                                    // ── No existe DISM → crear uno nuevo ──────────────
                                    var encabezadoDism = new DocumentoEncabezado
                                    {
                                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                                        IdArea = 6,
                                        IdTpDoc = 85,
                                        UsrDep = GetAreaName(User.Identity.Name),
                                        Anio = DateTime.Now.Year,
                                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                                        Alm = "",
                                        Fch = DateTime.Now,
                                        TpMov = "DISM",
                                        ComentAut = $"Discrepancia física: {motivo}",
                                        UsrDoc = User.Identity.Name,
                                        FchCap = DateTime.Now,
                                        Usr0 = Convert.ToInt32(parent["usr0"]),
                                        Fch0 = Convert.ToDateTime(parent["fch0"]),
                                        Usr1 = Convert.ToInt32(parent["usr1"]),
                                        Fch1 = Convert.ToDateTime(parent["fch1"]),
                                        Usr2 = usuarioId,
                                        Fch2 = DateTime.Now,
                                        Imp = impPartida,
                                        Dto = dtoPartida,
                                        CliProv = parent["cli_prov"]?.ToString(),
                                        Ccy = parent["ccy"]?.ToString(),
                                        Estatus = 1,
                                        Ref = Convert.ToInt32(parent["refe"]),
                                        Flete = Convert.ToDecimal(parent["flete"]),
                                        VdrCpr = parent["vdr_cpr"]?.ToString(),
                                        Coment1 = parent["coment1"]?.ToString(),
                                        EncabezadoPadre = idEncabezadoPedido,
                                        PlDias = Convert.ToInt32(parent["pl_dias"]),
                                        FchPgEntrega = Convert.ToDateTime(parent["fch_pg_entrega"]),
                                        Par = Convert.ToDecimal(parent["par"]),
                                        FPago = Convert.ToInt32(parent["f_pago"]),
                                        Mdp = parent["mdp"]?.ToString(),
                                        TipoPoceso = "discrepancia",
                                        CFDI = parent["cfdi"]?.ToString(),
                                        NatDocPadreChar = parent["orden_compra"]?.ToString()
                                    };

                                    var partidaDism = new List<PartidaDocumento>
                            {
                                new PartidaDocumento
                                {
                                    CveProd   = partida["cve_prod"]?.ToString(),
                                    DescrProd = partida["descr_prod"]?.ToString(),
                                    CantUd    = diferencia,
                                    PvProd    = Convert.ToDecimal(partida["pv_prod"]),
                                    Dto1      = Convert.ToDecimal(partida["dto1"]),
                                    ImpPart   = impPartida,
                                    Ud        = partida["ud"]?.ToString() ?? "PZA",
                                    IdProducto = partida["id_producto"] != DBNull.Value
                                                    ? Convert.ToInt32(partida["id_producto"])
                                                    : 0,
                                    TpDocAnt  = partida["tp_doc_ant"]?.ToString() ?? ""
                                }
                            };

                                    var folio = GenerarDocumentoConPartidas(encabezadoDism, partidaDism, conn, tx);
                                    idDism = Convert.ToInt32(folio["IdEncabezado"]);
                                    folioDism = folio["folio_generado"]?.ToString();
                                }
                            }
                            else
                            {
                                // ── diferencia == 0: si había DISM previo, cancelarlo ─
                                if (dismExistente?.Count > 0)
                                {
                                    int idDismViejo = Convert.ToInt32(dismExistente[0]["id_encabezado"]);
                                    folioDism = dismExistente[0]["folio"]?.ToString();

                                    RunQuery(@"
                                UPDATE encabezadomov
                                   SET estatus_id = 0,
                                       coment_aut = @motivo,
                                       usr2       = @usr,
                                       fch2       = NOW()
                                 WHERE id_encabezado = @id_dism",
                                        new Dictionary<string, object>
                                        {
                                    { "id_dism", idDismViejo },
                                    { "motivo",  $"Discrepancia cancelada — sin diferencia: {motivo}" },
                                    { "usr",     usuarioId }
                                        }, false, conn, tx);
                                }
                            }

                            tx.Commit();

                            return Json(new
                            {
                                success = true,
                                cantidad_fisica = cantidadFisica,
                                cantidad_sistema = cantSist,
                                diferencia = diferencia,
                                folio_dism = folioDism,
                                id_dism = idDism,
                                mensaje = diferencia != 0
                                    ? $"Discrepancia registrada y documento DISM {(dismExistente?.Count > 0 ? "actualizado" : "generado")}: {folioDism}. Físico: {cantidadFisica}, Sistema: {cantSist}, Diferencia: {diferencia}"
                                    : $"Discrepancia registrada sin diferencia. Físico: {cantidadFisica}, Sistema: {cantSist}"
                            });
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Iniciar surtido ─────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Iniciar surtido")]
        public JsonResult IniciarSurtido(int idEncabezadoPedido) // sigue siendo el PADRE
        {
            try
            {
                int usuarioId = GetUserId(User.Identity.Name);
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                var (idNormal, idModula) = ResolverHijos(idEncabezadoPedido, empresaId);

                if (idNormal == null && idModula == null)
                    return Json(new { success = false, message = "No se encontraron documentos asociados a este pedido." });

                // Verificar que no haya un surtido ya iniciado para ninguno de los dos hijos
                string queryCheck = @"
            SELECT COUNT(*) AS c
            FROM verificacion_detalle vd
            INNER JOIN partidasdoc pd ON pd.id_partidas = vd.id_partida
            WHERE pd.encabezado_id IN (@id_normal, @id_modula)
              AND vd.fecha_inicio_surtido       IS NOT NULL
              AND vd.encabezado_verificacion_id IS NULL";

                var check = RunQuery(queryCheck, new Dictionary<string, object>
        {
            { "id_normal", (object)idNormal ?? -1 },
            { "id_modula", (object)idModula ?? -1 }
        });

                if (check?.Count > 0 && Convert.ToInt32(check[0]["c"]) > 0)
                    return Json(new { success = false, message = "Ya existe un surtido iniciado para este pedido." });

                // Traer partidas pendientes de AMBOS documentos
                string queryPartidas = @"
            SELECT pd.id_partidas
            FROM partidasdoc pd
            WHERE pd.encabezado_id IN (@id_normal, @id_modula)
              AND NOT EXISTS (
                  SELECT 1 FROM verificacion_detalle vd
                  WHERE vd.id_partida = pd.id_partidas
                    AND vd.encabezado_verificacion_id IS NOT NULL
              )";

                var partidas = RunQuery(queryPartidas, new Dictionary<string, object>
        {
            { "id_normal", (object)idNormal ?? -1 },
            { "id_modula", (object)idModula ?? -1 }
        });

                if (partidas == null || partidas.Count == 0)
                    return Json(new { success = false, message = "El pedido no tiene partidas pendientes de verificar." });

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var p in partidas)
                            {
                                var existeVd = RunQuery(@"
                            SELECT id
                            FROM verificacion_detalle
                            WHERE id_partida = @id_part
                              AND encabezado_verificacion_id IS NULL
                            LIMIT 1",
                                    new Dictionary<string, object>
                                    {
                                { "id_part", Convert.ToInt32(p["id_partidas"]) }
                                    }, false, conn, tx);

                                if (existeVd?.Count > 0)
                                {
                                    RunQuery(@"
                                UPDATE verificacion_detalle
                                   SET fecha_inicio_surtido = NOW(),
                                       usuario_inicio_id    = @usr
                                 WHERE id = @id_vd
                                   AND fecha_inicio_surtido IS NULL",
                                        new Dictionary<string, object>
                                        {
                                    { "id_vd", Convert.ToInt32(existeVd[0]["id"]) },
                                    { "usr",   usuarioId }
                                        }, false, conn, tx);
                                }
                                else
                                {
                                    RunQuery(@"
                                INSERT INTO verificacion_detalle
                                    (encabezado_verificacion_id,
                                     id_partida,
                                     cve_almacen, n_almacen,
                                     cantidad_verificada,
                                     lote, ubicacion,
                                     usuario_id,
                                     fecha_inicio_surtido,
                                     usuario_inicio_id)
                                VALUES
                                    (NULL,
                                     @id_part,
                                     '', '',
                                     0,
                                     '', '',
                                     @usr,
                                     NOW(),
                                     @usr)",
                                        new Dictionary<string, object>
                                        {
                                    { "id_part", Convert.ToInt32(p["id_partidas"]) },
                                    { "usr",     usuarioId }
                                        }, false, conn, tx);
                                }
                            }

                            // Estatus 43 en AMBOS documentos (los que existan)
                            if (idNormal.HasValue)
                                RunQuery("UPDATE encabezadomov SET estatus_id = 43 WHERE id_encabezado = @id",
                                    new Dictionary<string, object> { { "id", idNormal.Value } }, false, conn, tx);

                            if (idModula.HasValue)
                                RunQuery("UPDATE encabezadomov SET estatus_id = 43 WHERE id_encabezado = @id",
                                    new Dictionary<string, object> { { "id", idModula.Value } }, false, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    message = "Surtido iniciado. Configure las cantidades y almacenes, luego guarde la verificación.",
                    fecha_inicio = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    usuario = User.Identity.Name
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Guardar verificación (SOLO persiste en verificacion_detalle, sin RMP) ──
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Verificacion de almacen")]
        public JsonResult GuardarVerificacion(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["documentid"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un pedido." });

                if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                    return Json(new { success = false, message = "No hay partidas verificadas." });

                var partidas = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                if (partidas == null || partidas.Count == 0)
                    return Json(new { success = false, message = "Debe verificar al menos un producto." });

                // Solo las que tienen cantidad > 0
                var partidasAGuardar = partidas
                    .Where(p => p.ContainsKey("id_partida")
                             && !string.IsNullOrWhiteSpace(p["id_partida"]))
                    .ToList();

                if (!partidasAGuardar.Any())
                    return Json(new { success = false, message = "No hay partidas para guardar." });

                int idEncabezadoPadre = Convert.ToInt32(fc["documentid"].ToString());
                int usuarioId = GetUserId(User.Identity.Name);

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var p in partidasAGuardar)
                            {
                                // Ya funciona — decimal.Parse acepta "0", no hay guard de cv > 0 dentro del foreach
                                decimal? cantVer = GetDecimal(p["cantidad_verificada"]);
                                int idPartida = int.Parse(p.ContainsKey("id_partida") ? p["id_partida"] : "0");
                                string cveAlm =  GetString(p["almacen_seleccionado"]) ;
                                string nAlm = p.ContainsKey("nombre_almacen") ? p["nombre_almacen"] : "";
                                string lote = p.ContainsKey("lote") ? p["lote"] : "";
                                string ubic = p.ContainsKey("ubicacion") ? p["ubicacion"] : "";

                                bool existeVd = RunQuery(@"
                            SELECT 1
                            FROM verificacion_detalle
                            WHERE id_partida                  = @id_part
                              AND encabezado_verificacion_id IS NULL",
                                    new Dictionary<string, object> { { "id_part", idPartida } },
                                    false, conn, tx)?.Count > 0;

                                if (existeVd)
                                {

                                    RunQuery(@"
                                        UPDATE verificacion_detalle
                                           SET cantidad_verificada = @cant_ver,
                                               cve_almacen          = @cve_alm,
                                               n_almacen            = @n_alm,
                                               lote                 = @lote,
                                               ubicacion            = @ubic,
                                               usuario_id           = @usr,
                                               fecha_fin_surtido    = CASE
                                                                          WHEN @cant_ver = @cant_pedida
                                                                               AND fecha_fin_surtido IS NULL
                                                                          THEN @fecha_fin_surtido
                                                                          ELSE fecha_fin_surtido
                                                                      END
                                         WHERE id_partida = @id_part
                                           AND encabezado_verificacion_id IS NULL",
                                            new Dictionary<string, object>
                                            {
                                                { "id_part", idPartida },
                                                { "cant_ver", cantVer },
                                                { "cant_pedida", GetDecimal((p["cantidad_pedida"])) },
                                                { "cve_alm", cveAlm },
                                                { "n_alm", nAlm },
                                                { "lote", lote },
                                                { "ubic", ubic },
                                                { "usr", usuarioId },
                                                { "fecha_fin_surtido", DateTime.Now }
                                            },
                                            false, conn, tx);
                                }
                                else
                                {
                                    // Insertar sin fecha_fin_surtido
                                    RunQuery(@"
                                INSERT INTO verificacion_detalle
                                    (encabezado_verificacion_id, id_partida,
                                     cve_almacen, n_almacen,
                                     cantidad_verificada, lote, ubicacion,
                                     usuario_id,
                                     fecha_inicio_surtido, usuario_inicio_id)
                                VALUES
                                    (NULL, @id_part,
                                     @cve_alm, @n_alm,
                                     @cant_ver, @lote, @ubic,
                                     @usr,
                                     NOW(), @usr)",
                                        new Dictionary<string, object>
                                        {
                                    { "id_part",  idPartida },
                                    { "cve_alm",  cveAlm },
                                    { "n_alm",    nAlm },
                                    { "cant_ver", cantVer },
                                    { "lote",     lote },
                                    { "ubic",     ubic },
                                    { "usr",      usuarioId }
                                        }, false, conn, tx);
                                }
                            }

                            // ── Marcar AMBOS documentos hijos como "en proceso" ──
                            var (idNormalG, idModulaG) = ResolverHijos(idEncabezadoPadre, Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")), conn, tx);

                            if (idNormalG.HasValue)
                                RunQuery(@"UPDATE encabezadomov SET estatus_id = 43 WHERE id_encabezado = @id AND estatus_id != 43",
                                    new Dictionary<string, object> { { "id", idNormalG.Value } }, false, conn, tx);

                            if (idModulaG.HasValue)
                                RunQuery(@"UPDATE encabezadomov SET estatus_id = 43 WHERE id_encabezado = @id AND estatus_id != 43",
                                    new Dictionary<string, object> { { "id", idModulaG.Value } }, false, conn, tx);

                            tx.Commit();

                            // ¿Todas las partidas del pedido ya tienen cantidad guardada?
                            bool todasGuardadas = TodasPartidasGuardadas(idEncabezadoPadre);

                            return Json(new
                            {
                                success = true,
                                message = "Verificación guardada correctamente.",
                                todas_guardadas = todasGuardadas
                            });
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Helper: ¿todas las partidas del pedido tienen cantidad guardada en detalle? ─

        private bool TodasPartidasGuardadas(int idEncabezadoPadre)
        {
            int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
            var (idNormal, idModula) = ResolverHijos(idEncabezadoPadre, empresaId);

            var r = RunQuery(@"
        SELECT COUNT(*) AS faltantes
        FROM partidasdoc pd
        WHERE pd.encabezado_id IN (@id_normal, @id_modula)
          AND NOT EXISTS (
              SELECT 1
              FROM verificacion_detalle vd
              WHERE vd.id_partida                  = pd.id_partidas
                AND vd.encabezado_verificacion_id IS NULL
          )",
                new Dictionary<string, object>
                {
            { "id_normal", (object)idNormal ?? -1 },
            { "id_modula", (object)idModula ?? -1 }
                });

            return r?.Count > 0 && Convert.ToInt32(r[0]["faltantes"]) == 0;
        }

        // ─── Finalizar surtido: ahora CREA el documento RMP ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Finalizar surtido y generar VIVER")]
        public JsonResult FinalizarSurtido(int idEncabezadoPedido) // PADRE
        {
            try
            {
                int usuarioId = GetUserId(User.Identity.Name);
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                if (!TodasPartidasGuardadas(idEncabezadoPedido))
                    return Json(new
                    {
                        success = false,
                        message = "No se puede finalizar: hay partidas sin cantidad verificada guardada."
                    });

                var (idNormal, idModula) = ResolverHijos(idEncabezadoPedido, empresaId);

                if (idNormal == null && idModula == null)
                    return Json(new { success = false, message = "No se encontraron documentos asociados a este pedido." });

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            string folioRmp = null;
                            int? idVerificacionRmp = null;
                            string folioModulaConfirmacion = null;
                            bool modulaEnviado = false;

                            // ═══════════════════════════════════════════
                            // PARTE 1: STOCK → genera RMP (igual que antes)
                            // ═══════════════════════════════════════════
                            if (idNormal.HasValue)
                            {
                                // El RMP hereda la configuración comercial del pedido (condiciones
                                // de pago, CFDI, incoterm, almacén…) porque es el documento que
                                // después se carga en la remisión industrial: lo que no se copie
                                // aquí llega vacío a ese formulario.
                                var parentResult = RunQuery(@"
                            SELECT em.usr0, em.fch0, em.usr1, em.fch1,
                                   em.usr2, em.fch2, em.cli_prov, em.ccy,
                                   em.par, em.vdr_cpr, em.coment1, em.coment_aut,
                                   em.mdp, em.f_pago, em.refe, em.flete,
                                   em.pl_dias, em.fch_pg_entrega, em.cfdi,
                                   em.orden_compra, em.tipo_proceso,
                                   em.alm, em.incoterm, em.sub, em.centro_costos
                            FROM encabezadomov em
                            WHERE em.id_encabezado = @id",
                                    new Dictionary<string, object> { { "id", idNormal.Value } }, false, conn, tx);

                                if (parentResult == null || parentResult.Count == 0)
                                    throw new Exception("Documento de stock (normal) no encontrado.");

                                var parent = parentResult[0];

                                var detallesStock = RunQuery(@"
                            SELECT
                                vd.id, vd.id_partida, vd.cantidad_verificada,
                                vd.cve_almacen, vd.n_almacen, vd.lote, vd.ubicacion,
                                pd.cve_prod, pd.descr_prod, pd.pv_prod, pd.dto1, pd.ud, pd.tp_doc_ant,
                                cp.id_catproductos AS id_producto
                            FROM verificacion_detalle vd
                            INNER JOIN partidasdoc pd ON pd.id_partidas = vd.id_partida
                            LEFT JOIN catproductos cp
                                ON cp.cve_prod = pd.cve_prod AND cp.empresa_id = @empresa_id
                            WHERE pd.encabezado_id              = @id_normal
                              AND vd.encabezado_verificacion_id IS NULL",
                                    new Dictionary<string, object>
                                    {
                                { "id_normal",  idNormal.Value },
                                { "empresa_id", empresaId }
                                    }, false, conn, tx);

                                if (detallesStock != null && detallesStock.Count > 0)
                                {
                                    decimal totalImp = detallesStock.Sum(d =>
                                        Convert.ToDecimal(d["cantidad_verificada"]) * Convert.ToDecimal(d["pv_prod"]));
                                    decimal totalDto = detallesStock.Sum(d =>
                                        Convert.ToDecimal(d["cantidad_verificada"]) *
                                        Convert.ToDecimal(d["pv_prod"]) *
                                        (Convert.ToDecimal(d["dto1"]) / 100m));

                                    var encabezado = new DocumentoEncabezado
                                    {
                                        EmpresaId = empresaId,
                                        IdArea = 6,
                                        IdTpDoc = 84,
                                        UsrDep = GetAreaName(User.Identity.Name),
                                        Anio = DateTime.Now.Year,
                                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                                        Alm = parent["alm"]?.ToString() ?? "",
                                        Fch = DateTime.Now,
                                        TpMov = "RMP",
                                        ComentAut = parent["coment_aut"]?.ToString(),
                                        UsrDoc = User.Identity.Name,
                                        FchCap = DateTime.Now,
                                        Usr0 = ValorInt(parent["usr0"]),
                                        Fch0 = Convert.ToDateTime(parent["fch0"]),
                                        Usr1 = ValorInt(parent["usr1"]),
                                        Fch1 = Convert.ToDateTime(parent["fch1"]),
                                        Usr2 = usuarioId,
                                        Fch2 = DateTime.Now,
                                        Imp = totalImp,
                                        Dto = totalDto,
                                        CliProv = parent["cli_prov"]?.ToString(),
                                        Ccy = parent["ccy"]?.ToString(),
                                        Estatus = 1,
                                        Ref = ValorInt(parent["refe"]),
                                        Flete = ValorDecimal(parent["flete"]),
                                        VdrCpr = parent["vdr_cpr"]?.ToString(),
                                        Coment1 = parent["coment1"]?.ToString(),
                                        EncabezadoPadre = idNormal.Value,
                                        // ── Condiciones de pago heredadas del pedido ──
                                        PlDias = ValorInt(parent["pl_dias"]),
                                        FchPgEntrega = ValorFecha(parent["fch_pg_entrega"]),
                                        Par = ValorDecimal(parent["par"], 1m),
                                        FPago = ValorIntNulo(parent["f_pago"]),
                                        Mdp = parent["mdp"]?.ToString(),
                                        // tipo_proceso es donde el pedido guarda si la venta es
                                        // "pedido_credito" o "pedido_contado": si se pisa con
                                        // "verificacion" esa condición se pierde para la remisión.
                                        TipoPoceso = parent["tipo_proceso"]?.ToString(),
                                        CFDI = parent["cfdi"]?.ToString(),
                                        Incoterm = parent["incoterm"]?.ToString(),
                                        Sub = ValorDecimal(parent["sub"]),
                                        CentroCostos = ValorIntNulo(parent["centro_costos"]),
                                        NatDocPadreChar = parent["orden_compra"]?.ToString()
                                    };

                                    var partidasDoc = detallesStock.Select(d => new PartidaDocumento
                                    {
                                        CveProd = d["cve_prod"]?.ToString(),
                                        DescrProd = d["descr_prod"]?.ToString(),
                                        CantUd = Convert.ToDecimal(d["cantidad_verificada"]),
                                        PvProd = Convert.ToDecimal(d["pv_prod"]),
                                        Dto1 = Convert.ToDecimal(d["dto1"]),
                                        ImpPart = Convert.ToDecimal(d["cantidad_verificada"]) * Convert.ToDecimal(d["pv_prod"]),
                                        Ud = d["ud"]?.ToString() ?? "PZA",
                                        IdProducto = d["id_producto"] != DBNull.Value ? Convert.ToInt32(d["id_producto"]) : 0,
                                        TpDocAnt = d["tp_doc_ant"]?.ToString() ?? ""
                                    }).ToList();

                                    var folio = GenerarDocumentoConPartidas(encabezado, partidasDoc, conn, tx);
                                    idVerificacionRmp = Convert.ToInt32(folio["IdEncabezado"]);
                                    folioRmp = folio["folio_generado"]?.ToString();

                                    foreach (var d in detallesStock)
                                    {
                                        RunQuery(@"
                                    UPDATE verificacion_detalle
                                       SET encabezado_verificacion_id = @enc,
                                           fecha_fin_surtido          = NOW(),
                                           usuario_fin_id             = @usr
                                     WHERE id = @id_vd",
                                            new Dictionary<string, object>
                                            {
                                        { "enc",   idVerificacionRmp },
                                        { "usr",   usuarioId },
                                        { "id_vd", Convert.ToInt32(d["id"]) }
                                            }, false, conn, tx);
                                    }

                                    RunQuery("UPDATE encabezadomov SET estatus_id = 44 WHERE id_encabezado = @id",
                                        new Dictionary<string, object> { { "id", idNormal.Value } }, false, conn, tx);
                                }
                                else
                                {
                                    // No hay partidas de stock con cantidad — igual marcar como verificado
                                    RunQuery("UPDATE encabezadomov SET estatus_id = 44 WHERE id_encabezado = @id",
                                        new Dictionary<string, object> { { "id", idNormal.Value } }, false, conn, tx);
                                }
                            }

                            // ═══════════════════════════════════════════
                            // PARTE 2: MODULA → webservice de salida real
                            // ═══════════════════════════════════════════
        //                    if (idModula.HasValue)
        //                    {
        //                        var detallesModula = RunQuery(@"
        //SELECT
        //    vd.id, vd.id_partida, vd.cantidad_verificada,
        //    vd.lote, vd.ubicacion,
        //    pd.cve_prod, pd.descr_prod, pd.ud,
        //    cp.id_catproductos AS id_producto
        //FROM verificacion_detalle vd
        //INNER JOIN partidasdoc pd ON pd.id_partidas = vd.id_partida
        //LEFT JOIN catproductos cp
        //    ON cp.cve_prod = pd.cve_prod AND cp.empresa_id = @empresa_id
        //WHERE pd.encabezado_id              = @id_modula
        //  AND vd.encabezado_verificacion_id IS NULL",
        //                            new Dictionary<string, object>
        //                            {
        //    { "id_modula",  idModula.Value },
        //    { "empresa_id", empresaId }
        //                            }, false, conn, tx);

        //                        if (detallesModula != null && detallesModula.Count > 0)
        //                        {
        //                            var (exitoModula, mensajeModula) = ProcesarSalidaModula(
        //                                idModula.Value, detallesModula, usuarioId, conn, tx);

        //                            if (!exitoModula)
        //                                throw new Exception($"Error al procesar salida en Modula: {mensajeModula}");

        //                            modulaEnviado = true;
        //                            folioModulaConfirmacion = $"MOD-{idModula.Value}";

        //                            foreach (var d in detallesModula)
        //                            {
        //                                RunQuery(@"
        //        UPDATE verificacion_detalle
        //           SET encabezado_verificacion_id = @enc_ficticio,
        //               fecha_fin_surtido          = NOW(),
        //               usuario_fin_id             = @usr
        //         WHERE id = @id_vd",
        //                                    new Dictionary<string, object>
        //                                    {
        //            { "enc_ficticio", idModula.Value }, // marca como "ya facturado" contra su propio doc
        //            { "usr",          usuarioId },
        //            { "id_vd",        Convert.ToInt32(d["id"]) }
        //                                    }, false, conn, tx);
        //                            }

        //                            // Estatus 44 = verificado/completo (el WS ya confirmó salida física)
        //                            RunQuery("UPDATE encabezadomov SET estatus_id = 44 WHERE id_encabezado = @id",
        //                                new Dictionary<string, object> { { "id", idModula.Value } }, false, conn, tx);
        //                        }
        //                        else
        //                        {
        //                            RunQuery("UPDATE encabezadomov SET estatus_id = 44 WHERE id_encabezado = @id",
        //                                new Dictionary<string, object> { { "id", idModula.Value } }, false, conn, tx);
        //                        }
        //                    }

                            // Mientras el webservice de arriba siga desactivado, el documento de
                            // Modula igual tiene que CERRARSE aquí: la remisión considera un
                            // documento surtido solo cuando todas sus partidas tienen
                            // fecha_fin_surtido (DocCompletoSql en VNRemision). Sin esto, un
                            // pedido con partidas de Modula nunca aparecía como listo para
                            // remisionar, y uno que fuera 100% Modula no cerraba nada en absoluto.
                            if (idModula.HasValue)
                            {
                                RunQuery(@"
                            UPDATE verificacion_detalle vd
                               SET encabezado_verificacion_id = @enc_modula,
                                   fecha_fin_surtido          = NOW(),
                                   usuario_fin_id             = @usr
                              FROM partidasdoc pd
                             WHERE pd.id_partidas = vd.id_partida
                               AND pd.encabezado_id = @id_modula
                               AND vd.encabezado_verificacion_id IS NULL",
                                    new Dictionary<string, object>
                                    {
                                // No hay RMP para Modula: las partidas se cierran contra su
                                // propio documento, que es lo que la remisión sabe leer.
                                { "enc_modula", idModula.Value },
                                { "usr",        usuarioId },
                                { "id_modula",  idModula.Value }
                                    }, false, conn, tx);

                                RunQuery("UPDATE encabezadomov SET estatus_id = 44 WHERE id_encabezado = @id",
                                    new Dictionary<string, object> { { "id", idModula.Value } }, false, conn, tx);
                            }

                            tx.Commit();

                            string folioCombinado = string.Join(" | ", new[] { folioRmp, folioModulaConfirmacion }
                                .Where(f => !string.IsNullOrEmpty(f)));

                            return Json(new
                            {
                                success = true,
                                folio_generado = folioCombinado,
                                id_verificacion = idVerificacionRmp,
                                id_encabezado_normal = idNormal,
                                id_encabezado_modula = idModula,
                                modula_enviado = modulaEnviado,
                                fecha_fin = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                                // El RMP solo existe si hubo partidas de stock; un pedido 100%
                                // Modula se cierra sin generar documento.
                                message = folioRmp != null
                                    ? (idModula.HasValue
                                        ? "Surtido finalizado. Documento RMP generado y surtido de Modula cerrado."
                                        : "Surtido finalizado y documento RMP generado correctamente.")
                                    : "Surtido de Modula finalizado correctamente."
                            });
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Obtener estado de surtido (para verificaciones previas cargadas) ────────
        [HttpGet]
        public IActionResult ObtenerEstadoSurtido(int idVerificacion)
        {
            try
            {
                // idVerificacion aquí es el RMP ya generado
                var r = RunQuery(@"
            SELECT
                MIN(vd.fecha_inicio_surtido) AS fecha_inicio,
                MIN(vd.fecha_fin_surtido)    AS fecha_fin
            FROM verificacion_detalle vd
            WHERE vd.encabezado_verificacion_id = @id",
                    new Dictionary<string, object> { { "id", idVerificacion } });

                if (r == null || r.Count == 0 || r[0]["fecha_inicio"] == DBNull.Value)
                    return Json(new { success = true, iniciado = false, finalizado = false });

                var row = r[0];
                bool finalizado = row["fecha_fin"] != DBNull.Value && row["fecha_fin"] != null;

                return Json(new
                {
                    success = true,
                    iniciado = true,
                    finalizado,
                    fecha_inicio = Convert.ToDateTime(row["fecha_inicio"]).ToString("dd/MM/yyyy HH:mm"),
                    fecha_fin = finalizado
                                   ? Convert.ToDateTime(row["fecha_fin"]).ToString("dd/MM/yyyy HH:mm")
                                   : null
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Helper: ¿todas las partidas del pedido tienen ya un VIVER? ──────────
        private bool EsPedidoCompleto(int idEncabezadoPadre, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var r = RunQuery(@"
                SELECT COUNT(*) AS faltantes
                FROM partidasdoc pd
                WHERE pd.encabezado_id = @pedido_id
                  AND NOT EXISTS (
                      SELECT 1 FROM verificacion_detalle vd
                      WHERE vd.id_partida = pd.id_partidas
                        AND vd.encabezado_verificacion_id IS NOT NULL
                  )",
                new Dictionary<string, object> { { "pedido_id", idEncabezadoPadre } },
                false, conn, tx);

            return r?.Count > 0 && Convert.ToInt32(r[0]["faltantes"]) == 0;
        }

        // ─── Obtener estado de surtido por pedido ────────────────────────────────
        [HttpGet]
        public IActionResult ObtenerEstadoSurtidoPorPedido(int idPedido) // PADRE
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                var (idNormal, idModula) = ResolverHijos(idPedido, empresaId);

                string query = @"
            SELECT
                MIN(vd.fecha_inicio_surtido) AS fecha_inicio,
                MIN(vd.fecha_fin_surtido)    AS fecha_fin
            FROM verificacion_detalle vd
            INNER JOIN partidasdoc pd ON pd.id_partidas = vd.id_partida
            WHERE pd.encabezado_id IN (@id_normal, @id_modula)";

                var r = RunQuery(query, new Dictionary<string, object>
        {
            { "id_normal", (object)idNormal ?? -1 },
            { "id_modula", (object)idModula ?? -1 }
        });

                if (r == null || r.Count == 0
                    || r[0]["fecha_inicio"] == DBNull.Value
                    || r[0]["fecha_inicio"] == null)
                    return Json(new { success = true, iniciado = false });

                var row = r[0];
                return Json(new
                {
                    success = true,
                    iniciado = true,
                    fecha_inicio = Convert.ToDateTime(row["fecha_inicio"]).ToString("dd/MM/yyyy HH:mm")
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Imprimir etiquetas ──────────────────────────────────────────────────
        [HttpGet]
        public IActionResult ObtenerEtiquetas(int idVerificacion)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id",         idVerificacion },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string query = @"
                    SELECT
                        em.folio,
                        em.fch,
                        em.cli_prov,
                        cc.n_cli,
                        pd.cve_prod,
                        pd.descr_prod,
                        pd.cant_ud      AS cantidad,
                        pd.ud           AS unidad,
                        pd.pv_prod      AS precio,
                        vd.cve_almacen,
                        vd.n_almacen,
                        vd.lote,
                        vd.ubicacion
                    FROM encabezadomov em
                    INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
                    INNER JOIN partidasdoc pd ON pd.encabezado_id = em.id_encabezado
                    LEFT JOIN verificacion_detalle vd
                        ON vd.encabezado_verificacion_id = em.id_encabezado
                       AND vd.id_partida = pd.id_partidas
                    WHERE em.id_encabezado = @id
                    ORDER BY pd.nro_part";

                var items = RunQuery(query, parameters);
                return Json(new { success = true, etiquetas = items });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Buscar pedidos disponibles para verificación ────────────────────────
        [HttpGet]
        public IActionResult BuscarPedidosParaVerificar(string nombre = "", int page = 1, int pageSize = 25)
        {
            var parameters = new Dictionary<string, object>
    {
        { "nombre",     $"%{nombre}%" },
        { "offset",     (page - 1) * pageSize },
        { "pageSize",   pageSize },
        { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    };

            // ── Base: documentos_relacionados, resolviendo folio/fecha/cliente
            //    desde cualquiera de los dos hijos que exista (normal o modula) ──
            string query = @"
        SELECT
            dr.id_encabezado_padre,
            dr.id_encabezado_normal,
            dr.id_encabezado_modula,
            dr.folio_normal,
            dr.folio_modula,
            em_ref.fch,
            em_ref.cli_prov,
            em_ref.imp,
            em_ref.usr1,
            em_ref.tipo_proceso,
            cc.n_cli,
            (dr.id_encabezado_normal IS NOT NULL
             AND dr.id_encabezado_modula IS NOT NULL) AS es_mixto
        FROM documentos_relacionados dr
        INNER JOIN encabezadomov em_ref
            ON em_ref.id_encabezado = COALESCE(dr.id_encabezado_normal, dr.id_encabezado_modula)
        INNER JOIN catclientes cc
            ON cc.id_cliente = em_ref.refe AND cc.empresa_id = @empresa_id
        WHERE dr.empresa_id = @empresa_id
          AND em_ref.suc    = @suc
          AND (
              -- Al menos uno de los dos documentos debe estar en estatus vigente
              EXISTS (
                  SELECT 1 FROM encabezadomov e1
                  WHERE e1.id_encabezado = dr.id_encabezado_normal
                    AND e1.estatus_id IN (1, 43)
              )
              OR EXISTS (
                  SELECT 1 FROM encabezadomov e2
                  WHERE e2.id_encabezado = dr.id_encabezado_modula
                    AND e2.estatus_id IN (1, 43)
              )
          )
          AND (
              LOWER(COALESCE(dr.folio_normal,''))  LIKE LOWER(@nombre)
           OR LOWER(COALESCE(dr.folio_modula,'')) LIKE LOWER(@nombre)
           OR LOWER(em_ref.cli_prov)              LIKE LOWER(@nombre)
           OR LOWER(cc.n_cli)                     LIKE LOWER(@nombre)
          )
        ORDER BY em_ref.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var itemsRaw = RunQuery(query, parameters);

            // ── Armar folio combinado para mostrar en la lista ──
            var items = itemsRaw.Select(r =>
            {
                string folioN = r["folio_normal"]?.ToString();
                string folioM = r["folio_modula"]?.ToString();
                bool mixto = Convert.ToBoolean(r["es_mixto"]);

                string folioDisplay = mixto
                    ? $"{folioN} + {folioM}"
                    : (folioN ?? folioM);

                return (object)new
                {
                    id_encabezado = r["id_encabezado_padre"], // ← ojo: ahora referimos al PADRE
                    id_encabezado_normal = r["id_encabezado_normal"],
                    id_encabezado_modula = r["id_encabezado_modula"],
                    folio = folioDisplay,
                    es_mixto = mixto,
                    fch = r["fch"],
                    cli_prov = r["cli_prov"],
                    n_cli = r["n_cli"],
                    imp = r["imp"]
                };
            }).ToList();

            // ── Total (mismo WHERE, sin paginación) ──
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM documentos_relacionados dr
        INNER JOIN encabezadomov em_ref
            ON em_ref.id_encabezado = COALESCE(dr.id_encabezado_normal, dr.id_encabezado_modula)
        INNER JOIN catclientes cc
            ON cc.id_cliente = em_ref.refe AND cc.empresa_id = @empresa_id
        WHERE dr.empresa_id = @empresa_id
          AND em_ref.suc    = @suc
          AND (
              EXISTS (SELECT 1 FROM encabezadomov e1
                      WHERE e1.id_encabezado = dr.id_encabezado_normal AND e1.estatus_id IN (1, 43))
           OR EXISTS (SELECT 1 FROM encabezadomov e2
                      WHERE e2.id_encabezado = dr.id_encabezado_modula AND e2.estatus_id IN (1, 43))
          )
          AND (
              LOWER(COALESCE(dr.folio_normal,''))  LIKE LOWER(@nombre)
           OR LOWER(COALESCE(dr.folio_modula,'')) LIKE LOWER(@nombre)
           OR LOWER(em_ref.cli_prov)              LIKE LOWER(@nombre)
           OR LOWER(cc.n_cli)                     LIKE LOWER(@nombre)
          )";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object>
    {
        { "nombre",     $"%{nombre}%" },
        { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    });

            int total = totalResult?.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;
            return Json(new { items, total });
        }

        // ─── Buscar documentos de verificación existentes ───────────────────────
        [HttpGet]
        public IActionResult BuscarDocumentosVerificacion(string nombre = "", int page = 1, int pageSize = 25)
        {
            var parameters = new Dictionary<string, object>
            {
                { "nombre",     $"%{nombre}%" },
                { "offset",     (page - 1) * pageSize },
                { "pageSize",   pageSize },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            string query = @"
                SELECT
                    em.id_encabezado,
                    em.folio,
                    em.fch,
                    em.cli_prov,
                    em.imp,
                    em.usr2,
                    cc.n_cli
                FROM encabezadomov em
                INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
                WHERE em.nat          = 'RMP'
                  AND em.suc          = @suc
                  AND em.estatus_id   = 1
                  AND (
                      LOWER(em.folio)    LIKE LOWER(@nombre)
                   OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
                   OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
                  )
                ORDER BY em.fch DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var items = RunQuery(query, parameters);

            string queryTotal = @"
                SELECT COUNT(*) AS total
                FROM encabezadomov em
                INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
                WHERE em.nat        = 'RMP'
                  AND em.suc        = @suc
                  AND em.estatus_id = 1
                  AND (LOWER(em.folio)    LIKE LOWER(@nombre)
                   OR  LOWER(em.cli_prov) LIKE LOWER(@nombre)
                   OR  LOWER(cc.n_cli)    LIKE LOWER(@nombre))";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object>
            {
                { "nombre",     $"%{nombre}%" },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            });

            int total = totalResult?.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;
            return Json(new { items, total });
        }

        // ─── Obtener datos de verificación para cargar en remisión ───────────────
        [HttpGet]
        public IActionResult ObtenerVerificacionParaRemision(int idVerificacion)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id",         idVerificacion },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                string queryEnc = @"
                    SELECT
                        em.id_encabezado,
                        em.folio,
                        em.cli_prov,
                        em.ccy, em.par, em.vdr_cpr,
                        em.coment1, em.coment_aut,
                        em.mdp, em.fch, em.refe,
                        em.encabezados_padre AS pedido_id,
                        cc.n_cli, cc.rfc,
                        f.cve_sat AS f_pago_cve
                    FROM encabezadomov em
                    INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
                    LEFT JOIN cat_f_pago f ON f.id_f_pago = em.f_pago
                    WHERE em.id_encabezado = @id";

                var encResult = RunQuery(queryEnc, parameters);
                if (encResult == null || encResult.Count == 0)
                    return Json(new { success = false, message = "Verificación no encontrada." });

                var enc = encResult[0];

                string queryPartidas = @"
                    SELECT
                        pd.id_partidas,
                        pd.cve_prod        AS producto_id,
                        pd.descr_prod      AS descripcion,
                        pd.cant_ud         AS cantidad_pedida,
                        COALESCE(vd.cantidad_verificada, 0) AS cantidad_verificada,
                        pd.pv_prod         AS precio,
                        pd.dto1            AS descuento,
                        pd.ud              AS udm,
                        pd.tp_doc_ant      AS comentario,
                        vd.cve_almacen,
                        vd.n_almacen,
                        vd.lote,
                        vd.ubicacion,
                        df.cantidad_fisica,
                        df.cantidad_sistema   AS cantidad_sistema_df,
                        df.motivo             AS motivo_discrepancia,
                        COALESCE((
                            SELECT SUM(tp2.cantidad)
                            FROM tarima_productos tp2
                            INNER JOIN cattarimas ct2  ON ct2.id_tarima   = tp2.tarima_id
                            INNER JOIN catniveles cn2  ON cn2.id_nivel    = ct2.nivel_id
                            INNER JOIN catcolumnas cc2 ON cc2.id_columna  = cn2.columna_id
                            INNER JOIN catracks cr2    ON cr2.id_rack     = cc2.rack_id
                            INNER JOIN catalmacenes ca2 ON ca2.id_almacen = cr2.almacen_id
                            INNER JOIN catsucursales cs2 ON cs2.id_sucursal = ca2.sucursal_id
                            WHERE tp2.producto_id = cp.id_catproductos
                              AND cs2.id_sucursal  = @sucursal
                              AND ca2.tipo = 'Stock'
                              AND tp2.cantidad > 0
                        ), 0) AS existencia,
                        pd.pv_prod AS costoUnitario
                    FROM partidasdoc pd
                    LEFT JOIN catproductos cp
                        ON cp.cve_prod = pd.cve_prod AND cp.empresa_id = @empresa_id
                    LEFT JOIN verificacion_detalle vd
                        ON vd.encabezado_verificacion_id = @id
                       AND vd.id_partida = pd.id_partidas
                    LEFT JOIN discrepancia_fisica df
                        ON df.id_verificacion_detalle = vd.id
                    WHERE pd.encabezado_id = @id
                    ORDER BY pd.nro_part";

                parameters["sucursal"] = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                var partidas = RunQuery(queryPartidas, parameters);

                return Json(new
                {
                    success = true,
                    id_encabezado = enc["id_encabezado"],
                    folio = enc["folio"],
                    cli_prov = enc["cli_prov"],
                    n_cli = enc["n_cli"],
                    rfc = enc["rfc"],
                    ccy = enc["ccy"],
                    par = enc["par"],
                    vdr_cpr = enc["vdr_cpr"],
                    coment1 = enc["coment1"],
                    coment_aut = enc["coment_aut"],
                    mdp = enc["mdp"],
                    f_pago = enc["f_pago_cve"],
                    pedido_id = enc["pedido_id"],
                    productos = partidas
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─── Helper: resuelve id_encabezado_normal / id_encabezado_modula desde el padre ──
        private (int? idNormal, int? idModula) ResolverHijos(int idEncabezadoPadre, int empresaId)
        {
            var relResult = RunQuery(@"
        SELECT id_encabezado_normal, id_encabezado_modula
        FROM documentos_relacionados
        WHERE id_encabezado_padre = @id_padre
          AND empresa_id          = @empresa_id
        ORDER BY id_relacion DESC
        LIMIT 1",
                new Dictionary<string, object>
                {
            { "id_padre", idEncabezadoPadre },
            { "empresa_id", empresaId }
                });

            if (relResult?.Count > 0)
                return LeerHijos(relResult[0]);

            // Fallback: pedido viejo sin split — se asume 100% stock
            return (idEncabezadoPadre, null);
        }

        // Overload con conexión/transacción explícita (para usar dentro de una tx abierta)
        private (int? idNormal, int? idModula) ResolverHijos(
            int idEncabezadoPadre, int empresaId, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var relResult = RunQuery(@"
        SELECT id_encabezado_normal, id_encabezado_modula
        FROM documentos_relacionados
        WHERE id_encabezado_padre = @id_padre
          AND empresa_id          = @empresa_id
        ORDER BY id_relacion DESC
        LIMIT 1",
                new Dictionary<string, object>
                {
            { "id_padre", idEncabezadoPadre },
            { "empresa_id", empresaId }
                }, false, conn, tx);

            if (relResult?.Count > 0)
                return LeerHijos(relResult[0]);

            return (idEncabezadoPadre, null);
        }

        // Un pedido puede tener solo stock, solo Modula o los dos. El tipo ausente TIENE que
        // quedar en null: RunQuery devuelve las columnas NULL como C# `null` (no DBNull.Value),
        // así que el check `!= DBNull.Value` era siempre cierto y Convert.ToInt32(null) daba 0.
        // Con idNormal = 0 el surtido de un pedido solo-Modula fallaba con "Documento de stock
        // (normal) no encontrado", porque buscaba el encabezado con id 0.
        private (int? idNormal, int? idModula) LeerHijos(Dictionary<string, object> rel)
        {
            int? idN = GetInt(rel["id_encabezado_normal"]);
            int? idM = GetInt(rel["id_encabezado_modula"]);
            return (idN > 0 ? idN : null, idM > 0 ? idM : null);
        }


        // ─── Envía salida a Modula y descuenta tarima_productos (tipo = 'Modula') ──
        private (bool exito, string mensaje) ProcesarSalidaModula(
            int idModula,
            List<Dictionary<string, object>> detallesModula, // filas de verificacion_detalle+partidasdoc
            int usuarioId,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var utils = new Utilities(true);

            // ── 1. Armar payload y llamar al webservice ──
            var items = detallesModula.Select(d => new ModulaOutboundHelper.ItemSalida
            {
                Codigo = d["cve_prod"]?.ToString(),
                Cantidad = Convert.ToDecimal(d["cantidad_verificada"]),
                Lote = d["lote"]?.ToString() ?? ""
            }).ToList();

            var resultadoWs = ModulaOutboundHelper.EnviarSalida(
                utils._configuration,
                ordenNumero: idModula.ToString(),
                ordenDescripcion: $"Surtido pedido {idModula}",
                items: items);

            // ── 2. Auditoría (misma tabla que usa ModulaController) ──
            RunQuery(@"
        INSERT INTO modula_auditoria
            (fecha, usuario, tipo_accion, origen, orden_numero,
             payload_json, ws_status, ws_respuesta, total_registros)
        VALUES
            (NOW(), @usuario, 'OUTBOUND', 'VERIFICACION_ALMACEN', @orden,
             @payload, @status, @respuesta, @total)",
                new Dictionary<string, object>
                {
            { "usuario",   User.Identity.Name },
            { "orden",     idModula.ToString() },
            { "payload",   JsonConvert.SerializeObject(items) },
            { "status",    resultadoWs.Exito ? "OK" : "ERROR" },
            { "respuesta", resultadoWs.Mensaje },
            { "total",     detallesModula.Count }
                }, false, conn, tx);

            if (!resultadoWs.Exito)
                return (false, resultadoWs.Mensaje);

            // ── 3. Descontar tarima_productos (tipo = 'Modula'), FIFO por tarima ──
            foreach (var d in detallesModula)
            {
                int idProducto = d["id_producto"] != DBNull.Value ? Convert.ToInt32(d["id_producto"]) : 0;
                decimal cantidadRestante = Convert.ToDecimal(d["cantidad_verificada"]);
                if (idProducto == 0 || cantidadRestante <= 0) continue;

                // Traer tarimas con existencia de este producto en almacenes tipo Modula, FIFO
                var tarimasDisponibles = RunQuery(@"
            SELECT tp.id, tp.cantidad
            FROM tarima_productos tp
            INNER JOIN cattarimas ct   ON ct.id_tarima   = tp.tarima_id
            INNER JOIN catniveles cn   ON cn.id_nivel    = ct.nivel_id
            INNER JOIN catcolumnas col ON col.id_columna = cn.columna_id
            INNER JOIN catracks cr     ON cr.id_rack     = col.rack_id
            INNER JOIN catalmacenes ca ON ca.id_almacen  = cr.almacen_id
            INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
            WHERE tp.producto_id = @producto_id
              AND cs.id_sucursal = @sucursal
              AND ca.tipo        = 'Modula'
              AND tp.cantidad    > 0
            ORDER BY tp.id ASC
            FOR UPDATE",
                    new Dictionary<string, object>
                    {
                { "producto_id", idProducto },
                { "sucursal",    Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
                    }, false, conn, tx);

                foreach (var t in tarimasDisponibles)
                {
                    if (cantidadRestante <= 0) break;

                    decimal disponibleEnTarima = Convert.ToDecimal(t["cantidad"]);
                    decimal aDescontar = Math.Min(disponibleEnTarima, cantidadRestante);

                    RunQuery(@"
                UPDATE tarima_productos
                   SET cantidad = cantidad - @desc
                 WHERE id = @id_tp",
                        new Dictionary<string, object>
                        {
                    { "desc",  aDescontar },
                    { "id_tp", Convert.ToInt32(t["id"]) }
                        }, false, conn, tx);

                    cantidadRestante -= aDescontar;
                }

                if (cantidadRestante > 0)
                {
                    // No había suficiente existencia física para descontar por completo.
                    // Decide si esto debe abortar la transacción o solo quedar registrado.
                    throw new Exception(
                        $"Existencia insuficiente en Modula para el producto {d["cve_prod"]} " +
                        $"(faltaron {cantidadRestante} unidades por descontar).");
                }
            }

            return (true, resultadoWs.Mensaje);
        }

        // ─── Lectura tolerante a NULL de los campos heredados del pedido ─────────────
        // Un pedido de contado no trae plazo ni fecha de pago, y Convert.ToInt32(DBNull)
        // revienta: sin esto, heredar la configuración fallaría justo en esos casos.

        private static bool EsNulo(object valor) => valor == null || valor == DBNull.Value;

        private static int ValorInt(object valor, int porDefecto = 0)
            => EsNulo(valor) ? porDefecto : Convert.ToInt32(valor);

        private static int? ValorIntNulo(object valor)
            => EsNulo(valor) ? (int?)null : Convert.ToInt32(valor);

        private static decimal ValorDecimal(object valor, decimal porDefecto = 0m)
            => EsNulo(valor) ? porDefecto : Convert.ToDecimal(valor);

        private static DateTime? ValorFecha(object valor)
            => EsNulo(valor) ? (DateTime?)null : Convert.ToDateTime(valor);
    }

 }