using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Npgsql;
using System.Text;
using BOS_ERP.Services.Refacturacion;
using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Models;
using Rotativa.AspNetCore;
using Rotativa.AspNetCore.Options;
using BOS_ERP.services.Facturacion;

namespace BOS_ERP.Controllers.Ventas.Refacturacion
{

    public class DatosGeneralesRefacturacionController : Utilities
    {
        private readonly RefacturacionOrchestrator _orchestrator;
        private readonly IWebHostEnvironment _env;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IComprobanteFiscalService _comprobanteFiscal;

        public DatosGeneralesRefacturacionController(
            RefacturacionOrchestrator orchestrator,
            IConfiguration configuration,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IComprobanteFiscalService comprobanteFiscal)
        {
            _orchestrator = orchestrator;
            _configuration = configuration;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _comprobanteFiscal = comprobanteFiscal;
        }
        // ============================================================
        // HELPERS DE PERMISOS  (copiados de FacturaConsultaController)
        // ============================================================
        private string GetFiltroTipoFactura()
        {
            var tiposPermitidos = new List<string>();

            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_especial_internacional"))
                tiposPermitidos.Add("'VIS'");
            if (Utilities.DoesUserHasRight(User.Identity.Name, "factura_arrendamiento"))
                tiposPermitidos.Add("'FAR'");
            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
                tiposPermitidos.Add("'VI', 'VS'");
            if (Utilities.DoesUserHasRight(User.Identity.Name, "facturacion_global"))
                tiposPermitidos.Add("'G'");
            //if (Utilities.DoesUserHasRight(User.Identity.Name, "complemento_pago"))
            //    tiposPermitidos.Add("'CC'");

            return tiposPermitidos.Any()
                ? $"AND fa.serie IN ({string.Join(",", tiposPermitidos)})"
                : "AND 1=0";
        }

        private string GetFiltroSucursal()
        {
            return "AND em.suc = @suc";
        }

        // ============================================================
        // OBTENER FACTURAS PARA REFACTURACIÓN
        // Basado en ObtenerFacturas de FacturaConsultaController,
        // con filtros adicionales para el contexto de refacturación.
        // ============================================================
        [HttpGet]
        public IActionResult ObtenerFacturas(
            string nombre = "",
            string uuid = "",
            string rfc = "",
            string fechaDesde = "",
            string fechaHasta = "",
            string tipoCFDI = "",
            string estadoSAT = "",
            int page = 1,
            int pageSize = 50)
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            var parameters = new Dictionary<string, object>
            {
                { "nombre",     nombre    ?? "" },
                { "uuid",       uuid      ?? "" },
                { "rfc",        rfc       ?? "" },
                { "fechaDesde", string.IsNullOrWhiteSpace(fechaDesde) ? (object)DBNull.Value : fechaDesde },
                { "fechaHasta", string.IsNullOrWhiteSpace(fechaHasta) ? (object)DBNull.Value : fechaHasta },
                { "offset",     (page - 1) * pageSize },
                { "pageSize",   pageSize },
                { "suc",        suc }
            };

            string filtroTipo = GetFiltroTipoFactura();
            string filtroSuc = GetFiltroSucursal();

            // Filtro de búsqueda libre (igual que FacturaConsultaController)
            string filtroNombre =
                "(LOWER(fa.folio) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(fa.rfccliente) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(fa.rsocliente) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) " +
                "    LIKE LOWER('%' || @nombre || '%'))";

            // Filtros adicionales del wizard
            string filtroUUID = string.IsNullOrWhiteSpace(uuid)
                ? "" : "AND fa.uuid::text ILIKE '%' || @uuid || '%'";

            string filtroRFC = string.IsNullOrWhiteSpace(rfc)
                ? "" : "AND UPPER(fa.rfccliente) LIKE '%' || UPPER(@rfc) || '%'";

            string filtroFechaDesde = string.IsNullOrWhiteSpace(fechaDesde)
                ? "" : "AND fa.fecha >= @fechaDesde::date";

            string filtroFechaHasta = string.IsNullOrWhiteSpace(fechaHasta)
                ? "" : "AND fa.fecha <= (@fechaHasta::date + INTERVAL '1 day')";

            // Mapeo combo "Tipo CFDI" → folios reales en BD
            string filtroTipoCFDI = "";
            if (!string.IsNullOrWhiteSpace(tipoCFDI))
            {
                var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Ingreso",             "'VI','VS'" },
                    { "Egreso",              "'NC','ND'" },
                    { "Traslado",            "'CT'"      },
                    { "Anticipo",            "'ANT'"     },
                    { "Complemento de Pago", "'CC'"      },
                    { "Global",              "'G'"       },
                };
                if (mapa.TryGetValue(tipoCFDI, out var series))
                    filtroTipoCFDI = $"AND fa.serie IN ({series})";
            }

            // Por defecto solo vigentes (Timbradas) a menos que el usuario filtre
            string filtroEstado;
            if (!string.IsNullOrWhiteSpace(estadoSAT))
            {
                switch (estadoSAT.ToLowerInvariant())
                {
                    case "vigente":
                        filtroEstado = "AND fa.statusfactura = 'Timbrada'"; break;
                    case "cancelado":
                        filtroEstado = "AND fa.statusfactura ILIKE '%cancela%'"; break;
                    case "pendiente cancelación":
                        filtroEstado = "AND fa.statusfactura ILIKE '%pendiente%'"; break;
                    default:
                        filtroEstado = "AND fa.statusfactura = 'Timbrada'"; break;
                }
            }
            else
            {
                filtroEstado = "AND fa.statusfactura = 'TIMBRADA'";
            }

            string where =
                $" WHERE {filtroNombre} " +
                "  AND em.variacion = 0 AND fa.tipo != 'ANTICIPO' " +
                $" {filtroSuc} " +
                $" {filtroTipo} " +
                $" {filtroUUID} " +
                $" {filtroRFC} " +
                $" {filtroFechaDesde} " +
                $" {filtroFechaHasta} " +
                $" {filtroTipoCFDI} " +
                $" {filtroEstado} ";

            string having = " HAVING 1=1 ";

            // Query principal — misma estructura que ObtenerFacturas original
            // más campos extra para las validaciones del wizard
            string query =
                "SELECT " +
                "CASE WHEN fc.fecha_carga_portal IS NULL THEN NULL " +
                "     ELSE EXTRACT(DAY FROM (fc.fecha_carga_portal + (fc.dias_credito || ' days')::INTERVAL - NOW())) " +
                "END AS dias_credito_restantes, " +
                "fc.fecha_carga_portal, fc.dias_credito, " +
                "em.refe, fa.id, serie, fa.folio AS fac_folio, idtipofactura, idcliente, " +
                "rfccliente, rsocliente, emlcliente, idemisor, rfcemisor, rsoemisor, " +
                "idexpedicion, idusuario, fecha, fechatimbrado, statusfactura, " +
                "mdpfactura, textfactura, idlugarexp, idtipopago, fa.uuid, " +
                "importe, descuento, subtotal, fa.iva, total, " +
                "cc.saldo_pendiente AS saldo, idpedido, " +
                "retisr, retiva, fa.moneda, fa.observaciones, idvendedor, " +
                "usocfdi, idusocfdi, cbb, parcialidad, sellosat, usr_doc, " +
                "sellocfdi, cadenaoriginal, oc, tdc, anticipo, " +
                "reg_fisr, reg_fise, cpr, cpe, tipo, fa.encabezado_id, " +
                "em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                // Flags para validaciones en Step 3
                // Complementos de pago: se identifican por la tabla dedicada factura_complementos_pago
                // (id_encabezado_factura → esta factura), NO por encabezados_padre/tp_mov='CPFAC'.
                "EXISTS (SELECT 1 FROM factura_complementos_pago fcp " +
                "        INNER JOIN factura fa_cp ON fa_cp.encabezado_id = fcp.id_encabezado_complemento " +
                "        WHERE fcp.id_encabezado_factura = fa.encabezado_id " +
                "          AND fa_cp.statusfactura NOT IN ('Cancelada','Error al Cancelar')) AS tiene_complemento, " +
                "COALESCE((SELECT COUNT(DISTINCT fcp.id_encabezado_complemento) FROM factura_complementos_pago fcp " +
                "           INNER JOIN factura fa_cp ON fa_cp.encabezado_id = fcp.id_encabezado_complemento " +
                "           WHERE fcp.id_encabezado_factura = fa.encabezado_id " +
                "             AND fa_cp.statusfactura NOT IN ('Cancelada','Error al Cancelar')), 0) AS complementos_count, " +
                "EXISTS (SELECT 1 FROM factura_anticipos fan " +
                "        WHERE fan.id_factura_principal = fa.id) AS tiene_anticipo, " +
                "COALESCE((SELECT SUM(fan.monto_aplicado) FROM factura_anticipos fan " +
                "          WHERE fan.id_factura_principal = fa.id), 0) AS saldo_anticipo_aplicado " +
                "FROM factura fa " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id " +
                "LEFT  JOIN factura_credito  fc ON fc.factura_id    = fa.id " +
                "LEFT  JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id " +
                $" {where} " +
                "GROUP BY em.folio, em.refe, fa.id, fa.serie, fa.folio, fa.idtipofactura, " +
                "fa.idcliente, fa.rfccliente, fa.rsocliente, fa.emlcliente, fa.idemisor, " +
                "fa.rfcemisor, fa.rsoemisor, fa.idexpedicion, fa.idusuario, fa.fecha, " +
                "fa.fechatimbrado, fa.statusfactura, fa.mdpfactura, fa.textfactura, " +
                "fa.idlugarexp, fa.idtipopago, fa.uuid, fa.importe, fa.descuento, " +
                "fa.subtotal, fa.iva, fa.total, cc.saldo_pendiente, fa.idpedido, " +
                "fa.retisr, fa.retiva, fa.moneda, fa.observaciones, fa.idvendedor, " +
                "fa.usocfdi, fa.idusocfdi, fa.cbb, fa.parcialidad, fa.sellosat, " +
                "em.usr_doc, fa.sellocfdi, fa.cadenaoriginal, fa.oc, fa.tdc, " +
                "fa.anticipo, fa.reg_fisr, fa.reg_fise, fa.cpr, fa.cpe, fa.tipo, " +
                "fa.encabezado_id, em.gen, em.nat, em.fch, em.fol_doc, em.variacion, " +
                "fc.fecha_carga_portal, fc.dias_credito " +
                $" {having} " +
                "ORDER BY fa.id DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            // Conteo total para paginación
            string queryCount =
                "SELECT COUNT(*) FROM (" +
                "  SELECT DISTINCT fa.id, fa.folio FROM factura fa " +
                "  INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id " +
                $" {where} " +
                "  GROUP BY fa.id, fa.folio " +
                $" {having} " +
                ") sub";

            var total = RunScalar(queryCount, parameters);

            return Json(new { data, total });
        }

        // ============================================================
        // DETALLE DE FACTURA  (copiado de FacturaConsultaController)
        // ============================================================
        [HttpGet]
        public IActionResult FacturaDetail(int id)
        {
            string filtroTipo = GetFiltroTipoFactura();
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            var parameters = new Dictionary<string, object>
            {
                { "id",  id  },
                { "suc", suc }
            };

            string query = $@"
                SELECT jsonb_build_object(
                    'encabezado', jsonb_build_object(
                        'id_encabezado', em.id_encabezado,
                        'suc',           em.suc,
                        'gen',           em.gen,
                        'nat',           em.nat,
                        'fol_doc',       em.fol_doc,
                        'cli_prov',      em.cli_prov,
                        'fecha_encabezado', em.fch,
                        'coment1',       em.coment1,
                        'coment2',       em.coment2,
                        'tp_mov',        em.tp_mov,
                        'cfdi',          em.cfdi,
                        'f_pago',        em.f_pago,
                        'incoterm',      em.incoterm,
                        'mdp',           em.mdp,
                        'estatus_id',    em.estatus_id
                    ),
                    'facturas', jsonb_agg(jsonb_build_object(
                        'factura_id',    fa.id,
                        'serie',         fa.serie,
                        'folio',         fa.folio,
                        'fecha',         fa.fecha,
                        'fecha_vencimiento', fc.fecha_vencimiento,
                        'fechatimbrado', fa.fechatimbrado,
                        'rfccliente',    fa.rfccliente,
                        'rsocliente',    fa.rsocliente,
                        'rfcemisor',     fa.rfcemisor,
                        'rsoemisor',     fa.rsoemisor,
                        'total',         fa.total,
                        'subtotal',      fa.subtotal,
                        'iva',           fa.iva,
                        'descuento',     fa.descuento,
                        'uuid',          fa.uuid,
                        'moneda',        fa.moneda,
                        'tdc',           fa.tdc,
                        'statusfactura', fa.statusfactura,
                        'mdpfactura',    fa.mdpfactura,
                        'textfactura',   fa.textfactura,
                        'idtipofactura', fa.idtipofactura,
                        'idcliente',     fa.idcliente,
                        'idvendedor',    fa.idvendedor,
                        'oc',            fa.oc,
                        'encabezado_id', fa.encabezado_id,
                        'usocfdi',       fa.usocfdi,
                        'reg_fisr',      fa.reg_fisr,
                        'productos', (
                            SELECT jsonb_agg(jsonb_build_object(
                                'idproducto',          df.idproducto,
                                'descripcion',         df.descripcion,
                                'cantidad',            df.cantidad,
                                'precio',              df.precio,
                                'descuento_partida',   df.descuento,
                                'saldo_partida',       df.saldo,
                                'udm',                 df.udm,
                                'claveprodserv',       df.claveprodserv,
                                'claveprod',           df.claveprod,
                                'fraccionarancelaria', df.fraccionarancelaria,
                                'clave_cliente',       df.clave_cliente
                            ))
                            FROM dfactura df WHERE df.idfac = fa.id
                        )
                    ))
                ) AS documento
                FROM encabezadomov em
                INNER JOIN factura fa ON fa.encabezado_id = em.id_encabezado
                LEFT  JOIN factura_credito fc ON fc.factura_id = fa.id
                WHERE fa.encabezado_id = @id
                  AND em.suc = @suc
                  {filtroTipo}
                GROUP BY
                    em.id_encabezado, em.suc, em.gen, em.nat, em.fol_doc, em.cli_prov,
                    em.fch, em.coment1, em.coment2, em.tp_mov, em.cfdi,
                    em.f_pago, em.incoterm, em.mdp, em.estatus_id;";

            // Complementos de pago vinculados (tabla dedicada factura_complementos_pago).
            string queryComplementos = @"
                SELECT DISTINCT
                    fa_cp.id             AS complemento_id,
                    fa_cp.serie,
                    fa_cp.folio,
                    fa_cp.fecha          AS fecha_pago,
                    fa_cp.total          AS monto_pagado,
                    fa_cp.uuid::text     AS uuid,
                    fa_cp.statusfactura  AS estatus,
                    fa_cp.mdpfactura     AS metodo_pago,
                    em_cp.mdp            AS forma_pago
                FROM factura_complementos_pago fcp
                INNER JOIN factura       fa_cp ON fa_cp.encabezado_id = fcp.id_encabezado_complemento
                INNER JOIN encabezadomov em_cp ON em_cp.id_encabezado = fa_cp.encabezado_id
                WHERE fcp.id_encabezado_factura = @id
                ORDER BY fa_cp.fecha ASC;";

            // Anticipos aplicados
            string queryAnticipos = @"
                SELECT
                    fan.id                        AS aplicacion_id,
                    fan.monto_aplicado,
                    fan.fecha_aplicacion,
                    fan.saldo_antes,
                    fan.saldo_despues,
                    fan.observaciones,
                    fa_ant.serie                  AS anticipo_serie,
                    fa_ant.folio                  AS anticipo_folio,
                    fa_ant.uuid::text             AS anticipo_uuid,
                    fa_ant.fecha                  AS anticipo_fecha,
                    fa_ant.total                  AS anticipo_monto_original,
                    fa_ant.saldo                  AS anticipo_saldo_actual
                FROM factura_anticipos fan
                INNER JOIN factura fa_ant ON fa_ant.id = fan.id_factura_anticipo
                WHERE fan.id_factura_principal = (
                    SELECT encabezado_id FROM factura WHERE encabezado_id = @id LIMIT 1
                )
                ORDER BY fan.fecha_aplicacion ASC;";

            // Info de crédito y saldo
            string queryCredito = @"
                SELECT
                    fc.fecha_carga_portal,
                    fc.dias_credito,
                    fc.fecha_vencimiento,
                    fc.estatus_carga,
                    fc.observaciones,
                    EXTRACT(DAY FROM (fc.fecha_vencimiento - NOW()))  AS dias_restantes,
                    COALESCE((
                        SELECT SUM(fcp.monto_aplicado)
                        FROM factura_complementos_pago fcp
                        INNER JOIN factura fa_cp ON fa_cp.encabezado_id = fcp.id_encabezado_complemento
                        WHERE fcp.id_encabezado_factura = fa.encabezado_id
                          AND fa_cp.statusfactura NOT IN ('Cancelada','Error al Cancelar')
                    ), 0)                                              AS pagado_con_complementos,
                    fa.total                                           AS total_factura,
                    COALESCE(cc.saldo_pendiente, 0)                   AS saldo_actual
                FROM factura fa
                LEFT JOIN factura_credito  fc ON fc.factura_id    = fa.id
                LEFT JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id
                WHERE fa.encabezado_id = @id
                  AND fa.serie NOT IN ('CC')
                LIMIT 1;";

            var documento = RunQuery(query, parameters);
            var complementos = RunQuery(queryComplementos, parameters);
            var anticipos = RunQuery(queryAnticipos, parameters);
            var creditoRows = RunQuery(queryCredito, parameters);

            return Json(new
            {
                documento,
                complementos,
                anticipos,
                creditoInfo = creditoRows.Count > 0 ? creditoRows[0] : null
            });
        }

        // ============================================================
        // BÚSQUEDA DE PRODUCTOS (modal del editor de conceptos)
        // Devuelve el producto YA con su equivalencia SAT (catrelacion) y precio de lista,
        // para que al seleccionarlo se arme el concepto CFDI sin capturar a mano.
        // ============================================================
        [HttpGet]
        public IActionResult BuscarProductos(string q, int limit = 25)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                string patron = "%" + (q ?? "").Trim() + "%";

                var rows = RunQuery(@"
                    SELECT cp.id_catproductos              AS idproducto,
                           cp.cve_prod                     AS cveprod,
                           cp.descr_prod                   AS descripcion,
                           cp.udm                          AS udm,
                           COALESCE(cp.pv1, 0)             AS precio,
                           COALESCE(cr.prod_sat, '')       AS claveprodserv,
                           COALESCE(cr.ud_sat, 'H87')      AS claveunidad,
                           COALESCE(cr.obj_impto, '02')    AS objetoimp,
                           CASE
                                WHEN COALESCE(cr.iva_ex, 'N') = 'S' THEN true
                                ELSE false
                           END AS ivaexento
                    FROM catproductos cp
                    LEFT JOIN catrelacion cr ON cr.prod_kepler = cp.cve_prod
                    WHERE cp.empresa_id = @empresa_id
                      AND (cp.cve_prod ILIKE @q OR cp.descr_prod ILIKE @q)
                    ORDER BY cp.cve_prod
                    LIMIT @limit",
                    new Dictionary<string, object>
                    {
                        { "empresa_id", empresaId },
                        { "q", patron },
                        { "limit", limit < 1 ? 25 : Math.Min(limit, 100) }
                    });

                return Json(new { success = true, productos = rows });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesRefacturacionI/BuscarProductos");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ============================================================
        // REMISIONES DE ORIGEN DE LA FACTURA (alerta del wizard)
        // En este ERP el inventario lo mueve la REMISIÓN, no la factura. Una factura puede
        // venir de VARIAS remisiones y/o facturar solo PARTE de ellas; ambas cosas cambian
        // lo que la refacturación hará con el inventario, así que se avisan al usuario.
        // Devuelve además las partidas PENDIENTES de facturar (para las 3 estrategias).
        // ============================================================
        [HttpGet]
        public IActionResult RemisionesOrigen(int encabezadoId)
        {
            try
            {
                var remisiones = RunQuery(@"
                    SELECT DISTINCT fro.encabezado_remision_id AS encabezado,
                           em.folio ||
                             CASE WHEN em.variacion > 0
                                  THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio,
                           em.fch AS fecha,
                           em.estatus_id
                    FROM factura_remisiones_origen fro
                    INNER JOIN encabezadomov em ON em.id_encabezado = fro.encabezado_remision_id
                    WHERE fro.encabezado_factura_id = @enc
                    ORDER BY 1",
                    new Dictionary<string, object> { { "enc", encabezadoId } });

                var lista = new List<object>();
                bool hayParciales = false;

                foreach (var r in remisiones)
                {
                    int remEnc = Convert.ToInt32(r["encabezado"]);

                    // ¿La remisión respalda además otras facturas? (bloquea la refacturación)
                    int otrasFacturas = Convert.ToInt32(RunScalar(
                        @"SELECT COUNT(DISTINCT encabezado_factura_id)
                          FROM factura_remisiones_origen WHERE encabezado_remision_id = @rem",
                        new Dictionary<string, object> { { "rem", remEnc } }));

                    // Partidas con saldo pendiente de facturar en esa remisión. Se traen ya con la
                    // equivalencia SAT (catrelacion: prod_kepler → prod_sat/ud_sat/obj_impto) para
                    // que la opción "facturar en la refacturación" pueda armar el concepto CFDI.
                    var pendientes = RunQuery(@"
                        SELECT rpf.id_partida_remision              AS idpartida,
                               rpf.cve_prod                         AS cveprod,
                               COALESCE(pd.descr_prod, '')          AS descripcion,
                               COALESCE(pd.ud, '')                  AS unidad,
                               rpf.cantidad_original                AS cantidadoriginal,
                               rpf.cantidad_facturada               AS cantidadfacturada,
                               (rpf.cantidad_original - rpf.cantidad_facturada) AS pendiente,
                               rpf.precio_unitario                  AS precio,
                               COALESCE(rpf.descuento, 0)           AS descuento,
                               COALESCE(cr.prod_sat, '')            AS claveprodserv,
                               COALESCE(cr.ud_sat, 'H87')           AS claveunidad,
                               COALESCE(cr.obj_impto, '02')         AS objetoimp
                        FROM remision_partidas_facturadas rpf
                        LEFT JOIN partidasdoc  pd ON pd.id_partidas   = rpf.id_partida_remision
                        LEFT JOIN catrelacion  cr ON cr.prod_kepler   = rpf.cve_prod
                        WHERE rpf.encabezado_remision_id = @rem
                          AND rpf.cantidad_original > rpf.cantidad_facturada
                        ORDER BY rpf.cve_prod",
                        new Dictionary<string, object> { { "rem", remEnc } });

                    if (pendientes.Count > 0) hayParciales = true;

                    lista.Add(new
                    {
                        encabezado = remEnc,
                        folio = r["folio"]?.ToString(),
                        fecha = r["fecha"],
                        compartida = otrasFacturas > 1,
                        pendientes
                    });
                }

                return Json(new
                {
                    success = true,
                    remisiones = lista,
                    multiples = lista.Count > 1,
                    tieneParciales = hayParciales
                });
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // Sin las tablas de remisiones no hay nada que avisar (facturas que no vienen de remisión).
                return Json(new { success = true, remisiones = new List<object>(), multiples = false, tieneParciales = false });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesRefacturacionI/RemisionesOrigen");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ============================================================
        // VALIDACIÓN DE DOCUMENTOS RELACIONADOS (Step 3 del wizard)
        // Fuente única de verdad de bloqueos/advertencias — el mismo AnalizadorDocumentos
        // que usan los handlers para bloquear la ejecución en el backend.
        // ============================================================
        [HttpGet]
        public IActionResult ValidarDocumentos(int encabezadoId, string tipoId)
        {
            var analisis = AnalizadorDocumentos.Analizar(this, encabezadoId);
            var verdicto = AnalizadorDocumentos.Evaluar(analisis, tipoId);

            return Json(new
            {
                puedeRefacturar = verdicto.PuedeRefacturar,
                bloqueos = verdicto.Bloqueos,
                advertencias = verdicto.Advertencias,
                sugerenciaNotaCredito = verdicto.SugerenciaNotaCredito,
                analisis = new
                {
                    complementosVigentes = analisis.ComplementosVigentes,
                    totalComplementos = analisis.TotalComplementos,
                    montoAnticipos = analisis.MontoAnticiposAplicado,
                    montoCobros = analisis.MontoCobrosAplicados,
                    notasCredito = analisis.NotasCredito,
                    notasDebito = analisis.NotasDebito,
                    notasAnticipo = analisis.NotasAplicacionAnticipo,
                    sustitutosVigentes = analisis.SustitutosVigentes,
                    estaPagada = analisis.EstaPagada,
                    tienePagos = analisis.TienePagos,
                    saldo = analisis.Saldo,
                    total = analisis.Total
                }
            });
        }

        // ============================================================
        // KPIs DEL MÓDULO
        // ============================================================
        [HttpGet]
        public IActionResult ObtenerKPIs()
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            string filtroTipo = GetFiltroTipoFactura();

            var parameters = new Dictionary<string, object> { { "suc", suc } };

            // Si tienes tabla refacturacion_log usa esas consultas;
            // si no, el catch devuelve ceros sin romper la página.
            try
            {
                string query = $@"
                    SELECT
                        (SELECT COUNT(*) FROM refacturacion_log
                         WHERE suc = @suc AND estatus = 'pendiente')        AS pendientes,
                        (SELECT COUNT(*) FROM refacturacion_log
                         WHERE suc = @suc AND estatus = 'completado'
                           AND fecha::date = CURRENT_DATE)                  AS hoy,
                        (SELECT COUNT(DISTINCT fa.id)
                         FROM factura fa
                         INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
                         WHERE em.suc = @suc
                           AND fa.statusfactura = 'Timbrada'
                           AND (fa.mdpfactura = 'PPD' OR fa.anticipo > 0)
                           {filtroTipo}
                        )                                                    AS riesgo_alto";

                var row = RunQuery(query, parameters);
                return Json(row.Count > 0 ? row[0]
                    : new Dictionary<string, object> { { "pendientes", 0 }, { "hoy", 0 }, { "riesgo_alto", 0 } });
            }
            catch
            {
                return Json(new { pendientes = 0, hoy = 0, riesgo_alto = 0 });
            }
        }

        // ============================================================
        // ADENDAS DISPONIBLES PARA UN CLIENTE
        // ============================================================
        [HttpGet]
        public IActionResult ObtenerAdendas(int idCliente)
        {
            var parameters = new Dictionary<string, object> { { "idCliente", idCliente } };
            string query = @"
        SELECT id_addenda, nombre, xml_namespace, xml_schema, xml_prefix,
               usar_conceptos, version, data_template
        FROM cfdi_addenda_def
        WHERE id_cliente = @idCliente AND activo = true
        ORDER BY nombre";
            var data = RunQuery(query, parameters);
            return Json(new { data });
        }

        [HttpPost]
        public async Task<IActionResult> EjecutarRefacturacion([FromBody] RefacturacionRequest req)
        {
            string connStr = _configuration.GetConnectionString("ERP_SRS");
            using var conn = new NpgsqlConnection(connStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // El orquestador hereda Utilities pero lo crea DI (sin HttpContext). Le prestamos
                // el ControllerContext de este controlador para que HttpContext.Session esté vivo
                // (helpers como GenerarDatosPoliza leen Session["Empresa"]).
                _orchestrator.ControllerContext = ControllerContext;

                var resultado = await _orchestrator.EjecutarAsync(req, conn, tx, User.Identity.Name);

                if (!resultado.Success)
                {
                    tx.Rollback();
                    return Json(new { success = false, message = resultado.Message });
                }

                tx.Commit();

                // Generar y GUARDAR el PDF ya con commit hecho (queda archivado en
                // wwwroot/Facturacion/facturas). Ahora es rápido (logo/QR en base64, sin red),
                // así que no cuelga el request. Si fallara, DescargarPdf lo regenera bajo demanda.
                if (resultado.FacturaNueva != null)
                    await GenerarPdfFactura(resultado.FacturaNueva);

                return Json(new
                {
                    success = true,
                    uuidNuevo = resultado.UuidNuevo,
                    message = resultado.Message,
                    xmlUrl = Url.Content($"~/DatosGeneralesRefacturacion/DescargarXml?uuid={resultado.UuidNuevo}"),
                    pdfUrl = Url.Content($"~/DatosGeneralesRefacturacion/DescargarPdf?uuid={resultado.UuidNuevo}")
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGeneralesRefacturacionI/EjecutarRefacturacion");
                tx.Rollback();
                LogErrorHelper.RegistrarLog("EjecutarRefacturacion", req.UuidOriginal, ex.ToString(), User.Identity.Name);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ============================================================
        // GENERAR PDF DEL CFDI NUEVO  (mismo molde/diseño que FacturacionVenta)
        // ============================================================
        private async Task<string> GenerarPdfFactura(Factura factura)
        {
            string headerPath = null;
            string rutaQrFisica = null;
            try
            {
                // El PDF final se archiva en wwwroot/Facturacion/facturas (excluido del watch en el .csproj).
                string facturasPath = Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas");
                Directory.CreateDirectory(facturasPath);

                // Archivos TEMPORALES (QR + header HTML) en el TEMP del sistema, FUERA del árbol del
                // proyecto: así el Hot Reload de Visual Studio no dispara un refresh del navegador al
                // escribirlos (era la causa de la "recarga" al generar el PDF).
                string tempDir = Path.Combine(Path.GetTempPath(), "refacturacion_pdf");
                Directory.CreateDirectory(tempDir);

                // QR incrustado como base64 (sin red): el molde lo pinta desde Model.RutaQr y
                // wkhtmltopdf no hace ninguna petición, evitando cuelgues.
                rutaQrFisica = Path.Combine(tempDir, $"qr_{factura.UUID}.png");
                _comprobanteFiscal.GenerarQr(factura, tempDir);
                factura.RutaQr = "data:image/png;base64," + Convert.ToBase64String(System.IO.File.ReadAllBytes(rutaQrFisica));

                // Header dinámico (emisor + logo) como HTML temporal, igual que FacturacionVenta.GenerarFactura
                string headerHtml = await RenderViewToStringAsync("/Views/Ventas/Refacturacion/Header.cshtml", factura);
                headerPath = Path.Combine(tempDir, $"header_{factura.UUID}.html");
                await System.IO.File.WriteAllTextAsync(headerPath, headerHtml, Encoding.UTF8);

                // PDF con la vista dedicada de refacturación (incluye CfdiRelacionados) + header
                var pdf = new ViewAsPdf("/Views/Ventas/Refacturacion/FacturaPdf.cshtml", factura)
                {
                    PageSize = Size.A4,
                    PageMargins = new Margins(45, 10, 20, 10),
                    FileName = $"Factura_{factura.UUID}.pdf",
                    CustomSwitches =
                        $"--encoding utf-8 --header-html \"{headerPath}\" " +
                        "--header-spacing 5 " +
                        "--footer-center \"Página [page] de [toPage]\" " +
                        "--footer-line --footer-font-size 8"
                };

                byte[] pdfBytes = await pdf.BuildFile(ControllerContext);
                string rutaPdf = Path.Combine(facturasPath, $"{factura.UUID}.pdf");
                await System.IO.File.WriteAllBytesAsync(rutaPdf, pdfBytes);

                return Url.Content($"~/DatosGeneralesRefacturacion/DescargarPdf?uuid={factura.UUID}");
            }
            catch (Exception ex)
            {
                // El CFDI ya es válido; si el PDF falla, no rompemos la operación.
                LogErrorHelper.RegistrarLog("Refacturacion_PDF", factura?.UUID ?? "", ex.ToString(), User.Identity.Name);
                return null;
            }
            finally
            {
                // Limpieza de temporales (header HTML + QR)
                try { if (headerPath != null && System.IO.File.Exists(headerPath)) System.IO.File.Delete(headerPath); }
                catch { /* no crítico */ }
                try { if (rutaQrFisica != null && System.IO.File.Exists(rutaQrFisica)) System.IO.File.Delete(rutaQrFisica); }
                catch { /* no crítico */ }
            }
        }

        // Renderiza una vista Razor a string (para el --header-html), como en FacturacionVenta.
        // Acepta ruta absoluta ("/Views/...") vía GetView, o nombre simple vía FindView.
        private async Task<string> RenderViewToStringAsync(string viewName, object model)
        {
            var actionContext = new ActionContext(HttpContext, RouteData, ControllerContext.ActionDescriptor);

            var viewResult = (viewName.StartsWith("/") || viewName.StartsWith("~"))
                ? _viewEngine.GetView(executingFilePath: null, viewPath: viewName, isMainPage: false)
                : _viewEngine.FindView(actionContext, viewName, isMainPage: false);

            if (viewResult?.View == null)
                throw new InvalidOperationException($"No se encontró la vista: {viewName}");

            using var sw = new StringWriter();

            var viewDataDictionary = new ViewDataDictionary(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
            {
                Model = model
            };

            var tempData = new TempDataDictionary(HttpContext, _tempDataProvider);

            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewDataDictionary,
                tempData,
                sw,
                new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }

        // ============================================================
        // DESCARGAS  (XML timbrado y PDF del CFDI nuevo)
        // ============================================================
        [HttpGet]
        public IActionResult DescargarXml(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return BadRequest("UUID requerido.");
            string path = Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "xml_timbrados", $"{uuid}.xml");
            if (!System.IO.File.Exists(path)) return NotFound("XML no encontrado.");
            return PhysicalFile(path, "application/xml", $"{uuid}.xml");
        }

        [HttpGet]
        public async Task<IActionResult> DescargarPdf(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return BadRequest("UUID requerido.");
            string path = Path.Combine(_env.ContentRootPath, "wwwroot", "Facturacion", "facturas", $"{uuid}.pdf");

            // Generación bajo demanda: si aún no existe el PDF, se reconstruye la factura y se genera.
            if (!System.IO.File.Exists(path))
            {
                var factura = ReconstruirFacturaParaPdf(uuid);
                if (factura == null) return NotFound("No se encontró la factura para generar el PDF.");
                await GenerarPdfFactura(factura);
            }

            if (!System.IO.File.Exists(path)) return StatusCode(500, "No se pudo generar el PDF.");
            return PhysicalFile(path, "application/pdf", $"Factura_{uuid}.pdf");
        }

        // Reconstruye la Factura (conceptos, sellos, comprobante, relacionados) desde la BD por UUID,
        // para poder generar su PDF bajo demanda. FacturaBuilder saca todo del XML timbrado (textfactura).
        private Factura ReconstruirFacturaParaPdf(string uuid)
        {
            if (!Guid.TryParse(uuid, out var uuidGuid)) return null;

            var rows = RunQuery(
                "SELECT encabezado_id FROM factura WHERE uuid = @uuid LIMIT 1",
                new Dictionary<string, object> { { "uuid", uuidGuid } });

            if (rows.Count == 0) return null;

            int encId = Convert.ToInt32(rows[0]["encabezado_id"]);
            return FacturaBuilder.DesdeEncabezado(this, encId);
        }
    }
}
