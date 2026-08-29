using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;

namespace BOS_ERP.Controllers.ComprasInternacionales.Procesos
{
    public class ProcesoComprasInternacionalController : Utilities
    {
        private readonly BOS_ERP.Services.EmailSender _emailSender;
        private readonly CorreoHelper _correoHelper;

        public ProcesoComprasInternacionalController(BOS_ERP.Services.EmailSender emailSenderService, CorreoHelper correoHelperService)
        {
            _emailSender = emailSenderService;
            _correoHelper = correoHelperService;
        }

        #region Obtener datos generales
        public JsonResult GetRequisiciones()
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.folio AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "   (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = em.usr_doc) AS responsable, " +
                "   a.nombre AS nombre_area, " +
                "   (SELECT COUNT(*) FROM partidasdoc pd WHERE pd.encabezado_id = em.id_encabezado) AS total_partidas " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "   em.gen = 'CPI' AND " +
                "   em.nat = 'PI' AND " +
                "   em.estatus_id != 11 AND em.estatus_id = 13";

            parameters.Clear();
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }
        public JsonResult GetOrdenesDeCompra()
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "   a.nombre AS nombre_area, em.reclasificacion " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "   em.gen = 'CPI' AND " +
                "   em.nat = 'OCDI' AND " +
                "   em.estatus_id != 11 AND em.estatus_id = 7";
            parameters.Clear();
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }

        public JsonResult GetSolicitudesDeCotizacion()
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "   a.nombre AS nombre_area, em.reclasificacion " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "   em.gen = 'CPI' AND " +
                "   em.nat = 'SCINT' AND " +
                "   em.estatus_id != 11";
            parameters.Clear();
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }


        public JsonResult GetReclasificaciones()
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT   " +
                "    em.suc,  " +
                "    em.gen,  " +
                "    em.nat,  " +
                "    em.nro_gpo_doc,   " +
                "    em.nro_tp_doc,  " +
                "    em.fol_doc,  " +
                "    em.fch,  " +
                "    em.cli_prov,  " +
                "    em.iva,  " +
                "    (em.imp - COALESCE(SUM(p.imp_part), 0)) AS imp_pendiente,  " +
                "    em.coment_aut,   " +
                "    em.usr1,  " +
                "    em.id_encabezado,  " +
                "    em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo,  " +
                "    u.nombre || ' ' || u.apellido AS nombre_usuario,  " +
                "    (SELECT nombre || ' ' || apellido AS responsable  " +
                "        FROM usuarios  " +
                "        WHERE nombreusuario = usr_doc) AS responsable,  " +
                "    a.nombre AS nombre_area, imp,  " +
                "    em.reclasificacion  " +
                "FROM encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1  " +
                "INNER JOIN areas a ON a.areaid = u.areaid  " +
                "LEFT JOIN partidasdoc p ON p.encabezado_id = em.id_encabezado " +
                "WHERE em.gen = 'CPI'  " +
                "  AND em.nat = 'RCL' " +
                "GROUP BY  " +
                "    em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc,  " +
                "    em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut, em.usr1, em.id_encabezado,  " +
                "    u.nombre, u.apellido, a.nombre, em.reclasificacion;";
            parameters.Clear();
            var result = RunQuery(query, parameters);

            return Json(new { data = result });
        }

        public JsonResult GetRequisicionesData(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));
                parameters.Add("area", GetAreaName(User.Identity.Name));
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var returnResult = new Dictionary<string, object>();

                string query = "    SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                    "  em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado,  " +
                    "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo, " +
                    "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                    "   a.nombre AS nombre_area, em.encabezados_padre, ccy, em.refe, cp.cve_prov, cp.n_prov, cp.tel " +
                    "FROM " +
                    "   encabezadomov em " +
                    "INNER JOIN usuarios u ON u.usuarioid = em.usr0 " +
                    "INNER JOIN areas a ON a.areaid = u.areaid " +
                    "left join catproveedores cp on cp.id_prov = em.refe AND cp.id_empresa = @id_empresa " +
                    "WHERE " +
                    "   em.gen = 'CPI' AND " +
                    "   (em.nat = 'PI' OR em.nat = 'SCINT') AND em.id_encabezado = @id;";

                var requisicion = RunQuery(query, parameters)[0];
                returnResult.Add("requisicion", requisicion);

                parameters.Add("encabezados_padre", int.Parse(requisicion["encabezados_padre"].ToString()));
                query = "SELECT p.fol_doc, p.cve_prod, p.descr_prod, p.id_partidas, p.pv_prod, p.imp_part, " +
                    "   p.cant_ud, iva, f_pago_id, refe, cve_vdr_cpr, p.ud " +
                    "FROM partidasdoc p " +
                    "WHERE p.encabezado_id = @id";
                var partidas = RunQuery(query, parameters);
                returnResult.Add("partidas", partidas);

                query = "SELECT ac.nombre_original, ac.path, ac.extencion " +
                    "FROM archivos_compras_proformai ac WHERE ac.encabezado_id = @id";
                var archivos = RunQuery(query, parameters);
                returnResult.Add("archivos", archivos);

                return Json(new { success = true, returnResult });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al obtener remisiones: " + ex.Message });

            }
        }

        public JsonResult GetReclasificacionData(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));
                parameters.Add("area", GetAreaName(User.Identity.Name));
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var returnResult = new Dictionary<string, object>();

                string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                    "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                    "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS nuevo_codigo, " +
                    "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                    "   a.nombre AS nombre_area, encabezados_padre, cp.n_prov " +
                    "FROM " +
                    "   encabezadomov em " +
                    "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                    "INNER JOIN areas a ON a.areaid = u.areaid " +
                    "INNER JOIN catproveedores cp ON cp.id_prov = em.refe AND cp.id_empresa = @id_empresa " +
                    "WHERE " +
                    "   em.gen = 'CPI' AND " +
                    "   em.nat = 'RCL' AND em.id_encabezado = @id";

                var encabezado = RunQuery(query, parameters).FirstOrDefault();
                returnResult.Add("requisicion", encabezado);

                if (encabezado == null)
                {
                    return Json(new { success = false, message = "No se encontró el encabezado." });
                }

                // 2. Partidas reclasificadas
                query = @"
            SELECT p.fol_doc, p.cve_prod, p.descr_prod, p.id_partidas, 
                   p.pv_prod, p.imp_part, p.cant_ud, p.ud
            FROM partidasdoc p
            WHERE p.encabezado_id = @id";
                var partidas = RunQuery(query, parameters);
                returnResult.Add("partidas", partidas);

                // 3. Archivos de comprobantes reclasificados
                query = @"
            SELECT ac.nombre_original, ac.path, ac.extencion, ac.uuid, ac.partida_id
            FROM archivos_compras_reclasificacion ac 
            WHERE ac.encabezado_id = @id";
                var archivos = RunQuery(query, parameters);
                returnResult.Add("archivos", archivos);

                query = "select p2.pedimento_sat " +
                    "FROM partidasdoc p " +
                    "inner join pedimentos_partidas pp on pp.partida_id = p.id_partidas " +
                    "inner join pedimentos p2  on p2.id_pedimento  = pp.pedimento_id  " +
                    "WHERE p.encabezado_id = @padre " +
                    "Group BY p2.pedimento_sat";

                parameters.Add("padre", encabezado["encabezados_padre"]);
                var partidasOC = RunQuery(query, parameters);
                returnResult.Add("partidasOC", partidasOC);

                query = "SELECT id_pedimento_partida, pedimento_id, partida_id, cantidad " +
                    "FROM pedimentos_partidas where;";

                return Json(new { success = true, data = returnResult });
            }

            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al obtener remisiones: " + ex.Message });

            }
        }

        public JsonResult GetOrdenesCompra(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("uuid", fc["uuid"].ToString());
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var returnResult = new Dictionary<string, object>();

            // Obtener documento actual
            string query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.refe, em.id_encabezado, em.coment_aut AS observaciones, " +
                "   em.folio AS folio " +
                "FROM encabezadomov em " +
                "WHERE em.uuid = @uuid";
            var ordenCompra = RunQuery(query, parameters)[0];
            returnResult.Add("ordenCompra", ordenCompra);

            // Obtener partidas del documento actual
            query = "SELECT  " +
                    "    pd.id_partidas, " +
                    "    pd.cve_prod AS codigo, " +
                    "    pd.descr_prod AS descripcion, " +
                    "    pd.cve_vdr_cpr AS proveedor, " +
                    "    pd.refe AS proveedorid, " +
                    "    pd.ud AS unidad,  " +
                    "    pd.variacion, " +
                    "    c.id_catproductos, " +
                    "    pd.pv_prod AS idp, " +
                    "    ROUND(pd.cant_ud::numeric, 2) AS cantidad_total, " +
                    "    ROUND(COALESCE(SUM(pp.cantidad), 0)::numeric, 2) AS cantidad_asignada, " +
                    "    ROUND((pd.cant_ud - COALESCE(SUM(pp.cantidad), 0))::numeric, 2) AS cantidad_pendiente " +
                    "FROM partidasdoc pd " +
                    "INNER JOIN encabezadomov em  " +
                    "    ON em.id_encabezado = pd.encabezado_id " +
                    "LEFT JOIN catproductos c  " +
                    "    ON c.cve_prod = pd.cve_prod  " +
                    "    AND c.empresa_id = @empresa_id " +
                    "LEFT JOIN pedimentos_partidas pp   " +
                    "    ON pp.partida_id = pd.id_partidas " +
                    "WHERE em.uuid = @uuid " +
                    "GROUP BY  " +
                    "    pd.id_partidas, pd.cve_prod, pd.descr_prod, pd.cve_vdr_cpr,  " +
                    "    pd.refe, pd.ud, pd.variacion, c.id_catproductos, pd.pv_prod, pd.cant_ud " +
                    "HAVING (ROUND(pd.cant_ud::numeric, 2) - COALESCE(SUM(pp.cantidad), 0)) > 0;";

            var partidas = RunQuery(query, parameters);
            returnResult.Add("partidas", partidas);

            query = "SELECT id_udm, cve_udm, descripcion FROM catunidades";
            var unidades = RunQuery(query);
            returnResult.Add("unidades", unidades);

            return Json(returnResult);
        }

        public JsonResult GetPedimentos()
        {
            try
            {
                string query = @"
            SELECT id_pedimento, pedimento_aduanal, agente_id, fecha, aduana_id, patente_id, consecutivo_sat, pedimento_sat, en_uso
            FROM pedimentos p;
            WHERE p.activo = true
            ORDER BY p.fecha DESC, p.pedimento_aduanal";

                var pedimentos = RunQuery(query);

                return Json(new
                {
                    success = true,
                    pedimentos = pedimentos
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al obtener pedimentos: " + ex.Message
                });
            }
        }

        public JsonResult GetPedimentoDataSelect(IFormCollection fc)
        {
            var returnResult = new Dictionary<string, object>();
            string queryPatente = "SELECT id_patente_sat, cve_patente_sat, inicio_vigencia, fin_vigencia " +
                "FROM catpatentes_sat;";
            string queryAduana = "SELECT id_agencia_aduanal, cve_agencia, nombre, calle, colonia, poblacion, tel1, tel2, fax " +
                "FROM catagencias_aduanales;";
            string queryAduanaSAT = "SELECT id_aduana, cve_aduana, nombre FROM aduana_sat;";
            var patentes = RunQuery(queryPatente);
            var aduanas = RunQuery(queryAduana);
            var aduanasSAT = RunQuery(queryAduanaSAT);

            returnResult.Add("patentes", patentes);
            returnResult.Add("aduanas", aduanas);
            returnResult.Add("aduanasSAT", aduanasSAT);

            return Json(returnResult);
        }
        #endregion

        #region Acciones documentos
        public async Task<JsonResult> CrearODCI(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["id_encabezado"].ToString()))
                {
                    return Json(new { success = false, message = "Documento no encontrado." });
                }

                var parameters = new Dictionary<string, object>();
                parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                string queryPartidas = "SELECT cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, nro_part,  " +
                    "cve_prod, cant_ud, descr_prod, ud, pv_prod, imp_part, dto1, iva, ieps, f_pago_id,  " +
                    "gpo_doc_ant, tp_doc_ant, fol_doc_ant, part_doc_ant, saldo_ud_part, cve_cli, cto_vta_part,  " +
                    "cve_vdr_cpr, refe, cve_alm, fch, mt_cto_, exis_prev_u, exis_prev_peso, ccy, cto_ccy,  " +
                    "vta_ccy, ot, conc, encabezado_id, id_partidas " +
                    "FROM partidasdoc " +
                    "WHERE encabezado_id = @id_encabezado;";


                var partidas = RunQuery(queryPartidas, parameters);


                decimal total = 0;

                foreach (var row in partidas)
                {
                    if (row["imp_part"] != null && decimal.TryParse(row["imp_part"].ToString(), out decimal importe))
                    {
                        total += importe;
                    }
                }

                string query = "SELECT u.usuarioid, u.nombre || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   u2.usuarioid AS managerId, u2.nombre || u2.apellido AS nombreManager, " +
                    "   u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";
                parameters.Add("userName", User.Identity.Name);
                var userResult = RunQuery(query, parameters)[0];

                if (userResult == null || userResult.Count() == 0)
                {
                    return Json(new { success = false, message = "No se encontro un gerente para esta area." });
                }


                var usrParameter = new Dictionary<string, object>();
                string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, tipo_proceso, tipo_producto " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                usrParameter.Add("id", Convert.ToInt32(fc["id_encabezado"].ToString()));
                var usrId = RunQuery(usrquery, usrParameter)[0];
                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 3,
                    IdTpDoc = 13,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "OCDI",
                    ComentAut = fc["comentario"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(usrId["usr0"]),
                    Fch0 = (DateTime)usrId["fch0"],
                    Usr1 = Convert.ToInt32(GetUserId(User.Identity.Name)),
                    Fch1 = DateTime.Now,
                    Imp = total,
                    CliProv = "srs",
                    Estatus = 7,
                    EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString()),
                    TipoPoceso = "oc_internacional",
                };

                // 📌 Parsear productos como partidas
                var partidasODCI = new List<PartidaDocumento>();
                int nro = 1;
                foreach (var row in RunQuery(queryPartidas, parameters))
                {
                    partidasODCI.Add(new PartidaDocumento
                    {
                        NroPart = nro++,
                        CveProd = row["cve_prod"]?.ToString() ?? "",
                        Ref = row["refe"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["refe"]),
                        CveVdrCpr = row["cve_vdr_cpr"]?.ToString() ?? "",
                        DescrProd = row["descr_prod"]?.ToString() ?? "",
                        CantUd = row["cant_ud"] != null ? Convert.ToDecimal(row["cant_ud"]) : 0,
                        PvProd = row["pv_prod"] != null ? Convert.ToDecimal(row["pv_prod"]) : 0,
                        CtoVtaPart = row["cto_vta_part"] != null ? Convert.ToDecimal(row["cto_vta_part"]) : 0,
                        ImpPart = row["imp_part"] != null ? Convert.ToDecimal(row["imp_part"]) : 0,
                        Ud = row["ud"]?.ToString() ?? "PZA"
                    });
                }

                // 📌 Guardar documento
                var folio = GenerarDocumentoConPartidas(encabezado, partidasODCI);

                query = "UPDATE encabezadomov SET estatus_id = 11  WHERE id_encabezado = @id_encabezado";
                RunUpdate(query, parameters);


                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string urlCotizacion = $"{urlBase}/?id={folio["folio_generado"].ToString()}";

                var emailData = new EmailSenderModel
                {
                    Folio = folio["folio_generado"].ToString(),
                    Date = DateTime.Now,
                    SenderName = userResult["nombreusuario"].ToString(),
                    SenderEmail = userResult["emailusuario"].ToString(),
                    Total = total,
                    RecipientName = userResult["nombremanager"].ToString(),
                    RecipientEmail = userResult["emailmanager"].ToString(),
                    URL = urlCotizacion
                };

                string htmlBody = await _emailSender.RenderViewToStringAsync("~/Views/Email/_SendCreateRequisitionNotification.cshtml", emailData);

                //_correoHelper.EnviarCorreoNotificacionAsync(emailData.RecipientEmail, "Nueva Requisicion Creada", htmlBody);

                _ = SendNotificationInterno(userResult["aliasmanager"].ToString(), userResult["emailmanager"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva solicitud de cotizacion creada",
                    message = $"El usuario {userResult["nombreusuario"]} ha creado una nueva solicitud de cotizacion con el folio {folio["folio_generado"]}, la cual requiere su aprobación.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                });

                return Json(new { success = true, message = "solicitud de cotizacion creada exitosamente.", folio_generado = folio["folio_generado"].ToString() });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al procesar los datos: " + ex.Message
                });

            }

        }

        [HttpPost]
        public JsonResult Reclasificar(IFormCollection fc)
        {
            if (YaEstaReclasificado(Convert.ToInt32(fc["id_encabezado"].ToString())))
            {
                return Json(new { success = false, message = "Este documento ya ha sido reclasificado previamente." });
            }

            try
            {
                var parameters = new Dictionary<string, object>();

                //string description = fc["observaciones2"].ToString();
                decimal total = decimal.Parse(fc["precio"].ToString());

                string query = "SELECT u.usuarioid, u.nombre || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   u2.usuarioid AS managerId, u2.nombre || u2.apellido AS nombreManager, " +
                    "   u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";
                parameters.Add("userName", User.Identity.Name);
                var userResult = RunQuery(query, parameters)[0];

                if (userResult == null || userResult.Count() == 0)
                {
                    return Json(new { success = false, message = "No se encontro un gerente para esta area." });
                }

                var usrParameter = new Dictionary<string, object>();
                string usrquery = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, em.usr2, em.fch2, tipo_proceso, tipo_producto, refe " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                usrParameter.Add("id", Convert.ToInt32(fc["id_encabezado"].ToString()));
                var usrId = RunQuery(usrquery, usrParameter)[0];
                // 📌 Crear encabezado del documento
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 3,
                    IdTpDoc = 21,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "RCL",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = usrId["usr0"] != DBNull.Value ? Convert.ToInt32(usrId["usr0"]) : (int?)null,
                    Fch0 = usrId["fch0"] != DBNull.Value ? (DateTime?)usrId["fch0"] : null,
                    Usr1 = usrId["usr1"] != DBNull.Value ? Convert.ToInt32(usrId["usr1"]) : (int?)null,
                    Fch1 = usrId["fch1"] != DBNull.Value ? (DateTime?)usrId["fch1"] : null,
                    Usr2 = Convert.ToInt32(GetUserId(User.Identity.Name)),
                    Fch2 = DateTime.Now,
                    Imp = total,
                    CliProv = "srs",
                    Estatus = 1,
                    Ref = Convert.ToInt32(usrId["refe"]),
                    EncabezadoPadre = Convert.ToInt32(fc["id_encabezado"].ToString()),
                };

                // 📌 Parsear productos como partidas
                var partidas = new List<PartidaDocumento>();
                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                query = "update encabezadomov set reclasificacion = true  where id_encabezado = @id_encabezado";
                parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Documento de reclasificación creado.", folio_generado = folio["folio_generado"].ToString() });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }
        #endregion

        #region Acciones opciones
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult GuardarPedimento(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "pedimento_aduanal", fc["pedimento"].ToString() },
            { "agente_id",Convert.ToInt32(fc["agencia"].ToString()) },
            { "fecha", DateTime.ParseExact(fc["fecha"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture) },
            { "aduana_id",Convert.ToInt32(fc["aduana"].ToString()) },
            { "patente_id", Convert.ToInt32(fc["patenteSat"].ToString()) },
            { "consecutivo_sat", fc["consecutivoSat"].ToString() },
            { "pedimento_sat", fc["numeroPedimentoSat"].ToString() }
        };

                string query = @"INSERT INTO pedimentos 
                        (pedimento_aduanal, agente_id, fecha, aduana_id, patente_id, consecutivo_sat, pedimento_sat, en_uso) 
                        VALUES(@pedimento_aduanal, @agente_id, @fecha, @aduana_id, @patente_id, @consecutivo_sat, @pedimento_sat, true);";

                RunUpdate(query, parameters);

                return Json(new { success = true, message = "✅ Pedimento creado correctamente" });
            }
            catch (Exception ex)
            {
                // Devuelves el error exacto al frontend
                return Json(new { success = false, message = "❌ Error interno: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ActualizarPedimento(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrEmpty(fc["pedimentoId"].ToString()))
                {
                    return Json(new { success = false, message = "❌ No se proporcionó el ID del pedimento a editar" });
                }

                int idPedimento = Convert.ToInt32(fc["pedimentoId"].ToString());

                var parameters = new Dictionary<string, object>
        {
            { "id_pedimento", idPedimento },
            { "pedimento_aduanal", fc["pedimento"].ToString() },
            { "agente_id", Convert.ToInt32(fc["agencia"].ToString()) },
            { "fecha", DateTime.ParseExact(fc["fecha"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture) },
            { "aduana_id", Convert.ToInt32(fc["aduana"].ToString()) },
            { "patente_id", Convert.ToInt32(fc["patenteSat"].ToString()) },
            { "consecutivo_sat", fc["consecutivoSat"].ToString() },
            { "pedimento_sat", fc["numeroPedimentoSat"].ToString() }
        };

                string query = @"UPDATE pedimentos
                         SET pedimento_aduanal = @pedimento_aduanal,
                             agente_id = @agente_id,
                             fecha = @fecha,
                             aduana_id = @aduana_id,
                             patente_id = @patente_id,
                             consecutivo_sat = @consecutivo_sat,
                             pedimento_sat = @pedimento_sat
                         WHERE id_pedimento = @id_pedimento;";

                RunUpdate(query, parameters);

                return Json(new { success = true, message = "✏️ Pedimento actualizado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "❌ Error interno: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult GuardarComprobantes(IFormCollection fc)
        {
            try
            {
                string idEncabezado = fc["id_encabezado"].ToString();
                if (string.IsNullOrWhiteSpace(idEncabezado))
                {
                    return Json(new { success = false, message = "ID de encabezado es obligatorio." });
                }

                var comprobantes = new List<dynamic>();
                int index = 0;

                while (true)
                {
                    string keyTitulo = $"comprobantes[{index}]_titulo";
                    string keyGasto = $"comprobantes[{index}]_gasto";
                    string keyImporte = $"comprobantes[{index}]_importe";
                    string keyArchivoPdf = $"comprobantes[{index}]_archivo";
                    string keyArchivoXml = $"comprobantes[{index}]_xml";

                    if (string.IsNullOrEmpty(fc[keyTitulo]) && string.IsNullOrEmpty(fc[keyImporte])
                        && Request.Form.Files[keyArchivoPdf] == null && Request.Form.Files[keyArchivoXml] == null)
                        break; // No hay más comprobantes

                    string titulo = fc[keyTitulo];
                    string gasto = fc[keyGasto];
                    string importeStr = fc[keyImporte];

                    IFormFile archivoPdf = Request.Form.Files[keyArchivoPdf];
                    IFormFile archivoXml = Request.Form.Files[keyArchivoXml]; // opcional

                    if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(importeStr))
                    {
                        return Json(new { success = false, message = $"Comprobante #{index + 1} tiene campos vacíos." });
                    }

                    if (!decimal.TryParse(importeStr, out decimal importe) || importe <= 0)
                    {
                        return Json(new { success = false, message = $"Importe inválido en el comprobante #{index + 1}." });
                    }

                    if (archivoPdf == null || archivoPdf.Length == 0)
                    {
                        return Json(new { success = false, message = $"Archivo PDF faltante en el comprobante #{index + 1}." });
                    }

                    // Leer XML solo si existe
                    DatosXML datosXml = null;
                    if (archivoXml != null && archivoXml.Length > 0)
                    {
                        try
                        {
                            archivoXml.OpenReadStream().Position = 0;
                            datosXml = LeerDatosDesdeXML(archivoXml.OpenReadStream());

                            // Puedes acceder a sus propiedades si lo necesitas
                            string version = datosXml.Version;
                            string serie = datosXml.Serie;
                            string folio = datosXml.Folio;
                            string fecha = datosXml.Fecha;
                            string subtotal = datosXml.SubTotal;
                            string total = datosXml.Total;
                            string moneda = datosXml.Moneda;
                            string tipoCambio = datosXml.TipoCambio;
                            string emisorRfc = datosXml.EmisorRFC;
                            string emisorNombre = datosXml.EmisorNombre;
                            string receptorRfc = datosXml.ReceptorRFC;
                            string receptorNombre = datosXml.ReceptorNombre;
                        }
                        catch (Exception ex)
                        {
                            return Json(new { success = false, message = $"Error al leer el XML del comprobante #{index + 1}: {ex.Message}" });
                        }
                    }

                    comprobantes.Add(new
                    {
                        Titulo = titulo,
                        Importe = importe,
                        Gasto = gasto,
                        ArchivoPDF = archivoPdf,
                        ArchivoXML = archivoXml, // puede ir null
                        DatosXML = datosXml      // puede ir null
                    });

                    index++;
                }

                var parametersEnc = new Dictionary<string, object>();
                string query = "select suc as cve_suc, gen, nat, nro_gpo_doc as nro_gpo_mov, nro_tp_doc as nro_tp_mov, fol_doc from encabezadomov where id_encabezado = @id_encabezado";
                parametersEnc.Add("id_encabezado", Convert.ToInt32(idEncabezado));
                var encResult = RunQuery(query, parametersEnc)[0];

                if (comprobantes.Count == 0)
                {
                    return Json(new { success = false, message = "Debe agregar al menos un comprobante." });
                }

                // Guardar archivos en carpeta
                foreach (var c in comprobantes)
                {
                    var parameters = new Dictionary<string, object>();
                    var parametersArchivos = new Dictionary<string, object>();

                    query = "INSERT INTO partidasdoc " +
                                    "   (cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, cant_ud, pv_prod, imp_part, cve_prod, descr_prod, encabezado_id) " +
                                    "VALUES(@cve_suc, @gen, @nat, @nro_gpo_mov, @nro_tp_mov, @fol_doc, @cant_ud, @pv_prod, @imp_part, @cve_prod, @descr_prod, @encabezado_id) " +
                                    "RETURNING id_partidas;";

                    parameters.Add("cve_suc", encResult["cve_suc"]);
                    parameters.Add("gen", encResult["gen"]);
                    parameters.Add("nat", encResult["nat"]);
                    parameters.Add("nro_gpo_mov", encResult["nro_gpo_mov"]);
                    parameters.Add("nro_tp_mov", encResult["nro_tp_mov"]);
                    parameters.Add("fol_doc", encResult["fol_doc"]);
                    parameters.Add("cant_ud", 1);
                    parameters.Add("pv_prod", Convert.ToDecimal(c.Importe));
                    parameters.Add("imp_part", Convert.ToDecimal(c.Importe));
                    parameters.Add("cve_prod", c.Gasto);
                    parameters.Add("descr_prod", c.Titulo);
                    parameters.Add("encabezado_id", Convert.ToInt32(idEncabezado));

                    var idPartida = RunScalar(query, parameters);

                    string nombreOriginal = c.ArchivoPDF.FileName;
                    string ruta = "content/archivos_compras_reclasificacion/";
                    string uuid = Guid.NewGuid().ToString();
                    string extencion = Path.GetExtension(nombreOriginal);

                    var result = UploadFormFileToPath(ruta, c.ArchivoPDF, uuid, extencion);
                    query = "INSERT INTO archivos_compras_reclasificacion " +
                            "(nombre_original, path, uuid, extencion, encabezado_id, partida_id) " +
                            "VALUES(@nombre_original, @ruta, @uuid, @extencion, @encabezado_id, @partida_id); ";
                    parametersArchivos.Add("nombre_original", nombreOriginal);
                    parametersArchivos.Add("ruta", ruta + uuid + extencion);
                    parametersArchivos.Add("uuid", uuid);
                    parametersArchivos.Add("extencion", extencion);
                    parametersArchivos.Add("encabezado_id", Convert.ToInt32(idEncabezado));
                    parametersArchivos.Add("partida_id", Convert.ToInt32(Convert.ToInt32(idPartida)));

                    RunUpdate(query, parametersArchivos);

                    // Guardar XML solo si existe
                    if (c.ArchivoXML != null && c.ArchivoXML.ContentLength > 0)
                    {
                        string nombreXml = c.ArchivoXML.FileName;
                        string uuidXml = Guid.NewGuid().ToString();
                        string extXml = Path.GetExtension(nombreXml);
                        string rutaXml = "content/archivos_compras_reclasificacion/";

                        var resultXml = UploadFormFileToPath(rutaXml, c.ArchivoXML, uuidXml, extXml);

                        string queryXml = "INSERT INTO archivos_compras_reclasificacion " +
                                          "(nombre_original, path, uuid, extencion, encabezado_id, partida_id) " +
                                          "VALUES(@nombre_original, @ruta, @uuid, @extencion, @encabezado_id, @partida_id); ";

                        var parametersXml = new Dictionary<string, object>
                {
                    { "nombre_original", nombreXml },
                    { "ruta", rutaXml + uuidXml + extXml },
                    { "uuid", uuidXml },
                    { "extencion", extXml },
                    { "encabezado_id", Convert.ToInt32(idEncabezado) },
                    { "partida_id", Convert.ToInt32(idPartida) }
                };

                        RunUpdate(queryXml, parametersXml);
                    }
                }

                return Json(new { success = true, message = "Comprobantes guardados correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error interno: " + ex.Message });
            }
        }


        [HttpPost]
        [AuditAction(Modulo = "Compras Internacionales", Accion = "Materiales agregados a la requisicion")]
        public JsonResult GuardarMaterial(IFormCollection fc)
        {
            try
            {
                var _parameters = new List<Dictionary<string, object>>();
                int documentoId = Convert.ToInt32(fc["id_encabezado"].ToString());
                string productosJson = fc["materiales"].ToString();
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);

                string deleteQuery = "DELETE FROM partidasdoc WHERE encabezado_id = @id_encabezado";
                var deleteParams = new Dictionary<string, object>
                {
                    { "id_encabezado", documentoId }
                };
                RunUpdate(deleteQuery, deleteParams);
                foreach (var item in productos)
                {
                    var parametersPartidas = new Dictionary<string, object>();
                    //var queryProveedor = "select id_prov, n_prov from catproveedores where cve_prov = @cve_prov ";
                    //parametersPartidas.Add("cve_prov", item["proveedor"]);
                    //var provResult = RunQuery(queryProveedor, parametersPartidas);

                    var queryProducto = "SELECT descr_prod from catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id";
                    parametersPartidas.Add("cve_prod", item["codigo"]);
                    parametersPartidas.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    var descProd = RunScalar(queryProducto, parametersPartidas);

                    var queryEncabezado = "SELECT fol_doc FROM encabezadomov WHERE id_encabezado = @id_encabezado";
                    parametersPartidas.Add("id_encabezado", documentoId);
                    var folioDoc = RunScalar(queryEncabezado, parametersPartidas);

                    var queryIdArea = "select areaid from areas where nombre = nombre";
                    parametersPartidas.Add("nombre", GetAreaName(User.Identity.Name));
                    var idarea = RunScalar(queryIdArea, parametersPartidas);

                    if (descProd == null || string.IsNullOrEmpty(descProd.ToString()))
                    {
                        return Json(new { success = false, message = "Error al guardar materiales, El producto no existe " });
                    }


                    var parameters = new Dictionary<string, object>
                    {
                        { "cve_suc", "01" },
                        { "gen", "CPI" },
                        { "nat", "PI" },
                        { "nro_gpo_mov", Convert.ToInt32(idarea)},
                        { "nro_tp_mov", 20 },
                        { "fol_doc", folioDoc.ToString() }, // Puedes cambiar este valor si aplica
                        { "cve_prod", item["codigo"] },
                        { "cant_ud", Convert.ToDecimal(item["cantidad"]) },
                        { "descr_prod", descProd.ToString() }, // Puedes obtener la descripción si está disponible
                        { "ud", item["unidad"] },
                        { "pv_prod", Convert.ToDecimal(item["costoUnitario"]) },
                        { "imp_part", Convert.ToDecimal(item["total"]) },
                        { "encabezado_id", documentoId }
                    };
                    _parameters.Add(parameters);
                }
                string query = "INSERT INTO partidasdoc " +
                        "(cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, cve_prod, cant_ud, descr_prod, ud, pv_prod, imp_part, encabezado_id) " +
                        "VALUES(@cve_suc, @gen, @nat, @nro_gpo_mov, @nro_tp_mov, @fol_doc, @cve_prod, @cant_ud, @descr_prod, @ud, @pv_prod, @imp_part, @encabezado_id);";
                RunUpdate(query, _parameters);


                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al guardar materiales: " + ex.Message });
            }
        }

        public JsonResult Movimientos(int id)
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>
            {
                {"id_encabezado",id }
            };
            string query = "SELECT id_movimiento, tipo_movimiento FROM movimientos_ocdi";
            result.Add("movimientos", RunQuery(query));

            query = "select fch_pg_entrega from encabezadomov where id_encabezado = @id_encabezado";
            result.Add("fecha", RunQuery(query, parameters));
            return Json(new { result, success = true });
        }
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Internacionales", Accion = "Movimiento en la orden de compra internacional")]
        public JsonResult AgregarMovimientos(IFormCollection fc)
        {
            try
            {
                var documentoId = Convert.ToInt32(fc["id_encabezado"].ToString());
                var userId = Convert.ToInt32(GetUserId(User.Identity.Name));

                // 🔹 Movimiento (opcional)
                if (!string.IsNullOrEmpty(fc["opcion"].ToString()))
                {
                    var movimientoId = Convert.ToInt32(fc["opcion"].ToString());
                    var parametersInsert = new Dictionary<string, object>
            {
                { "documento_id", documentoId },
                { "movimiento_id", movimientoId },
                { "user_id", userId }
            };

                    string queryInsert = @"
                INSERT INTO h_doc_mov 
                    (documento_id, movimiento_id, fecha_registro, user_id) 
                VALUES(@documento_id, @movimiento_id, CURRENT_TIMESTAMP, @user_id);";

                    RunQuery(queryInsert, parametersInsert);
                }

                // 🔹 Fecha estimada de entrega (opcional)
                if (!string.IsNullOrEmpty(fc["fecha_estimada"].ToString()))
                {
                    DateTime fechaEntrega = DateTime.ParseExact(fc["fecha_estimada"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

                    var parametersUpdate = new Dictionary<string, object>
            {
                { "documento_id", documentoId },
                { "fecha_estimada", fechaEntrega }
            };

                    string queryUpdate = @"
                UPDATE encabezadomov
                SET fch_pg_entrega = @fecha_estimada
                WHERE id_encabezado = @documento_id;";

                    RunQuery(queryUpdate, parametersUpdate);
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al procesar la operación: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult AprobarODCI(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id_encabezado", Convert.ToInt32(fc["id_encabezado"].ToString()));
                parameters.Add("firma", fc["firma"].ToString());

                string queryDetalles =
                                    "SELECT  " +
                                    "    pd.id_partidas, em.fch_pg_entrega, em.reclasificacion," +
                                    "    pd.cve_prod AS codigo, " +
                                    "    pd.descr_prod AS descripcion, " +
                                    "    ROUND(pd.cant_ud::numeric, 2) AS cantidad_total, " +
                                    "    ROUND(COALESCE(SUM(pp.cantidad), 0)::numeric, 2) AS cantidad_asignada, " +
                                    "    ROUND((pd.cant_ud - COALESCE(SUM(pp.cantidad), 0))::numeric, 2) AS cantidad_pendiente " +
                                    "FROM partidasdoc pd " +
                                    "INNER JOIN encabezadomov em  " +
                                    "    ON em.id_encabezado = pd.encabezado_id " +
                                    "LEFT JOIN pedimentos_partidas pp   " +
                                    "    ON pp.partida_id = pd.id_partidas " +
                                    "WHERE em.id_encabezado = @id_encabezado " +
                                    "GROUP BY pd.id_partidas, pd.cve_prod, pd.descr_prod, pd.cant_ud, em.fch_pg_entrega, em.reclasificacion " +
                                    "HAVING (ROUND(pd.cant_ud::numeric, 2) - COALESCE(SUM(pp.cantidad), 0)) > 0;";

                var partidasPendientes = RunQuery(queryDetalles, parameters);

                if (partidasPendientes.Any(p => p["fch_pg_entrega"] == null))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Todas las partidas deben tener fecha de entrega para aprobar la ODCI."
                    });
                }

                if (partidasPendientes.Any(p => p["reclasificacion"] == null || !(bool)p["reclasificacion"]))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Debes marcar la reclasificación en todas las partidas para aprobar la ODCI."
                    });
                }


                if (partidasPendientes != null && partidasPendientes.Count > 0)
                {
                    // Asegurarse de que el diccionario contiene la llave y que no sea null
                    var nombres = string.Join(", ",
                        partidasPendientes
                            .Where(p => p.ContainsKey("descripcion") && p["descripcion"] != null)
                            .Select(p => p["descripcion"].ToString())
                    );

                    return Json(new
                    {
                        success = false,
                        message = "Hay partidas sin pedimento: " + nombres
                    });
                }

                parameters.Add("centro", Convert.ToInt32(fc["centro"].ToString()));
                string query = "UPDATE encabezadomov SET estatus_id = 26, centro_costos = @centro, " +
                    "   firma2 = @firma, fch2 = CURRENT_TIMESTAMP  " +
                    "WHERE id_encabezado = @id_encabezado";
                RunUpdate(query, parameters);

                //List<PolizaData> poliza = GenerarDatosPoliza(Convert.ToInt32(fc["id_encabezado"].ToString()));
                //RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(fc["id_encabezado"].ToString()), poliza);

                return Json(new { success = true, message = "Aprobación de ODCI" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al aprobar ODCI: " + ex.Message });
            }
        }


        public bool YaEstaReclasificado(int id_encabezado)
        {
            var parameters = new Dictionary<string, object>
            {
                { "id_encabezado", id_encabezado }
            };
            string query = "select reclasificacion from encabezadomov where id_encabezado = @id_encabezado";
            var result = RunScalar(query, parameters);

            if (result == null || result == DBNull.Value)
                return false;

            return Convert.ToBoolean(result);
        }

        public JsonResult ActualizarPartida(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "UPDATE partidasdoc SET cant_ud= @cantidad, pv_prod=@precio, imp_part = @cantidad * @precio WHERE id_partidas=@partida_id;";
                parameters.Add("partida_id", Convert.ToInt32(fc["partida_id"].ToString()));
                parameters.Add("precio", Convert.ToDecimal(fc["precio"].ToString()));
                parameters.Add("cantidad", Convert.ToDecimal(fc["cantidad"].ToString()));
                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Documento de reclasificación creado." });
            } //, folio_generado = folio["folio_generado"].ToString()
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }

        }

        public JsonResult GuardarDivisa(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "update encabezadomov set ccy = @ccy where id_encabezado = @id_encabezado";
                parameters.Add("ccy", fc["ccy"].ToString());
                parameters.Add("id_encabezado", Convert.ToInt32(fc["requisicion_id"].ToString()));
                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Guardado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }
        public JsonResult GuardarProveedor(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string query = "update encabezadomov set refe = @refe where id_encabezado = @id_encabezado";
                parameters.Add("refe", Convert.ToInt32(fc["proveedor_id"].ToString()));
                parameters.Add("id_encabezado", Convert.ToInt32(fc["requisicion_id"].ToString()));
                RunUpdate(query, parameters);
                return Json(new { success = true, message = "Guardado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        public JsonResult VerificarPrecioReclasificacion(int id_encabezado)
        {
            try
            {
                string query = "SELECT " +
                    "e.id_encabezado, " +
                    "e.imp AS total_encabezado, " +
                    "COALESCE(SUM(p.imp_part), 0) AS total_pagado, " +
                    "(e.imp - COALESCE(SUM(p.imp_part), 0)) AS maximo_por_pagar, " +
                    "CASE  " +
                    "WHEN e.imp > 0 THEN ROUND((COALESCE(SUM(p.imp_part), 0) / e.imp) * 100, 2) " +
                    "ELSE 0 " +
                    "END AS porcentaje_pagado " +
                    "FROM encabezadomov e " +
                    "LEFT JOIN partidasdoc p  " +
                    "ON p.encabezado_id = e.id_encabezado " +
                    "WHERE e.id_encabezado = @id_encabezado " +
                    "GROUP BY e.id_encabezado, e.imp;";
                var parameters = new Dictionary<string, object>
                {
                    { "id_encabezado", id_encabezado }
                };

                var result = RunQuery(query, parameters);

                return Json(new { success = true, message = "Guardado correctamente.", data = result[0] });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }
        public JsonResult ProcesarXML()
        {
            try
            {

                var comprobantes = new List<dynamic>();

                int index = 0;

                string keyArchivoXml = $"comprobantes[{index}]_xml";

                IFormFile archivoXml = Request.Form.Files["xmlFile"]; // puede ser null

                // Leer XML solo si existe
                DatosXML datosXml = null;
                if (archivoXml != null && archivoXml.Length > 0)
                {
                    try
                    {
                        datosXml = LeerDatosDesdeXML(archivoXml.OpenReadStream());
                    }
                    catch (Exception ex)
                    {
                        return Json(new { success = false, message = $"Error al leer el XML del comprobante #{index + 1}: {ex.Message}" });
                    }
                }
                // Asegúrate de que el stream esté al inicio
                archivoXml.OpenReadStream().Position = 0;

                // Llamar al método
                DatosXML datos = LeerDatosDesdeXML(archivoXml.OpenReadStream());

                // Acceder a sus propiedades
                string version = datos.Version;
                string serie = datos.Serie;
                string folio = datos.Folio;
                string fecha = datos.Fecha;
                string subtotal = datos.SubTotal;
                string total = datos.Total;
                string moneda = datos.Moneda;
                string tipoCambio = datos.TipoCambio;
                string emisorRfc = datos.EmisorRFC;
                string emisorNombre = datos.EmisorNombre;
                string receptorRfc = datos.ReceptorRFC;
                string receptorNombre = datos.ReceptorNombre;

                index++;

                return Json(new { success = true, message = "Guardado correctamente.", data = datos });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult AsignarPedimento(IFormCollection fc)
        {
            try
            {
                string pedimentoId = fc["pedimentoId"].ToString();
                string assignmentsJson = fc["assignments"].ToString();

                if (string.IsNullOrEmpty(pedimentoId))
                {
                    return Json(new { success = false, message = "ID de pedimento requerido" });
                }

                if (string.IsNullOrEmpty(assignmentsJson))
                {
                    return Json(new { success = false, message = "No se encontraron asignaciones" });
                }

                // Deserializar las asignaciones
                var assignments = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ProductAssignment>>(assignmentsJson);

                if (assignments == null || assignments.Count == 0)
                {
                    return Json(new { success = false, message = "No hay productos para asignar" });
                }

                // Validar que el pedimento existe
                var parameters = new Dictionary<string, object>();
                parameters.Add("pedimento_id", Convert.ToInt32(pedimentoId));

                string validateQuery = "SELECT COUNT(*) as count FROM pedimentos WHERE id_pedimento = @pedimento_id AND en_uso = true";
                var validateResult = RunQuery(validateQuery, parameters);

                if (Convert.ToInt32(validateResult[0]["count"]) == 0)
                {
                    return Json(new { success = false, message = "Pedimento no encontrado o inactivo" });
                }

                int totalAssigned = 0;

                // Procesar cada asignación
                foreach (var assignment in assignments)
                {
                    // Validar cantidad disponible
                    parameters.Clear();
                    parameters.Add("partida_id", assignment.ProductId);

                    string checkQuery = @"
                SELECT 
                    pd.cant_ud AS cantidad_total,
                    COALESCE(SUM(pp.cantidad), 0) AS cantidad_asignada,
                    (pd.cant_ud - COALESCE(SUM(pp.cantidad), 0)) AS cantidad_disponible
                FROM partidasdoc pd
                LEFT JOIN pedimentos_partidas pp ON pp.partida_id = pd.id_partidas
                WHERE pd.id_partidas = @partida_id
                GROUP BY pd.id_partidas, pd.cant_ud";

                    var checkResult = RunQuery(checkQuery, parameters);

                    if (checkResult.Count == 0)
                    {
                        return Json(new { success = false, message = $"Producto {assignment.ProductId} no encontrado" });
                    }

                    decimal cantidadDisponible = Convert.ToDecimal(checkResult[0]["cantidad_disponible"]);

                    if (assignment.Quantity > cantidadDisponible)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Cantidad solicitada ({assignment.Quantity}) excede la disponible ({cantidadDisponible}) para el producto {assignment.ProductId}"
                        });
                    }

                    // Insertar la asignación
                    parameters.Clear();
                    parameters.Add("pedimento_id", Convert.ToInt32(pedimentoId));
                    parameters.Add("partida_id", assignment.ProductId);
                    parameters.Add("cantidad", assignment.Quantity);
                    //parameters.Add("fecha_asignacion", DateTime.Now);
                    //parameters.Add("usuario_asignacion", Session["Usuario"]?.ToString() ?? "Sistema");

                    string insertQuery = @"
                INSERT INTO pedimentos_partidas 
                (pedimento_id, partida_id, cantidad)
                VALUES (@pedimento_id, @partida_id, @cantidad)";

                    RunQuery(insertQuery, parameters);
                    totalAssigned++;
                }

                return Json(new
                {
                    success = true,
                    message = $"Se asignaron {totalAssigned} productos al pedimento correctamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al asignar pedimento: " + ex.Message
                });
            }
        }

        public class ProductAssignment
        {
            public int ProductId { get; set; }
            public decimal Quantity { get; set; }
        }

        #endregion

    }
}