using BOS_ERP.Controllers;
using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class ContabilidadController : Utilities
    {
        public JsonResult GetCuentasFinanzas()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            
            var returnResult = new Dictionary<string, object>();
            string query = "SELECT cf.id_cuenta_contable, cf.codigo, cf.nombre, cf.uuid, cf.naturaleza " +
                "FROM cuentas_finanzas cf " +
                "WHERE cf.empresa_id = @empresa";
            var cuentas = RunQuery(query, parameters);
            returnResult.Add("cuentas", cuentas);

            query = "SELECT areaid, nombre, descripcion, abreviatura " +
                "FROM areas";
            var centroCostos = RunQuery(query);
            returnResult.Add("centroCostos", centroCostos);

            return Json(returnResult);
        }

        public JsonResult GetDetallesDocumento(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, em.imp, " +
                "   em.folio, (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                "   fch, em.uuid " +
                "FROM encabezadomov em " +
                "WHERE em.uuid = @uuid";
            parameters.Add("uuid", fc["uuid"].ToString());
            var detalles = RunQuery(query, parameters)[0];

            query = "SELECT COUNT(*) qty FROM polizas WHERE referencia = @encabezado_id";
            parameters.Add("encabezado_id", detalles["id_encabezado"]);
            int qty = Convert.ToInt32(RunScalar(query, parameters));

            if (qty > 0)
            {
                query = "SELECT uuid FROM polizas WHERE referencia = @encabezado_id";
                var uuidP = RunScalar(query, parameters);

                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", title = "Este folio ya tiene una poliza", html = $"<p>El folio <b>{detalles["folio"]}</b> ya pertenece a la poliza con el id <b>{uuidP.ToString()}</b></p>", showCancelButton = false });
            }

            query = "SELECT io.subtotal, io.importe, io.imp_variable, ci.cve_impuesto, ci.es_retencion " +
                "FROM imp_oc io " +
                "INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = io.encabezado_id " +
                "WHERE e.uuid = @uuid";
            var impuestos = RunQuery(query, parameters);

            query = "SELECT SUM(p.imp_part - p.dto1) subTotal FROM partidasdoc p " +
                "INNER JOIN encabezadomov e ON e.id_encabezado = p.encabezado_id " +
                "WHERE e.uuid = @uuid";
            var subtotal = RunScalar(query, parameters);

            return Json(new { detalles, impuestos, subtotal, icon = "success" });

        }

        [AuditAction(Modulo = "Contabilidad", Accion = "Creacion de polizas especiales")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult RegistrarPoliza(IFormCollection fc)
        {
            try
            {
                int usuario = GetUserId(User.Identity.Name);
                string raw = fc["encabezado_id"].ToString();
                int? encabezado = null;

                if (fc["encabezado_id"].ToString() != "" && fc["encabezado_id"].ToString() != "null")
                {
                    encabezado = int.Parse(fc["encabezado_id"].ToString());
                }

                string datosPolizaJson = fc["datosPoliza"].ToString();

                // Puedes deserializarlo como dynamic, o un tipo específico si ya tienes una clase PolizaDTO
                PolizaData datosPoliza = JsonConvert.DeserializeObject<PolizaData>(datosPolizaJson);
                datosPoliza.EsManual = true;

                // Llamas a tu método que hace la transacción
                var poliza = RegistrarPolizas(usuario, encabezado, datosPoliza);

                return Json(new { icon = "success", title = "Poliza registrada correctamente", html = $"<p>Puedes revisar consultar la poliza en la pagina Consultar poliza con el id unico: {poliza.uuidPoliza} </p>" });
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", title = "Error al registrar póliza: ", html = ex.Message });
            }
        }
    }
}