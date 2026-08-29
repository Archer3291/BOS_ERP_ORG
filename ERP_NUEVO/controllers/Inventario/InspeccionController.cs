using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using BOS_ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using System.Configuration;

namespace BOS_ERP.Controllers
{
    public partial class InventarioController : Utilities
    {
        private readonly EmailSender _emailSender;
        private readonly CorreoHelper _correoHelper;

        public InventarioController(EmailSender emailSenderService, CorreoHelper correoHelperService)
        {
            _emailSender = emailSenderService;
            _correoHelper = correoHelperService;
        }

        public JsonResult GetInventarioDisponible(IFormCollection fc)
        {
            // Separar los códigos por coma si vienen varios
            string idInput = fc["id"].ToString() ?? "";
            var codigos = idInput.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            parameters.Add("id", codigos); // Npgsql detecta array automáticamente
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = @"
                SELECT cp.cve_prod AS codigo, cp.descr_prod AS descripcion, cp.udm AS unidadproducto, ct.codigo AS tarima, cn.ulocation, tp.cantidad, 
                    cun.descripcion AS unidadnombre, ct.id_tarima, ca.descripcion almacen, csu.descripcion sucursal, em.nombre empresa
                FROM cattarimas ct
                INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                INNER JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id
                INNER JOIN empresas em ON em.empresaid = csu.empresa_id
                LEFT JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima
                LEFT JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id
                LEFT JOIN catunidades cun ON cun.id_udm = tp.unidad
                WHERE ca.tipo = 'Stock' AND csu.id_sucursal = @sucursal
                    --AND (cp.cve_prod = ANY(@id) OR cp.cve_prod IS NULL)
                ORDER BY ca.descripcion ASC;";

            return Json(new { data = RunQuery(query, parameters) });
        }

        [AuditAction(Modulo = "Inventario", Accion = "Proceso de inspeccion, se genera documento de recpcion a stock o se van a cuarentena")]
        public async Task<JsonResult> RecibirMaterialInspeccion(IFormCollection fc)
        {
            try
            {
                var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                var discrepancias = products.Where(p => p.ContainsKey("status") && !Convert.ToBoolean(p["status"])).ToList();
                var aceptados = products.Where(p => p.ContainsKey("status") && Convert.ToBoolean(p["status"])).ToList();
                var docsGenerados = new List<string>();
                string listaHtml = "";

                bool hayDiscrepancia = false;
                int documentoDisc = 0;

                if (discrepancias.Count > 0)
                {
                    hayDiscrepancia = true;
                    var parameters = new Dictionary<string, object>();
                    string query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Cuarentena'";
                    parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                    int cuarentena = Convert.ToInt32(RunScalar(query, parameters));

                    if (cuarentena == 0 || cuarentena == null)
                    {
                        return Json(new { icon = "error", title = "Ocurrio un error con la sucursla", html = "La sucursal a la que perteneces no cuenta con almacen de cuarentena, favor de contactar a soporte" });
                    }

                    query = "SELECT fch1, fch2, fch3, fch4, fch5, fch6, firma1, firma6, " +
                        "   usr1, usr2, usr3, usr4, usr5, usr6 " +
                        "FROM encabezadomov " +
                        "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    decimal? precioTotal = discrepancias.Sum(p => GetDecimal(p["cantidad"], 0) * GetDecimal(p["idp"], 0));

                    string motivo = "ingreso_cuerantena";
                    var encabezado = new DocumentoEncabezado
                    {
                        EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                        IdArea = 5,
                        IdTpDoc = 58,
                        Anio = DateTime.Now.Year,
                        Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                        Fch = DateTime.Now,
                        TpMov = "INGCUA",
                        UsrDoc = User.Identity.Name,
                        FchCap = DateTime.Now,
                        Usr0 = GetUserId(User.Identity.Name),
                        Fch0 = DateTime.Now,
                        Usr1 = Convert.ToInt32(usr["usr1"]),
                        Fch1 = (DateTime)usr["fch1"],
                        Firma1 = usr["firma1"].ToString(),
                        Usr2 = Convert.ToInt32(usr["usr2"]),
                        Fch2 = (DateTime)usr["fch2"],
                        Usr3 = Convert.ToInt32(usr["usr3"]),
                        Fch3 = (DateTime)usr["fch3"],
                        Usr4 = Convert.ToInt32(usr["usr4"]),
                        Fch4 = usr["fch4"] == DBNull.Value ? null : (DateTime?)usr["fch4"],
                        Usr5 = Convert.ToInt32(usr["usr5"]),
                        Fch5 = (DateTime)usr["fch5"],
                        Usr6 = Convert.ToInt32(usr["usr6"]),
                        Fch6 = (DateTime)usr["fch6"],
                        Firma6 = usr["firma6"].ToString(),
                        CliProv = discrepancias[0]["proveedor_nombre"].ToString(),
                        Ref = GetInt(discrepancias[0]["proveedor"]),
                        EncabezadoPadre = Convert.ToInt32(discrepancias[0]["encabezado"]),
                        Estatus = 19,
                        UsrDep = GetAreaName(User.Identity.Name),
                        TipoPoceso = "ingreso_cuerantena",
                        Coment1 = fc["comentario"].ToString(),
                        Imp = precioTotal,
                        CentroCostos = 5
                    };

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();

                    foreach (var disc in discrepancias)
                    {
                        if (Convert.ToInt32(disc["cantidad"]) == 0)
                        {
                            continue;
                        }

                        motivo = string.IsNullOrEmpty(disc["motivo"]?.ToString()) ? null : disc["motivo"].ToString();
                        decimal? cantUd = GetDecimal(disc["cantidad"], 0);
                        decimal? pvProd = GetDecimal(disc["idp"], 0);

                        partidas.Add(new PartidaDocumento
                        {
                            NroPart = nro++,
                            CveProd = GetString(disc["codigo"]),
                            DescrProd = GetString(disc["descripcion"]),
                            CantUd = cantUd,
                            CveVdrCpr = GetString(disc["proveedor_nombre"]),
                            Ref = GetInt(disc["proveedor"]),
                            Ud = disc["cve_unidad"].ToString(),
                            FolDocAnt = motivo,
                            PvProd = pvProd,
                            ImpPart = cantUd * pvProd,
                            FPagoId = GetInt(disc["fp"]),
                            Dto1 = GetDecimal(disc["dto"], 0),
                        });
                    }

                    //var documento = GenerarDocumentoConPartidas(encabezado, partidas);
                    //docsGenerados.Add(documento["folio_generado"].ToString());
                    //documentoDisc = Convert.ToInt32(documento["IdEncabezado"]);
                    //RegistrarMovimiento(discrepancias, GetUserId(User.Identity.Name), "Ingreso", null, cuarentena, motivo, Convert.ToInt32(documento["IdEncabezado"]));
                    //List<PolizaData> poliza = GenerarDatosPoliza(documentoDisc);
                    //RegistrarPolizas(GetUserId(User.Identity.Name), documentoDisc, poliza);

                    var documento = new Dictionary<string, object>();

                    var utils = new Utilities(true);
                    string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                    using (var conn = new NpgsqlConnection(connStr))
                    {
                        conn.Open();

                        using (var tx = conn.BeginTransaction())
                        {
                            try
                            {
                                documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                                docsGenerados.Add(documento["folio_generado"].ToString());
                                documentoDisc = Convert.ToInt32(documento["IdEncabezado"]);

                                RegistrarMovimiento(discrepancias, GetUserId(User.Identity.Name), "Ingreso", null, cuarentena, motivo, documentoDisc, null, conn, tx);

                                List<PolizaData> poliza = GenerarDatosPoliza(documentoDisc, null, null, conn, tx);
                                RegistrarPolizas(GetUserId(User.Identity.Name), documentoDisc, poliza, false, null, conn, tx);

                                tx.Commit();
                            }
                            catch
                            {
                                tx.Rollback();
                                throw;
                            }
                        }
                    }

                    parameters = new Dictionary<string, object>();
                    query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, u.email AS emailUsuario, " +
                    "   u.nombreusuario AS aliasUsuario, u2.usuarioid AS managerId, u2.nombre || ' ' || u2.apellido AS nombreManager, " +
                    "   u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = (SELECT areaid FROM areas a WHERE a.nombre = 'Compras Nacionales') AND u2.sucursal_id = u.sucursal_id " +
                    "WHERE u.nombreusuario = @userName";

                    parameters.Add("userName", User.Identity.Name);
                    var userResult = RunQuery(query, parameters)[0];

                    query = "SELECT folio " +
                        "FROM encabezadomov em WHERE em.id_encabezado = (SELECT encabezados_padre FROM encabezadomov WHERE id_encabezado = @encabezado)";
                    parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    var folioOC = RunScalar(query, parameters);

                    var urlBase = $"{Request.Scheme}://{Request.Host}";
                    string path = "/Operaciones/PolizasDiscrepancia";
                    string urlCotizacion = $"{urlBase}{path}?q={folioOC.ToString()}";

                    var emailData = new EmailSenderModel
                    {
                        Folio = documento["folio_generado"].ToString(),
                        Date = DateTime.Now,
                        SenderName = userResult["nombreusuario"].ToString(),
                        SenderEmail = userResult["emailusuario"].ToString(),
                        Description = fc["comentario"].ToString(),
                        RecipientName = userResult["nombremanager"].ToString(),
                        RecipientEmail = userResult["emailmanager"].ToString(),
                        URL = urlCotizacion
                    };

                    string htmlBody = await _emailSender.RenderViewToStringAsync("~/Views/Email/_SendCreateRequisitionNotification.cshtml", emailData);
                    await _correoHelper.EnviarCorreoNotificacionAsync(emailData.RecipientEmail, "Nueva Requisicion Creada", htmlBody);

                    _ = SendNotificationInterno(userResult["aliasmanager"].ToString(), userResult["emailmanager"].ToString(), new
                    {
                        icon = "info",
                        title = "Poliza con discrepancia.",
                        message = $"El usuario {userResult["nombreusuario"]} ha detectado una discrepancia con los productos de la orden de compra {folioOC} y es necesaria su revicion.",
                        buttons = new[]
                   {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                        timer = 0,
                        folio = documento["folio_generado"],
                    });

                    listaHtml = "Folios generados <br><ul style='text-align:left'>";
                    foreach (var folio in docsGenerados)
                    {
                        // El formato es INV-RINVD-2025-0000001
                        var partes = folio.Split('-');
                        var tipo = partes.Length > 1 ? partes[1] : "";

                        if (tipo == "RINVP")
                            listaHtml += $"<li><b>Discrepancia:</b> {folio}</li>";
                        else if (tipo == "RINV")
                            listaHtml += $"<li><b>Recepción stock:</b> {folio}</li>";
                        else if (tipo == "OC")
                            listaHtml += $"<li><b>Variacion de la orden de compra con partidas pendientes:</b> {folio}</li>";
                        else if (tipo == "GTO")
                            listaHtml += $"<li><b>Variacion de la solicitud de gasto con partidas pendientes:</b> {folio}</li>";
                        else
                            listaHtml += $"<li>{folio}</li>";
                    }
                    listaHtml += "</ul>";
                }

                if (aceptados.Count > 0)
                {
                    var parameters = new Dictionary<string, object>();
                    string query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Recepcion'";
                    parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                    int resepcion = Convert.ToInt32(RunScalar(query, parameters));

                    if (resepcion == 0 || resepcion == null)
                    {
                        return Json(new { icon = "error", title = "Ocurrio un error con la sucursla", html = "La sucursal a la que perteneces no cuenta con almacen de recepcion, favor de contactar a soporte" });
                    }

                    var sinStock = new List<Dictionary<string, object>>();

                    foreach (var prod in aceptados)
                    {
                        int productoId = Convert.ToInt32(prod["id_producto"]);
                        decimal cantidadSolicitada = Convert.ToDecimal(prod["cantidad"]);

                        // Traemos el stock real disponible en cuarentena
                        query = @"
                            SELECT COALESCE(SUM(tp.cantidad), 0) 
                            FROM tarima_productos tp
                            INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                            INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                            INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                            INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                            INNER JOIN catalmacenes c ON c.id_almacen = cr.almacen_id
                            INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id
                            WHERE cs.id_sucursal = @sucursal
                            AND c.tipo = 'Recepcion'
                            AND tp.producto_id = @productoId";

                        parameters = new Dictionary<string, object>
                        {
                            { "sucursal", HttpContext.Session.GetInt32("Sucursal") },
                            { "productoId", productoId }
                        };

                        decimal stockDisponible = Convert.ToDecimal(RunScalar(query, parameters));

                        if (stockDisponible < cantidadSolicitada)
                        {
                            // Añadimos la info del producto y stock disponible
                            prod.Add("stockDisponible", stockDisponible);
                            sinStock.Add(prod);
                        }
                    }

                    if (sinStock.Any())
                    {
                        return Json(new
                        {
                            icon = "error",
                            title = "Algunos productos no tienen stock suficiente",
                            productos = sinStock
                        });
                    }

                    query = "SELECT fch0, fch1, fch2, fch3, fch4, fch5, fch6, firma0, " +
                        "   firma0, firma1, firma2, firma3, firma4, firma5, firma6, " +
                        "   usr0, usr1, usr2, usr3, usr4, usr5, usr6, nat, imp " +
                        "FROM encabezadomov " +
                        "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", GetInt(fc["id_encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    decimal? precioTotal = aceptados.Sum(p => GetDecimal(p["cantidad"], 0) * GetDecimal(p["idp"], 0));

                    string motivo = "ingreso_cuerantena";
                    var encabezado = new DocumentoEncabezado();
                    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                    encabezado.IdArea = 5;
                    encabezado.IdTpDoc = 57;
                    encabezado.Anio = DateTime.Now.Year;
                    encabezado.Suc = GetInt(HttpContext.Session.GetInt32("Sucursal"));
                    encabezado.Fch = DateTime.Now;
                    encabezado.TpMov = "INGINV";
                    encabezado.UsrDoc = User.Identity.Name;
                    encabezado.FchCap = DateTime.Now;
                    encabezado.Usr0 = GetInt(usr["usr0"]);
                    encabezado.Fch0 = GetDate(usr["fch0"]);
                    encabezado.Firma0 = GetString(usr["firma0"]);
                    encabezado.Usr1 = GetInt(usr["usr1"]);
                    encabezado.Fch1 = GetDate(usr["fch1"]);
                    encabezado.Firma1 = GetString(usr["firma1"]);
                    encabezado.Usr2 = GetInt(usr["usr2"]);
                    encabezado.Fch2 = GetDate(usr["fch2"]);
                    encabezado.Firma2 = GetString(usr["firma2"]);
                    encabezado.Usr3 = GetInt(usr["usr3"]);
                    encabezado.Fch3 = GetDate(usr["fch3"]);
                    encabezado.Firma3 = GetString(usr["firma3"]);
                    encabezado.Usr4 = GetInt(usr["usr4"]);
                    encabezado.Fch4 = GetDate(usr["fch4"]);
                    encabezado.Firma4 = GetString(usr["firma4"]);
                    encabezado.Usr5 = GetInt(usr["usr5"]);
                    encabezado.Fch5 = GetDate(usr["fch5"]);
                    encabezado.Firma5 = GetString(usr["firma5"]);
                    encabezado.Usr6 = GetUserId(User.Identity.Name);
                    encabezado.Fch6 = DateTime.Now;
                    encabezado.CliProv = aceptados[0]["proveedor_nombre"].ToString();
                    encabezado.Ref = GetInt(aceptados[0]["proveedor"]);
                    encabezado.EncabezadoPadre = GetInt(aceptados[0]["encabezado"]);
                    encabezado.Estatus = 11;
                    encabezado.UsrDep = GetAreaName(User.Identity.Name);
                    encabezado.TipoPoceso = "ingreso_almacen";
                    encabezado.Coment1 = fc["comentario"].ToString();
                    encabezado.Imp = precioTotal;

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();

                    foreach (var disc in aceptados)
                    {
                        if (GetInt(disc["cantidad"]) == 0)
                        {
                            continue;
                        }

                        motivo = string.IsNullOrEmpty(disc["motivo"]?.ToString()) ? null : disc["motivo"].ToString();
                        decimal? cantUd = GetDecimal(disc["cantidad"], 0);
                        decimal? pvProd = GetDecimal(disc["idp"], 0);

                        partidas.Add(new PartidaDocumento
                        {
                            NroPart = nro++,
                            CveProd = GetString(disc["codigo"]),
                            DescrProd = GetString(disc["descripcion"]),
                            CantUd = cantUd,
                            CveVdrCpr = GetString(disc["proveedor_nombre"]),
                            Ref = GetInt(disc["proveedor"]),
                            Ud = disc["cve_unidad"].ToString(),
                            FolDocAnt = motivo,
                            PvProd = pvProd,
                            ImpPart = cantUd * pvProd,
                            FPagoId = GetInt(disc["fp"]),
                            Dto1 = GetDecimal(disc["dto"], 0),
                        });
                    }

                    //var documento = GenerarDocumentoConPartidas(encabezado, partidas);
                    //docsGenerados.Add(documento["folio_generado"].ToString());
                    //var docAceptado = Convert.ToInt32(documento["IdEncabezado"]);
                    //List<PolizaData> poliza = GenerarDatosPoliza(docAceptado);
                    //RegistrarPolizas(GetUserId(User.Identity.Name), docAceptado, poliza);

                    //var productosPorDestino = aceptados.Where(p => p.ContainsKey("destino")).GroupBy(p => Convert.ToInt32(p["destino"])).ToList();

                    //string ubicaciones = "<ul>";
                    //foreach (var grupo in productosPorDestino)
                    //{
                    //    parameters = new Dictionary<string, object>();
                    //    int destino = grupo.Key;
                    //    var listaProductos = grupo.ToList();

                    //    RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name), "Movimiento", resepcion, destino, "traslado", docAceptado);

                    //    query = "SELECT cn.ulocation " +
                    //        "FROM cattarimas ct " +
                    //        "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                    //        "WHERE ct.id_tarima = @destino";
                    //    parameters.Add("destino", destino);
                    //    var des = RunScalar(query, parameters);
                    //    ubicaciones += $"<li> {des.ToString()} </li>";
                    //}

                    //parameters = new Dictionary<string, object>();
                    //query = "UPDATE encabezadomov SET estatus_id = 20 WHERE id_encabezado = @encabezado";
                    //parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                    //RunUpdate(query, parameters);

                    var documento = new Dictionary<string, object>();
                    string ubicaciones = "<ul>";

                    var utils = new Utilities(true);
                    string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                    using (var conn = new NpgsqlConnection(connStr))
                    {
                        conn.Open();

                        using (var tx = conn.BeginTransaction())
                        {
                            try
                            {
                                documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                                docsGenerados.Add(documento["folio_generado"].ToString());
                                int docAceptado = Convert.ToInt32(fc["id_encabezado"].ToString());
                                //List<PolizaData> poliza = GenerarDatosPoliza(docAceptado, null, null, conn, tx);
                                //RegistrarPolizas(GetUserId(User.Identity.Name), docAceptado, poliza, false, null, conn, tx);

                                var productosPorDestino = aceptados.Where(p => p.ContainsKey("destino")).GroupBy(p => Convert.ToInt32(p["destino"])).ToList();

                                foreach (var grupo in productosPorDestino)
                                {
                                    parameters = new Dictionary<string, object>();
                                    int destino = grupo.Key;
                                    var listaProductos = grupo.ToList();

                                    RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name), "Ingreso", resepcion, destino, "traslado", docAceptado, null, conn, tx);

                                    query = "SELECT cn.ulocation " +
                                        "FROM cattarimas ct " +
                                        "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                                        "WHERE ct.id_tarima = @destino";
                                    parameters.Add("destino", destino);
                                    var des = RunScalar(query, parameters, false, conn, tx);
                                    ubicaciones += $"<li> {des.ToString()} </li>";
                                }

                                parameters = new Dictionary<string, object>();
                                query = "UPDATE encabezadomov SET estatus_id = 20 WHERE id_encabezado = @encabezado";
                                parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                                RunUpdate(query, parameters, false, conn, tx);

                                tx.Commit();
                            }
                            catch
                            {
                                tx.Rollback();
                                throw;
                            }
                        }
                    }

                    listaHtml += $"Se traslado el material seleccionado desde Recepcion a la ubicacion <br> {ubicaciones} </ul>";
                }

                //if (hayDiscrepancia)
                //{
                //    var parameters = new Dictionary<string, object>();
                //    var query = "SELECT encabezados_padre FROM encabezadomov WHERE id_encabezado = @encabezado";
                //    parameters.Add("encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                //    int ordenPadre = Convert.ToInt32(RunScalar(query, parameters));

                //    query = "UPDATE polizas SET tiene_discrepancia = true, id_documento_disc = @documento_disc " +
                //        "WHERE referencia = @encabezadoPadre";
                //    parameters.Add("documento_disc", documentoDisc);
                //    parameters.Add("encabezadoPadre", ordenPadre);
                //    RunUpdate(query, parameters);
                //}

                return Json(new { icon = "success", title = "Material ingresado", showCancelButton = false, html = listaHtml });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Inventario/Inspeccion");
                return Json(new { icon = "error", text = "Ocurrio un error inesperado, revise los datos que se estan mandando o intentelo de nuevo.", showCancelButton = false });
            }
        }
    }
}