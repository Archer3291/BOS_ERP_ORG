using Microsoft.AspNetCore.Authorization;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public partial class DatosGeneralesController : Utilities
    {


        public IActionResult BuscarVID(string nombre, int page = 1, int pageSize = 50)
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
        AND em.nat = 'VICOT'
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
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VICOT'";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }


        public IActionResult BuscarDVIped(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string nat = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) == 2
                ? "VIPED"
                : "RMP";

            string query = $@"
SELECT 
    em.id_encabezado,
    em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
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
INNER JOIN catclientes cc 
    ON cc.id_cliente = em.refe 
   AND cc.empresa_id = @empresa_id
WHERE (
       LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
    OR LOWER(em.gen) LIKE LOWER(@nombre)
    OR LOWER(em.nat) LIKE LOWER(@nombre)
    OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
)
AND em.nat = @nat
AND em.suc = @suc
AND em.estatus_id = 1
ORDER BY em.fch DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nat", nat);
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
WHERE (
       LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
    OR LOWER(em.gen) LIKE LOWER(@nombre)
    OR LOWER(em.nat) LIKE LOWER(@nombre)
    OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
)
AND em.nat = @nat
AND em.suc = @suc
AND em.estatus_id = 1";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object>
{
    { "nombre", $"%{nombre}%" },
    { "nat", nat },
    { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
});
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        // Endpoints de búsqueda de remisiones facturables (modal de facturación especial).
        // Ambos delegan en el mismo helper parametrizado por `nat` (antes eran 2 copias).
        public IActionResult BuscarDVIrem(string nombre = "", int page = 1, int pageSize = 25,
            string cliente = "", string modo = "documento")
            => BuscarRemisionesFacturablesJson("VIREM", nombre, page, pageSize, cliente, modo);

        // Igual pero para remisiones NACIONALES (nat='VNREM'): alimenta el modal de
        // facturación especial (parcial/múltiple) de la factura nacional.
        public IActionResult BuscarDVNrem(string nombre = "", int page = 1, int pageSize = 25,
            string cliente = "", string modo = "documento")
            => BuscarRemisionesFacturablesJson("VNREM", nombre, page, pageSize, cliente, modo);

        // Búsqueda compartida: remisiones con saldo por facturar (estatus 1 ó 41), con
        // conteos de tracking e importe_pendiente para el modal. `modo` se conserva por
        // compatibilidad (los tres modos comparten el mismo filtro de saldo).
        private IActionResult BuscarRemisionesFacturablesJson(
            string nat, string nombre, int page, int pageSize, string cliente, string modo)
        {
            var parameters = new Dictionary<string, object>();

            // ── Filtro de cliente ────────────────────────────────────
            string filtroCliente = !string.IsNullOrWhiteSpace(cliente)
                ? "AND em.cli_prov = @cliente"
                : "";

            // ── Filtro de saldo ──────────────────────────────────────
            //
            // El tracking (remision_partidas_facturadas) se crea de forma
            // perezosa: una remisión recién hecha NO tiene renglones todavía.
            // Por eso "tiene saldo" significa:
            //
            //   · aún no tiene tracking  → nada facturado, saldo = total, o
            //   · tiene al menos una partida que no está en 'completa'
            //
            // Antes se exigía EXISTS(...pendiente|parcial), lo que dejaba
            // fuera precisamente a las remisiones nuevas — el modal salía
            // vacío en modo "parciales".
            const string filtroSaldo = @"
          AND (
              NOT EXISTS (
                  SELECT 1 FROM remision_partidas_facturadas r0
                  WHERE r0.encabezado_remision_id = em.id_encabezado
              )
              OR EXISTS (
                  SELECT 1 FROM remision_partidas_facturadas r1
                  WHERE r1.encabezado_remision_id = em.id_encabezado
                    AND r1.estatus <> 'completa'
              )
          )";

            // 1  = abierta / sin facturar     41 = parcialmente facturada
            // Ambas son facturables en cualquiera de los tres modos.
            const string filtroEstatus = "AND em.estatus_id IN (1, 41)";

            // ── Query principal ──────────────────────────────────────
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
 
            -- Conteos para el modal (útiles en modo parciales y documento)
            COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'pendiente') AS partidas_pendientes,
            COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'parcial')   AS partidas_parciales,
            COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'completa')  AS partidas_completas,
            COUNT(rpf.id)                                           AS total_partidas_tracking,
            (SELECT COUNT(*) FROM partidasdoc pd
              WHERE pd.encabezado_id = em.id_encabezado)            AS total_partidas,

            -- Sin tracking todavía = nada facturado: el pendiente es el
            -- subtotal completo de la remisión, no 0.
            CASE WHEN COUNT(rpf.id) = 0
                 THEN COALESCE(em.sub, em.imp, 0)
                 ELSE COALESCE(
                          SUM(
                              (rpf.cantidad_pendiente * rpf.precio_unitario
                              * (1 - rpf.descuento / 100.0))::numeric(18,2)
                          ) FILTER (WHERE rpf.estatus IN ('pendiente','parcial')),
                          0
                      )
            END AS importe_pendiente

        FROM encabezadomov em
        INNER JOIN catclientes cc
            ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
        LEFT JOIN remision_partidas_facturadas rpf
            ON rpf.encabezado_remision_id = em.id_encabezado
        WHERE em.nat = @nat
          AND em.suc = @suc
          AND (
               LOWER(em.gen || '-' || em.nat || '-' ||
                     EXTRACT(YEAR FROM em.fch)::text || '-' ||
                     em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov)               LIKE LOWER(@nombre)
            OR LOWER(cc.n_cli)                  LIKE LOWER(@nombre)
          )
          {filtroEstatus}
          {filtroCliente}
          {filtroSaldo}
        GROUP BY
            em.id_encabezado, em.folio, em.gen, em.nat, em.fch,
            em.fol_doc, em.variacion, em.imp, em.sub, em.cli_prov, em.usr0, cc.n_cli
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nat", nat);
            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            if (!string.IsNullOrWhiteSpace(cliente))
                parameters.Add("cliente", cliente);

            var items = RunQuery(query, parameters);

            // ── Query total ──────────────────────────────────────────
            string queryTotal = $@"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        INNER JOIN catclientes cc
            ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
        WHERE em.nat = @nat
          AND em.suc = @suc
          AND (
               LOWER(em.gen || '-' || em.nat || '-' ||
                     EXTRACT(YEAR FROM em.fch)::text || '-' ||
                     em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
            OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
          )
          {filtroEstatus}
          {filtroCliente}
          {filtroSaldo}";

            var totalParameters = new Dictionary<string, object>
    {
        { "nat",        nat },
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


    //    public IActionResult BuscarPartidasRemisionCorregido(int id)
    //    {
    //        try
    //        {
    //            var parameters = new Dictionary<string, object> { { "id", id } };

    //            string connStr = ConfigurationManager
    //                .ConnectionStrings["ERP_SRS"].ConnectionString;

    //            using (var conn = new NpgsqlConnection(connStr))
    //            {
    //                conn.Open();
    //                using (var tx = conn.BeginTransaction())
    //                {
    //                    // Inicializar tracking si no existe para esta remisión
    //                    var insParam = new Dictionary<string, object>
    //            {
    //                { "enc",     id },
    //                { "usuario", User.Identity.Name ?? "sistema" }
    //            };
    //                    RunQuery(@"
    //                INSERT INTO remision_partidas_facturadas
    //                    (encabezado_remision_id, id_partida_remision, cve_prod,
    //                     cantidad_original, cantidad_facturada, precio_unitario,
    //                     descuento, usuario_registro)
    //                SELECT
    //                    @enc,
    //                    pd.id_partidas,
    //                    pd.cve_prod,
    //                    pd.cant_ud,
    //                    0,
    //                    pd.pv_prod,
    //                    COALESCE(pd.dto1, 0),
    //                    @usuario
    //                FROM partidasdoc pd
    //                WHERE pd.encabezado_id = @enc
    //                ON CONFLICT (encabezado_remision_id, id_partida_remision)
    //                DO NOTHING",
    //                        insParam, false, conn, tx);

    //                    tx.Commit();
    //                }
    //            }

    //            // Consultar partidas con estado real (solo las pendientes/parciales)
    //            string queryPartidas = @"
    //SELECT
    //    pd.id_partidas                                AS id,
    //    rpf.id                                        AS id_tracking,
    //    pd.id_partidas                                AS id_partida_remision, -- ← ANTES ERA rpf.id
    //    pd.cve_prod,
    //    pd.descr_prod                                 AS descripcion,
    //    pd.ud                                         AS unidad,
    //    rpf.cantidad_original,
    //    rpf.cantidad_facturada,
    //    rpf.cantidad_pendiente,
    //    rpf.precio_unitario,
    //    rpf.descuento,
    //    rpf.estatus,
    //    ROUND(
    //        rpf.cantidad_pendiente
    //        * rpf.precio_unitario
    //        * (1 - rpf.descuento / 100.0), 2
    //    ) AS importe_pendiente_partida
    //FROM partidasdoc pd
    //INNER JOIN remision_partidas_facturadas rpf
    //    ON rpf.encabezado_remision_id = @id
    //   AND rpf.id_partida_remision    = pd.id_partidas  -- ← join por id_partidas real
    //WHERE pd.encabezado_id = @id
    //  AND rpf.estatus IN ('pendiente', 'parcial')
    //ORDER BY pd.nro_part";

    //            var partidas = RunQuery(queryPartidas, parameters);

    //            // Datos del encabezado
    //            string queryEnc = @"
    //        SELECT
    //            em.gen || '-' || em.nat || '-' ||
    //                EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
    //                CASE WHEN em.variacion > 0
    //                     THEN '-' || num_to_letters(em.variacion)
    //                     ELSE '' END AS folio,
    //            em.cli_prov,
    //            em.id_encabezado,
    //            cc.n_cli
    //        FROM encabezadomov em
    //        INNER JOIN catclientes cc
    //            ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    //        WHERE em.id_encabezado = @id";

    //            var encParam = new Dictionary<string, object>
    //    {
    //        { "id",         id },
    //        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    //    };
    //            var encabezado = RunQuery(queryEnc, encParam);

    //            return Json(new
    //            {
    //                success = true,
    //                result = new[]
    //                {
    //            new
    //            {
    //                id_encabezado = id,
    //                folio    = encabezado.FirstOrDefault()?["folio"]?.ToString()    ?? "",
    //                cli_prov = encabezado.FirstOrDefault()?["cli_prov"]?.ToString() ?? "",
    //                n_cli    = encabezado.FirstOrDefault()?["n_cli"]?.ToString()    ?? "",
    //                productos = partidas
    //            }
    //        }
    //            });
    //        }
    //        catch (Exception ex)
    //        {
    //            return Json(new { success = false, message = ex.Message },
    //                JsonRequestBehavior.AllowGet);
    //        }
    //    }
        [HttpGet]
        public IActionResult VerificarOrdenCompra(string oc)
        {
            if (string.IsNullOrWhiteSpace(oc))
                return Json(new { existe = false });

            var parameters = new Dictionary<string, object>
    {
        { "oc", oc.Trim() },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    };

            string query = @"
        SELECT 
            f.id,
            f.serie,
            f.folio,
            f.oc,
            f.fecha,
            f.rfccliente,
            f.rsocliente,
            f.total,
            f.statusfactura,
            f.tipo,
            em.folio AS folio_interno
        FROM factura f
        LEFT JOIN encabezadomov em ON em.id_encabezado = f.encabezado_id
        WHERE LOWER(TRIM(f.oc)) = LOWER(TRIM(@oc))
          AND f.statusfactura NOT IN ('CANCELADA', 'CANCELADO')
        ORDER BY f.fecha DESC
        LIMIT 5";

            var resultados = RunQuery(query, parameters);

            if (resultados == null || resultados.Count == 0)
                return Json(new { existe = false });

            var usos = resultados.Select(r => new
            {
                id = r["id"],
                folio = $"{r["serie"]}-{r["folio"]}",
                folioInterno = r["folio_interno"]?.ToString() ?? "",
                oc = r["oc"]?.ToString() ?? "",
                fecha = r["fecha"] is DateTime dt
                                ? dt.ToString("dd/MM/yyyy")
                                : r["fecha"]?.ToString() ?? "",
                rfc = r["rfccliente"]?.ToString() ?? "",
                cliente = r["rsocliente"]?.ToString() ?? "",
                total = Convert.ToDecimal(r["total"] ?? 0),
                estatus = r["statusfactura"]?.ToString() ?? "",
                tipo = r["tipo"]?.ToString() ?? ""
            }).ToList();

            return Json(new { existe = true, usos });
        }
    }
}