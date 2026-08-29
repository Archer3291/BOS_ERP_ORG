using BOS_ERP.Filters;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class GastosController
    {

        /* ═══════════════════════════════════════════════════════════════════
           VISTA — Consulta de mis solicitudes
        ═══════════════════════════════════════════════════════════════════ */

        [HttpGet]
        public IActionResult Consulta()
        {
            // Asegúrate de que el usuario esté autenticado
            if (HttpContext.Session.GetString("Usuario") == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        /* ═══════════════════════════════════════════════════════════════════
           API — Buscar MIS solicitudes (filtrado por usuario en sesión)
           Solo devuelve solicitudes donde usr_doc = alias del usuario actual
        ═══════════════════════════════════════════════════════════════════ */

        [HttpPost]
        public JsonResult BuscarMisSolicitudes(
            string search = "",
            string folio = "",
            string categoria = "",
            string estatus = "",
            string urgencia = "",
            string fechaDesde = "",
            string fechaHasta = "",
            int page = 1,
            int pageSize = 25)
        {
            try
            {
                // ── Alias del usuario en sesión ───────────────────────────────
                // Ajusta la clave de sesión según tu proyecto.
                // Ejemplos comunes: Session["UsuarioAlias"], Session["UserName"],
                //                   User.Identity.Name
                string usuarioAlias = User.Identity.Name;
                if (string.IsNullOrWhiteSpace(usuarioAlias))
                    return Json(new { success = false, message = "Usuario no autenticado." });

                var p = new Dictionary<string, object>();
                var where = new List<string>
                {
                    "1=1",
                    "de.tp_mov    = 'FSGTO'",
                    "de.usr_doc   = @usuarioAlias",   // <-- filtro clave
                };
                p["usuarioAlias"] = usuarioAlias;

                // ── Filtros opcionales ────────────────────────────────────────
                if (!string.IsNullOrWhiteSpace(folio))
                {
                    where.Add("LOWER(de.folio) LIKE @folio");
                    p["folio"] = "%" + folio.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(categoria))
                {
                    where.Add("LOWER(de.coment3) LIKE @categoria");
                    p["categoria"] = "%" + categoria.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(estatus) && int.TryParse(estatus, out int estatusInt))
                {
                    where.Add("de.estatus_id = @estatus");
                    p["estatus"] = estatusInt;
                }

                if (!string.IsNullOrWhiteSpace(urgencia))
                {
                    where.Add("LOWER(de.coment1) LIKE @urgencia");
                    p["urgencia"] = "%" + urgencia.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    where.Add(@"(
                        LOWER(de.cli_prov)  LIKE @search
                        OR LOWER(de.folio)  LIKE @search
                    )");
                    p["search"] = "%" + search.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(fechaDesde) &&
                    DateTime.TryParse(fechaDesde, out DateTime desde))
                {
                    where.Add("de.fch >= @fechaDesde");
                    p["fechaDesde"] = desde;
                }

                if (!string.IsNullOrWhiteSpace(fechaHasta) &&
                    DateTime.TryParse(fechaHasta, out DateTime hasta))
                {
                    where.Add("de.fch <= @fechaHasta");
                    p["fechaHasta"] = hasta.AddDays(1).AddSeconds(-1);
                }

                string whereClause = string.Join(" AND ", where);

                // ── Count total ───────────────────────────────────────────────
                string queryCount = $@"
                    SELECT COUNT(*)
                    FROM   encabezadomov de
                    WHERE  {whereClause}";

                int total = Convert.ToInt32(RunScalar(queryCount, p) ?? 0);
                int offset = (page - 1) * pageSize;

                // ── Query paginado ────────────────────────────────────────────
                string query = $@"
                    SELECT
                        de.id_encabezado                        AS id,
                        de.folio                                AS folio,
                        de.fch                                  AS fecha,
                        de.cli_prov                             AS proveedor,
                        de.imp                                  AS monto,
                        de.ccy                                  AS moneda,
                        de.coment1                              AS urgencia,
                        de.coment3                              AS cat_sub,
                        de.estatus_id,
                        de.usr_dep                              AS area,
                        ger.nombre || ' ' || ger.apellido       AS aprobador
                    FROM  encabezadomov de
                    LEFT JOIN usuarios ger ON ger.usuarioid = de.usr1
                    WHERE {whereClause}
                    ORDER BY de.fch_cap DESC
                    LIMIT @pageSize OFFSET @offset";

                p["pageSize"] = pageSize;
                p["offset"] = offset;

                var rows = RunQuery(query, p);

                var items = rows.Select(r =>
                {
                    string catSub = r["cat_sub"]?.ToString() ?? "";
                    bool hasSep = catSub.Contains(" / ");
                    string cat = hasSep ? catSub.Split(new[] { " / " }, 2, StringSplitOptions.None)[0] : catSub;
                    string subcat = hasSep ? catSub.Split(new[] { " / " }, 2, StringSplitOptions.None)[1] : "";

                    return new
                    {
                        id = r["id"],
                        folio = r["folio"],
                        fecha = r["fecha"],
                        proveedor = r["proveedor"],
                        monto = r["monto"],
                        moneda = r["moneda"],
                        urgencia = r["urgencia"],
                        estatus = r["estatus_id"],
                        area = r["area"],
                        aprobador = r["aprobador"],
                        categoria = cat,
                        subcategoria = subcat,
                    };
                }).ToList();

                return Json(new
                {
                    success = true,
                    data = items,
                    total = total,
                    page = page,
                    pageSize = pageSize,
                    totalPages = (int)Math.Ceiling((double)total / pageSize),
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /* ═══════════════════════════════════════════════════════════════════
           API — Buscar solicitudes (para TableBuilder)
           Tabla:  encabezadomov
           Campo:  estatus_id  (integer)
           Aprobador asignado: usr1
        ═══════════════════════════════════════════════════════════════════ */

        [HttpPost]
        public JsonResult BuscarSolicitudes(
            string search = "",
            string folio = "",
            string categoria = "",
            string estatus = "",
            string urgencia = "",
            string fechaDesde = "",
            string fechaHasta = "",
            int page = 1,
            int pageSize = 25)
        {
            try
            {
                var p = new Dictionary<string, object>();
                var where = new List<string> { "1=1", "de.tp_mov = 'FSGTO'" };

                // ── Filtrar por aprobador asignado (usr1 = usuario en sesión) ──
                // Solo ve sus solicitudes asignadas. Comenta estas líneas si
                // cualquier aprobador puede ver todas las solicitudes pendientes.
                // if (Session["Usuario"] != null)
                // {
                //     where.Add("de.usr1 = @usr1id");
                //     p["usr1id"] = Convert.ToInt32(Session["Usuario"]);
                // }

                if (!string.IsNullOrWhiteSpace(folio))
                {
                    where.Add("LOWER(de.folio) LIKE @folio");
                    p["folio"] = "%" + folio.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(categoria))
                {
                    where.Add("LOWER(de.coment3) LIKE @categoria");
                    p["categoria"] = "%" + categoria.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(estatus) && int.TryParse(estatus, out int estatusInt))
                {
                    where.Add("de.estatus_id = @estatus");
                    p["estatus"] = estatusInt;
                }

                if (!string.IsNullOrWhiteSpace(urgencia))
                {
                    where.Add("LOWER(de.coment1) LIKE @urgencia");
                    p["urgencia"] = "%" + urgencia.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    where.Add(@"(
                        LOWER(u.nombre || ' ' || u.apellido) LIKE @search
                        OR LOWER(de.cli_prov)                LIKE @search
                        --OR LOWER(de.coment)                  LIKE @search
                        OR LOWER(de.folio)                   LIKE @search
                    )");
                    p["search"] = "%" + search.Trim().ToLower() + "%";
                }

                if (!string.IsNullOrWhiteSpace(fechaDesde) && DateTime.TryParse(fechaDesde, out DateTime desde))
                {
                    where.Add("de.fch >= @fechaDesde");
                    p["fechaDesde"] = desde;
                }

                if (!string.IsNullOrWhiteSpace(fechaHasta) && DateTime.TryParse(fechaHasta, out DateTime hasta))
                {
                    where.Add("de.fch <= @fechaHasta");
                    p["fechaHasta"] = hasta.AddDays(1).AddSeconds(-1);
                }

                string whereClause = string.Join(" AND ", where);

                // ── Query total ───────────────────────────────────────────────
                string queryCount = $@"
                    SELECT COUNT(*)
                    FROM   encabezadomov de
                    LEFT JOIN usuarios u ON u.nombreusuario = de.usr_doc
                    WHERE  {whereClause}";

                int total = Convert.ToInt32(RunScalar(queryCount, p) ?? 0);
                int offset = (page - 1) * pageSize;

                // ── Query paginado ────────────────────────────────────────────
                string query = $@"
                    SELECT
                        de.id_encabezado                                AS id,
                        de.folio                                        AS folio,
                        u.nombre    || ' ' || u.apellido                AS solicitante,
                        u.nombreusuario                                 AS alias_solicitante,
                        dep.nombre                                      AS area,
                        de.fch                                          AS fecha,
                        de.fch_cap                                      AS fecha_captura,
                        de.cli_prov                                     AS proveedor,
                        --de.coment                                       AS concepto,
                        de.coment_aut                                   AS justificacion,
                        de.imp                                          AS monto,
                        de.ccy                                          AS moneda,
                        de.coment1                                      AS urgencia,
                        de.coment2                                      AS notas,
                        de.coment3                                      AS cat_sub,
                        de.estatus_id,
                        ger.nombre  || ' ' || ger.apellido              AS aprobador,
                        de.usr_dep                                      AS departamento
                    FROM  encabezadomov de
                    LEFT JOIN usuarios  u   ON u.nombreusuario  = de.usr_doc
                    LEFT JOIN areas     dep ON dep.areaid        = de.centro_costos
                    LEFT JOIN usuarios  ger ON ger.usuarioid     = de.usr1
                    WHERE {whereClause}
                    ORDER BY de.fch_cap DESC
                    LIMIT @pageSize OFFSET @offset";

                p["pageSize"] = pageSize;
                p["offset"] = offset;

                var rows = RunQuery(query, p);

                var items = rows.Select(r =>
                {
                    string catSub = r["cat_sub"]?.ToString() ?? "";
                    bool hasSep = catSub.Contains(" / ");
                    string cat = hasSep ? catSub.Split(new[] { " / " }, 2, StringSplitOptions.None)[0] : catSub;
                    string subcat = hasSep ? catSub.Split(new[] { " / " }, 2, StringSplitOptions.None)[1] : "";

                    return new
                    {
                        id = r["id"],
                        folio = r["folio"],
                        solicitante = r["solicitante"],
                        area = r["area"],
                        fecha = r["fecha"],
                        proveedor = r["proveedor"],
                        monto = r["monto"],
                        moneda = r["moneda"],
                        urgencia = r["urgencia"],
                        estatus = r["estatus_id"],
                        aprobador = r["aprobador"],
                        categoria = cat,
                        subcategoria = subcat,
                    };
                }).ToList();

                return Json(new
                {
                    success = true,
                    data = items,
                    total = total,
                    page = page,
                    pageSize = pageSize,
                    totalPages = (int)Math.Ceiling((double)total / pageSize),
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /* ═══════════════════════════════════════════════════════════════════
           API — KPIs del resumen
        ═══════════════════════════════════════════════════════════════════ */

        [HttpGet]
        public JsonResult ResumenKPI(string fechaDesde = "", string fechaHasta = "")
        {
            try
            {
                var p = new Dictionary<string, object>();
                var where = new List<string> { "tp_mov = 'FSGTO'" };

                if (HttpContext.Session.GetInt32("Empresa") != null)
                {
                    where.Add("empresaid = @empresaid");
                    p["empresaid"] = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                }

                if (!string.IsNullOrWhiteSpace(fechaDesde) && DateTime.TryParse(fechaDesde, out DateTime desde))
                {
                    where.Add("fch >= @fechaDesde");
                    p["fechaDesde"] = desde;
                }

                if (!string.IsNullOrWhiteSpace(fechaHasta) && DateTime.TryParse(fechaHasta, out DateTime hasta))
                {
                    where.Add("fch <= @fechaHasta");
                    p["fechaHasta"] = hasta.AddDays(1).AddSeconds(-1);
                }

                string wh = string.Join(" AND ", where);
                string query = $@"
                    SELECT
                        COUNT(*)                                                        AS total,
                        COALESCE(SUM(imp), 0)                                           AS monto_total,
                        COUNT(*) FILTER (WHERE estatus_id = 1)                          AS pendientes,
                        COUNT(*) FILTER (WHERE estatus_id = 2)                          AS aprobadas,
                        COUNT(*) FILTER (WHERE estatus_id = 3)                          AS rechazadas,
                        COUNT(*) FILTER (WHERE estatus_id = 1
                                          AND LOWER(coment1) LIKE '%inmediato%')        AS urgentes,
                        COALESCE(SUM(imp) FILTER (WHERE estatus_id = 2), 0)             AS monto_aprobado,
                        ROUND(
                            AVG(
                                EXTRACT(EPOCH FROM (fch_cap - fch)) / 86400.0
                            ) FILTER (WHERE estatus_id IN (2,3)), 1
                        )                                                                AS tiempo_promedio
                    FROM encabezadomov
                    WHERE {wh}";

                var row = RunQuery(query, p).FirstOrDefault();

                if (row == null)
                    return Json(new { total = 0, monto_total = 0 });

                return Json(new
                {
                    total = row["total"],
                    monto_total = row["monto_total"],
                    pendientes = row["pendientes"],
                    aprobadas = row["aprobadas"],
                    rechazadas = row["rechazadas"],
                    urgentes = row["urgentes"],
                    monto_aprobado = row["monto_aprobado"],
                    tiempo_promedio = row["tiempo_promedio"],
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /* ═══════════════════════════════════════════════════════════════════
           API — Detalle de una solicitud
        ═══════════════════════════════════════════════════════════════════ */

        [HttpGet]
        public JsonResult DetalleSolicitud(int id)
        {
            try
            {
                var p = new Dictionary<string, object> { { "id", id } };

                // ── Encabezado ────────────────────────────────────────────────
                string qEnc = @"
                    SELECT
                        de.id_encabezado,
                        de.folio,
                        de.fch                                          AS fecha,
                        de.fch_cap                                      AS fecha_captura,
                        de.imp                                          AS monto,
                        de.ccy                                          AS moneda,
                        de.cli_prov                                     AS proveedor,
                        --de.coment                                       AS concepto,
                        de.coment_aut                                   AS justificacion,
                        de.coment1                                      AS urgencia,
                        de.coment2                                      AS notas,
                        de.coment3                                      AS cat_sub,
                        de.estatus_id,
                        de.usr_dep                                      AS area,
                        u_cre.nombre || ' ' || u_cre.apellido          AS creador,
                        u_apr.nombre || ' ' || u_apr.apellido          AS aprobador,
                        dep.nombre                                      AS centro_costo
                    FROM  encabezadomov de
                    LEFT JOIN usuarios  u_cre ON u_cre.nombreusuario = de.usr_doc
                    LEFT JOIN usuarios  u_apr ON u_apr.usuarioid     = de.usr1
                    LEFT JOIN areas     dep   ON dep.areaid           = de.centro_costos
                    WHERE de.id_encabezado = @id
                    LIMIT 1";

                var enc = RunQuery(qEnc, p).FirstOrDefault();
                if (enc == null)
                    return Json(new { success = false, message = "Solicitud no encontrada." });

                // Separar categoría / subcategoría desde coment3
                string catSub = enc["cat_sub"]?.ToString() ?? "";
                bool hasSep = catSub.Contains(" / ");
                string cat = hasSep ? catSub.Split(new[] { " / " }, 2, StringSplitOptions.None)[0] : catSub;
                string subcat = hasSep ? catSub.Split(new[] { " / " }, 2, StringSplitOptions.None)[1] : "";

                // Verificar si el usuario actual es el aprobador asignado (usr1)
                // para mostrar/ocultar el panel de decisión en el front.
                // Ajusta la clave de sesión a la que uses en tu proyecto.
                bool esAprobadorAsignado = true; // por defecto se muestra; 
                // Si quieres restringir descomenta:
                // if (Session["UsuarioId"] != null)
                //     esAprobadorAsignado = (enc["usr1_id"]?.ToString() == Session["UsuarioId"].ToString());

                var solicitud = new
                {
                    id = enc["id_encabezado"],
                    folio = enc["folio"],
                    fecha = enc["fecha"],
                    fecha_captura = enc["fecha_captura"],
                    monto = enc["monto"],
                    moneda = enc["moneda"],
                    proveedor = enc["proveedor"],
                    justificacion = enc["justificacion"],
                    urgencia = enc["urgencia"],
                    notas = enc["notas"],
                    categoria = cat,
                    subcategoria = subcat,
                    estatus = enc["estatus_id"],
                    area = enc["area"],
                    creador = enc["creador"],
                    aprobador = enc["aprobador"],
                    centro_costo = enc["centro_costo"],
                    puede_aprobar = esAprobadorAsignado,
                };

                // ── Archivos adjuntos ─────────────────────────────────────────
                string qArch = @"
                    SELECT nombre_original, path, uuid, extencion
                    FROM   archivos_solicitud
                    WHERE  encabezado_id = @id
                    ORDER  BY encabezado_id ASC";

                var archivos = RunQuery(qArch, p).Select(a => new
                {
                    nombre_original = a["nombre_original"],
                    path = a["path"],
                    uuid = a["uuid"],
                    extencion = a["extencion"],
                }).ToList();

                // ── Campos dinámicos ──────────────────────────────────────────────
                string qCampos = @"
                    SELECT campo_label, valor_texto, campo_id
                    FROM   solicitud_gastos_campos
                    WHERE  encabezado_id = @id
                    ORDER  BY id";

                var camposDinamicos = RunQuery(qCampos, p).Select(c => new
                {
                    label = c["campo_label"],
                    valor = c["valor_texto"],
                    id = c["campo_id"],
                }).ToList();


                // ── Historial ─────────────────────────────────────────────────
                string qHist = @"
                    SELECT
                        u.nombre || ' ' || u.apellido   AS usuario,
                        gh.accion,
                        gh.comentario,
                        gh.fecha
                    FROM  gasto_historial gh
                    LEFT JOIN usuarios u ON u.nombreusuario = gh.usuario_alias
                    WHERE gh.encabezado_id = @id
                    ORDER BY gh.fecha ASC";

                List<object> historial;
                try
                {
                    historial = RunQuery(qHist, p).Select(h => (object)new
                    {
                        usuario = h["usuario"],
                        accion = h["accion"],
                        comentario = h["comentario"],
                        fecha = h["fecha"],
                    }).ToList();
                }
                catch { historial = new List<object>(); }

                return Json(new
                {
                    success = true,
                    solicitud = solicitud,
                    archivos = archivos,
                    historial = historial,
                    camposDinamicos = camposDinamicos,
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /* ═══════════════════════════════════════════════════════════════════
           API — Procesar decisión (aprobar / rechazar / comentar)
           Estatus_id:  1 = Pendiente | 2 = Aprobado | 3 = Rechazado
        ═══════════════════════════════════════════════════════════════════ */

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Gastos", Accion = "Aprobación / Rechazo de solicitud de gasto")]
        public JsonResult ProcesarDecision(int id, string accion, string comentario = "")
        {
            try
            {
                if (id <= 0)
                    return Json(new { success = false, message = "ID de solicitud inválido." });

                accion = (accion ?? "").Trim().ToLower();

                if (accion != "aprobar" && accion != "rechazar" && accion != "comentar")
                    return Json(new { success = false, message = "Acción no reconocida." });

                if (accion == "rechazar" && string.IsNullOrWhiteSpace(comentario))
                    return Json(new { success = false, message = "El motivo de rechazo es obligatorio." });

                // ── Verificar existencia y estatus actual ─────────────────────
                var pChk = new Dictionary<string, object> { { "id", id } };
                string qChk = @"
                    SELECT estatus_id, folio, imp
                    FROM   encabezadomov
                    WHERE  id_encabezado = @id
                    LIMIT  1";

                var rowChk = RunQuery(qChk, pChk).FirstOrDefault();

                if (rowChk == null)
                    return Json(new { success = false, message = "Solicitud no encontrada." });

                int estatusActual = Convert.ToInt32(rowChk["estatus_id"]);
                string folio = rowChk["folio"]?.ToString() ?? id.ToString();

                if (accion != "comentar" && estatusActual != 1)
                    return Json(new { success = false, message = "Esta solicitud ya fue procesada y no puede modificarse." });

                // ── Datos del encabezado padre ────────────────────────────────────────
                var usrParameter = new Dictionary<string, object> { { "id", id } };
                string usrquery = @"
    SELECT em.usr0, em.fch0, em.usr1, em.firma3, em.fch1, em.usr2, em.fch2, em.usr3,
           em.fch3, em.usr4, em.fch4, em.usr5, em.firma5, em.fch5,
           em.tipo_proceso, em.tipo_producto, em.usr_dep, em.en_presupuesto,
           em.nat, em.es_servicio, em.centro_costos,
           em.cli_prov, em.refe
    FROM   encabezadomov em
    WHERE  em.id_encabezado = @id";

                var usrId = RunQuery(usrquery, usrParameter)[0];

                string proveedor = GetString(usrId["cli_prov"]);
                int refe = Convert.ToInt32(usrId["refe"]);

                // ── Armar encabezado del documento GTO ───────────────────────────────
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 2,
                    IdTpDoc = 14,
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "GTO",
                    UsrDep = GetString(usrId["usr_dep"]),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(usrId["usr0"]),
                    Fch0 = (DateTime)usrId["fch0"],
                    Usr1 = Convert.ToInt32(GetUserId(User.Identity.Name)),
                    Fch1 = DateTime.Now,
                    Imp = Convert.ToDecimal(rowChk["imp"]),
                    TipoPoceso = "orden_gasto",
                    CliProv = proveedor,
                    Ref = refe,
                    EncabezadoPadre = id,
                    EnPresupuesto = Convert.ToBoolean(usrId["en_presupuesto"]),
                    EsServicio = Convert.ToBoolean(usrId["es_servicio"]),
                    CentroCostos = GetInt(usrId["centro_costos"]),
                    Estatus = 1
                };

                // ── Obtener partidas de la solicitud padre ────────────────────────────
                string queryPartidas = @"
    SELECT cve_prod, descr_prod, cant_ud, pv_prod, iva, ud, f_pago_id, dto1
    FROM   partidasdoc
    WHERE  encabezado_id = @id";

                var productos = RunQuery(queryPartidas, usrParameter);   // reutiliza usrParameter {id}

                // ── Insertar en catproductos los que no existan ───────────────────────
                bool esActivo = usrId["tipo_proceso"].ToString() == "gasto";

                foreach (var prod in productos)
                {
                    var prodParam = new Dictionary<string, object>
    {
        { "cve_prod",   GetString(prod["cve_prod"])              },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"))      },
    };

                    int existe = Convert.ToInt32(RunScalar(
                        "SELECT COUNT(*) FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id",
                        prodParam));

                    if (existe == 0)
                    {
                        prodParam.Add("descr_prod", GetString(prod["descr_prod"]));
                        prodParam.Add("es_activo", esActivo);
                        prodParam.Add("udm", GetString(prod["ud"], "PZA"));

                        RunQuery(@"
            INSERT INTO catproductos (cve_prod, descr_prod, udm, empresa_id, es_activo)
            VALUES (@cve_prod, @descr_prod, @udm, @empresa_id, @es_activo)",
                            prodParam);
                    }
                }

                // ── Armar partidas del documento GTO ─────────────────────────────────
                var partidas = new List<PartidaDocumento>();
                int nro = 1;

                foreach (var prod in productos)
                {
                    decimal? pvProd = GetDecimal(prod["pv_prod"], 0);
                    decimal? cantUd = GetDecimal(prod["cant_ud"], 0);

                    partidas.Add(new PartidaDocumento
                    {
                        NroPart = nro++,
                        CveProd = GetString(prod["cve_prod"]),
                        DescrProd = GetString(prod["descr_prod"]),
                        CantUd = cantUd,
                        PvProd = pvProd,
                        ImpPart = pvProd * cantUd,
                        FPagoId = GetInt(prod["f_pago_id"]),
                        Iva = GetDecimal(prod["iva"]),
                        CveVdrCpr = proveedor,
                        Ref = refe,
                        Ud = GetString(prod["ud"], "PZA"),
                        Dto1 = GetDecimal(prod["dto1"], 0),
                    });
                }

                // ── Generar documento ─────────────────────────────────────────────────
                var documento = GenerarDocumentoConPartidas(encabezado, partidas);


                var poliza = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), null, null);
                var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), poliza, false, null);
                RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name));

                // ── Actualizar estatus_id ─────────────────────────────────────
                if (accion == "aprobar" || accion == "rechazar")
                {
                    int nuevoEstatus = accion == "aprobar" ? 2 : 3;

                    var pUpd = new Dictionary<string, object>
                    {
                        { "id",      id           },
                        { "estatus", nuevoEstatus },
                        { "usr",     User.Identity.Name },
                        { "fch",     DateTime.Now },
                    };

                    RunUpdate(@"
                        UPDATE encabezadomov
                        SET    estatus_id = @estatus,
                               fch1     = @fch,
                               fch2     = @fch
                        WHERE  id_encabezado = @id", pUpd);
                }

                try
                {
                    var pHist = new Dictionary<string, object>
                    {
                        { "encabezado_id", id                  },
                        { "usuario_alias", User.Identity.Name  },
                        { "accion",        accion == "aprobar"  ? "Aprobado"
                                         : accion == "rechazar" ? "Rechazado"
                                         :                        "Comentario" },
                        { "comentario",    comentario ?? ""    },
                        { "fecha",         DateTime.Now        },
                    };

                    RunUpdate(@"
                        INSERT INTO gasto_historial
                            (encabezado_id, usuario_alias, accion, comentario, fecha)
                        VALUES
                            (@encabezado_id, @usuario_alias, @accion, @comentario, @fecha)",
                        pHist);
                }
                catch { /* No bloquea el flujo si la tabla aún no existe */ }

                // ── Auditoría ─────────────────────────────────────────────────
                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = id,
                    Folio = folio,
                    Observaciones = $"Acción: {accion} — {comentario}",
                };

                // ── Notificación al solicitante ───────────────────────────────
                try
                {
                    var pNot = new Dictionary<string, object> { { "id", id } };
                    string qNot = @"
                        SELECT u.nombreusuario AS alias,
                               u.email,
                               u.nombre || ' ' || u.apellido AS nombre
                        FROM   encabezadomov de
                        JOIN   usuarios u ON u.nombreusuario = de.usr_doc
                        WHERE  de.id_encabezado = @id
                        LIMIT  1";

                    var solicitante = RunQuery(qNot, pNot).FirstOrDefault();
                    if (solicitante != null)
                    {
                        string msgAccion = accion == "aprobar"
                            ? $"Tu solicitud de gasto <strong>{folio}</strong> fue <strong style='color:#059669'>aprobada</strong>."
                            : accion == "rechazar"
                            ? $"Tu solicitud de gasto <strong>{folio}</strong> fue <strong style='color:#dc2626'>rechazada</strong>. Motivo: {comentario}"
                            : $"Nuevo comentario en tu solicitud <strong>{folio}</strong>: {comentario}";

                        string urlBase = $"{Request.Scheme}://{Request.Host}";
                        string urlDet = $"{urlBase}/Gastos/Consulta";

                        _ = SendNotificationInterno(
                            solicitante["alias"].ToString(),
                            solicitante["email"].ToString(),
                            new
                            {
                                icon = accion == "aprobar" ? "success" : accion == "rechazar" ? "error" : "info",
                                title = $"Solicitud {folio} — {(accion == "aprobar" ? "Aprobada" : accion == "rechazar" ? "Rechazada" : "Comentada")}",
                                message = msgAccion,
                                buttons = new[]
                                {
                                    new { text   = "Ver mis solicitudes",
                                          style  = "primary",
                                          action = $"window.open('{urlDet}', '_blank')" },
                                    new { text   = "Cerrar",
                                          style  = "secondary",
                                          action = (string)null },
                                },
                                timer = 0,
                                folio = folio,
                            }
                        );
                    }
                }
                catch { /* Notificación no bloquea el flujo */ }

                string labelAccion = accion == "aprobar" ? "aprobada"
                                   : accion == "rechazar" ? "rechazada"
                                   : "comentada";

                return Json(new
                {
                    success = true,
                    message = $"Solicitud {folio} {labelAccion} correctamente.",
                    folio = folio,
                    estatus = accion == "aprobar" ? 2 : accion == "rechazar" ? 3 : estatusActual,
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar: " + ex.Message });
            }
        }

        /* ═══════════════════════════════════════════════════════════════════
           API — Descargar archivo adjunto
        ═══════════════════════════════════════════════════════════════════ */

        [HttpGet]
        public IActionResult DescargarArchivo(string uuid, string ext)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(uuid))
                    return NotFound();

                var p = new Dictionary<string, object> { { "uuid", uuid } };
                string q = @"
                    SELECT path, nombre_original, extencion
                    FROM   archivos_solicitud
                    WHERE  uuid = @uuid
                    LIMIT  1";

                var row = RunQuery(q, p).FirstOrDefault();
                if (row == null) return NotFound();

                string ruta = row["path"]?.ToString() ?? "content/archivos_solicitud/";
                string nombre = row["nombre_original"]?.ToString() ?? uuid;
                string exten = row["extencion"]?.ToString() ?? ext ?? ".pdf";

                string fullPath = Path.Combine("~/" + ruta.TrimEnd('/') + "/" + uuid + exten);
                if (!System.IO.File.Exists(fullPath))
                    return NotFound();

                string mimeExt = exten.TrimStart('.').ToLower();
                string mime;

                switch (mimeExt)
                {
                    case "pdf":
                        mime = "application/pdf";
                        break;

                    case "png":
                        mime = "image/png";
                        break;

                    case "jpg":
                    case "jpeg":
                        mime = "image/jpeg";
                        break;

                    default:
                        mime = "application/octet-stream";
                        break;
                }

                return File(fullPath, mime, nombre);
            }
            catch (Exception ex)
            {
                return Content("Error: " + ex.Message);
            }
        }
    }
}
