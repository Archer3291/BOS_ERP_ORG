// Controllers/FacturaConsultaController.cs
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Npgsql;
using Rotativa.AspNetCore;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Drawing.Imaging;
using System.Net.Mail;
using System.Text;
using System.Xml.Linq;
using TuNamespace.Models;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    [RightAuthorize(new[] { "facturacion_especial", "facturacion_normal", "facturacion_global", "complemento_pago" })]
    public class FacturaConsultaController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;

        public FacturaConsultaController(
            BOS_ERP.Services.EmailSender emailSenderService,
            IConfiguration configuration,
            IWebHostEnvironment env,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider)
        {
            _emailSender = emailSenderService;
            _configuration = configuration;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
        }

        /// <summary>
        /// Renderiza una vista a texto usando el HttpContext REAL de la petición.
        /// EmailSender no sirve para esto: fabrica un DefaultHttpContext sin sesión, y las
        /// vistas de encabezado leen Context.Session (por ejemplo "EmpresaFactura" para
        /// elegir el logo). Con el contexto sintético eso lanza "Session has not been
        /// configured for this application or request".
        /// </summary>
        [NonAction]
        private async Task<string> RenderViewToStringAsync(string viewPath, object model)
        {
            var actionContext = new ActionContext(HttpContext, RouteData, ControllerContext.ActionDescriptor);
            var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath, isMainPage: false);

            if (viewResult?.View == null)
                throw new FileNotFoundException($"No se encontró la vista '{viewPath}'.");

            using var sw = new StringWriter();

            var viewData = new ViewDataDictionary(
                new EmptyModelMetadataProvider(), new ModelStateDictionary())
            { Model = model };

            var viewContext = new ViewContext(
                actionContext, viewResult.View, viewData,
                new TempDataDictionary(HttpContext, _tempDataProvider),
                sw, new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }

        // Rutas FÍSICAS. "~/algo" es una ruta web y Path.Combine no la resuelve: se tomaba
        // como relativa y creaba una carpeta llamada literalmente "~" junto al ejecutable.
        // Ahí acabaron los acuses y los QR, mientras Url.Content("~/...") apuntaba a
        // wwwroot —donde no estaban—, así que los enlaces de descarga quedaban rotos.
        private string CancelacionesPath => Path.Combine(_env.WebRootPath, "Facturacion", "Cancelaciones");
        private string CancelacionesPdfPath => Path.Combine(CancelacionesPath, "pdf");
        private string ContentPdfPath => Path.Combine(_env.WebRootPath, "content", "pdf");

        /// <summary>
        /// Este módulo lista COMPROBANTES, así que sólo debe mostrar los que cuelgan de un
        /// documento de factura. Una fila de `factura` puede apuntar temporalmente a un
        /// documento de venta —es el caso de la autofacturación: el cliente timbra desde el
        /// portal cuando todavía no existe el VSFAC, que se crea al contabilizar en el
        /// cierre de caja— y mientras tanto no tiene nada que hacer aquí.
        ///
        /// La lista se armó a partir de las naturalezas realmente presentes en `factura`:
        ///   VSFAC/VIFAC/VNFAC/VINFAC (ventas), GLFAC (global), FAR (arrendamiento),
        ///   NT (notas de crédito), CPFAC (complementos de pago), RICD (carga de cartera).
        /// La única que quedaba fuera era VSUC, que es una venta.
        ///
        /// ⚠ Si se agrega un tipo de comprobante nuevo hay que darlo de alta aquí, o sus
        ///   facturas desaparecerán del módulo sin aviso.
        /// </summary>
        private const string FiltroDocumentosDeFactura =
            " AND em.nat IN ('VSFAC','VIFAC','VNFAC','VINFAC','FAR','NT','CPFAC','RICD','FACLIB') ";

        private string GetFiltroTipoFactura()
        {
            var tiposPermitidos = new List<string>();

            // Mapeo permiso -> valor del campo fa.tipo en la BD
            // Ajusta los valores ('E', 'N', 'G', 'P') según los que uses en tu tabla
            if (DoesUserHasRight(User.Identity.Name, "facturacion_especial_internacional"))
                tiposPermitidos.Add("'VIS'");
            if (DoesUserHasRight(User.Identity.Name, "factura_arrendamiento"))
                tiposPermitidos.Add("'FAR'");

            // 'AF' = autofacturación: el cliente emitió el CFDI desde el portal público.
            // Es una factura de ingreso como cualquier otra; sólo cambia quién capturó los
            // datos del receptor. Lo que la mantenía fuera del módulo mientras no estuviera
            // contabilizada no es la serie sino la naturaleza del documento del que cuelga
            // (ver FiltroDocumentosDeFactura).
            if (DoesUserHasRight(User.Identity.Name, "facturacion_normal"))
                tiposPermitidos.Add("'VI', 'VS', 'VN', 'AF', 'FL' ");

            if (DoesUserHasRight(User.Identity.Name, "facturacion_global"))
                tiposPermitidos.Add("'G'");

            if (DoesUserHasRight(User.Identity.Name, "complemento_pago"))
                tiposPermitidos.Add("'CC'");

            return tiposPermitidos.Any()
                ? $"AND fa.serie IN ({string.Join(",", tiposPermitidos)})"
                : "AND 1=0"; // Sin permisos = no ve nada
        }

        private string GetFiltroSucursal()
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            // Opción futura: si tienes un permiso de admin global que ve todas:
            // if (DoesUserHasRight(User.Identity.Name, "admin_global"))
            //     return ""; // Sin filtro de sucursal

            return "AND em.suc = @suc";
        }

        // ============================================
        // OBTENER FACTURAS (con filtros de permisos)
        // ============================================

        /// <summary>
        /// Filtro por estatus del comprobante.
        /// Se compara con LOWER porque el dato está inconsistente en la base: conviven
        /// "TIMBRADA" y "Timbrada", y sin normalizar la pestaña dejaría fuera un tercio de
        /// las facturas. Cancelada agrupa también los estados intermedios del proceso de
        /// cancelación ("Pendiente Cancelación", "Error al Cancelar"), que de otro modo no
        /// aparecerían en ninguna pestaña.
        /// </summary>
        private static string GetFiltroEstatus(string estatus) => (estatus ?? "").ToLowerInvariant() switch
        {
            "timbradas" => " AND LOWER(fa.statusfactura) = 'timbrada' ",
            "canceladas" => " AND LOWER(fa.statusfactura) LIKE '%cancel%' ",
            _ => ""     // "todas": sin filtro, para que nada quede escondido
        };

        /// <summary>Conteo por estatus, para las etiquetas de las pestañas.</summary>
        [HttpPost]
        public IActionResult ContarPorEstatus()
        {
            string filtroTipo = GetFiltroTipoFactura();
            string filtroSuc = GetFiltroSucursal();

            var parameters = new Dictionary<string, object>
            {
                { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
            };

            var r = RunQuery(
                "SELECT " +
                "  COUNT(*) AS todas, " +
                "  COUNT(*) FILTER (WHERE LOWER(fa.statusfactura) = 'timbrada')   AS timbradas, " +
                "  COUNT(*) FILTER (WHERE LOWER(fa.statusfactura) LIKE '%cancel%') AS canceladas " +
                "FROM factura fa " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id " +
                // Los espacios alrededor de cada filtro son necesarios: GetFiltroSucursal
                // termina en "@suc" sin espacio y GetFiltroTipoFactura empieza con "AND",
                // así que al concatenarlos directo quedaba "@sucAND fa.serie ..." y Postgres
                // respondía «error de sintaxis en o cerca de "fa"».
                $" WHERE 1=1 {filtroSuc} {filtroTipo} {FiltroDocumentosDeFactura} ",
                parameters).FirstOrDefault();

            return Json(new
            {
                success = true,
                todas = Convert.ToInt32(r?["todas"] ?? 0),
                timbradas = Convert.ToInt32(r?["timbradas"] ?? 0),
                canceladas = Convert.ToInt32(r?["canceladas"] ?? 0)
            });
        }

        public IActionResult ObtenerFacturas(string nombre, string estatus = null, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre ?? "" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
            };

            // ✅ Filtros de seguridad
            string filtroTipo = GetFiltroTipoFactura();
            string filtroSuc = GetFiltroSucursal();

            string where =
                " WHERE (LOWER(fa.serie) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(fa.rfccliente) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(fa.rsocliente) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(em.folio || " +
                "  CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%')) " +
                " " +
                $" {filtroSuc} " +                    // ✅ filtro sucursal
                $" {filtroTipo} " +                   // ✅ filtro tipo de factura
                FiltroDocumentosDeFactura +           // ✅ sólo documentos de factura
                GetFiltroEstatus(estatus);            // ✅ pestaña: timbradas / canceladas

            string having = " HAVING 1=1 ";

            string query =
                "SELECT  " +
                "CASE WHEN fc.fecha_carga_portal IS NULL THEN NULL  ELSE EXTRACT(DAY FROM (fc.fecha_carga_portal + (fc.dias_credito || ' days')::INTERVAL - NOW())) " +
                "END AS dias_credito_restantes, fc.fecha_carga_portal, fc.dias_credito, " +
                "em.refe, fa.id, serie, fa.folio as fac_folio, idtipofactura, idcliente, rfccliente, rsocliente, emlcliente, idemisor, rfcemisor,  " +
                "rsoemisor, idexpedicion, idusuario, fecha, fechatimbrado, statusfactura,  " +
                "mdpfactura, textfactura, idlugarexp, idtipopago, fa.uuid, importe, descuento, subtotal, fa.iva, total, cc.saldo_pendiente AS saldo, idpedido,  " +
                "retisr, retiva, fa.moneda, fa.observaciones, idvendedor, usocfdi, idusocfdi, cbb, parcialidad, sellosat, usr_doc, " +
                "sellocfdi, cadenaoriginal, oc, tdc, anticipo, reg_fisr, reg_fise, cpr, cpe, tipo, fa.encabezado_id, " +
                " em.folio ||  " +
                "  CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio,  " +
                " EXISTS (SELECT 1 FROM encabezadomov em_cp " +
                "       INNER JOIN factura fa_cp ON fa_cp.encabezado_id = em_cp.id_encabezado " +
                "       WHERE em_cp.encabezados_padre = fa.encabezado_id AND em_cp.tp_mov = 'CPFAC')" +
                "       AS tiene_complemento," +
                "COALESCE((SELECT COUNT(*)FROM encabezadomov em_cp WHERE em_cp.encabezados_padre = fa.encabezado_id AND em_cp.tp_mov = 'CPFAC'), 0)                                               AS complementos_count,\r\n\r\n    -- ¿Tiene anticipos aplicados?\r\n    EXISTS (\r\n        SELECT 1\r\n        FROM factura_anticipos fan\r\n        WHERE fan.id_factura_principal = fa.id\r\n    )                                                   AS tiene_anticipo,\r\n\r\n    -- Monto total de anticipos aplicados\r\n    COALESCE((\r\n        SELECT SUM(fan.monto_aplicado)\r\n        FROM factura_anticipos fan\r\n        WHERE fan.id_factura_principal = fa.id\r\n    ), 0)                                               AS saldo_anticipo_aplicado " +
                "FROM factura fa  " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id " +
                "LEFT JOIN factura_credito fc ON fc.factura_id = fa.id  " +
                "LEFT JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id" +
                $" {where} " +
                "GROUP BY em.folio, em.refe, fa.id, fa.serie, fa.folio, fa.idtipofactura, fa.idcliente, fa.rfccliente, fa.rsocliente, fa.emlcliente, fa.idemisor, fa.rfcemisor, fa.rsoemisor, fa.idexpedicion, fa.idusuario, fa.fecha, fa.fechatimbrado, fa.statusfactura, fa.mdpfactura, fa.textfactura, fa.idlugarexp, fa.idtipopago, fa.uuid, fa.importe, fa.descuento, fa.subtotal, fa.iva, fa.total, cc.saldo_pendiente, fa.idpedido, fa.retisr, fa.retiva, fa.moneda, fa.observaciones, fa.idvendedor, fa.usocfdi, fa.idusocfdi, fa.cbb, fa.parcialidad, fa.sellosat, em.usr_doc, fa.sellocfdi, fa.cadenaoriginal, fa.oc, fa.tdc, fa.anticipo, fa.reg_fisr, fa.reg_fise, fa.cpr, fa.cpe, fa.tipo, fa.encabezado_id, em.gen, em.nat, em.fch, em.fol_doc, em.variacion, fc.fecha_carga_portal, fc.dias_credito " +
                $" {having}  " +
                "ORDER BY id DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            // Query de conteo (usa los mismos filtros)
            query =
                "SELECT COUNT(*) " +
                "FROM (SELECT DISTINCT fa.id, fa.folio FROM factura fa " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id " +
                $" {where} " +
                "GROUP BY fa.id, fa.folio " +
                $" {having}) sub";

            var total = RunScalar(query, parameters);

            return Json(new { data, total });
        }

        // ============================================
        // DETALLE DE FACTURA
        // ============================================

        public IActionResult FacturaDetail(int id)
        {
            // Seguridad: verificar que el encabezado_id pertenece a la sucursal del usuario
            // y a un tipo de factura al que tiene acceso
            string filtroTipo = GetFiltroTipoFactura();
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            string query = $@"
                SELECT jsonb_build_object(
    'encabezado', jsonb_build_object(
        'id_encabezado', em.id_encabezado,
        'suc', em.suc,
        'gen', em.gen,
        'nat', em.nat,
        'fol_doc', em.fol_doc,
        'cli_prov', em.cli_prov,
        'fecha_encabezado', em.fch,
        'coment1', em.coment1,
        'coment2', em.coment2,
        'coment_aut', em.coment_aut,
        'tp_mov', em.tp_mov,
        'cfdi', em.cfdi,
        'f_pago', em.f_pago,
        'incoterm', em.incoterm,
        'mdp', em.mdp,
        'estatus_id', em.estatus_id
    ),
    'facturas', jsonb_agg(
        jsonb_build_object(
            'factura_id', fa.id,
            'serie', fa.serie,
            'folio', fa.folio,
            'fecha', fa.fecha, 
            'fecha_vencimiento', fc.fecha_vencimiento,
            'fechatimbrado', fa.fechatimbrado,
            'rfccliente', fa.rfccliente,
            'rsocliente', fa.rsocliente,
            'rfcemisor', fa.rfcemisor,
            'rsoemisor', fa.rsoemisor,
            'total', fa.total,
            'subtotal', fa.subtotal,
            'iva', fa.iva,
            'descuento', fa.descuento,
            'uuid', fa.uuid,
            'moneda', fa.moneda,
            'statusfactura', fa.statusfactura,
            'mdpfactura', fa.mdpfactura,
            'textfactura', fa.textfactura,
            'idtipofactura', fa.idtipofactura,
            'idcliente', fa.idcliente,
            'idvendedor', fa.idvendedor,
            'oc', fa.oc,
            'encabezado_id', fa.encabezado_id,
            'productos', (
                SELECT jsonb_agg(
                    jsonb_build_object(
                        'idproducto', df.idproducto,
                        'descripcion', df.descripcion,
                        'cantidad', df.cantidad,
                        'precio', df.precio,
                        'descuento_partida', df.descuento,
                        'saldo_partida', df.saldo,
                        'udm', df.udm,
                        'claveprodserv', df.claveprodserv,
                        'claveprod', df.claveprod,
                        'fraccionarancelaria', df.fraccionarancelaria,
                        'clave_cliente', df.clave_cliente
                    )
                )
                FROM dfactura df
                WHERE df.idfac = fa.id
            )
        )
    )
) AS documento
FROM encabezadomov em
INNER JOIN factura fa ON fa.encabezado_id = em.id_encabezado
LEFT JOIN factura_credito fc ON fc.factura_id = fa.id
WHERE fa.encabezado_id = @id
  AND em.suc = @suc
  {filtroTipo}
GROUP BY em.id_encabezado, em.suc, em.gen, em.nat, em.fol_doc, em.cli_prov,
         em.fch, em.coment1, em.coment2, em.coment_aut, em.tp_mov, em.cfdi,
         em.f_pago, em.incoterm, em.mdp, em.estatus_id;
";

            var parameters = new Dictionary<string, object>
            {
                { "id", id },
                { "suc", suc }
            };

            // ── Complementos de pago vinculados ──────────────────────────
            string queryComplementos = $@"
        SELECT
            fa_cp.id                    AS complemento_id,
            fa_cp.serie                 AS serie,
            fa_cp.folio                 AS folio,
            fa_cp.fecha                 AS fecha_pago,
            fa_cp.total                 AS monto_pagado,
            fa_cp.uuid::text            AS uuid,
            fa_cp.statusfactura         AS estatus,
            fa_cp.mdpfactura            AS metodo_pago,
            em_cp.mdp                   AS forma_pago
        FROM encabezadomov em_cp
        INNER JOIN factura fa_cp ON fa_cp.encabezado_id = em_cp.id_encabezado
        WHERE em_cp.encabezados_padre = (
            SELECT encabezado_id FROM factura WHERE encabezado_id = @id LIMIT 1
        )
          AND em_cp.tp_mov = 'CPFAC'
        ORDER BY fa_cp.fecha ASC;";

            var complementos = RunQuery(queryComplementos, parameters);

            // ── Anticipos aplicados ───────────────────────────────────────
            string queryAnticipos = @"
        SELECT
            fan.id                          AS aplicacion_id,
            fan.monto_aplicado,
            fan.fecha_aplicacion,
            fan.saldo_antes,
            fan.saldo_despues,
            fan.observaciones,
            fa_ant.serie                    AS anticipo_serie,
            fa_ant.folio                    AS anticipo_folio,
            fa_ant.uuid::text               AS anticipo_uuid,
            fa_ant.fecha                    AS anticipo_fecha,
            fa_ant.total                    AS anticipo_monto_original,
            fa_ant.saldo                    AS anticipo_saldo_actual
        FROM factura_anticipos fan
        INNER JOIN factura fa_ant 
              ON fa_ant.id = fan.id_factura_anticipo
        WHERE fan.id_factura_principal = (
              SELECT id FROM factura WHERE encabezado_id = @id AND serie != 'NC'
              LIMIT 1
        )
        ORDER BY fan.fecha_aplicacion ASC;";

            var anticipos = RunQuery(queryAnticipos, parameters);

            // ── Saldo de crédito (PPD) — ya lo tenías, aquí lo enriquecemos
            string queryCredito = @"
        SELECT
            fc.fecha_carga_portal,
            fc.dias_credito,
            fc.fecha_vencimiento,
            fc.estatus_carga,
            fc.observaciones,
            EXTRACT(DAY FROM (fc.fecha_vencimiento - NOW()))    AS dias_restantes,
            -- Suma de complementos ya pagados
            COALESCE((
                SELECT SUM(fa_cp.total)
                FROM encabezadomov em_cp
                INNER JOIN factura fa_cp ON fa_cp.encabezado_id = em_cp.id_encabezado
                WHERE em_cp.encabezados_padre = fa.encabezado_id
                  AND em_cp.tp_mov = 'CPFAC'
                  AND fa_cp.statusfactura NOT IN ('Cancelada', 'Error al Cancelar')
            ), 0)                                               AS pagado_con_complementos,
            fa.total                                            AS total_factura,
            COALESCE(cc.saldo_pendiente, 0) AS saldo_actual
        FROM factura fa
        LEFT JOIN factura_credito fc ON fc.factura_id = fa.id
        LEFT JOIN cartera_clientes cc ON cc.encabezado_id = fa.encabezado_id
        WHERE fa.encabezado_id = @id
          AND fa.serie NOT IN ('CC')
        LIMIT 1;";

            var creditoRows = RunQuery(queryCredito, parameters);
            var creditoInfo = creditoRows.Count > 0 ? creditoRows[0] : null;

            // ── Remisiones de origen (de qué remisiones se creó la factura) ──────
            // El inventario/venta industrial nace en la remisión; aquí se listan las que
            // respaldan esta factura (factura_remisiones_origen) con sus partidas facturadas.
            List<Dictionary<string, object>> remisiones = new();
            try
            {
                remisiones = RunQuery(@"
                    SELECT em.id_encabezado AS encabezado,
                           em.folio || CASE WHEN em.variacion > 0
                                THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio,
                           em.fch          AS fecha,
                           em.estatus_id   AS estatus_id,
                           em.variacion    AS variacion,
                           COALESCE(jsonb_agg(jsonb_build_object(
                               'cve_prod', fro.cve_prod,
                               'cantidad', fro.cantidad_facturada,
                               'precio',   fro.precio_unitario,
                               'importe',  fro.importe
                           ) ORDER BY fro.cve_prod), '[]'::jsonb)::text AS partidas
                    FROM factura_remisiones_origen fro
                    INNER JOIN encabezadomov em ON em.id_encabezado = fro.encabezado_remision_id
                    WHERE fro.encabezado_factura_id = @id
                    GROUP BY em.id_encabezado, em.folio, em.variacion, em.fch, em.estatus_id
                    ORDER BY em.id_encabezado", parameters);
            }
            catch (PostgresException) { remisiones = new(); }

            // ── Refactura / sustitución (relaciones CFDI 04) ─────────────────────
            // Indica si esta factura sustituyó a otra (es una refactura) y/o si fue
            // sustituida por otra. Datos de refacturacion_relaciones + refacturacion_log.
            object refactura = null;
            try
            {
                var uuidRows = RunQuery(
                    @"SELECT uuid::text AS uuid FROM factura
                      WHERE encabezado_id = @id AND serie NOT IN ('NC','ND','CC')
                      ORDER BY id LIMIT 1", parameters);

                if (uuidRows.Count > 0 && uuidRows[0]["uuid"] != null)
                {
                    var pRef = new Dictionary<string, object> { { "uuid", Guid.Parse(uuidRows[0]["uuid"].ToString()) } };

                    const string colsRef = @"
                        rr.tipo_relacion, rr.fecha,
                        rl.tipo AS tipo_refacturacion, rl.modo,
                        f.serie, f.folio, f.statusfactura, f.total, f.encabezado_id";

                    var sustituyeA = RunQuery($@"
                        SELECT rr.uuid_original::text AS uuid, {colsRef}
                        FROM refacturacion_relaciones rr
                        LEFT JOIN refacturacion_log rl
                               ON rl.uuid_nuevo = rr.uuid_nuevo AND rl.uuid_original = rr.uuid_original
                        LEFT JOIN factura f ON f.uuid = rr.uuid_original
                        WHERE rr.uuid_nuevo = @uuid
                        ORDER BY rr.fecha DESC LIMIT 1", pRef);

                    var sustituidaPor = RunQuery($@"
                        SELECT rr.uuid_nuevo::text AS uuid, {colsRef}
                        FROM refacturacion_relaciones rr
                        LEFT JOIN refacturacion_log rl
                               ON rl.uuid_nuevo = rr.uuid_nuevo AND rl.uuid_original = rr.uuid_original
                        LEFT JOIN factura f ON f.uuid = rr.uuid_nuevo
                        WHERE rr.uuid_original = @uuid
                        ORDER BY rr.fecha DESC LIMIT 1", pRef);

                    refactura = new
                    {
                        sustituyeA = sustituyeA.Count > 0 ? sustituyeA[0] : null,
                        sustituidaPor = sustituidaPor.Count > 0 ? sustituidaPor[0] : null
                    };
                }
            }
            catch (PostgresException) { refactura = null; }

            var documento = RunQuery(query, parameters);

            return Json(new
            {
                documento,                       // ya existía
                complementos,                    // NUEVO
                anticipos,                       // NUEVO
                creditoInfo,                     // NUEVO (reemplaza o complementa lo que había)
                remisiones,                      // NUEVO — remisiones que crearon la factura
                refactura,                       // NUEVO — sustitución/refactura (rel. 04)
            });
        }

        // ============================================
        // ENVIAR EMAIL (desde facturación)
        // ============================================

        [HttpPost]
        public async Task<IActionResult> EnviarEmail()
        {
            try
            {
                string perfil = HttpContext.Session.GetString("EmpresaFactura");
                var emisores = _configuration.GetSection("emisores").Get<NameValueCollection>();

                string facturaId = Request.Form["facturaId"].ToString();
                string emailsString = Request.Form["emails"].ToString();
                string asunto = Request.Form["asunto"].ToString();
                string observaciones = Request.Form["mensaje"].ToString();
                string incluirPDFStr = Request.Form["incluirPDF"].ToString();
                string incluirXMLStr = Request.Form["incluirXML"].ToString();

                string serie = Request.Form["serie"].ToString();
                string folio = Request.Form["folio"].ToString();
                string totalStr = Request.Form["total"].ToString();
                string razonSocial = Request.Form["razonSocial"].ToString();
                string rfcCliente = Request.Form["rfcCliente"].ToString();

                if (string.IsNullOrWhiteSpace(emailsString) || string.IsNullOrWhiteSpace(asunto))
                    return Json(new { success = false, message = "Asunto y destinatario son obligatorios." });

                if (string.IsNullOrWhiteSpace(facturaId))
                    return Json(new { success = false, message = "ID de factura es obligatorio." });

                var emails = emailsString
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct()
                    .ToList();

                if (!emails.Any())
                    return Json(new { success = false, message = "No se proporcionaron correos válidos." });

                var emailsInvalidos = emails.Where(e => !EsEmailValido(e)).ToList();
                if (emailsInvalidos.Any())
                    return Json(new { success = false, message = $"Los siguientes correos tienen formato inválido: {string.Join(", ", emailsInvalidos)}" });

                bool incluirPDF = incluirPDFStr == "true";
                bool incluirXML = incluirXMLStr == "true";

                if (!incluirPDF && !incluirXML)
                    return Json(new { success = false, message = "Debe seleccionar al menos un archivo para adjuntar." });

                string xmlBasePath = Path.Combine("~/Facturacion/xml_timbrados/");
                string pdfBasePath = Path.Combine("~/Facturacion/facturas/");
                string xmlPath = Path.Combine(xmlBasePath, $"{facturaId}.xml");
                string pdfPath = Path.Combine(pdfBasePath, $"{facturaId}.pdf");
                string pdfEnPath = Path.Combine(pdfBasePath, $"{facturaId}_EN.pdf");
                bool incluirPDFEN = Request.Form["incluirPDFEN"].ToString() == "true";

                bool pdfEnExists = incluirPDFEN && System.IO.File.Exists(pdfEnPath);
                bool xmlExists = incluirXML && System.IO.File.Exists(xmlPath);
                bool pdfExists = incluirPDF && System.IO.File.Exists(pdfPath);

                if (!xmlExists && !pdfExists)
                    return Json(new { success = false, message = "No se encontraron los archivos solicitados." });

                decimal total = 0;
                decimal.TryParse(totalStr, out total);
                decimal subtotal = total / 1.16m;
                decimal iva = total - subtotal;

                string baseUrl = $"{Request.Scheme}://{Request.Host}";

                var emailModel = new FacturaEmailModel
                {
                    UUID = facturaId,
                    Serie = serie,
                    Folio = folio,
                    FechaEmision = DateTime.Now,
                    Total = total,
                    Subtotal = subtotal,
                    IVA = iva,
                    EmisorRazonSocial = emisores[$"{perfil}.RazonSocial"],
                    EmisorRFC = emisores[$"{perfil}.Rfc"],
                    EmisorDireccion = emisores[$"{perfil}.Direccion"] ?? "Dirección no disponible",
                    EmisorTelefono = emisores[$"{perfil}.Telefono"] ?? "",
                    EmisorEmail = emisores[$"{perfil}.Correo"] ?? "facturacion@tuempresa.com",
                    EmisorLogoUrl = $"http://sellosyretenes.dynalias.com:9000/Content/img/{perfil}/logo.png",
                    ReceptorNombre = razonSocial,
                    ReceptorRFC = rfcCliente,
                    ReceptorEmail = emails.First(),
                    CantidadConceptos = 1,
                    ObservacionesVendedor = observaciones,
                    PdfUrl = pdfExists ? $"{baseUrl}/Facturacion/facturas/{facturaId}.pdf" : null,
                    XmlUrl = xmlExists ? $"{baseUrl}/Facturacion/xml_timbrados/{facturaId}.xml" : null,
                    FormaPago = "01 - Efectivo",
                    MetodoPago = "PUE - Pago en una sola exhibición",
                    UsoCFDI = "G03 - Gastos en general"
                };

                string htmlBody;
                try
                {
                    htmlBody = await _emailSender.RenderViewToStringAsync(
                        "~/Views/Email/_SendFacuraEmail.cshtml",
                        emailModel
                    );
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al generar el contenido del email: " + ex.Message });
                }

                // Cambia la llamada a EnviarCorreos para incluir los nuevos parámetros:
                return await EnviarCorreos(emails, emailModel, htmlBody, pdfExists, xmlExists, pdfEnExists, pdfPath, xmlPath, pdfEnPath, observaciones);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al enviar el correo.", error = ex.Message });
            }
        }

        // ============================================
        // ENVIAR EMAIL (desde consulta)
        // ============================================

        [HttpPost]
        public async Task<IActionResult> EnviarEmailDesdeConsulta()
        {
            try
            {
                string perfil = HttpContext.Session.GetString("EmpresaFactura");
                var emisores = _configuration.GetSection("emisores").Get<NameValueCollection>();

                string facturaId = Request.Form["facturaId"].ToString();
                string emailsString = Request.Form["emails"].ToString();
                string asunto = Request.Form["asunto"].ToString();
                string observaciones = Request.Form["mensaje"].ToString();
                string incluirPDFStr = Request.Form["incluirPDF"].ToString();
                string incluirXMLStr = Request.Form["incluirXML"].ToString();

                if (string.IsNullOrWhiteSpace(emailsString) || string.IsNullOrWhiteSpace(asunto))
                    return Json(new { success = false, message = "Asunto y destinatario son obligatorios." });

                if (string.IsNullOrWhiteSpace(facturaId))
                    return Json(new { success = false, message = "ID de factura es obligatorio." });

                var parametersFactura = new Dictionary<string, object>
                {
                    { "uuid", Guid.Parse(facturaId) }
                };

                string queryFactura = @"
                    SELECT f.serie, f.folio, f.total, f.rsocliente, f.rfccliente
                    FROM factura f
                    WHERE f.uuid = @uuid
                    LIMIT 1";

                var datosFactura = RunQuery(queryFactura, parametersFactura);

                if (datosFactura.Count == 0)
                    return Json(new { success = false, message = "No se encontraron datos de la factura." });

                string serie = datosFactura[0]["serie"]?.ToString() ?? "";
                string folio = datosFactura[0]["folio"]?.ToString() ?? "";
                string totalStr = datosFactura[0]["total"]?.ToString() ?? "0";
                string razonSocial = datosFactura[0]["rsocliente"]?.ToString() ?? "";
                string rfcCliente = datosFactura[0]["rfccliente"]?.ToString() ?? "";

                var emails = emailsString
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct()
                    .ToList();

                if (!emails.Any())
                    return Json(new { success = false, message = "No se proporcionaron correos válidos." });

                var emailsInvalidos = emails.Where(e => !EsEmailValido(e)).ToList();
                if (emailsInvalidos.Any())
                    return Json(new { success = false, message = $"Los siguientes correos tienen formato inválido: {string.Join(", ", emailsInvalidos)}" });

                bool incluirPDF = incluirPDFStr == "true";
                bool incluirXML = incluirXMLStr == "true";

                if (!incluirPDF && !incluirXML)
                    return Json(new { success = false, message = "Debe seleccionar al menos un archivo para adjuntar." });

                string xmlBasePath = Path.Combine("~/Facturacion/xml_timbrados/");
                string pdfBasePath = Path.Combine("~/Facturacion/facturas/");
                string xmlPath = Path.Combine(xmlBasePath, $"{facturaId}.xml");
                string pdfPath = Path.Combine(pdfBasePath, $"{facturaId}.pdf");
                string pdfEnPath = Path.Combine(pdfBasePath, $"{facturaId}_EN.pdf");
                bool incluirPDFEN = Request.Form["incluirPDFEN"].ToString() == "true";
                bool pdfEnExists = incluirPDFEN && System.IO.File.Exists(pdfEnPath);

                bool xmlExists = incluirXML && System.IO.File.Exists(xmlPath);
                bool pdfExists = incluirPDF && System.IO.File.Exists(pdfPath);

                if (!xmlExists && !pdfExists)
                    return Json(new { success = false, message = "No se encontraron los archivos solicitados." });

                decimal total = 0;
                decimal.TryParse(totalStr, out total);
                decimal subtotal = total / 1.16m;
                decimal iva = total - subtotal;

                string baseUrl = $"{Request.Scheme}://{Request.Host}";

                var emailModel = new FacturaEmailModel
                {
                    UUID = facturaId,
                    Serie = serie,
                    Folio = folio,
                    FechaEmision = DateTime.Now,
                    Total = total,
                    Subtotal = subtotal,
                    IVA = iva,
                    EmisorRazonSocial = emisores[$"{perfil}.RazonSocial"],
                    EmisorRFC = emisores[$"{perfil}.Rfc"],
                    EmisorDireccion = emisores[$"{perfil}.Direccion"] ?? "Dirección no disponible",
                    EmisorTelefono = emisores[$"{perfil}.Telefono"] ?? "",
                    EmisorEmail = emisores[$"{perfil}.Correo"] ?? "facturacion@tuempresa.com",
                    EmisorLogoUrl = $"http://sellosyretenes.dynalias.com:9000/Content/img/{perfil}/logo.png",
                    ReceptorNombre = razonSocial,
                    ReceptorRFC = rfcCliente,
                    ReceptorEmail = emails.First(),
                    CantidadConceptos = 1,
                    ObservacionesVendedor = observaciones,
                    PdfUrl = pdfExists ? $"{baseUrl}/Facturacion/facturas/{facturaId}.pdf" : null,
                    XmlUrl = xmlExists ? $"{baseUrl}/Facturacion/xml_timbrados/{facturaId}.xml" : null,
                    FormaPago = "01 - Efectivo",
                    MetodoPago = "PUE - Pago en una sola exhibición",
                    UsoCFDI = "G03 - Gastos en general"
                };

                string htmlBody;
                try
                {
                    htmlBody = await _emailSender.RenderViewToStringAsync(
                        "~/Views/Email/_SendFacuraEmail.cshtml",
                        emailModel
                    );
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al generar el contenido del email: " + ex.Message });
                }

                // Cambia la llamada a EnviarCorreos para incluir los nuevos parámetros:
                return await EnviarCorreos(emails, emailModel, htmlBody, pdfExists, xmlExists, pdfEnExists, pdfPath, xmlPath, pdfEnPath, observaciones);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al enviar el correo.", error = ex.Message });
            }
        }

        // ============================================
        // MÉTODO COMPARTIDO: ENVÍO DE CORREOS
        // ============================================

        private async Task<IActionResult> EnviarCorreos(
            List<string> emails,
            FacturaEmailModel emailModel,
            string htmlBody,
            bool pdfExists,
            bool xmlExists,
            bool pdfEnExists,          // ← NUEVO
            string pdfPath,
            string xmlPath,
            string pdfEnPath,          // ← NUEVO
            string observaciones)
        {
            int enviosExitosos = 0;
            var errores = new List<string>();

            foreach (var emailDestino in emails)
            {
                try
                {
                    using (MailMessage mail = new MailMessage())
                    {
                        mail.From = new MailAddress("BOS@sellosyretenes.com", emailModel.EmisorRazonSocial);
                        mail.To.Add(emailDestino);
                        mail.Subject = emailModel.UUID;
                        mail.Body = htmlBody;
                        mail.IsBodyHtml = true;
                        mail.Priority = MailPriority.High;

                        if (pdfExists)
                        {
                            var pdfStream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            mail.Attachments.Add(new Attachment(pdfStream, Path.GetFileName(pdfPath), "application/pdf"));
                        }

                        // ← NUEVO: adjuntar PDF en inglés si existe y fue solicitado
                        if (pdfEnExists)
                        {
                            var pdfEnStream = new FileStream(pdfEnPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            mail.Attachments.Add(new Attachment(pdfEnStream, Path.GetFileName(pdfEnPath), "application/pdf"));
                        }

                        if (xmlExists)
                        {
                            var xmlStream = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            mail.Attachments.Add(new Attachment(xmlStream, Path.GetFileName(xmlPath), "text/xml"));
                        }

                        using (var smtp = new SmtpClient())
                        {
                            await smtp.SendMailAsync(mail);
                        }

                        foreach (var att in mail.Attachments)
                            att.ContentStream.Dispose();

                        enviosExitosos++;
                    }
                }
                catch (Exception exEmail)
                {
                    errores.Add($"{emailDestino}: {exEmail.Message}");
                }
            }

            if (enviosExitosos == emails.Count)
                return Json(new { success = true, message = $"Correo enviado exitosamente a {enviosExitosos} destinatario(s).", enviadosA = emails, total = enviosExitosos, tieneObservaciones = !string.IsNullOrWhiteSpace(observaciones) });

            if (enviosExitosos > 0)
                return Json(new { success = true, message = $"Correo enviado a {enviosExitosos} de {emails.Count} destinatario(s).", enviadosA = emails.Take(enviosExitosos).ToList(), errores, parcial = true, tieneObservaciones = !string.IsNullOrWhiteSpace(observaciones) });

            return Json(new { success = false, message = "No se pudo enviar el correo a ningún destinatario.", errores });
        }

        // ============================================
        // OBTENER CORREOS DEL CLIENTE
        // ============================================

        [HttpGet]
        public JsonResult ObtenerCorreosCliente(string uuid)
        {
            try
            {
                var parameters = new Dictionary<string, object>();

                string queryCliente = @"
                    SELECT f.idcliente 
                    FROM factura f 
                    WHERE f.uuid = @uuid
                    LIMIT 1";

                parameters.Add("uuid", Guid.Parse(uuid));
                var resultCliente = RunQuery(queryCliente, parameters);

                int idCliente = 0;

                if (resultCliente.Count > 0 && Convert.ToInt32(resultCliente[0]["idcliente"]) != 0)
                {
                    idCliente = Convert.ToInt32(resultCliente[0]["idcliente"]);
                }
                else
                {
                    string queryClienteAlternativo = @"
                        SELECT c.id_cliente
                        FROM factura f 
                        INNER JOIN catclientes c ON c.rfc = f.rfccliente
                        WHERE f.uuid = @uuid
                        LIMIT 1";

                    parameters.Clear();
                    parameters.Add("uuid", Guid.Parse(uuid));
                    var resultAlternativo = RunQuery(queryClienteAlternativo, parameters);

                    if (resultAlternativo.Count > 0 && resultAlternativo[0]["id_cliente"] != DBNull.Value)
                        idCliente = Convert.ToInt32(resultAlternativo[0]["id_cliente"]);
                    else
                        return Json(new { success = false, message = "No se encontró el cliente relacionado con la factura" });
                }

                parameters.Clear();
                parameters.Add("cliente_id", idCliente);

                string queryCorreos = @"
                    SELECT id_correo_cli, cliente_id, correo 
                    FROM correos_cliente 
                    WHERE cliente_id = @cliente_id";

                var correosResult = RunQuery(queryCorreos, parameters);

                var correos = correosResult.Select(c => new
                {
                    id = c["id_correo_cli"],
                    correo = c["correo"].ToString()
                }).ToList();

                return Json(new { success = true, correos });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al obtener correos: " + ex.Message });
            }
        }

        // ============================================
        // CANCELAR FACTURA
        // ============================================

        /// <summary>
        /// Documentos cuyo movimiento de almacén debe revertirse al cancelar esta factura.
        /// Devuelve lista vacía cuando la cancelación es puramente fiscal.
        ///
        /// Tres filtros, en orden:
        ///  1. La naturaleza de la factura decide si aplica reponer (ReversionInventarioPolicy).
        ///  2. El origen sale de factura_remisiones_origen, que además guarda la cantidad.
        ///  3. Sólo para facturas viejas —las emitidas antes de que se registrara el origen—
        ///     se cae al documento padre, y nada más si de verdad movió almacén.
        /// </summary>
        [NonAction]
        private List<int> ResolverDocumentosParaRevertir(
            int idFacturaEncabezado, string uuid, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var vacio = new List<int>();

            string natFactura = RunScalar(
                "SELECT nat FROM encabezadomov WHERE id_encabezado = @id",
                new Dictionary<string, object> { { "id", idFacturaEncabezado } },
                false, conn, tx)?.ToString();

            if (!ReversionInventarioPolicy.DevuelveMercancia(natFactura, out string motivoPolitica))
            {
                LogErrorHelper.RegistrarLog("CancelacionFactura_Inventario", uuid,
                    $"Sin reversión de inventario — {motivoPolitica}", User.Identity?.Name, nivel: "INFO");
                return vacio;
            }

            var origenes = RunQuery(@"
                SELECT DISTINCT encabezado_remision_id
                FROM factura_remisiones_origen
                WHERE encabezado_factura_id = @facturaId
                ORDER BY encabezado_remision_id",
                new Dictionary<string, object> { { "facturaId", idFacturaEncabezado } },
                false, conn, tx)
                .Where(r => r["encabezado_remision_id"] != null)
                .Select(r => Convert.ToInt32(r["encabezado_remision_id"]))
                .ToList();

            // Respaldo para el histórico. El padre sólo se acepta si es una remisión y si
            // efectivamente tiene movimientos: apuntar a un pedido, a otra factura o a un
            // documento sin tarimas_mov haría que revertir_movimiento_inventario marcara
            // `revertido` y devolviera 0 sin reponer nada — un no-op silencioso.
            if (origenes.Count == 0)
            {
                var padre = RunQuery(@"
                    SELECT pa.id_encabezado, pa.nat,
                           EXISTS (SELECT 1 FROM tarimas_mov t
                                    WHERE t.encabezado_id = pa.id_encabezado) AS tiene_movimiento
                    FROM encabezadomov em
                    INNER JOIN encabezadomov pa ON pa.id_encabezado = em.encabezados_padre
                    WHERE em.id_encabezado = @id",
                    new Dictionary<string, object> { { "id", idFacturaEncabezado } },
                    false, conn, tx).FirstOrDefault();

                if (padre == null)
                {
                    LogErrorHelper.RegistrarLog("CancelacionFactura_Inventario", uuid,
                        $"Factura {idFacturaEncabezado} sin origen registrado y sin documento padre; " +
                        "no se revierte inventario.", User.Identity?.Name, nivel: "WARN");
                    return vacio;
                }

                string natPadre = padre["nat"]?.ToString();
                bool tieneMovimiento = Convert.ToBoolean(padre["tiene_movimiento"]);

                if (!ReversionInventarioPolicy.EsRemision(natPadre) || !tieneMovimiento)
                {
                    LogErrorHelper.RegistrarLog("CancelacionFactura_Inventario", uuid,
                        $"Factura {idFacturaEncabezado} sin origen registrado. El padre " +
                        $"{padre["id_encabezado"]} ({natPadre}) " +
                        (tieneMovimiento ? "no es una remisión" : "no tiene movimientos de almacén") +
                        "; no se revierte inventario.", User.Identity?.Name, nivel: "WARN");
                    return vacio;
                }

                origenes.Add(Convert.ToInt32(padre["id_encabezado"]));

                LogErrorHelper.RegistrarLog("CancelacionFactura_Inventario", uuid,
                    $"Factura {idFacturaEncabezado} sin origen registrado; se revierte por " +
                    $"documento padre {padre["id_encabezado"]} ({natPadre}).",
                    User.Identity?.Name, nivel: "INFO");
            }

            // Un documento ya revertido hace que revertir_movimiento_inventario lance
            // excepción, y eso abortaría la cancelación completa. Se descarta antes.
            var yaRevertidos = RunQuery(@"
                SELECT id_encabezado FROM encabezadomov
                WHERE id_encabezado = ANY(@ids) AND revertido = true",
                new Dictionary<string, object> { { "ids", origenes.ToArray() } },
                false, conn, tx)
                .Select(r => Convert.ToInt32(r["id_encabezado"]))
                .ToHashSet();

            if (yaRevertidos.Count > 0)
            {
                LogErrorHelper.RegistrarLog("CancelacionFactura_Inventario", uuid,
                    $"Documentos ya revertidos, se omiten: {string.Join(",", yaRevertidos)}.",
                    User.Identity?.Name, nivel: "INFO");
            }

            return origenes.Where(id => !yaRevertidos.Contains(id)).ToList();
        }

        [HttpPost]
        public IActionResult CancelarFacturaAjax(string rfcEmisor, string uuid, string motivo, string folioSustitucion = "")
        {
            try
            {
                var resultado = CancelarFactura(rfcEmisor, uuid, motivo, folioSustitucion);
                return Json(new { success = true, message = resultado });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        public JsonResult CancelarFactura(string rfcEmisor, string uuid, string motivo, string folioSustitucion = "")
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            Guid uuidGuid = Guid.Parse(uuid);

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                        string filtroTipo = GetFiltroTipoFactura();

                        // ─── 1. Verificar ownership + status actual (FOR UPDATE bloquea el row) ───
                        string queryVerifica = $@"
                    SELECT fa.subtotal, fa.iva, fa.total, fa.statusfactura
                    FROM factura fa
                    INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
                    WHERE fa.uuid = @uuid
                      AND em.suc = @suc
                      {filtroTipo}
                    LIMIT 1
                    FOR UPDATE";

                        var verificacion = RunQuery(queryVerifica, new Dictionary<string, object>
                {
                    { "uuid", uuidGuid },
                    { "suc", suc }
                }, false, conn, tx);

                        if (verificacion.Count == 0)
                        {
                            tx.Rollback();
                            return Json(new { success = false, message = "No tiene permisos para cancelar esta factura." });
                        }

                        string statusActual = verificacion[0]["statusfactura"].ToString();

                        if (statusActual == "Cancelada")
                        {
                            tx.Rollback();
                            return Json(new { success = true, message = "La factura ya se encontraba cancelada." });
                        }

                        // ─── 2. Resolver encabezado y datos relacionados ANTES de tocar nada ───
                        int idFacturaEncabezado = Convert.ToInt32(
                            RunScalar(
                                "SELECT encabezado_id FROM factura WHERE uuid = @uuid",
                                new Dictionary<string, object> { { "uuid", uuidGuid } },
                                false, conn, tx
                            )
                        );

                        // Qué documentos hay que devolver al almacén. No es "lo que haya en
                        // factura_remisiones_origen": depende de la naturaleza de la factura.
                        // Ver ResolverDocumentosParaRevertir.
                        var docsARevertir = ResolverDocumentosParaRevertir(
                            idFacturaEncabezado, uuid, conn, tx);

                        // ─── 3. Aplicar TODOS los updates locales — sin commit todavía ───
                        RunUpdate(
                            "UPDATE factura SET statusfactura='Cancelada' WHERE uuid = @uuid",
                            new Dictionary<string, object> { { "uuid", uuidGuid } },
                            false, conn, tx);

                        RunUpdate(
                            "UPDATE encabezadomov SET estatus_id=27 WHERE id_encabezado = @encId",
                            new Dictionary<string, object> { { "encId", idFacturaEncabezado } },
                            false, conn, tx);

                        RunUpdate(
                            "UPDATE polizas SET cancelada=true, estado='cancelada' WHERE referencia = @encId",
                            new Dictionary<string, object> { { "encId", idFacturaEncabezado } },
                            false, conn, tx);

                        var cobros = RunQuery(
                            "SELECT id_encabezado FROM encabezadomov WHERE encabezados_padre = @encabezados_padre AND nat = 'CXC'",
                            new Dictionary<string, object> { { "encabezados_padre", idFacturaEncabezado } },
                            false, conn, tx);

                        foreach (var c in cobros)
                        {
                            RunQuery(
                                "SELECT cancelar_cobro_cliente(@cobroId, @user)",
                                new Dictionary<string, object> { { "cobroId", c["id_encabezado"] }, { "user", GetUserId(User.Identity.Name) } },
                                false, conn, tx
                            );
                        }

                        foreach (int idDocumento in docsARevertir)
                            RevertirMovimientoInventario(idDocumento, GetUserId(User.Identity.Name), conn, tx);

                        RegistrarAuditoria("CANCELACION", uuid, User.Identity.Name, suc, motivo);

                        // ─── 4. Llamar al PAC — CON la transacción todavía abierta ───
                        // La sección "AppSettings" no existe en appsettings.json: ambas
                        // llegaban null y el PAC rechazaba la conexión. Están en "Timbrado".
                        var (timbradoUser, timbradoPass) = ObtenerCredencialesPac();

                        bool cancelacionExitosa = false;
                        string mensajePAC = null;
                        string acuseXml = null;

                        try
                        {
                            using (var client = new ServiceReference1.TimbradoServiceClient())
                            {
                                client.ClientCredentials.UserName.UserName = timbradoUser;
                                client.ClientCredentials.UserName.Password = timbradoPass;
                                client.Open();

                                var response = client.CancelarTest(rfcEmisor, uuid, motivo, folioSustitucion);

                                if (response == null)
                                {
                                    tx.Rollback();
                                    return Json(new { success = false, message = "No se recibió respuesta del servicio de cancelación." });
                                }

                                mensajePAC = response.message ?? "";
                                acuseXml = response.acuse ?? "";

                                cancelacionExitosa =
                                    mensajePAC.ToLower().Contains("satisfactoriamente") ||
                                    mensajePAC.ToLower().Contains("ya se encuentra cancelado") ||
                                    mensajePAC.ToLower().Contains("previously cancelled");
                            }
                        }
                        catch (Exception exPAC)
                        {
                            tx.Rollback();
                            LogErrorHelper.RegistrarLog("CancelacionFactura_PAC", uuid, exPAC.ToString(), User.Identity.Name);
                            return Json(new { success = false, message = "No se pudo contactar al PAC: " + exPAC.Message });
                        }

                        if (!cancelacionExitosa)
                        {
                            tx.Rollback();
                            LogErrorHelper.RegistrarLog("CancelacionFactura_PAC", uuid, $"PAC Error: {mensajePAC}", User.Identity.Name);
                            return Json(new { success = false, message = "El PAC devolvió un error: " + mensajePAC });
                        }

                        // ─── 5. PAC exitoso → guardar acuse físico + COMMIT ───
                        // Bajo wwwroot, que es lo que sirve Url.Content: antes se escribía
                        // en una carpeta "~" junto al ejecutable y el enlace de descarga
                        // apuntaba a un archivo inexistente.
                        string carpeta = CancelacionesPath;
                        Directory.CreateDirectory(carpeta);

                        string rutaArchivo = Path.Combine(carpeta, $"{uuid}_AcuseCancelacion.xml");
                        System.IO.File.WriteAllText(rutaArchivo, acuseXml);
                        string rutaWeb = Url.Content($"~/Facturacion/Cancelaciones/{uuid}_AcuseCancelacion.xml");

                        tx.Commit();

                        GenerarAcuseCancelacionPdf(uuid, rfcEmisor, motivo, folioSustitucion);

                        return Json(new
                        {
                            success = true,
                            message = "Cancelación exitosa.",
                            uuid,
                            acusePath = rutaWeb
                        });
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        LogErrorHelper.RegistrarLog("CancelacionFactura", uuid, ex.ToString(), User.Identity.Name);
                        return Json(new { success = false, message = "Error al cancelar la factura: " + ex.Message });
                    }
                }
            }
        }

        // ─── Helper: revertir estado intermedio cuando el PAC falla ──────────────────
        private void RevertirAEstadoOriginal(Guid uuid)
        {
            try
            {
                // El estatus que deja la refacturación es 'Pendiente Cancelación'
                // (RefacturacionOrchestrator). Aquí se buscaba 'Cancelación Pendiente' —las
                // palabras invertidas—, así que este UPDATE nunca encontraba nada y la
                // factura se quedaba en pendiente para siempre en vez de pasar a error.
                // Peor aún: ese estatus no figura en los NOT IN ('Cancelada','Error al
                // Cancelar') del resto del sistema, así que seguía contando como vigente.
                RunUpdate(
                    "UPDATE factura SET statusfactura='Error al Cancelar' " +
                    "WHERE uuid = @uuid AND statusfactura='Pendiente Cancelación'",
                    new Dictionary<string, object> { { "uuid", uuid } });
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog("CancelacionFactura_Revert", uuid.ToString(), ex.ToString(), "sistema");
            }
        }
        // ============================================
        // REGISTRAR CARGA AL PORTAL (PPD)
        // ============================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult RegistrarCargaPortal(string uuid, DateTime fechaCargaPortal, int diasCredito, string observaciones = "")
        {
            try
            {
                int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                string filtroTipo = GetFiltroTipoFactura();

                // Verificar que la factura es PPD y pertenece al usuario
                string queryVerifica = $@"
            SELECT fa.id, fa.mdpfactura, fa.total, fa.rsocliente
            FROM factura fa
            INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
            WHERE fa.uuid = @uuid 
              AND em.suc = @suc
              AND fa.statusfactura != 'Cancelada'
              AND (UPPER(fa.mdpfactura) = 'PPD' OR fa.mdpfactura = 'Pago en parcialidades o diferido')
              {filtroTipo}
            LIMIT 1";

                var factura = RunQuery(queryVerifica, new Dictionary<string, object>
        {
            { "uuid", Guid.Parse(uuid) },
            { "suc", suc }
        });

                if (factura.Count == 0)
                    return Json(new { success = false, message = "Factura no encontrada, no es PPD, o no tiene permisos." });

                // Insertar o actualizar la carga al portal
                string queryUpsert = @"
            INSERT INTO factura_credito 
                (factura_id, uuid, fecha_carga_portal, dias_credito, estatus_carga, usuario_registro, observaciones)
            VALUES 
                (@factura_id, @uuid, @fecha_carga_portal, @dias_credito, 'cargada', @usuario, @observaciones)
            ON CONFLICT (factura_id) 
            DO UPDATE SET
                fecha_carga_portal = EXCLUDED.fecha_carga_portal,
                dias_credito       = EXCLUDED.dias_credito,
                estatus_carga      = 'cargada',
                usuario_registro   = EXCLUDED.usuario_registro,
                observaciones      = EXCLUDED.observaciones,
                fecha_registro     = NOW()";

                RunUpdate(queryUpsert, new Dictionary<string, object>
        {
            { "factura_id", Convert.ToInt32(factura[0]["id"]) },
            { "uuid",       Guid.Parse(uuid) },
            { "fecha_carga_portal", fechaCargaPortal },
            { "dias_credito",       diasCredito },
            { "usuario",            User.Identity.Name },
            { "observaciones",      observaciones ?? "" }
        });

                RegistrarAuditoria("CARGA_PORTAL", uuid, User.Identity.Name, suc,
                    $"Fecha carga: {fechaCargaPortal:yyyy-MM-dd}, Días crédito: {diasCredito}");

                DateTime fechaVencimiento = fechaCargaPortal.AddDays(diasCredito);

                return Json(new
                {
                    success = true,
                    message = "Fecha de carga registrada correctamente.",
                    fechaCarga = fechaCargaPortal.ToString("yyyy-MM-dd"),
                    fechaVencimiento = fechaVencimiento.ToString("yyyy-MM-dd"),
                    diasRestantes = (int)(fechaVencimiento - DateTime.Now).TotalDays
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ============================================
        // OBTENER ESTADO DE CRÉDITO (PPD)
        // ============================================

        [HttpGet]
        public JsonResult ObtenerEstadoCredito(string uuid)
        {
            try
            {
                string query = @"
            SELECT 
                fc.fecha_carga_portal,
                fc.dias_credito,
                fc.fecha_carga_portal + (fc.dias_credito || ' days')::INTERVAL AS fecha_vencimiento,
                fc.estatus_carga,
                fc.observaciones,
                EXTRACT(DAY FROM (
                    fc.fecha_carga_portal + (fc.dias_credito || ' days')::INTERVAL - NOW()
                )) AS dias_restantes,
                f.total,
                f.saldo,
                f.rsocliente
            FROM factura_credito fc
            INNER JOIN factura f ON f.id = fc.factura_id
            WHERE fc.uuid = @uuid";

                var result = RunQuery(query, new Dictionary<string, object>
        {
            { "uuid", Guid.Parse(uuid) }
        });

                if (result.Count == 0)
                    return Json(new { success = false, message = "Sin datos de crédito registrados." });

                var r = result[0];
                int diasRestantes = Convert.ToInt32(r["dias_restantes"]);
                string semaforo = diasRestantes < 0 ? "vencida"
                                : diasRestantes <= 5 ? "por_vencer"
                                : "vigente";

                return Json(new
                {
                    success = true,
                    fechaCargaPortal = r["fecha_carga_portal"],
                    fechaVencimiento = r["fecha_vencimiento"],
                    diasCredito = r["dias_credito"],
                    diasRestantes,
                    semaforo,           // vigente | por_vencer | vencida
                    estatusCarga = r["estatus_carga"],
                    total = r["total"],
                    saldo = r["saldo"],
                    cliente = r["rsocliente"],
                    observaciones = r["observaciones"]
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ============================================
        // OBTENER DÍAS DE CRÉDITO DEL CLIENTE (para PPD)
        // ============================================

        [HttpGet]
        public JsonResult ObtenerDiasCredito(string uuid)
        {
            try
            {
                // Buscar días de crédito del cliente relacionado con la factura
                string query = @"
            SELECT 
                COALESCE(c.pl_crd, 0) AS pl_crd,
                c.n_cli,
                c.id_cliente,
                -- También traer si ya existe una carga previa registrada
                fc.fecha_carga_portal,
                fc.dias_credito        AS dias_carga_anterior,
                fc.estatus_carga
            FROM factura f
            LEFT JOIN catclientes c ON c.id_cliente = f.idcliente
            LEFT JOIN factura_credito fc ON fc.factura_id = f.id
            WHERE f.uuid = @uuid
            LIMIT 1";

                var result = RunQuery(query, new Dictionary<string, object>
        {
            { "uuid", Guid.Parse(uuid) }
        });

                if (result.Count == 0)
                    return Json(new { success = false, message = "No se encontró la factura." });

                var r = result[0];
                int plCrd = Convert.ToInt32(r["pl_crd"]);

                return Json(new
                {
                    success = true,
                    diasCredito = plCrd,                              // 0 = no configurado
                    tieneCredito = plCrd > 0,
                    cliente = r["n_cli"]?.ToString() ?? "",
                    // Carga previa si existe
                    cargaPrevia = r["fecha_carga_portal"] != null && r["fecha_carga_portal"] != DBNull.Value,
                    fechaCargaAnterior = r["fecha_carga_portal"] != DBNull.Value
                                       ? Convert.ToDateTime(r["fecha_carga_portal"]).ToString("yyyy-MM-dd")
                                       : null,
                    diasCargaAnterior = r["dias_carga_anterior"] != DBNull.Value
                                       ? Convert.ToInt32(r["dias_carga_anterior"])
                                       : (int?)null,
                    estatusCarga = r["estatus_carga"]?.ToString()
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private void RegistrarAuditoria(string accion, string referencia, string usuario, int sucursal, string detalle = "")
        {
            try
            {
                string query = @"
            INSERT INTO auditoria_facturas (accion, referencia, usuario, sucursal, detalle, fecha)
            VALUES (@accion, @referencia, @usuario, @sucursal, @detalle, NOW())";

                RunUpdate(query, new Dictionary<string, object>
        {
            { "accion",     accion },
            { "referencia", referencia },
            { "usuario",    usuario },
            { "sucursal",   sucursal },
            { "detalle",    detalle ?? "" }
        });
            }
            catch { /* No interrumpir el flujo principal si falla la auditoría */ }
        }

        // ============================================
        // HELPERS PRIVADOS
        // ============================================

        private bool EsEmailValido(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch { return false; }
        }

        // ============================================
        // EXPORTAR FACTURAS A CSV
        // ============================================

        [HttpGet]
        public IActionResult ExportarFacturasCSV(string nombre = "", string usuarioFiltro = "")
        {
            int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            string filtroTipo = GetFiltroTipoFactura();
            string filtroSuc = GetFiltroSucursal();

            string usuarioEfectivo = string.IsNullOrWhiteSpace(usuarioFiltro)
                ? User.Identity.Name
                : usuarioFiltro;

            var parameters = new Dictionary<string, object>
            {
                { "nombre",          nombre ?? "" },
                { "suc",             suc },
                { "usuarioFiltro",   usuarioEfectivo }
            };



            string query = $@"
        SELECT
            em.folio ||
                CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio,
            fa.serie,
            fa.folio          AS fac_folio,
            fa.rfccliente,
            fa.rsocliente,
            fa.fecha,
            fa.fechatimbrado,
            fa.total,
            fa.subtotal,
            fa.iva,
            fa.descuento,
            fa.saldo,
            fa.moneda,
            fa.statusfactura,
            fa.mdpfactura,
            fa.uuid,
            fa.tipo,
            em.usr_doc        AS usuario,
            em.suc            AS sucursal,
            fc.fecha_carga_portal,
            fc.dias_credito,
            CASE
                WHEN fc.fecha_carga_portal IS NULL THEN NULL
                ELSE CAST(EXTRACT(DAY FROM (
                    fc.fecha_carga_portal + (fc.dias_credito || ' days')::INTERVAL - NOW()
                )) AS INTEGER)
            END               AS dias_credito_restantes
        FROM factura fa
        INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
        LEFT  JOIN factura_credito fc ON fc.factura_id  = fa.id
        WHERE em.variacion = 0
          AND (
              LOWER(fa.serie)      LIKE LOWER('%' || @nombre || '%')
           OR LOWER(fa.rfccliente) LIKE LOWER('%' || @nombre || '%')
           OR LOWER(fa.rsocliente) LIKE LOWER('%' || @nombre || '%')
          )
          {filtroSuc}
          {filtroTipo}
        ORDER BY fa.id DESC";

            var filas = RunQuery(query, parameters);

            // ── Construir CSV ──────────────────────────────────────────────
            var sb = new System.Text.StringBuilder();

            // Encabezados
            sb.AppendLine(
                "Folio,Serie,Folio Factura,RFC Cliente,Razón Social Cliente," +
                "Fecha Emisión,Fecha Timbrado,Subtotal,IVA,Descuento,Total,Saldo," +
                "Moneda,Status,Método de Pago,UUID,Tipo,Usuario,Sucursal," +
                "Fecha Carga Portal,Días Crédito,Días Restantes"
            );

            foreach (var row in filas)
            {
                sb.AppendLine(string.Join(",", new[]
                {
            CsvCell(row["folio"]),
            CsvCell(row["serie"]),
            CsvCell(row["fac_folio"]),
            CsvCell(row["rfccliente"]),
            CsvCell(row["rsocliente"]),
            CsvCell(row["fecha"]),
            CsvCell(row["fechatimbrado"]),
            CsvCell(row["subtotal"]),
            CsvCell(row["iva"]),
            CsvCell(row["descuento"]),
            CsvCell(row["total"]),
            CsvCell(row["saldo"]),
            CsvCell(row["moneda"]),
            CsvCell(row["statusfactura"]),
            CsvCell(row["mdpfactura"]),
            CsvCell(row["uuid"]),
            CsvCell(row["tipo"]),
            CsvCell(row["usuario"]),
            CsvCell(row["sucursal"]),
            CsvCell(row["fecha_carga_portal"]),
            CsvCell(row["dias_credito"]),
            CsvCell(row["dias_credito_restantes"])
        }));
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetPreamble()  // BOM para Excel
                .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))
                .ToArray();

            string fileName = $"Facturas_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv", fileName);
        }

        // ── Helper: escapa y envuelve la celda en comillas ──────────────────
        private string CsvCell(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            string s = value.ToString().Replace("\"", "\"\"");   // escapa comillas internas
            return $"\"{s}\"";
        }


        // ============================================
        // ENVÍO MASIVO POR CLIENTE
        // ============================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarMasivoCliente()
        {
            try
            {
                string perfil = HttpContext.Session.GetString("EmpresaFactura");

                var emisores = _configuration.GetSection($"emisores:{perfil}");

                int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                string filtroTipo = GetFiltroTipoFactura();

                // IDs de facturas seleccionadas (UUIDs separados por coma)
                string uuidsRaw = Request.Form["uuids"].ToString();
                string asunto = Request.Form["asunto"].ToString();
                string mensaje = Request.Form["mensaje"].ToString();
                bool incluirPDF = Request.Form["incluirPDF"].ToString() == "true";
                bool incluirXML = Request.Form["incluirXML"].ToString() == "true";
                bool incluirPDFEN = Request.Form["incluirPDFEN"].ToString() == "true";

                // Correos adicionales ingresados manualmente (JSON array de strings)
                string correosAdicionalesRaw = Request.Form["correosAdicionales"].ToString();
                var correosAdicionales = string.IsNullOrWhiteSpace(correosAdicionalesRaw)
                    ? new List<string>()
                    : Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(correosAdicionalesRaw)
                    ?? new List<string>();

                if (string.IsNullOrWhiteSpace(uuidsRaw))
                    return Json(new { success = false, message = "No se seleccionaron facturas." });

                if (!incluirPDF && !incluirXML)
                    return Json(new { success = false, message = "Seleccione al menos un tipo de archivo." });

                var uuids = uuidsRaw
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim())
                    .Distinct()
                    .ToList();

                // ── Verificar que todas las facturas pertenecen a la sucursal/permisos ──
                var placeholders = string.Join(",", uuids.Select((_, i) => $"@uuid{i}"));
                var verifyParams = new Dictionary<string, object> { { "suc", suc } };
                for (int i = 0; i < uuids.Count; i++) verifyParams[$"uuid{i}"] = Guid.Parse(uuids[i]);

                string queryVerify = $@"
            SELECT fa.uuid::text, fa.serie, fa.folio, fa.total, fa.rsocliente,
                   fa.rfccliente, fa.emlcliente, fa.idcliente
            FROM factura fa
            INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
            WHERE fa.uuid IN ({placeholders})
              AND em.suc = @suc
              AND fa.statusfactura != 'Cancelada'
              {filtroTipo}";

                var facturas = RunQuery(queryVerify, verifyParams);

                if (facturas.Count == 0)
                    return Json(new { success = false, message = "No se encontraron facturas válidas." });

                // ── Obtener correos de cada cliente único ──────────────────────────────
                var idsCliente = facturas
                    .Select(f => Convert.ToInt32(f["idcliente"]))
                    .Distinct()
                    .Where(id => id > 0)
                    .ToList();

                Dictionary<int, List<string>> correosPorCliente = new Dictionary<int, List<string>>();

                if (idsCliente.Any())
                {
                    var correoPlaceholders = string.Join(",", idsCliente.Select((_, i) => $"@cid{i}"));
                    var correoParams = new Dictionary<string, object>();
                    for (int i = 0; i < idsCliente.Count; i++) correoParams[$"cid{i}"] = idsCliente[i];

                    string queryCorreos = $@"
                SELECT cliente_id, correo
                FROM correos_cliente
                WHERE cliente_id IN ({correoPlaceholders})
                ORDER BY cliente_id";

                    var correoRows = RunQuery(queryCorreos, correoParams);
                    foreach (var row in correoRows)
                    {
                        int cid = Convert.ToInt32(row["cliente_id"]);
                        string correo = row["correo"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(correo))
                        {
                            if (!correosPorCliente.ContainsKey(cid))
                                correosPorCliente[cid] = new List<string>();
                            correosPorCliente[cid].Add(correo);
                        }
                    }
                }

                // ── Agrupar facturas por cliente ──────────────────────────────────────
                var grupos = facturas
                    .GroupBy(f => f["rfccliente"]?.ToString() ?? "")
                    .ToList();

                string xmlBasePath = Path.Combine("~/Facturacion/xml_timbrados/");
                string pdfBasePath = Path.Combine("~/Facturacion/facturas/");
                string baseUrl = $"{Request.Scheme}://{Request.Host}";

                var resultados = new List<object>();
                int totalEnviados = 0;

                foreach (var grupo in grupos)
                {
                    var primeraFac = grupo.First();
                    string rfcCliente = grupo.Key;
                    string razonSocial = primeraFac["rsocliente"]?.ToString() ?? "";
                    int idCliente = Convert.ToInt32(primeraFac["idcliente"]);

                    // Correos destino: del catálogo + adicionales manuales
                    var correosDestino = new List<string>();
                    if (correosPorCliente.ContainsKey(idCliente))
                        correosDestino.AddRange(correosPorCliente[idCliente]);

                    // emlcliente como fallback
                    string emlCli = primeraFac["emlcliente"]?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(emlCli) && !correosDestino.Contains(emlCli))
                        correosDestino.Add(emlCli);

                    // Agregar correos adicionales manuales para TODOS los grupos
                    correosDestino.AddRange(correosAdicionales);
                    correosDestino = correosDestino
                        .Where(e => EsEmailValido(e))
                        .Distinct()
                        .ToList();

                    if (!correosDestino.Any())
                    {
                        resultados.Add(new
                        {
                            rfc = rfcCliente,
                            cliente = razonSocial,
                            facturas = grupo.Count(),
                            success = false,
                            message = "Sin correos registrados para este cliente."
                        });
                        continue;
                    }

                    // Construir lista de adjuntos para todas las facturas del grupo
                    var adjuntosPDF = new List<string>();
                    var adjuntosXML = new List<string>();
                    var adjuntosPDFEN = new List<string>();
                    decimal totalGrupo = 0;

                    foreach (var fac in grupo)
                    {
                        string uuid = fac["uuid"]?.ToString() ?? "";
                        totalGrupo += Convert.ToDecimal(fac["total"] ?? 0);

                        if (incluirPDF)
                        {
                            string p = Path.Combine(pdfBasePath, $"{uuid}.pdf");
                            if (System.IO.File.Exists(p)) adjuntosPDF.Add(p);
                        }
                        if (incluirXML)
                        {
                            string x = Path.Combine(xmlBasePath, $"{uuid}.xml");
                            if (System.IO.File.Exists(x)) adjuntosXML.Add(x);
                        }
                        if (incluirPDFEN)
                        {
                            string pe = Path.Combine(pdfBasePath, $"{uuid}_EN.pdf");
                            if (System.IO.File.Exists(pe)) adjuntosPDFEN.Add(pe);
                        }
                    }

                    if (!adjuntosPDF.Any() && !adjuntosXML.Any() && !adjuntosPDFEN.Any())
                    {
                        resultados.Add(new
                        {
                            rfc = rfcCliente,
                            cliente = razonSocial,
                            facturas = grupo.Count(),
                            success = false,
                            message = "No se encontraron archivos para adjuntar."
                        });
                        continue;
                    }

                    // Modelo de email consolidado
                    var primeraFactura = grupo.First();
                    var emailModel = new FacturaEmailModel
                    {
                        UUID = primeraFactura["uuid"]?.ToString() ?? "",
                        Serie = primeraFactura["serie"]?.ToString() ?? "",
                        Folio = primeraFactura["folio"]?.ToString() ?? "",
                        FechaEmision = DateTime.Now,
                        Total = totalGrupo,
                        Subtotal = totalGrupo / 1.16m,
                        IVA = totalGrupo - (totalGrupo / 1.16m),
                        EmisorRazonSocial = emisores[$"{perfil}.RazonSocial"],
                        EmisorRFC = emisores[$"{perfil}.Rfc"],
                        EmisorDireccion = emisores[$"{perfil}.Direccion"] ?? "Dirección no disponible",
                        EmisorTelefono = emisores[$"{perfil}.Telefono"] ?? "",
                        EmisorEmail = emisores[$"{perfil}.Correo"] ?? "facturacion@tuempresa.com",
                        EmisorLogoUrl = $"http://sellosyretenes.dynalias.com:9000/Content/img/{perfil}/logo.png",
                        ReceptorNombre = razonSocial,
                        ReceptorRFC = rfcCliente,
                        ReceptorEmail = correosDestino.First(),
                        CantidadConceptos = grupo.Count(),
                        ObservacionesVendedor = mensaje,
                        FormaPago = "Varios",
                        MetodoPago = "Varios",
                        UsoCFDI = "Varios"
                    };

                    string htmlBody;
                    try
                    {
                        htmlBody = await _emailSender.RenderViewToStringAsync(
                            "~/Views/Email/_SendFacuraEmail.cshtml",
                            emailModel
                        );
                    }
                    catch (Exception ex)
                    {
                        resultados.Add(new
                        {
                            rfc = rfcCliente,
                            cliente = razonSocial,
                            facturas = grupo.Count(),
                            success = false,
                            message = "Error generando HTML del email: " + ex.Message
                        });
                        continue;
                    }

                    // ── Enviar a todos los correos del grupo ────────────────────────────
                    int enviados = 0;
                    var errores = new List<string>();

                    foreach (var dest in correosDestino)
                    {
                        try
                        {
                            using (var mail = new MailMessage())
                            {
                                mail.From = new MailAddress("BOS@sellosyretenes.com", emailModel.EmisorRazonSocial);
                                mail.To.Add(dest);
                                mail.Subject = string.IsNullOrWhiteSpace(asunto)
                                    ? $"Facturas Electrónicas - {razonSocial}"
                                    : asunto;
                                mail.Body = htmlBody;
                                mail.IsBodyHtml = true;
                                mail.Priority = MailPriority.Normal;

                                foreach (var path in adjuntosPDF)
                                {
                                    var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                                    mail.Attachments.Add(new Attachment(s, Path.GetFileName(path), "application/pdf"));
                                }
                                foreach (var path in adjuntosPDFEN)
                                {
                                    var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                                    mail.Attachments.Add(new Attachment(s, Path.GetFileName(path), "application/pdf"));
                                }
                                foreach (var path in adjuntosXML)
                                {
                                    var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                                    mail.Attachments.Add(new Attachment(s, Path.GetFileName(path), "text/xml"));
                                }

                                using (var smtp = new SmtpClient())
                                    await smtp.SendMailAsync(mail);

                                foreach (var att in mail.Attachments) att.ContentStream.Dispose();
                                enviados++;
                            }
                        }
                        catch (Exception exMail) { errores.Add($"{dest}: {exMail.Message}"); }
                    }

                    totalEnviados += enviados;
                    RegistrarAuditoria("ENVIO_MASIVO", rfcCliente, User.Identity.Name, suc,
                        $"Facturas: {grupo.Count()}, Destinatarios: {enviados}/{correosDestino.Count}");

                    resultados.Add(new
                    {
                        rfc = rfcCliente,
                        cliente = razonSocial,
                        facturas = grupo.Count(),
                        enviados,
                        destinatarios = correosDestino.Count,
                        success = enviados > 0,
                        message = enviados == correosDestino.Count
                                    ? $"Enviado a {enviados} destinatario(s)"
                                    : enviados > 0
                                        ? $"Enviado parcialmente ({enviados}/{correosDestino.Count})"
                                        : "Falló el envío",
                        errores
                    });
                }

                return Json(new
                {
                    success = totalEnviados > 0,
                    message = $"Proceso completado. {totalEnviados} envío(s) realizados en {grupos.Count} cliente(s).",
                    resultados
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error inesperado: " + ex.Message });
            }
        }

        // ── Obtener resumen de facturas seleccionadas para el preview ─────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerResumenSeleccion()
        {
            try
            {
                string uuidsRaw = Request.Form["uuids"].ToString();
                int suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                string filtroTipo = GetFiltroTipoFactura();

                if (string.IsNullOrWhiteSpace(uuidsRaw))
                    return Json(new { success = false, message = "Sin UUIDs." });

                var uuids = uuidsRaw
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim()).Distinct().ToList();

                var placeholders = string.Join(",", uuids.Select((_, i) => $"@uuid{i}"));
                var pars = new Dictionary<string, object> { { "suc", suc } };
                for (int i = 0; i < uuids.Count; i++) pars[$"uuid{i}"] = Guid.Parse(uuids[i]);

                string query = $@"
            SELECT
                fa.uuid::text,
                fa.serie,
                fa.folio,
                fa.total,
                fa.rsocliente,
                fa.rfccliente,
                fa.idcliente,
                fa.emlcliente,
                fa.fecha,
                fa.moneda,
                COALESCE((
                    SELECT string_agg(correo, ', ')
                    FROM correos_cliente
                    WHERE cliente_id = fa.idcliente
                ), fa.emlcliente, '') AS correos_cliente
            FROM factura fa
            INNER JOIN encabezadomov em ON em.id_encabezado = fa.encabezado_id
            WHERE fa.uuid IN ({placeholders})
              AND em.suc = @suc
              AND fa.statusfactura != 'Cancelada'
              {filtroTipo}
            ORDER BY fa.rfccliente, fa.fecha DESC";

                var rows = RunQuery(query, pars);

                // Agrupar por cliente para el resumen
                var grupos = rows.GroupBy(r => r["rfccliente"]?.ToString() ?? "").Select(g => new
                {
                    rfc = g.Key,
                    cliente = g.First()["rsocliente"]?.ToString() ?? "",
                    idCliente = Convert.ToInt32(g.First()["idcliente"] ?? 0),
                    correos = (g.First()["correos_cliente"]?.ToString() ?? "")
                              .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                              .Distinct().Where(e => !string.IsNullOrWhiteSpace(e)).ToList(),
                    facturas = g.Select(f => new
                    {
                        uuid = f["uuid"]?.ToString(),
                        serie = f["serie"]?.ToString(),
                        folio = f["folio"]?.ToString(),
                        total = Convert.ToDecimal(f["total"] ?? 0),
                        fecha = f["fecha"]
                    }).ToList(),
                    totalGrupo = g.Sum(f => Convert.ToDecimal(f["total"] ?? 0)),
                    moneda = g.First()["moneda"]?.ToString() ?? "MXN"
                }).ToList();

                return Json(new { success = true, grupos, totalFacturas = rows.Count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        public static class AcuseCancelacionReader
        {
            // Recibe el string del acuse directamente, no la ruta del archivo
            public static AcuseCancelacion ComplementarDesdeXml(string acuseXmlContent, string uuid, string rfcEmisor, string motivo, string folioSustitucion)
            {
                var acuse = new AcuseCancelacion
                {
                    Uuid = uuid,
                    Motivo = motivo,
                    FolioSustitucion = folioSustitucion,
                    FechaCancelacion = DateTime.Now,
                    RfcEmisor = rfcEmisor // fallback por si el XML no lo trae
                };

                try
                {
                    XDocument xdoc = XDocument.Parse(acuseXmlContent); // ← Parse, no Load

                    var fecha = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "fechaField");
                    if (fecha != null && DateTime.TryParse(fecha.Value, out var fechaParsed))
                        acuse.FechaCancelacion = fechaParsed;

                    var rfcEmisorXml = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "rfcEmisorField");
                    if (rfcEmisorXml != null && !string.IsNullOrWhiteSpace(rfcEmisorXml.Value))
                        acuse.RfcEmisor = rfcEmisorXml.Value;

                    var acuseFolio = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "AcuseFolios");
                    if (acuseFolio != null)
                    {
                        var estatus = acuseFolio.Elements().FirstOrDefault(e => e.Name.LocalName == "estatusUUIDField");
                        if (estatus != null)
                            acuse.Estatus = estatus.Value;

                        var uuidNodo = acuseFolio.Elements().FirstOrDefault(e => e.Name.LocalName == "uUIDField");
                        if (uuidNodo != null && !string.IsNullOrWhiteSpace(uuidNodo.Value))
                            acuse.Uuid = uuidNodo.Value;
                    }

                    var selloSat = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "signatureValueField");
                    if (selloSat != null)
                        acuse.SelloSAT = selloSat.Value;

                    var keyName = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "keyNameField");
                    if (keyName != null)
                        acuse.NoCertificadoSAT = keyName.Value;

                    acuse.XmlAcuseRaw = acuseXmlContent;
                }
                catch (Exception ex)
                {
                    LogErrorHelper.RegistrarLog("AcuseCancelacionReader", uuid, ex.ToString(), "Sistema");
                }

                return acuse;
            }

            public static string EstatusTexto(string estatus)
            {
                switch (estatus)
                {
                    case "201": return "Cancelado sin aceptación";
                    case "202": return "Solicitud de cancelación aceptada";
                    case "203": return "Solicitud de cancelación rechazada";
                    default: return estatus ?? "";
                }
            }
        }


        public IActionResult GenerarAcuseCancelacionPdf(string uuid, string rfcEmisor, string motivo, string folioSustitucion = "")
        {
            if (string.IsNullOrWhiteSpace(uuid))
                return Content("UUID no válido.");

            string rutaXmlAcuse = Path.Combine(CancelacionesPath, $"{uuid}_AcuseCancelacion.xml");

            if (!System.IO.File.Exists(rutaXmlAcuse))
                return Content("No se encontró el XML del acuse de cancelación para este UUID.");

            // 🔹 Carpetas necesarias
            string contentPdfPath = ContentPdfPath;
            string acusesPdfPath = CancelacionesPdfPath;

            Directory.CreateDirectory(contentPdfPath);
            Directory.CreateDirectory(acusesPdfPath);

            // 1. Leer el CONTENIDO del XML (File.ReadAllText detecta el encoding por BOM automáticamente)
            string acuseXmlContent = System.IO.File.ReadAllText(rutaXmlAcuse);

            // 2. Complementar modelo desde el contenido del XML
            var acuse = AcuseCancelacionReader.ComplementarDesdeXml(acuseXmlContent, uuid, rfcEmisor, motivo, folioSustitucion);

            // 3. (Opcional) QR apuntando a la verificación de cancelación del SAT
            string rutaQrFisica = Path.Combine(acusesPdfPath, $"qr_{uuid}.png");
            GenerarQrAcuse(acuse, rutaQrFisica);
            acuse.RutaQr = Url.Content($"~/Facturacion/Cancelaciones/pdf/qr_{uuid}.png");

            // 4. Renderizar header
            //string headerHtml = RenderViewToString("HeaderAcuse", acuse);
            //string headerPath = Path.Combine(contentPdfPath, $"header_acuse_{uuid}.html");

            // Ruta completa, no el nombre: se resuelve con GetView, que espera la ruta del
            // archivo y NO recorre las carpetas de búsqueda como haría FindView.
            // Y se rinde con el HttpContext de la petición, no con el de EmailSender: esta
            // vista lee Context.Session para elegir el logo de la empresa.
            string headerHtml = RenderViewToStringAsync("~/Views/Shared/HeaderAcuse.cshtml", acuse)
                .GetAwaiter().GetResult();

            string headerPath = Path.Combine(contentPdfPath, $"header_acuse_{uuid}.html");

            System.IO.File.WriteAllText(headerPath, headerHtml, Encoding.UTF8);

            // 5. Configurar el PDF
            var pdf = new ViewAsPdf("AcuseCancelacionPdf", acuse)
            {
                PageSize = Rotativa.AspNetCore.Options.Size.A4,
                PageMargins = new Rotativa.AspNetCore.Options.Margins(45, 10, 20, 10),
                FileName = $"AcuseCancelacion_{uuid}.pdf",
                CustomSwitches =
                    $"--encoding utf-8 --header-html \"{headerPath}\" " +
                    "--header-spacing 5 " +
                    "--footer-center \"Página [page] de [toPage]\" " +
                    "--footer-line --footer-font-size 10"
            };

            // 6. Generar y guardar PDF
            string rutaPDF = Path.Combine(acusesPdfPath, $"{uuid}.pdf");

            Task<byte[]> pdfBytes = pdf.BuildFile(ControllerContext);
            System.IO.File.WriteAllBytes(rutaPDF, pdfBytes.Result);

            // Limpieza del header temporal
            System.IO.File.Delete(headerPath);

            return Content($"PDF de acuse de cancelación generado correctamente en: {rutaPDF}");
        }

        private void GenerarQrAcuse(AcuseCancelacion acuse, string rutaArchivo)
        {
            // QR de verificación de cancelación del SAT
            string url = $"https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx?id={acuse.Uuid}&re={acuse.RfcEmisor}&fe=";

            using (var qr = new QRCoder.QRCodeGenerator())
            {
                var datos = qr.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.Q);
                var qrCode = new QRCoder.QRCode(datos);
                using (var bitmap = qrCode.GetGraphic(20))
                {
                    bitmap.Save(rutaArchivo, ImageFormat.Png);
                }
            }
        }
    }
}