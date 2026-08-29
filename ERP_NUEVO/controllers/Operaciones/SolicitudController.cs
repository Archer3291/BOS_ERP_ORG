using BOS_ERP.Helpers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Data;
using BOS_ERP.Services;

namespace BOS_ERP.Controllers.Operaciones
{
    public class SolicitudCotizacionController : Utilities
    {
        private readonly EmailSender _emailSender;
        private readonly CorreoHelper _correoHelper;

        public SolicitudCotizacionController(EmailSender emailSenderService, CorreoHelper correoHelperService)
        {
            _emailSender = emailSenderService;
            _correoHelper = correoHelperService;
        }

        [HttpPost]
        public JsonResult GuardarSolicitudCotizacion(IFormCollection form)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string cliente = form["cliente"];
                string vendedor = form["vendedor"];
                string fechaStr = form["fecha"];
                string hora = form["hora"];
                string nota = form["nota"];
                string opcion1 = form["opcion1"];
                string opcion2 = form["opcion2"];
                string opcion3 = form["opcion3"];
                string subtotalStr = form["subtotal"];
                string ivaStr = form["iva"];
                string totalStr = form["total"];
                string usuario = "admin"; // Puedes obtenerlo de la sesión

                DateTime fecha = DateTime.Parse(fechaStr);
                decimal subtotal = decimal.Parse(subtotalStr);
                decimal iva = decimal.Parse(ivaStr);
                decimal total = decimal.Parse(totalStr);
                string usr1 = User.Identity.Name;

                string idUserQuery = "SELECT UsuarioId, AreaId, Email FROM Usuarios WHERE NombreUsuario = @nombre_usuario";
                parameters.Add("nombre_usuario", usr1);
                var userResult = RunQuery(idUserQuery, parameters);

                string getAreaManagerQuery = "SELECT usuarioid, email FROM usuarios WHERE areaid = @AreaId AND Autorizador = @Autorizador";
                parameters.Clear();
                parameters.Add("AreaId", int.Parse(userResult[0]["areaid"].ToString()));
                parameters.Add("Autorizador", true);
                var areaManagerResult = RunQuery(getAreaManagerQuery, parameters);

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 2,
                    IdTpDoc = 3, // Cotización
                    Anio = fecha.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = fecha,
                    CliProv = cliente,
                    TpMov = "COT",
                    ComentAut = nota,
                    Coment1 = opcion1,
                    Coment2 = opcion2,
                    Coment3 = opcion3,
                    UsrDoc = usuario,
                    FchCap = DateTime.Now,
                    Usr1 = Convert.ToInt32(userResult[0]["usuarioid"]),
                    //Vendedor = vendedor,
                    //Hora = hora,
                    //Subtotal = subtotal,
                    Imp = total
                };

                string productosJson = form["productos"];
                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(productosJson);

                var partidas = new List<PartidaDocumento>();
                int nro = 1;
                foreach (var p in productos)
                {
                    partidas.Add(new PartidaDocumento
                    {
                        NroPart = nro++,
                        CveProd = p.ContainsKey("codigo") ? p["codigo"] : "",
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0,
                        PvProd = p.ContainsKey("precioUnitario") ? decimal.Parse(p["precioUnitario"]) : 0,
                        ImpPart = (p.ContainsKey("cantidad") ? decimal.Parse(p["cantidad"]) : 0) * (p.ContainsKey("precioUnitario") ? decimal.Parse(p["precioUnitario"]) : 0)
                        //Total = p.ContainsKey("total") ? decimal.Parse(p["total"].Replace("$", "").Trim()) : 0,
                        //RutaImagen = rutaImagen
                    });
                }

                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                foreach (var archivo in Request.Form.Files)
                {
                    if (archivo != null && archivo.Length > 0)
                    {
                        if (archivo.Length > 5 * 1024 * 1024) // Máximo 5MB
                            return Json(new { success = false, error = "Archivo demasiado grande (máximo 5MB)." });

                        string extension = Path.GetExtension(archivo.FileName).ToLower();
                        ;

                        string nombreOriginal = Path.GetFileName(archivo.FileName);
                        Guid uid = Guid.NewGuid();
                        string nuevoNombre = uid.ToString() + extension;

                        string rutaCarpeta = Path.Combine("~/Adjuntos/");
                        if (!Directory.Exists(rutaCarpeta))
                            Directory.CreateDirectory(rutaCarpeta);

                        string rutaCompleta = Path.Combine(rutaCarpeta, nuevoNombre);
                        using var stream = new FileStream(rutaCompleta, FileMode.Create);
                        archivo.CopyToAsync(stream);

                        string adjuntosQuery = @"INSERT INTO documentos_adj 
                    (n_arch_orig, n_arch_uid, ext, rt_arch, FechaCarga, folio_documento) 
                    VALUES 
                    (@n_arch_orig, @n_arch_uid, @ext, @rt_arch, @FechaCarga, @folio_documento)";

                        parameters.Clear();
                        parameters.Add("n_arch_orig", nombreOriginal);
                        parameters.Add("n_arch_uid", uid);
                        parameters.Add("ext", extension);
                        parameters.Add("rt_arch", "/Adjuntos/" + nuevoNombre);
                        parameters.Add("FechaCarga", DateTime.Now);
                        parameters.Add("folio_documento", folio["folio_generado"].ToString());

                        RunQuery(adjuntosQuery, parameters);

                        string fileName = Path.GetFileName(archivo.FileName);
                        string savePath = Path.Combine("~/content/Solicitudes/" + fileName);

                        using var stream2 = new FileStream(rutaCompleta, FileMode.Create);
                        archivo.CopyToAsync(stream2);
                    }
                }

                SendEmail(folio["folio_generado"].ToString(), folio["folio_generado"].ToString(), DateTime.Now.ToString(), User.Identity.Name, userResult[0]["email"].ToString(), total.ToString(), nota, areaManagerResult[0]["email"].ToString());
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar los datos: " + ex.Message });

            }
        }


        [HttpGet]
        [Authorize]
        public JsonResult SolicitudPorUsuario()
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string solicitudUser = "SELECT suc, gen, nat, nro_gpo_doc, nro_tp_doc, fol_doc, ccy, alm, fch, " +
                    "   cli_prov, refe, vdr_cpr, dto, iva, ieps_isr, imp, pl_dias, fch_pg_entrega, " +
                    "   dto1, dto2, dto3, rfc, iva_ret, coment1, coment2, coment3, ped_adu, nro_coment_x_part, " +
                    "   nro_car_coment_x_part, tp_mov, n_cli, cl_cli, col_cli, pob_cli, nat_doc_anex, " +
                    "   gpo_doc_anex, tp_doc_anex, fol_doc_anex, par, fch_ref, saldo_doc, stat, cve_proy, " +
                    "   cva_bco, cve_cli, dest_ch, n_pers, mto_antic, com_vdr, mt_extra1, mt_extra2, mt_extra3, " +
                    "   mt_extra4, mt_extra5, mt_extra6, mt_extra7, mt_extra8, mt_extra9, mt_extra10, cve_dpto, " +
                    "   hr_pg_entrega, bas_fol, usr_doc, fch_cap, hr_cap, nro_cot_prev, ped_orig, stat_soltd_gto, " +
                    "   cve_pais, cve_edo, cve_mpio, cve_conf, ped, veh, cto_fte, cto_ad_fte, incoterm, tc_fte, " +
                    "   peso_teor, usr_aut, coment_aut, mdp, fch_repgm, suc_doc_padre_char, gen_doc_padre_char, " +
                    "   nat_doc_padre_char, gpo_doc_padre_num, tpo_doc_padre_num, fol_doc_padre_char, doc_padre_compl, " +
                    "   cve_veh_emb, fch1, hr1, bda1, usr1, fch2, hr2, bda2, usr2, fch3, hr3, bda3, usr3, fch4, hr4, " +
                    "   bda4, usr4, fch5, hr5, bda5, usr5, fch6, hr6, bda6, usr6 " +
                    "FROM encabezadomov " +
                    "WHERE nat = 'SC' " +
                    "AND gen = 'CPN' " +
                    "AND usr1 = @usr1";


                string idUserQuery = "SELECT usuarioid, areaid, email FROM usuarios WHERE nombreusuario = @nombre_usuario";
                parameters.Add("nombre_usuario", User.Identity.Name);
                var userResult = RunQuery(idUserQuery, parameters)[0];

                parameters.Clear();
                parameters.Add("usr1", userResult["usuarioid"].ToString());
                var solicitudResult = RunQuery(solicitudUser, parameters, false, null, null, "ERP_SRS");

                var solicitudes = solicitudResult.Select(t => new
                {
                    Sucursal = t["suc"],
                    Generador = t["gen"],
                    Naturaleza = t["nat"],
                    GrupoDoc = t["nro_gpo_doc"],
                    TipoDoc = t["nro_tp_doc"],
                    Folio = t["fol_doc"],
                    Moneda = t["ccy"],
                    Almacen = t["alm"],
                    Fecha = t["fch"],
                    ClienteProveedor = t["cli_prov"],
                    Referencia = t["refe"],
                    VendedorComprador = t["vdr_cpr"],
                    Descuento = t["dto"],
                    Iva = t["iva"],
                    IepsIsr = t["ieps_isr"],
                    Importe = t["imp"],
                    PlazoDias = t["pl_dias"],
                    FechaEntrega = t["fch_pg_entrega"],
                    Comentario1 = t["coment1"],
                    Comentario2 = t["coment2"],
                    Comentario3 = t["coment3"],
                    ComentAut = t["coment_aut"],

                }).ToList();

                return Json(new { data = solicitudes });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al buscar tickets: " + ex.Message });
            }
        }


        [HttpGet]
        [Authorize]
        public JsonResult SolicitudPorUsuarioPartidas(string fol_doc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "SELECT cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, nro_part, cve_prod, cant_ud, " +
                    "   descr_prod, ud, pv_prod, imp_part, dto1, iva, ieps, nat_doc_ant, gpo_doc_ant, tp_doc_ant, " +
                    "   fol_doc_ant, part_doc_ant, saldo_ud_part, cve_cli, cto_vta_part, cve_vdr_cpr, refe, cve_alm, " +
                    "   fch, mt_cto_, exis_prev_u, exis_prev_peso, ccy, cto_ccy, vta_ccy, ot, conc " +
                    "   FROM partidasdoc " +
                    "WHERE fol_doc =  @fol_doc " +
                    "AND gen = 'CPN' " +
                    "AND nat = 'SC'";

                parameters.Add("fol_doc", fol_doc);

                var solicitudesPartidas = RunQuery(query, parameters, false, null, null, "ERP_SRS");

                return Json(new { data = solicitudesPartidas });

            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = "Error al buscar tickets: " + ex.Message
                });
            }
        }

        public void SendEmail(string folio, string id, string solicitud, string usr, string email, string total, string comentarios, string destinatario)
        {
            var urlBase = $"{Request.Scheme}://{Request.Host}";
            string urlCotizacion = $"{urlBase}/Solicitudes/Detalle/{id}";

            string mensajeHtml = $@"
            <table width='100%' cellpadding='0' cellspacing='0' style='font-family: Arial, sans-serif; background-color: #f4f4f4; padding: 20px;'>
              <tr>
                <td align='center'>
                  <table width='600' cellpadding='0' cellspacing='0' style='background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,0.1);'>
                    <tr style='background-color: #004080; color: #ffffff;'>
                      <td style='padding: 20px; text-align: center;'>
                        <span style='font-size: 22px;'>Nueva solicitud de cotización</span>
                      </td>
                    </tr>
                    <tr>
                      <td style='padding: 30px;'>
                        <p style='font-size: 16px; color: #333;'>Hola,</p>
                        <p style='font-size: 16px; color: #333;'>Se ha registrado una nueva solicitud de cotización con los siguientes datos:</p>
            
                        <table width='100%' cellpadding='5' cellspacing='0' style='font-size: 15px; color: #555;'>
                          <tr>
                            <td style='font-weight: bold; width: 180px;'>📄 Folio solicitud:</td>
                            <td>{folio}</td>
                          </tr>
                          <tr>
                            <td style='font-weight: bold;'>📅 Fecha:</td>
                            <td>{solicitud}</td>
                          </tr>
                          <tr>
                            <td style='font-weight: bold;'>👤 Solicitante:</td>
                            <td>{usr}</td>
                          </tr>
                          <tr>
                            <td style='font-weight: bold;'>📧 Correo:</td>
                            <td>{email}</td>
                          </tr>
                          <tr>
                            <td style='font-weight: bold;'>💰 Monto total:</td>
                            <td>${total}</td>
                          </tr>
                          <tr>
                            <td style='font-weight: bold;'>📝 Comentarios:</td>
                            <td>{comentarios}</td>
                          </tr>
                        </table>
            
                        <table cellpadding='0' cellspacing='0' border='0' align='center' style='margin: 30px auto 0 auto;'>
                          <tr bgcolor='#007bff'>
                            <td style='background-color: #007bff; border-radius: 5px; text-align: center;'>
                              <a href='{urlCotizacion}'
                                 style='display: inline-block; padding: 12px 24px; color: #ffffff; font-size: 16px;
                                        text-decoration: none; font-weight: bold; font-family: Arial, sans-serif;'>
                                🔍 Ver solicitud
                              </a>
                            </td>
                          </tr>
                        </table>
            
                        <p style='margin-top: 30px; font-size: 14px; color: #888;'>Este correo es solo para fines de notificación.</p>
                      </td>
                    </tr>
                    <tr style='background-color: #f0f0f0;'>
                      <td style='padding: 20px; text-align: center; font-size: 12px; color: #999;'>
                        © {DateTime.Now.Year} Sellos y Retenes. Todos los derechos reservados.
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>";

            _correoHelper.EnviarCorreoNotificacionAsync(destinatario, "Solicitud de Cotización", mensajeHtml
            );
        }
       
    }
}