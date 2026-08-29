//@*Inventario*@
using BOS_ERP.Filters;
using BOS_ERP.Models;
using DocumentFormat.OpenXml.Office2013.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Query;
using Newtonsoft.Json;
using Npgsql;
using System.Configuration;
using System.Net;
using System.Web;
using static BOS_ERP.Controllers.ContabilidadController;

namespace BOS_ERP.Controllers
{
    public partial class InventarioController : Utilities
    {
        #region Obtener informacion general
        public JsonResult GetOrdenesCompra(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("uuid", fc["uuid"].ToString());
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var returnResult = new Dictionary<string, object>();

            // Obtener documento actual
            string query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.refe, em.id_encabezado, em.coment_aut AS observaciones, variacion, " +
                "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "       CASE WHEN em.nat = 'OC' AND em.variacion > 0 " +
                "           THEN '-' || num_to_letters(em.variacion) " +
                "           ELSE '' END AS folio, cp.n_prov, cp.cve_pais pais, cp.dir, cp.col, cp.pob, cp.tel, " +
                "   cp.rfc, cp.cp, CP.cve_mpio, em.encabezados_padre " +
                "FROM encabezadomov em " +
                "LEFT JOIN catproveedores cp ON cp.id_prov = em.refe AND cp.id_empresa = @id_empresa " +
                "WHERE em.uuid = @uuid";
            var ordenCompra = RunQuery(query, parameters)[0];
            returnResult.Add("ordenCompra", ordenCompra);

            query = "SELECT COUNT(*) " +
                "FROM pedimentos_partidas pp " +
                "INNER JOIN partidasdoc p ON p.id_partidas = pp.partida_id " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = p.encabezado_id " +
                "WHERE em.uuid = @uuid ";
            int qty = Convert.ToInt32(RunScalar(query, parameters));

            if (qty > 0)
            {
                query = "SELECT p.cve_prod AS codigo, p.descr_prod AS descripcion, pp.cantidad AS cantidad, pe.pedimento_sat, pe.id_pedimento, pp.id_pedimento_partida, " +
                    "   p.cve_vdr_cpr AS proveedor, p.refe AS proveedorid, p.id_partidas, p.ud, udm.id_udm AS unidad, " +
                    "   p.variacion, c.id_catproductos, p.pv_prod idp, p.f_pago_id fp, p.fol_doc_ant,  p.dto1 " +
                    "FROM pedimentos_partidas pp " +
                    "INNER JOIN pedimentos pe ON pe.id_pedimento = pp.pedimento_id " +
                    "INNER JOIN partidasdoc p ON p.id_partidas = pp.partida_id " +
                    "LEFT JOIN catproductos c  ON c.cve_prod = p.cve_prod AND c.empresa_id = @empresa_id " +
                    "INNER JOIN catunidades udm ON udm.cve_udm = p.ud " +
                    "INNER JOIN encabezadomov em ON em.id_encabezado = p.encabezado_id " +
                    "WHERE em.uuid = @uuid ";
                var partidas = RunQuery(query, parameters);
                returnResult.Add("partidas", partidas);
            }
            else
            {
                // Obtener partidas del documento actual
                query = "SELECT pd.cant_ud AS cantidad, pd.cve_prod AS codigo, pd.descr_prod AS descripcion, pd.cve_vdr_cpr AS proveedor, " +
                    "   pd.refe AS proveedorid, pd.id_partidas, pd.ud, udm.id_udm AS unidad, " +
                    "   pd.variacion, c.id_catproductos, pd.pv_prod idp, pd.f_pago_id fp, pd.fol_doc_ant, pd.dto1 " +
                    "FROM partidasdoc pd " +
                    "INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id " +
                    "LEFT JOIN catproductos c ON c.cve_prod = pd.cve_prod  AND c.empresa_id = @empresa_id " +
                    "INNER JOIN catunidades udm ON udm.cve_udm = pd.ud " +
                    "WHERE em.uuid = @uuid ";
                var partidas = RunQuery(query, parameters);
                returnResult.Add("partidas", partidas);
            }

            query = "SELECT ct.codigo AS tarima, cn.ulocation, ct.id_tarima, csu.descripcion " +
                "FROM cattarimas ct " +
                "LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "LEFT JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "LEFT JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "LEFT JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id " +
                "WHERE ca.tipo = 'Stock' AND csu.id_sucursal = @sucursal";
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            var ubicaciones = RunQuery(query, parameters);
            returnResult.Add("ubicaciones", ubicaciones);

            if (Convert.ToInt32(ordenCompra["variacion"]) > 0)
            {
                query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                    "   em.fch, em.cli_prov, em.refe, em.id_encabezado, em.coment_aut AS observaciones, variacion, " +
                    "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                    "       CASE WHEN em.nat = 'OC' AND em.variacion > 0 " +
                    "           THEN '-' || num_to_letters(em.variacion) " +
                    "           ELSE '' END AS folio, cp.n_prov, cp.cve_pais pais, cp.dir, cp.col, cp.pob, cp.tel, " +
                    "   cp.rfc, cp.cp, CP.cve_mpio, em.encabezados_padre " +
                    "FROM encabezadomov em " +
                    "LEFT JOIN catproveedores cp ON cp.id_prov = em.refe AND cp.id_empresa = @id_empresa " +
                    "WHERE em.variacion_padre = (SELECT variacion_padre FROM encabezadomov WHERE uuid = @uuid)";
                var varPadre = RunQuery(query, parameters)[0];
                returnResult.Add("variacionPadre", varPadre);
            }

            return Json(returnResult);
        }

        public JsonResult GetPartidasPedimentos(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT pd.cant_ud AS cantidad, pd.cve_prod AS codigo, pd.descr_prod AS descripcion, " +
                    "   pd.cve_vdr_cpr AS proveedor, pd.refe AS proveedorid, pd.id_partidas, pd.ud, udm.id_udm AS unidad, " +
                    "   pd.variacion, c.id_catproductos, pd.pv_prod idp, pd.f_pago_id fp, pd.fol_doc_ant, " +
                    "   pp.cantidad, p.pedimento_sat, pp.fecha " +
                    "FROM partidasdoc pd " +
                    "INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id " +
                    "LEFT JOIN catproductos c ON c.cve_prod = pd.cve_prod AND c.empresa_id = @empresa_id " +
                    "INNER JOIN catunidades udm ON udm.cve_udm = pd.ud " +
                    "LEFT JOIN pedimentos_partidas pp ON pp.partida_id = pd.id_partidas " +
                    "LEFT JOIN pedimentos p ON p.id_pedimento  = pp.pedimento_id " +
                    "WHERE pd.id_partidas = @partida_id AND p.id_pedimento = @pedimento_id";
            parameters.Add("partida_id", Convert.ToInt32(fc["partida_id"].ToString()));
            parameters.Add("pedimento_id", Convert.ToInt32(fc["pedimento_id"].ToString()));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var pedimentos = RunQuery(query, parameters);

            return Json(pedimentos);
        }

        public JsonResult GetListaUnidades()
        {
            string query = "SELECT id_udm, cve_udm, descripcion FROM catunidades";
            var unidades = RunQuery(query);

            return Json(unidades);
        }

        public JsonResult GetListaClientes()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT cve_cli, n_cli, id_cliente FROM catclientes WHERE empresa_id = @empresa_id";
            var clientes = RunQuery(query, parameters);

            query = "SELECT clave_vendedor, nombre, id FROM vendedores";
            var vendedores = RunQuery(query);

            return Json(new { clientes, vendedores });
        }
        #endregion

        #region Acciones de ingreso de material
        [AuditAction(Modulo = "Inventario", Accion = "Proceso de recepcion de material, genera documento de recepcion a inspeccion o de cuarentena")]
        public JsonResult RecibirMaterial(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();

                var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                var discrepancias = products.Where(p => p.ContainsKey("status") && !Convert.ToBoolean(p["status"])).ToList();
                var aceptados = products.Where(p => p.ContainsKey("status") && Convert.ToBoolean(p["status"])).ToList();

                var docsGenerados = new List<string>();
                int id_encabezado = Convert.ToInt32(fc["encabezado"].ToString());

                parameters = new Dictionary<string, object>();
                string query = "SELECT ct.id_tarima FROM catalmacenes c " +
                    "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                    "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                    "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                    "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                    "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                    "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Recepcion'";
                parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                int destino = Convert.ToInt32(RunScalar(query, parameters));

                if (destino == 0 || destino == null)
                {
                    return Json(new { icon = "error", title = "Ocurrio un error con la sucursal", html = "La sucursal a la que perteneces no cuenta con almacen de recepcion, favor de contactar a soporte" });
                }

                parameters = new Dictionary<string, object>();
                query = "SELECT fch1, fch2, fch3, fch4, fch5, fch6, firma1, firma6, " +
                    "   usr1, usr2, usr3, usr4, usr5, usr6, nat, imp " +
                    "FROM encabezadomov " +
                    "WHERE id_encabezado = @encabezado";
                parameters.Add("encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
                var usr = RunQuery(query, parameters)[0];

                decimal? descuentos = aceptados.Sum(p => GetDecimal(p["dto"], 0));

                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 5;
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.Firma1 = GetString(usr["firma1"]);
                encabezado.Usr1 = GetInt(usr["usr1"]);
                encabezado.Fch1 = usr["fch1"] == DBNull.Value ? null : (DateTime?)usr["fch1"];
                encabezado.Usr2 = GetInt(usr["usr2"]);
                encabezado.Fch2 = usr["fch2"] == DBNull.Value ? null : (DateTime?)usr["fch2"];
                encabezado.Usr3 = GetInt(usr["usr3"]);
                encabezado.Fch3 = usr["fch3"] == DBNull.Value ? null : (DateTime?)usr["fch3"];
                encabezado.Usr4 = GetInt(usr["usr4"]);
                encabezado.Fch4 = usr["fch4"] == DBNull.Value ? null : (DateTime?)usr["fch4"];
                encabezado.Usr5 = GetInt(usr["usr5"]);
                encabezado.Fch5 = usr["fch5"] == DBNull.Value ? null : (DateTime?)usr["fch5"];
                encabezado.Usr6 = GetInt(usr["usr6"]);
                encabezado.Fch6 = usr["fch6"] == DBNull.Value ? null : (DateTime?)usr["fch6"];
                encabezado.Firma6 = GetString(usr["firma6"]);
                encabezado.Estatus = 18;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.Coment1 = fc["comentario"].ToString();
                encabezado.Imp = GetDecimal(usr["imp"]);
                encabezado.Dto = descuentos;

                if (usr["nat"].ToString() == "OCD" && DoesUserHasRight(User.Identity.Name, "usar_compra_directa"))
                {
                    parameters = new Dictionary<string, object>();
                    query = "SELECT ct.id_tarima FROM catalmacenes c " +
                    "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                    "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                    "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                    "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                    "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                    "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Stock'";
                    parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                    destino = Convert.ToInt32(RunScalar(query, parameters));

                    encabezado.Estatus = 11;
                    encabezado.IdTpDoc = 57;
                    encabezado.TpMov = "INGINV";
                    encabezado.TipoPoceso = "ingreso_directo";
                    encabezado.CliProv = products[0]["proveedor_nombre"].ToString();
                    encabezado.Ref = string.IsNullOrWhiteSpace(products[0]["proveedor"].ToString()) || products[0]["proveedor"].ToString().ToLower() == "null" ? (int?)null : Convert.ToInt32(products[0]["proveedor"]);
                    encabezado.EncabezadoPadre = Convert.ToInt32(products[0]["encabezado"]);
                }
                else if (discrepancias.Count > 0)
                {
                    encabezado.IdTpDoc = 32;
                    encabezado.TpMov = "RINVP";
                    encabezado.TipoPoceso = "ingreso_parcial";
                    encabezado.CliProv = discrepancias[0]["proveedor_nombre"].ToString();
                    encabezado.Ref = string.IsNullOrWhiteSpace(discrepancias[0]["proveedor"].ToString()) || discrepancias[0]["proveedor"].ToString().ToLower() == "null" ? (int?)null : Convert.ToInt32(discrepancias[0]["proveedor"]);
                    encabezado.EncabezadoPadre = Convert.ToInt32(discrepancias[0]["encabezado"]);
                }
                else if (aceptados.Count > 0)
                {
                    encabezado.IdTpDoc = 26;
                    encabezado.TpMov = "RINV";
                    encabezado.TipoPoceso = "ingreso_completo";
                    encabezado.CliProv = aceptados[0]["proveedor_nombre"].ToString();
                    encabezado.Ref = string.IsNullOrWhiteSpace(aceptados[0]["proveedor"].ToString()) || aceptados[0]["proveedor"].ToString().ToLower() == "null" ? (int?)null : Convert.ToInt32(aceptados[0]["proveedor"]);
                    encabezado.EncabezadoPadre = Convert.ToInt32(aceptados[0]["encabezado"]);
                }

                int nro = 1;
                var partidas = new List<PartidaDocumento>();

                foreach (var disc in products)
                {
                    if (Convert.ToInt32(disc["cantidad"]) == 0)
                    {
                        Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        return Json(new { icon = "error", text = $"Debes indicar la cantidad en discrepancia para {disc["descripcion"].ToString()}", showCancelButton = false });
                    }

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
                        Ud = GetString(disc["cve_unidad"]),
                        FolDocAnt = "ingreso_recepcion",
                        PvProd = pvProd,
                        ImpPart = cantUd * pvProd,
                        FPagoId = GetInt(disc["fp"]),
                        Dto1 = GetDecimal(disc["dto"], 0),
                    });
                }

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
                            RegistrarMovimiento(products, GetUserId(User.Identity.Name), "ingreso_recepcion", null, destino, "Recepcion de material", Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);

                            // La poliza y la cartera se generan al registrar la factura del
                            // proveedor (Compras > Ordenes de compra), no al recibir el material.
                            RegistrarCompra(id_encabezado, GetUserId(User.Identity.Name), conn, tx);

                            parameters = new Dictionary<string, object>();
                            query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                            parameters.Add("id", Convert.ToInt32(id_encabezado));
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

                var listaHtml = "<ul style='text-align:left'>";
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

                return Json(new { icon = "success", title = "Material ingresado", showCancelButton = false, html = "Folios generados:" + listaHtml });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", text = ex.Message, showCancelButton = false });
            }
        }

        [AuditAction(Modulo = "Inventario", Accion = "Confirmación de recepción de devolución RMD, genera documento RINVDEV y actualiza estatus del reporte")]
        public JsonResult RecibirDevolucion(IFormCollection fc)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        var documento = new Dictionary<string, object>();
                        var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                        int reporteId = int.TryParse(fc["reporteId"].ToString(), out int rid) ? rid : 0;

                        // ── Validar lista no vacía ────────────────────────────
                        if (products == null || products.Count == 0)
                        {
                            Response.StatusCode = (int)HttpStatusCode.BadRequest;
                            return Json(new
                            {
                                icon = "error",
                                text = "La lista de productos está vacía. Verifica que la tabla tenga datos válidos.",
                                showCancelButton = false
                            });
                        }

                        string queryEncPadre = "SELECT encabezado_id FROM rmd_reporte_detalle WHERE reporte_id = @reporte_id";

                        // ── Buscar tarima de cuarentena de la sucursal ────────
                        var parameters = new Dictionary<string, object>();
                        parameters.Add("reporte_id", reporteId);

                        int encPadre = Convert.ToInt32(RunScalar(queryEncPadre, parameters, false, conn, tx));
                        decimal imp = 0;
                        string query = "SELECT ct.id_tarima " +
                            "FROM catalmacenes c " +
                            "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                            "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                            "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                            "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                            "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                            "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Devolucion'";
                        parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                        int destino = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                        if (destino == 0)
                            return Json(new
                            {
                                icon = "error",
                                title = "Error de configuración",
                                html = "La sucursal no tiene almacén de cuarentena. Contacta a soporte.",
                            });

                        // ── Construir encabezado del documento ────────────────
                        var encabezado = new DocumentoEncabezado();
                        encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                        encabezado.IdArea = 5;
                        encabezado.IdTpDoc = 30;
                        encabezado.Anio = DateTime.Now.Year;
                        encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                        encabezado.Fch = DateTime.Now;
                        encabezado.TpMov = "RINVDEV";
                        encabezado.UsrDoc = User.Identity.Name;
                        encabezado.FchCap = DateTime.Now;
                        encabezado.Usr0 = GetUserId(User.Identity.Name);
                        encabezado.Fch0 = DateTime.Now;
                        encabezado.CliProv = "srs";
                        encabezado.Estatus = 11;
                        encabezado.UsrDep = GetAreaName(User.Identity.Name);
                        encabezado.TipoPoceso = "Devolucion";
                        encabezado.Coment1 = fc["observaciones"].ToString() ?? "";
                        encabezado.Coment2 = fc["motivo"].ToString() ?? "";
                        encabezado.Coment3 = fc["factura"].ToString() ?? "";
                        encabezado.EncabezadoPadre = encPadre;

                        // ── Construir partidas ────────────────────────────────
                        int nro = 1;
                        var partidas = new List<PartidaDocumento>();

                        foreach (var disc in products)
                        {
                            parameters = new Dictionary<string, object>();
                            query = "SELECT cve_udm FROM catunidades WHERE id_udm = @unidad";
                            parameters.Add("unidad", Convert.ToInt32(disc["unidad"]));
                            var unidad = RunScalar(query, parameters, false, conn, tx);

                            query = "SELECT pv_prod, dto1 FROM partidasdoc WHERE encabezado_id = @encabezado_id AND cve_prod = @cve_prod";
                            parameters.Add("encabezado_id", encPadre);
                            parameters.Add("cve_prod", disc["codigo"].ToString());

                            var precios = RunQuery(query, parameters, false, conn, tx);

                            PartidaDocumento partida = new PartidaDocumento();

                            partida.NroPart = nro++;
                            partida.CveProd = disc["codigo"].ToString();
                            partida.DescrProd = disc["descripcion"].ToString();
                            partida.CantUd = GetDecimal(disc["cantidad"]);
                            partida.Ud = unidad.ToString() ?? "";
                            partida.FolDocAnt = fc["motivo"].ToString() ?? "";
                            partida.PvProd = GetDecimal(precios[0]["pv_prod"]);
                            partida.Dto1 = GetDecimal(precios[0]["dto1"]);
                            partida.ImpPart = GetDecimal(precios[0]["pv_prod"]) * GetDecimal(disc["cantidad"]);

                            partidas.Add(partida);

                            imp += Convert.ToDecimal(precios[0]["pv_prod"]) * Convert.ToDecimal(disc["cantidad"]);

                        }
                        encabezado.Imp = imp;

                        // ── Generar documento dentro de la transacción ────────
                        documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                        // ── Registrar movimiento de inventario ────────────────
                        RegistrarMovimiento(products, GetUserId(User.Identity.Name), "Ingreso", null, destino, fc["motivo"].ToString() ?? "", Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);

                        // ── Si viene de un reporte RMD, actualizar sus estatus ─
                        if (reporteId > 0)
                        {
                            // Marcar reporte como DOC_GENERADO
                            parameters = new Dictionary<string, object>();
                            query = "UPDATE rmd_reporte SET estatus_actual = 'DOC_GENERADO', usuario_inventario = @usr, fecha_recepcion = NOW() WHERE id = @reporte_id";
                            parameters.Add("reporte_id", reporteId);
                            parameters.Add("usr", GetUserId(User.Identity.Name));
                            RunUpdate(query, parameters, false, conn, tx);

                            // Actualizar detalle con el id del doc generado
                            parameters = new Dictionary<string, object>();
                            query = "UPDATE rmd_reporte_detalle SET estatus_detalle = 'DOC_GENERADO', doc_generado_id = @doc_id WHERE reporte_id = @reporte_id";
                            parameters.Add("reporte_id", reporteId);
                            parameters.Add("doc_id", Convert.ToInt32(documento["IdEncabezado"]));
                            RunUpdate(query, parameters, false, conn, tx);

                            // Comentario de trazabilidad en cada encabezado original
                            parameters = new Dictionary<string, object>();
                            query = "SELECT encabezado_id FROM rmd_reporte_detalle WHERE reporte_id = @reporte_id";
                            parameters.Add("reporte_id", reporteId);
                            var detalles = RunQuery(query, parameters, false, conn, tx);

                            foreach (var det in detalles)
                            {
                                var pComment = new Dictionary<string, object>
                                {
                                    ["enc_id"] = Convert.ToInt32(det["encabezado_id"]),
                                    ["comentario"] = $"[RECEPCIÓN DEVOLUCIÓN] Folio generado: {documento["folio_generado"]} — Usuario inventario: {User.Identity.Name}",
                                    ["usr"] = GetUserId(User.Identity.Name),
                                    ["ref_id"] = reporteId,
                                };

                                query = "INSERT INTO enc_comentarios (" +
                                    "   encabezado_id, tipo_comentario, comentario, usuario_id, fecha, origen_modulo, referencia_id) " +
                                    "VALUES (" +
                                    "   @enc_id, 'RECEPCION_DEV', @comentario, @usr, NOW(), 'Inventario', @ref_id)";

                                RunQuery(query, pComment, false, conn, tx);
                            }
                        }

                        tx.Commit();

                        return Json(new { icon = "success", title = "Material recibido", html = $"Se generó el documento de devolución con el folio <b>{documento["folio_generado"]}</b>", showCancelButton = false, });
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        LogErrorHelper.RegistrarLog("Inventario", "RecibirDevolucion",
                            $"Error: {ex.Message}", nivel: "ERROR");

                        return Json(new { icon = "error", text = "Ocurrió un error inesperado. Por favor inténtalo más tarde.", showCancelButton = false, });
                    }
                }
            }
        }

        [AuditAction(Modulo = "Inventario", Accion = "Proceso de recepcion de material internacional, genera un documento de recepcion de inventario interncional y se va a inspeccion o genera uno de discrepacia y se va a cuarenten")]
        public JsonResult RecibirMaterialInternacional(IFormCollection fc)
        {
            try
            {
                var docsGenerados = new List<string>();

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                            var discrepancias = products.Where(p => p.ContainsKey("status") && !Convert.ToBoolean(p["status"])).ToList();
                            var aceptados = products.Where(p => p.ContainsKey("status") && Convert.ToBoolean(p["status"])).ToList();
                            var parameters = new Dictionary<string, object>();
                            string query = "";

                            if (discrepancias.Count > 0)
                            {
                                parameters = new Dictionary<string, object>();
                                query = "SELECT ct.id_tarima FROM catalmacenes c " +
                                    "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                                    "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                                    "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                                    "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                                    "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                                    "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Cuarentena'";
                                parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                                int destino = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                                if (destino == 0 || destino == null)
                                {
                                    return Json(new { icon = "error", title = "Ocurrio un error con la sucursal", html = "La sucursal a la que perteneces no cuenta con almacen de cuarentena, favor de contactar a soporte" });
                                }

                                parameters = new Dictionary<string, object>();
                                query = "SELECT fch1, fch2, fch3, fch4, fch5, fch6, firma0, " +
                                    "   firma1, firma2, firma3, firma4, firma5, firma6, " +
                                    "   usr1, usr2, usr3, usr4, usr5, usr6, nat, imp " +
                                    "FROM encabezadomov " +
                                    "WHERE id_encabezado = @encabezado";
                                parameters.Add("encabezado", Convert.ToInt32(discrepancias[0]["encabezado"]));
                                var usr = RunQuery(query, parameters)[0];

                                string motivo = "parcial";
                                var encabezado = new DocumentoEncabezado
                                {
                                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                                    IdArea = 5,
                                    IdTpDoc = 38,
                                    Anio = DateTime.Now.Year,
                                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                                    Fch = DateTime.Now,
                                    TpMov = "RINVDI",
                                    UsrDoc = User.Identity.Name,
                                    FchCap = DateTime.Now,
                                    Usr0 = GetInt(usr["usr0"]),
                                    Fch0 = GetDate(usr["fch0"]),
                                    Usr1 = GetInt(usr["usr1"]),
                                    Fch1 = GetDate(usr["fch1"]),
                                    Usr2 = GetInt(usr["usr1"]),
                                    Fch2 = GetDate(usr["fch1"]),
                                    Usr3 = GetUserId(User.Identity.Name),
                                    Fch3 = DateTime.Now,
                                    CliProv = discrepancias[0]["proveedor_nombre"].ToString() ?? "",
                                    Ref = string.IsNullOrWhiteSpace(discrepancias[0]["proveedor"].ToString()) || discrepancias[0]["proveedor"].ToString().ToLower() == "null" ? (int?)null : Convert.ToInt32(discrepancias[0]["proveedor"]),
                                    EncabezadoPadre = Convert.ToInt32(discrepancias[0]["encabezado"]),
                                    Estatus = 18,
                                    UsrDep = GetAreaName(User.Identity.Name),
                                    TipoPoceso = "Parcial",
                                    Coment1 = fc["comentario"].ToString(),
                                    Coment2 = motivo,
                                };

                                int nro = 1;
                                var partidas = new List<PartidaDocumento>();
                                var precioTotal = discrepancias.Sum(p => Convert.ToDecimal(p["idp"]));

                                foreach (var disc in discrepancias)
                                {
                                    //if (Convert.ToDecimal(disc["cantidad"]) == 0)
                                    //{
                                    //    Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                    //    return Json(new { icon = "error", text = $"Debes indicar la cantidad en discrepancia para {disc["descripcion"].ToString()}", showCancelButton = false });
                                    //}

                                    motivo = "discrepancia";
                                    partidas.Add(new PartidaDocumento
                                    {
                                        NroPart = nro++,
                                        CveProd = disc["codigo"].ToString(),
                                        DescrProd = disc["descripcion"].ToString(),
                                        CantUd = Convert.ToDecimal(disc["cantidad"]),
                                        CveVdrCpr = string.IsNullOrEmpty(disc["proveedor_nombre"].ToString()) || disc["proveedor_nombre"].ToString().ToLower() == "null" ? (string)null : disc["proveedor_nombre"].ToString(),
                                        Ref = string.IsNullOrWhiteSpace(disc["proveedor"].ToString()) || disc["proveedor"].ToString().ToLower() == "null" ? (int?)null : Convert.ToInt32(disc["proveedor"]),
                                        Ud = disc["cve_unidad"].ToString(),
                                        FolDocAnt = motivo,
                                        PvProd = Convert.ToDecimal(disc["idp"]),
                                        ImpPart = precioTotal,
                                        FPagoId = Convert.ToInt32(disc["f_pago"]),
                                    });
                                }

                                var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                                docsGenerados.Add(documento["folio_generado"].ToString());
                                RegistrarMovimiento(discrepancias, GetUserId(User.Identity.Name), "Ingreso", null, destino, motivo, Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);


                                var id_encabezado = Convert.ToInt32(fc["id_encabezado"].ToString());
                                // La poliza se genera al registrar la factura del proveedor
                                RegistrarCompra(id_encabezado, GetUserId(User.Identity.Name), conn, tx);
                            }

                            if (aceptados.Count > 0)
                            {
                                parameters = new Dictionary<string, object>();
                                query = "SELECT ct.id_tarima FROM catalmacenes c " +
                                    "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                                    "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                                    "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                                    "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                                    "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                                    "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Recepcion'";
                                parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                                int destino = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                                if (destino == 0 || destino == null)
                                {
                                    return Json(new { icon = "error", title = "Ocurrio un error con la sucursal", html = "La sucursal a la que perteneces no cuenta con almacen de recepcion, favor de contactar a soporte" });
                                }

                                parameters = new Dictionary<string, object>();
                                query = "SELECT fch0, fch1, fch2, fch3, fch4, fch5, fch6, firma0, " +
                                    "   firma0, firma1, firma2, firma3, firma4, firma5, firma6, " +
                                    "   usr0, usr1, usr2, usr3, usr4, usr5, usr6, nat, imp " +
                                    "FROM encabezadomov " +
                                    "WHERE id_encabezado = @encabezado";
                                parameters.Add("encabezado", Convert.ToInt32(aceptados[0]["encabezado"]));
                                var usr = RunQuery(query, parameters)[0];

                                string motivo = "Parcial";
                                var encabezado = new DocumentoEncabezado
                                {
                                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                                    IdArea = 5,
                                    IdTpDoc = 37,
                                    Anio = DateTime.Now.Year,
                                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                                    Fch = DateTime.Now,
                                    TpMov = "RINVI",
                                    UsrDoc = User.Identity.Name,
                                    FchCap = DateTime.Now,
                                    Usr0 = GetInt(usr["usr0"]),
                                    Fch0 = GetDate(usr["fch0"]),
                                    Firma0 = GetString(usr["firma0"]),
                                    Usr1 = GetInt(usr["usr1"]),
                                    Fch1 = GetDate(usr["fch1"]),
                                    Firma1 = GetString(usr["firma1"]),
                                    Usr2 = GetInt(usr["usr1"]),
                                    Fch2 = GetDate(usr["fch1"]),
                                    Firma2 = GetString(usr["firma2"]),
                                    Usr3 = GetUserId(User.Identity.Name),
                                    Fch3 = DateTime.Now,
                                    CliProv = aceptados[0]["proveedor_nombre"].ToString() ?? "",
                                    Ref = string.IsNullOrWhiteSpace(aceptados[0]["proveedor"].ToString()) || aceptados[0]["proveedor"].ToString().ToLower() == "null" ? (int?)null : Convert.ToInt32(aceptados[0]["proveedor"]),
                                    EncabezadoPadre = Convert.ToInt32(aceptados[0]["encabezado"]),
                                    Estatus = 18,
                                    UsrDep = GetAreaName(User.Identity.Name),
                                    TipoPoceso = "Parcial",
                                    Coment1 = fc["comentario"].ToString(),
                                    Coment2 = motivo,
                                };

                                int nro = 1;
                                var precioTotal = aceptados.Sum(p => Convert.ToDecimal(p["idp"]));
                                var partidas = new List<PartidaDocumento>();
                                foreach (var disc in aceptados)
                                {
                                    motivo = "ingreso";
                                    partidas.Add(new PartidaDocumento
                                    {
                                        NroPart = nro++,
                                        CveProd = disc["codigo"].ToString(),
                                        DescrProd = disc["descripcion"].ToString(),
                                        CantUd = Convert.ToDecimal(disc["cantidad"]),
                                        CveVdrCpr = string.IsNullOrEmpty(disc["proveedor_nombre"].ToString()) || disc["proveedor_nombre"].ToString().ToLower() == "null" ? (string)null : disc["proveedor_nombre"].ToString(),
                                        Ref = string.IsNullOrWhiteSpace(disc["proveedor"].ToString()) || disc["proveedor"].ToString().ToLower() == "null" ? (int?)null : Convert.ToInt32(disc["proveedor"]),
                                        Ud = disc["cve_unidad"].ToString(),
                                        FolDocAnt = motivo,
                                        PvProd = Convert.ToDecimal(disc["idp"]),
                                        ImpPart = precioTotal,
                                        FPagoId = Convert.ToInt32(disc["f_pago"]),
                                    });

                                }

                                //if (discrepancias.Count > 0)
                                //{
                                //    parameters = new Dictionary<string, object>();
                                //    query = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb)";
                                //    parameters.Add("p_id_original", Convert.ToInt32(discrepancias[0]["encabezado"]));
                                //    parameters.Add("p_total", precioTotal);
                                //    parameters.Add("p_observaciones", fc["comentario"].ToString());
                                //    parameters.Add("p_usuario", GetUserId(User.Identity.Name));
                                //    parameters.Add("p_partidas", JsonConvert.SerializeObject(partidas));
                                //    var poClonada = RunQuery(query, parameters)[0];
                                //}

                                var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                                docsGenerados.Add(documento["folio_generado"].ToString());
                                RegistrarMovimiento(aceptados, GetUserId(User.Identity.Name), "Ingreso", null, destino, motivo, Convert.ToInt32(documento["IdEncabezado"]), null, conn, tx);

                                var id_encabezado = Convert.ToInt32(fc["id_encabezado"].ToString());
                                // La poliza se genera al registrar la factura del proveedor
                                RegistrarCompra(id_encabezado, GetUserId(User.Identity.Name), conn, tx);
                            }

                            parameters = new Dictionary<string, object>();
                            query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @id";
                            parameters.Add("id", Convert.ToInt32(aceptados[0]["encabezado"]));
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

                var listaHtml = "<ul style='text-align:left'>";
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

                return Json(new { icon = "success", title = "Material ingresado", showCancelButton = false, html = "Folios generados:" + listaHtml });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", text = "Ocurrio un error inesperado, revise los datos que se estan mandando o intentelo de nuevo.", showCancelButton = false });
            }
        }

        [AuditAction(Modulo = "Inventario", Accion = "Proceso de inventario extraordinario para venta")]
        public JsonResult RecibirMaterialExtraordinario(IFormCollection fc, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { icon = "error", title = "Ocurrió un error", html = "El archivo no contiene información válida o no fue cargado correctamente.", crearProducto = false });
                }

                var productosNoEncontrados = new List<string>();
                var fraccionesNoEncontradas = new List<string>();
                var productos = new List<Dictionary<string, object>>();
                var partidas = new List<PartidaDocumento>();
                var parameters = new Dictionary<string, object>();
                decimal? imp = 0m;
                decimal? dto = 0m;
                string referencia = null;
                decimal? paridad = null;

                string query = "SELECT cve_cli, n_cli, id_cliente FROM catclientes WHERE id_cliente = @cliente AND empresa_id = @empresa_id ";
                parameters.Add("cliente", GetInt(fc["cliente"].ToString()));
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var _cli = RunQuery(query, parameters);

                if (_cli.Count() == 0)
                {
                    //Response.StatusCode = 400;
                    return Json(new { icon = "error", title = "Cliente no encontrado", html = "El cliente seleccionado no existe o no fue identificado correctamente. Verifica la información e inténtalo nuevamente.", crearProducto = false });
                }

                var cli = _cli[0];

                // Lectura del archivo .csv
                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    string line;
                    int linea = 0;
                    int nro = 1;

                    while ((line = reader.ReadLine()) != null)
                    {
                        linea++;

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        var columnas = line.Split(',');

                        // Necesitas al menos 8 columnas
                        if (columnas.Length < 9)
                        {
                            //Response.StatusCode = 400;
                            return Json(new { icon = "error", title = "Formato de archivo inválido", html = $"La línea {linea} no cumple con la estructura esperada del archivo CSV.", crearProducto = false });
                        }

                        string clave = columnas[0].Trim();
                        string descripcion = columnas[1].Trim();
                        decimal? cantidad = GetDecimal(columnas[2], 0);
                        string unidadClave = columnas[3].Trim();
                        decimal? precio = GetDecimal(columnas[4], 0);
                        decimal? descuento = GetDecimal(columnas[5], 0);
                        decimal? cantidadDescuento = GetDecimal(columnas[6], 0);
                        decimal? total = GetDecimal(columnas[7], 0);
                        string fraccionArancelaria = GetString(columnas[8]);
                        if (referencia == null && columnas.Length > 9)
                        {
                            referencia = columnas[9].Trim();
                        }
                        if (paridad == null && columnas.Length > 10)
                        {
                            paridad = GetDecimal(columnas[10]);
                        }

                        imp += total;
                        dto += cantidadDescuento;

                        // 🔎 Validar existencia del producto ANTES de tocar la BD
                        if (!ProductoExiste(clave))
                        {
                            productosNoEncontrados.Add($"{clave} - {descripcion}");
                            continue;
                        }

                        // 1. No viene en el Excel
                        if (string.IsNullOrWhiteSpace(fraccionArancelaria))
                        {
                            fraccionesNoEncontradas.Add(
                                $"{clave} - {descripcion}: el archivo no tiene fracción arancelaria. Agrégala al Excel y vuelve a intentar."
                            );
                            continue;
                        }

                        // 2. Viene, pero no existe en la BD
                        if (!FraccionExiste(clave))
                        {
                            fraccionesNoEncontradas.Add(
                                $"{clave} - {descripcion}: la fracción arancelaria '{fraccionArancelaria}' no existe en el sistema."
                            );
                            continue;
                        }

                        // 📦 Obtener IDs
                        parameters = new Dictionary<string, object>();
                        parameters.Add("clave", clave);
                        parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        string queryProd = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @clave AND empresa_id = @empresa_id";
                        object prodResult = RunScalar(queryProd, parameters);

                        int id_producto = Convert.ToInt32(prodResult);

                        parameters = new Dictionary<string, object>();
                        parameters.Add("unidad", unidadClave);

                        string queryUdm = "SELECT id_udm FROM catunidades WHERE cve_udm = @unidad";
                        object udmResult = RunScalar(queryUdm, parameters);

                        if (udmResult == null)
                        {
                            Response.StatusCode = 400;
                            return Json(new
                            { icon = "error", title = "Unidad de medida no válida", html = $"La unidad '{unidadClave}' en la línea {linea} no se encuentra registrada en el sistema.", crearProducto = false });
                        }

                        int id_unidad = Convert.ToInt32(udmResult);

                        partidas.Add(new PartidaDocumento
                        {
                            NroPart = nro++,
                            CveProd = clave,
                            DescrProd = descripcion,
                            CantUd = cantidad,
                            CveVdrCpr = "srs",
                            Ref = null,
                            Ud = unidadClave,
                            FolDocAnt = "entrada_extraordinaria",
                            PvProd = precio,
                            ImpPart = total,
                            FPagoId = null,
                            IdProducto = id_producto,
                            Dto1 = cantidadDescuento,
                            Iva = descuento
                        });

                        productos.Add(new Dictionary<string, object>
                        {
                            { "id_producto", id_producto },
                            { "codigo", clave },
                            { "descripcion", descripcion },
                            { "cantidad", cantidad },
                            { "unidad", id_unidad },
                        });
                    }
                }

                // 🚨 Productos faltantes
                if (fraccionesNoEncontradas.Any())
                {
                    //Response.StatusCode = 400;

                    string html = "<ul style='text-align:left'>";
                    foreach (var p in fraccionesNoEncontradas)
                    {
                        html += $"<li>{HttpUtility.HtmlEncode(p)}</li>";
                    }
                    html += "</ul>";

                    return Json(new { title = "Productos sin fraccion arancelaria", html, icon = "warning", crearFraccion = true, productos = productosNoEncontrados });
                }

                if (productosNoEncontrados.Any())
                {
                    //Response.StatusCode = 400;

                    string html = "<ul style='text-align:left'>";
                    foreach (var p in productosNoEncontrados)
                    {
                        html += $"<li>{HttpUtility.HtmlEncode(p)}</li>";
                    }
                    html += "</ul>";

                    return Json(new { title = "Productos no registrados", html, icon = "warning", crearProducto = true, productos = productosNoEncontrados });
                }

                parameters = new Dictionary<string, object>();
                query = "SELECT ct.id_tarima, c.id_almacen FROM catalmacenes c " +
                            "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                            "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                            "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                            "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                            "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                            "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Temporal'";
                parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                var destinoResult = RunQuery(query, parameters);

                if (destinoResult.Count == 0)
                {
                    Response.StatusCode = 400;
                    return Json(new { icon = "error", title = "Configuración incompleta", html = "No se encontró una tarima de almacén temporal configurada para la sucursal actual." });
                }

                int destino = Convert.ToInt32(destinoResult[0]["id_tarima"]);
                int almacen = Convert.ToInt32(destinoResult[0]["id_almacen"]);

                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 5;
                encabezado.IdTpDoc = 61;
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "AIEINV";
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);
                encabezado.Fch0 = DateTime.Now;
                encabezado.CliProv = cli["cve_cli"].ToString();
                encabezado.Ref = GetInt(fc["cliente"].ToString());
                encabezado.EncabezadoPadre = 0;
                encabezado.Estatus = 1;
                encabezado.UsrDep = GetAreaName(User.Identity.Name);
                encabezado.TipoPoceso = "entrada_extraordinaria";
                encabezado.Imp = imp;
                encabezado.Incoterm = "EXW";
                encabezado.Coment1 = "Referencia de documento kepler: " + referencia;
                encabezado.VdrCpr = fc["vendedor"].ToString();
                encabezado.Alm = almacen.ToString();
                encabezado.Ccy = "DLLS";
                encabezado.Mdp = "PUE";
                encabezado.Dto = dto;
                encabezado.Par = paridad;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                var documento = new Dictionary<string, object>();

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1️⃣ Generas documento
                            documento = GenerarDocumentoConPartidas(
                                encabezado,
                                partidas,
                                conn,
                                tx
                            );

                            // 2️⃣ Registras movimiento
                            RegistrarMovimiento(
                                productos,
                                GetUserId(User.Identity.Name),
                                "Ingreso",
                                null,
                                destino,
                                "Entrada de material extraordinario",
                                Convert.ToInt32(documento["IdEncabezado"]),
                                null,
                                conn,
                                tx
                            );

                            // 3️⃣ Todo bien → commit
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

                return Json(new
                {
                    icon = "success",
                    title = "Proceso completado",
                    html = $"El archivo fue procesado correctamente. Se generó el documento con folio {documento["folio_generado"]}.",
                    productos
                });
            }
            catch (PostgresException ex)
            {
                Response.StatusCode = 400;
                return Json(new
                {
                    icon = "error",
                    title = "Error en base de datos",
                    html = ex.MessageText,
                    //html = "Ocurrió un error al procesar la información en la base de datos. Contacta al administrador del sistema."
                });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new
                {
                    icon = "error",
                    title = "Error inesperado",
                    html = ex.Message,
                    //html = "Ocurrió un error inesperado durante el procesamiento. Intenta nuevamente o contacta al área de sistemas."
                });
            }
        }

        public JsonResult RecibirTuboBarra([FromBody] List<BarraRequest> barras)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;

            try
            {
                if (barras == null || barras.Count == 0)
                {
                    return Json(new { icon = "error", title = "Debe indicar al menos una barra." });
                }

                foreach (var barra in barras)
                {
                    if (string.IsNullOrWhiteSpace(barra.ProductoId))
                        return Json(new { icon = "error", title = "Todas las barras deben tener un producto." });

                    if (barra.Longitud <= 0)
                        return Json(new { icon = "error", title = $"La longitud de la barra del producto {barra.ProductoId} debe ser mayor a 0." });

                    if (barra.Cantidad <= 0)
                        return Json(new { icon = "error", title = $"La cantidad de la barra del producto {barra.ProductoId} debe ser mayor a 0." });

                    if (string.IsNullOrWhiteSpace(barra.Unidad))
                        return Json(new { icon = "error", title = $"Debe indicar la unidad del producto {barra.ProductoId}." });
                }

                int sucursal = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                var utils = new Utilities(true);
                conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS"));

                conn.Open();
                trx = conn.BeginTransaction();

                var resultados = new List<object>();

                foreach (var barra in barras)
                {
                    var productosMovimiento = new List<Dictionary<string, object>>();
                    var parameters = new Dictionary<string, object>();
                    parameters.Add("udm", barra.Unidad);
                    parameters.Add("clave", barra.ProductoId);
                    parameters.Add("empresaId", empresaId);
                    parameters.Add("sucursal", sucursal);

                    string query = "SELECT id_udm FROM catunidades WHERE cve_udm = @udm";
                    int? unidad = GetInt(RunScalar(query, parameters, false, conn, trx));
                    if (!unidad.HasValue)
                        throw new InvalidOperationException("No se encontro una unidad registrada");

                    query = "SELECT id_catproductos FROM catproductos WHERE empresa_id = @empresaId AND cve_prod = @clave";
                    int? idProducto = GetInt(RunScalar(query, parameters, false, conn, trx));
                    if (!idProducto.HasValue)
                        throw new InvalidOperationException("Ocurrio un error al obtener el producto");

                    var productos = new Dictionary<string, object>();
                    productos.Add("id_producto", idProducto);
                    productos.Add("codigo", barra.ProductoId);
                    productos.Add("descripcion", barra.Comentario);
                    productos.Add("cantidad", barra.Longitud * barra.Cantidad);
                    productos.Add("unidad", unidad);

                    productosMovimiento.Add(productos);

                    query = "SELECT ct.id_tarima " +
                            "FROM catalmacenes c " +
                            "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                            "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                            "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                            "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                            "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                            "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Stock'";
                    int idTarima = Convert.ToInt32(RunScalar(query, parameters, false, conn, trx));

                    Dictionary<string, List<MovimientoDetalle>> movimiento = RegistrarMovimiento(productosMovimiento, GetUserId(User.Identity.Name), "ingreso", null, idTarima, "Ingreso extraordinario de barra", null, "Ingreso extraordinario de barra", conn, trx);

                    parameters = new Dictionary<string, object>();
                    parameters.Add("productoId", barra.ProductoId);
                    parameters.Add("empresaId", empresaId);
                    parameters.Add("sucursal", sucursal);

                    query = "SELECT tp.id_tarima_producto, tp.tarima_id " +
                        "FROM tarima_productos tp " +
                        "INNER JOIN catproductos cp ON cp.id_catproductos = tp.producto_id " +
                        "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                        "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                        "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                        "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                        "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                        "WHERE cp.cve_prod = @productoId AND cp.empresa_id = @empresaId AND cs.id_sucursal = @sucursal AND ca.tipo = 'Stock' " +
                        "FOR UPDATE;";

                    var filaTp = RunQuery(query, parameters, false, conn, trx);

                    if (filaTp.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"No se encontró inventario del producto {barra.ProductoId} en la sucursal indicada."
                        );
                    }

                    int idTarimaProducto = Convert.ToInt32(filaTp[0]["id_tarima_producto"]);

                    query = "SELECT fn_generar_folio_corte();";
                    string folio = RunScalar(query, new Dictionary<string, object>(), false, conn, trx).ToString();

                    parameters = new Dictionary<string, object>();
                    parameters.Add("folio", folio);
                    parameters.Add("idTp", movimiento[barra.ProductoId][0].IdTarimaProducto);
                    parameters.Add("longitud", barra.Longitud);
                    parameters.Add("cantidad", barra.Cantidad);
                    parameters.Add("unidad", barra.Unidad);
                    parameters.Add("usuario", User.Identity.Name);
                    parameters.Add("comentario", string.IsNullOrWhiteSpace(barra.Comentario) ? DBNull.Value : barra.Comentario);

                    query = "INSERT INTO tarima_productos_cortes (folio, tarima_producto_id, longitud, cantidad, cantidad_original, usuario_creacion, comentario) " +
                        "VALUES ( @folio, @idTp, @longitud, @cantidad, @cantidad, @usuario, @comentario);";

                    RunQuery(query, parameters, false, conn, trx);

                    resultados.Add(new
                    {
                        folio,
                        productoId = barra.ProductoId,
                        longitud = barra.Longitud,
                        cantidad = barra.Cantidad,
                        unidad = barra.Unidad
                    });
                }


                trx.Commit();

                return Json(new { icon = "success", barras = resultados, title = "Barras registradas correctamente." });
            }
            catch (Exception ex)
            {
                trx?.Rollback();

                return Json(new { icon = "error", title = "Ocurrió un error", html = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }
        #endregion

        #region Crear productos
        private bool ProductoExiste(string clave)
        {
            var parameters = new Dictionary<string, object>
            {
                { "clave", clave }
            };
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT COUNT(*) FROM catproductos WHERE cve_prod = @clave AND empresa_id = @empresa_id";
            object result = RunScalar(query, parameters);

            int qty = result == null ? 0 : Convert.ToInt32(result);

            return qty > 0;
        }

        public JsonResult CrearProducto(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                Response.StatusCode = 400;
                return Json(new { icon = "error", title = "Ocurrió un error", html = "El archivo no contiene información válida o no fue cargado correctamente.", crearProducto = false });
            }

            var productos = new List<Dictionary<string, object>>();
            string referencia = null;

            // Lectura del archivo .csv
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                string line;
                int linea = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    linea++;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var columnas = line.Split(',');

                    // Necesitas al menos 11 columnas
                    if (columnas.Length < 8)
                    {
                        Response.StatusCode = 400;
                        return Json(new { icon = "error", title = "Formato de archivo inválido", html = $"La línea {linea} no cumple con la estructura esperada del archivo CSV.", crearProducto = false });
                    }

                    string clave = columnas[0].Trim();
                    string descripcion = columnas[1].Trim();
                    decimal? cantidad = GetDecimal(columnas[2], 0);
                    string unidadClave = columnas[3].Trim();
                    decimal? precio = GetDecimal(columnas[4], 0);
                    decimal? descuento = GetDecimal(columnas[5], 0);
                    decimal? cantidadDescuento = GetDecimal(columnas[6], 0);
                    decimal? total = GetDecimal(columnas[7], 0);
                    string fraccionArancelaria = GetString(columnas[8]);
                    if (referencia == null && columnas.Length > 9)
                    {
                        referencia = columnas[9].Trim();
                    }

                    // 🔎 Validar existencia del producto ANTES de tocar la BD
                    if (!ProductoExiste(clave))
                    {
                        productos.Add(new Dictionary<string, object>
                        {
                            {"clave", clave},
                            {"descripcion", descripcion},
                            {"cantidad", cantidad},
                            {"unidad", unidadClave},
                            {"precio", precio},
                            {"descuento", cantidadDescuento},
                            {"importe", total},
                            {"f_arancelaria", fraccionArancelaria },
                            {"empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                        });
                        continue;
                    }
                }
            }

            string query = "INSERT INTO catproductos (cve_prod, descr_prod, fr_ar, udm, pv1, dto_prov, empresa_id) " +
                "VALUES (@clave, @descripcion, @f_arancelaria, @unidad, @precio, @descuento, @empresa_id)";
            RunUpdate(query, productos);

            return Json(new
            {
                icon = "success",
                title = "Productos registrados exitosamente",
                html = "Los productos fueron agregados correctamente a la base de datos. Si necesitas completar información adicional, puedes hacerlo desde la página de <a href='Almacen/Productos' target='_blank'> Agregar productos </a>. Después, vuelve a intentar el proceso de ingreso al almacén temporal."
            });

        }
        #endregion

        #region Crear Fraccion
        public bool FraccionExiste(string clave)
        {
            var parameters = new Dictionary<string, object>
            {
                { "clave", clave }
            };

            string query = "SELECT COUNT(*) FROM frac_arancelarias WHERE cve_prod = @clave";
            object result = RunScalar(query, parameters);

            int qty = result == null ? 0 : Convert.ToInt32(result);

            return qty > 0;
        }

        public JsonResult CrearFraccion(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                Response.StatusCode = 400;
                return Json(new { icon = "error", title = "Ocurrió un error", html = "El archivo no contiene información válida o no fue cargado correctamente.", crearProducto = false });
            }

            var productos = new List<Dictionary<string, object>>();
            var parameters = new Dictionary<string, object>();
            var productosNoEncontrados = new List<string>();
            var fraccionesNoEncontradas = new List<string>();
            string referencia = null;
            string query = "";

            // Lectura del archivo .csv
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                string line;
                int linea = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    linea++;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var columnas = line.Split(',');

                    // Necesitas al menos 11 columnas
                    if (columnas.Length < 8)
                    {
                        //Response.StatusCode = 400;
                        return Json(new { icon = "error", title = "Formato de archivo inválido", html = $"La línea {linea} no cumple con la estructura esperada del archivo CSV.", crearProducto = false });
                    }

                    string clave = columnas[0].Trim();
                    string descripcion = columnas[1].Trim();
                    decimal? cantidad = GetDecimal(columnas[2], 0);
                    string unidadClave = columnas[3].Trim();
                    decimal? precio = GetDecimal(columnas[4], 0);
                    decimal? descuento = GetDecimal(columnas[5], 0);
                    decimal? cantidadDescuento = GetDecimal(columnas[6], 0);
                    decimal? total = GetDecimal(columnas[7], 0);
                    string fraccionArancelaria = GetString(columnas[8]);
                    if (referencia == null && columnas.Length > 9)
                    {
                        referencia = columnas[9].Trim();
                    }

                    // 🔎 Validar existencia del producto ANTES de tocar la BD
                    if (!ProductoExiste(clave))
                    {
                        productosNoEncontrados.Add($"{clave} - {descripcion}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(fraccionArancelaria))
                    {
                        fraccionesNoEncontradas.Add(
                            $"{clave} - {descripcion}: el archivo no tiene fracción arancelaria. Agrégala al Excel y vuelve a intentar."
                        );
                        continue;
                    }

                    parameters = new Dictionary<string, object>();
                    parameters.Add("clave", clave);
                    parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                    //query = "SELECT cg.descripcion " +
                    //    "FROM catproductos cp " +
                    //    "JOIN catgrupo cg ON cg.id_grupo_producto = cp.gpo::integer " +
                    //    "WHERE cp.cve_prod = @clave " +
                    //    "   AND cp.empresa_id = @empresa_id " +
                    //    "LIMIT 1";
                    //var grupo = RunScalar(query, parameters);

                    query = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @clave AND empresa_id = @empresa_id ";
                    var id_prod = RunScalar(query, parameters);

                    parameters = new Dictionary<string, object>()
                        {
                            { "clave", clave},
                            { "descripcion", descripcion},
                            { "cantidad", cantidad},
                            { "unidad", unidadClave},
                            { "precio", precio},
                            { "descuento", cantidadDescuento},
                            { "importe", total},
                            { "f_arancelaria", fraccionArancelaria },
                            //{ "grupo", grupo },
                            { "id_producto", id_prod },
                        };

                    productos.Add(parameters);
                    continue;
                }
            }

            if (productosNoEncontrados.Any())
            {
                Response.StatusCode = 400;

                string html = "<ul style='text-align:left'>";
                foreach (var p in productosNoEncontrados)
                {
                    html += $"<li>{HttpUtility.HtmlEncode(p)}</li>";
                }
                html += "</ul>";

                return Json(new { title = "Productos no registrados", html, icon = "warning", crearProducto = true, productos = productosNoEncontrados });
            }

            if (fraccionesNoEncontradas.Any())
            {
                Response.StatusCode = 400;

                string html = "<ul style='text-align:left'>";
                foreach (var p in fraccionesNoEncontradas)
                {
                    html += $"<li>{HttpUtility.HtmlEncode(p)}</li>";
                }
                html += "</ul>";

                return Json(new { title = "Productos sin fraccion arancelaria, edite el documento .csv para agregar las fracciones arancelarias que faltan", html, icon = "warning", crearFraccion = true, productos = productosNoEncontrados });
            }

            query = "INSERT INTO frac_arancelarias (cve_prod, \"desc\", gpo_marca, frac, prod_id) " +
                "VALUES (@clave, @descripcion, '', @f_arancelaria, @id_producto) " +
                "ON CONFLICT (cve_prod) DO UPDATE SET frac = EXCLUDED.frac;";
            RunUpdate(query, productos);

            return Json(new
            {
                icon = "success",
                title = "Productos registrados exitosamente",
                html = "Los productos fueron agregados correctamente a la base de datos. Si necesitas completar información adicional, puedes hacerlo desde la página de <a href='Almacen/Productos' target='_blank'> Agregar productos </a>. Después, vuelve a intentar el proceso de ingreso al almacén temporal."
            });
        }
        #endregion

        #region Reportes
        // ══════════════════════════════════════════════════════════════
        //  GET: Reportes RMD pendientes para que inventario los procese
        // ══════════════════════════════════════════════════════════════
        public JsonResult GetReportesDevolucionPendientes()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = @"
        SELECT
            r.id,
            r.fecha_reporte,
            r.tipo_danio,
            r.descripcion,
            r.urgencia,
            r.referencia,
            r.ubicacion_material,
            r.estatus_actual,
            r.folios_txt,
            r.encabezado_ids_json,
            u.nombre AS nombre_vendedor,
            COUNT(pd.id) AS total_partidas,
            SUM(pd.cant_danada) AS total_unidades
        FROM rmd_reporte r
        LEFT JOIN usuarios u ON u.usuarioid = r.usuario_vendedor
        LEFT JOIN rmd_partida_danada pd ON pd.reporte_id = r.id
        WHERE r.sucursal = @sucursal
          AND r.estatus_actual = 'PENDIENTE'
        GROUP BY r.id, u.nombre
        ORDER BY
            CASE r.urgencia
                WHEN 'critica'  THEN 1
                WHEN 'alta'     THEN 2
                ELSE                 3
            END,
            r.fecha_reporte ASC";

            var reportes = RunQuery(query, parameters);
            return Json(reportes);
        }

        // ══════════════════════════════════════════════════════════════
        //  GET: Partidas de un reporte RMD específico
        // ══════════════════════════════════════════════════════════════
        public JsonResult GetPartidasReporte(int reporteId)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("reporte_id", reporteId);

            string query = @"
        SELECT
            pd.id,
            pd.encabezado_id,
            pd.producto_id   AS codigo,
            pd.descripcion,
            pd.cant_total_factura AS cantidad_factura,
            pd.cant_danada   AS cantidad,
            pd.precio_unitario,
            pd.ccy,
            e.folio,
            cu.id_udm        AS unidad,
            cu.cve_udm,
            cu.descripcion   AS desc_unidad,
            cp.id_catproductos
        FROM rmd_partida_danada pd
        LEFT JOIN encabezadomov e  ON e.id_encabezado = pd.encabezado_id
        LEFT JOIN catproductos cp  ON cp.cve_prod = pd.producto_id
                                   AND cp.empresa_id = @empresa_id
        LEFT JOIN catunidades cu   ON cu.cve_udm = cp.udm
        WHERE pd.reporte_id = @reporte_id
        ORDER BY pd.id";

            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var partidas = RunQuery(query, parameters);
            return Json(partidas);
        }
        #endregion
    }

    public class BarraRequest
    {
        public string ProductoId { get; set; }
        public decimal Longitud { get; set; }
        public decimal Cantidad { get; set; }
        public string Unidad { get; set; }
        public string Comentario { get; set; }
    }
}