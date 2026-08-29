using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using System.Configuration;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    [Authorize]
    public class VIRemisionController : Utilities
    {

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industriales", Accion = "Creacion de remision")]
        public JsonResult Guardar(IFormCollection fc)
        {
            int idEncabezadoGenerado = 0;
            var foliosGenerados = new List<string>();

            try
            {
                var parameters = new Dictionary<string, object>();
                string tipo = fc["tipo"].ToString()?.ToLower() ?? "";

                // ─── Validaciones previas ────────────────────────────────────────────
                if (string.IsNullOrWhiteSpace(fc["cliente"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un cliente." });
                if (string.IsNullOrWhiteSpace(fc["moneda"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una moneda." });
                if (string.IsNullOrWhiteSpace(fc["vendedor"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar un vendedor." });
                if (string.IsNullOrWhiteSpace(fc["forma-pago"].ToString()))
                    return Json(new { success = false, message = "Debe seleccionar una forma de pago." });

                // ─── Deserializar productos ──────────────────────────────────────────
                List<Dictionary<string, string>> productos = new List<Dictionary<string, string>>();
                if (tipo != "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["productosJSON"].ToString()))
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });

                    productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(fc["productosJSON"].ToString());
                    if (productos == null || productos.Count == 0)
                        return Json(new { success = false, message = "Debe agregar al menos un producto." });
                }

                // ─── Reglas de precio ────────────────────────────────────────────────
                // El front ya bloquea los campos y manda los tokens, pero hasta ahora el
                // servidor los ignoraba y la remisión directa saltaba la regla.
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

                // ─── Fecha de pago / anticipo ────────────────────────────────────────
                DateTime? fechaPago = null;
                if (tipo == "credito")
                {
                    //if (string.IsNullOrWhiteSpace(fc["fechaPago"].ToString()))
                    //    return Json(new { success = false, message = "Debe ingresar la fecha de pago (solo para crédito)." });
                    //if (!DateTime.TryParse(fc["fechaPago"].ToString(), out DateTime fch))
                    //    return Json(new { success = false, message = "Formato de fecha de pago no válido." });
                    //fechaPago = fch;
                }
                else if (tipo == "anticipo")
                {
                    if (string.IsNullOrWhiteSpace(fc["fechaAnticipo"].ToString()))
                        return Json(new { success = false, message = "Debe ingresar la fecha del anticipo." });
                    if (!DateTime.TryParse(fc["fechaAnticipo"].ToString(), out DateTime fch))
                        return Json(new { success = false, message = "Formato de fecha del anticipo no válido." });
                    fechaPago = fch;
                }

                // Se conserva la fecha de pago que venga del formulario (la validación de
                // arriba está desactivada a propósito): sin esto el encabezado guardaba
                // DateTime.Now y la factura heredaba una fecha de pago equivocada.
                if (fechaPago == null && DateTime.TryParse(fc["fechaPago"].ToString(), out DateTime fchPagoForm))
                    fechaPago = fchPagoForm;

                // ─── Validar crédito — solo aplica cuando la venta es a crédito ──────
                var credito = this.ValidarCreditoVenta(
                    fc["cliente"].ToString(),
                    Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    CreditoVentasHelper.TotalDocumentoParaCredito(fc),
                    tipo,
                    int.TryParse(fc["documentid"].ToString(), out int docCredito) ? docCredito : 0,
                    null, null, fc["creditoToken"].ToString());

                if (!credito.Permitido)
                    return Json(new { success = false, message = credito.Mensaje });

                // ─── Datos del encabezado padre (si existe) ──────────────────────────
                int idEncabezadoPadre = 0;
                int usrId0 = 0, usrId1 = 0;
                DateTime usrFch0 = DateTime.Now, usrFch1 = DateTime.Now;

                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                {
                    var usrParameter = new Dictionary<string, object> { { "id", Convert.ToInt32(fc["documentid"].ToString()) } };
                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.fch1 FROM encabezadomov em WHERE em.id_encabezado = @id";
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

                // ─── Datos del cliente ───────────────────────────────────────────────
                int idCliente = 0;
                string clienteNombre = "";
                if (!string.IsNullOrEmpty(fc["cliente"].ToString()))
                {
                    var clienteParameter = new Dictionary<string, object>
            {
                { "cliente", fc["cliente"].ToString() },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };
                    var clienteResult = RunQuery(
                        "SELECT cl.id_cliente, cl.n_cli FROM catclientes cl WHERE cl.cve_cli = @cliente AND cl.empresa_id = @empresa_id",
                        clienteParameter);
                    if (clienteResult.Count > 0)
                    {
                        idCliente = Convert.ToInt32(clienteResult[0]["id_cliente"]);
                        clienteNombre = clienteResult[0]["n_cli"].ToString();
                    }
                }

                // ─── Forma de pago ───────────────────────────────────────────────────
                var fPagoParameter = new Dictionary<string, object> { { "cve_sat", fc["forma-pago"].ToString() } };
                int fpago = Convert.ToInt32(RunScalar("SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat", fPagoParameter));

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // ════════════════════════════════════════════════════════
                            // PASO 1: Resolver distribución de stock por almacén
                            // ════════════════════════════════════════════════════════
                            // Estructura: almacenId → lista de asignaciones de producto/tarima
                            // AlmacenAsignacion: { almacenId, productoId, productoData, tarimas: [{tarimaId, cantidad}] }

                            var distribucion = ResolverDistribucionPorAlmacen(
                                productos, fc, conn, tx);

                            if (!distribucion.Valida)
                                return Json(new { success = false, message = distribucion.MensajeError });

                            // ════════════════════════════════════════════════════════
                            // PASO 2: Generar un documento por almacén
                            // ════════════════════════════════════════════════════════
                            int userId = GetUserId(User.Identity.Name);

                            foreach (var almacenGrupo in distribucion.GruposPorAlmacen)
                            {
                                // Calcular totales para este subconjunto de productos
                                var totalesAlmacen = CalcularTotalesGrupo(almacenGrupo.Partidas);

                                var encabezado = new DocumentoEncabezado
                                {
                                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                                    IdArea = 13,
                                    IdTpDoc = 47,
                                    UsrDep = GetAreaName(User.Identity.Name),
                                    Anio = DateTime.Now.Year,
                                    Suc = Convert.ToInt32(fc["sucursal"].ToString()),
                                    Alm = almacenGrupo.AlmacenClave,   // ← almacén específico
                                    Fch = DateTime.Now,
                                    TpMov = "VIREM",
                                    ComentAut = fc["comentarios"].ToString(),
                                    UsrDoc = User.Identity.Name,
                                    FchCap = DateTime.Now,
                                    Usr0 = usrId0,
                                    Fch0 = usrFch0,
                                    Usr1 = usrId1,
                                    Fch1 = usrFch1,
                                    Usr2 = GetUserId(User.Identity.Name),
                                    Fch2 = DateTime.Now,
                                    Imp = totalesAlmacen.Total,
                                    Dto = totalesAlmacen.Descuento,
                                    CliProv = fc["cliente"].ToString(),
                                    Ref = idCliente,
                                    Ccy = fc["moneda"].ToString(),
                                    Estatus = 1,
                                    Flete = almacenGrupo.EsPrimero
                                                    ? Convert.ToDecimal(fc["flete"].ToString())  // flete solo en primera remisión
                                                    : 0,
                                    VdrCpr = fc["vendedor"].ToString(),
                                    Coment1 = fc["concepto"].ToString(),
                                    EncabezadoPadre = idEncabezadoPadre,
                                    PlDias = string.IsNullOrWhiteSpace(fc["plazo"].ToString()) ? 0 : Convert.ToInt32(fc["plazo"].ToString()),
                                    FchPgEntrega = fechaPago ?? DateTime.Now,
                                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                                    FPago = fpago,
                                    Mdp = fc["metodo-pago"].ToString(),
                                    TipoPoceso = "remision_" + tipo,
                                    CFDI = fc["uso-cfdi"].ToString(),
                                    NatDocPadreChar = fc["ordenCompra"].ToString()
                                };

                                // Partidas del almacén
                                var partidas = new List<PartidaDocumento>();
                                foreach (var asign in almacenGrupo.Partidas)
                                {
                                    partidas.Add(new PartidaDocumento
                                    {
                                        CveProd = asign.CodigoProducto,
                                        DescrProd = asign.Descripcion,
                                        CantUd = asign.CantidadTotal,
                                        PvProd = asign.PrecioUnitario,
                                        Dto1 = asign.Descuento,
                                        ImpPart = asign.CantidadTotal * asign.CostoUnitario,
                                        Ud = asign.Unidad,
                                        IdProducto = asign.IdProducto,
                                        TpDocAnt = asign.Comentario
                                    });
                                }

                                // Guardar documento
                                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                                int idEncActual = Convert.ToInt32(folio["IdEncabezado"]);
                                foliosGenerados.Add(folio["folio_generado"].ToString());
                                idEncabezadoGenerado = idEncActual;

                                // ── Registrar movimientos de salida por tarima ────────
                                foreach (var asign in almacenGrupo.Partidas)
                                {
                                    foreach (var tarimaAsign in asign.TarimasAsignadas)
                                    {
                                        var prodMovimiento = new Dictionary<string, object>
                                {
                                    { "id_producto",  asign.IdProducto },
                                    { "codigo",       asign.CodigoProducto },
                                    { "descripcion",  asign.Descripcion },
                                    { "cantidad",     tarimaAsign.Cantidad },
                                    { "unidad",       asign.IdUdm },
                                    { "tarima_id",    tarimaAsign.TarimaId },
                                    { "tipo",         "venta" },
                                    { "movimiento",   "salida" }
                                };

                                        RegistrarMovimiento(
                                            new List<Dictionary<string, object>> { prodMovimiento },
                                            userId, "venta",
                                            tarimaAsign.TarimaId,
                                            null, "salida",
                                            idEncActual,
                                            null,
                                            conn, tx);
                                    }
                                }
                                
                                // ── Registrar impuestos ───────────────────────────────
                                var inpuestosParameter = new Dictionary<string, object>
                        {
                            { "encabezado_id", idEncActual },
                            { "impuesto_id",   Convert.ToInt32(GetSetting("impuesto")) },
                            { "subtotal",      totalesAlmacen.Subtotal },
                            { "importe",       totalesAlmacen.Iva },
                            { "orden_apl",     1 },
                            { "imp_variable",  16 },
                            { "prov_nom",      clienteNombre },
                            { "f_pago_id",     fpago }
                        };

                                RunQuery(
                                    "INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
                                    "VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);",
                                    inpuestosParameter, false, conn, tx);

                                RegistrarCompra(idEncActual, userId, conn, tx);
                            }

                            // ── Cerrar documento padre ────────────────────────────────
                            if (idEncabezadoPadre > 0)
                            {
                                var paramsUpdate = new Dictionary<string, object> { { "id", idEncabezadoPadre } };
                                RunUpdate("UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id",
                                          paramsUpdate, false, conn, tx);
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                string mensajeFolios = foliosGenerados.Count == 1
                    ? $"Remisión generada: {foliosGenerados[0]}"
                    : $"Se generaron {foliosGenerados.Count} remisiones por almacén: {string.Join(", ", foliosGenerados)}";

                return Json(new
                {
                    success = true,
                    message = "",//mensajeFolios,
                    folio_generado = string.Join(", ", foliosGenerados),
                    total_remisiones = foliosGenerados.Count
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VIRemision/?");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ================================================================
        // MODELOS AUXILIARES (agrégalos en la misma clase o en Models/)
        // ================================================================

        private class TarimaAsignacion
        {
            public int TarimaId { get; set; }
            public decimal Cantidad { get; set; }
        }

        private class ProductoAsignacion
        {
            public string CodigoProducto { get; set; }
            public string Descripcion { get; set; }
            public int IdProducto { get; set; }
            public int IdUdm { get; set; }
            public string Unidad { get; set; }
            public decimal CantidadTotal { get; set; }
            public decimal PrecioUnitario { get; set; }
            public decimal CostoUnitario { get; set; }
            public decimal Descuento { get; set; }
            public string Comentario { get; set; }
            public string IdFilaOrigen { get; set; }   // ← NUEVO: identifica la fila exacta del formulario
            public List<TarimaAsignacion> TarimasAsignadas { get; set; } = new List<TarimaAsignacion>();
        }
        private class AlmacenGrupo
        {
            public int AlmacenId { get; set; }
            public string AlmacenClave { get; set; }
            public bool EsPrimero { get; set; }
            public List<ProductoAsignacion> Partidas { get; set; } = new List<ProductoAsignacion>();
        }

        private class DistribucionResult
        {
            public bool Valida { get; set; }
            public string MensajeError { get; set; }
            public List<AlmacenGrupo> GruposPorAlmacen { get; set; } = new List<AlmacenGrupo>();
        }

        private class TotalesGrupo
        {
            public decimal Subtotal { get; set; }
            public decimal Descuento { get; set; }
            public decimal Iva { get; set; }
            public decimal Total { get; set; }
        }


        // ================================================================
        // MÉTODO PRINCIPAL: ResuelveDistribuciónPorAlmacén
        // ================================================================

        private DistribucionResult ResolverDistribucionPorAlmacen(
            List<Dictionary<string, string>> productos,
            IFormCollection fc,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var result = new DistribucionResult { Valida = true };

            // almacenId → AlmacenGrupo
            var grupos = new Dictionary<int, AlmacenGrupo>();
            int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
            int sucursalId = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

            foreach (var p in productos)
            {
                string cveProd = p.ContainsKey("productoId") ? p["productoId"] : "";
                decimal cantidadSolicitada = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0;

                // ── Obtener ID y datos del producto ──────────────────────────────
                var paramProd = new Dictionary<string, object>
        {
            { "cve_prod",   cveProd },
            { "empresa_id", empresaId }
        };
                var prodInfo = RunQuery(
                    "SELECT id_catproductos, cve_prod, descr_prod FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id",
                    paramProd, false, conn, tx);

                if (prodInfo.Count == 0)
                {
                    result.Valida = false;
                    result.MensajeError = $"Producto no encontrado: {cveProd}";
                    return result;
                }

                int idProducto = Convert.ToInt32(prodInfo[0]["id_catproductos"]);
                string descripcion = prodInfo[0]["descr_prod"].ToString();
                string unidad = p.ContainsKey("unidad") ? p["unidad"] : "PZA";

                // ── Obtener ID de unidad ──────────────────────────────────────────
                int idUdm = 0;
                try
                {
                    var paramUdm = new Dictionary<string, object> { { "cve_udm", unidad } };
                    idUdm = Convert.ToInt32(RunScalar("SELECT id_udm FROM catunidades WHERE cve_udm = @cve_udm", paramUdm, false, conn, tx));
                }
                catch { /* unidad no encontrada, se deja en 0 */ }

                // ── Consultar stock por almacén y tarima ──────────────────────────
                //    Ordena: primero tarimas que cubren la cantidad exacta,
                //            luego por mayor stock disponible
                var paramStock = new Dictionary<string, object>
        {
            { "sucursal",   sucursalId },
            { "producto_id", idProducto }
        };

                string queryStock =
                    @"SELECT ct.id_tarima,
                     tp.cantidad        AS stock,
                     c.id_almacen,
                     c.cve_almacen as cve_alm, 
                     cp.udm
              FROM   catalmacenes c
              INNER JOIN catsucursales   cs ON cs.id_sucursal  = c.sucursal_id
              INNER JOIN catracks        cr ON cr.almacen_id   = c.id_almacen
              INNER JOIN catcolumnas     cc ON cc.rack_id      = cr.id_rack
              INNER JOIN catniveles      cn ON cn.columna_id   = cc.id_columna
              INNER JOIN cattarimas      ct ON ct.nivel_id     = cn.id_nivel
              INNER JOIN tarima_productos tp ON tp.tarima_id   = ct.id_tarima
              INNER JOIN catproductos    cp ON cp.id_catproductos = tp.producto_id
              WHERE  cs.id_sucursal = @sucursal
                AND  c.tipo        = 'Stock'
                AND  tp.producto_id = @producto_id
                AND  tp.cantidad   > 0
          
              ORDER BY
                -- 1er criterio: priorizar almacén que cubre la cantidad exacta en una sola tarima
                CASE WHEN tp.cantidad >= @cantidad THEN 0 ELSE 1 END,
                -- 2do criterio: mayor stock disponible
                tp.cantidad DESC";

                paramStock["cantidad"] = cantidadSolicitada;

                var stockRows = RunQuery(queryStock, paramStock, false, conn, tx);

                if (unidad == "SRV")
                {
                    // Los servicios no manejan stock ni tarimas, se agregan directamente
                    // al primer almacén del grupo (o a uno ficticio si aún no hay grupos)
                    int almacenServicio = grupos.Count > 0
                        ? grupos.Keys.First()
                        : -1; // ID ficticio para servicios sin almacén físico

                    string claveServicio = grupos.Count > 0
                        ? grupos[almacenServicio].AlmacenClave
                        : "SRV";

                    var productoSrv = NuevoProductoAsignacion(
                        p, idProducto, unidad, idUdm, descripcion, cantidadSolicitada);

                    // Sin tarimas asignadas (lista vacía)
                    AgregarAsignacionAlGrupo(
                        grupos,
                        almacenServicio,
                        claveServicio,
                        productoSrv,
                        new List<TarimaAsignacion>());

                    continue;
                }

                if (stockRows.Count == 0)
                {
                    result.Valida = false;
                    result.MensajeError = $"Sin stock disponible para el producto: {cveProd}";
                    return result;
                }

                // ── Estrategia de asignación ──────────────────────────────────────
                //
                //  1. Intentar satisfacer con UN SOLO almacén (el primero que tenga
                //     stock suficiente en conjunto).
                //  2. Si no es posible, distribuir entre almacenes tomando del de
                //     mayor stock primero (greedy).
                //
                decimal restante = cantidadSolicitada;

                // Agrupar stock por almacén para evaluar cobertura total
                var stockPorAlmacen = stockRows
                    .GroupBy(r => Convert.ToInt32(r["id_almacen"]))
                    .Select(g => new
                    {
                        AlmacenId = g.Key,
                        AlmacenClave = g.First()["cve_alm"].ToString(),
                        StockTotal = g.Sum(r => Convert.ToDecimal(r["stock"])),
                        Tarimas = g.OrderByDescending(r => Convert.ToDecimal(r["stock"])).ToList()
                    })
                    .OrderByDescending(a => a.StockTotal)
                    .ToList();

                // ¿Algún almacén cubre la cantidad completa?
                var almacenCompleto = stockPorAlmacen.FirstOrDefault(a => a.StockTotal >= cantidadSolicitada);

                if (almacenCompleto != null)
                {
                    // ── Caso A: un único almacén cubre todo ───────────────────────
                    // Dentro del almacén, usar la tarima que mejor se ajusta:
                    // primero la que cubra exacto; luego la de mayor stock.
                    var tarimasAlmacen = almacenCompleto.Tarimas;

                    var asignaciones = AsignarDesdeTarimas(tarimasAlmacen, cantidadSolicitada,
                                                           r => Convert.ToInt32(r["id_tarima"]),
                                                           r => Convert.ToDecimal(r["stock"]));

                    AgregarAsignacionAlGrupo(
                        grupos,
                        almacenCompleto.AlmacenId,
                        almacenCompleto.AlmacenClave,
                        NuevoProductoAsignacion(p, idProducto, unidad, idUdm, descripcion, cantidadSolicitada),
                        asignaciones);
                }
                else
                {
                    // ── Caso B: distribuir entre múltiples almacenes (greedy) ─────
                    foreach (var almacen in stockPorAlmacen)
                    {
                        if (restante <= 0) break;

                        decimal cantParaEsteAlmacen = Math.Min(almacen.StockTotal, restante);
                        restante -= cantParaEsteAlmacen;

                        var asignaciones = AsignarDesdeTarimas(almacen.Tarimas, cantParaEsteAlmacen,
                                                               r => Convert.ToInt32(r["id_tarima"]),
                                                               r => Convert.ToDecimal(r["stock"]));

                        AgregarAsignacionAlGrupo(
                            grupos,
                            almacen.AlmacenId,
                            almacen.AlmacenClave,
                            NuevoProductoAsignacion(p, idProducto, unidad, idUdm, descripcion, cantParaEsteAlmacen),
                            asignaciones);
                    }

                    // Stock insuficiente global: avisar pero no bloquear
                    if (restante > 0)
                    {
                        // Opcional: puedes cambiar esto a return error si prefieres bloquear
                        throw new Exception($"⚠️ Stock insuficiente para {cveProd}. Faltaron {restante} unidades.");
                    }
                }
            }

            // Marcar el primer grupo (para asignar el flete solo ahí)
            bool primero = true;
            foreach (var grupo in grupos.Values)
            {
                grupo.EsPrimero = primero;
                primero = false;
                result.GruposPorAlmacen.Add(grupo);
            }

            return result;
        }


        // ================================================================
        // HELPERS
        // ================================================================

        private List<TarimaAsignacion> AsignarDesdeTarimas<T>(
            IEnumerable<T> tarimas,
            decimal cantidadNecesaria,
            Func<T, int> getId,
            Func<T, decimal> getStock)
        {
            var asignaciones = new List<TarimaAsignacion>();
            decimal restante = cantidadNecesaria;

            // Ordenar: primero la que cubra exacto, luego mayor stock
            var ordenadas = tarimas
                .OrderBy(t => getStock(t) >= cantidadNecesaria ? 0 : 1)
                .ThenByDescending(t => getStock(t));

            foreach (var tarima in ordenadas)
            {
                if (restante <= 0) break;

                decimal stock = getStock(tarima);
                decimal descontar = Math.Min(stock, restante);
                restante -= descontar;

                asignaciones.Add(new TarimaAsignacion
                {
                    TarimaId = getId(tarima),
                    Cantidad = descontar
                });
            }

            return asignaciones;
        }

        private void AgregarAsignacionAlGrupo(
     Dictionary<int, AlmacenGrupo> grupos,
     int almacenId, string almacenClave,
     ProductoAsignacion nueva,
     List<TarimaAsignacion> tarimas)
        {
            if (!grupos.ContainsKey(almacenId))
            {
                grupos[almacenId] = new AlmacenGrupo
                {
                    AlmacenId = almacenId,
                    AlmacenClave = almacenClave
                };
            }

            // Fusionar SOLO si es la MISMA fila de origen repartida en varias tarimas
            // del mismo almacén (no fusionar por IdProducto, eso uniría partidas distintas)
            var existente = grupos[almacenId].Partidas
                .FirstOrDefault(x => x.IdFilaOrigen == nueva.IdFilaOrigen);

            if (existente != null)
            {
                existente.CantidadTotal += nueva.CantidadTotal;
                existente.TarimasAsignadas.AddRange(tarimas);
            }
            else
            {
                nueva.TarimasAsignadas = tarimas;
                grupos[almacenId].Partidas.Add(nueva);
            }
        }

        private ProductoAsignacion NuevoProductoAsignacion(
     Dictionary<string, string> p,
     int idProducto, string unidad, int idUdm,
     string descripcion, decimal cantidad)
        {
            return new ProductoAsignacion
            {
                IdProducto = idProducto,
                CodigoProducto = p.ContainsKey("productoId") ? p["productoId"] : "",
                Descripcion = descripcion,
                CantidadTotal = cantidad,
                Unidad = unidad,
                IdUdm = idUdm,
                PrecioUnitario = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0,
                CostoUnitario = p.ContainsKey("costoUnitario") ? decimal.Parse(p["costoUnitario"]) : 0,
                Descuento = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0,
                Comentario = p.ContainsKey("comentario") ? p["comentario"] : "",
                IdFilaOrigen = p.ContainsKey("id") ? p["id"] : Guid.NewGuid().ToString() // fallback de seguridad
            };
        }

        private TotalesGrupo CalcularTotalesGrupo(List<ProductoAsignacion> partidas)
        {
            decimal subtotal = partidas.Sum(p => p.CantidadTotal * p.PrecioUnitario);
            decimal descuento = partidas.Sum(p => p.CantidadTotal * p.PrecioUnitario * p.Descuento / 100m);
            decimal base_ = subtotal - descuento;
            decimal iva = base_ * 0.16m;

            return new TotalesGrupo
            {
                Subtotal = subtotal,
                Descuento = descuento,
                Iva = iva,
                Total = base_ + iva
            };
        }
    }
}