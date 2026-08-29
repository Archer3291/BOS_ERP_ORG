using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Models;

namespace BOS_ERP.Controllers.Almacen
{
    public class ReglasPrecioController : Utilities
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Traducción de criterios: el modal manda el ID del catálogo, pero
        //  catproductos guarda las CLAVES (cve_linea / cve_grupo / cve_tipo) y es
        //  contra esas columnas que obtener_precio_final compara la regla. Antes
        //  solo se traducía la línea, así que las reglas por grupo o por tipo
        //  comparaban un id contra una clave y nunca hacían match.
        //
        //  Los nombres de tabla y columna son constantes del código (no entran por
        //  el request), así que la interpolación no abre inyección.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly (string Tabla, string Clave, string Id)[] Catalogos =
        {
            ("catlineas",   "cve_linea", "id_linea"),
            ("catgrupo",    "cve_grupo", "id_grupo_producto"),
            ("cattipo_prd", "cve_tipo",  "id_tipo_producto")
        };

        private const int Linea = 0, Grupo = 1, Tipo = 2;

        private string ClaveCatalogo(int catalogo, string valorId)
        {
            if (string.IsNullOrWhiteSpace(valorId)) return null;
            if (!int.TryParse(valorId, out int id) || id <= 0) return null;

            var c = Catalogos[catalogo];
            object clave = RunScalar(
                $"SELECT {c.Clave} FROM {c.Tabla} WHERE {c.Id} = @id",
                new Dictionary<string, object> { { "id", id } });

            return clave?.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Cuántos productos alcanza una combinación de criterios.
        //
        //  Replica exactamente el filtro de obtener_precio_final, incluido el no
        //  filtrar por es_activo: si aquí saliera un número distinto al que ve el
        //  motor de precios, el dato engañaría más de lo que ayuda.
        //
        //  Es la señal que faltaba: una regla que alcanza 0 productos no hace nada
        //  y hasta ahora eso solo se descubría consultando la base a mano.
        // ─────────────────────────────────────────────────────────────────────
        private int ContarProductosAfectados(int? productoId, string claveLinea, string claveGrupo, string claveTipo)
        {
            object total = RunScalar(@"
                SELECT COUNT(*)
                FROM   catproductos p
                WHERE  p.empresa_id = @empresa_id
                  AND  (@producto_id = 0 OR p.id_catproductos = @producto_id)
                  AND  (@lin_prod = ''  OR p.lin_prod = @lin_prod)
                  AND  (@gpo      = ''  OR p.gpo      = @gpo)
                  AND  (@tp       = ''  OR p.tp       = @tp)",
                new Dictionary<string, object>
                {
                    { "empresa_id",  HttpContext.Session.GetInt32("Empresa") },
                    { "producto_id", productoId ?? 0 },
                    { "lin_prod",    claveLinea ?? "" },
                    { "gpo",         claveGrupo ?? "" },
                    { "tp",          claveTipo  ?? "" }
                });

            return total == null ? 0 : Convert.ToInt32(total);
        }

        /// <summary>
        /// Previsualización para el modal: a cuántos productos alcanzarían los criterios
        /// que el usuario está capturando, antes de guardar.
        /// </summary>
        [HttpGet]
        public IActionResult ProductosAfectados(string cve_prod, string lin_prod, string gpo, string tp)
        {
            bool sinCriterios = string.IsNullOrWhiteSpace(cve_prod) && string.IsNullOrWhiteSpace(lin_prod)
                             && string.IsNullOrWhiteSpace(gpo) && string.IsNullOrWhiteSpace(tp);

            if (sinCriterios)
                return Json(new { total = 0, sin_criterios = true });

            int? productoId = int.TryParse(cve_prod, out int pid) && pid > 0 ? pid : (int?)null;

            return Json(new
            {
                total = ContarProductosAfectados(
                            productoId,
                            ClaveCatalogo(Linea, lin_prod),
                            ClaveCatalogo(Grupo, gpo),
                            ClaveCatalogo(Tipo, tp)),
                sin_criterios = false
            });
        }

        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryLineas = "SELECT id_linea           AS id, descripcion AS nombre FROM catlineas    ORDER BY descripcion DESC";
            string queryTipos = "SELECT id_tipo_producto   AS id, descripcion AS nombre FROM cattipo_prd  ORDER BY descripcion DESC";
            string queryGrupos = "SELECT id_grupo_producto  AS id, descripcion AS nombre FROM catgrupo     ORDER BY descripcion DESC";

            result.Add("lineas", RunQuery(queryLineas));
            result.Add("tipos", RunQuery(queryTipos));
            result.Add("grupos", RunQuery(queryGrupos));

            return Json(result);
        }

        [HttpGet]
        public IActionResult Listar()
        {
            // Los criterios se guardan como clave; el modal de edición necesita el id del
            // catálogo para repoblar sus selects, y la tabla la descripción para mostrarla.
            // El join del producto compara como texto: rp.cve_prod es varchar y un valor no
            // numérico hacía fallar el ::integer y con él toda la consulta.
            string query = @"
                SELECT rp.*,
                       c.n_cli  AS nombre_cliente,
                       cp.cve_prod AS clave,
                       cl.id_linea          AS lin_prod_id,
                       cl.descripcion       AS lin_prod_nombre,
                       cg.id_grupo_producto AS gpo_id,
                       cg.descripcion       AS gpo_nombre,
                       ct.id_tipo_producto  AS tp_id,
                       ct.descripcion       AS tp_nombre,
                       -- Productos que alcanza la regla, con el mismo filtro que usa
                       -- obtener_precio_final. En 0 la regla no hace absolutamente nada.
                       (SELECT COUNT(*)
                        FROM   catproductos p
                        WHERE  p.empresa_id = rp.empresa_id
                          AND  (rp.producto_id IS NULL OR p.id_catproductos = rp.producto_id)
                          AND  (rp.lin_prod    IS NULL OR p.lin_prod = rp.lin_prod)
                          AND  (rp.gpo         IS NULL OR p.gpo      = rp.gpo)
                          AND  (rp.tp          IS NULL OR p.tp       = rp.tp)
                       ) AS productos_afectados
                FROM   reglas_precio rp
                LEFT JOIN catclientes c
                       ON c.id_cliente = rp.cliente_id
                      AND c.empresa_id = rp.empresa_id
                LEFT JOIN catproductos cp
                       ON cp.id_catproductos::text = rp.cve_prod
                      AND cp.empresa_id = rp.empresa_id
                LEFT JOIN catlineas   cl ON cl.cve_linea = rp.lin_prod
                LEFT JOIN catgrupo    cg ON cg.cve_grupo = rp.gpo
                LEFT JOIN cattipo_prd ct ON ct.cve_tipo  = rp.tp
                WHERE  rp.empresa_id = @empresa_id
                ORDER  BY rp.prioridad ASC, rp.id DESC";

            var lista = RunQuery(query, new Dictionary<string, object>
            {
                { "empresa_id", HttpContext.Session.GetInt32("Empresa") }
            });

            return Json(lista);
        }

        [HttpGet]
        public IActionResult BuscarClientes(string q = "", int page = 1, int pageSize = 30)
        {
            bool esNumero = int.TryParse(q, out int idBuscado);
            string query = @"
                SELECT id_cliente AS id,
                       n_cli      AS nombre,
                       cve_cli    AS clave
                FROM   catclientes
                WHERE  empresa_id       = @empresa_id
                  AND  estatus_cliente  = 'activo'
                  AND  (
                         n_cli    ILIKE @q
                      OR cve_cli  ILIKE @q
                      OR (@esNumero AND id_cliente = @id)
                  )
                ORDER  BY (id_cliente = @id) DESC, n_cli ASC
                LIMIT  @pageSize OFFSET @offset";

            var lista = RunQuery(query, new Dictionary<string, object>
            {
                { "empresa_id", HttpContext.Session.GetInt32("Empresa") },
                { "q",         $"%{q}%" },
                { "pageSize",  pageSize },
                { "offset",    (page - 1) * pageSize },
                { "esNumero",  esNumero },
                { "id",        esNumero ? idBuscado : 0 }
            });

            string countQuery = @"
                SELECT COUNT(*) AS total
                FROM   catclientes
                WHERE  empresa_id      = @empresa_id
                  AND  estatus_cliente = 'activo'
                  AND  (n_cli ILIKE @q OR cve_cli ILIKE @q)";

            var countResult = RunQuery(countQuery, new Dictionary<string, object>
            {
                { "empresa_id", HttpContext.Session.GetInt32("Empresa") },
                { "q",         $"%{q}%" }
            });

            int total = countResult.Count > 0 ? Convert.ToInt32(countResult[0]["total"]) : 0;

            return Json(new
            {
                items = lista,
                has_more = (page * pageSize) < total,
                total = total
            });
        }

        [HttpGet]
        public IActionResult BuscarProductos(string q = "", int page = 1, int pageSize = 30)
        {
            bool esNumero = int.TryParse(q, out int idBuscado);
            string query = @"
                SELECT id_catproductos AS id,
                       cve_prod        AS clave,
                       descr_prod      AS nombre,
                       lin_prod, tp, gpo
                FROM   catproductos
                WHERE  empresa_id = @empresa_id
                  AND  (
                         cve_prod   ILIKE @q
                      OR descr_prod ILIKE @q
                      OR (@esNumero AND id_catproductos = @id)
                  )
                ORDER  BY cve_prod ASC
                LIMIT  @pageSize OFFSET @offset";

            var lista = RunQuery(query, new Dictionary<string, object>
            {
                { "empresa_id", HttpContext.Session.GetInt32("Empresa") },
                { "q",         $"%{q}%" },
                { "pageSize",  pageSize },
                { "offset",    (page - 1) * pageSize },
                { "esNumero",  esNumero },
                { "id",        esNumero ? idBuscado : 0 }
            });

            string countQuery = @"
                SELECT COUNT(*) AS total
                FROM   catproductos
                WHERE  empresa_id = @empresa_id
                  AND  es_activo  = true
                  AND  (cve_prod ILIKE @q OR descr_prod ILIKE @q)";

            var countResult = RunQuery(countQuery, new Dictionary<string, object>
            {
                { "empresa_id", HttpContext.Session.GetInt32("Empresa") },
                { "q",         $"%{q}%" }
            });

            int total = countResult.Count > 0 ? Convert.ToInt32(countResult[0]["total"]) : 0;

            return Json(new
            {
                items = lista,
                has_more = (page * pageSize) < total,
                total = total
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        //  BUSCAR CONFLICTOS  (mejorado)
        //
        //  Lógica:
        //    Una regla existente CONFLICTA con la que se está creando/editando
        //    cuando ambas podrían aplicarse al mismo producto + cliente.
        //
        //    Dos reglas "colisionan" si:
        //      • Tienen el mismo tipo_regla (DESCUENTO vs PRECIO_FIJO)
        //      • La regla existente cubre al cliente buscado
        //          (cliente_id IS NULL  →  aplica a todos)
        //          (cliente_id = X      →  aplica solo al cliente X)
        //      • La regla existente cubre al producto buscado
        //          Se cruza con la nueva si algún criterio de producto coincide
        //          O si la existente no tiene ningún filtro de producto (global).
        //
        //    nivel_conflicto:
        //      'EXACTO'     → misma especificidad Y misma prioridad → ambigüedad real
        //      'SOLAPADO'   → distintas especificidades/prioridades → la más
        //                     específica gana pero conviene saber
        // ─────────────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult BuscarConflictos(
            int? cliente_id,
            string cve_prod,
            string lin_prod,
            string gpo,
            string tp,
            string tipo_regla = "",
            int prioridad = 0,
            int excluir_id = 0)
        {
            // Sin criterio de producto → nada que buscar
            if (string.IsNullOrEmpty(cve_prod) && string.IsNullOrEmpty(lin_prod)
             && string.IsNullOrEmpty(gpo) && string.IsNullOrEmpty(tp))
                return Json(new { conflictos = new object[0] });

            // Especificidad de la regla que se está editando
            int especActual = 0;
            if (cliente_id.HasValue) especActual += 4;
            if (!string.IsNullOrEmpty(cve_prod)) especActual += 3;
            if (!string.IsNullOrEmpty(lin_prod)) especActual += 2;
            if (!string.IsNullOrEmpty(gpo)) especActual += 2;
            if (!string.IsNullOrEmpty(tp)) especActual += 2;

            // ── Query principal ───────────────────────────────────────────────
            // Una regla existente entra en conflicto si:
            //   1. Es del mismo tipo_regla (o no se filtra por tipo)
            //   2. Su filtro de cliente "cubre" al cliente que buscamos:
            //         cliente_id IS NULL  (aplica a todos)  ← siempre cubre
            //         cliente_id = :cliente_id              ← cubre si coincide
            //   3. Al menos UNO de sus criterios de producto hace match con
            //      alguno de los criterios de la regla nueva
            //      (o la existente no tiene ningún criterio → global).
            //
            // IMPORTANTE: usamos IS NOT DISTINCT FROM para manejar NULLs
            // correctamente sin recurrir a IS NULL OR = valor.
            string query = @"
                SELECT
                    rp.id,
                    rp.tipo_regla,
                    rp.valor,
                    rp.prioridad,
                    rp.activo,
                    rp.es_promocion,
                    rp.nombre_promocion,
                    c.n_cli AS nombre_cliente,
                    rp.cliente_id,
                    rp.cve_prod,
                    rp.lin_prod,
                    rp.gpo,
                    rp.tp,
                    rp.producto_id,

                    -- Especificidad de la regla existente
                    (CASE WHEN rp.cliente_id  IS NOT NULL THEN 4 ELSE 0 END
                   + CASE WHEN rp.producto_id IS NOT NULL THEN 3 ELSE 0 END
                   + CASE WHEN rp.lin_prod    IS NOT NULL THEN 2 ELSE 0 END
                   + CASE WHEN rp.gpo         IS NOT NULL THEN 2 ELSE 0 END
                   + CASE WHEN rp.tp          IS NOT NULL THEN 2 ELSE 0 END
                    ) AS especificidad

                FROM reglas_precio rp
                LEFT JOIN catclientes c
                       ON c.id_cliente = rp.cliente_id
                      AND c.empresa_id = rp.empresa_id

                WHERE rp.empresa_id = @empresa_id
                  AND rp.id        <> @excluir_id

                  -- Filtro de tipo de regla (solo si se pasa)
                  AND (@tipo_regla = '' OR rp.tipo_regla = @tipo_regla)

                  -- El cliente de la regla existente debe cubrir al cliente buscado:
                  --   NULL → aplica a todos (siempre cubre)
                  --   X   → solo cubre si X = cliente buscado
                  AND (
                        rp.cliente_id IS NULL
                     OR rp.cliente_id = @cliente_id
                  )

                  -- Al menos un criterio de producto hace solapamiento:
                  --   Si la existente no tiene NINGÚN criterio → es global → solapa siempre
                  --   Si tiene alguno → solapan si alguno coincide con los criterios nuevos
                  AND (
                        -- Regla existente es global (sin ningún criterio de producto)
                        (    rp.producto_id IS NULL
                         AND rp.lin_prod    IS NULL
                         AND rp.gpo         IS NULL
                         AND rp.tp          IS NULL
                        )
                     OR -- Coincide por producto exacto
                        (rp.cve_prod IS NOT NULL AND rp.cve_prod = @cve_prod)
                     OR -- Coincide por línea
                        (rp.lin_prod IS NOT NULL AND rp.lin_prod = @lin_prod AND @lin_prod <> '')
                     OR -- Coincide por grupo
                        (rp.gpo IS NOT NULL AND rp.gpo = @gpo AND @gpo <> '')
                     OR -- Coincide por tipo de producto
                        (rp.tp IS NOT NULL AND rp.tp = @tp AND @tp <> '')
                  )

                ORDER BY especificidad DESC, rp.prioridad ASC
                LIMIT 10";

            // Los criterios llegan como id de catálogo pero se almacenan como clave: sin
            // traducir, el solapamiento por línea/grupo/tipo nunca se detectaba.
            var lista = RunQuery(query, new Dictionary<string, object>
            {
                { "empresa_id",  HttpContext.Session.GetInt32("Empresa") },
                { "excluir_id",  excluir_id },
                { "tipo_regla",  tipo_regla ?? "" },
                { "cliente_id",  (object)cliente_id ?? DBNull.Value },
                { "cve_prod",    string.IsNullOrEmpty(cve_prod) ? (object)DBNull.Value : cve_prod },
                { "lin_prod",    ClaveCatalogo(Linea, lin_prod) ?? "" },
                { "gpo",         ClaveCatalogo(Grupo, gpo)      ?? "" },
                { "tp",          ClaveCatalogo(Tipo,  tp)       ?? "" },
            });

            // Enriquecer cada conflicto con nivel y razón desde el backend
            foreach (var row in lista)
            {
                int especExist = Convert.ToInt32(row["especificidad"]);
                int prioExist = Convert.ToInt32(row["prioridad"]);

                string nivel;
                string razon;

                if (especExist == especActual && prioExist == prioridad && prioridad > 0)
                {
                    nivel = "EXACTO";
                    razon = "Misma especificidad (" + especActual + " pts) y misma prioridad (" + prioridad + "). Habrá ambigüedad — la más reciente ganará.";
                }
                else if (especExist == especActual)
                {
                    nivel = "MISMA_ESPEC";
                    razon = "Misma especificidad (" + especActual + " pts), prioridad diferente (" + prioExist + " vs " + prioridad + "). La de mayor prioridad (número menor) ganará.";
                }
                else if (especExist > especActual)
                {
                    nivel = "MAS_ESPECIFICA";
                    razon = "La regla existente es MÁS específica (" + especExist + " pts vs " + especActual + " pts), tendrá precedencia sobre la nueva.";
                }
                else
                {
                    nivel = "MENOS_ESPECIFICA";
                    razon = "La regla existente es MENOS específica (" + especExist + " pts vs " + especActual + " pts), la nueva tendrá precedencia.";
                }

                row["nivel_conflicto"] = nivel;
                row["razon_conflicto"] = razon;
                row["espec_actual"] = especActual;
            }

            return Json(new
            {
                conflictos = lista,
                hay_exacto = lista.Exists(r => r.ContainsKey("nivel_conflicto") && r["nivel_conflicto"].ToString() == "EXACTO"),
                espec_actual = especActual,
                prioridad_req = prioridad
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        //  VALIDAR CONFLICTOS (endpoint llamado antes de guardar — devuelve
        //  si la operación puede proceder o debe bloquearse)
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost]
        public IActionResult ValidarConflictos(ReglaPrecio model)
        {
            if (string.IsNullOrEmpty(model.cve_prod) && string.IsNullOrEmpty(model.lin_prod)
             && string.IsNullOrEmpty(model.gpo) && string.IsNullOrEmpty(model.tp))
                return Json(new { bloqueado = false, conflictos = new object[0] });

            int especActual = 0;
            if (model.cliente_id.HasValue) especActual += 4;
            if (!string.IsNullOrEmpty(model.cve_prod)) especActual += 3;
            if (!string.IsNullOrEmpty(model.lin_prod)) especActual += 2;
            if (!string.IsNullOrEmpty(model.gpo)) especActual += 2;
            if (!string.IsNullOrEmpty(model.tp)) especActual += 2;

            string query = @"
                SELECT
                    rp.id,
                    rp.tipo_regla,
                    rp.valor,
                    rp.prioridad,
                    rp.activo,
                    c.n_cli AS nombre_cliente,
                    (CASE WHEN rp.cliente_id  IS NOT NULL THEN 4 ELSE 0 END
                   + CASE WHEN rp.producto_id IS NOT NULL THEN 3 ELSE 0 END
                   + CASE WHEN rp.lin_prod    IS NOT NULL THEN 2 ELSE 0 END
                   + CASE WHEN rp.gpo         IS NOT NULL THEN 2 ELSE 0 END
                   + CASE WHEN rp.tp          IS NOT NULL THEN 2 ELSE 0 END
                    ) AS especificidad
                FROM reglas_precio rp
                LEFT JOIN catclientes c
                       ON c.id_cliente = rp.cliente_id
                      AND c.empresa_id = rp.empresa_id
                WHERE rp.empresa_id = @empresa_id
                  AND rp.id        <> @excluir_id
                  AND rp.tipo_regla = @tipo_regla
                  AND (rp.cliente_id IS NULL OR rp.cliente_id = @cliente_id)
                  AND (
                        (rp.producto_id IS NULL AND rp.lin_prod IS NULL AND rp.gpo IS NULL AND rp.tp IS NULL)
                     OR (rp.cve_prod IS NOT NULL AND rp.cve_prod = @cve_prod)
                     OR (rp.lin_prod IS NOT NULL AND rp.lin_prod = @lin_prod AND @lin_prod <> '')
                     OR (rp.gpo IS NOT NULL AND rp.gpo = @gpo AND @gpo <> '')
                     OR (rp.tp  IS NOT NULL AND rp.tp  = @tp  AND @tp  <> '')
                  )
                HAVING
                    (CASE WHEN rp.cliente_id  IS NOT NULL THEN 4 ELSE 0 END
                   + CASE WHEN rp.producto_id IS NOT NULL THEN 3 ELSE 0 END
                   + CASE WHEN rp.lin_prod    IS NOT NULL THEN 2 ELSE 0 END
                   + CASE WHEN rp.gpo         IS NOT NULL THEN 2 ELSE 0 END
                   + CASE WHEN rp.tp          IS NOT NULL THEN 2 ELSE 0 END
                    ) = @espec_actual
                  AND rp.prioridad = @prioridad
                LIMIT 5";

            // Nota: HAVING sobre columnas calculadas — algunos motores necesitan subquery.
            // Si tu versión de PostgreSQL no soporta HAVING sin GROUP BY aquí,
            // usa la versión con subquery de abajo.
            string querySafe = @"
                SELECT * FROM (
                    SELECT
                        rp.id,
                        rp.tipo_regla,
                        rp.valor,
                        rp.prioridad,
                        rp.activo,
                        c.n_cli AS nombre_cliente,
                        (CASE WHEN rp.cliente_id  IS NOT NULL THEN 4 ELSE 0 END
                       + CASE WHEN rp.producto_id IS NOT NULL THEN 3 ELSE 0 END
                       + CASE WHEN rp.lin_prod    IS NOT NULL THEN 2 ELSE 0 END
                       + CASE WHEN rp.gpo         IS NOT NULL THEN 2 ELSE 0 END
                       + CASE WHEN rp.tp          IS NOT NULL THEN 2 ELSE 0 END
                        ) AS especificidad
                    FROM reglas_precio rp
                    LEFT JOIN catclientes c
                           ON c.id_cliente = rp.cliente_id
                          AND c.empresa_id = rp.empresa_id
                    WHERE rp.empresa_id = @empresa_id
                      AND rp.id        <> @excluir_id
                      AND rp.tipo_regla = @tipo_regla
                      -- Una regla inactiva no compite por nada: incluirla aquí y no en el
                      -- chequeo de Guardar hacía que la vista bloqueara guardados que el
                      -- servidor sí aceptaba.
                      AND rp.activo = true
                      AND (rp.cliente_id IS NULL OR rp.cliente_id = @cliente_id)
                      AND (
                            (rp.producto_id IS NULL AND rp.lin_prod IS NULL AND rp.gpo IS NULL AND rp.tp IS NULL)
                         OR (rp.cve_prod IS NOT NULL AND rp.cve_prod = @cve_prod)
                         OR (rp.lin_prod IS NOT NULL AND rp.lin_prod = @lin_prod AND @lin_prod <> '')
                         OR (rp.gpo IS NOT NULL AND rp.gpo = @gpo AND @gpo <> '')
                         OR (rp.tp  IS NOT NULL AND rp.tp  = @tp  AND @tp  <> '')
                      )
                ) sub
                WHERE sub.especificidad = @espec_actual
                  AND sub.prioridad     = @prioridad
                LIMIT 5";

            var exactos = RunQuery(querySafe, new Dictionary<string, object>
            {
                { "empresa_id",  HttpContext.Session.GetInt32("Empresa") },
                { "excluir_id",  model.id },
                { "tipo_regla",  model.tipo_regla },
                { "cliente_id",  (object)model.cliente_id ?? DBNull.Value },
                { "cve_prod",    string.IsNullOrEmpty(model.cve_prod) ? (object)DBNull.Value : model.cve_prod },
                { "lin_prod",    ClaveCatalogo(Linea, model.lin_prod) ?? "" },
                { "gpo",         ClaveCatalogo(Grupo, model.gpo)      ?? "" },
                { "tp",          ClaveCatalogo(Tipo,  model.tp)       ?? "" },
                { "espec_actual",especActual },
                { "prioridad",   model.prioridad },
            });

            return Json(new
            {
                bloqueado = exactos.Count > 0,
                conflictos = exactos,
                mensaje = exactos.Count > 0
                    ? "Existe " + exactos.Count + " regla(s) con idéntica especificidad (" + especActual + " pts) y prioridad (" + model.prioridad + ") del mismo tipo. Esto crearía ambigüedad. Cambia la prioridad o ajusta los criterios."
                    : null
            });
        }

        /// <param name="forzar">
        /// Permite guardar aunque los criterios no alcancen ningún producto. Lo manda la
        /// vista cuando el usuario confirma el aviso.
        /// </param>
        [HttpPost]
        public IActionResult Guardar(ReglaPrecio model, bool forzar = false)
        {
            try
            {
                // ── Validaciones de negocio ───────────────────────────────────
                // El front ya valida todo esto, pero el endpoint es alcanzable directo y
                // una regla mal formada no falla: se guarda y calcula precios mal en
                // silencio. Por eso el servidor repite el juego completo.
                if (string.IsNullOrWhiteSpace(model.cve_prod) && string.IsNullOrWhiteSpace(model.lin_prod)
                 && string.IsNullOrWhiteSpace(model.gpo) && string.IsNullOrWhiteSpace(model.tp))
                    return Json(new { success = false, message = "Debes seleccionar al menos un criterio: producto, línea, grupo o tipo." });

                if (model.tipo_regla != "DESCUENTO" && model.tipo_regla != "PRECIO_FIJO")
                    return Json(new { success = false, message = "El tipo de regla debe ser DESCUENTO o PRECIO_FIJO." });

                // Un 0 aquí es una regla que no hace nada (0% de descuento) o que regala el
                // producto (precio fijo de $0).
                if (model.valor <= 0)
                    return Json(new
                    {
                        success = false,
                        message = model.tipo_regla == "DESCUENTO"
                            ? "El descuento debe ser mayor a 0%."
                            : "El precio fijo debe ser mayor a $0."
                    });

                if (model.tipo_regla == "DESCUENTO" && model.valor > 100)
                    return Json(new { success = false, message = "El descuento no puede ser mayor a 100%." });

                if (model.max_descuento.HasValue
                 && (model.max_descuento < 0 || model.max_descuento > 100))
                    return Json(new { success = false, message = "El descuento máximo debe estar entre 0% y 100%." });

                if (model.prioridad < 1 || model.prioridad > 999)
                    return Json(new { success = false, message = "La prioridad debe estar entre 1 y 999." });

                if (model.es_promocion && string.IsNullOrWhiteSpace(model.nombre_promocion))
                    return Json(new { success = false, message = "Una promoción necesita nombre." });

                if (model.fecha_inicio.HasValue && model.fecha_fin.HasValue
                 && model.fecha_inicio >= model.fecha_fin)
                    return Json(new { success = false, message = "La fecha de fin debe ser posterior a la de inicio." });

                // ── Validación anti-conflicto exacto en el backend ────────────
                // Doble seguridad: aunque el frontend lo bloquee, el backend
                // también rechaza si hay choque exacto de tipo+especificidad+prioridad.
                int especActual = 0;
                if (model.cliente_id.HasValue) especActual += 4;
                if (!string.IsNullOrEmpty(model.cve_prod)) especActual += 3;
                if (!string.IsNullOrEmpty(model.lin_prod)) especActual += 2;
                if (!string.IsNullOrEmpty(model.gpo)) especActual += 2;
                if (!string.IsNullOrEmpty(model.tp)) especActual += 2;

                // Los criterios se guardan como CLAVE, que es lo que compara la función
                // de precios contra catproductos.
                string claveLinea = ClaveCatalogo(Linea, model.lin_prod);
                string claveGrupo = ClaveCatalogo(Grupo, model.gpo);
                string claveTipo = ClaveCatalogo(Tipo, model.tp);

                // Una regla que no alcanza ningún producto es casi siempre un error de
                // captura, y hasta ahora se guardaba sin decir nada: quedaba en el catálogo
                // aparentando funcionar. Se avisa, pero se puede forzar (por ejemplo si el
                // producto de esa línea todavía no está dado de alta).
                if (!forzar)
                {
                    int alcance = ContarProductosAfectados(
                        int.TryParse(model.cve_prod, out int pidChk) && pidChk > 0 ? pidChk : (int?)null,
                        claveLinea, claveGrupo, claveTipo);

                    if (alcance == 0)
                        return Json(new
                        {
                            success = false,
                            code = "SIN_PRODUCTOS",
                            message = "Con estos criterios la regla no alcanza ningún producto, así que " +
                                      "no tendría ningún efecto sobre los precios. Revisa la línea, el grupo " +
                                      "o el tipo seleccionados."
                        });
                }

                string conflictQuery = @"
                    SELECT COUNT(*) AS total FROM (
                        SELECT
                            rp.id,
                            (CASE WHEN rp.cliente_id  IS NOT NULL THEN 4 ELSE 0 END
                           + CASE WHEN rp.producto_id IS NOT NULL THEN 3 ELSE 0 END
                           + CASE WHEN rp.lin_prod    IS NOT NULL THEN 2 ELSE 0 END
                           + CASE WHEN rp.gpo         IS NOT NULL THEN 2 ELSE 0 END
                           + CASE WHEN rp.tp          IS NOT NULL THEN 2 ELSE 0 END
                            ) AS espec
                        FROM reglas_precio rp
                        WHERE rp.empresa_id  = @empresa_id
                          AND rp.id         <> @excluir_id
                          AND rp.tipo_regla  = @tipo_regla
                          AND rp.prioridad   = @prioridad
                          AND rp.activo      = true
                          AND (rp.cliente_id IS NULL OR rp.cliente_id = @cliente_id)
                          AND (
                                (rp.producto_id IS NULL AND rp.lin_prod IS NULL AND rp.gpo IS NULL AND rp.tp IS NULL)
                             OR (rp.cve_prod IS NOT NULL AND rp.cve_prod = @cve_prod)
                             OR (rp.lin_prod IS NOT NULL AND rp.lin_prod = @lin_prod AND @lin_prod <> '')
                             OR (rp.gpo IS NOT NULL AND rp.gpo = @gpo AND @gpo <> '')
                             OR (rp.tp  IS NOT NULL AND rp.tp  = @tp  AND @tp  <> '')
                          )
                    ) sub WHERE sub.espec = @espec_actual";



                var conflictResult = RunQuery(conflictQuery, new Dictionary<string, object>
                {
                    { "empresa_id",  HttpContext.Session.GetInt32("Empresa") },
                    { "excluir_id",  model.id },
                    { "tipo_regla",  model.tipo_regla },
                    { "prioridad",   model.prioridad },
                    { "cliente_id",  (object)model.cliente_id ?? DBNull.Value },
                    { "cve_prod",    string.IsNullOrEmpty(model.cve_prod) ? (object)DBNull.Value : model.cve_prod },
                    { "lin_prod",    claveLinea ?? "" },
                    { "gpo",         claveGrupo ?? "" },
                    { "tp",          claveTipo  ?? "" },
                    { "espec_actual",especActual },
                });

                int totalConflictos = conflictResult.Count > 0
                    ? Convert.ToInt32(conflictResult[0]["total"])
                    : 0;

                if (totalConflictos > 0)
                    return Json(new
                    {
                        success = false,
                        code = "CONFLICT_EXACT",
                        message = $"Existe {totalConflictos} regla(s) activa(s) del mismo tipo con idéntica especificidad ({especActual} pts) y prioridad ({model.prioridad}). " +
                                  "Esto crearía ambigüedad en el cálculo de precios. Cambia la prioridad o ajusta los criterios."
                    });

                // ── Persistir ─────────────────────────────────────────────────
                string queryDml = model.id == 0 ? QueryInsertar() : QueryActualizar();

                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id",        HttpContext.Session.GetInt32("Empresa") },
                    { "cliente_id",        (object)model.cliente_id     ?? DBNull.Value },
                    { "cve_prod",          (object)model.cve_prod        ?? DBNull.Value },
                    { "lin_prod",          (object)claveLinea            ?? DBNull.Value },
                    { "gpo",               (object)claveGrupo            ?? DBNull.Value },
                    { "tp",                (object)claveTipo             ?? DBNull.Value },
                    { "tipo_regla",        model.tipo_regla },
                    { "valor",             model.valor },
                    { "max_descuento",     (object)model.max_descuento   ?? DBNull.Value },
                    { "prioridad",         model.prioridad },
                    { "fecha_inicio",      (object)model.fecha_inicio    ?? DBNull.Value },
                    { "fecha_fin",         (object)model.fecha_fin       ?? DBNull.Value },
                    { "nombre_promocion",  (object)model.nombre_promocion ?? DBNull.Value },
                    { "es_promocion",      model.es_promocion },
                    { "activo",            model.activo },
                    { "aplicar_automatico",model.aplicar_automatico },
                    { "producto_id",       int.TryParse(model.cve_prod, out int prodId)
                                               ? (object)prodId : DBNull.Value },
                };

                if (model.id != 0) parameters.Add("id", model.id);

                RunQuery(queryDml, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult Eliminar(int id)
        {
            RunQuery(
                "DELETE FROM reglas_precio WHERE id = @id AND empresa_id = @empresa_id",
                new Dictionary<string, object>
                {
                    { "id",         id },
                    { "empresa_id", HttpContext.Session.GetInt32("Empresa") }
                });
            return Json(new { success = true });
        }

        // ── Queries DML ──────────────────────────────────────────────────────
        private string QueryInsertar() => @"
            INSERT INTO reglas_precio
            (empresa_id, cliente_id, cve_prod, lin_prod, gpo, tp,
             tipo_regla, valor, max_descuento, prioridad,
             fecha_inicio, fecha_fin, nombre_promocion, es_promocion,
             activo, aplicar_automatico, producto_id)
            VALUES
            (@empresa_id, @cliente_id, @cve_prod, @lin_prod, @gpo, @tp,
             @tipo_regla, @valor, @max_descuento, @prioridad,
             @fecha_inicio, @fecha_fin, @nombre_promocion, @es_promocion,
             @activo, @aplicar_automatico, @producto_id)";

        private string QueryActualizar() => @"
            UPDATE reglas_precio SET
                cliente_id         = @cliente_id,
                cve_prod           = @cve_prod,
                lin_prod           = @lin_prod,
                gpo                = @gpo,
                tp                 = @tp,
                tipo_regla         = @tipo_regla,
                valor              = @valor,
                max_descuento      = @max_descuento,
                prioridad          = @prioridad,
                fecha_inicio       = @fecha_inicio,
                fecha_fin          = @fecha_fin,
                nombre_promocion   = @nombre_promocion,
                es_promocion       = @es_promocion,
                activo             = @activo,
                aplicar_automatico = @aplicar_automatico,
                producto_id        = @producto_id
            WHERE id = @id AND empresa_id = @empresa_id";
    }
}