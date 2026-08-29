using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using System.Configuration;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Nacionales
{
    [Authorize]
    public class VNRemisionController : Utilities
    {
        // ─── Verificación de mercancía por escáner ──────────────────────────────────
        // El setting `escaneo_remision_obligatorio` (tabla settings) decide si la
        // remisión exige que cada partida se haya escaneado en el almacén. El front abre
        // el modal y manda el resultado en `escaneoJSON`; aquí se vuelve a validar
        // porque el front es UX, no seguridad (se puede saltar desde la consola).
        private const string SettingEscaneoRemision = "escaneo_remision_obligatorio";

        // Unidades sin pieza física que escanear: los servicios no se verifican
        // (p. ej. SERVICIO DE CORTE, udm SRV, que la remisión consolida como partida).
        private static readonly string[] UnidadesSinEscaneo = { "SRV" };

        private static bool EscaneoRemisionObligatorio()
            => (GetSetting(SettingEscaneoRemision) ?? "").Trim().ToLower() == "true";

        private static string Campo(Dictionary<string, string> d, string clave)
            => d != null && d.TryGetValue(clave, out var v) && v != null ? v : "";

        private static decimal ADecimal(string v)
            => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0m;

        /// <summary>
        /// Confronta las partidas del documento contra lo que el almacén escaneó.
        /// Devuelve null si todo cuadra, o el mensaje de error a mostrar.
        /// </summary>
        private static string ValidarEscaneoRemision(List<Dictionary<string, string>> productos, string escaneoJson)
        {
            List<Dictionary<string, string>> escaneo;
            try
            {
                escaneo = string.IsNullOrWhiteSpace(escaneoJson)
                    ? new List<Dictionary<string, string>>()
                    : JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(escaneoJson);
            }
            catch
            {
                escaneo = new List<Dictionary<string, string>>();
            }

            // Escaneado por clave de producto (solo lo marcado como escaneado).
            var escaneado = new Dictionary<string, decimal>();
            foreach (var e in escaneo ?? new List<Dictionary<string, string>>())
            {
                if (!bool.TryParse(Campo(e, "escaneado"), out bool ok) || !ok) continue;

                string cve = Campo(e, "productoId").Trim().ToUpper();
                if (cve.Length == 0) continue;

                decimal cant = ADecimal(Campo(e, "cantidadEscaneada"));
                escaneado[cve] = escaneado.TryGetValue(cve, out var acc) ? acc + cant : cant;
            }

            // Un producto puede venir en varias partidas (consolidado stock + modula):
            // se compara el total pedido contra el total escaneado por clave.
            var faltantes = new List<string>();
            var grupos = productos
                .Where(p => !UnidadesSinEscaneo.Contains(Campo(p, "unidad").Trim().ToUpper()))
                .GroupBy(p => Campo(p, "productoId").Trim().ToUpper())
                .Where(g => g.Key.Length > 0);

            foreach (var g in grupos)
            {
                decimal solicitado = g.Sum(p => ADecimal(Campo(p, "cantidad")));
                decimal verificado = escaneado.TryGetValue(g.Key, out var v) ? v : 0m;

                if (verificado + 0.001m < solicitado)
                    faltantes.Add(g.Key);
            }

            if (faltantes.Count == 0) return null;

            return "La verificación por escáner es obligatoria y faltan partidas por escanear: "
                   + string.Join(", ", faltantes.Take(10))
                   + (faltantes.Count > 10 ? $" (y {faltantes.Count - 10} más)" : "")
                   + ". Escanea la mercancía antes de generar la remisión.";
        }


        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Nacionales", Accion = "Creacion de cotizacion")]
        public JsonResult Guardar(IFormCollection fc)
        {
            int idEncabezadoGenerado = 0;
            try
            {
                var parameters = new Dictionary<string, object>();
                string tipo = fc["tipo"].ToString()?.ToLower() ?? ""; // contado, credito, anticipo

                // 🔹 Validaciones previas
                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un cliente." });

                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una moneda." });

                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un vendedor." });

                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una forma de pago." });

                // 🔹 Validar productos solo si NO es anticipo
                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
                if (tipo != "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });

                    productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                    if (productos == null || productos.Count == 0)
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });

                    // 🔹 Verificación de mercancía por escáner (setting escaneo_remision_obligatorio)
                    if (EscaneoRemisionObligatorio())
                    {
                        string errorEscaneo = ValidarEscaneoRemision(productos, fc["escaneoJSON"].ToString());
                        if (errorEscaneo != null)
                            return Json(new { success = false, message = errorEscaneo });
                    }
                }

                // 🔹 Validar precios y descuentos contra las reglas de precio
                // Mismo criterio que ventas industriales (Helpers/ReglasPrecioHelper.cs).
                TokenStore.LimpiarExpirados();
                string usuarioReglas = User.Identity.Name;
                string descuentoToken = fc["descuentoToken"].ToString() ?? "";
                string precioToken = fc["precioToken"].ToString() ?? "";
                int empresaReglas = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                var reglas = this.ValidarPartidas(
                    empresaReglas,
                    this.ResolverClienteId(fc["cliente"].ToString(), empresaReglas),
                    productos,
                    TokenStore.Validar(precioToken, usuarioReglas, "CAMBIO DE PRECIO"),
                    TokenStore.Validar(descuentoToken, usuarioReglas, "DESCUENTO"));

                if (!reglas.Permitido)
                    return Json(new { success = false, message = reglas.Mensaje });

                TokenStore.Invalidar(descuentoToken);
                TokenStore.Invalidar(precioToken);

                // 🔹 Validar fecha de pago solo si es crédito
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaPago"].ToString()))
                        return Json(new { success = false, message = "Debe ingresar la fecha de pago (solo para crédito)." });

                    if (!DateTime.TryParse(fc["fechaPago"].ToString(), out DateTime fch))
                        return Json(new { success = false, message = "Formato de fecha de pago no válido." });

                    fechaPago = fch;
                }
                else if (tipo == "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaAnticipo"].ToString()))
                        return Json(new { success = false, message = "Debe ingresar la fecha del anticipo." });
                    if (!DateTime.TryParse(fc["fechaAnticipo"].ToString(), out DateTime fch))
                        return Json(new { success = false, message = "Formato de fecha del anticipo no válido." });
                    fechaPago = fch;
                }
                // 🔹 Validar crédito — solo aplica cuando la venta es a crédito
                var credito = this.ValidarCreditoVenta(
                    fc["cliente"].ToString(),
                    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    CreditoVentasHelper.TotalDocumentoParaCredito(fc),
                    tipo,
                    int.TryParse(fc["documentid"].ToString(), out int docCredito) ? docCredito : 0,
                    null, null, fc["creditoToken"].ToString());

                if (!credito.Permitido)
                    return Json(new { success = false, message = credito.Mensaje });

                int idEncabezadoPadre = GetUserId(User.Identity.Name);
                int usrId0 = 0;
                DateTime usrFch0 = DateTime.Now;
                int usrId1 = 0;
                DateTime usrFch1 = DateTime.Now;

                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                {
                    // 🔹 Cargar datos del encabezado existente
                    var usrParameter = new Dictionary<string, object>();
                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3,  " +
                                      "       em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto " +
                                      "FROM encabezadomov em " +
                                      "WHERE em.id_encabezado = @id";

                    usrParameter.Add("id", Convert.ToInt32(fc["documentid"].ToString()));

                    var result = RunQuery(usrquery, usrParameter);

                    if (result.Count > 0)
                    {
                        var usrId = result[0];
                        usrId0 = Convert.ToInt32(usrId["usr0"]);
                        usrFch0 = Convert.ToDateTime(usrId["fch0"]);
                        usrId1 = Convert.ToInt32(usrId["usr1"]);
                        usrFch1 = Convert.ToDateTime(usrId["fch1"]);
                        idEncabezadoPadre = Convert.ToInt32(fc["documentid"].ToString());
                    }
                }

                int idCliente = 0;
                string clienteNombre = "";
                if (!string.IsNullOrEmpty(fc["cliente"].ToString()))
                {
                    var clienteParameter = new Dictionary<string, object>();
                    string clientequery = "SELECT cl.id_cliente, cl.n_cli " +
                                          "FROM catclientes cl " +
                                          "WHERE cl.cve_cli = @cliente AND cl.empresa_id = @empresa_id";
                    clienteParameter.Add("cliente", fc["cliente"].ToString());
                    clienteParameter.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    var clienteResult = RunQuery(clientequery, clienteParameter);
                    if (clienteResult.Count > 0)
                    {
                        var cliente = clienteResult[0];
                        idCliente = Convert.ToInt32(cliente["id_cliente"]);
                        clienteNombre = cliente["n_cli"].ToString();

                    }
                }
                var fPagoParameter = new Dictionary<string, object>();
                fPagoParameter.Add("cve_sat", fc["forma-pago"].ToString());
                // 🔹 Crear encabezado
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 12,
                    IdTpDoc = 43,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = fc["almacen"].ToString(),
                    Fch = DateTime.Now,
                    TpMov = "VNREM",
                    ComentAut = fc["comentarios"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = usrId0,
                    Fch0 = usrFch0,
                    Usr1 = usrId1,
                    Fch1 = usrFch1,
                    Usr2 = GetUserId(User.Identity.Name),
                    Fch2 = DateTime.Now,
                    Imp = Convert.ToDecimal(fc["total"].ToString()),
                    CliProv = fc["cliente"].ToString(),
                    Ref = idCliente,
                    Ccy = fc["moneda"].ToString(),
                    Estatus = 1,
                    Flete = Convert.ToDecimal(fc["flete"].ToString()),
                    VdrCpr = fc["vendedor"].ToString(),
                    Coment1 = fc["concepto"].ToString(),
                    EncabezadoPadre = idEncabezadoPadre,
                    PlDias = string.IsNullOrWhiteSpace(fc["plazo"].ToString()) ? 0 : Convert.ToInt32(fc["plazo"].ToString()),
                    FchPgEntrega = fechaPago ?? DateTime.Now, // si no hay, usa fecha actual
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    FPago = Convert.ToInt32(RunScalar("select id_f_pago from cat_f_pago where cve_sat = @cve_sat", fPagoParameter)),
                    Mdp = fc["metodo-pago"].ToString(),
                    TipoPoceso = "remision_" + tipo,
                    CFDI = fc["uso-cfdi"].ToString(),
                    //CentroCostos = Convert.ToInt32(fc["CentroCostosId"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString()
                };

                // 🔹 Crear partidas (solo si hay productos)
                var partidas = new List<PartidaDocumento>();
                if (productos.Count > 0)
                {
                    foreach (var p in productos)
                    {
                        var parametersP = new Dictionary<string, object>();
                        string queryId = "select id_catproductos from catproductos where cve_prod = @cve_prod AND empresa_id = @empresa_id";
                        parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
                        parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        int productId = Convert.ToInt32(RunScalar(queryId, parametersP));

                        partidas.Add(new PartidaDocumento
                        {
                            CveProd = p.ContainsKey("productoId") ? p["productoId"] : "",
                            DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                            CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
                            PvProd = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0,
                            Dto1 = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0,
                            ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) *
                                      (p.ContainsKey("costoUnitario") ? decimal.Parse(p["costoUnitario"]) : 0),
                            Ud = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA",
                            IdProducto = productId,
                            TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                        });
                    }
                }
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                var folio = new Dictionary<string, object>();
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // 🔹 Guardar documento
                            folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                            idEncabezadoGenerado = Convert.ToInt32(folio["IdEncabezado"]);

                            var movimientos = new List<Dictionary<string, object>>();
                            int userId = GetUserId(User.Identity.Name);
                            int idEncabezado = Convert.ToInt32(folio["IdEncabezado"]);

                            foreach (var p in productos)
                            {
                                string prodCve = p.ContainsKey("productoId") ? p["productoId"] : "";
                                decimal cantidadSolicitada = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0;
                                string unidadStr = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA"; // 🔹 viene del JSON

                                // 🔹 Obtener ID de unidad desde catunidades
                                int idudm = 0;
                                try
                                {
                                    var paramUdm = new Dictionary<string, object> { { "cve_udm", unidadStr } };
                                    string queryUdm = "SELECT id_udm FROM catunidades WHERE cve_udm = @cve_udm;";
                                    idudm = Convert.ToInt32(RunScalar(queryUdm, paramUdm));
                                }
                                catch
                                {
                                    Console.WriteLine($"⚠️ Unidad no encontrada para clave: {unidadStr}. Se asignará 0.");
                                }

                                // 🔹 Buscar datos del producto
                                var paramProd = new Dictionary<string, object> { { "cve_prod", prodCve }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } };
                                var prodInfo = RunQuery(@"
                                   SELECT id_catproductos AS id_producto, cve_prod AS codigo, descr_prod AS descripcion, es_tubo
                                   FROM catproductos
                                   WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id", paramProd, false, conn, tx);

                                if (prodInfo.Count == 0)
                                {
                                    Console.WriteLine($"⚠️ Producto no encontrado: {prodCve}");
                                    continue;
                                }

                                var prodData = prodInfo[0];
                                int idProducto = Convert.ToInt32(prodData["id_producto"]);
                                string codigo = prodData["codigo"].ToString();
                                string descripcion = prodData["descripcion"].ToString();
                                bool esTubo = prodData.ContainsKey("es_tubo")
                                              && prodData["es_tubo"] != DBNull.Value
                                              && Convert.ToBoolean(prodData["es_tubo"]);

                                // 🔹 TUBOS: en vez de UNA salida agregada, se registra una salida por
                                //    cada pieza cortada y confirmada, con su longitud real y desde la
                                //    tarima que respaldó el corte. Así el kardex refleja 1:1 la
                                //    configuración del corte (p. ej. 2 piezas de 0.5 m = 2 movimientos
                                //    de 0.5 m, no 1 de 1 m). Si no se hallan cortes (pedido suelto o
                                //    dato faltante), cae al descuento genérico de abajo.
                                if (esTubo)
                                {
                                    // Pedido de tubo real del grupo (TYBCOT → VIPED/TYBPED)
                                    var tuboRow = RunQuery(@"
                                        SELECT tub.id_encabezado
                                        FROM documentos_relacionados dr
                                        INNER JOIN encabezadomov tub
                                            ON tub.encabezados_padre = dr.id_encabezado_tubo
                                           AND tub.nat IN ('TYBPED','VIPED')
                                        WHERE dr.id_encabezado_padre = @grupo
                                        ORDER BY CASE WHEN tub.nat = 'TYBPED' THEN 0 ELSE 1 END,
                                                 tub.id_encabezado DESC
                                        LIMIT 1",
                                        new Dictionary<string, object> { { "grupo", idEncabezadoPadre } },
                                        false, conn, tx);

                                    if (tuboRow.Count > 0)
                                    {
                                        int idTuboPedido = Convert.ToInt32(tuboRow[0]["id_encabezado"]);

                                        // Una fila por asignación confirmada = una pieza cortada.
                                        var piezas = RunQuery(@"
                                            SELECT tp.tarima_id, c.longitud, tpc.folio
                                            FROM partidasdoc pdt
                                            INNER JOIN pedido_detalle_corte c
                                                ON c.pedido_detalle_id = pdt.id_partidas
                                            INNER JOIN pedido_detalle_corte_asignacion a
                                                ON a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado'
                                            INNER JOIN tarima_productos_cortes tpc
                                                ON tpc.id_corte = a.tarima_producto_corte_id
                                            INNER JOIN tarima_productos tp
                                                ON tp.id_tarima_producto = tpc.tarima_producto_id
                                            WHERE pdt.encabezado_id = @tubo AND pdt.producto_id = @producto_id
                                            ORDER BY c.longitud DESC, a.id",
                                            new Dictionary<string, object>
                                            {
                                                { "tubo", idTuboPedido },
                                                { "producto_id", idProducto }
                                            }, false, conn, tx);

                                        if (piezas.Count > 0)
                                        {
                                            foreach (var pieza in piezas)
                                            {
                                                int tarimaCorte = Convert.ToInt32(pieza["tarima_id"]);
                                                decimal longitudPieza = Convert.ToDecimal(pieza["longitud"]);
                                                string folioCorte = pieza["folio"]?.ToString() ?? "";

                                                var prodMovCorte = new Dictionary<string, object>
                                                {
                                                    { "id_producto", idProducto },
                                                    { "codigo", codigo },
                                                    { "descripcion", descripcion },
                                                    { "cantidad", longitudPieza },
                                                    { "unidad", idudm },
                                                    { "tarima_id", tarimaCorte },
                                                    { "tipo", "venta" },
                                                    { "movimiento", "salida" }
                                                };

                                                movimientos.Add(prodMovCorte);

                                                RegistrarMovimiento(
                                                    new List<Dictionary<string, object>> { prodMovCorte },
                                                    userId,
                                                    "venta",
                                                    tarimaCorte,
                                                    null,
                                                    "salida",
                                                    idEncabezado,
                                                    string.IsNullOrEmpty(folioCorte) ? "Corte de tubo" : $"Corte {folioCorte}",
                                                    conn, tx
                                                );
                                            }

                                            continue; // ✅ tubo descontado por cortes; sin descuento genérico
                                        }
                                    }
                                    // sin cortes hallados → continúa al descuento genérico (fallback)
                                }

                                // 🔹 Buscar tarimas con stock disponible
                                var paramTarimas = new Dictionary<string, object> { { "producto_id", idProducto } };

                                string queryTarima = "SELECT ct.id_tarima, tp.cantidad FROM catalmacenes c " +
                                    "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                                    "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                                    "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                                    "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                                    "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                                    "INNER JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima " +
                                    "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id " +
                                    "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Stock' AND tp.producto_id = @producto_id AND tp.cantidad > 0 ";

                                paramTarimas.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));

                                var tarimas = RunQuery(queryTarima, paramTarimas, false, conn, tx);

                                decimal restante = cantidadSolicitada;

                                foreach (var t in tarimas)
                                {
                                    if (restante <= 0)
                                        break;

                                    int tarimaId = Convert.ToInt32(t["id_tarima"]);
                                    decimal stockTarima = Convert.ToDecimal(t["cantidad"]);

                                    decimal descontar = Math.Min(stockTarima, restante);
                                    restante -= descontar;

                                    // 🔹 Construir producto para movimiento
                                    var prodMovimiento = new Dictionary<string, object>
                                    {
                                        { "id_producto", idProducto },
                                        { "codigo", codigo },
                                        { "descripcion", descripcion },
                                        { "cantidad", descontar },
                                        { "unidad", idudm }, // 🔹 id real desde catunidades
                                        { "tarima_id", tarimaId },
                                        { "tipo", "venta" },
                                        { "movimiento", "salida" }
                                    };

                                    movimientos.Add(prodMovimiento);

                                    // 🔹 Registrar movimiento de salida (por tarima)
                                    RegistrarMovimiento(
                                        new List<Dictionary<string, object>> { prodMovimiento },
                                        userId,
                                        "venta",
                                        tarimaId,
                                        null,
                                        "salida",
                                        idEncabezado,
                                        null,
                                        conn, tx
                                    );
                                }

                                if (restante > 0)
                                {
                                    Console.WriteLine($"⚠️ No hay suficiente stock para el producto {codigo}. Faltaron {restante} unidades.");
                                }
                            }

                            var fpagoParameter = new Dictionary<string, object>();
                            string fpago = "select id_f_pago from cat_f_pago where cve_sat = @f_pago_id ";
                            fpagoParameter.Add("f_pago_id", fc["forma-pago"].ToString());
                            int fp = Convert.ToInt32(RunScalar(fpago, fpagoParameter, false, conn, tx));

                            string inpuestos = "INSERT INTO imp_oc " +
                                "(encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
                                "VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);";
                            var inpuestosParameter = new Dictionary<string, object>();

                            inpuestosParameter.Add("encabezado_id", Convert.ToInt32(folio["IdEncabezado"]));
                            inpuestosParameter.Add("impuesto_id", Convert.ToInt32(GetSetting("impuesto"))); // IVA
                            inpuestosParameter.Add("subtotal", Convert.ToDecimal(fc["subtotal1"].ToString()));
                            inpuestosParameter.Add("importe", Convert.ToDecimal(fc["iva"].ToString()));
                            inpuestosParameter.Add("orden_apl", 1);
                            inpuestosParameter.Add("imp_variable", 16);
                            inpuestosParameter.Add("prov_nom", clienteNombre);
                            inpuestosParameter.Add("f_pago_id", fp);

                            RunQuery(inpuestos, inpuestosParameter, false, conn, tx);

                            RegistrarCompra(Convert.ToInt32(folio["IdEncabezado"]), GetUserId(User.Identity.Name), conn, tx);

                            //var polisa = GenerarDatosPoliza(Convert.ToInt32(folio["IdEncabezado"]));
                            //RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(folio["IdEncabezado"]), polisa);

                            ViewData["detalles"] = new AuditDetails
                            {
                                DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                                Folio = folio["folio_generado"].ToString()
                            };


                            string query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                            parameters.Add("id", idEncabezadoPadre);
                            RunUpdate(query, parameters, false, conn, tx);
                            tx.Commit();
                        }
                        catch
                        {
                            // 💣 Algo explotó → rollback real
                            tx.Rollback();
                            throw;
                        }
                    }
                }
                return Json(new { success = true, message = "Documento creado exitosamente.", folio_generado = folio["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNRemision/?");
                if (idEncabezadoGenerado > 0)
                {
                    //BorradoFacturasIncorrectas(idEncabezadoGenerado, "remision");
                }
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Predicados de "surtido completo" (mismos criterios que VISeguimiento) ──────
        // Un documento de stock/modula está completo cuando TODAS sus partidas tienen
        // fecha_fin_surtido. NULL (documento inexistente) cuenta como completo (no aplica).
        private static string DocCompletoSql(string idExpr) => $@"({idExpr} IS NULL OR (
            EXISTS (SELECT 1 FROM partidasdoc p WHERE p.encabezado_id = {idExpr})
            AND NOT EXISTS (
                SELECT 1 FROM partidasdoc p
                WHERE p.encabezado_id = {idExpr}
                  AND NOT EXISTS (SELECT 1 FROM verificacion_detalle vd
                                  WHERE vd.id_partida = p.id_partidas AND vd.fecha_fin_surtido IS NOT NULL))
        ))";

        // Un pedido de tubo está completo cuando TODOS sus cortes están confirmados.
        private static string TuboCompletoSql(string idExpr) => $@"({idExpr} IS NULL OR (
            EXISTS (SELECT 1 FROM pedido_detalle_corte c
                    INNER JOIN partidasdoc pdc ON pdc.id_partidas = c.pedido_detalle_id
                    WHERE pdc.encabezado_id = {idExpr})
            AND NOT EXISTS (
                SELECT 1 FROM pedido_detalle_corte c
                INNER JOIN partidasdoc pdc ON pdc.id_partidas = c.pedido_detalle_id
                WHERE pdc.encabezado_id = {idExpr}
                  AND NOT EXISTS (SELECT 1 FROM pedido_detalle_corte_asignacion a
                                  WHERE a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado'))
        ))";

        // RunQuery devuelve las columnas NULL como C# `null` (no DBNull.Value); esta ayuda
        // colapsa ambos casos a int? null y evita que Convert.ToInt32(null) devuelva 0.
        private static int? IntOrNull(object v)
            => (v == null || v == DBNull.Value) ? (int?)null : Convert.ToInt32(v);

        // Resuelve el pedido de tubo REAL (TYBPED) a partir de la cotización de tubo (TYBCOT).
        private int? ResolverTuboReal(int? idTuboCot)
        {
            if (!idTuboCot.HasValue) return null;
            // El pedido de tubo (hijo del TYBCOT) nace como 'VIPED' y sólo se vuelve 'TYBPED'
            // al aprobar crédito; se acepta cualquiera de los dos.
            var r = RunQuery(@"
                SELECT id_encabezado FROM encabezadomov
                WHERE encabezados_padre = @id AND nat IN ('TYBPED', 'VIPED')
                ORDER BY CASE WHEN nat = 'TYBPED' THEN 0 ELSE 1 END, id_encabezado DESC LIMIT 1",
                new Dictionary<string, object> { { "id", idTuboCot.Value } });
            return r?.Count > 0 ? Convert.ToInt32(r[0]["id_encabezado"]) : idTuboCot;
        }

        // ─── Buscador de pedidos LISTOS para remisión ───────────────────────────────
        // Solo lista grupos (padre) cuyos surtidos (normal + tubo + modula) están TODOS
        // completos y que aún no tienen remisión generada.
        [HttpGet]
        public IActionResult BuscarPedidosListos(string nombre = "", int page = 1, int pageSize = 50)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                string query = $@"
WITH grupos AS (
    SELECT
        dr.id_encabezado_padre AS id_grupo,
        dr.id_encabezado_normal,
        dr.id_encabezado_modula,
        COALESCE(etr.id_encabezado, dr.id_encabezado_tubo) AS id_tubo_real,
        COALESCE(en.cli_prov, em.cli_prov, et.cli_prov)    AS cli_prov,
        COALESCE(en.fch, em.fch, et.fch)                   AS fch,
        COALESCE(en.imp,0) + COALESCE(em.imp,0) + COALESCE(etr.imp, et.imp, 0) AS imp,
        COALESCE(en.usr_doc, em.usr_doc, et.usr_doc)       AS usr_doc,
        CONCAT_WS(' + ',
            NULLIF(dr.folio_normal, ''),
            NULLIF(COALESCE(etr.folio, dr.folio_tubo), ''),
            NULLIF(dr.folio_modula, '')
        ) AS folio_combinado
    FROM documentos_relacionados dr
    LEFT JOIN encabezadomov en ON en.id_encabezado = dr.id_encabezado_normal
    LEFT JOIN encabezadomov em ON em.id_encabezado = dr.id_encabezado_modula
    LEFT JOIN encabezadomov et ON et.id_encabezado = dr.id_encabezado_tubo
    LEFT JOIN encabezadomov ep ON ep.id_encabezado = dr.id_encabezado_padre
    LEFT JOIN LATERAL (
        SELECT eht.id_encabezado, eht.folio, eht.imp
        FROM encabezadomov eht
        WHERE eht.encabezados_padre = dr.id_encabezado_tubo AND eht.nat IN ('TYBPED', 'VIPED')
        ORDER BY CASE WHEN eht.nat = 'TYBPED' THEN 0 ELSE 1 END, eht.id_encabezado DESC LIMIT 1
    ) etr ON dr.id_encabezado_tubo IS NOT NULL
    WHERE dr.empresa_id = @empresa_id
      -- Solo grupos del canal NACIONAL. documentos_relacionados es COMPARTIDO:
      -- los splits industriales (padre VICOT / normal VIPED) también viven aquí y
      -- se colaban en la remisión nacional. El padre del grupo distingue el canal.
      AND ep.nat IN ('VNCOT', 'VNPED')
)
SELECT
    g.id_grupo       AS id_encabezado,
    g.folio_combinado AS folio,
    g.cli_prov,
    g.fch            AS fecha,
    g.imp,
    g.usr_doc        AS usr0,
    cc.n_cli
FROM grupos g
INNER JOIN catclientes cc ON cc.cve_cli = g.cli_prov AND cc.empresa_id = @empresa_id
WHERE {DocCompletoSql("g.id_encabezado_normal")}
  AND {DocCompletoSql("g.id_encabezado_modula")}
  AND {TuboCompletoSql("g.id_tubo_real")}
  -- Al menos un documento debe tener surtido (evita listar grupos vacíos/sin iniciar)
  AND (
        EXISTS (SELECT 1 FROM verificacion_detalle vd
                INNER JOIN partidasdoc p ON p.id_partidas = vd.id_partida
                WHERE p.encabezado_id IN (g.id_encabezado_normal, g.id_encabezado_modula)
                  AND vd.fecha_fin_surtido IS NOT NULL)
        OR g.id_tubo_real IS NOT NULL
      )
  -- Aún no remisionado
  AND NOT EXISTS (SELECT 1 FROM encabezadomov r
                  WHERE r.nat = 'VNREM' AND r.encabezados_padre = g.id_grupo)
  AND (
        LOWER(g.folio_combinado) LIKE LOWER(@nombre)
     OR LOWER(g.cli_prov)        LIKE LOWER(@nombre)
     OR LOWER(cc.n_cli)          LIKE LOWER(@nombre)
      )
ORDER BY g.fch DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var parameters = new Dictionary<string, object>
                {
                    { "empresa_id", empresaId },
                    { "nombre", $"%{nombre}%" },
                    { "offset", (page - 1) * pageSize },
                    { "pageSize", pageSize }
                };

                var items = RunQuery(query, parameters);
                return Json(new { items, total = items.Count });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNRemision/BuscarPedidosListos");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Consolidar los hijos de un pedido en un documento de remisión ──────────────
        // Devuelve [documento] (mismo formato que BuscarDocumento) con las partidas de
        // normal + tubo + modula juntas, usando la cantidad surtida. Solo si TODO está
        // completo; si no, devuelve [{ error:true, message }].
        [HttpGet]
        public IActionResult ObtenerPedidoParaRemision(int id)
        {
            try
            {
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                // 1. Resolver el grupo (padre + hijos). id puede ser el padre o cualquier hijo.
                var rel = RunQuery(@"
                    SELECT id_encabezado_padre, id_encabezado_normal, id_encabezado_tubo, id_encabezado_modula,
                           folio_normal, folio_tubo, folio_modula
                    FROM documentos_relacionados
                    WHERE (@id IN (id_encabezado_padre, id_encabezado_normal, id_encabezado_tubo, id_encabezado_modula))
                      AND empresa_id = @empresa_id
                    ORDER BY id_relacion DESC LIMIT 1",
                    new Dictionary<string, object> { { "id", id }, { "empresa_id", empresaId } });

                int idGrupo;
                int? idNormal, idTuboCot, idModula;

                if (rel?.Count > 0)
                {
                    var r = rel[0];
                    idGrupo = Convert.ToInt32(r["id_encabezado_padre"]);
                    // ★ Un tipo ausente en documentos_relacionados debe quedar como NULL. RunQuery
                    //   devuelve las columnas NULL como C# `null` (NO DBNull.Value), por lo que el
                    //   check `!= DBNull.Value` era SIEMPRE verdadero y `Convert.ToInt32(null)`
                    //   devolvía 0 → un id 0 "existe" y bloqueaba la remisión pidiendo un surtido
                    //   de un documento que no existe. IntOrNull trata tanto null como DBNull.
                    idNormal = IntOrNull(r["id_encabezado_normal"]);
                    idTuboCot = IntOrNull(r["id_encabezado_tubo"]);
                    idModula = IntOrNull(r["id_encabezado_modula"]);
                }
                else
                {
                    // Fallback: pedido suelto (legacy sin split)
                    idGrupo = id;
                    idNormal = id;
                    idTuboCot = null;
                    idModula = null;
                }

                int? idTubo = ResolverTuboReal(idTuboCot);

                // 2. Validar que TODO esté completo.
                //    ★ Un pedido NO siempre tiene los 3 tipos: puede ser solo normal, solo modula,
                //    solo tubo o cualquier combinación. Los tipos ausentes llegan como NULL.
                //    Los predicados ya tratan NULL como "no aplica / completo" (`{id} IS NULL OR ...`),
                //    PERO hay que castear el parámetro a ::int: si se manda un NULL sin tipo (DBNull),
                //    Postgres no puede inferir el tipo del parámetro dentro de los subqueries EXISTS
                //    y la query REVIENTA ("no se pudo determinar el tipo del parámetro"), lo que hacía
                //    fallar la carga de cualquier pedido al que le faltara un tipo.
                var okResult = RunQuery($@"
                    SELECT {DocCompletoSql("@id_normal::int")} AS n_ok,
                           {TuboCompletoSql("@id_tubo::int")}  AS t_ok,
                           {DocCompletoSql("@id_modula::int")} AS m_ok",
                    new Dictionary<string, object>
                    {
                        { "id_normal", (object)idNormal ?? DBNull.Value },
                        { "id_tubo",   (object)idTubo   ?? DBNull.Value },
                        { "id_modula", (object)idModula ?? DBNull.Value }
                    });

                bool nOk = okResult.Count > 0 && Convert.ToBoolean(okResult[0]["n_ok"]);
                bool tOk = okResult.Count > 0 && Convert.ToBoolean(okResult[0]["t_ok"]);
                bool mOk = okResult.Count > 0 && Convert.ToBoolean(okResult[0]["m_ok"]);

                if (!nOk || !tOk || !mOk)
                {
                    var pendientes = new List<string>();
                    if (idNormal.HasValue && !nOk) pendientes.Add("Normal");
                    if (idTubo.HasValue && !tOk) pendientes.Add("Tubo");
                    if (idModula.HasValue && !mOk) pendientes.Add("Modula");

                    return Json(new[] { new
                    {
                        error = true,
                        message = "El pedido aún tiene surtidos pendientes en: " +
                                  string.Join(", ", pendientes) +
                                  ". Complétalos antes de generar la remisión."
                    }});
                }

                // 3. Lista de ids hijos existentes
                var idsList = new List<int>();
                if (idNormal.HasValue) idsList.Add(idNormal.Value);
                if (idTubo.HasValue) idsList.Add(idTubo.Value);
                if (idModula.HasValue) idsList.Add(idModula.Value);
                if (idsList.Count == 0)
                    return Json(new[] { new { error = true, message = "El pedido no tiene documentos asociados." } });

                int idPrimario = idNormal ?? idModula ?? idTubo.Value;

                // 4. Encabezado de referencia (del primer hijo disponible)
                var encResult = RunQuery(@"
                    SELECT
                        em.id_encabezado, em.suc, em.alm, em.cli_prov, em.ccy, em.par, em.vdr_cpr,
                        em.coment1, em.coment_aut, em.mdp, em.tipo_proceso, em.flete, em.cfdi,
                        em.orden_compra AS ordenCompra,
                        -- Condiciones de pago capturadas en el pedido. Sin ellas la remisión
                        -- caía al plazo del catálogo del cliente y dejaba la fecha de pago
                        -- vacía, así que el guardado la rechazaba pidiéndola de nuevo.
                        em.pl_dias,
                        TO_CHAR(em.fch_pg_entrega, 'YYYY-MM-DD') AS fecha_pago,
                        cfp.cve_sat AS f_pago,
                        em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc AS folio,
                        cc.rfc, cc.n_cli, cc.lim_crd, cc.pl_crd, cc.id_cliente,
                        COALESCE(cc.dir,'') || CHR(10) || COALESCE(cc.col,'') || CHR(10) ||
                        COALESCE(cc.pob,'') || CHR(10) || COALESCE(cc.cp,'') AS info_cli
                    FROM encabezadomov em
                    LEFT JOIN catclientes cc ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
                    LEFT JOIN cat_f_pago cfp ON cfp.id_f_pago = em.f_pago
                    WHERE em.id_encabezado = @id_primario",
                    new Dictionary<string, object> { { "id_primario", idPrimario }, { "empresa_id", empresaId } });

                if (encResult == null || encResult.Count == 0)
                    return Json(new[] { new { error = true, message = "No se encontró el encabezado del pedido." } });

                var enc = encResult[0];

                // 5. Partidas consolidadas de todos los hijos, con la cantidad surtida.
                //    Se incluye la existencia (Stock + Modula) de la sucursal para que la tabla
                //    de la remisión la muestre igual que al capturar el pedido.
                var partidas = RunQuery($@"
                    SELECT
                        pd.cve_prod AS producto_id,
                        pd.descr_prod AS descripcion,
                        COALESCE((
                            SELECT vd.cantidad_verificada FROM verificacion_detalle vd
                            WHERE vd.id_partida = pd.id_partidas AND vd.encabezado_verificacion_id IS NULL
                            ORDER BY vd.id DESC LIMIT 1
                        ), pd.cant_ud) AS cantidad,
                        pd.pv_prod AS precio,
                        pd.dto1 AS descuento,
                        pd.ud AS unidad,
                        pd.tp_doc_ant AS comentario,
                        {SqlExistencia("pd.producto_id", "Stock")}  AS existencia_stock,
                        {SqlExistencia("pd.producto_id", "Modula")} AS existencia_modula,
                        {SqlExistencia("pd.producto_id", "Stock")}
                            + {SqlExistencia("pd.producto_id", "Modula")} AS existencia
                    FROM partidasdoc pd
                    WHERE pd.encabezado_id = ANY(@ids)
                    ORDER BY
                        CASE WHEN pd.encabezado_id = @id_normal THEN 1
                             WHEN pd.encabezado_id = @id_tubo   THEN 2
                             ELSE 3 END,
                        pd.nro_part",
                    new Dictionary<string, object>
                    {
                        { "ids", idsList.ToArray() },
                        { "id_normal", (object)idNormal ?? -1 },
                        { "id_tubo",   (object)idTubo   ?? -1 },
                        { "sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }
                    });

                // Folio combinado para mostrar en el form
                string folioCombinado = "";
                if (rel?.Count > 0)
                {
                    var partes = new List<string>();
                    string fn = rel[0]["folio_normal"]?.ToString();
                    string ft = rel[0]["folio_tubo"]?.ToString();
                    string fm = rel[0]["folio_modula"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(fn)) partes.Add(fn);
                    if (!string.IsNullOrWhiteSpace(ft)) partes.Add(ft);
                    if (!string.IsNullOrWhiteSpace(fm)) partes.Add(fm);
                    folioCombinado = string.Join(" + ", partes);
                }
                if (string.IsNullOrWhiteSpace(folioCombinado))
                    folioCombinado = enc["folio"]?.ToString();

                // 6. Documento consolidado. id_encabezado = grupo (padre) para que la remisión
                //    quede ligada al pedido y éste ya no vuelva a listarse.
                enc["id_encabezado"] = idGrupo;
                enc["folio"] = folioCombinado;
                enc["productos"] = partidas;

                return Json(new[] { enc });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNRemision/?");
                return Json(new[] { new { error = true, message = ex.Message } });
            }
        }

    }
}