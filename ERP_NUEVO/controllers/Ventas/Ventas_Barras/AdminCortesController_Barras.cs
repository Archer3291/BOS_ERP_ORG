using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    /// <summary>
    /// Taller de barras: los datos que consume la escena 3D y todo lo que gira
    /// alrededor del código de barras por pieza física (<c>corte_piezas</c>).
    ///
    /// Requiere haber ejecutado <c>sql/cortes_piezas.sql</c>. Las acciones
    /// originales del controlador siguen intactas en el archivo principal.
    /// </summary>
    public partial class AdminCortesController : Utilities
    {
        // Joins que acotan el inventario a la sucursal en sesión y a almacenes
        // de tipo Stock, con el mismo criterio que usa la asignación de cortes.
        private const string JOINS_UBICACION = @"
            INNER JOIN cattarimas   ct  ON ct.id_tarima   = tp.tarima_id
            INNER JOIN catniveles   cn  ON cn.id_nivel    = ct.nivel_id
            INNER JOIN catcolumnas  col ON col.id_columna = cn.columna_id
            INNER JOIN catracks     cr  ON cr.id_rack     = col.rack_id
            INNER JOIN catalmacenes ca  ON ca.id_almacen  = cr.almacen_id
            INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id";

        private int EmpresaActual() => Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
        private int SucursalActual() => Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

        // ─────────────────────────────────────────────────────────────────────
        //  Escena: todo el material de la sucursal, agrupado por producto
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// GET /AdminCortes/EscenaBarras
        /// Devuelve productos → cortes → piezas físicas en una sola llamada,
        /// que es lo que la escena 3D necesita para dibujar barra por barra.
        /// El árbol se arma en Postgres y se reenvía tal cual, como el del
        /// almacén virtual: deserializarlo aquí solo arriesgaría que el
        /// serializador de MVC renombre las claves.
        /// </summary>
        [HttpGet]
        public IActionResult EscenaBarras(string busqueda, string tipo)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id", EmpresaActual() },
                    { "sucursal",   SucursalActual() }
                };

                // Antes esta vista exigía al menos un corte activo, así que un
                // producto con existencia pero SIN desglosar (el caso más común
                // al recibir material nuevo) era invisible en la escena. Ahora
                // basta con tener existencia; el front dibuja lo no configurado
                // como una barra "pendiente" a partir de existencia_total menos
                // metros_configurados.
                string filtroBusqueda = "";
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    filtroBusqueda = @" AND (cp.cve_prod ILIKE @busqueda OR cp.descr_prod ILIKE @busqueda
                                          OR EXISTS (
                                                SELECT 1 FROM tarima_productos_cortes tpcb
                                                WHERE tpcb.tarima_producto_id = tp.id_tarima_producto
                                                  AND tpcb.activo = true AND tpcb.folio ILIKE @busqueda
                                             )) ";
                    parameters.Add("busqueda", $"%{busqueda}%");
                }

                // tipo: 'tubo' | 'barra' | vacío = ambos
                string filtroTipo = tipo == "tubo" ? " AND COALESCE(cp.es_tubo, false) = true "
                                  : tipo == "barra" ? " AND COALESCE(cp.es_tubo, false) = false "
                                  : "";

                string query = $@"
SELECT COALESCE(jsonb_agg(t ORDER BY t.es_tubo DESC, t.cve_prod), '[]'::jsonb)
FROM (
    SELECT
        cp.id_catproductos              AS id_producto,
        cp.cve_prod,
        cp.descr_prod,
        COALESCE(cp.es_tubo, false)     AS es_tubo,
        cp.udm,
        tp.id_tarima_producto,
        tp.cantidad                     AS existencia_total,
        ct.codigo                       AS tarima,
        cn.ulocation,
        cr.nombre                       AS rack,
        ca.cve_almacen                  AS almacen,
        COALESCE((
            SELECT SUM(tpc.longitud * tpc.cantidad)
            FROM tarima_productos_cortes tpc
            WHERE tpc.tarima_producto_id = tp.id_tarima_producto AND tpc.activo = true
        ), 0) AS metros_configurados,
        COALESCE((
            SELECT jsonb_agg(c ORDER BY c.longitud DESC)
            FROM (
                SELECT
                    tpc.id_corte, tpc.folio, tpc.longitud, tpc.cantidad,
                    tpc.cantidad_original, tpc.comentario, tpc.fecha_creacion, tpc.es_sobrante,
                    COALESCE((
                        SELECT jsonb_agg(p ORDER BY p.id_pieza)
                        FROM (
                            SELECT pz.id_pieza, pz.codigo, pz.longitud, pz.estado,
                                   pz.fecha_creacion, pz.pieza_madre_id
                            FROM corte_piezas pz
                            WHERE pz.corte_id = tpc.id_corte
                              AND pz.estado = 'disponible'
                        ) p
                    ), '[]'::jsonb) AS piezas
                FROM tarima_productos_cortes tpc
                WHERE tpc.tarima_producto_id = tp.id_tarima_producto
                  AND tpc.activo = true
                  AND tpc.cantidad > 0
            ) c
        ), '[]'::jsonb) AS cortes
    FROM tarima_productos tp
    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                              AND cp.empresa_id = @empresa_id
    {JOINS_UBICACION}
    WHERE cs.id_sucursal = @sucursal
      AND ca.tipo = 'Stock'
      AND tp.cantidad > 0
      AND cp.es_tubo = true
      {filtroTipo}
      {filtroBusqueda}
) t;";

                var filas = RunQuery(query, parameters);
                string json = filas.Count > 0 ? filas[0].Values.First()?.ToString() : "[]";
                if (string.IsNullOrWhiteSpace(json)) json = "[]";

                return Content("{\"ok\":true,\"productos\":" + json + "}", "application/json");
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes_Barras/EscenaBarras");
                return Json(new { ok = false, message = ex.Message });
            }
        }

        /// <summary>
        /// GET /AdminCortes/BuscarProductoInventario?q=texto
        /// Autocompletado para el modal de "Agregar pieza": solo devuelve
        /// productos que YA tienen existencia en la sucursal en sesión, para
        /// no dejar elegir una clave que luego el desglose rechazará por no
        /// tener inventario.
        /// </summary>
        [HttpGet]
        public IActionResult BuscarProductoInventario(string q)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id", EmpresaActual() },
                    { "sucursal",   SucursalActual() }
                };

                string filtro = "";
                if (!string.IsNullOrWhiteSpace(q))
                {
                    filtro = " AND (cp.cve_prod ILIKE @q OR cp.descr_prod ILIKE @q) ";
                    parameters.Add("q", $"%{q}%");
                }

                var productos = RunQuery($@"
                    SELECT cp.cve_prod, cp.descr_prod, COALESCE(cp.es_tubo, false) AS es_tubo,
                           SUM(tp.cantidad) AS existencia_total
                    FROM tarima_productos tp
                    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                                              AND cp.empresa_id = @empresa_id
                    {JOINS_UBICACION}
                    WHERE cs.id_sucursal = @sucursal AND ca.tipo = 'Stock' AND tp.cantidad > 0
                      AND cp.es_tubo = true
                      {filtro}
                    GROUP BY cp.cve_prod, cp.descr_prod, cp.es_tubo
                    ORDER BY cp.cve_prod
                    LIMIT 20;",
                    parameters);

                return Json(new { ok = true, productos });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes_Barras/BuscarProductoInventario");
                return Json(new { ok = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Piezas físicas y sus etiquetas
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// GET /AdminCortes/ListarPiezas?idCorte=X
        /// Las barras individuales de un corte, cada una con su código.
        /// </summary>
        [HttpGet]
        public IActionResult ListarPiezas(int idCorte, bool incluirBajas = false)
        {
            try
            {
                string filtroEstado = incluirBajas ? "" : " AND pz.estado = 'disponible' ";

                var piezas = RunQuery($@"
                    SELECT pz.id_pieza, pz.codigo, pz.longitud, pz.estado,
                           pz.fecha_creacion, pz.fecha_baja, pz.comentario,
                           madre.codigo AS codigo_madre,
                           c.folio, c.longitud AS longitud_corte,
                           cp.cve_prod, cp.descr_prod,
                           COALESCE(cp.es_tubo, false) AS es_tubo
                    FROM corte_piezas pz
                    INNER JOIN tarima_productos_cortes c ON c.id_corte = pz.corte_id
                    INNER JOIN tarima_productos tp ON tp.id_tarima_producto = c.tarima_producto_id
                    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                    LEFT  JOIN corte_piezas madre ON madre.id_pieza = pz.pieza_madre_id
                    WHERE pz.corte_id = @idCorte {filtroEstado}
                    ORDER BY pz.id_pieza;",
                    new Dictionary<string, object> { { "idCorte", idCorte } });

                return Json(new { ok = true, piezas });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes_Barras/ListarPiezas");
                return Json(new { ok = false, message = ex.Message });
            }
        }

        /// <summary>
        /// GET /AdminCortes/BuscarPieza?codigo=BRR000000123
        /// Lo que hay detrás de una etiqueta escaneada: qué barra es, cuánto
        /// mide, dónde está y —si ya se consumió— en qué pedidos se usó el
        /// corte del que salió.
        /// </summary>
        [HttpGet]
        public IActionResult BuscarPieza(string codigo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(codigo))
                    return Json(new { ok = false, message = "Indica un código." });

                codigo = codigo.Trim().ToUpperInvariant();

                var filas = RunQuery($@"
                    SELECT pz.id_pieza, pz.codigo, pz.longitud, pz.estado,
                           pz.fecha_creacion, pz.fecha_baja, pz.comentario,
                           madre.codigo AS codigo_madre,
                           c.id_corte, c.folio, c.longitud AS longitud_corte,
                           c.cantidad AS cantidad_corte, c.comentario AS comentario_corte,
                           cp.cve_prod, cp.descr_prod, cp.udm,
                           COALESCE(cp.es_tubo, false) AS es_tubo,
                           ct.codigo AS tarima, cn.ulocation,
                           cr.nombre AS rack, ca.cve_almacen, cs.cve_sucursal
                    FROM corte_piezas pz
                    INNER JOIN tarima_productos_cortes c ON c.id_corte = pz.corte_id
                    INNER JOIN tarima_productos tp ON tp.id_tarima_producto = c.tarima_producto_id
                    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                    {JOINS_UBICACION}
                    LEFT JOIN corte_piezas madre ON madre.id_pieza = pz.pieza_madre_id
                    WHERE pz.codigo = @codigo;",
                    new Dictionary<string, object> { { "codigo", codigo } });

                if (filas.Count == 0)
                    return Json(new { ok = false, noEncontrado = true, message = $"No existe ninguna pieza con el código {codigo}." });

                var pieza = filas[0];

                // Para una pieza consumida no hay enlace directo a la asignación
                // (el trigger no la conoce), así que se devuelven las del corte
                // del que salió — que es la respuesta honesta a «dónde acabó».
                var usos = RunQuery(@"
                    SELECT a.id AS asignacion_id, a.cantidad_asignada, a.sobrante,
                           a.estatus, a.fecha_confirmacion,
                           cc.longitud AS longitud_solicitada,
                           pd.nro_part, em.folio AS folio_pedido, em.cli_prov
                    FROM pedido_detalle_corte_asignacion a
                    INNER JOIN pedido_detalle_corte cc ON cc.id = a.pedido_detalle_corte_id
                    INNER JOIN partidasdoc pd ON pd.id_partidas = cc.pedido_detalle_id
                    INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
                    WHERE a.tarima_producto_corte_id = @idCorte
                    ORDER BY a.fecha_confirmacion DESC NULLS LAST, a.id DESC
                    LIMIT 20;",
                    new Dictionary<string, object> { { "idCorte", pieza["id_corte"] } });

                return Json(new { ok = true, pieza, usos });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes_Barras/BuscarPieza");
                return Json(new { ok = false, message = ex.Message });
            }
        }

        /// <summary>
        /// GET /AdminCortes/PiezasParaEtiquetar?idCorte=&amp;productoId=&amp;codigo=
        /// Las piezas que se van a imprimir. Admite un corte suelto, todas las
        /// de un producto (como se etiqueta cuando llega material nuevo), o una
        /// sola pieza por su código (para reponer una etiqueta perdida).
        /// </summary>
        [HttpGet]
        public IActionResult PiezasParaEtiquetar(int? idCorte, string productoId, string codigo)
        {
            try
            {
                if (!idCorte.HasValue && string.IsNullOrWhiteSpace(productoId) && string.IsNullOrWhiteSpace(codigo))
                    return Json(new { ok = false, message = "Indica un corte, un producto o un código." });

                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id", EmpresaActual() },
                    { "sucursal",   SucursalActual() }
                };

                string filtro;
                if (!string.IsNullOrWhiteSpace(codigo))
                {
                    filtro = " AND pz.codigo = @codigo ";
                    parameters.Add("codigo", codigo.Trim().ToUpperInvariant());
                }
                else if (idCorte.HasValue)
                {
                    filtro = " AND pz.corte_id = @idCorte ";
                    parameters.Add("idCorte", idCorte.Value);
                }
                else
                {
                    filtro = " AND cp.cve_prod = @productoId ";
                    parameters.Add("productoId", productoId);
                }

                var piezas = RunQuery($@"
                    SELECT pz.id_pieza, pz.codigo, pz.longitud, pz.estado,
                           c.folio, cp.cve_prod, cp.descr_prod, cp.udm,
                           COALESCE(cp.es_tubo, false) AS es_tubo,
                           cn.ulocation, cr.nombre AS rack, ca.cve_almacen
                    FROM corte_piezas pz
                    INNER JOIN tarima_productos_cortes c ON c.id_corte = pz.corte_id
                    INNER JOIN tarima_productos tp ON tp.id_tarima_producto = c.tarima_producto_id
                    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                                              AND cp.empresa_id = @empresa_id
                    {JOINS_UBICACION}
                    WHERE pz.estado = 'disponible'
                      AND cs.id_sucursal = @sucursal AND ca.tipo = 'Stock'
                      {filtro}
                    ORDER BY cp.cve_prod, c.longitud DESC, pz.id_pieza;",
                    parameters);

                return Json(new { ok = true, piezas });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes_Barras/PiezasParaEtiquetar");
                return Json(new { ok = false, message = ex.Message });
            }
        }

        /// <summary>
        /// GET /AdminCortes/ResumenTaller
        /// Cifras de cabecera: cuántos tubos y cuántas barras hay, en piezas y
        /// en metros. Va aparte de la escena porque se refresca más a menudo.
        /// </summary>
        [HttpGet]
        public IActionResult ResumenTaller()
        {
            try
            {
                var filas = RunQuery($@"
                    SELECT COALESCE(cp.es_tubo, false) AS es_tubo,
                           COUNT(pz.id_pieza)          AS piezas,
                           COALESCE(SUM(pz.longitud), 0) AS metros,
                           COUNT(DISTINCT cp.id_catproductos) AS productos
                    FROM corte_piezas pz
                    INNER JOIN tarima_productos_cortes c ON c.id_corte = pz.corte_id
                    INNER JOIN tarima_productos tp ON tp.id_tarima_producto = c.tarima_producto_id
                    INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id
                                              AND cp.empresa_id = @empresa_id
                    {JOINS_UBICACION}
                    WHERE pz.estado = 'disponible'
                      AND cs.id_sucursal = @sucursal AND ca.tipo = 'Stock'
                    GROUP BY COALESCE(cp.es_tubo, false);",
                    new Dictionary<string, object>
                    {
                        { "empresa_id", EmpresaActual() },
                        { "sucursal",   SucursalActual() }
                    });

                return Json(new { ok = true, resumen = filas });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "AdminCortes_Barras/ResumenTaller");
                return Json(new { ok = false, message = ex.Message });
            }
        }
    }
}
