using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Finanzas
{
    [Authorize]
    public class DevolucionEfectivoController : Utilities
    {
        // =====================================================================
        // GET /VIDevolucionEfectivo/Index
        // =====================================================================
        public IActionResult Index()
        {
            return View();
        }

        // =====================================================================
        // GET /VIDevolucionEfectivo/BuscarDocumentos
        //     ?folio=&cliente=&tipo=&page=1&pageSize=50
        //
        // Documentos origen: nat='NT', reembolso=TRUE, con saldo disponible
        // mayor a 0 en cobros_cliente.
        // =====================================================================
        public IActionResult BuscarDocumentos(string folio = "", string cliente = "", string tipo = "", int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>
            {
                { "folio",      $"%{folio}%" },
                { "cliente",    $"%{cliente}%" },
                { "tipo",       tipo ?? "" },
                { "offset",     (page - 1) * pageSize },
                { "pageSize",   pageSize },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            string query = @"
                SELECT
                    em.id_encabezado,
                    em.folio ||
                        CASE WHEN em.variacion > 0
                            THEN '-' || num_to_letters(em.variacion)
                            ELSE ''
                        END AS folio,
                    em.gen,
                    em.nat,
                    em.fch              AS fecha,
                    em.imp              AS total,
                    em.cli_prov,
                    em.mdp              AS forma_pago,
                    cc.n_cli            AS nombre_cliente,
                    cc.rfc,
                    COALESCE(cob.saldo_disponible, 0) AS saldo_disponible,
                    CASE
                        WHEN COALESCE(cob.saldo_disponible, 0) <= 0 THEN 'Sin saldo'
                        ELSE 'Con saldo'
                    END AS estatus
                FROM encabezadomov em
                INNER JOIN catclientes cc
                    ON cc.id_cliente  = em.refe
                    AND cc.empresa_id = @empresa_id
                -- Tomamos el cobro más reciente no cancelado para este encabezado
                LEFT JOIN LATERAL (
                    SELECT saldo_disponible
                    FROM cobros_cliente
                    WHERE encabezado_id = (SELECT id_encabezado FROM encabezadomov WHERE encabezados_padre = em.id_encabezado) 
                      AND cancelado     = FALSE
                    ORDER BY fecha_cobro DESC
                    LIMIT 1
                ) cob ON TRUE
                WHERE em.nat       = 'NT'
                  AND em.reembolso = TRUE
                  AND em.suc       = @suc
                  AND (
                      @folio = '%%'
                      OR LOWER(em.gen || '-' || em.nat || '-' ||
                               EXTRACT(YEAR FROM em.fch)::text || '-' ||
                               em.fol_doc) LIKE LOWER(@folio)
                      OR LOWER(em.folio) LIKE LOWER(@folio)
                  )
                  AND (
                      @cliente = '%%'
                      OR LOWER(cc.n_cli)    LIKE LOWER(@cliente)
                      OR LOWER(em.cli_prov) LIKE LOWER(@cliente)
                  )
                  AND (@tipo = '' OR LOWER(em.gen) = LOWER(@tipo))
                ORDER BY em.fch DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            ";

            var items = RunQuery(query, parameters);

            string queryTotal = @"
                SELECT COUNT(*) AS total
                FROM encabezadomov em
                INNER JOIN catclientes cc
                    ON cc.id_cliente  = em.refe
                    AND cc.empresa_id = @empresa_id
                WHERE em.nat       = 'NT'
                  AND em.reembolso = TRUE
                  AND em.suc       = @suc
                  AND (
                      @folio = '%%'
                      OR LOWER(em.gen || '-' || em.nat || '-' ||
                               EXTRACT(YEAR FROM em.fch)::text || '-' ||
                               em.fol_doc) LIKE LOWER(@folio)
                      OR LOWER(em.folio) LIKE LOWER(@folio)
                  )
                  AND (
                      @cliente = '%%'
                      OR LOWER(cc.n_cli)    LIKE LOWER(@cliente)
                      OR LOWER(em.cli_prov) LIKE LOWER(@cliente)
                  )
                  AND (@tipo = '' OR LOWER(em.gen) = LOWER(@tipo));
            ";

            var totalParams = new Dictionary<string, object>
            {
                { "folio",      $"%{folio}%" },
                { "cliente",    $"%{cliente}%" },
                { "tipo",       tipo ?? "" },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };

            var totalResult = RunQuery(queryTotal, totalParams);
            int total = totalResult != null && totalResult.Count > 0
                ? Convert.ToInt32(totalResult[0]["total"])
                : 0;

            return Json(new { items, total });
        }

        // =====================================================================
        // GET /VIDevolucionEfectivo/HistorialDevoluciones
        //
        // Documentos AF/NT cuyo cobro en cobros_cliente tiene saldo_disponible = 0
        // (devolución ya procesada y saldada).
        // =====================================================================
        public IActionResult HistorialDevoluciones(int page = 1, int pageSize = 8)
        {
            var parameters = new Dictionary<string, object>
            {
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "offset",     (page - 1) * pageSize },
                { "pageSize",   pageSize }
            };

            string query = @"
                SELECT
                    -- Folio del documento de devolución (AF/NT)
                    dev.folio ||
                        CASE WHEN dev.variacion > 0
                             THEN '-' || num_to_letters(dev.variacion)
                             ELSE ''
                        END AS folio_devolucion,

                    -- Folio del documento origen (NT)
                    orig.folio ||
                        CASE WHEN orig.variacion > 0
                             THEN '-' || num_to_letters(orig.variacion)
                             ELSE ''
                        END AS folio_documento,

                    cc.n_cli            AS nombre_cliente,
                    dev.imp             AS monto,
                    dev.fch             AS fecha,
                    dev.coment_aut      AS autorizado_por,
                    'Completada'        AS estatus
                FROM encabezadomov dev
                INNER JOIN encabezadomov orig
                    ON orig.id_encabezado = dev.encabezados_padre
                INNER JOIN catclientes cc
                    ON cc.id_cliente  = dev.refe
                    AND cc.empresa_id = @empresa_id
                -- Solo aparecen en el historial si el cobro vinculado tiene saldo = 0
                INNER JOIN cobros_cliente cob
                    ON cob.encabezado_id     = dev.id_encabezado
                    AND cob.cancelado        = FALSE
                    AND cob.saldo_disponible = 0
                WHERE dev.gen = 'AF'
                  AND dev.nat = 'NT'
                  AND dev.suc = @suc
                ORDER BY dev.fch DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            ";

            var items = RunQuery(query, parameters);
            return Json(new { items });
        }

        // =====================================================================
        // GET /VIDevolucionEfectivo/ResumenKPI
        // =====================================================================
        public IActionResult ResumenKPI()
        {
            var parameters = new Dictionary<string, object>
            {
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
            };

            // Devoluciones del día: documentos AF/NT con cobro saldado hoy
            var hoyResult = RunQuery(@"
                SELECT
                    COUNT(*)                   AS cantidad,
                    COALESCE(SUM(dev.imp), 0)  AS monto_total
                FROM encabezadomov dev
                INNER JOIN cobros_cliente cob
                    ON cob.encabezado_id     = dev.id_encabezado
                    AND cob.cancelado        = FALSE
                    AND cob.saldo_disponible = 0
                WHERE dev.gen        = 'AF'
                  AND dev.nat        = 'NT'
                  AND dev.suc        = @suc
                  AND DATE(dev.fch)  = CURRENT_DATE;
            ", parameters);

            // Documentos origen con saldo aún disponible en cobros_cliente
            var saldoResult = RunQuery(@"
                SELECT
                    COUNT(*)                              AS docs_con_saldo,
                    COALESCE(SUM(cob.saldo_disponible), 0) AS saldo_total
                FROM encabezadomov em
                INNER JOIN catclientes cc
                    ON cc.id_cliente  = em.refe
                    AND cc.empresa_id = @empresa_id
                -- Cobro más reciente no cancelado con saldo > 0
                INNER JOIN LATERAL (
                    SELECT saldo_disponible
                    FROM cobros_cliente
                    WHERE encabezado_id = em.id_encabezado
                      AND cancelado     = FALSE
                      AND saldo_disponible > 0
                    ORDER BY fecha_cobro DESC
                    LIMIT 1
                ) cob ON TRUE
                WHERE em.nat       = 'NT'
                  AND em.reembolso = TRUE
                  AND em.suc       = @suc;
            ", parameters);

            int devolucionesHoy = 0;
            decimal montoHoy = 0;
            int docsConSaldo = 0;
            decimal saldoTotal = 0;

            if (hoyResult != null && hoyResult.Count > 0)
            {
                devolucionesHoy = Convert.ToInt32(hoyResult[0]["cantidad"]);
                montoHoy = Convert.ToDecimal(hoyResult[0]["monto_total"]);
            }
            if (saldoResult != null && saldoResult.Count > 0)
            {
                docsConSaldo = Convert.ToInt32(saldoResult[0]["docs_con_saldo"]);
                saldoTotal = Convert.ToDecimal(saldoResult[0]["saldo_total"]);
            }

            return Json(new { devolucionesHoy, montoHoy, docsConSaldo, saldoTotal });
        }

        // =====================================================================
        // POST /VIDevolucionEfectivo/ProcesarDevolucion
        //
        // Inserta documento AF/NT en encabezadomov y pone saldo_disponible = 0
        // en cobros_cliente del documento origen.
        // =====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ProcesarDevolucion(int documentoId, decimal monto, string motivo, string autorizadoPor)
        {
            try
            {
                if (monto <= 0)
                    return Json(new { success = false, message = "El monto debe ser mayor a cero." });

                if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
                    return Json(new { success = false, message = "El motivo de la devolución es obligatorio." });

                if (string.IsNullOrWhiteSpace(autorizadoPor))
                    return Json(new { success = false, message = "El campo 'Autorizó' es obligatorio." });

                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int sucursal = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                int almacen = Convert.ToInt32(HttpContext.Session.GetString("Almacen"));
                string usuario = HttpContext.Session.GetString("Usuario") ?? "";

                // ------------------------------------------------------------------
                // 1. Verificar documento origen y saldo disponible en cobros_cliente
                // ------------------------------------------------------------------
                var origen = RunQuery(@"
                    SELECT
                        em.id_encabezado,
                        em.refe,
                        em.cli_prov,
                        em.ccy,
                        cob.id_cobro,
                        COALESCE(cob.saldo_disponible, 0) AS saldo_disponible
                    FROM encabezadomov em
                    -- Cobro más reciente no cancelado con saldo > 0
                    INNER JOIN LATERAL (
                        SELECT id_cobro, saldo_disponible
                        FROM cobros_cliente
                        WHERE encabezado_id   = (SELECT id_encabezado FROM encabezadomov WHERE encabezados_padre = em.id_encabezado)
                          AND cancelado       = FALSE
                          AND saldo_disponible > 0
                        ORDER BY fecha_cobro DESC
                        LIMIT 1
                    ) cob ON TRUE
                    WHERE em.id_encabezado = @id
                      AND em.nat           = 'NT'
                      AND em.reembolso     = TRUE;
                ", new Dictionary<string, object> { { "id", documentoId } });

                if (origen == null || origen.Count == 0)
                    return Json(new
                    {
                        success = false,
                        message = "Documento no encontrado, sin saldo disponible o no elegible para reembolso."
                    });

                decimal saldoDisponible = Convert.ToDecimal(origen[0]["saldo_disponible"]);
                if (monto > saldoDisponible + 0.01m)
                    return Json(new
                    {
                        success = false,
                        message = $"El monto ({monto:C}) supera el saldo disponible ({saldoDisponible:C})."
                    });

                int refe = Convert.ToInt32(origen[0]["refe"]);
                string cliProv = origen[0]["cli_prov"]?.ToString() ?? "";
                string ccy = origen[0]["ccy"]?.ToString() ?? "MXN";
                int cobroId = Convert.ToInt32(origen[0]["id_cobro"]);


                RunQuery("SELECT reembolsar_nota_credito(@id_notacredito)", new Dictionary<string, object> { { "id_notacredito", documentoId } });


                return Json(new
                {
                    success = true,
                    folioDevolucion = "xcvxc",
                    message = $"Devolución registrada correctamente."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
