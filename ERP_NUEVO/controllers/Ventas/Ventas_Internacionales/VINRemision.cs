using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using System.Configuration;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Internacionales
{
    [Authorize]
    public class VINRemisionController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;

        public VINRemisionController(BOS_ERP.Services.EmailSender emailSenderService, BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
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
                }

                // 🔹 Validar fecha de pago solo si es crédito
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
                    IdArea = 17,
                    IdTpDoc = 55,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = fc["almacen"].ToString(),
                    Fch = DateTime.Now,
                    TpMov = "VINREM",
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
                    Incoterm = fc["incoterm"].ToString(),
                    FPago = Convert.ToInt32(fc["forma-pago"].ToString()),
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

                                string queryTarima = "SELECT ct.id_tarima, tp.cantidad FROM catalmacenes c " +
                                    "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                                    "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                                    "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                                    "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                                    "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                                    "INNER JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima " +
                                    "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id " +
                                    "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Temporal' AND tp.producto_id = @producto_id AND tp.cantidad > 0 ";

                                paramTarimas.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));

                                var tarimas = RunQuery(queryTarima, paramTarimas, false, conn, tx);

                                //if (tarimas.Count == 0)
                                //{
                                //    throw new Exception($"⚠️ No hay suficiente stock para el producto {codigo}.");
                                //}
                                decimal restante = cantidadSolicitada;

                                foreach (var t in tarimas)
                                {
                                    int tarimaId = Convert.ToInt32(t["id_tarima"]);
                                    decimal stockTarima = Convert.ToDecimal(t["cantidad"]);

                                    if (restante <= 0)
                                        break;

                                    decimal descontar = Math.Min(stockTarima, restante);

                                    restante -= descontar;

                                    var prodMovimiento = new Dictionary<string, object>
                                    {
                                        { "id_producto", idProducto },
                                        { "codigo", codigo },
                                        { "descripcion", descripcion },
                                        { "cantidad", descontar },
                                        { "unidad", idudm },
                                        { "tarima_id", tarimaId },
                                        { "tipo", "venta" },
                                        { "movimiento", "salida" }
                                    };

                                    movimientos.Add(prodMovimiento);

                                    RegistrarMovimiento(
                                        new List<Dictionary<string, object>> { prodMovimiento },
                                        userId,
                                        "venta",
                                        tarimaId,
                                        null,
                                        "salida",
                                        idEncabezado,
                                        null,
                                        conn,
                                        tx
                                    );
                                }

                                //if (restante > 0)
                                //{
                                //    throw new Exception($"⚠️ No hay suficiente stock para el producto {codigo}. Faltaron {restante} unidades.");
                                //}
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
                            inpuestosParameter.Add("impuesto_id", 1); // IVA
                            inpuestosParameter.Add("subtotal", 0);
                            inpuestosParameter.Add("importe", 0);
                            inpuestosParameter.Add("orden_apl", 1);
                            inpuestosParameter.Add("imp_variable", 0);
                            inpuestosParameter.Add("prov_nom", clienteNombre);
                            inpuestosParameter.Add("f_pago_id", fp);

                            RunQuery(inpuestos, inpuestosParameter, false, conn, tx);

                            //RegistrarCompra(Convert.ToInt32(folio["IdEncabezado"]), GetUserId(User.Identity.Name), conn, tx);

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
                RegistrarErrorParaTicket(ex, "VINRemision/?");
                if (idEncabezadoGenerado > 0)
                {
                    BorradoFacturasIncorrectas(idEncabezadoGenerado, "remision", "VS");
                }
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult ValidarFraccionesArancelarias(IFormCollection fc)
        {
            string productosJSON = fc["productosJSON"].ToString();

            if (string.IsNullOrWhiteSpace(productosJSON))
                return Json(new { success = false, message = "No se recibieron productos." });
            try
            {
                var productoIds = JsonConvert.DeserializeObject<List<string>>(productosJSON);

                if (productoIds == null || productoIds.Count == 0)
                    return Json(new { success = false, message = "Lista de productos vacía." });

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                var resultados = new List<object>();

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    foreach (var productoId in productoIds)
                    {
                        // ── 1. Fracción arancelaria ──────────────────────────────
                        var arParam = new Dictionary<string, object>
                {
                    { "cve_prod", productoId }
                };

                        string qFrac =
                            "SELECT fa.frac " +
                            "FROM frac_arancelarias fa " +
                            "INNER JOIN fracciones_arancelarias_sat fas " +
                            "       ON fas.fraccion_arancelaria = fa.frac " +
                            "WHERE fa.cve_prod = @cve_prod";

                        var frac = RunQuery(qFrac, arParam, false, conn);

                        bool tieneFraccion = frac.Count > 0
                            && !string.IsNullOrWhiteSpace(frac[0]["frac"]?.ToString());

                        // ── 2. Descripción del producto ──────────────────────────
                        var descParam = new Dictionary<string, object>
                {
                    { "cve_prod",    productoId },
                    { "empresa_id",  Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                        string qDesc =
                            "SELECT descr_prod " +
                            "FROM catproductos " +
                            "WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id";

                        var descResult = RunQuery(qDesc, descParam, false, conn);

                        string descripcion = descResult.Count > 0
                            ? descResult[0]["descr_prod"]?.ToString() ?? productoId
                            : productoId;

                        resultados.Add(new
                        {
                            productoId,
                            descripcion,
                            tieneFraccion,
                            fraccion = tieneFraccion ? frac[0]["frac"].ToString() : null
                        });
                    }
                }

                return Json(new { success = true, resultados });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VINRemision/ValidarFraccionesArancelarias");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ============================================================
        // Enviar alerta por correo cuando hay productos sin fracción arancelaria
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> EnviarAlertaFraccion()
        {
            try
            {
                string emailsString = Request.Form["emails"].ToString();
                string resultadosJson = Request.Form["resultadosJson"].ToString();
                string folio = Request.Form["folio"].ToString() ?? "Sin folio";
                string cliente = Request.Form["cliente"].ToString() ?? "Sin cliente";

                // ── Validaciones básicas ─────────────────────────────────────────────
                if (string.IsNullOrWhiteSpace(emailsString))
                    return Json(new { success = false, message = "Debe ingresar al menos un correo destinatario." });

                if (string.IsNullOrWhiteSpace(resultadosJson))
                    return Json(new { success = false, message = "No hay resultados de validación para enviar." });

                var resultados = JsonConvert.DeserializeObject<List<dynamic>>(resultadosJson);

                // Solo nos interesan los que NO tienen fracción arancelaria
                var sinFraccion = resultados?
                    .Where(r => !(bool)r.tieneFraccion)
                    .ToList();

                if (sinFraccion == null || sinFraccion.Count == 0)
                    return Json(new { success = false, message = "Todos los productos tienen fracción arancelaria registrada." });

                // ── Parsear y validar correos ────────────────────────────────────────
                var emails = emailsString
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct()
                    .ToList();

                if (!emails.Any())
                    return Json(new { success = false, message = "No se proporcionaron correos válidos." });

                var emailsInvalidos = emails.Where(e =>
                {
                    try { new System.Net.Mail.MailAddress(e); return false; }
                    catch { return true; }
                }).ToList();

                if (emailsInvalidos.Any())
                    return Json(new
                    {
                        success = false,
                        message = $"Correos con formato inválido: {string.Join(", ", emailsInvalidos)}"
                    });

                // ── Armar modelo para la vista ───────────────────────────────────────
                var emailData = new AlertaFraccionEmailModel
                {
                    Folio = folio,
                    Cliente = cliente,
                    UsuarioQueValido = User.Identity.Name ?? "Sistema",
                    FechaValidacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    ProductosSinFraccion = sinFraccion.Select(r => new ProductoFraccionInvalido
                    {
                        ProductoId = r.productoId?.ToString() ?? "-",
                        Descripcion = r.descripcion?.ToString() ?? "-"
                    }).ToList()
                };

                // ── Renderizar vista HTML ────────────────────────────────────────────
                string htmlBody = await emailSender.RenderViewToStringAsync(
                    "~/Views/Email/_AlertaFraccionEmail.cshtml",
                    emailData
                );

                // ── Enviar a cada destinatario ───────────────────────────────────────
                int enviosExitosos = 0;
                var errores = new List<string>();

                foreach (var email in emails)
                {
                    try
                    {
                        await correoHelper.EnviarCorreoNotificacionAsync(
                        email,
                        $"⚠ Alerta Fracción Arancelaria — Productos sin configuración | Folio {folio}",
                        htmlBody
                    );
                        enviosExitosos++;
                    }
                    catch (Exception exEmail)
                    {
                        errores.Add($"{email}: {exEmail.Message}");
                    }
                }

                // ── Respuesta según resultado ────────────────────────────────────────
                if (enviosExitosos == emails.Count)
                    return Json(new
                    {
                        success = true,
                        message = $"Alerta enviada correctamente a {enviosExitosos} destinatario(s)."
                    });

                if (enviosExitosos > 0)
                    return Json(new
                    {
                        success = true,
                        message = $"Alerta enviada a {enviosExitosos} de {emails.Count} destinatario(s).",
                        errores,
                        parcial = true
                    });

                return Json(new
                {
                    success = false,
                    message = "No se pudo enviar el correo a ningún destinatario.",
                    errores
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VINRemision/EnviarAlertaFraccion");
                return Json(new
                {
                    success = false,
                    message = "Error al enviar la alerta de fracciones: " + ex.Message
                });
            }
        }

    }
}