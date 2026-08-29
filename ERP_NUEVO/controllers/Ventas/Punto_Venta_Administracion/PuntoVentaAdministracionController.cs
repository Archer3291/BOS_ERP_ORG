using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Data;
using System.Globalization;
using System.Text;


namespace BOS_ERP.Controllers.Ventas.Punto_Venta_Administracion
{
    [Authorize]
    public partial class PuntoVentaAdministracionController : FacturacionVentaController
    {
        private readonly IConfiguration _configuration;
        private readonly BOS_ERP.Services.EmailSender _emailSender;

        // Roles que pueden autorizar movimientos de caja (tabla roles: 8, 1 y 5).
        private const string RolGerente = "Gerente";
        private const string RolAdministrador = "Administrador";
        private const string RolSuperAdministrador = "Super Administrador";

        // Tope diario de aperturas; la 3ra requiere autorización de un gerente.
        private const int MaxAperturasPorDia = 3;

        // encabezadomov.estatus_id de un documento cancelado (mismo valor que usa
        // CancelacionDocumentosController).
        private const int EstatusCancelado = 27;

        public PuntoVentaAdministracionController(
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            IConfiguration configuration,
            BOS_ERP.Services.EmailSender emailSender,
            XmlBuilderService xmlService)
            : base(timbradoOptions, viewEngine, tempDataProvider, env, xmlService)
        {
            _configuration = configuration;
            _emailSender = emailSender;
        }

        public IActionResult DatosGeneral()
        {
            var parameters = new Dictionary<string, object>();
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryFacturas = @"
SELECT
    ffp.metodo AS forma_pago,
    COALESCE(cfp.descripcion, 'SIN DESCRIPCIÓN') AS forma_pago_nombre,
    SUM(ffp.monto) AS total_monto
FROM factura_formas_pagos ffp
LEFT JOIN cat_f_pago cfp
    ON cfp.cve_sat = ffp.metodo
INNER JOIN encabezadomov e 
    ON e.id_encabezado = ffp.encabezado_id
WHERE e.suc = @suc
AND e.fch::date = CURRENT_DATE
GROUP BY
    ffp.metodo,
    cfp.descripcion
ORDER BY total_monto DESC;
            ";


            //agregar  WHEN em.tipo_proceso = 'ingreso' THEN em.imp   --si se toman en cuenta ingresos
            string queryIngresosRetiros = @"
            SELECT 
                SUM(
                    CASE                     
                        WHEN em.tipo_proceso = 'retiro' THEN em.imp
                        ELSE 0
                    END
                ) AS total_dinero
            FROM encabezadomov em
            WHERE em.suc = @suc
            AND em.fch::date = CURRENT_DATE;
            ";


            string queryFondoInicial = @"
            SELECT  
                em.id_encabezado as id,
                em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END as folio,
                em.tipo_proceso as type,
                cs.cve_sucursal as cajero,
                em.fch as fecha,
                em.imp as montoinicial
            FROM encabezadomov em
            INNER JOIN catsucursales cs ON cs.id_sucursal = em.suc
            WHERE em.nat = 'ACAJA'
              AND em.suc = @suc
            AND em.fch::date = CURRENT_DATE
            ORDER BY em.fch DESC
            limit 1
            ";
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            result.Add("facturas", RunQuery(queryFacturas, parameters));
            result.Add("ingresosRetiros", RunQuery(queryIngresosRetiros, parameters));
            result.Add("fondoInicial", RunQuery(queryFondoInicial, parameters));


            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ObtenerTodosDocumentosFacturables()
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string queryEncabezado = @"
            SELECT  
                em.folio ||
                    CASE WHEN em.variacion > 0  
                        THEN '-' || num_to_letters(em.variacion)  
                        ELSE '' END AS folio,
                em.id_encabezado,
                em.encabezados_padre,
                em.suc,
                em.alm,
                em.gen,
                em.nat,
                em.usr0,
                em.usr1,
                em.fch,
                em.fch0,
                em.fch1,
                -- Para que la pantalla pueda señalar los documentos rezagados.
                (CURRENT_DATE - em.fch::date) AS dias_rezago,
                em.cli_prov,
                em.coment1,
                em.coment_aut,
                em.ccy,
                em.imp,
                em.vdr_cpr,
                em.flete,
                em.incoterm,
                em.mdp,
                em.f_pago,
                em.cfdi,
                em.tipo_proceso,
                cc.rfc,
                em.par,
                em.encabezado_hijo,
                cc.lim_crd,
                cc.n_cli,
                cc.pl_crd,
                cc.dir,
                -- El CP con el que se timbra sale de catclientes, no de la dirección de
                -- facturación: es el que lee PrepararFacturaDesdeDocs.
                cc.cp AS cp_cliente,
                cocl.correo,
                df.forma_pago, 
                df.razon_social, 
                df.uso_sugerido, 
                df.regimen_fiscal, 
                df.calle, 
                df.no_exterior, 
                df.no_interior, 
                df.colonia, 
                df.localidad, 
                df.municipio, 
                df.estado, 
                df.pais, 
                df.codigo_postal,
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli
            FROM encabezadomov em
            LEFT JOIN catclientes cc  
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df 
                ON df.entidad_clave = cc.cve_cli
            LEFT JOIN (
                SELECT DISTINCT ON (cliente_id) cliente_id, correo
                FROM correos_cliente
                WHERE correo IS NOT NULL
                ORDER BY cliente_id, id_correo_cli ASC   -- elige el correo con menor id (el primero)
            ) cocl ON cocl.cliente_id = cc.id_cliente
            WHERE em.nat = 'VSUC'
            AND em.suc = @suc
            AND em.variacion = 0
            -- Una venta cancelada no se factura. La rama de rezagados ya lo evitaba al
            -- exigir estatus_id = 1, pero la de las ventas del día no filtraba nada: las
            -- canceladas entraban al corte y se iban dentro de la global.
            AND em.estatus_id <> @estatus_cancelado
            AND (
                  -- lo de hoy, facturado o no (la vista usa el resto para el conteo
                  -- de 'ya facturados')
                  em.fch::date = CURRENT_DATE
                  -- ...más los rezagados de cualquier fecha: con el filtro anterior una
                  -- venta capturada a las 23:58 quedaba infacturable para siempre a
                  -- partir de las 00:00. Se exige estatus_id = 1 (abierto) además de
                  -- encabezado_hijo NULL, para no arrastrar documentos en otros estados.
                  OR (em.encabezado_hijo IS NULL AND em.estatus_id = 1)
                )
            ORDER BY em.fch;
        ";

                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                parameters.Add("estatus_cancelado", EstatusCancelado);
                var encabezadoResult = RunQuery(queryEncabezado, parameters);

                if (encabezadoResult == null || encabezadoResult.Count == 0)
                {
                    return Json(new { success = true, data = new List<object>() });
                }

                ClasificarDestinoFacturacion(encabezadoResult);

                return Json(new { success = true, data = encabezadoResult });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoVentaAdministracion/ObtenerTodosDocumentosFacturables");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Clave SAT del efectivo: la única forma de pago que puede irse dentro de la
        // factura global, porque es la única que la global sabe declarar.
        private const string ClaveSatEfectivo = "01";

        /// <summary>
        /// Decide a dónde va el CFDI de cada venta del corte y por qué.
        ///
        /// La regla: la factura GLOBAL agrupa sólo las ventas cobradas 100% en efectivo.
        /// Cualquier otra forma de pago se timbra en su propio comprobante a público en
        /// general, porque un CFDI declara UNA sola FormaPago y la global la declara
        /// siempre como efectivo (ResolverReceptor): meter ahí una venta con tarjeta
        /// hacía que el comprobante dijera que ese dinero entró en efectivo.
        ///
        /// Una venta mixta (efectivo + otro método) va COMPLETA a su comprobante
        /// individual, con la forma de pago del importe mayor. No se parte: las partidas
        /// de un ticket no se pueden repartir entre dos CFDIs sin inventar importes que
        /// ya no cuadran ni con la venta ni con el inventario.
        /// </summary>
        private void ClasificarDestinoFacturacion(List<Dictionary<string, object>> documentos)
        {
            var ids = documentos
                .Select(d => GetInt(d["id_encabezado"], 0) ?? 0)
                .Where(id => id > 0)
                .ToArray();

            // Desglose de cobros por documento. Se trae aparte y no como JSON dentro de
            // la consulta principal para no tener que reparsearlo aquí ni en la pantalla.
            var pagos = ids.Length == 0
                ? new List<Dictionary<string, object>>()
                : RunQuery(@"
                    SELECT ffp.encabezado_id,
                           ffp.metodo,
                           COALESCE(cfp.descripcion, ffp.metodo_nombre) AS nombre,
                           ffp.monto
                    FROM factura_formas_pagos ffp
                    LEFT JOIN cat_f_pago cfp ON cfp.cve_sat = ffp.metodo
                    WHERE ffp.encabezado_id = ANY(@ids)
                    ORDER BY ffp.monto DESC;",
                    new Dictionary<string, object> { { "ids", ids } });

            var pagosPorDocumento = pagos
                .GroupBy(r => GetInt(r["encabezado_id"], 0) ?? 0)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var doc in documentos)
            {
                int id = GetInt(doc["id_encabezado"], 0) ?? 0;
                var cobros = pagosPorDocumento.TryGetValue(id, out var lista)
                    ? lista
                    : new List<Dictionary<string, object>>();

                doc["formas_pago_detalle"] = cobros.Select(c => new
                {
                    metodo = c["metodo"]?.ToString(),
                    nombre = c["nombre"]?.ToString(),
                    monto = Convert.ToDecimal(c["monto"] == DBNull.Value ? 0m : c["monto"])
                }).ToList();

                string tipoProceso = doc["tipo_proceso"]?.ToString() ?? "";
                bool esCredito = tipoProceso.Contains("_credito", StringComparison.OrdinalIgnoreCase);
                bool yaFacturado = doc["encabezado_hijo"] != null && doc["encabezado_hijo"] != DBNull.Value;

                doc["es_credito"] = esCredito;

                // El orden de las ramas importa: una venta a crédito ya nació facturada,
                // así que se explica por lo que ES y no por "ya está facturada".
                if (esCredito)
                {
                    doc["destino"] = "credito";
                    doc["forma_pago_clave"] = null;
                    doc["forma_pago_nombre"] = "A crédito";
                    doc["motivo"] = "Venta a crédito: se facturó nominativa al registrarse y su importe " +
                                    "está en la cartera del cliente. No entra al corte ni suma a la caja.";
                    continue;
                }

                if (yaFacturado)
                {
                    doc["destino"] = "facturado";
                    doc["forma_pago_clave"] = null;
                    doc["forma_pago_nombre"] = "";
                    doc["motivo"] = "Ya tiene factura.";
                    continue;
                }

                // Forma de pago predominante: la del importe mayor. Sin desglose se cae al
                // f_pago del documento, que es justo esa misma (lo fija el punto de venta).
                var predominante = cobros.FirstOrDefault();
                string claveSat = predominante?["metodo"]?.ToString();
                string nombrePago = predominante?["nombre"]?.ToString();

                if (predominante == null)
                {
                    var delDocumento = RunQuery(
                        "SELECT cve_sat, descripcion FROM cat_f_pago WHERE id_f_pago = @id LIMIT 1;",
                        new Dictionary<string, object> { { "id", GetInt(doc["f_pago"], 0) ?? 0 } })
                        .FirstOrDefault();

                    claveSat = delDocumento?["cve_sat"]?.ToString()?.Trim();
                    nombrePago = delDocumento?["descripcion"]?.ToString();
                }

                bool todoEfectivo = cobros.Count > 0
                    ? cobros.All(c => (c["metodo"]?.ToString() ?? "").Trim() == ClaveSatEfectivo)
                    : claveSat == ClaveSatEfectivo;

                doc["forma_pago_clave"] = claveSat;
                doc["forma_pago_nombre"] = nombrePago ?? "Sin forma de pago";

                if (todoEfectivo)
                {
                    doc["destino"] = "global";
                    doc["motivo"] = "Cobrada en efectivo: entra en la factura global del día.";
                }
                else if (cobros.Count > 1)
                {
                    doc["destino"] = "individual";
                    doc["motivo"] = "Pago mixto (" +
                                    string.Join(" + ", cobros.Select(c => c["nombre"]?.ToString())) +
                                    "). Un CFDI sólo declara una forma de pago, así que se timbra completa " +
                                    $"como VSFAC a público en general con la predominante: {nombrePago}.";
                }
                else if (string.IsNullOrWhiteSpace(claveSat))
                {
                    doc["destino"] = "individual";
                    doc["motivo"] = "No se pudo determinar la forma de pago de esta venta; se timbra aparte " +
                                    "para no declararla como efectivo dentro de la global.";
                }
                else
                {
                    doc["destino"] = "individual";
                    doc["motivo"] = $"Cobrada con {nombrePago}: no puede ir en la global, que declara todo " +
                                    "como efectivo. Se timbra como VSFAC a público en general con su propia forma de pago.";
                }
            }
        }

        public IActionResult DatosSelect()
        {
            var parameters = new Dictionary<string, object>();
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryFacturas = @"
            SELECT
                em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0
                        THEN '-' || num_to_letters(em.variacion)
                        ELSE '' END AS folio,
                em.id_encabezado,
                em.encabezados_padre,
                em.suc,
                em.alm,
                em.gen,
                em.nat,
                em.usr0,
                em.usr1,
                em.fch0,
                em.fch1,
                em.cli_prov,
                em.coment1,
                em.coment_aut,
                em.ccy,
                em.imp,
                em.vdr_cpr,
                em.flete,
                em.incoterm,
                em.mdp,
                em.f_pago,
                -- Clave SAT de la forma de pago del documento. Es el respaldo del corte
                -- cuando una venta no tiene desglose de cobros; `mdp` no sirve para eso
                -- porque es PUE/PPD, el método de pago, no la forma.
                (SELECT cve_sat FROM cat_f_pago WHERE id_f_pago = em.f_pago) AS f_pago_cve_sat,
                em.cfdi,
                -- El corte lo usa para no contar como efectivo una venta a crédito, que
                -- no pasó por la caja.
                em.tipo_proceso,
                cc.rfc,
                em.par,
                em.encabezado_hijo,
                cc.lim_crd,
                cc.n_cli,
                cc.pl_crd,
                cc.dir,
                cocl.correo,
                df.forma_pago,
                df.razon_social,
                df.uso_sugerido,
                df.regimen_fiscal,
                df.calle,
                df.no_exterior,
                df.no_interior,
                df.colonia,
                df.localidad,
                df.municipio,
                df.estado,
                df.pais,
                df.codigo_postal,
            
                -- 🔥 FORMAS DE PAGO AGRUPADAS (JSON ARRAY)
                fp.formas_pago,
            
                -- INFO CLIENTE
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli,
            
                -- PRODUCTOS CONCATENADOS
                prod.productos_lista,
                prod.productos_codigos,
                prod.productos_descripciones
            
            FROM encabezadomov em
            
            LEFT JOIN catclientes cc ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
            
            LEFT JOIN (
                SELECT DISTINCT ON (cliente_id) cliente_id, correo
                FROM correos_cliente
                WHERE correo IS NOT NULL
                ORDER BY cliente_id, id_correo_cli ASC
            ) cocl ON cocl.cliente_id = cc.id_cliente
            
            -- 🔥 SUBCONSULTA PARA AGRUPAR FORMAS DE PAGO
            LEFT JOIN (
                SELECT
                    ffp.encabezado_id,
                    JSON_AGG(
                        JSON_BUILD_OBJECT(
                            'metodo', ffp.metodo,
                            'metodo_nombre', COALESCE(cfp.descripcion, ffp.metodo_nombre),
                            'monto', ffp.monto
                        ) ORDER BY ffp.monto DESC
                    ) AS formas_pago
                FROM factura_formas_pagos ffp
                LEFT JOIN cat_f_pago cfp ON cfp.cve_sat = ffp.metodo
                GROUP BY ffp.encabezado_id
            ) fp ON fp.encabezado_id = em.id_encabezado
            
            -- SUBCONSULTA PARA AGRUPAR PRODUCTOS
            LEFT JOIN (
                SELECT
                    encabezado_id,
                    STRING_AGG(cve_prod || ' - ' || descr_prod, ', ' ORDER BY id_partidas) AS productos_lista,
                    STRING_AGG(cve_prod, ', ' ORDER BY id_partidas) AS productos_codigos,
                    STRING_AGG(descr_prod, ', ' ORDER BY id_partidas) AS productos_descripciones
                FROM partidasdoc
                GROUP BY encabezado_id
            ) prod ON prod.encabezado_id = em.id_encabezado
            
            WHERE em.nat = 'VSUC'
            AND em.variacion = 0
            AND em.suc = @suc
            -- Igual que arriba: una venta cancelada no entró a la caja, así que no puede
            -- sumar en el desglose de formas de pago del corte.
            AND em.estatus_id <> @estatus_cancelado
            AND em.fch >= CURRENT_DATE
            AND em.fch < CURRENT_DATE + INTERVAL '1 day';
            ";

            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("estatus_cancelado", EstatusCancelado);
            result.Add("facturas", RunQuery(queryFacturas, parameters));


            return Json(result);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Retiro de efectivo de caja")]
        public JsonResult DocumentoRetiro(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();

                string concept = fc["concept"].ToString();
                string notes = fc["notes"].ToString();

                if (!decimal.TryParse(fc["amount"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal total))
                    return Json(new { success = false, message = "El importe del movimiento no es válido." });

                if (total <= 0)
                    return Json(new { success = false, message = "El importe del movimiento debe ser mayor a cero." });

                if (string.IsNullOrWhiteSpace(concept))
                    return Json(new { success = false, message = "Debe capturar el concepto del movimiento." });

                string query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, " +
                    "u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "u2.usuarioid AS managerid, u2.nombre || ' ' || u2.apellido AS nombreManager, " +
                    "u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios WHERE rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";

                parameters.Add("userName", User.Identity.Name);
                // FirstOrDefault, no [0]: sin gerente en el área esto reventaba antes
                // de poder devolver el mensaje de abajo.
                var userResult = RunQuery(query, parameters).FirstOrDefault();

                if (userResult == null || userResult.Count == 0)
                {
                    return Json(new { success = false, message = "No se encontró un gerente para esta área." });
                }

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 62,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "RET",
                    ComentAut = concept,
                    Coment1 = notes,
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Firma0 = fc["firmaMovimiento"].ToString(),
                    Usr1 = Convert.ToInt32(userResult["managerid"]),
                    Imp = total,
                    CliProv = "srs",
                    Estatus = 1,
                    TipoPoceso = fc["type"].ToString()
                };

                var partidas = new List<PartidaDocumento>();

                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                return Json(new
                {
                    success = true,
                    message = "Movimiento registrado correctamente",
                    folio = folio["folio_generado"].ToString()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoVentaAdministracion/DocumentoRetiro");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        public class DenominacionItem
        {
            public decimal Valor { get; set; }
            public int Cantidad { get; set; }
            public decimal Subtotal { get; set; }
        }

        /// <summary>
        /// Lee las denominaciones del form recalculando SIEMPRE el subtotal como
        /// valor × cantidad. El subtotal que manda el navegador es informativo:
        /// aceptarlo permitía declarar un fondo de caja que no correspondía al
        /// efectivo realmente contado.
        /// </summary>
        private static (List<DenominacionItem> Denominaciones, decimal Total, string Error) LeerDenominaciones(IFormCollection fc)
        {
            var denominaciones = new List<DenominacionItem>();
            decimal total = 0m;
            int i = 0;

            while (fc.ContainsKey($"denominaciones[{i}].valor"))
            {
                if (!decimal.TryParse(fc[$"denominaciones[{i}].valor"], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valor) ||
                    !int.TryParse(fc[$"denominaciones[{i}].cantidad"], NumberStyles.Any, CultureInfo.InvariantCulture, out int cantidad))
                    return (null, 0m, $"La denominación #{i + 1} tiene un formato inválido.");

                if (valor <= 0)
                    return (null, 0m, $"La denominación #{i + 1} debe tener un valor mayor a cero.");

                if (cantidad < 0)
                    return (null, 0m, $"La denominación de ${valor} no puede tener cantidad negativa.");

                if (cantidad > 0)
                {
                    decimal subtotal = valor * cantidad;
                    denominaciones.Add(new DenominacionItem { Valor = valor, Cantidad = cantidad, Subtotal = subtotal });
                    total += subtotal;
                }

                i++;
            }

            if (denominaciones.Count == 0)
                return (null, 0m, "Debe capturar al menos una denominación.");

            return (denominaciones, total, null);
        }

        /// <summary>
        /// Convierte denominaciones ya validadas en partidas, recalculando el subtotal.
        /// </summary>
        private static (List<PartidaDocumento> Partidas, decimal Total, string Error) ConstruirPartidasDeDenominaciones(List<DenominacionItem> denominaciones)
        {
            if (denominaciones == null || denominaciones.Count == 0)
                return (null, 0m, "La solicitud no tiene denominaciones registradas.");

            var partidas = new List<PartidaDocumento>();
            decimal total = 0m;

            foreach (var d in denominaciones)
            {
                if (d.Valor <= 0 || d.Cantidad < 0)
                    return (null, 0m, "La solicitud contiene denominaciones inválidas.");

                if (d.Cantidad == 0) continue;

                decimal subtotal = d.Valor * d.Cantidad;
                total += subtotal;

                partidas.Add(new PartidaDocumento
                {
                    CveProd = "Apertura",
                    DescrProd = $"BILLETE/MONEDA ${d.Valor}",
                    CantUd = d.Cantidad,
                    PvProd = d.Valor,
                    ImpPart = subtotal
                });
            }

            if (partidas.Count == 0)
                return (null, 0m, "La solicitud no tiene denominaciones con cantidad mayor a cero.");

            return (partidas, total, null);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Apertura de Caja")]
        public JsonResult DocumentoAperturaCaja(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();

                string queryConteo = @"
            SELECT COUNT(*) as total FROM encabezadomov em
            WHERE em.nat = 'ACAJA' AND em.suc = @suc AND em.fch::date = CURRENT_DATE";
                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                var conteoResult = RunQuery(queryConteo, parameters);
                int totalAperturas = conteoResult != null && conteoResult.Count > 0
                    ? Convert.ToInt32(conteoResult[0]["total"]) : 0;

                if (totalAperturas >= MaxAperturasPorDia)
                    return Json(new { success = false, message = $"Ya se alcanzó el límite de {MaxAperturasPorDia} aperturas por día." });

                bool esAprobacion = (totalAperturas == 2); // la que se va a registrar será la 3ra

                // ── Denominaciones: el monto lo define el conteo, no el form ───────
                var (denominaciones, total, errorDenom) = LeerDenominaciones(fc);
                if (errorDenom != null)
                    return Json(new { success = false, message = errorDenom });

                decimal montoDeclarado = decimal.TryParse(fc["montoInicial"].ToString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out decimal md) ? md : -1m;

                if (montoDeclarado >= 0 && Math.Abs(montoDeclarado - total) > 0.05m)
                    return Json(new
                    {
                        success = false,
                        message = $"El monto inicial declarado ({montoDeclarado:N2}) no coincide con la suma de las denominaciones ({total:N2})."
                    });

                string query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, " +
                    "u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "u2.usuarioid AS managerid, u2.nombre || ' ' || u2.apellido AS nombreManager, " +
                    "u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios WHERE rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";

                parameters.Add("userName", User.Identity.Name);
                // FirstOrDefault, no [0]: sin gerente en el área esto lanzaba
                // ArgumentOutOfRangeException y el mensaje amable de abajo era inalcanzable.
                var userResult = RunQuery(query, parameters).FirstOrDefault();

                if (userResult == null || userResult.Count == 0)
                    return Json(new { success = false, message = "No se encontró un gerente para esta área." });

                string firmaGerente = esAprobacion ? fc["firmaMovimiento"].ToString() : null;

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 64,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "ACAJA",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Usr1 = Convert.ToInt32(userResult["managerid"]),
                    Imp = total,
                    CliProv = "srs",
                    Estatus = 1,
                    Firma0 = firmaGerente,   // ← se guarda null en 1ra/2da, firma en 3ra
                    TipoPoceso = esAprobacion ? "apertura_caja_aprobada" : "apertura_caja"
                };

                // ── Construir partidas con cada denominación ──────────────────────
                var (partidas, _, errorPartidas) = ConstruirPartidasDeDenominaciones(denominaciones);
                if (errorPartidas != null)
                    return Json(new { success = false, message = errorPartidas });

                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                return Json(new
                {
                    success = true,
                    message = "Movimiento registrado correctamente",
                    folio = folio["folio_generado"].ToString()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoVentaAdministracion/DocumentoAperturaCaja");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }
        public IActionResult ObtenerDocumentos(string folio)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                var result = new Dictionary<string, List<Dictionary<string, object>>>();
                string query = @"
SELECT  
    em.id_encabezado as id,
    em.folio ||
        CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END as nombre,
    em.tipo_proceso as type,
    em.imp as amount,
    em.suc,
    em.fch as date,
    em.usr_doc as user,
    em.coment_aut as concept,
    em.coment1 as notes
FROM encabezadomov em
WHERE em.nat = 'RET'
AND em.suc = @suc
AND em.fch::date = CURRENT_DATE
ORDER BY em.fch DESC";
                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                result.Add("movements", RunQuery(query, parameters));

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoVentaAdministracion/ObtenerDocumentos");
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
        public JsonResult VerificarAperturaDelDia()
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string query = @"
SELECT  
    em.id_encabezado as id,
    em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
        CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END as folio,
    em.tipo_proceso as type,
    cs.cve_sucursal as cajero,
    em.fch as fecha,
    em.imp as montoinicial
FROM encabezadomov em
INNER JOIN catsucursales cs ON cs.id_sucursal = em.suc
WHERE em.nat = 'ACAJA'
  AND em.suc = @suc
  AND em.fch::date = CURRENT_DATE
ORDER BY em.fch DESC";

                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                var filas = RunQuery(query, parameters);
                int totalAperturas = filas?.Count ?? 0;

                if (totalAperturas == 0)
                    return Json(new { existe = false, totalAperturas = 0 });

                // ── Construir lista de aperturas con sus denominaciones ──
                var aperturas = new List<object>();

                foreach (var fila in filas)
                {
                    int idEncabezado = 0;
                    if (fila.TryGetValue("id", out var vId) && vId != null)
                        int.TryParse(vId.ToString(), out idEncabezado);

                    string folio = fila.TryGetValue("folio", out var vFolio) && vFolio != null ? vFolio.ToString() : null;
                    decimal montoInicial = 0m;
                    if (fila.TryGetValue("montoinicial", out var vMonto) && vMonto != null)
                        decimal.TryParse(vMonto.ToString(), out montoInicial);
                    string cajero = fila.TryGetValue("cajero", out var vCajero) && vCajero != null ? vCajero.ToString() : null;
                    DateTime? fecha = null;
                    if (fila.TryGetValue("fecha", out var vFecha) && vFecha != null)
                    {
                        if (vFecha is DateTime dt) fecha = dt;
                        else if (DateTime.TryParse(vFecha.ToString(), out DateTime dt2)) fecha = dt2;
                    }

                    // Denominaciones de esta apertura
                    var paramPartidas = new Dictionary<string, object>();
                    paramPartidas.Add("idEncabezado", idEncabezado);
                    string queryPartidas = @"
SELECT 
    p.descr_prod as descripcion,
    p.cant_ud    as cantidad,
    p.pv_prod    as valor,
    p.imp_part   as subtotal
FROM partidasdoc p
WHERE p.encabezado_id = @idEncabezado
ORDER BY p.pv_prod DESC";
                    var partidas = RunQuery(queryPartidas, paramPartidas);

                    aperturas.Add(new
                    {
                        folio,
                        montoInicial,
                        cajero,
                        hora = fecha.HasValue ? fecha.Value.ToString("o") : null,
                        denominaciones = partidas
                    });
                }

                return Json(new
                {
                    existe = true,
                    totalAperturas,
                    apertura = aperturas[0],   // ← compatibilidad con código existente
                    aperturas                  // ← lista completa
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoVentaAdministracion/VerificarAperturaDelDia");
                return Json(new { existe = false, error = ex.Message });
            }
        }

        // ── Solicitar 3ra apertura (cajero) ─────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SolicitarTerceraApertura(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();

                // Verificar que realmente son 2 aperturas hoy
                string queryConteo = @"
            SELECT COUNT(*) AS total FROM encabezadomov
            WHERE nat = 'ACAJA' AND suc = @suc AND fch::date = CURRENT_DATE";
                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                var conteo = RunQuery(queryConteo, parameters);
                int totalHoy = Convert.ToInt32(conteo[0]["total"]);

                if (totalHoy < 2)
                    return Json(new { success = false, message = "Aún no se han hecho 2 aperturas hoy." });
                if (totalHoy >= MaxAperturasPorDia)
                    return Json(new { success = false, message = "Ya existe una 3ra apertura aprobada hoy." });

                // Verificar que no haya ya una solicitud pendiente
                parameters.Clear();
                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                string queryPendiente = @"
            SELECT id FROM solicitudes_apertura_caja
            WHERE suc = @suc
              AND estatus = 'pendiente'
              AND fecha_solicitud::date = CURRENT_DATE";
                var pendiente = RunQuery(queryPendiente, parameters);
                if (pendiente != null && pendiente.Count > 0)
                    return Json(new { success = false, message = "Ya hay una solicitud pendiente para esta sucursal.", yaExiste = true });

                // Denominaciones con subtotal recalculado; el monto sale del conteo.
                var (denominaciones, monto, errorDenom) = LeerDenominaciones(fc);
                if (errorDenom != null)
                    return Json(new { success = false, message = errorDenom });

                decimal montoDeclarado = decimal.TryParse(fc["montoInicial"].ToString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out decimal md) ? md : -1m;

                if (montoDeclarado >= 0 && Math.Abs(montoDeclarado - monto) > 0.05m)
                    return Json(new
                    {
                        success = false,
                        message = $"El monto inicial declarado ({montoDeclarado:N2}) no coincide con la suma de las denominaciones ({monto:N2})."
                    });

                parameters.Clear();
                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                parameters.Add("cajero", User.Identity.Name);
                parameters.Add("monto", monto);
                // Se serializa en minúsculas a propósito: AprobacionAperturas.cshtml lee
                // d.valor / d.cantidad / d.subtotal, y con PascalCase el desglose que ve
                // el gerente saldría vacío.
                parameters.Add("denominaciones", JsonConvert.SerializeObject(
                    denominaciones.Select(d => new { valor = d.Valor, cantidad = d.Cantidad, subtotal = d.Subtotal })));
                parameters.Add("numero", 3);

                string queryInsert = @"
            INSERT INTO solicitudes_apertura_caja
                (suc, cajero_usuario, monto_inicial, denominaciones, numero_apertura)
            VALUES
                (@suc, @cajero, @monto, @denominaciones::jsonb, @numero)
            RETURNING id";

                var insertResult = RunQuery(queryInsert, parameters);
                int nuevaId = Convert.ToInt32(insertResult[0]["id"]);

                return Json(new { success = true, solicitudId = nuevaId });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoVentaAdministracion/SolicitarTerceraApertura");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── Cajero consulta el estado de su solicitud (polling) ──────────────────────
        public JsonResult EstadoSolicitudApertura(int solicitudId)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id", solicitudId);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));

            string query = @"
        SELECT estatus, motivo_rechazo, fecha_resolucion
        FROM solicitudes_apertura_caja
        WHERE id = @id AND suc = @suc";

            var result = RunQuery(query, parameters);
            if (result == null || result.Count == 0)
                return Json(new { success = false, message = "Solicitud no encontrada" });

            return Json(new
            {
                success = true,
                estatus = result[0]["estatus"]?.ToString(),
                motivo = result[0]["motivo_rechazo"]?.ToString()
            });
        }

        // ── Gerente consulta solicitudes pendientes de todas sus sucursales ───────────
        // Sin el filtro de rol, el mismo cajero que solicitaba la 3ra apertura podía
        // listarla y aprobarla: el control de doble firma no existía en la práctica.
        [AuthorizeRole(RolGerente, RolAdministrador, RolSuperAdministrador)]
        public JsonResult ObtenerSolicitudesPendientes()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = @"
        SELECT
            s.id,
            s.suc,
            cs.cve_sucursal  AS nombre_sucursal,
            s.cajero_usuario,
            s.monto_inicial,
            s.denominaciones,
            s.fecha_solicitud,
            s.numero_apertura
        FROM solicitudes_apertura_caja s
        INNER JOIN catsucursales cs ON cs.id_sucursal = s.suc
        WHERE s.estatus = 'pendiente'
          AND cs.empresa_id = @empresa_id
          AND s.fecha_solicitud::date = CURRENT_DATE
        ORDER BY s.fecha_solicitud DESC";

            var result = RunQuery(query, parameters);
            return Json(new { success = true, solicitudes = result });
        }

        // ── Gerente aprueba o rechaza ────────────────────────────────────────────────
        // El permiso depende de la acción: aprobar exige rol de gerente, pero rechazar
        // también lo usa el propio cajero para cancelar su solicitud (AperturaCaja.cshtml).
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Resolucion de solicitud de apertura de caja")]
        public JsonResult ResolverSolicitudApertura(IFormCollection fc)
        {
            try
            {
                if (!int.TryParse(fc["solicitudId"].ToString(), out int solicitudId) || solicitudId <= 0)
                    return Json(new { success = false, message = "Solicitud inválida." });

                string accion = fc["accion"].ToString();   // 'aprobar' | 'rechazar'
                string motivoRechazo = fc["motivo"].ToString();

                if (accion != "aprobar" && accion != "rechazar")
                    return Json(new { success = false, message = "Acción inválida." });

                var parameters = new Dictionary<string, object>();
                parameters.Add("id", solicitudId);
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                // Obtener datos de la solicitud (acotada a la empresa del usuario)
                string queryGet = @"
            SELECT s.suc, s.cajero_usuario, s.monto_inicial, s.denominaciones
            FROM solicitudes_apertura_caja s
            INNER JOIN catsucursales cs ON cs.id_sucursal = s.suc
            WHERE s.id = @id AND s.estatus = 'pendiente' AND cs.empresa_id = @empresa_id";
                var solicitud = RunQuery(queryGet, parameters);

                if (solicitud == null || solicitud.Count == 0)
                    return Json(new { success = false, message = "Solicitud no encontrada o ya resuelta." });

                var sol = solicitud[0];
                int suc = Convert.ToInt32(sol["suc"]);

                // ── Autorización ───────────────────────────────────────────────
                // Antes cualquier usuario autenticado podía aprobar: el cajero que
                // pedía la 3ra apertura podía aprobársela él mismo.
                bool esAutorizador = AuthorizeRoleAttribute.TieneRol(
                    HttpContext.Session, RolGerente, RolAdministrador, RolSuperAdministrador);
                bool esDuenoDeLaSolicitud = string.Equals(
                    sol["cajero_usuario"]?.ToString(), User.Identity?.Name, StringComparison.OrdinalIgnoreCase);

                if (accion == "aprobar" && !esAutorizador)
                    return Json(new { success = false, message = "Sólo un gerente puede aprobar una apertura de caja." });

                if (accion == "rechazar" && !esAutorizador && !esDuenoDeLaSolicitud)
                    return Json(new { success = false, message = "Sólo un gerente o el cajero que la solicitó pueden cancelar esta solicitud." });

                // Antes de aprobar hay que revalidar el tope: entre la solicitud y la
                // resolución pudo registrarse otra apertura y se creaba una 4ta.
                if (accion == "aprobar")
                {
                    var conteo = RunQuery(
                        "SELECT COUNT(*) AS total FROM encabezadomov WHERE nat = 'ACAJA' AND suc = @suc AND fch::date = CURRENT_DATE",
                        new Dictionary<string, object> { { "suc", suc } });

                    if (Convert.ToInt32(conteo[0]["total"]) >= MaxAperturasPorDia)
                        return Json(new { success = false, message = $"La sucursal ya alcanzó el límite de {MaxAperturasPorDia} aperturas hoy." });
                }

                // Actualizar estatus
                parameters.Clear();
                parameters.Add("id", solicitudId);
                parameters.Add("estatus", accion == "aprobar" ? "aprobada" : "rechazada");
                parameters.Add("motivo", motivoRechazo ?? "");
                parameters.Add("gerenteId", GetUserId(User.Identity.Name));

                string queryUpdate = @"
            UPDATE solicitudes_apertura_caja
            SET estatus            = @estatus,
                motivo_rechazo     = @motivo,
                gerente_usuario_id = @gerenteId,
                fecha_resolucion   = NOW()
            WHERE id = @id AND estatus = 'pendiente'";
                RunUpdate(queryUpdate, parameters);

                // Si aprobó → registrar la apertura real en encabezadomov
                string folio = null;
                if (accion == "aprobar")
                {
                    string cajero = sol["cajero_usuario"]?.ToString();
                    string denomJson = sol["denominaciones"]?.ToString();

                    var denominaciones = JsonConvert.DeserializeObject<List<DenominacionItem>>(denomJson ?? "[]")
                                         ?? new List<DenominacionItem>();

                    // El subtotal se recalcula aquí también: el JSON guardado viene de un
                    // form del cajero y no es fuente de verdad para el importe.
                    var (partidas, montoRecalculado, errorDenom) = ConstruirPartidasDeDenominaciones(denominaciones);
                    if (errorDenom != null)
                        return Json(new { success = false, message = errorDenom });

                    // Obtener gerente para usr1
                    var paramUser = new Dictionary<string, object>();
                    paramUser.Add("userName", User.Identity.Name);
                    string queryUser = "SELECT usuarioid FROM usuarios WHERE nombreusuario = @userName";
                    var gerenteRow = RunQuery(queryUser, paramUser);
                    int gerenteUserId = gerenteRow != null && gerenteRow.Count > 0
                        ? Convert.ToInt32(gerenteRow[0]["usuarioid"]) : 0;

                    var encabezado = new DocumentoEncabezado
                    {
                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        IdArea = 14,
                        IdTpDoc = 64,
                        UsrDep = GetAreaName(cajero),
                        Anio = DateTime.Now.Year,
                        Suc = suc,
                        Fch = DateTime.Now,
                        TpMov = "ACAJA",
                        UsrDoc = cajero,
                        FchCap = DateTime.Now,
                        Usr0 = GetUserId(cajero),
                        Fch0 = DateTime.Now,
                        Usr1 = gerenteUserId,
                        Imp = montoRecalculado,
                        CliProv = "srs",
                        Estatus = 1,
                        TipoPoceso = "apertura_caja_aprobada"
                    };

                    var folioResult = GenerarDocumentoConPartidas(encabezado, partidas);
                    folio = folioResult["folio_generado"]?.ToString();
                }

                return Json(new
                {
                    success = true,
                    message = accion == "aprobar" ? "Apertura aprobada y registrada." : "Solicitud rechazada.",
                    folio = folio
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoVentaAdministracion/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── Polling del gerente: ¿hay solicitudes pendientes? (para la notif push) ───
        [AuthorizeRole(RolGerente, RolAdministrador, RolSuperAdministrador)]
        public JsonResult HaySolicitudesPendientes()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = @"
        SELECT COUNT(*) AS total
        FROM solicitudes_apertura_caja s
        INNER JOIN catsucursales cs ON cs.id_sucursal = s.suc
        WHERE s.estatus = 'pendiente'
          AND cs.empresa_id = @empresa_id
          AND s.fecha_solicitud::date = CURRENT_DATE";

            var result = RunQuery(query, parameters);
            int total = Convert.ToInt32(result[0]["total"]);
            return Json(new { total });
        }

    }
}