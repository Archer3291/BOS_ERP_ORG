using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers.Inventario
{
    [Authorize]
    public class VIValidacionDiscrepanciasController : Utilities
    {
        // ─── Vista principal ────────────────────────────────────────────────
        public IActionResult Index() => View();

        // ─── Buscar documentos DISM paginados con filtros ───────────────────
        [HttpGet]
        public IActionResult BuscarDism(
            string nombre = "",
            string estatus = "",
            string fechaDesde = "",
            string fechaHasta = "",
            int page = 1,
            int pageSize = 25)
        {
            try
            {
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

                var where = new List<string>
                {
                    "em.tp_mov      = 'DISM'",
                    "em.estatus_id != 0",
                    "em.suc         = @suc"
                };

                var p = new Dictionary<string, object>
                {
                    { "suc",      sucursalId },
                    { "offset",   (page - 1) * pageSize },
                    { "pageSize", pageSize }
                };

                if (!string.IsNullOrWhiteSpace(nombre))
                {
                    where.Add(@"(LOWER(em.folio)    LIKE LOWER(@nombre)
                              OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
                              OR LOWER(cc.n_cli)    LIKE LOWER(@nombre))");
                    p["nombre"] = $"%{nombre}%";
                }

                if (!string.IsNullOrWhiteSpace(fechaDesde))
                {
                    where.Add("em.fch >= @fdesde::date");
                    p["fdesde"] = fechaDesde;
                }

                if (!string.IsNullOrWhiteSpace(fechaHasta))
                {
                    where.Add("em.fch < (@fhasta::date + INTERVAL '1 day')");
                    p["fhasta"] = fechaHasta;
                }

                switch (estatus)
                {
                    case "pendiente":
                        where.Add("(em.validacion_dism IS NULL OR em.validacion_dism = 'pendiente')");
                        break;
                    case "parcial":
                        where.Add("em.validacion_dism = 'parcial'");
                        break;
                    case "validado":
                        where.Add("em.validacion_dism = 'validado'");
                        break;
                }

                string wStr = string.Join(" AND ", where);

                string query = $@"
                    SELECT
                        em.id_encabezado,
                        em.folio,
                        em.fch,
                        em.cli_prov,
                        em.imp,
                        em.coment_aut,
                        em.validacion_dism,
                        em.pendiente_de              AS encabezados_padre,
                        cc.n_cli,
                        ep.folio                     AS folio_padre,

                        -- Total de discrepancias del DISM
                        (SELECT COUNT(*)
                         FROM   discrepancia_fisica df
                         WHERE  df.id_encabezado_pedido = em.id_encabezado
                        )                            AS total_partidas,

                        -- Partidas ya validadas (ajuste confirmado)
                        (SELECT COUNT(*)
                         FROM   discrepancia_fisica df
                         WHERE  df.id_encabezado_pedido = em.id_encabezado
                           AND  df.validado             = TRUE
                        )                            AS partidas_validadas,

                        -- Usuario de la última validación
                        (SELECT u.nombre
                         FROM   usuarios u
                         INNER JOIN discrepancia_fisica df
                             ON df.usuario_valida_id = u.usuarioid
                         WHERE  df.id_encabezado_pedido = em.id_encabezado
                           AND  df.validado             = TRUE
                         ORDER  BY df.fecha_validacion DESC
                         LIMIT  1
                        )                            AS usuario_validacion,

                        -- Fecha de última validación
                        (SELECT df.fecha_validacion
                         FROM   discrepancia_fisica df
                         WHERE  df.id_encabezado_pedido = em.id_encabezado
                           AND  df.validado             = TRUE
                         ORDER  BY df.fecha_validacion DESC
                         LIMIT  1
                        )                            AS fecha_validacion

                    FROM encabezadomov em
                    INNER JOIN catclientes cc
                        ON cc.id_cliente = em.refe
                    LEFT JOIN encabezadomov ep
                        ON ep.id_encabezado = em.pendiente_de
                    WHERE {wStr}
                    ORDER BY em.fch DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var items = RunQuery(query, p);

                // ── Total para paginación ──
                var pTotal = new Dictionary<string, object> { { "suc", sucursalId } };
                if (p.ContainsKey("nombre")) pTotal["nombre"] = p["nombre"];
                if (p.ContainsKey("fdesde")) pTotal["fdesde"] = p["fdesde"];
                if (p.ContainsKey("fhasta")) pTotal["fhasta"] = p["fhasta"];

                string qTotal = $@"
                    SELECT COUNT(*) AS total
                    FROM encabezadomov em
                    INNER JOIN catclientes cc ON cc.id_cliente = em.refe
                    WHERE {wStr}";

                var totalRes = RunQuery(qTotal, pTotal);
                int total = totalRes?.Count > 0 ? Convert.ToInt32(totalRes[0]["total"]) : 0;

                return Json(new { success = true, items, total });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── KPIs del módulo ────────────────────────────────────────────────
        [HttpGet]
        public IActionResult ObtenerKpis()
        {
            try
            {
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

                string q = @"
                    SELECT
                        COUNT(DISTINCT em.id_encabezado)                                   AS total_dism,
                        COUNT(DISTINCT em.id_encabezado)
                            FILTER (WHERE COALESCE(em.validacion_dism,'pendiente')
                                         IN ('pendiente',''))                              AS pendientes,
                        COUNT(DISTINCT em.id_encabezado)
                            FILTER (WHERE em.validacion_dism = 'parcial')                  AS parciales,
                        COUNT(DISTINCT em.id_encabezado)
                            FILTER (WHERE em.validacion_dism = 'validado')                 AS validados,
                        COALESCE(SUM(DISTINCT em.imp), 0)                                  AS importe_total,
                        COALESCE(SUM(DISTINCT em.imp)
                            FILTER (WHERE em.validacion_dism = 'validado'), 0)             AS importe_validado,
                        -- Total de discrepancias pendientes de ajuste
                        (SELECT COUNT(*) FROM discrepancia_fisica df2
                         INNER JOIN encabezadomov em2 ON em2.id_encabezado = df2.id_encabezado_pedido
                         WHERE df2.validado = FALSE
                           AND em2.suc = @suc
                           AND em2.fch >= NOW() - INTERVAL '30 days')                      AS discrepancias_pendientes
                    FROM encabezadomov em
                    WHERE em.tp_mov      = 'DISM'
                      AND em.estatus_id != 0
                      AND em.suc         = @suc
                      AND em.fch        >= (NOW() - INTERVAL '30 days')";

                var r = RunQuery(q, new Dictionary<string, object> { { "suc", sucursalId } });
                var row = r?[0] ?? new Dictionary<string, object>();

                return Json(new
                {
                    success = true,
                    total_dism = row.GetOrDefault("total_dism", 0),
                    pendientes = row.GetOrDefault("pendientes", 0),
                    parciales = row.GetOrDefault("parciales", 0),
                    validados = row.GetOrDefault("validados", 0),
                    importe_total = row.GetOrDefault("importe_total", 0),
                    importe_validado = row.GetOrDefault("importe_validado", 0),
                    discrepancias_pendientes = row.GetOrDefault("discrepancias_pendientes", 0)
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Detalle completo de un DISM ────────────────────────────────────
        // Devuelve todas las discrepancias físicas registradas para el DISM,
        // junto con la existencia actual en tarima_productos (cantidad_sistema real)
        // y los campos para que el encargado ingrese su reconteo.
        [HttpGet]
        public IActionResult ObtenerDetalleDism(int idDism)
        {
            try
            {
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

                // ── Encabezado ──
                var encRes = RunQuery(@"
                    SELECT
                        em.id_encabezado,
                        em.folio,
                        em.fch,
                        em.cli_prov,
                        em.imp,
                        em.coment_aut,
                        em.validacion_dism,
                        em.pendiente_de     AS encabezados_padre,
                        cc.n_cli,
                        cc.rfc,
                        ep.folio            AS folio_padre
                    FROM encabezadomov em
                    INNER JOIN catclientes cc ON cc.id_cliente = em.refe
                    LEFT  JOIN encabezadomov ep ON ep.id_encabezado = em.pendiente_de
                    WHERE em.id_encabezado = @id",
                    new Dictionary<string, object> { { "id", idDism } });

                if (encRes == null || encRes.Count == 0)
                    return Json(new { success = false, message = "DISM no encontrado." });

                var enc = encRes[0];

                // ── Discrepancias con datos de partida y existencia actual ──
                // La cantidad "en sistema" que ve el encargado es la suma real
                // de tarima_productos en tarimas de tipo Stock de esta sucursal.
                var partidas = RunQuery(@"
                    SELECT
                        -- Discrepancia física
                        df.id_discrepancia_fisica,
                        df.id_partida,
                        df.id_verificacion_detalle,
                        df.cantidad_sistema         AS cantidad_sistema_captura,   -- al momento del conteo
                        df.cantidad_fisica,                                         -- lo que encontró el almacenista
                        df.diferencia,
                        df.motivo,
                        df.validado,
                        df.cantidad_real_ajustada,
                        df.motivo_validacion,
                        df.fecha_validacion,
                        df.usuario_valida_id,
                        uv.nombre                   AS nombre_validador,

                        -- Partida del DISM
                        pd.nro_part,
                        pd.cve_prod,
                        pd.descr_prod,
                        pd.cant_ud                  AS cantidad_dism,
                        pd.ud                       AS unidad,
                        pd.pv_prod                  AS precio,

                        -- Almacén/lote donde se detectó la discrepancia
                        vd.cve_almacen,
                        vd.n_almacen,
                        vd.lote,
                        vd.ubicacion,

                        -- Existencia ACTUAL en sistema (tarimas Stock) para poder comparar al reconteo
                        COALESCE((
                            SELECT SUM(tp.cantidad)
                            FROM   tarima_productos  tp
                            INNER JOIN cattarimas    ct  ON ct.id_tarima   = tp.tarima_id
                            INNER JOIN catniveles    cn  ON cn.id_nivel    = ct.nivel_id
                            INNER JOIN catcolumnas   col ON col.id_columna = cn.columna_id
                            INNER JOIN catracks      cr  ON cr.id_rack     = col.rack_id
                            INNER JOIN catalmacenes  ca  ON ca.id_almacen  = cr.almacen_id
                            INNER JOIN catsucursales cs  ON cs.id_sucursal = ca.sucursal_id
                            WHERE  tp.producto_id  = pd.producto_id
                              AND  cs.id_sucursal  = @sucursal
                              AND  ca.tipo         = 'Stock'
                              AND  tp.cantidad     > 0
                        ), 0)                       AS existencia_sistema_actual

                    FROM discrepancia_fisica df
                    INNER JOIN partidasdoc pd
                        ON pd.id_partidas = df.id_partida
                    LEFT JOIN verificacion_detalle vd
                        ON vd.id = df.id_verificacion_detalle
                    LEFT JOIN usuarios uv
                        ON uv.usuarioid = df.usuario_valida_id
                    WHERE df.id_encabezado_pedido = (select encabezados_padre from encabezadomov where id_encabezado = @id)
                    ORDER BY pd.nro_part",
                    new Dictionary<string, object>
                    {
                        { "id",       idDism },
                        { "sucursal", sucursalId }
                    });

                return Json(new
                {
                    success = true,
                    encabezado = new
                    {
                        id_encabezado = enc["id_encabezado"],
                        folio = enc["folio"],
                        fch = enc["fch"],
                        cli_prov = enc["cli_prov"],
                        n_cli = enc["n_cli"],
                        rfc = enc["rfc"],
                        imp = enc["imp"],
                        coment_aut = enc["coment_aut"],
                        validacion_dism = enc["validacion_dism"],
                        pedido_padre = enc["encabezados_padre"],
                        folio_padre = enc["folio_padre"]
                    },
                    partidas
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Confirmar ajuste de inventario para una partida ────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras", Accion = "Ajuste inventario por discrepancia DISM")]
        public JsonResult ConfirmarAjuste(
           int idDiscrepancia,
           int idDism,
           decimal cantidadReal,
           string motivoValidacion)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivoValidacion))
                    return Json(new { success = false, message = "Debe indicar el motivo del ajuste." });

                if (cantidadReal < 0)
                    return Json(new { success = false, message = "La cantidad real no puede ser negativa." });

                int usuarioId = GetUserId(User.Identity.Name);
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // ── 1. Obtener datos de la partida desde la discrepancia ──────────
                            var partidaRes = RunQuery(@"
                        SELECT
                            pd.producto_id,
                            pd.cve_prod,
                            pd.descr_prod,
                            pd.ud           AS unidad_id,
                            df.diferencia,
                            df.cantidad_sistema,
                            df.cantidad_fisica
                        FROM discrepancia_fisica df
                        INNER JOIN partidasdoc pd ON pd.id_partidas = df.id_partida
                        WHERE df.id_discrepancia_fisica = @id_df",
                                new Dictionary<string, object> { { "id_df", idDiscrepancia } },
                                false, conn, tx);

                            if (partidaRes == null || partidaRes.Count == 0)
                                return Json(new { success = false, message = "Discrepancia no encontrada." });

                            var partida = partidaRes[0];
                            int idProducto = Convert.ToInt32(partida["producto_id"]);
                            string codigo = partida["cve_prod"]?.ToString();
                            string descripcion = partida["descr_prod"]?.ToString();


                            var parameters = new Dictionary<string, object>();
                            string query = "SELECT id_udm FROM catunidades WHERE cve_udm = @unidad";
                            parameters.Add("unidad", partida["unidad_id"]?.ToString());
                            int idudm = (int)RunScalar(query, parameters, false, conn, tx);

                            // La diferencia a ajustar es: cantidadReal - cantidad_sistema
                            // Si es negativa → salida de inventario; si positiva → entrada
                            decimal cantidadSistema = Convert.ToDecimal(partida["cantidad_sistema"]);
                            decimal diferencia = cantidadReal - cantidadSistema; // negativo = faltante
                            decimal cantidadADescontar = Math.Abs(diferencia);
                            string tipoMovimiento = diferencia < 0 ? "salida" : "entrada";

                            // ── 2. Marcar la discrepancia como validada ───────────────────────
                            RunQuery(@"
                        UPDATE discrepancia_fisica
                           SET validado               = TRUE,
                               cantidad_real_ajustada = @cant_real,
                               motivo_validacion      = @motivo,
                               usuario_valida_id      = @usr,
                               fecha_validacion       = NOW()
                         WHERE id_discrepancia_fisica = @id_df",
                                new Dictionary<string, object>
                                {
                            { "id_df",     idDiscrepancia  },
                            { "cant_real", cantidadReal    },
                            { "motivo",    motivoValidacion },
                            { "usr",       usuarioId       }
                                }, false, conn, tx);

                            // ── 3. Registrar movimientos por tarima solo si hay diferencia ────
                            if (diferencia != 0)
                            {
                                var tarimas = RunQuery(
                                    @"SELECT ct.id_tarima, tp.cantidad
                              FROM   catalmacenes    c
                              INNER JOIN catsucursales  cs  ON cs.id_sucursal = c.sucursal_id
                              INNER JOIN catracks        cr  ON cr.almacen_id  = c.id_almacen
                              INNER JOIN catcolumnas     col ON col.rack_id     = cr.id_rack
                              INNER JOIN catniveles      cn  ON cn.columna_id   = col.id_columna
                              INNER JOIN cattarimas      ct  ON ct.nivel_id     = cn.id_nivel
                              INNER JOIN tarima_productos tp  ON tp.tarima_id   = ct.id_tarima
                              WHERE  cs.id_sucursal  = @sucursal
                                AND  c.tipo          = 'Stock'
                                AND  tp.producto_id  = @producto_id
                                AND  tp.cantidad     > 0",
                                    new Dictionary<string, object>
                                    {
                                { "sucursal",    sucursalId },
                                { "producto_id", idProducto }
                                    }, false, conn, tx);

                                decimal restante = cantidadADescontar;

                                foreach (var t in tarimas)
                                {
                                    if (restante <= 0) break;

                                    int tarimaId = Convert.ToInt32(t["id_tarima"]);
                                    decimal stockTarima = Convert.ToDecimal(t["cantidad"]);
                                    //decimal descontar = Math.Min(stockTarima, restante);
                                    //restante -= descontar; // ← corregido: restaba mal antes

                                    var prodMovimiento = new Dictionary<string, object>
                                    {
                                        { "id_producto",  idProducto  },
                                        { "codigo",       codigo      },
                                        { "descripcion",  descripcion },
                                        { "cantidad",     restante   },
                                        { "unidad",       idudm       },
                                        { "tarima_id",    tarimaId    },
                                        { "tipo",         "reajuste"  },
                                        { "movimiento",   tipoMovimiento }
                                    };


                                    if (diferencia > 0)
                                    {
                                        RegistrarMovimiento(
                                        new List<Dictionary<string, object>> { prodMovimiento },
                                        usuarioId,
                                        "reajuste",
                                        null,
                                        tarimaId,
                                        tipoMovimiento,
                                        idDiscrepancia,
                                        null,
                                        conn, tx
                                    );
                                    }

                                    if (diferencia < 0)
                                    {
                                        RegistrarMovimiento(
                                            new List<Dictionary<string, object>> { prodMovimiento },
                                            usuarioId,
                                            "reajuste",
                                            tarimaId,
                                            null,
                                            tipoMovimiento,
                                            idDiscrepancia,
                                            null,
                                            conn, tx
                                        );
                                    }
                                }
                            }

                            // ── 4. Recalcular estado global del DISM ─────────────────────────
                            string nuevoEstado = RecalcularEstadoDism(idDism, conn, tx);

                            RunQuery(@"
                        UPDATE encabezadomov
                           SET validacion_dism = @est
                         WHERE id_encabezado   = @id_dism",
                                new Dictionary<string, object>
                                {
                            { "est",     nuevoEstado },
                            { "id_dism", idDism      }
                                }, false, conn, tx);

                            tx.Commit();

                            return Json(new
                            {
                                success = true,
                                nuevo_estado = nuevoEstado,
                                message = "Inventario ajustado correctamente."
                            });
                        }
                        catch { tx.Rollback(); throw; }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Confirmar ajuste masivo (todas las partidas del DISM) ──────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras", Accion = "Ajuste masivo inventario DISM")]
        public JsonResult ConfirmarAjusteMasivo(
            int idDism,
            string partidasJson,
            string motivoGlobal = "Ajuste masivo validado")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(partidasJson))
                    return Json(new { success = false, message = "No se recibieron partidas." });

                var partidas = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(partidasJson);
                if (partidas == null || !partidas.Any())
                    return Json(new { success = false, message = "Lista de partidas vacía." });

                int usuarioId = GetUserId(User.Identity.Name);
                int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Llamar a la función de PostgreSQL que procesa el ajuste de inventario
                            using (var cmd = new NpgsqlCommand(
                                "SELECT procesar_ajuste_inventario(@p_encabezado_id, @p_usuario, @p_empresa)",
                                conn, tx))
                            {
                                cmd.Parameters.AddWithValue("p_encabezado_id", idDism);
                                cmd.Parameters.AddWithValue("p_usuario", usuarioId);
                                cmd.Parameters.AddWithValue("p_empresa", empresaId);
                                cmd.ExecuteScalar();
                            }

                            // 2. Actualizar el estatus del documento a 11 (validado/cerrado)
                            using (var cmd = new NpgsqlCommand(@"
                        UPDATE encabezadomov
                           SET estatus_id      = 11,
                               validacion_dism = 'validado'
                         WHERE id_encabezado = @id_dism",
                                conn, tx))
                            {
                                cmd.Parameters.AddWithValue("id_dism", idDism);
                                cmd.ExecuteNonQuery();
                            }

                            tx.Commit();

                            return Json(new
                            {
                                success = true,
                                nuevo_estado = "validado",
                                message = $"Ajuste masivo aplicado. {partidas.Count} partida(s) procesada(s)."
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

        // ─── Rechazar DISM (sin tocar inventario) ───────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras", Accion = "Rechazar discrepancia DISM")]
        public JsonResult RechazarDism(int idDism, string motivoRechazo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivoRechazo))
                    return Json(new { success = false, message = "Debe indicar el motivo del rechazo." });

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
                            RunQuery(@"
                                UPDATE discrepancia_fisica
                                   SET validado               = FALSE,
                                       cantidad_real_ajustada = cantidad_fisica,
                                       motivo_validacion      = @motivo,
                                       usuario_valida_id      = @usr,
                                       fecha_validacion       = NOW()
                                 WHERE id_encabezado_pedido = @id_dism",
                                new Dictionary<string, object>
                                {
                                    { "id_dism", idDism },
                                    { "motivo",  $"RECHAZADO: {motivoRechazo}" },
                                    { "usr",     usuarioId }
                                }, false, conn, tx);

                            RunQuery(@"
                                UPDATE encabezadomov
                                   SET estatus_id      = 0,
                                       validacion_dism = 'rechazado',
                                       coment_aut      = @motivo
                                 WHERE id_encabezado = @id_dism",
                                new Dictionary<string, object>
                                {
                                    { "id_dism", idDism },
                                    { "motivo",  $"RECHAZADO: {motivoRechazo}" }
                                }, false, conn, tx);

                            tx.Commit();
                            return Json(new { success = true, message = "DISM rechazado. Inventario sin cambios." });
                        }
                        catch { tx.Rollback(); throw; }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Historial de auditoría ─────────────────────────────────────────
        [HttpGet]
        public IActionResult ObtenerHistorial(int idDism)
        {
            try
            {
                var r = RunQuery(@"
                    SELECT
                        df.id_discrepancia_fisica,
                        pd.cve_prod,
                        pd.descr_prod,
                        df.cantidad_sistema,
                        df.cantidad_fisica,
                        df.diferencia,
                        df.motivo                   AS motivo_discrepancia,
                        df.validado,
                        df.cantidad_real_ajustada,
                        df.motivo_validacion,
                        df.fecha_validacion,
                        u_reg.nombre                AS usuario_registro,
                        u_val.nombre                AS usuario_validacion,
                        df.fecha_registro,
                        -- Diferencia aplicada al inventario
                        (df.cantidad_real_ajustada - df.cantidad_sistema) AS diferencia_aplicada
                    FROM discrepancia_fisica df
                    INNER JOIN partidasdoc pd
                        ON pd.id_partidas = df.id_partida
                    LEFT JOIN verificacion_detalle vd
                        ON vd.id = df.id_verificacion_detalle
                    LEFT JOIN usuarios u_reg ON u_reg.usuarioid = df.usuario_id
                    LEFT JOIN usuarios u_val ON u_val.usuarioid = df.usuario_valida_id
                    WHERE df.id_encabezado_pedido = @id_dism
                    ORDER BY df.fecha_registro DESC",
                    new Dictionary<string, object> { { "id_dism", idDism } });

                return Json(new { success = true, historial = r });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Helper: recalcular estado del DISM dentro de una transacción ──
        private string RecalcularEstadoDism(int idDism, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var res = RunQuery(@"
                SELECT
                    COUNT(*)                                    AS total,
                    COUNT(*) FILTER (WHERE df.validado = TRUE) AS validadas
                FROM discrepancia_fisica df
                WHERE df.id_encabezado_pedido = @id_dism",
                new Dictionary<string, object> { { "id_dism", idDism } },
                false, conn, tx);

            if (res == null || res.Count == 0) return "pendiente";

            int total = Convert.ToInt32(res[0]["total"]);
            int validadas = Convert.ToInt32(res[0]["validadas"]);

            return validadas == 0 ? "pendiente"
                 : validadas < total ? "parcial"
                 : "validado";
        }
    }

    internal static class DictExtensions
    {
        public static TValue GetOrDefault<TKey, TValue>(
            this IDictionary<TKey, TValue> dict, TKey key, TValue def = default)
            => dict != null && dict.TryGetValue(key, out TValue v) ? v : def;
    }
}