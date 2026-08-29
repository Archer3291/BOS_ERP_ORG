using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using Npgsql;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Nacionales
{
    [Authorize]
    public partial class VNPedidoController : Utilities
    {
        // Los usa el flujo de autorización de crédito (VNAutorizacionCredito.cs).
        private readonly BOS_ERP.Services.EmailSender emailSender;
        private readonly BOS_ERP.Helpers.CorreoHelper correoHelper;

        public VNPedidoController(
            BOS_ERP.Services.EmailSender emailSenderService,
            BOS_ERP.Helpers.CorreoHelper correoHelperService)
        {
            emailSender = emailSenderService;
            correoHelper = correoHelperService;
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Nacionales", Accion = "Creacion de pedido")]
        public JsonResult Guardar(IFormCollection fc)
            => GenerarPedidoDocumentos(fc, estatusDocumentos: 1, omitirValidacionCredito: false);

        /// <summary>
        /// Genera los documentos del pedido (VNPED / MODPED / TYBCOT según las partidas).
        ///
        /// El reparto entre Stock, Modula y Tubos depende de las existencias del momento, así
        /// que se calcula una sola vez: cuando el pedido excede el crédito se genera aquí mismo
        /// en <paramref name="estatusDocumentos"/> = 40 (pendiente de autorización) y aprobar
        /// solo lo activa. Reconstruirlo después daría un reparto distinto al que vio el vendedor.
        /// </summary>
        /// <param name="estatusDocumentos">1 = activo · 40 = pendiente de autorización.</param>
        /// <param name="omitirValidacionCredito">
        /// True al generar el pedido junto con la solicitud de autorización: ahí el exceso de
        /// crédito ya se conoce y es justo lo que se está mandando a autorizar.
        /// </param>
        private JsonResult GenerarPedidoDocumentos(
            IFormCollection fc, int estatusDocumentos, bool omitirValidacionCredito)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;

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

                // 🔹 Validar fecha de pago solo si es crédito / anticipo
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
                if (!omitirValidacionCredito)
                {
                    var credito = this.ValidarCreditoVenta(
                        fc["cliente"].ToString(),
                        Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        CreditoVentasHelper.TotalDocumentoParaCredito(fc),
                        tipo,
                        int.TryParse(fc["documentid"].ToString(), out int docCredito) ? docCredito : 0,
                        null, null, fc["creditoToken"].ToString());

                    if (!credito.Permitido)
                        return Json(new { success = false, message = credito.Mensaje });
                }

                int idEncabezadoPadre = GetUserId(User.Identity.Name);
                int usrId0 = 0;
                DateTime usrFch0 = DateTime.Now;

                // Un pedido puede generar hasta tres documentos + el registro de relación + el
                // cambio de estatus del padre. Antes cada paso corría en su propia conexión, así
                // que una falla a media generación dejaba documentos huérfanos y el padre sin
                // actualizar. Ahora todo vive en la misma transacción.
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                conn = new NpgsqlConnection(connStr);
                conn.Open();
                trx = conn.BeginTransaction();

                if (!string.IsNullOrEmpty(fc["documentid"].ToString()))
                {
                    var usrParameter = new Dictionary<string, object>();
                    string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.firma1, em.fch1, em.usr2, em.fch2, em.usr3, " +
                                      "       em.fch3, em.usr4, em.fch4, em.usr5, em.fch5, tipo_proceso, tipo_producto, usr_dep, en_presupuesto " +
                                      "FROM encabezadomov em " +
                                      "WHERE em.id_encabezado = @id";

                    usrParameter.Add("id", Convert.ToInt32(fc["documentid"].ToString()));

                    var result = RunQuery(usrquery, usrParameter, false, conn, trx);

                    if (result.Count > 0)
                    {
                        var usrId = result[0];
                        usrId0 = Convert.ToInt32(usrId["usr0"]);
                        usrFch0 = Convert.ToDateTime(usrId["fch0"]);
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
                    var clienteResult = RunQuery(clientequery, clienteParameter, false, conn, trx);
                    if (clienteResult.Count > 0)
                    {
                        var cliente = clienteResult[0];
                        idCliente = Convert.ToInt32(cliente["id_cliente"]);
                        clienteNombre = cliente["n_cli"].ToString();
                    }
                }

                decimal flete = string.IsNullOrWhiteSpace(fc["flete"].ToString()) ? 0 : Convert.ToDecimal(fc["flete"].ToString());

                // La clave SAT del formulario se traduce una sola vez al id del catálogo, que es
                // lo que guarda encabezadomov.f_pago y lo que buscan las consultas de carga.
                object idFPago = RunScalar(
                    "SELECT id_f_pago FROM cat_f_pago WHERE cve_sat = @cve_sat",
                    new Dictionary<string, object> { { "cve_sat", fc["forma-pago"].ToString() } },
                    false, conn, trx);

                if (idFPago == null || idFPago == DBNull.Value)
                    return Json(new { success = false, message = $"La forma de pago {fc["forma-pago"]} no existe en el catálogo." });

                int idFormaPago = Convert.ToInt32(idFPago);

                // 🔹 Datos de los documentos creados (para respuesta + tabla de relación)
                int? idEncNormal = null, idEncTubo = null, idEncModula = null;
                string folioNormal = null, folioTubo = null, folioModula = null;
                var documentosCreados = new List<object>();
                Dictionary<string, object> ultimoFolio = null;

                if (tipo == "anticipo")
                {
                    // 🔹 Caso ANTICIPO: no hay partidas que dividir, se conserva el comportamiento original
                    var encabezadoAnticipo = ConstruirEncabezadoPed(
                        fc, tipo, idEncabezadoPadre, usrId0, usrFch0, idCliente, fechaPago, flete, idFormaPago,
                        idArea: 12, idTpDoc: 42, tpMov: "VNPED",
                        imp: Convert.ToDecimal(fc["total"].ToString())
                    );

                    ultimoFolio = GenerarDocumentoConPartidas(encabezadoAnticipo, new List<PartidaDocumento>(), conn, trx);
                    idEncNormal = Convert.ToInt32(ultimoFolio["IdEncabezado"]);
                    folioNormal = ultimoFolio["folio_generado"].ToString();
                    documentosCreados.Add(new { id_encabezado = idEncNormal, folio = folioNormal, tipo = "VNPED" });
                }
                else
                {
                    // 🔹 Separar partidas: normales vs tubo vs modula.
                    // El ruteo ya no depende sólo de las banderas del catálogo: para los
                    // productos marcados prod_modula se consulta la existencia real y Modula
                    // tiene prioridad. Si Modula no alcanza a cubrir la cantidad pedida, la
                    // partida se parte en dos (lo que hay en Modula + el resto en Stock).
                    var partidasNormales = new List<PartidaDocumento>();
                    var partidasTubo = new List<PartidaDocumento>();
                    var partidasModula = new List<PartidaDocumento>();

                    // Un mismo producto puede venir en varias partidas; hay que ir descontando
                    // lo que ya se comprometió en Modula para no asignarlo dos veces.
                    var modulaDisponiblePorProducto = new Dictionary<int, decimal>();

                    foreach (var p in productos)
                    {
                        var parametersP = new Dictionary<string, object>();
                        string queryId = $@"
                            SELECT cp.id_catproductos, cp.es_tubo, cp.prod_modula,
                                   {SqlExistencia("cp.id_catproductos", "Stock")}  AS existencia_stock,
                                   {SqlExistencia("cp.id_catproductos", "Modula")} AS existencia_modula
                            FROM catproductos cp
                            WHERE cp.cve_prod = @cve_prod AND cp.empresa_id = @empresa_id";
                        parametersP.Add("cve_prod", p.ContainsKey("productoId") ? p["productoId"] : "");
                        parametersP.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        parametersP.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));

                        var productoInfo = RunQuery(queryId, parametersP, false, conn, trx);

                        int productId = 0;
                        bool esTubo = false;
                        bool esModula = false;
                        decimal existenciaStock = 0;
                        decimal existenciaModula = 0;

                        if (productoInfo.Count > 0)
                        {
                            productId = Convert.ToInt32(productoInfo[0]["id_catproductos"]);
                            esTubo = productoInfo[0]["es_tubo"] != null && Convert.ToBoolean(productoInfo[0]["es_tubo"]);
                            esModula = productoInfo[0]["prod_modula"] != null && Convert.ToBoolean(productoInfo[0]["prod_modula"]);
                            existenciaStock = Convert.ToDecimal(productoInfo[0]["existencia_stock"]);
                            existenciaModula = Convert.ToDecimal(productoInfo[0]["existencia_modula"]);
                        }

                        decimal cantidad = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0;
                        decimal precio = p.ContainsKey("precio") ? decimal.Parse(p["precio"]) : 0;
                        decimal descuentoPct = p.ContainsKey("descuento") ? decimal.Parse(p["descuento"]) : 0;

                        string cveProd = p.ContainsKey("productoId") ? p["productoId"] : "";
                        string descrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "";
                        string unidad = p.ContainsKey("unidad") ? p["unidad"].ToString() : "PZA";
                        string comentario = p.ContainsKey("comentario") ? p["comentario"] : "";

                        PartidaDocumento NuevaPartida(decimal cant) => new PartidaDocumento
                        {
                            CveProd = cveProd,
                            DescrProd = descrProd,
                            CantUd = cant,
                            PvProd = precio,
                            Dto1 = descuentoPct,
                            ImpPart = cant * precio * (1 - descuentoPct / 100),
                            Ud = unidad,
                            IdProducto = productId,
                            TpDocAnt = comentario
                        };

                        if (esTubo)
                        {
                            // Los tubos van completos al documento de tubos: su disponibilidad
                            // se resuelve por corte, no por existencia en tarima.
                            partidasTubo.Add(NuevaPartida(cantidad));
                            continue;
                        }

                        if (!esModula)
                        {
                            partidasNormales.Add(NuevaPartida(cantidad));
                            continue;
                        }

                        if (!modulaDisponiblePorProducto.TryGetValue(productId, out decimal dispModula))
                            dispModula = existenciaModula;

                        decimal desdeModula = Math.Min(cantidad, Math.Max(0, dispModula));

                        if (desdeModula > 0)
                        {
                            partidasModula.Add(NuevaPartida(desdeModula));
                            modulaDisponiblePorProducto[productId] = dispModula - desdeModula;

                            decimal resto = cantidad - desdeModula;
                            if (resto > 0)
                                partidasNormales.Add(NuevaPartida(resto));
                        }
                        else if (existenciaStock > 0)
                        {
                            // Modula no tiene nada que aportar pero Stock sí: se surte de Stock
                            // en lugar de generar un MODPED imposible de surtir.
                            partidasNormales.Add(NuevaPartida(cantidad));
                        }
                        else
                        {
                            // Sin existencia en ningún almacén: se rutea por la bandera del
                            // catálogo, como antes.
                            modulaDisponiblePorProducto[productId] = 0;
                            partidasModula.Add(NuevaPartida(cantidad));
                        }
                    }

                    // El flete se cobra una sola vez: se aplica al primer documento generado
                    // (normal → modula → tubo). Antes se sumaba íntegro a los tres, así que un
                    // pedido dividido cobraba el flete tantas veces como documentos generara.
                    decimal fletePorAplicar = flete;

                    // 🔹 Documento normal (VNPED)
                    if (partidasNormales.Count > 0)
                    {
                        decimal fleteN = fletePorAplicar;
                        fletePorAplicar = 0;

                        decimal totalN = CalcularTotalDesdeParametros(partidasNormales, fleteN);
                        decimal dtoN = CalcularDescuentoDesdeParametros(partidasNormales);

                        var encabezadoNormal = ConstruirEncabezadoPed(
                            fc, tipo, idEncabezadoPadre, usrId0, usrFch0, idCliente, fechaPago, fleteN, idFormaPago,
                            idArea: 12, idTpDoc: 42, tpMov: "VNPED",
                            imp: totalN, dto: dtoN, estatus: estatusDocumentos
                        );

                        ultimoFolio = GenerarDocumentoConPartidas(encabezadoNormal, partidasNormales, conn, trx);
                        idEncNormal = Convert.ToInt32(ultimoFolio["IdEncabezado"]);
                        folioNormal = ultimoFolio["folio_generado"].ToString();
                        documentosCreados.Add(new { id_encabezado = idEncNormal, folio = folioNormal, tipo = "VNPED" });
                    }

                    // 🔹 Documento modula (MODPED)
                    if (partidasModula.Count > 0)
                    {
                        decimal fleteM = fletePorAplicar;
                        fletePorAplicar = 0;

                        decimal totalM = CalcularTotalDesdeParametros(partidasModula, fleteM);
                        decimal dtoM = CalcularDescuentoDesdeParametros(partidasModula);

                        var encabezadoModula = ConstruirEncabezadoPed(
                            fc, tipo, idEncabezadoPadre, usrId0, usrFch0, idCliente, fechaPago, fleteM, idFormaPago,
                            idArea: 6, idTpDoc: 89, tpMov: "MODPED",
                            imp: totalM, dto: dtoM, estatus: estatusDocumentos
                        );

                        ultimoFolio = GenerarDocumentoConPartidas(encabezadoModula, partidasModula, conn, trx);
                        idEncModula = Convert.ToInt32(ultimoFolio["IdEncabezado"]);
                        folioModula = ultimoFolio["folio_generado"].ToString();
                        documentosCreados.Add(new { id_encabezado = idEncModula, folio = folioModula, tipo = "MODPED" });
                    }

                    // 🔹 Documento de tubos (TYBCOT)
                    if (partidasTubo.Count > 0)
                    {
                        decimal fleteT = fletePorAplicar;
                        fletePorAplicar = 0;

                        decimal totalT = CalcularTotalDesdeParametros(partidasTubo, fleteT);
                        decimal dtoT = CalcularDescuentoDesdeParametros(partidasTubo);

                        var encabezadoTubo = ConstruirEncabezadoPed(
                            fc, tipo, idEncabezadoPadre, usrId0, usrFch0, idCliente, fechaPago, fleteT, idFormaPago,
                            idArea: 26, idTpDoc: 87, tpMov: "TYBCOT",
                            imp: totalT, dto: dtoT, estatus: estatusDocumentos
                        );

                        ultimoFolio = GenerarDocumentoConPartidas(encabezadoTubo, partidasTubo, conn, trx);
                        idEncTubo = Convert.ToInt32(ultimoFolio["IdEncabezado"]);
                        folioTubo = ultimoFolio["folio_generado"].ToString();
                        documentosCreados.Add(new { id_encabezado = idEncTubo, folio = folioTubo, tipo = "TYBCOT" });
                    }

                    if (documentosCreados.Count == 0)
                        return Json(new { success = false, message = "No se generó ningún documento (sin partidas)." });

                    // 🔹 Registrar la relación entre los documentos generados (solo aplica a contado/credito, no anticipo)
                    InsertarDocumentosRelacionados(idEncabezadoPadre, idEncNormal, idEncTubo, idEncModula, folioNormal, folioTubo, folioModula, conn, trx);
                }

                // 🔹 Actualizar estatus del documento padre. Con documentos pendientes de
                //    autorización la cotización no se cierra: si el gerente rechaza, debe
                //    seguir disponible para rehacer el pedido.
                if (estatusDocumentos == 1)
                {
                    string query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                    parameters.Add("id", idEncabezadoPadre);
                    RunQuery(query, parameters, false, conn, trx);
                }

                trx.Commit();

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(ultimoFolio["IdEncabezado"]),
                    Folio = string.Join(", ", documentosCreados.Select(d => ((dynamic)d).folio))
                };

                return Json(new
                {
                    success = true,
                    message = "Documento(s) creado(s) exitosamente.",
                    folio_generado = string.Join(", ", documentosCreados.Select(d => ((dynamic)d).folio)),
                    documentos = documentosCreados,
                    // Para ligar la solicitud de autorización a los documentos recién creados.
                    id_encabezado_padre = idEncabezadoPadre,
                    id_encabezado_normal = idEncNormal,
                    id_encabezado_modula = idEncModula,
                    id_encabezado_tubo = idEncTubo
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VNPedido/?");
                try { trx?.Rollback(); } catch { /* ya se cerró o no aplica */ }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }

        // 🔹 Recalcula subtotal1, descuento, IVA y total de un grupo de partidas + flete completo
        private decimal CalcularTotalDesdeParametros(List<PartidaDocumento> partidas, decimal flete)
        {
            decimal subtotal1 = partidas.Sum(x => (x.CantUd ?? 0m) * (x.PvProd ?? 0m));
            decimal descuento = CalcularDescuentoDesdeParametros(partidas);

            decimal subtotal2 = subtotal1 - descuento + flete;
            decimal iva = subtotal2 * 0.16m;
            decimal total = subtotal2 + iva;

            return total;
        }

        // 🔹 Descuento total en pesos de un grupo de partidas (para encabezadomov.dto)
        private decimal CalcularDescuentoDesdeParametros(List<PartidaDocumento> partidas)
        {
            return partidas.Sum(x =>
                (x.CantUd ?? 0m) *
                (x.PvProd ?? 0m) *
                ((x.Dto1 ?? 0m) / 100m));
        }

        // 🔹 Arma el encabezado común, variando solo IdArea/IdTpDoc/TpMov/Imp según el documento
        private DocumentoEncabezado ConstruirEncabezadoPed(
            IFormCollection fc, string tipo, int idEncabezadoPadre, int usrId0, DateTime usrFch0,
            int idCliente, DateTime? fechaPago, decimal flete, int idFormaPago,
            int idArea, int idTpDoc, string tpMov, decimal imp, decimal dto = 0,
            int estatus = 1)
        {
            return new DocumentoEncabezado
            {
                EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                IdArea = idArea,
                IdTpDoc = idTpDoc,
                UsrDep = GetAreaName(User.Identity.Name),
                Anio = DateTime.Now.Year,
                Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                Alm = fc["almacen"].ToString(),
                Fch = DateTime.Now,
                TpMov = tpMov,
                ComentAut = fc["comentarios"].ToString(),
                UsrDoc = User.Identity.Name,
                FchCap = DateTime.Now,
                Usr0 = usrId0,
                Fch0 = usrFch0,
                Usr1 = GetUserId(User.Identity.Name),
                Fch1 = DateTime.Now,
                Imp = imp,
                Dto = dto,
                CliProv = fc["cliente"].ToString(),
                Ccy = fc["moneda"].ToString(),
                Estatus = estatus,
                Ref = idCliente,
                Flete = flete,
                VdrCpr = fc["vendedor"].ToString(),
                Coment1 = fc["concepto"].ToString(),
                EncabezadoPadre = idEncabezadoPadre,
                PlDias = string.IsNullOrWhiteSpace(fc["plazo"].ToString()) ? 0 : Convert.ToInt32(fc["plazo"].ToString()),
                FchPgEntrega = fechaPago ?? DateTime.Now,
                Par = Convert.ToDecimal(fc["paridad"].ToString()),
                // encabezadomov.f_pago guarda el id_f_pago del catálogo, NO la clave SAT que
                // manda el formulario. Guardar la clave tal cual solo "funcionaba" con
                // 01-Efectivo, donde id_f_pago y cve_sat coinciden por casualidad: con 99-Por
                // definir (la que se usa en las ventas a crédito) el LEFT JOIN de las consultas
                // de carga no encontraba fila y la remisión y la factura llegaban sin forma de
                // pago. El resto de los controladores de venta ya resolvían el id así.
                FPago = idFormaPago,
                Mdp = fc["metodo-pago"].ToString(),
                TipoPoceso = "pedido_" + tipo,
                CFDI = fc["uso-cfdi"].ToString(),
                NatDocPadreChar = fc["ordenCompra"].ToString()
            };
        }

        // 🔹 Inserta el registro de relación entre los documentos generados (normal, tubo, modula)
        private void InsertarDocumentosRelacionados(
            int idEncabezadoPadre, int? idEncNormal, int? idEncTubo, int? idEncModula,
            string folioNormal, string folioTubo, string folioModula,
            NpgsqlConnection conn, NpgsqlTransaction trx)
        {
            string tipoRelacion = CalcularTipoRelacion(idEncNormal, idEncTubo, idEncModula);

            var parametersRel = new Dictionary<string, object>();
            string insertQuery = @"
        INSERT INTO documentos_relacionados
            (id_encabezado_padre, id_encabezado_normal, id_encabezado_tubo, id_encabezado_modula,
             folio_normal, folio_tubo, folio_modula, tipo_relacion, empresa_id, usr_creacion, fecha_creacion)
        VALUES
            (@id_encabezado_padre, @id_encabezado_normal, @id_encabezado_tubo, @id_encabezado_modula,
             @folio_normal, @folio_tubo, @folio_modula, @tipo_relacion, @empresa_id, @usr_creacion, now())";

            parametersRel.Add("tipo_relacion", tipoRelacion);
            parametersRel.Add("id_encabezado_padre", idEncabezadoPadre);
            parametersRel.Add("id_encabezado_normal", (object)idEncNormal ?? DBNull.Value);
            parametersRel.Add("id_encabezado_tubo", (object)idEncTubo ?? DBNull.Value);
            parametersRel.Add("id_encabezado_modula", (object)idEncModula ?? DBNull.Value);
            parametersRel.Add("folio_normal", (object)folioNormal ?? DBNull.Value);
            parametersRel.Add("folio_tubo", (object)folioTubo ?? DBNull.Value);
            parametersRel.Add("folio_modula", (object)folioModula ?? DBNull.Value);
            parametersRel.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parametersRel.Add("usr_creacion", GetUserId(User.Identity.Name));

            RunQuery(insertQuery, parametersRel, false, conn, trx);
        }

        private string CalcularTipoRelacion(int? idEncNormal, int? idEncTubo, int? idEncModula)
        {
            var partes = new List<string>();
            if (idEncNormal.HasValue) partes.Add("normal");
            if (idEncTubo.HasValue) partes.Add("tubo");
            if (idEncModula.HasValue) partes.Add("modula");
            return "pedido_split_" + string.Join("_", partes);
        }
    }
}