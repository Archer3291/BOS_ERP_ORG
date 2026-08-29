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


namespace BOS_ERP.Controllers.Ventas.Ventas_Sucursales
{
    [Authorize]
    public class VSRemisionController : Utilities
    {

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Creacion de remision")]
        public JsonResult Guardar(IFormCollection fc)
        {
            int idEncabezadoGenerado = 0;
            try
            {
                string tipo = fc["tipo"].ToString()?.ToLower() ?? ""; // contado, credito, anticipo
                var parameters = new Dictionary<string, object>();
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
                }

                // 🔹 Reglas de precio y crédito. La remisión no muestra precios, pero los
                // arrastra del pedido y es la que descarga almacén: se revalida aquí para que
                // un POST manipulado no pueda cambiarlos ni saltarse la línea de crédito.
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

                var credito = this.ValidarCreditoVenta(
                    fc["cliente"].ToString(),
                    empresaReglas,
                    CreditoVentasHelper.TotalDocumentoParaCredito(fc),
                    tipo,
                    int.TryParse(fc["documentid"].ToString(), out int docCredito) ? docCredito : 0,
                    null, null, fc["creditoToken"].ToString());

                if (!credito.Permitido)
                    return Json(new { success = false, message = credito.Mensaje });

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

                // 🔹 Crear encabezado
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 51,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    // La sucursal viaja en un select oculto: si el TomSelect todavía no se
                    // pobló, llegaba vacío y Convert.ToInt32("") reventaba. Manda la del
                    // usuario, que es la única con la que puede vender de todos modos.
                    Suc = int.TryParse(fc["sucursal"].ToString(), out int sucDoc) && sucDoc > 0
                            ? sucDoc
                            : Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = fc["almacen"].ToString(),
                    Fch = DateTime.Now,
                    TpMov = "VSREM",
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
                    // f_pago guarda el id de cat_f_pago, no la clave SAT. Antes se guardaba
                    // "03" como si fuera el id: al recargar el documento, el select de forma
                    // de pago del siguiente paso quedaba con la forma equivocada o vacío.
                    FPago = Convert.ToInt32(RunScalar(
                        "select id_f_pago from cat_f_pago where cve_sat = @cve_sat",
                        new Dictionary<string, object> { { "cve_sat", fc["forma-pago"].ToString() } })),
                    Mdp = fc["metodo-pago"].ToString(),
                    TipoPoceso = "remision_" + tipo,
                    CFDI = fc["uso-cfdi"].ToString(),
                    // La remisión ya no pregunta por el centro de costos (lo hace la factura,
                    // que es donde se genera la póliza). Sin este parseo tolerante,
                    // Convert.ToInt32("") reventaba con FormatException al guardar.
                    CentroCostos = int.TryParse(fc["CentroCostosId"].ToString(), out int ccRem) ? ccRem : 0,
                    Dto = Convert.ToDecimal(fc["descuento"].ToString()),
                    Sub = Convert.ToDecimal(fc["subtotal1"].ToString()),
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
                            // Importe = cantidad x precio menos descuento (antes iba contra
                            // costoUnitario, que el front no manda, y quedaba en 0). Sin esto
                            // el modal de facturación parcial calcula saldos sobre importes 0.
                            ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"], System.Globalization.CultureInfo.InvariantCulture) : 0)
                                      * (p.ContainsKey("precio") ? decimal.Parse(p["precio"], System.Globalization.CultureInfo.InvariantCulture) : 0)
                                      * (1 - (p.ContainsKey("descuento") ? decimal.Parse(p["descuento"], System.Globalization.CultureInfo.InvariantCulture) : 0) / 100m),
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
                                    idudm = Convert.ToInt32(RunScalar(queryUdm, paramUdm, false, conn, tx));
                                }
                                catch
                                {
                                    Console.WriteLine($"⚠️ Unidad no encontrada para clave: {unidadStr}. Se asignará 0.");
                                }

                                // 🔹 Buscar datos del producto
                                var paramProd = new Dictionary<string, object> { { "cve_prod", prodCve }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } };
                                var prodInfo = RunQuery(@"
                                    SELECT id_catproductos AS id_producto, cve_prod AS codigo, descr_prod AS descripcion
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

                                // 🔹 Buscar tarimas con stock disponible
                                var paramTarimas = new Dictionary<string, object> { { "producto_id", idProducto } };
                                var tarimas = RunQuery(@"
                                    SELECT tarima_id, cantidad
                                    FROM tarima_productos
                                    WHERE producto_id = @producto_id AND cantidad > 0
                                    ORDER BY tarima_id ASC", paramTarimas, false, conn, tx);

                                foreach (var t in tarimas)
                                {
                                    if (cantidadSolicitada <= 0)
                                        break;

                                    int tarimaId = Convert.ToInt32(t["tarima_id"]);
                                    decimal stockTarima = Convert.ToDecimal(t["cantidad"]);

                                    // 🔹 Construir producto para movimiento
                                    var prodMovimiento = new Dictionary<string, object>
                                    {
                                        { "id_producto", idProducto },
                                        { "codigo", codigo },
                                        { "descripcion", descripcion },
                                        { "cantidad", cantidadSolicitada },
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

                            //var polisa = GenerarDatosPoliza(Convert.ToInt32(folio["IdEncabezado"]), null, conn, tx);
                            //RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(folio["IdEncabezado"]), polisa, false, null, conn, tx);
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
                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString()
                };


                return Json(new { success = true, message = "Documento creado exitosamente.", folio_generado = folio["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VSRemision/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

    }
}