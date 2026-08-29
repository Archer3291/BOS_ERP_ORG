using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class ConsultaController : Utilities
    {

        // ---------------------------------------------------------------
        // GET  /Consulta/GetPlanoSucursal?sucursalId=X
        // Devuelve el plano (con posiciones) o crea uno por defecto
        // ---------------------------------------------------------------
        public JsonResult GetPlanoSucursal(int sucursalId)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                string queryBuscar = @"
            SELECT row_to_json(t) FROM (
                SELECT
                    ps.id_plano, ps.sucursal_id, ps.nombre,
                    ps.ancho_m, ps.alto_m, ps.escala_px_m,
                    ps.fondo_color, ps.imagen_fondo_url,
                    COALESCE((
                        SELECT jsonb_agg(jsonb_build_object(
                            'almacen_id',  ppa.almacen_id,
                            'x', ppa.x, 'y', ppa.y,
                            'ancho', ppa.ancho, 'alto', ppa.alto,
                            'rotacion', ppa.rotacion,
                            'color_fondo', ppa.color_fondo,
                            'color_borde', ppa.color_borde
                        ))
                        FROM plano_posiciones_almacen ppa
                        WHERE ppa.plano_id = ps.id_plano
                    ), '[]'::jsonb) AS almacenes,
                    COALESCE((
                        SELECT jsonb_agg(jsonb_build_object(
                            'rack_id',    ppr.rack_id,
                            'almacen_id', ppr.almacen_id,
                            'x', ppr.x, 'y', ppr.y,
                            'ancho', ppr.ancho, 'alto', ppr.alto,
                            'rotacion', ppr.rotacion,
                            'color_fondo', ppr.color_fondo,
                            'color_borde', ppr.color_borde
                        ))
                        FROM plano_posiciones_rack ppr
                        WHERE ppr.plano_id = ps.id_plano
                    ), '[]'::jsonb) AS racks
                FROM planos_sucursal ps
                WHERE ps.sucursal_id = @sucursal_id
                  AND ps.empresa_id  = @empresa_id
                  AND ps.activo = TRUE
                LIMIT 1
            ) t;";

                var parameters = new Dictionary<string, object>
        {
            { "sucursal_id", sucursalId },
            { "empresa_id",  empresaId  }
        };

                var result = RunQuery(queryBuscar, parameters);

                // Verificar si el plano existe (result vacío = no existe)
                bool planoVacio = result == null
                    || (result is System.Collections.IList lista && lista.Count == 0)
                    || result.ToString() == "";

                if (planoVacio)
                {
                    // Crear e inmediatamente retornar el plano default con su ID
                    string queryCrear = @"
                INSERT INTO planos_sucursal
                    (sucursal_id, empresa_id, nombre, ancho_m, alto_m, escala_px_m, fondo_color)
                VALUES
                    (@sucursal_id, @empresa_id, 'Plano Principal', 100, 60, 8, '#111827')
                RETURNING row_to_json(row(id_plano, sucursal_id, nombre,
                    ancho_m, alto_m, escala_px_m, fondo_color))::text;";

                    // Mejor aún: usar una query que retorne el objeto completo directamente
                    string queryCrearCompleto = @"
                WITH nuevo AS (
                    INSERT INTO planos_sucursal
                        (sucursal_id, empresa_id, nombre, ancho_m, alto_m, escala_px_m, fondo_color)
                    VALUES
                        (@sucursal_id, @empresa_id, 'Plano Principal', 100, 60, 8, '#111827')
                    RETURNING *
                )
                SELECT row_to_json(t) FROM (
                    SELECT
                        n.id_plano, n.sucursal_id, n.nombre,
                        n.ancho_m, n.alto_m, n.escala_px_m,
                        n.fondo_color,
                        '[]'::jsonb AS almacenes,
                        '[]'::jsonb AS racks
                    FROM nuevo n
                ) t;";

                    result = RunQuery(queryCrearCompleto, parameters);
                }

                // Parsear el resultado de row_to_json (llega como array de dynamic)
                object planoData = result;
                if (result is System.Collections.IList lst && lst.Count > 0)
                {
                    var first = lst[0];
                    // row_to_json retorna { "row_to_json": "{...}" }
                    var dict = first as IDictionary<string, object>;
                    if (dict != null)
                    {
                        var val = dict.Values.First();
                        planoData = val; // puede ser string JSON o ya un objeto
                    }
                }

                return Json(new { ok = true, data = planoData });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        private int CrearPlanoDefault(int sucursalId, int empresaId)
        {
            string query = @"
                INSERT INTO planos_sucursal
                    (sucursal_id, empresa_id, nombre, ancho_m, alto_m, escala_px_m, fondo_color)
                VALUES
                    (@sucursal_id, @empresa_id, 'Plano Principal', 100, 60, 8, '#111827')
                RETURNING id_plano;";

            var parameters = new Dictionary<string, object>
            {
                { "sucursal_id", sucursalId },
                { "empresa_id",  empresaId  }
            };

            var result = RunQuery(query, parameters);
            return Convert.ToInt32(result);
        }

        // ---------------------------------------------------------------
        // POST /Consulta/GuardarPosicionesPlano
        // Guarda el layout completo (batch upsert)
        // Body JSON: { planoId, almacenes: [...], racks: [...] }
        // ---------------------------------------------------------------
        [HttpPost]
        public JsonResult GuardarPosicionesPlano(IFormCollection fc)
        {
            try
            {
                int planoId = Convert.ToInt32(fc["planoId"].ToString());

                var almacenes = JsonConvert.DeserializeObject<List<PosAlmacenDto>>(
                    fc["almacenes"].ToString() ?? "[]");

                var racks = JsonConvert.DeserializeObject<List<PosRackDto>>(
                    fc["racks"].ToString() ?? "[]");

                // Almacenes
                foreach (var alm in almacenes)
                {
                    var parameters = new Dictionary<string, object>
            {
                { "p", planoId },
                { "a", alm.AlmacenId },
                { "x", alm.X },
                { "y", alm.Y },
                { "w", alm.Ancho },
                { "h", alm.Alto },
                { "r", alm.Rotacion },
                { "cf", alm.ColorFondo },
                { "cb", alm.ColorBorde }
            };

                    RunQuery(@"
                SELECT upsert_posicion_almacen(
                    @p,@a,@x,@y,@w,@h,@r,@cf,@cb
                );
            ", parameters);
                }

                // Racks
                foreach (var rack in racks)
                {
                    var parameters = new Dictionary<string, object>
            {
                { "p", planoId },
                { "ri", rack.RackId },
                { "ai", rack.AlmacenId },
                { "x", rack.X },
                { "y", rack.Y },
                { "w", rack.Ancho },
                { "h", rack.Alto },
                { "r", rack.Rotacion },
                { "cf", rack.ColorFondo },
                { "cb", rack.ColorBorde }
            };

                    RunQuery(@"
                SELECT upsert_posicion_rack(
                    @p,@ri,@ai,@x,@y,@w,@h,@r,@cf,@cb
                );
            ", parameters);
                }

                return Json(new
                {
                    ok = true
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    msg = ex.Message
                });
            }
        }

        // ---------------------------------------------------------------
        // POST /Consulta/ActualizarConfigPlano
        // Actualiza métricas del plano (dimensiones, escala, color fondo)
        // ---------------------------------------------------------------
        [HttpPost]
        public JsonResult ActualizarConfigPlano(IFormCollection fc)
        {
            try
            {
                int planoId = Convert.ToInt32(fc["planoId"].ToString());
                decimal anchoM = Convert.ToDecimal(fc["anchoM"].ToString());
                decimal altoM  = Convert.ToDecimal(fc["altoM"].ToString());
                decimal escala = Convert.ToDecimal(fc["escalaPxM"].ToString());
                string fondoColor = fc["fondoColor"].ToString() ?? "#111827";

                string query = @"
                    UPDATE planos_sucursal
                    SET ancho_m       = @ancho_m,
                        alto_m        = @alto_m,
                        escala_px_m   = @escala,
                        fondo_color   = @fondo_color,
                        modificado_en = NOW()
                    WHERE id_plano = @plano_id;";

                var parameters = new Dictionary<string, object>
                {
                    { "plano_id",   planoId    },
                    { "ancho_m",    anchoM     },
                    { "alto_m",     altoM      },
                    { "escala",     escala     },
                    { "fondo_color",fondoColor }
                };

                RunQuery(query, parameters);
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        // ---------------------------------------------------------------
        // GET /Consulta/GetRackDetalle?rackId=X
        // Detalle completo del rack: columnas → niveles → tarimas → productos
        // ---------------------------------------------------------------
        public JsonResult GetRackDetalle(int rackId)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                string query = @"
                    SELECT row_to_json(t) FROM (
                        SELECT
                            r.id_rack,
                            r.nombre,
                            r.num_rack,
                            r.tipo,
                            r.lado,
                            r.capacidad,
                            COALESCE((
                                SELECT jsonb_agg(
                                    jsonb_build_object(
                                        'id',      c.id_columna,
                                        'nombre',  c.nombre,
                                        'num_col', c.num_col,
                                        'niveles', COALESCE((
                                            SELECT jsonb_agg(
                                                jsonb_build_object(
                                                    'id',         n.id_nivel,
                                                    'nombre',     n.nombre,
                                                    'num_nivel',  n.num_nivel,
                                                    'ulocation',  n.ulocation,
                                                    'capacidad',  r.capacidad,
                                                    'tarimas', COALESCE((
                                                        SELECT jsonb_agg(
                                                            jsonb_build_object(
                                                                'id',      t.id_tarima,
                                                                'codigo',  t.codigo,
                                                                'uuid',    t.uuid,
                                                                'fecha',   t.fecha,
                                                                'productos', COALESCE((
                                                                    SELECT jsonb_agg(
                                                                        jsonb_build_object(
                                                                            'id',          tp.id_tarima_producto,
                                                                            'producto_id', tp.producto_id,
                                                                            'cantidad',    tp.cantidad,
                                                                            'unidad',      tp.unidad,
                                                                            'cve_prod',    p.cve_prod,
                                                                            'descr_prod',  p.descr_prod,
                                                                            'lin_prod',    p.lin_prod,
                                                                            'tp',          p.tp,
                                                                            'gpo',         p.gpo,
                                                                            'cod_prov',    p.cod_prov,
                                                                            'cbr',         p.cbr,
                                                                            'udm',         p.udm
                                                                        )
                                                                    )
                                                                    FROM tarima_productos tp
                                                                    JOIN catproductos p ON p.id_catproductos = tp.producto_id
                                                                    WHERE tp.tarima_id = t.id_tarima
                                                                      AND p.empresa_id = @empresa_id
                                                                      AND tp.cantidad > 0
                                                                ), '[]'::jsonb)
                                                            )
                                                        )
                                                        FROM cattarimas t
                                                        WHERE t.nivel_id = n.id_nivel
                                                    ), '[]'::jsonb)
                                                )
                                            ORDER BY n.num_nivel DESC
                                            )
                                            FROM catniveles n
                                            WHERE n.columna_id = c.id_columna
                                        ), '[]'::jsonb)
                                    )
                                ORDER BY c.num_col
                                )
                                FROM catcolumnas c
                                WHERE c.rack_id = r.id_rack
                            ), '[]'::jsonb) AS columnas
                        FROM catracks r
                        WHERE r.id_rack = @rack_id
                    ) t;";

                var parameters = new Dictionary<string, object>
                {
                    { "rack_id",    rackId    },
                    { "empresa_id", empresaId }
                };

                var result = RunQuery(query, parameters);
                return Json(new { ok = true, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        // ---------------------------------------------------------------
        // GET /Consulta/GetHistorialProducto?productoId=X&page=0&pageSize=10
        // Historial de movimientos de tarimas para un producto, paginado
        // ---------------------------------------------------------------
        public JsonResult GetHistorialProducto(int productoId, int page = 0, int pageSize = 10)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int offset = page * pageSize;

                // Query de conteo total (para saber cuántas páginas hay)
                string queryCount = @"
            SELECT COUNT(*)
            FROM tarimas_mov rm
            INNER JOIN catproductos cp ON cp.id_catproductos = rm.producto_id
            WHERE rm.producto_id = @producto_id
              AND cp.empresa_id  = @empresa_id;";

                // Query paginado
                string query = @"
            SELECT
                cp.descr_prod                                   AS producto,
                rm.id_tarima_mov,
                rm.fecha,
                rm.tipo_movimiento,
                rm.cantidad,
                cu.cve_udm,
                CASE
                    WHEN cu.cve_udm = 'SRV'
                    THEN 'Por logica de negocio, los servicios no son inventariables.'
                    ELSE ''
                END                                             AS comentario_adicional,
                COALESCE(e.nat, 'N/A')                          AS tipo_documento,
                CASE
                    WHEN e.gen IS NOT NULL
                    THEN e.gen || '-' || e.nat || '-' ||
                         EXTRACT(YEAR FROM e.fch)::text || '-' || e.fol_doc
                    ELSE '—'
                END                                             AS folio_documento,
                t_origen.codigo                                 AS tarima_origen,
                t_destino.codigo                                AS tarima_destino,
                a_origen.descripcion                            AS almacen_origen,
                a_destino.descripcion                           AS almacen_destino,
                s_origen.descripcion                            AS sucursal_origen,
                s_destino.descripcion                           AS sucursal_destino,
                rm.motivo,
                u.nombre                                        AS usuario
            FROM tarimas_mov rm
            INNER JOIN catproductos cp ON cp.id_catproductos = rm.producto_id
            LEFT  JOIN cattarimas   t_origen  ON t_origen.id_tarima  = rm.tarima_origen
            LEFT  JOIN cattarimas   t_destino ON t_destino.id_tarima = rm.tarima_destino
            LEFT  JOIN catcolumnas  col_orig  ON col_orig.id_columna =
                        (SELECT columna_id FROM catniveles
                         WHERE id_nivel = t_origen.nivel_id LIMIT 1)
            LEFT  JOIN catcolumnas  col_dest  ON col_dest.id_columna =
                        (SELECT columna_id FROM catniveles
                         WHERE id_nivel = t_destino.nivel_id LIMIT 1)
            LEFT  JOIN catracks     r_origen  ON r_origen.id_rack  = col_orig.rack_id
            LEFT  JOIN catracks     r_destino ON r_destino.id_rack = col_dest.rack_id
            LEFT  JOIN catalmacenes a_origen  ON a_origen.id_almacen  = r_origen.almacen_id
            LEFT  JOIN catalmacenes a_destino ON a_destino.id_almacen = r_destino.almacen_id
            LEFT  JOIN catsucursales s_origen  ON s_origen.id_sucursal  = a_origen.sucursal_id
            LEFT  JOIN catsucursales s_destino ON s_destino.id_sucursal = a_destino.sucursal_id
            LEFT  JOIN encabezadomov e  ON e.id_encabezado = rm.encabezado_id
            LEFT  JOIN usuarios      u  ON u.usuarioid     = rm.usuario
            LEFT  JOIN catunidades   cu ON cu.id_udm        = rm.unidad
            WHERE rm.producto_id = @producto_id
              AND cp.empresa_id  = @empresa_id
            ORDER BY rm.fecha DESC
            LIMIT @page_size OFFSET @offset;";

                var parameters = new Dictionary<string, object>
        {
            { "producto_id", productoId },
            { "empresa_id",  empresaId  },
            { "page_size",   pageSize   },
            { "offset",      offset     }
        };

                var countParams = new Dictionary<string, object>
        {
            { "producto_id", productoId },
            { "empresa_id",  empresaId  }
        };

                var total = Convert.ToInt32(RunScalar(queryCount, countParams));
                var rows = RunQuery(query, parameters);

                return Json(new
                {
                    ok = true,
                    total = total,
                    page = page,
                    pageSize = pageSize,
                    totalPages = (int)Math.Ceiling((double)total / pageSize),
                    data = rows
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        // ---------------------------------------------------------------
        //  DTOs
        // ---------------------------------------------------------------
        private class PosAlmacenDto
        {
            public int     AlmacenId  { get; set; }
            public decimal X          { get; set; }
            public decimal Y          { get; set; }
            public decimal Ancho      { get; set; }
            public decimal Alto       { get; set; }
            public decimal Rotacion   { get; set; }
            public string  ColorFondo { get; set; }
            public string  ColorBorde { get; set; }
        }

        private class PosRackDto
        {
            public int     RackId     { get; set; }
            public int     AlmacenId  { get; set; }
            public decimal X          { get; set; }
            public decimal Y          { get; set; }
            public decimal Ancho      { get; set; }
            public decimal Alto       { get; set; }
            public decimal Rotacion   { get; set; }
            public string  ColorFondo { get; set; }
            public string  ColorBorde { get; set; }
        }
    }
}
