using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Operaciones
{
    public class PolizasDiscrepanciaController : Utilities
    {
        #region Obtener Datos
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetPolziasDiscrepancia(string nombre, int page = 1, int pageSize = 50, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("nombre", nombre ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("fechaInicio", fechaInicio);
            parameters.Add("fechaFin", fechaFin);

            string where = " WHERE p.tiene_discrepancia = true AND (LOWER(p.descripcion) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(p.uuid) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "  CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(emd.gen || '-' || emd.nat || '-' || EXTRACT(YEAR FROM emd.fch)::text || '-' || emd.fol_doc || " +
                "  CASE WHEN emd.variacion > 0 THEN '-' || num_to_letters(emd.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%')) ";

            if (fechaInicio.HasValue)
                where += " AND p.fecha >= @fechaInicio ";
            if (fechaFin.HasValue)
                where += " AND p.fecha <= @fechaFin ";

            string query = "SELECT p.id_poliza, p.tipo, p.descripcion, p.estado, " +
                "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "       CASE WHEN em.variacion > 0 " +
                "       THEN '-' || num_to_letters(em.variacion) " +
                "   ELSE '' END AS folio, " +
                "   p.fecha_creacion fecha, p.uuid, u.nombre || ' ' || u.apellido usuario, p.referencia, p.id_documento_disc, emd.coment1 " +
                "FROM polizas p " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN encabezadomov emd ON emd.id_encabezado = p.id_documento_disc " +
                "INNER JOIN usuarios u ON u.usuarioId = p.usuario_id " +
                $" {where} " +
                "ORDER BY p.fecha_creacion DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM polizas p " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN encabezadomov emd ON emd.id_encabezado = p.id_documento_disc " +
                "INNER JOIN usuarios u ON u.usuarioId = p.usuario_id " +
                $" {where} ";
            var total = RunScalar(query, parameters);

            return Json(new { data });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDetallesPolizaDiscrepancia(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT p.id_poliza, p.uuid poliza_uuid, cf.codigo, c.n_prov, debe, haber, a.nombre centro_costos, " +
                "   dp.descripcion " +
                "FROM detalles_polizas dp " +
                "INNER JOIN polizas p ON p.id_poliza = dp.poliza_id " +
                "INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id " +
                "INNER JOIN catproveedores c ON c.id_prov = p.proveedor_id AND c.id_empresa = @id_empresa " +
                "INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                "WHERE p.id_poliza = @id_poliza";
            parameters.Add("id_poliza", Convert.ToInt32(fc["id_poliza"].ToString()));
            parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var detallesPoliza = RunQuery(query, parameters);

            query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.tipo_proceso, em.nro_tp_doc, em.fol_doc, " +
              "   em.cli_prov, em.iva, em.coment_aut,  em.usr1, em.id_encabezado, " +
              "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
              "       CASE WHEN em.variacion > 0 " +
              "           THEN '-' || num_to_letters(em.variacion) " +
              "           ELSE '' END AS folio, " +
              "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
              "   fch, em.uuid, em.id_encabezado, c.cve_prov, " +
              "   (SELECT SUM((cant_ud * pv_prod) - COALESCE(dto1, 0)) FROM partidasdoc p WHERE p.encabezado_id = em.id_encabezado) AS imp " +
              "FROM encabezadomov em " +
              "INNER JOIN catproveedores c ON c.id_prov = em.refe AND c.id_empresa = @id_empresa " +
              "WHERE em.id_encabezado = @referencia";
            parameters.Add("referencia", Convert.ToInt32(fc["referencia"].ToString()));
            var referencia = RunQuery(query, parameters)[0];

            query = "SELECT pd.id_partidas AS id, pd.cve_prod AS codigo_producto, pd.producto_id, pd.descr_prod AS descripcion, pd.cant_ud AS cantidad, " +
                "   pd.pv_prod AS precio, pd.dto1 AS descuento, pd.ud AS unidad, (pd.imp_part - pd.dto1) AS importe, pd.fol_doc_ant estado " +
                "FROM partidasdoc pd " +
                "WHERE pd.encabezado_id = @referencia";
            var partidasReferencia = RunQuery(query, parameters);

            query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.tipo_proceso, em.nro_tp_doc, em.fol_doc, " +
              "   em.cli_prov, em.iva, em.coment_aut,  em.usr1, em.id_encabezado, " +
              "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
              "       CASE WHEN em.variacion > 0 " +
              "           THEN '-' || num_to_letters(em.variacion) " +
              "           ELSE '' END AS folio, " +
              "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
              "   fch, em.uuid, em.id_encabezado, c.cve_prov, " +
              "   (SELECT SUM((cant_ud * pv_prod) - COALESCE(dto1, 0)) FROM partidasdoc p WHERE p.encabezado_id = em.id_encabezado) AS imp " +
              "FROM encabezadomov em " +
              "INNER JOIN catproveedores c ON c.id_prov = em.refe AND c.id_empresa = @id_empresa " +
              "WHERE em.id_encabezado = @referencia_discrepancia";
            parameters.Add("referencia_discrepancia", Convert.ToInt32(fc["referencia_discrepancia"].ToString()));
            var referenciaDiscrepancia = RunQuery(query, parameters)[0];

            query = "SELECT pd.id_partidas AS id, pd.cve_prod AS codigo_producto, pd.producto_id, pd.descr_prod AS descripcion, pd.cant_ud AS cantidad, " +
                "   pd.pv_prod AS precio, pd.dto1 AS descuento, pd.ud AS unidad, (pd.imp_part - pd.dto1) AS importe, pd.fol_doc_ant estado " +
                "FROM partidasdoc pd " +
                "WHERE pd.encabezado_id = @referencia_discrepancia";
            var partidasReferenciaDiscrepancia = RunQuery(query, parameters);

            return Json(new { detallesPoliza, referencia, partidasReferencia, referenciaDiscrepancia, partidasReferenciaDiscrepancia });
        }
        #endregion

        #region procesar polizas con discrepancia
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult ProcesarPolizas(int poliza_id, string descripcion, string accion, List<IFormFile> archivos)
        {
            var parameters = new Dictionary<string, object>();
            var _parameters = new List<Dictionary<string, object>>();

            string query = "INSERT INTO archivos_polizas_discrepancia (nombre_original, uuid, extencion, path, poliza_id) " +
                "   VALUES (@nombre, @uuid, @extencion, @path, @poliza_id)";
            foreach (var file in archivos)
            {
                parameters = new Dictionary<string, object>();
                if (file != null && file.Length > 0)
                {
                    var nombreOriginal = file.FileName;
                    var extension = Path.GetExtension(file.FileName);
                    var ruta = "content/archivos_polizas_discrepancia/";
                    var uuid = Guid.NewGuid().ToString();
                    UploadFormFileToPath(ruta, file, uuid, extension);

                    parameters.Add("nombre", nombreOriginal);
                    parameters.Add("uuid", uuid);
                    parameters.Add("extencion", extension);
                    parameters.Add("path", $"{ruta}{uuid}{extension}");
                    parameters.Add("poliza_id", poliza_id);

                    _parameters.Add(parameters);
                }
            }

            RunUpdate(query, _parameters);

            parameters = new Dictionary<string, object>();
            query = "UPDATE polizas SET tiene_discrepancia = false, acciones = @accion, comentario_discrepancia = @comentario " +
                "WHERE id_poliza = @poliza_id";
            parameters.Add("accion", accion);
            parameters.Add("poliza_id", poliza_id);
            parameters.Add("comentario", descripcion);
            RunUpdate(query, parameters);

            return Json(new { icon = "success", title = "Cambios realizados", html = "Los cambios se aplicaron correctamente a la poliza" });
        }
        #endregion

        #region Consultar datos
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetPolizasDiscrepanciaConsulta(string nombre, int page = 1, int pageSize = 50, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("nombre", nombre ?? "");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("fechaInicio", fechaInicio);
            parameters.Add("fechaFin", fechaFin);

            string where = " WHERE p.tiene_discrepancia = false AND id_documento_disc IS NOT NULL AND (LOWER(p.descripcion) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(p.uuid) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "  CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%')) ";

            if (fechaInicio.HasValue)
                where += " AND p.fecha >= @fechaInicio ";
            if (fechaFin.HasValue)
                where += " AND p.fecha <= @fechaFin ";

            string query = "SELECT p.id_poliza, p.tipo, p.descripcion, p.estado, " +
                "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "       CASE WHEN em.variacion > 0 " +
                "       THEN '-' || num_to_letters(em.variacion) " +
                "   ELSE '' END AS folio, " +
                "   p.fecha_creacion fecha, p.uuid, u.nombre || ' ' || u.apellido usuario, p.referencia, p.id_documento_disc, emd.coment1 " +
                "FROM polizas p " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN encabezadomov emd ON emd.id_encabezado = p.id_documento_disc " +
                "INNER JOIN usuarios u ON u.usuarioId = p.usuario_id " +
                $" {where} " +
                "ORDER BY p.fecha_creacion DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM polizas p " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN usuarios u ON u.usuarioId = p.usuario_id " +
                $" {where} ";
            var total = RunScalar(query, parameters);

            return Json(new { data });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDetallesPolizaDiscrepanciaConsulta(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id_poliza", Convert.ToInt32(fc["id_poliza"].ToString()));
            parameters.Add("referencia", Convert.ToInt32(fc["referencia"].ToString()));
            parameters.Add("referencia_discrepancia", Convert.ToInt32(fc["referencia_discrepancia"].ToString()));
            parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT p.id_poliza, u.nombre || ' ' || u.apellido AS nombre, p.tipo, p.descripcion, p.estado, " +
                 "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                 "       CASE WHEN em.variacion > 0 " +
                 "       THEN '-' || num_to_letters(em.variacion) " +
                 "   ELSE '' END AS folio, " +
                 "   emd.gen || '-' || emd.nat || '-' || EXTRACT(YEAR FROM emd.fch)::text || '-' || emd.fol_doc || " +
                 "       CASE WHEN emd.variacion > 0 " +
                 "       THEN '-' || num_to_letters(emd.variacion) " +
                 "   ELSE '' END AS folio_disc, " +
                 "   p.fecha, p.uuid, p.referencia, p.acciones, p.comentario_discrepancia  " +
                 "FROM polizas p " +
                 "INNER JOIN usuarios u ON u.usuarioid = p.usuario_id " +
                 "INNER JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                 "INNER JOIN encabezadomov emd ON emd.id_encabezado = p.id_documento_disc " +
                 "WHERE p.id_poliza = @id_poliza";
            var poliza = RunQuery(query, parameters)[0];

            query = "SELECT p.id_poliza, p.uuid poliza_uuid, cf.codigo, c.n_prov, debe, haber, a.nombre centro_costos, " +
                "   dp.descripcion " +
                "FROM detalles_polizas dp " +
                "INNER JOIN polizas p ON p.id_poliza = dp.poliza_id " +
                "INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id " +
                "INNER JOIN catproveedores c ON c.id_prov = p.proveedor_id AND c.id_empresa = @id_empresa " +
                "INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                "WHERE p.id_poliza = @id_poliza";
            var detallesPoliza = RunQuery(query, parameters);

            query = "SELECT nombre_original, extencion, path " +
                "FROM archivos_polizas_discrepancia " +
                "WHERE poliza_id = @id_poliza";
            var archivos = RunQuery(query, parameters);

            query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.tipo_proceso, em.nro_tp_doc, em.fol_doc, " +
              "   em.cli_prov, em.iva, em.coment_aut,  em.usr1, em.id_encabezado, " +
              "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
              "       CASE WHEN em.variacion > 0 " +
              "           THEN '-' || num_to_letters(em.variacion) " +
              "           ELSE '' END AS folio, " +
              "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
              "   fch, em.uuid, em.id_encabezado, c.cve_prov, " +
              "   (SELECT SUM((cant_ud * pv_prod) - COALESCE(dto1, 0)) FROM partidasdoc p WHERE p.encabezado_id = em.id_encabezado) AS imp " +
              "FROM encabezadomov em " +
              "INNER JOIN catproveedores c ON c.id_prov = em.refe AND c.id_empresa = @id_empresa " +
              "WHERE em.id_encabezado = @referencia";
            var referencia = RunQuery(query, parameters)[0];

            query = "SELECT pd.id_partidas AS id, pd.cve_prod AS codigo_producto, pd.producto_id, pd.descr_prod AS descripcion, pd.cant_ud AS cantidad, " +
                "   pd.pv_prod AS precio, pd.dto1 AS descuento, pd.ud AS unidad, (pd.imp_part - pd.dto1) AS importe, pd.fol_doc_ant estado " +
                "FROM partidasdoc pd " +
                "WHERE pd.encabezado_id = @referencia";
            var partidasReferencia = RunQuery(query, parameters);

            query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.tipo_proceso, em.nro_tp_doc, em.fol_doc, " +
              "   em.cli_prov, em.iva, em.coment_aut,  em.usr1, em.id_encabezado, " +
              "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
              "       CASE WHEN em.variacion > 0 " +
              "           THEN '-' || num_to_letters(em.variacion) " +
              "           ELSE '' END AS folio, " +
              "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
              "   fch, em.uuid, em.id_encabezado, c.cve_prov, " +
              "   (SELECT SUM((cant_ud * pv_prod) - COALESCE(dto1, 0)) FROM partidasdoc p WHERE p.encabezado_id = em.id_encabezado) AS imp " +
              "FROM encabezadomov em " +
              "INNER JOIN catproveedores c ON c.id_prov = em.refe AND c.id_empresa = @id_empresa " +
              "WHERE em.id_encabezado = @referencia_discrepancia";
            var referenciaDiscrepancia = RunQuery(query, parameters)[0];

            query = "SELECT pd.id_partidas AS id, pd.cve_prod AS codigo_producto, pd.producto_id, pd.descr_prod AS descripcion, pd.cant_ud AS cantidad, " +
                "   pd.pv_prod AS precio, pd.dto1 AS descuento, pd.ud AS unidad, (pd.imp_part - pd.dto1) AS importe, pd.fol_doc_ant estado " +
                "FROM partidasdoc pd " +
                "WHERE pd.encabezado_id = @referencia_discrepancia";
            var partidasReferenciaDiscrepancia = RunQuery(query, parameters);

            return Json(new { poliza, detallesPoliza, archivos, referencia, partidasReferencia, referenciaDiscrepancia, partidasReferenciaDiscrepancia });
        }
        #endregion
    }
}