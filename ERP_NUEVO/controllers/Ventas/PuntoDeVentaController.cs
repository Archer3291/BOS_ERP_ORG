using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Extensions;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.CuentasContables;
using BOS_ERP.Models.Options;
using BOS_ERP.services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Npgsql;
using System.Data;
using System.Globalization;
using System.Runtime.InteropServices;

namespace BOS_ERP.Controllers.Ventas
{
    [Authorize]
    public partial class PuntoDeVentaController : FacturacionVentaController
    {
        // Utilities expone _configuration como campo público, pero sólo lo inicializa su
        // constructor Utilities(bool). El DI construye este controlador por la cadena
        // PuntoDeVentaController -> FacturacionVentaController -> Utilities(), así que
        // _configuration llegaba SIEMPRE null y cualquier lectura de appsettings
        // (los datos del emisor, por ejemplo) reventaba con NullReferenceException.
        // Se inyecta la configuración real del host y se asigna a la del base.
        public PuntoDeVentaController(
            IOptions<TimbradoOptions> timbradoOptions,
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IWebHostEnvironment env,
            XmlBuilderService xmlService,
            IConfiguration configuration)
            : base(timbradoOptions, viewEngine, tempDataProvider, env, xmlService)
        {
            _configuration = configuration;
        }
    
    public class ResultadoDetallado
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public Dictionary<string, object> Data { get; set; }

            public ResultadoDetallado()
            {
                Data = new Dictionary<string, object>();
            }
        }

        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryFacturas = "SELECT  " +
                "    em.id_encabezado as id, " +
                "    em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "        CASE WHEN em.variacion > 0  " +
                "             THEN '-' || num_to_letters(em.variacion)  " +
                "             ELSE '' END as nombre " +
                "FROM encabezadomov em " +
                "WHERE em.nat = 'VINFAC' " +
                "ORDER BY em.fch DESC";


            result.Add("facturas", RunQuery(queryFacturas));


            return Json(result);
        }
        public IActionResult BuscarDocumento(string folio)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "folio", folio } };

                // Se filtra por nat para no cargar en el POS un documento que no sea
                // cotización de sucursal (Guardar exige VSCOT; sin este filtro la venta
                // se cargaba en pantalla y reventaba hasta el momento de guardar).
                string queryBuscarId = @"
            SELECT em.id_encabezado, em.estatus_id, em.tiene_pendientes
            FROM encabezadomov em
            WHERE em.folio = @folio AND em.suc = @suc AND em.nat = @nat
            LIMIT 1;";
                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                parameters.Add("nat", NatCotizacionSucursal);
                var idResult = RunQuery(queryBuscarId, parameters);

                if (idResult == null || idResult.Count == 0)
                    return Json(new { success = false, message = "El folio no existe." });

                var row = idResult.First();
                int id = Convert.ToInt32(row["id_encabezado"]);
                int estatus = Convert.ToInt32(row["estatus_id"]);
                bool tienePendientes = row["tiene_pendientes"] != DBNull.Value && Convert.ToBoolean(row["tiene_pendientes"]);

                // ❌ Ya fue cerrado completamente
                if (estatus == 11)
                    return Json(new { success = false, message = "Esta cotización ya fue procesada y cerrada completamente." });

                // ❌ Estatus no válido (ni abierta ni parcial)
                if (estatus != 1 && estatus != 42)
                    return Json(new { success = false, message = "La cotización no está disponible para ventas (estatus inválido)." });

                // ── Encabezado ──
                parameters.Clear();
                parameters.Add("id", id);
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                string queryEncabezado = @"
            SELECT  
                em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio,
                em.id_encabezado, em.fch, em.hr_cap, em.cli_prov, em.vdr_cpr,
                em.ccy, em.flete, em.mdp, em.f_pago, em.cfdi,
                em.sub, em.dto1, em.iva, em.ieps_isr, em.imp, em.suc,
                cc.n_cli, cc.cve_cli, cc.rfc, cc.dir, cc.col, cc.pob, cc.cp,
                -- El vendedor se captura en la cotización y ya no cambia. En pantalla se
                -- muestra su nombre; lo que se guarda en los documentos es la clave.
                v.nombre AS vdr_nombre
            FROM encabezadomov em
            INNER JOIN catclientes cc ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            -- Sin filtrar por sucursal: clave_vendedor es única en todo el catálogo (cero
            -- duplicados) y los vendedores están dados de alta en la sucursal 1 aunque
            -- atiendan en otras. Cruzándolo por sucursal el nombre nunca resolvía.
            LEFT JOIN vendedores v ON v.clave_vendedor = em.vdr_cpr
            WHERE em.id_encabezado = @id;";

                var enc = RunQuery(queryEncabezado, parameters).First();
                int sucursal = Convert.ToInt32(enc["suc"]);
                parameters.Add("sucursal", sucursal);

                // Condición de pago de la cotización. El POS la necesita para saber si
                // tiene que pedir dinero o mandar el importe a la cartera del cliente.
                bool cotizacionACredito = CreditoVentasHelper.EsVentaCredito(enc["mdp"]?.ToString());

                // Situación de crédito del cliente, sólo para informar en pantalla: la
                // validación que decide se hace en Guardar, contra el total real.
                var infoCredito = cotizacionACredito
                    ? this.ValidarCreditoVenta(
                        enc["cve_cli"]?.ToString(),
                        Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        DecimalDe(enc["imp"]),
                        MetodoPagoCreditoSat,
                        id)
                    : null;

                object creditoCliente = infoCredito == null ? null : new
                {
                    estatus = infoCredito.Estatus,
                    permitido = infoCredito.Permitido,
                    autorizado = infoCredito.Autorizado,
                    limite = infoCredito.Limite,
                    usado = infoCredito.Usado,
                    disponible = infoCredito.Limite - infoCredito.Usado,
                    mensaje = infoCredito.Mensaje
                };

                // ── Si es PARCIAL: traer pendientes con stock actual ──
                if (tienePendientes && estatus == 42)
                {
                    string queryPendientes = @"
                SELECT 
                    vp.id_pendiente,
                    vp.cve_prod        AS code,
                    vp.descr_prod      AS description,
                    vp.cantidad_pendiente AS quantity,
                    vp.cantidad_original,
                    vp.cantidad_entregada,
                    vp.pv_prod         AS unitPrice,
                    vp.dto1            AS discountPercent,
                    vp.ud              AS unidad,
                    vp.estatus,
                    venta.tipo_proceso,
                    COALESCE(stk.cantidadStock, 0) AS existencia
                FROM ventas_pendientes vp
                LEFT JOIN encabezadomov venta ON venta.id_encabezado = vp.encabezado_venta
                LEFT JOIN (
                    SELECT tp.producto_id, SUM(tp.cantidad) AS cantidadStock
                    FROM tarima_productos tp
                    INNER JOIN cattarimas ct  ON ct.id_tarima  = tp.tarima_id
                    INNER JOIN catniveles cn  ON cn.id_nivel   = ct.nivel_id
                    INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                    INNER JOIN catracks cr    ON cr.id_rack    = cc.rack_id
                    INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                    INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                    WHERE ca.tipo = 'Stock' AND cs.id_sucursal = @sucursal
                    GROUP BY tp.producto_id
                ) stk ON stk.producto_id = vp.producto_id
                WHERE vp.encabezado_origen = @id
                  AND vp.estatus IN ('pendiente', 'parcial')
                ORDER BY vp.id_pendiente;";

                    var pendientes = RunQuery(queryPendientes, parameters);

                    // Si la venta original cobró sólo lo entregado, esta entrega se cobra
                    // ahora y el POS debe pedir formas de pago; si se pagó completa, no.
                    bool requiereCobroEntrega = pendientes.Any(p =>
                        string.Equals(p["tipo_proceso"]?.ToString(), TipoVentaParcialPorCobrar,
                                      StringComparison.OrdinalIgnoreCase));

                    var result = new
                    {
                        folio = enc["folio"],
                        id = id,
                        esParcial = true,
                        requiereCobro = requiereCobroEntrega,
                        esCredito = cotizacionACredito,
                        credito = creditoCliente,
                        date = Convert.ToDateTime(enc["fch"]).ToString("yyyy-MM-dd"),
                        client = new
                        {
                            name = enc["n_cli"],
                            rfc = enc["rfc"],
                            cve = enc["cve_cli"],
                            address = $"{enc["dir"]}, Col. {enc["col"]}, {enc["pob"]}, CP {enc["cp"]}"
                        },
                        seller = enc["vdr_cpr"],
                        // Nombre para mostrar; la clave es la que viaja en los documentos.
                        sellerName = enc["vdr_nombre"] ?? enc["vdr_cpr"],
                        items = pendientes.Select(p => new
                        {
                            idPendiente = Convert.ToInt32(p["id_pendiente"]),
                            code = p["code"]?.ToString(),
                            description = p["description"]?.ToString(),
                            // quantity = lo que FALTA por entregar (no el original)
                            quantity = Convert.ToDecimal(p["quantity"]),
                            quantityOriginal = Convert.ToDecimal(p["cantidad_original"]),
                            quantityDelivered = Convert.ToDecimal(p["cantidad_entregada"]),
                            unitPrice = Convert.ToDecimal(p["unitprice"]),
                            discountPercent = Convert.ToDecimal(p["discountpercent"]),
                            // ✅ stock actual en bodega
                            stock = Convert.ToDecimal(p["existencia"]),
                            unidad = p["unidad"]?.ToString(),
                            total = Convert.ToDecimal(p["unitprice"]) * Convert.ToDecimal(p["quantity"])
                        }).ToList(),
                        subtotal = 0M,
                        grandTotal = 0M
                    };

                    return Json(new { success = true, data = result });
                }

                // ── Venta nueva (estatus = 1): traer partidas originales ──
                string queryPartidas = @"
            SELECT 
                pd.id_partidas AS id,
                pd.cve_prod    AS code,
                pd.descr_prod  AS description,
                pd.cant_ud     AS quantity,
                pd.pv_prod     AS unitPrice,
                pd.dto1        AS discountPercent,
                pd.ud          AS unidad,
                pd.imp_part    AS importe,
                pd.pv_prod     AS total,
                COALESCE(stk.cantidadStock, 0) AS existencia
            FROM partidasdoc pd
            LEFT JOIN (
                SELECT tp.producto_id, SUM(tp.cantidad) AS cantidadStock
                FROM tarima_productos tp
                INNER JOIN cattarimas ct  ON ct.id_tarima  = tp.tarima_id
                INNER JOIN catniveles cn  ON cn.id_nivel   = ct.nivel_id
                INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                INNER JOIN catracks cr    ON cr.id_rack    = cc.rack_id
                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                WHERE ca.tipo = 'Stock' AND cs.id_sucursal = @sucursal
                GROUP BY tp.producto_id
            ) stk ON stk.producto_id = pd.producto_id
            WHERE pd.encabezado_id = @id
            ORDER BY pd.nro_part;";

                var partidas = RunQuery(queryPartidas, parameters);

                var resultNormal = new
                {
                    folio = enc["folio"],
                    id = id,
                    esParcial = false,
                    esCredito = cotizacionACredito,
                    credito = creditoCliente,
                    date = Convert.ToDateTime(enc["fch"]).ToString("yyyy-MM-dd"),
                    client = new
                    {
                        name = enc["n_cli"],
                        rfc = enc["rfc"],
                        cve = enc["cve_cli"],
                        address = $"{enc["dir"]}, Col. {enc["col"]}, {enc["pob"]}, CP {enc["cp"]}"
                    },
                    seller = enc["vdr_cpr"],
                    sellerName = enc["vdr_nombre"] ?? enc["vdr_cpr"],
                    items = partidas.Select(p => new
                    {
                        idPendiente = 0,
                        code = p["code"]?.ToString(),
                        description = p["description"]?.ToString(),
                        quantity = Convert.ToDecimal(p["quantity"]),
                        quantityOriginal = Convert.ToDecimal(p["quantity"]),
                        quantityDelivered = 0M,
                        unitPrice = Convert.ToDecimal(p["unitprice"]),
                        discountPercent = p.ContainsKey("discountpercent") ? Convert.ToDecimal(p["discountpercent"]) : 0M,
                        stock = Convert.ToDecimal(p["existencia"]),
                        unidad = p["unidad"]?.ToString(),
                        total = Convert.ToDecimal(p["total"])
                    }).ToList(),
                    subtotal = Convert.ToDecimal(enc["sub"]),
                    grandTotal = Convert.ToDecimal(enc["imp"])
                };

                return Json(new { success = true, data = resultNormal });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class FormaPago
        {
            public string method { get; set; }
            public string methodName { get; set; }
            public decimal amount { get; set; }
            public int IdFPago { get; set; }

            /// <summary>
            /// Cuenta de banco (catbancos.id_catbanco) que el cajero eligió para este
            /// cobro, de entre las configuradas para la forma de pago en
            /// pos_formas_pago_bancos. Es 0 cuando esa forma de pago no tiene ninguna
            /// configurada: el POS no pregunta y la cuenta se define al facturar.
            /// </summary>
            public int bancoId { get; set; }
        }

        // Nat del documento de cotización que genera Ventas Sucursales (VSCotizacion: TpMov = "VSCOT").
        private const string NatCotizacionSucursal = "VSCOT";

        // Clave SAT de efectivo: es la única forma de pago de la que se puede dar cambio.
        private const string FormaPagoEfectivoSat = "01";

        // Margen para comparar importes y no rechazar ventas por redondeos de centavos.
        private const decimal ToleranciaCentavos = 0.05m;

        // Modo de cobro de una venta parcial.
        private const string ModoCobroTotal = "total";          // se cobra el pedido completo
        private const string ModoCobroEntregado = "entregado";  // se cobra sólo lo que se lleva

        // tipo_proceso del documento de venta. El de cobro parcial se distingue para que
        // la entrega posterior sepa si tiene que cobrar o si la mercancía ya está pagada.
        private const string TipoVentaCompleta = "venta_sucursal";
        private const string TipoVentaParcialPagada = "venta_sucursal_parcial";
        private const string TipoVentaParcialPorCobrar = "venta_sucursal_parcial_por_cobrar";

        // Venta a crédito: no entra dinero a la caja, el importe se va a cartera y la
        // factura se timbra en el momento. El marcador va en tipo_proceso y NO en `mdp`
        // a propósito: hay ventas viejas con mdp = PPD —heredado de una cotización a
        // crédito que el POS cobró como contado, antes de que existiera este flujo— y
        // ésas deben seguir tratándose como contado.
        private const string TipoVentaCreditoCompleta = "venta_sucursal_credito";
        private const string TipoVentaCreditoParcial = "venta_sucursal_credito_parcial";
        private const string TipoVentaCreditoEntregaPendiente = "venta_sucursal_credito_entrega";

        // Clave SAT "Por definir": la que exige el SAT en un CFDI con método PPD, porque
        // al emitirlo todavía no se sabe con qué va a pagar el cliente.
        private const string FormaPagoPorDefinirSat = "99";

        // Método de pago SAT de una venta a crédito: pago en parcialidades o diferido.
        private const string MetodoPagoCreditoSat = "PPD";

        /// <summary>
        /// Lee un decimal de texto sin reventar cuando la clave no viene en el form o trae basura.
        /// Cultura invariante a propósito: el front siempre manda punto decimal.
        /// </summary>
        private static decimal ParseDecimal(string valor, decimal porDefecto = 0m)
            => decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? d
                : porDefecto;

        /// <summary>
        /// Totales de un documento a partir de sus partidas. Convención única de la cadena
        /// VSUC → VSPED → VSREM → VIFAC, alineada con la que aplica XmlBuilderService:
        ///   · sub      = importe BRUTO  (cantidad × precio, sin descuento)
        ///   · imp_part = importe NETO   (ya trae el descuento aplicado; es la base del IVA)
        ///   · imp      = neto + IVA
        /// El IVA se redondea POR PARTIDA, igual que el CFDI, para que el total del
        /// documento y el del comprobante no difieran por centavos.
        /// </summary>
        private static (decimal Bruto, decimal Neto, decimal Iva, decimal Total) CalcularTotales(
            IEnumerable<PartidaDocumento> partidas)
        {
            decimal bruto = 0m, neto = 0m, iva = 0m;

            foreach (var p in partidas)
            {
                decimal impPart = p.ImpPart.GetValueOrDefault();

                bruto += Math.Round(p.CantUd.GetValueOrDefault() * p.PvProd.GetValueOrDefault(), 2);
                neto += impPart;
                iva += Math.Round(impPart * 0.16m, 2);
            }

            return (bruto, neto, iva, neto + iva);
        }

        /// <summary>
        /// Sucursal y empresa de la sesión. SessionManagementFilter ya corta las peticiones
        /// sin sesión, pero Convert.ToInt32(GetInt32(...)) devuelve 0 en silencio si la clave
        /// falta, y un documento con suc = 0 es invisible en todas las consultas del módulo.
        /// Antes de escribir dinero conviene verificarlo explícitamente.
        /// </summary>
        private bool TryGetContextoSesion(out int sucursal, out int empresa, out string error)
        {
            sucursal = HttpContext.Session.GetInt32("Sucursal") ?? 0;
            empresa = HttpContext.Session.GetInt32("Empresa") ?? 0;

            if (sucursal <= 0 || empresa <= 0)
            {
                error = "La sesión no tiene sucursal o empresa asignada. Vuelve a iniciar sesión.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Lee las formas de pago del form y resuelve su id de catálogo. Una clave SAT
        /// desconocida es error: antes se descartaba en silencio y la venta se guardaba
        /// con ese pago desaparecido.
        /// </summary>
        private (bool Ok, List<FormaPago> Formas, string Error) ParsearFormasPago(IFormCollection fc)
        {
            var json = fc["paymentMethodsJson"].ToString();
            var formas = string.IsNullOrWhiteSpace(json)
                ? new List<FormaPago>()
                : JsonConvert.DeserializeObject<List<FormaPago>>(json) ?? new List<FormaPago>();

            if (formas.Count == 0)
                return (false, null, "Debe registrar al menos una forma de pago.");

            if (formas.Any(f => f.amount <= 0))
                return (false, null, "Todas las formas de pago deben tener un importe mayor a cero.");

            foreach (var f in formas)
            {
                var idFPago = RunScalar(
                    "SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat LIMIT 1",
                    new Dictionary<string, object> { { "cve_sat", f.method } });

                if (idFPago == null || !int.TryParse(idFPago.ToString(), out int id))
                    return (false, null, $"La forma de pago '{f.method}' no existe en el catálogo SAT.");

                f.IdFPago = id;
            }

            return (true, formas, null);
        }

        /// <summary>
        /// Comprueba que los pagos cubran el total y devuelve el cambio. Sólo se puede dar
        /// cambio de lo recibido en efectivo.
        /// </summary>
        private static (bool Ok, decimal Cambio, string Error) ValidarPagosCubrenTotal(
            List<FormaPago> formas, decimal total)
        {
            decimal pagado = formas.Sum(f => f.amount);

            if (pagado < total - ToleranciaCentavos)
                return (false, 0m, $"Los pagos registrados ({pagado:N2}) no cubren el total ({total:N2}).");

            decimal cambio = pagado - total;
            decimal efectivo = formas.Where(f => f.method == FormaPagoEfectivoSat).Sum(f => f.amount);

            if (cambio > ToleranciaCentavos && cambio > efectivo + ToleranciaCentavos)
                return (false, 0m,
                    $"El excedente de {cambio:N2} supera el efectivo recibido ({efectivo:N2}); no se puede dar cambio de un pago electrónico.");

            return (true, cambio, null);
        }

        /// <summary>
        /// Registra el desglose de formas de pago de un documento. El cambio se descuenta
        /// del efectivo: en la caja sólo queda lo que realmente cubre la venta.
        /// </summary>
        private void RegistrarFormasPago(
            int idEncabezado, List<FormaPago> formas, decimal cambio,
            NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            decimal cambioPorAplicar = cambio;

            // La columna llega con sql/pos_formas_pago_bancos.sql. Mientras no se corra,
            // el cobro se registra igual que siempre y sólo se pierde el dato de a qué
            // cuenta bancaria entró el dinero.
            bool guardaBanco = ExisteColumna("factura_formas_pagos", "banco_id", conn, tx);

            foreach (var f in formas)
            {
                decimal monto = f.amount;

                if (cambioPorAplicar > 0 && f.method == FormaPagoEfectivoSat)
                {
                    decimal ajuste = Math.Min(cambioPorAplicar, monto);
                    monto -= ajuste;
                    cambioPorAplicar -= ajuste;
                }

                if (monto <= 0) continue;

                var valores = new Dictionary<string, object>
                {
                    { "encabezado_id", idEncabezado },
                    { "metodo",        f.method },
                    { "metodo_nombre", f.methodName },
                    { "monto",         Math.Round(monto, 2) }
                };

                string columnas = "encabezado_id, metodo, metodo_nombre, monto";
                string valores_sql = "@encabezado_id, @metodo, @metodo_nombre, @monto";

                if (guardaBanco)
                {
                    columnas += ", banco_id";
                    // NULLIF para no meter un 0 que violaría la FK contra catbancos.
                    valores_sql += ", NULLIF(@banco_id, 0)";
                    valores.Add("banco_id", f.bancoId);
                }

                RunUpdate(
                    $"INSERT INTO factura_formas_pagos ({columnas}) VALUES ({valores_sql});",
                    valores, false, conn, tx);
            }
        }

        // AbrirConexion vive ahora en Utilities: la usan también los controladores de
        // Crédito y Cobranza para envolver la refacturación en una transacción.

        /// <summary>
        /// Texto de una fila de catálogo, con respaldo cuando la columna no viene en la
        /// consulta o está vacía. La usa la venta a crédito para leer los datos fiscales
        /// del cliente sin reventar si el cliente no tiene dirección de facturación.
        /// </summary>
        private static string ValorTexto(Dictionary<string, object> fila, string columna, string porDefecto)
        {
            if (fila != null && fila.TryGetValue(columna, out var valor)
                && valor != null && valor != DBNull.Value
                && !string.IsNullOrWhiteSpace(valor.ToString()))
                return valor.ToString();

            return porDefecto;
        }

        /// <summary>Decimal proveniente de la BD, tolerante a NULL/DBNull.</summary>
        private static decimal DecimalDe(object valor, decimal porDefecto = 0m)
            => (valor == null || valor == DBNull.Value) ? porDefecto : Convert.ToDecimal(valor);

        /// <summary>
        /// Existencias por producto en los almacenes tipo 'Stock' de una sucursal.
        /// Misma fuente que usa BuscarDocumento para pintar la columna de existencia,
        /// para que lo que valida el servidor y lo que ve el cajero sean el mismo número.
        /// </summary>
        private Dictionary<int, decimal> ObtenerExistenciasPorProducto(
            int sucursal, IEnumerable<int> productoIds,
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            var ids = productoIds.Where(i => i > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return new Dictionary<int, decimal>();

            var filas = RunQuery(@"
        SELECT tp.producto_id, SUM(tp.cantidad) AS existencia
        FROM tarima_productos tp
        INNER JOIN cattarimas ct    ON ct.id_tarima   = tp.tarima_id
        INNER JOIN catniveles cn    ON cn.id_nivel    = ct.nivel_id
        INNER JOIN catcolumnas cc   ON cc.id_columna  = cn.columna_id
        INNER JOIN catracks cr      ON cr.id_rack     = cc.rack_id
        INNER JOIN catalmacenes ca  ON ca.id_almacen  = cr.almacen_id
        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
        WHERE ca.tipo = 'Stock'
          AND cs.id_sucursal = @sucursal
          AND tp.producto_id = ANY(@ids)
        GROUP BY tp.producto_id;",
                new Dictionary<string, object> { { "sucursal", sucursal }, { "ids", ids } },
                false, conn, tx);

            return filas.ToDictionary(
                f => Convert.ToInt32(f["producto_id"]),
                f => DecimalDe(f["existencia"]));
        }

        /// <summary>
        /// Descuenta del inventario la cantidad indicada, tomando tarimas de almacenes
        /// tipo 'Stock' de la sucursal dada (mismo universo que valida el POS).
        /// Devuelve la cantidad que NO se pudo descontar; 0 si salió completa.
        /// </summary>
        private decimal DescontarExistencia(
            int idProducto, string codigo, string descripcion, string unidad,
            decimal cantidad, int sucursal, int usuarioId, int encabezadoId,
            NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            if (idProducto <= 0 || cantidad <= 0)
                return 0m;

            int idUdm = 0;
            try
            {
                idUdm = Convert.ToInt32(RunScalar(
                    "SELECT id_udm FROM catunidades WHERE cve_udm = @cve_udm;",
                    new Dictionary<string, object> { { "cve_udm", string.IsNullOrWhiteSpace(unidad) ? "PZA" : unidad } },
                    false, conn, tx));
            }
            catch { idUdm = 0; }

            var tarimas = RunQuery(@"
        SELECT tp.tarima_id, tp.cantidad
        FROM tarima_productos tp
        INNER JOIN cattarimas ct   ON ct.id_tarima  = tp.tarima_id
        INNER JOIN catniveles cn   ON cn.id_nivel   = ct.nivel_id
        INNER JOIN catcolumnas cc  ON cc.id_columna = cn.columna_id
        INNER JOIN catracks cr     ON cr.id_rack    = cc.rack_id
        INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
        WHERE tp.producto_id = @producto_id
          AND tp.cantidad > 0
          AND ca.tipo = 'Stock'
          AND ca.sucursal_id = @sucursal
        ORDER BY tp.tarima_id ASC",
                new Dictionary<string, object> { { "producto_id", idProducto }, { "sucursal", sucursal } },
                false, conn, tx);

            decimal restante = cantidad;

            foreach (var t in tarimas)
            {
                if (restante <= 0) break;

                int tarimaId = Convert.ToInt32(t["tarima_id"]);
                decimal descontar = Math.Min(DecimalDe(t["cantidad"]), restante);
                if (descontar <= 0) continue;

                restante -= descontar;

                RegistrarMovimiento(
                    new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            { "id_producto", idProducto  },
                            { "codigo",      codigo      },
                            { "descripcion", descripcion },
                            { "cantidad",    descontar   },
                            { "unidad",      idUdm       },
                            { "tarima_id",   tarimaId    },
                            { "tipo",        "venta"     },
                            { "movimiento",  "salida"    }
                        }
                    },
                    usuarioId, "venta", tarimaId, null, "salida", encabezadoId, null, conn, tx);
            }

            return restante;
        }

        /// <summary>Primer texto no vacío entre lo que manda el form, lo que trae la cotización y el default.</summary>
        private static string TextoDe(string delForm, object deCotizacion, string porDefecto = "")
        {
            if (!string.IsNullOrWhiteSpace(delForm)) return delForm;
            var s = (deCotizacion == null || deCotizacion == DBNull.Value) ? null : deCotizacion.ToString();
            return string.IsNullOrWhiteSpace(s) ? porDefecto : s;
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Ventas Realizada")]
        public async Task<JsonResult> Guardar(IFormCollection fc)
        {
            try
            {
                if (!TryGetContextoSesion(out int sucursalSesion, out int empresaSesion, out string errorSesion))
                    return Json(new { success = false, sessionExpired = true, message = errorSesion });

                if (string.IsNullOrWhiteSpace(fc["clientName"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un cliente." });

                // El vendedor ya no se valida contra el formulario: se hereda de la
                // cotización y se comprueba más abajo, al cargarla.

                if (string.IsNullOrWhiteSpace(fc["itemsJson"].ToString()))
                    return Json(new { success = false, message = "Debe agregar al menos un producto." });

                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["itemsJson"].ToString());
                if (productos == null || productos.Count == 0)
                    return Json(new { success = false, message = "Debe agregar al menos un producto." });

                if (!DateTime.TryParse(fc["timestamp"].ToString(), out DateTime fch))
                    return Json(new { success = false, message = "Formato de fecha no válido." });

                // ── Cliente ───────────────────────────────────────────────────
                var parameters = new Dictionary<string, object>
        {
            { "cve_cli", fc["cve_cli"].ToString() },
            { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
        };
                var cli = RunQuery(
                    "SELECT * FROM catclientes cc LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli WHERE cc.cve_cli = @cve_cli AND cc.empresa_id = @empresa_id",
                    parameters).FirstOrDefault();

                if (cli == null)
                    return Json(new { success = false, message = "El cliente seleccionado no existe en esta empresa." });

                // ── Documento origen: SIEMPRE una cotización de Ventas Sucursales ──
                // Antes, si no venía documentID se usaba GetUserId() como encabezado padre:
                // eso hacía que la venta cerrara (estatus_id = 11) un documento ajeno
                // cuyo id_encabezado coincidía con el id del usuario.
                if (!int.TryParse(fc["documentID"].ToString(), out int idEncabezadoPadre) || idEncabezadoPadre <= 0)
                    return Json(new { success = false, message = "Debe cargar una cotización antes de registrar la venta." });

                var cotParam = new Dictionary<string, object>
                {
                    { "id",  idEncabezadoPadre },
                    { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                    { "nat", NatCotizacionSucursal }
                };

                var cot = RunQuery(@"
            SELECT em.usr0, em.fch0, em.alm, em.ccy, em.par, em.flete,
                   em.mdp, em.cfdi, em.coment1, em.coment_aut, em.estatus_id,
                   em.vdr_cpr
            FROM encabezadomov em
            WHERE em.id_encabezado = @id
              AND em.nat = @nat
              AND em.suc = @suc;",
                    cotParam).FirstOrDefault();

                if (cot == null)
                    return Json(new { success = false, message = "La cotización no existe o no pertenece a esta sucursal." });

                // El vendedor lo fija la cotización y se hereda sin cambios por toda la
                // cadena de documentos. No se toma del formulario: era editable en pantalla,
                // así que la venta podía quedar acreditada a un vendedor distinto del que
                // la cotizó.
                string vendedorCotizacion = cot["vdr_cpr"]?.ToString();

                if (string.IsNullOrWhiteSpace(vendedorCotizacion))
                    return Json(new { success = false, message = "La cotización no tiene vendedor asignado. Corrígela antes de vender." });

                int estatusCotizacion = Convert.ToInt32(cot["estatus_id"]);
                if (estatusCotizacion != 1 && estatusCotizacion != 42)
                    return Json(new { success = false, message = "La cotización ya fue procesada y no está disponible para venta." });

                int usrId0 = GetInt(cot["usr0"]) ?? 0;
                DateTime usrFch0 = cot["fch0"] is DateTime fchCot ? fchCot : DateTime.Now;

                // ── ¿Contado o crédito? ───────────────────────────────────────
                // Lo decide la COTIZACIÓN (mdp = PPD), no la pantalla: así el cajero no
                // puede convertir en crédito una venta que se cotizó de contado, ni al
                // revés. Todo lo que sigue es aditivo; con esCredito = false el flujo es
                // exactamente el de siempre.
                bool esCredito = CreditoVentasHelper.EsVentaCredito(cot["mdp"]?.ToString());

                if (esCredito)
                {
                    // A crédito la factura se timbra en el acto y va nominativa, así que
                    // los datos fiscales del cliente tienen que estar completos ANTES de
                    // que la mercancía salga del almacén.
                    var faltantesFiscales = new List<string>();
                    if (string.IsNullOrWhiteSpace(cli["rfc"]?.ToString())) faltantesFiscales.Add("RFC");
                    if (string.IsNullOrWhiteSpace(cli["cp"]?.ToString())) faltantesFiscales.Add("código postal");

                    if (faltantesFiscales.Count > 0)
                        return Json(new
                        {
                            success = false,
                            message = $"El cliente no tiene {string.Join(" ni ", faltantesFiscales)} en el catálogo. " +
                                      "Una venta a crédito se factura al registrarse, así que esos datos son obligatorios."
                        });
                }

                // ── Partidas autorizadas por la cotización ─────────────────────
                // El precio, el descuento y la cantidad máxima se leen SIEMPRE de aquí.
                // Lo que manda el navegador en itemsJson sólo decide QUÉ se entrega,
                // nunca a qué precio: los inputs son readonly en la UI, pero eso es
                // HTML y no impide un POST armado a mano.
                var partidasCotizacion = RunQuery(@"
            SELECT pd.cve_prod, pd.descr_prod, pd.cant_ud, pd.pv_prod, pd.dto1, pd.ud, pd.producto_id,
                   COALESCE(cp.udm = 'SRV', FALSE) AS es_servicio
            FROM partidasdoc pd
            LEFT JOIN catproductos cp ON cp.id_catproductos = pd.producto_id
            WHERE pd.encabezado_id = @id;",
                    new Dictionary<string, object> { { "id", idEncabezadoPadre } });

                if (partidasCotizacion.Count == 0)
                    return Json(new { success = false, message = "La cotización no tiene partidas." });

                var lineasAutorizadas = partidasCotizacion
                    .GroupBy(r => (r["cve_prod"]?.ToString() ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            CantidadMaxima = g.Sum(r => DecimalDe(r["cant_ud"])),
                            Precio = DecimalDe(g.First()["pv_prod"]),
                            Descuento = DecimalDe(g.First()["dto1"]),
                            Unidad = g.First()["ud"]?.ToString() ?? "PZA",
                            Descripcion = g.First()["descr_prod"]?.ToString() ?? "",
                            ProductoId = GetInt(g.First()["producto_id"]) ?? 0,
                            // Los servicios no tienen existencias: ni se validan ni se descuentan.
                            EsServicio = g.First()["es_servicio"] is bool b && b
                        },
                        StringComparer.OrdinalIgnoreCase);

                // Saldo por clave, para que un itemsJson con la misma clave repetida
                // no pueda multiplicar la cantidad autorizada.
                var saldoPorClave = lineasAutorizadas.ToDictionary(
                    kv => kv.Key, kv => kv.Value.CantidadMaxima, StringComparer.OrdinalIgnoreCase);

                // ── Formas de pago ────────────────────────────────────────────
                // A crédito no hay cobro en caja: la lista va vacía y el encabezado se
                // marca con la clave SAT 99 ("por definir"), que es la que corresponde a
                // un CFDI PPD. De contado, todo igual que siempre.
                List<FormaPago> formasPago;
                string formasPagoConcatenadas;
                int idFormaPagoPrincipal;

                if (esCredito)
                {
                    if (!string.IsNullOrWhiteSpace(fc["paymentMethodsJson"].ToString())
                        && fc["paymentMethodsJson"].ToString() != "[]")
                        return Json(new
                        {
                            success = false,
                            message = "Esta cotización es a crédito: no se cobra en caja. " +
                                      "Si el cliente va a pagar ahora, cotiza de contado."
                        });

                    formasPago = new List<FormaPago>();
                    formasPagoConcatenadas = null;

                    var idPorDefinir = RunScalar(
                        "SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve LIMIT 1",
                        new Dictionary<string, object> { { "cve", FormaPagoPorDefinirSat } });

                    if (idPorDefinir == null || !int.TryParse(idPorDefinir.ToString(), out idFormaPagoPrincipal))
                        return Json(new
                        {
                            success = false,
                            message = $"No existe la forma de pago SAT '{FormaPagoPorDefinirSat}' en el catálogo; " +
                                      "sin ella no se puede timbrar una factura a crédito."
                        });
                }
                else
                {
                    var (pagosOk, formas, errorPagos) = ParsearFormasPago(fc);
                    if (!pagosOk)
                        return Json(new { success = false, message = errorPagos });

                    formasPago = formas;

                    formasPagoConcatenadas = formasPago.Count > 0
                        ? string.Join(",", formasPago.Select(f => f.IdFPago))
                        : null;

                    idFormaPagoPrincipal = formasPago
                        .OrderByDescending(f => f.amount)
                        .FirstOrDefault()?.IdFPago ?? 0;
                }

                // ── Modo de cobro ─────────────────────────────────────────────
                // En una venta parcial el cliente puede pagar el pedido completo (y
                // recibir después lo que falta sin cobro adicional) o pagar sólo lo que
                // se lleva hoy (y pagar el resto al entregarse). Lo decide la caja.
                bool cobrarSoloEntregado = string.Equals(
                    fc["modoCobro"].ToString(), ModoCobroEntregado, StringComparison.OrdinalIgnoreCase);

                // ── Construir partidas y detectar pendientes ───────────────────
                var partidasAEntregar = new List<PartidaDocumento>();
                var pendientesARegistrar = new List<Dictionary<string, object>>();
                bool esParcial = false;

                // Totales acumulados sobre los precios de la cotización, no sobre los del form.
                decimal descuentoTotal = 0m;

                // Cantidad que sale físicamente de bodega, por producto.
                var entregaPorProducto = new Dictionary<int, (decimal Cantidad, string Clave, string Descripcion, string Unidad)>();

                foreach (var p in productos)
                {
                    string cve = (p.GetValueOrDefault("code", "") ?? "").Trim();

                    if (!lineasAutorizadas.TryGetValue(cve, out var linea))
                        return Json(new { success = false, message = $"El producto '{cve}' no forma parte de la cotización." });

                    decimal cantidadPedida = ParseDecimal(p.GetValueOrDefault("quantityOriginal", "0"));
                    decimal cantidadAEntregar = ParseDecimal(p.GetValueOrDefault("quantity", "0"));

                    if (cantidadPedida <= 0)
                        return Json(new { success = false, message = $"La cantidad del producto '{cve}' debe ser mayor a cero." });

                    if (cantidadAEntregar < 0 || cantidadAEntregar > cantidadPedida)
                        return Json(new { success = false, message = $"La cantidad a entregar de '{cve}' no puede superar la cantidad pedida." });

                    if (cantidadPedida > saldoPorClave[cve] + 0.0001m)
                        return Json(new { success = false, message = $"La cantidad de '{cve}' ({cantidadPedida}) excede lo autorizado en la cotización ({saldoPorClave[cve]})." });

                    saldoPorClave[cve] -= cantidadPedida;

                    // Precio, descuento, unidad, descripción y producto_id: SIEMPRE de la cotización.
                    decimal precio = linea.Precio;
                    decimal descuento = linea.Descuento;
                    string unidad = linea.Unidad;
                    int productId = linea.ProductoId;

                    // Cantidad que se factura en ESTE documento: el pedido completo o
                    // sólo lo que se entrega, según el modo de cobro.
                    decimal cantidadFacturada = cobrarSoloEntregado ? cantidadAEntregar : cantidadPedida;

                    // El descuento sí se aplica al importe. Antes imp_part era cantidad*precio
                    // y el descuento se guardaba en dto1 sin afectar nada: el cliente pagaba
                    // con descuento en caja y el documento (y el CFDI) salían a precio de lista.
                    decimal importeBruto = Math.Round(cantidadFacturada * precio, 2);
                    decimal montoDescuento = Math.Round(importeBruto * (descuento / 100m), 2);
                    decimal importeNeto = importeBruto - montoDescuento;

                    descuentoTotal += montoDescuento;

                    if (productId > 0 && cantidadAEntregar > 0 && !linea.EsServicio)
                    {
                        entregaPorProducto[productId] = entregaPorProducto.TryGetValue(productId, out var acum)
                            ? (acum.Cantidad + cantidadAEntregar, acum.Clave, acum.Descripcion, acum.Unidad)
                            : (cantidadAEntregar, cve, linea.Descripcion, unidad);
                    }

                    decimal cantidadPendiente = cantidadPedida - cantidadAEntregar;

                    // Se factura la cantidad total pedida, o sólo la entregada si la caja
                    // eligió cobrar únicamente lo que el cliente se lleva. En ese modo una
                    // partida sin entrega no genera renglón: no se está vendiendo todavía.
                    if (cantidadFacturada > 0)
                    {
                        partidasAEntregar.Add(new PartidaDocumento
                        {
                            CveProd = cve,
                            DescrProd = linea.Descripcion,
                            CantUd = cantidadFacturada,
                            PvProd = precio,
                            Dto1 = descuento,
                            ImpPart = importeNeto,
                            Ud = unidad,
                            IdProducto = productId
                        });
                    }

                    if (cantidadPendiente > 0)
                    {
                        esParcial = true;
                        pendientesARegistrar.Add(new Dictionary<string, object>
                {
                    { "cve_prod",           cve },
                    { "descr_prod",         linea.Descripcion },
                    { "producto_id",        productId },
                    { "cantidad_original",  cantidadPedida },
                    { "cantidad_entregada", cantidadAEntregar },
                    { "cantidad_pendiente", cantidadPendiente },
                    { "pv_prod",            precio },
                    { "dto1",               descuento },
                    { "ud",                 unidad }
                });
                    }
                }

                if (partidasAEntregar.Count == 0)
                    return Json(new
                    {
                        success = false,
                        message = cobrarSoloEntregado
                            // En este modo sólo se facturan las cantidades entregadas: si no
                            // hay existencia de nada, no queda ningún renglón que registrar.
                            ? "No hay existencia de ningún artículo, así que no hay nada que cobrar. Usa \"cobrar pedido completo\" para dejarlo pagado y entregarlo después."
                            : "No hay productos para registrar."
                    });

                // ── Existencias ────────────────────────────────────────────────
                // La UI ya limita la entrega al stock mostrado, pero eso es sólo la UI:
                // sin esta comprobación un POST armado a mano entregaba más de lo que
                // hay en bodega. Lo que excede el stock debe quedar como pendiente,
                // no salir del almacén.
                var existencias = ObtenerExistenciasPorProducto(sucursalSesion, entregaPorProducto.Keys);

                var faltantes = new List<string>();
                foreach (var entrega in entregaPorProducto)
                {
                    decimal disponible = existencias.GetValueOrDefault(entrega.Key, 0m);
                    if (entrega.Value.Cantidad > disponible + 0.0001m)
                        faltantes.Add($"{entrega.Value.Clave} (se intenta entregar {entrega.Value.Cantidad:N2}, existencia {disponible:N2})");
                }

                if (faltantes.Count > 0)
                    return Json(new
                    {
                        success = false,
                        message = "Existencias insuficientes en la sucursal para: " + string.Join("; ", faltantes)
                    });

                // ── Totales ────────────────────────────────────────────────────
                var totales = CalcularTotales(partidasAEntregar);
                decimal ivaReal = totales.Iva;
                decimal totalReal = totales.Total;

                // ── Los pagos tienen que cubrir la venta ───────────────────────
                // A crédito no hay pagos que cuadrar; lo que se comprueba es que el
                // cliente tenga línea suficiente. Es la misma validación que usan
                // Industrial, Nacional y Sucursales, incluida la autorización de gerente.
                decimal cambio = 0m;

                if (esCredito)
                {
                    var credito = this.ValidarCreditoVenta(
                        fc["cve_cli"].ToString(),
                        empresaSesion,
                        totalReal,
                        MetodoPagoCreditoSat,
                        idEncabezadoPadre,
                        tokenAutorizacion: fc["creditoToken"].ToString());

                    if (!credito.Permitido)
                        return Json(new
                        {
                            success = false,
                            requiereAutorizacion = true,
                            estadoCredito = credito.Estatus,
                            limite = credito.Limite,
                            usado = credito.Usado,
                            message = credito.Mensaje
                        });
                }
                else
                {
                    var (pagosCubren, cambioCalculado, errorCobertura) = ValidarPagosCubrenTotal(formasPago, totalReal);
                    if (!pagosCubren)
                        return Json(new { success = false, message = errorCobertura });

                    cambio = cambioCalculado;
                }

                // ── Datos de encabezado: el form del POS no captura estos campos,
                //    así que se heredan de la cotización y sólo se sobreescriben
                //    si el front llegara a mandarlos. Nunca se hace Convert.ToDecimal("")
                //    directo sobre el form: eso hacía fallar TODA venta con FormatException.
                decimal flete = ParseDecimal(fc["flete"].ToString(), DecimalDe(cot["flete"]));
                decimal paridad = ParseDecimal(fc["paridad"].ToString(), DecimalDe(cot["par"], 1m));
                if (paridad <= 0) paridad = 1m;

                // ── Encabezado del documento de venta ─────────────────────────
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 60,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = TextoDe(fc["almacen"].ToString(), cot["alm"]),
                    Fch = DateTime.Now,
                    TpMov = "VSUC",
                    ComentAut = TextoDe(fc["comentarios"].ToString(), cot["coment_aut"]),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = usrId0,
                    Fch0 = usrFch0,
                    Usr1 = GetUserId(User.Identity.Name),
                    Fch1 = DateTime.Now,
                    CliProv = fc["cve_cli"].ToString(),
                    Ref = GetInt(cli["id_cliente"]),
                    Ccy = TextoDe(fc["moneda"].ToString(), cot["ccy"], "PESOS"),
                    Estatus = 1,
                    Flete = flete,
                    VdrCpr = vendedorCotizacion,
                    Coment1 = TextoDe(fc["concepto"].ToString(), cot["coment1"]),
                    EncabezadoPadre = idEncabezadoPadre,
                    FchPgEntrega = fch,
                    Par = paridad,
                    Veh = formasPagoConcatenadas,
                    FPago = idFormaPagoPrincipal,
                    // El plazo sólo tiene sentido a crédito; de contado se queda en NULL,
                    // como hasta ahora. De aquí lo hereda la factura y con él nace la
                    // cartera con su fecha de vencimiento.
                    PlDias = esCredito ? (GetInt(cli["pl_crd"], 0) ?? 0) : (int?)null,
                    Mdp = TextoDe(fc["metodo-pago"].ToString(), cot["mdp"], "PUE"),
                    Sub = totales.Bruto,
                    Imp = totalReal,
                    // En una venta de mostrador `cfdi` NO es el uso de CFDI: es la marca que
                    // usa el portal de autofacturación para guardar el UUID del comprobante
                    // con que el cliente facturó su ticket, y su consulta exige que sea NULL
                    // para ofrecer la venta. Al guardar aquí un "G03" ninguna venta del punto
                    // de venta aparecía en el portal, y además ese valor terminaba pisando el
                    // uso de CFDI elegido en la pantalla de facturación.
                    // El uso de CFDI se decide al facturar, no al vender.
                    CFDI = null,
                    TipoPoceso = esCredito
                        ? (esParcial ? TipoVentaCreditoParcial : TipoVentaCreditoCompleta)
                        : (!esParcial
                            ? TipoVentaCompleta
                            : (cobrarSoloEntregado ? TipoVentaParcialPorCobrar : TipoVentaParcialPagada)),
                    TienePendientes = esParcial
                };

                // ── Escritura atómica ─────────────────────────────────────────
                // Documento + pendientes + formas de pago + cierre de la cotización
                // van juntos: si algo falla a media venta no puede quedar el documento
                // creado sin sus pendientes o con la cotización sin marcar.
                // Datos fiscales del receptor para la factura a crédito. Salen del
                // catálogo del cliente: a crédito no hay nada que capturar en mostrador.
                string usoCfdiCliente = ValorTexto(cli, "uso_sugerido", "G03");
                string regimenCliente = ValorTexto(cli, "regimen_fiscal", "616");

                Dictionary<string, object> folio;
                int idFactura;
                string folioFacturaCredito = null;
                string uuidFacturaCredito = null;
                using (var connVenta = AbrirConexion())
                {
                    using (var txVenta = connVenta.BeginTransaction())
                    {
                        try
                        {
                            folio = GenerarDocumentoConPartidas(encabezado, partidasAEntregar, connVenta, txVenta);
                            idFactura = Convert.ToInt32(folio["IdEncabezado"]);

                            // ── Salida de inventario ───────────────────────────
                            // El movimiento se registra aquí, cuando la mercancía sale
                            // físicamente con el cliente, no en la remisión del corte de
                            // caja. Al estar en la misma transacción que la validación de
                            // existencias, no hay ventana entre comprobar y descontar.
                            int usuarioActual = GetUserId(User.Identity.Name);
                            foreach (var entrega in entregaPorProducto)
                            {
                                decimal sinDescontar = DescontarExistencia(
                                    entrega.Key,
                                    entrega.Value.Clave,
                                    entrega.Value.Descripcion,
                                    entrega.Value.Unidad,
                                    entrega.Value.Cantidad,
                                    sucursalSesion,
                                    usuarioActual,
                                    idFactura,
                                    connVenta, txVenta);

                                if (sinDescontar > 0.0001m)
                                    throw new InvalidOperationException(
                                        $"No hay existencia suficiente de {entrega.Value.Clave}: faltaron {sinDescontar:N2} unidades por descontar.");
                            }

                            // ── Formas de pago del ticket ──────────────────────
                            // Sin esto la tabla nunca se llenaba y el desglose del corte
                            // de caja (DatosGeneral / DatosSelect) siempre salía vacío.
                            RegistrarFormasPago(idFactura, formasPago, cambio, connVenta, txVenta);

                            // ── Registrar pendientes ───────────────────────────
                            if (pendientesARegistrar.Count > 0)
                            {
                                foreach (var pendiente in pendientesARegistrar)
                                {
                                    pendiente.Add("encabezado_origen", idEncabezadoPadre);
                                    pendiente.Add("encabezado_venta", idFactura);
                                    pendiente.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                                    pendiente.Add("sucursal_id", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                                    pendiente.Add("usuario_id", GetUserId(User.Identity.Name));
                                    pendiente.Add("estatus", "pendiente");

                                    RunUpdate(@"
                                INSERT INTO ventas_pendientes
                                (encabezado_origen, encabezado_venta, cve_prod, descr_prod, producto_id,
                                 cantidad_original, cantidad_entregada, cantidad_pendiente, pv_prod, dto1, ud,
                                 empresa_id, sucursal_id, usuario_id, estatus)
                                VALUES
                                (@encabezado_origen, @encabezado_venta, @cve_prod, @descr_prod, @producto_id,
                                 @cantidad_original, @cantidad_entregada, @cantidad_pendiente, @pv_prod, @dto1, @ud,
                                 @empresa_id, @sucursal_id, @usuario_id, @estatus)",
                                        pendiente, false, connVenta, txVenta);
                                }

                                // Marcar cotización origen como parcial (NO cerrarla)
                                RunUpdate(
                                    "UPDATE encabezadomov SET tiene_pendientes = TRUE, estatus_id = 42 WHERE id_encabezado = @id",
                                    new Dictionary<string, object> { { "id", idEncabezadoPadre } },
                                    false, connVenta, txVenta);
                            }
                            else
                            {
                                // Venta completa: cerrar cotización
                                RunUpdate(
                                    "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id",
                                    new Dictionary<string, object> { { "id", idEncabezadoPadre } },
                                    false, connVenta, txVenta);
                            }

                            // ── Factura de la venta a crédito ──────────────────
                            // A crédito la venta nace facturada: sin CFDI no hay cartera
                            // que respalde la deuda, y la mercancía ya se está entregando.
                            // Va dentro de esta misma transacción, así que si el PAC
                            // rechaza el comprobante se revierte también la salida de
                            // inventario y la venta entera.
                            if (esCredito)
                            {
                                var facturacion = await FacturarVentaCreditoAsync(
                                    idFactura, usoCfdiCliente, regimenCliente,
                                    idFormaPagoPrincipal, connVenta, txVenta);

                                if (!facturacion.Ok)
                                {
                                    txVenta.Rollback();
                                    return Json(new
                                    {
                                        success = false,
                                        step = facturacion.Paso,
                                        message = "No se pudo facturar la venta a crédito, así que no se " +
                                                  $"registró nada: {facturacion.Error}"
                                    });
                                }

                                folioFacturaCredito = facturacion.Folio;
                                uuidFacturaCredito = facturacion.Uuid;
                            }

                            txVenta.Commit();
                        }
                        catch
                        {
                            txVenta.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    esParcial,
                    esCredito,
                    folioFactura = folioFacturaCredito,
                    uuidFactura = uuidFacturaCredito,
                    modoCobro = cobrarSoloEntregado ? ModoCobroEntregado : ModoCobroTotal,
                    message = esCredito
                        ? (esParcial
                            ? $"Venta a crédito registrada y facturada. Quedan {pendientesARegistrar.Count} artículo(s) por entregar."
                            : "Venta a crédito registrada y facturada. El importe quedó en la cartera del cliente.")
                        : (esParcial
                            ? (cobrarSoloEntregado
                                ? $"Venta parcial registrada. Quedan {pendientesARegistrar.Count} artículo(s) pendiente(s), que se cobrarán al entregarse."
                                : $"Venta parcial registrada y pagada por completo. Quedan {pendientesARegistrar.Count} artículo(s) por entregar.")
                            : "Documento creado exitosamente."),
                    folio_generado = folio["folio_generado"].ToString(),
                    // Encabezado de la venta: con él se imprime el ticket de mostrador, que
                    // es el comprobante con el que el cliente se autofactura después.
                    ventaId = idFactura,
                    subtotal = totales.Bruto,
                    descuento = descuentoTotal,
                    iva = ivaReal,
                    total = totalReal,
                    cambio = cambio > 0 ? Math.Round(cambio, 2) : 0m,
                    pendientes = pendientesARegistrar.Count
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Entrega de articulos pendientes")]
        public async Task<JsonResult> EntregarPendientes(IFormCollection fc)
        {
            try
            {
                if (!int.TryParse(fc["encabezadoOrigenId"].ToString(), out int idEncabezadoOrigen) || idEncabezadoOrigen <= 0)
                    return Json(new { success = false, message = "Documento origen inválido." });

                var items = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["itemsJson"].ToString());
                if (items == null || items.Count == 0)
                    return Json(new { success = false, message = "No se recibieron artículos." });

                if (!TryGetContextoSesion(out int sucursalSesion, out int empresaSesion, out string errorSesion))
                    return Json(new { success = false, sessionExpired = true, message = errorSesion });

                // ── Pendientes activos, acotados a la empresa y sucursal de la sesión ──
                // Antes se filtraba sólo por encabezado_origen, que viene del request:
                // cualquier usuario autenticado podía entregar los pendientes de otra sucursal.
                var paramOrigen = new Dictionary<string, object>
                {
                    { "origen",      idEncabezadoOrigen },
                    { "empresa_id",  empresaSesion      },
                    { "sucursal_id", sucursalSesion     }
                };

                var pendientesActivos = RunQuery(@"
            SELECT vp.id_pendiente, vp.cve_prod, vp.descr_prod, vp.producto_id, vp.ud,
                   vp.cantidad_pendiente, vp.cantidad_entregada, vp.cantidad_original,
                   vp.pv_prod, COALESCE(vp.dto1, 0) AS dto1,
                   vp.encabezado_venta,
                   COALESCE(cp.udm = 'SRV', FALSE) AS es_servicio,
                   venta.tipo_proceso,
                   venta.cli_prov
            FROM ventas_pendientes vp
            LEFT JOIN catproductos cp   ON cp.id_catproductos = vp.producto_id
            LEFT JOIN encabezadomov venta ON venta.id_encabezado = vp.encabezado_venta
            WHERE vp.encabezado_origen = @origen
              AND vp.empresa_id  = @empresa_id
              AND vp.sucursal_id = @sucursal_id
              AND vp.estatus IN ('pendiente', 'parcial')",
                    paramOrigen);

                if (pendientesActivos == null || pendientesActivos.Count == 0)
                    return Json(new { success = false, message = "No hay pendientes activos para este documento en esta sucursal." });

                // Si la venta original cobró sólo lo entregado, esta entrega hay que
                // cobrarla y facturarla: genera su propio documento de venta. Si se cobró
                // el pedido completo, la mercancía ya está pagada y sólo se entrega.
                bool requiereCobro = pendientesActivos.Any(p =>
                    string.Equals(p["tipo_proceso"]?.ToString(), TipoVentaParcialPorCobrar, StringComparison.OrdinalIgnoreCase));

                // La venta a crédito facturó ÚNICAMENTE lo que entregó, así que lo que
                // salga ahora también necesita su documento y su factura: si no, el
                // cliente se llevaría mercancía sin CFDI y sin cargo en su cuenta.
                bool esEntregaCredito = pendientesActivos.Any(p =>
                    (p["tipo_proceso"]?.ToString() ?? "")
                        .StartsWith(TipoVentaCreditoCompleta, StringComparison.OrdinalIgnoreCase));

                // Las dos ramas generan documento de venta; sólo cambia si se cobra en
                // caja o si el importe se manda a la cartera del cliente.
                bool generaDocumento = requiereCobro || esEntregaCredito;

                var idsPermitidos = pendientesActivos
                    .Select(p => Convert.ToInt32(p["id_pendiente"]))
                    .ToHashSet();

                // ── Validar TODO antes de escribir nada ───────────────────────
                var entregas = new List<(int IdPendiente, decimal Cantidad, string Clave,
                                         int ProductoId, string Descripcion, string Unidad,
                                         bool EsServicio, int EncabezadoVenta,
                                         decimal Precio, decimal Descuento)>();
                var errores = new List<string>();

                foreach (var item in items)
                {
                    if (!int.TryParse(item.GetValueOrDefault("idPendiente", "0"), out int idPendiente) || idPendiente <= 0)
                        continue;

                    if (!decimal.TryParse(item.GetValueOrDefault("quantity", "0"), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out decimal cantEntregaAhora) || cantEntregaAhora <= 0)
                        continue;

                    if (!idsPermitidos.Contains(idPendiente))
                    {
                        errores.Add($"El pendiente #{idPendiente} no pertenece a este documento.");
                        continue;
                    }

                    var pend = pendientesActivos.First(p => Convert.ToInt32(p["id_pendiente"]) == idPendiente);
                    decimal pendienteActual = DecimalDe(pend["cantidad_pendiente"]);
                    string clave = pend["cve_prod"]?.ToString() ?? idPendiente.ToString();

                    // Antes esto sólo agregaba un aviso y aun así entregaba la cantidad
                    // recortada, devolviendo success = true. Ahora es un error duro.
                    if (cantEntregaAhora > pendienteActual)
                    {
                        errores.Add($"{clave}: se intenta entregar {cantEntregaAhora:N2} y sólo quedan {pendienteActual:N2} pendientes.");
                        continue;
                    }

                    entregas.Add((idPendiente, cantEntregaAhora, clave,
                                  GetInt(pend["producto_id"]) ?? 0,
                                  pend["descr_prod"]?.ToString() ?? "",
                                  pend["ud"]?.ToString() ?? "PZA",
                                  pend["es_servicio"] is bool esSrv && esSrv,
                                  GetInt(pend["encabezado_venta"]) ?? idEncabezadoOrigen,
                                  DecimalDe(pend["pv_prod"]),
                                  DecimalDe(pend["dto1"])));
                }

                if (errores.Count > 0)
                    return Json(new { success = false, message = "No se registró la entrega: " + string.Join(" ", errores), errores });

                if (entregas.Count == 0)
                    return Json(new { success = false, message = "No se recibió ninguna cantidad válida para entregar." });

                // ── Cobro de la entrega (sólo si la venta original no lo incluía) ──
                List<PartidaDocumento> partidasCobro = null;
                List<FormaPago> formasPagoEntrega = null;
                decimal cambioEntrega = 0m, totalEntrega = 0m;
                DocumentoEncabezado encabezadoCobro = null;

                // Sólo se usan cuando la entrega es a crédito.
                int idFormaPagoCredito = 0;
                int plazoClienteEntrega = 0;
                string usoCfdiEntrega = "G03";
                string regimenEntrega = "616";
                string folioFacturaEntrega = null;

                if (esEntregaCredito)
                {
                    string cveClienteCredito = pendientesActivos[0]["cli_prov"]?.ToString();

                    var clienteCredito = RunQuery(@"
                SELECT cc.pl_crd, cc.rfc, cc.cp, df.uso_sugerido, df.regimen_fiscal
                FROM catclientes cc
                LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
                WHERE cc.cve_cli = @cve AND cc.empresa_id = @empresa_id;",
                        new Dictionary<string, object>
                        {
                            { "cve",        cveClienteCredito },
                            { "empresa_id", empresaSesion     }
                        }).FirstOrDefault();

                    if (clienteCredito == null)
                        return Json(new { success = false, message = "No se encontró el cliente de la venta a crédito." });

                    if (string.IsNullOrWhiteSpace(clienteCredito["rfc"]?.ToString())
                        || string.IsNullOrWhiteSpace(clienteCredito["cp"]?.ToString()))
                        return Json(new
                        {
                            success = false,
                            message = "El cliente no tiene RFC o código postal en el catálogo, y esta entrega " +
                                      "se factura al registrarse."
                        });

                    plazoClienteEntrega = GetInt(clienteCredito["pl_crd"], 0) ?? 0;
                    usoCfdiEntrega = ValorTexto(clienteCredito, "uso_sugerido", "G03");
                    regimenEntrega = ValorTexto(clienteCredito, "regimen_fiscal", "616");

                    var idPorDefinirEntrega = RunScalar(
                        "SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve LIMIT 1",
                        new Dictionary<string, object> { { "cve", FormaPagoPorDefinirSat } });

                    if (idPorDefinirEntrega == null
                        || !int.TryParse(idPorDefinirEntrega.ToString(), out idFormaPagoCredito))
                        return Json(new
                        {
                            success = false,
                            message = $"No existe la forma de pago SAT '{FormaPagoPorDefinirSat}' en el catálogo."
                        });
                }

                if (generaDocumento)
                {
                    partidasCobro = entregas.Select(e =>
                    {
                        decimal bruto = Math.Round(e.Cantidad * e.Precio, 2);
                        decimal desc = Math.Round(bruto * (e.Descuento / 100m), 2);
                        return new PartidaDocumento
                        {
                            CveProd = e.Clave,
                            DescrProd = e.Descripcion,
                            CantUd = e.Cantidad,
                            PvProd = e.Precio,
                            Dto1 = e.Descuento,
                            ImpPart = bruto - desc,
                            Ud = e.Unidad,
                            IdProducto = e.ProductoId
                        };
                    }).ToList();

                    var totalesEntrega = CalcularTotales(partidasCobro);
                    totalEntrega = totalesEntrega.Total;

                    List<FormaPago> formas;

                    if (esEntregaCredito)
                    {
                        // No se cobra nada: lo que se comprueba es que al cliente le quede
                        // línea para este cargo adicional, igual que al registrar la venta.
                        formas = new List<FormaPago>();
                        formasPagoEntrega = formas;
                        cambioEntrega = 0m;

                        var creditoEntrega = this.ValidarCreditoVenta(
                            pendientesActivos[0]["cli_prov"]?.ToString(),
                            empresaSesion,
                            totalEntrega,
                            MetodoPagoCreditoSat,
                            idEncabezadoOrigen,
                            tokenAutorizacion: fc["creditoToken"].ToString());

                        if (!creditoEntrega.Permitido)
                            return Json(new
                            {
                                success = false,
                                requiereAutorizacion = true,
                                estadoCredito = creditoEntrega.Estatus,
                                message = creditoEntrega.Mensaje
                            });
                    }
                    else
                    {
                        var (pagosOk, formasCobro, errorPagos) = ParsearFormasPago(fc);
                        if (!pagosOk)
                            return Json(new { success = false, requierePago = true, totalPorCobrar = totalEntrega, message = errorPagos });

                        var (cubren, cambioCalc, errorCobertura) = ValidarPagosCubrenTotal(formasCobro, totalEntrega);
                        if (!cubren)
                            return Json(new { success = false, requierePago = true, totalPorCobrar = totalEntrega, message = errorCobertura });

                        formas = formasCobro;
                        formasPagoEntrega = formas;
                        cambioEntrega = cambioCalc;
                    }

                    // El documento de cobro se construye a partir de la venta original:
                    // mismo cliente, vendedor, moneda y datos fiscales.
                    var ventaOriginal = RunQuery(@"
                SELECT em.alm, em.ccy, em.par, em.cli_prov, em.refe, em.vdr_cpr, em.coment1,
                       em.coment_aut, em.mdp, em.cfdi, em.usr0, em.fch0, em.encabezados_padre
                FROM encabezadomov em
                WHERE em.id_encabezado = @id AND em.suc = @suc;",
                        new Dictionary<string, object>
                        {
                            { "id",  entregas[0].EncabezadoVenta },
                            { "suc", sucursalSesion }
                        }).FirstOrDefault();

                    if (ventaOriginal == null)
                        return Json(new { success = false, message = "No se encontró la venta original de estos pendientes." });

                    encabezadoCobro = new DocumentoEncabezado
                    {
                        EmpresaId = empresaSesion,
                        IdArea = 14,
                        IdTpDoc = 60,
                        UsrDep = GetAreaName(User.Identity.Name),
                        Anio = DateTime.Now.Year,
                        Suc = sucursalSesion,
                        Alm = ventaOriginal["alm"]?.ToString() ?? "",
                        Fch = DateTime.Now,
                        TpMov = "VSUC",
                        ComentAut = ventaOriginal["coment_aut"]?.ToString() ?? "",
                        UsrDoc = User.Identity.Name,
                        FchCap = DateTime.Now,
                        Usr0 = GetInt(ventaOriginal["usr0"]) ?? 0,
                        Fch0 = ventaOriginal["fch0"] is DateTime f0 ? f0 : DateTime.Now,
                        Usr1 = GetUserId(User.Identity.Name),
                        Fch1 = DateTime.Now,
                        CliProv = ventaOriginal["cli_prov"]?.ToString(),
                        Ref = GetInt(ventaOriginal["refe"]),
                        Ccy = ventaOriginal["ccy"]?.ToString() ?? "PESOS",
                        Estatus = 1,
                        Flete = 0m,
                        VdrCpr = ventaOriginal["vdr_cpr"]?.ToString(),
                        Coment1 = ventaOriginal["coment1"]?.ToString(),
                        EncabezadoPadre = GetInt(ventaOriginal["encabezados_padre"]) ?? idEncabezadoOrigen,
                        FchPgEntrega = DateTime.Now,
                        Par = DecimalDe(ventaOriginal["par"], 1m) > 0 ? DecimalDe(ventaOriginal["par"], 1m) : 1m,
                        Veh = esEntregaCredito ? null : string.Join(",", formas.Select(f => f.IdFPago)),
                        FPago = esEntregaCredito
                            ? idFormaPagoCredito
                            : formas.OrderByDescending(f => f.amount).First().IdFPago,
                        Mdp = esEntregaCredito ? MetodoPagoCreditoSat : (ventaOriginal["mdp"]?.ToString() ?? "PUE"),
                        PlDias = esEntregaCredito ? plazoClienteEntrega : (int?)null,
                        Sub = totalesEntrega.Bruto,
                        Imp = totalesEntrega.Total,
                        // Igual que la venta: se deja libre para que el portal de
                        // autofacturación pueda ofrecer este cobro y marcarlo con el UUID.
                        CFDI = null,
                        TipoPoceso = esEntregaCredito
                            ? TipoVentaCreditoEntregaPendiente
                            : "venta_sucursal_entrega_pendiente",
                        TienePendientes = false
                    };
                }

                // ── Escritura atómica ─────────────────────────────────────────
                int restantes;
                bool todosCompletos;
                string folioCobro = null;
                using (var connEnt = AbrirConexion())
                {
                    using (var txEnt = connEnt.BeginTransaction())
                    {
                        try
                        {
                            // Documento de venta de esta entrega, cuando el pendiente
                            // quedó por cobrar en la venta original.
                            if (generaDocumento)
                            {
                                var folioCob = GenerarDocumentoConPartidas(encabezadoCobro, partidasCobro, connEnt, txEnt);
                                int idCobro = Convert.ToInt32(folioCob["IdEncabezado"]);
                                folioCobro = folioCob["folio_generado"]?.ToString();

                                if (esEntregaCredito)
                                {
                                    // Mismo trato que la venta a crédito: la entrega nace
                                    // facturada y su importe se suma a la cartera. Si el PAC
                                    // rechaza, se revierte también la entrega.
                                    var facturacion = await FacturarVentaCreditoAsync(
                                        idCobro, usoCfdiEntrega, regimenEntrega,
                                        idFormaPagoCredito, connEnt, txEnt);

                                    if (!facturacion.Ok)
                                    {
                                        txEnt.Rollback();
                                        return Json(new
                                        {
                                            success = false,
                                            step = facturacion.Paso,
                                            message = "No se pudo facturar la entrega a crédito, así que no se " +
                                                      $"registró nada: {facturacion.Error}"
                                        });
                                    }

                                    folioFacturaEntrega = facturacion.Folio;
                                }
                                else
                                {
                                    RegistrarFormasPago(idCobro, formasPagoEntrega, cambioEntrega, connEnt, txEnt);
                                }
                            }

                            foreach (var entrega in entregas)
                            {
                                // Resta relativa con guarda en el propio WHERE: dos entregas
                                // simultáneas ya no pueden pisarse. Antes se calculaba el
                                // nuevo pendiente en memoria y se escribía como valor absoluto.
                                var actualizado = RunQuery(@"
                            UPDATE ventas_pendientes SET
                                cantidad_entregada  = cantidad_entregada + @cant,
                                cantidad_pendiente  = cantidad_pendiente - @cant,
                                estatus             = CASE WHEN cantidad_pendiente - @cant <= 0
                                                           THEN 'completado' ELSE 'parcial' END,
                                fecha_actualizacion = NOW()
                            WHERE id_pendiente = @id
                              AND empresa_id   = @empresa_id
                              AND sucursal_id  = @sucursal_id
                              AND estatus IN ('pendiente', 'parcial')
                              AND cantidad_pendiente >= @cant
                            RETURNING id_pendiente;",
                                    new Dictionary<string, object>
                                    {
                                        { "id",          entrega.IdPendiente },
                                        { "cant",        entrega.Cantidad    },
                                        { "empresa_id",  empresaSesion       },
                                        { "sucursal_id", sucursalSesion      }
                                    },
                                    false, connEnt, txEnt);

                                if (actualizado.Count == 0)
                                    throw new InvalidOperationException(
                                        $"El pendiente de {entrega.Clave} cambió mientras se registraba la entrega. Vuelva a cargar el documento.");

                                // La mercancía sale ahora: el movimiento de inventario va aquí.
                                // La remisión del corte de caja ya no descuenta nada, así que
                                // esta entrega no se descuenta dos veces. El movimiento se
                                // cuelga del documento de venta, no de la cotización.
                                if (entrega.EsServicio) continue;

                                decimal sinDescontar = DescontarExistencia(
                                    entrega.ProductoId, entrega.Clave, entrega.Descripcion, entrega.Unidad,
                                    entrega.Cantidad, sucursalSesion, GetUserId(User.Identity.Name),
                                    entrega.EncabezadoVenta, connEnt, txEnt);

                                if (sinDescontar > 0.0001m)
                                    throw new InvalidOperationException(
                                        $"No hay existencia suficiente de {entrega.Clave}: faltaron {sinDescontar:N2} unidades por descontar.");
                            }

                            // ── ¿Quedan pendientes sin completar? ─────────────
                            var aun = RunQuery(@"
                        SELECT COUNT(*) AS cnt
                        FROM ventas_pendientes
                        WHERE encabezado_origen = @origen
                          AND empresa_id  = @empresa_id
                          AND sucursal_id = @sucursal_id
                          AND estatus IN ('pendiente', 'parcial')",
                                paramOrigen, false, connEnt, txEnt);

                            restantes = Convert.ToInt32(aun[0]["cnt"]);
                            todosCompletos = restantes == 0;

                            if (todosCompletos)
                            {
                                // Cerrar la cotización original definitivamente
                                RunUpdate(
                                    "UPDATE encabezadomov SET estatus_id = 11, tiene_pendientes = FALSE WHERE id_encabezado = @id AND suc = @suc",
                                    new Dictionary<string, object>
                                    {
                                        { "id",  idEncabezadoOrigen },
                                        { "suc", sucursalSesion     }
                                    },
                                    false, connEnt, txEnt);
                            }

                            txEnt.Commit();
                        }
                        catch
                        {
                            txEnt.Rollback();
                            throw;
                        }
                    }
                }

                string detalleCobro =
                    esEntregaCredito
                        ? $" Se cargaron {totalEntrega:N2} a la cuenta del cliente (factura {folioFacturaEntrega})."
                    : requiereCobro
                        ? $" Se cobraron {totalEntrega:N2} (folio {folioCobro})."
                        : " La mercancía ya estaba pagada en la venta original.";

                return Json(new
                {
                    success = true,
                    todosCompletos,
                    restantes,
                    entregados = entregas.Count,
                    seCobro = requiereCobro,
                    totalCobrado = requiereCobro ? Math.Round(totalEntrega, 2) : 0m,
                    cambio = requiereCobro && cambioEntrega > 0 ? Math.Round(cambioEntrega, 2) : 0m,
                    folio_generado = folioCobro,
                    esCredito = esEntregaCredito,
                    totalACredito = esEntregaCredito ? Math.Round(totalEntrega, 2) : 0m,
                    folioFactura = folioFacturaEntrega,
                    message = (todosCompletos
                        ? "✅ Todos los artículos fueron entregados. Cotización cerrada."
                        : $"Entrega registrada. Aún quedan {restantes} artículo(s) pendiente(s).") + detalleCobro
                });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Ventas/EntregarPendientes");
                LogErrorHelper.RegistrarLog("PuntoDeVenta/EntregarPendientes", "SIN_FOLIO",
                    $"Error al entregar pendientes: {ex}", User.Identity?.Name);
                return Json(new { success = false, message = "No se pudo registrar la entrega." });
            }
        }

        /// <summary>
        /// Todos los pendientes de entrega activos de la sucursal, agrupados por documento
        /// de origen. Hasta ahora los pendientes sólo eran alcanzables tecleando el folio
        /// exacto en el POS: si el cajero no lo recordaba, quedaban invisibles.
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerPendientesSucursal()
        {
            try
            {
                if (!TryGetContextoSesion(out int sucursalSesion, out int empresaSesion, out string errorSesion))
                    return Json(new { success = false, sessionExpired = true, message = errorSesion });

                var filas = RunQuery(@"
            SELECT
                vp.id_pendiente,
                vp.cve_prod,
                vp.descr_prod,
                vp.ud,
                vp.cantidad_original,
                vp.cantidad_entregada,
                vp.cantidad_pendiente,
                vp.pv_prod,
                COALESCE(vp.dto1, 0)                       AS dto1,
                vp.estatus,
                vp.fecha_registro,
                (CURRENT_DATE - vp.fecha_registro::date)   AS dias,
                vp.encabezado_origen,
                cot.folio                                  AS folio_cotizacion,
                vp.encabezado_venta,
                venta.folio                                AS folio_venta,
                venta.tipo_proceso,
                venta.cli_prov,
                cli.n_cli                                  AS cliente,
                venta.vdr_cpr                              AS vendedor,
                COALESCE(stk.existencia, 0)                AS existencia
            FROM ventas_pendientes vp
            LEFT JOIN encabezadomov cot   ON cot.id_encabezado = vp.encabezado_origen
            LEFT JOIN encabezadomov venta ON venta.id_encabezado = vp.encabezado_venta
            LEFT JOIN catclientes cli     ON cli.cve_cli = venta.cli_prov AND cli.empresa_id = @empresa_id
            LEFT JOIN (
                SELECT tp.producto_id, SUM(tp.cantidad) AS existencia
                FROM tarima_productos tp
                INNER JOIN cattarimas ct    ON ct.id_tarima   = tp.tarima_id
                INNER JOIN catniveles cn    ON cn.id_nivel    = ct.nivel_id
                INNER JOIN catcolumnas cc   ON cc.id_columna  = cn.columna_id
                INNER JOIN catracks cr      ON cr.id_rack     = cc.rack_id
                INNER JOIN catalmacenes ca  ON ca.id_almacen  = cr.almacen_id
                WHERE ca.tipo = 'Stock' AND ca.sucursal_id = @sucursal_id
                GROUP BY tp.producto_id
            ) stk ON stk.producto_id = vp.producto_id
            WHERE vp.empresa_id  = @empresa_id
              AND vp.sucursal_id = @sucursal_id
              AND vp.estatus IN ('pendiente', 'parcial')
            ORDER BY vp.fecha_registro, vp.id_pendiente;",
                    new Dictionary<string, object>
                    {
                        { "empresa_id",  empresaSesion  },
                        { "sucursal_id", sucursalSesion }
                    });

                // Se agrupa por documento de origen: el cajero razona por folio, no por partida.
                var documentos = filas
                    .GroupBy(f => GetInt(f["encabezado_origen"]) ?? 0)
                    .Select(g =>
                    {
                        var primera = g.First();
                        bool porCobrar = string.Equals(primera["tipo_proceso"]?.ToString(),
                            TipoVentaParcialPorCobrar, StringComparison.OrdinalIgnoreCase);

                        var articulos = g.Select(f =>
                        {
                            decimal pendiente = DecimalDe(f["cantidad_pendiente"]);
                            decimal precio = DecimalDe(f["pv_prod"]);
                            decimal dto = DecimalDe(f["dto1"]);
                            decimal bruto = Math.Round(pendiente * precio, 2);
                            decimal neto = bruto - Math.Round(bruto * (dto / 100m), 2);

                            return new
                            {
                                idPendiente = GetInt(f["id_pendiente"]) ?? 0,
                                code = f["cve_prod"]?.ToString(),
                                description = f["descr_prod"]?.ToString(),
                                unidad = f["ud"]?.ToString(),
                                cantidadOriginal = DecimalDe(f["cantidad_original"]),
                                cantidadEntregada = DecimalDe(f["cantidad_entregada"]),
                                cantidadPendiente = pendiente,
                                existencia = DecimalDe(f["existencia"]),
                                // Cuánto de lo pendiente se puede surtir hoy
                                surtible = Math.Min(pendiente, DecimalDe(f["existencia"])),
                                importePendiente = Math.Round(neto * 1.16m, 2)
                            };
                        }).ToList();

                        return new
                        {
                            encabezadoOrigenId = g.Key,
                            // Encabezado de la VENTA: es el que imprime el ticket de
                            // mostrador (el origen es la cotización, que no tiene cobros).
                            encabezadoVentaId = GetInt(primera["encabezado_venta"]) ?? 0,
                            folioCotizacion = primera["folio_cotizacion"]?.ToString(),
                            folioVenta = primera["folio_venta"]?.ToString(),
                            cliente = primera["cliente"]?.ToString() ?? primera["cli_prov"]?.ToString(),
                            vendedor = primera["vendedor"]?.ToString(),
                            dias = GetInt(primera["dias"]) ?? 0,
                            porCobrar,
                            articulos,
                            totalPendiente = articulos.Sum(a => a.importePendiente),
                            // Verde si todo lo pendiente ya se puede entregar
                            surtibleCompleto = articulos.All(a => a.surtible >= a.cantidadPendiente - 0.0001m)
                        };
                    })
                    .OrderByDescending(d => d.dias)
                    .ToList();

                return Json(new
                {
                    success = true,
                    documentos,
                    resumen = new
                    {
                        documentos = documentos.Count,
                        articulos = filas.Count,
                        importe = documentos.Sum(d => d.totalPendiente),
                        listosParaEntregar = documentos.Count(d => d.surtibleCompleto),
                        porCobrar = documentos.Count(d => d.porCobrar)
                    }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Ventas/PendientesSucursal");
                LogErrorHelper.RegistrarLog("PuntoDeVenta/PendientesSucursal", "SIN_FOLIO",
                    $"Error al listar pendientes: {ex}", User.Identity?.Name);
                return Json(new { success = false, message = "No se pudieron cargar los pendientes." });
            }
        }

        // Consultar pendientes de un documento
        [HttpGet]
        public JsonResult ObtenerPendientes(int encabezadoOrigenId)
        {
            if (encabezadoOrigenId <= 0)
                return Json(new { success = false, message = "Documento origen inválido." });

            int sucursalSesion = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            // El empresa_id ya venía como parámetro pero la consulta no lo usaba: bastaba
            // con probar ids de encabezado para leer los pendientes de cualquier sucursal.
            var param = new Dictionary<string, object>
            {
                { "origen",      encabezadoOrigenId },
                { "empresa_id",  Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "sucursal_id", sucursalSesion },
                { "sucursal",    sucursalSesion }
            };

            var pendientes = RunQuery(@"
        SELECT vp.*,
               COALESCE(stk.cantidadStock, 0) AS stock_actual
        FROM ventas_pendientes vp
        LEFT JOIN (
            SELECT tp.producto_id, SUM(tp.cantidad) AS cantidadStock
            FROM tarima_productos tp
            INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
            INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
            INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
            INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
            INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
            INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
            WHERE ca.tipo = 'Stock' AND cs.id_sucursal = @sucursal
            GROUP BY tp.producto_id
        ) stk ON stk.producto_id = vp.producto_id
        WHERE vp.encabezado_origen = @origen
          AND vp.empresa_id  = @empresa_id
          AND vp.sucursal_id = @sucursal_id
          AND vp.estatus IN ('pendiente','parcial')
        ORDER BY vp.id_pendiente",
                param);

            return Json(new { success = true, data = pendientes });
        }

    }
}
