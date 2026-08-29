using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Operaciones
{
    public class ServiciosController : Utilities
    {
        public JsonResult GetServicios(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre ?? "" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
            };

            string where = " AND (" +
                "   LOWER(em.uuid) LIKE LOWER('%' || @nombre || '%') " +
                "   OR LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "  CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%')) ";

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                "   em.gen || '-' || em.nat || '-' || (EXTRACT(YEAR FROM em.fch))::text || '-' || em.fol_doc AS folio, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "   (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE usuarioid = usr0) AS responsable, " +
                "   a.nombre AS nombre_area " +
                "FROM encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                $"WHERE em.nat IN ('OC', 'GTO') AND em.estatus_id != 11 {where} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var servicis = RunQuery(query, parameters);
            return Json(servicis);
        }

        //[AuditAction(Modulo = "Servicios", Accion = "Cerrar servicio")]
        //[HttpPost, ValidateAntiForgeryToken]
        //public JsonResult CerrarServicio(IFormCollection fc)
        //{
        //    try
        //    {
        //        var parameters = new Dictionary<string, object>();
        //        int encabezado = Convert.ToInt32(fc["encabezado"].ToString());
        //        string query = "UPDATE encabezadomov SET estatus_id = 11 WHERE id_encabezado = @encabezado";
        //        parameters.Add("encabezado", encabezado);
        //        RunUpdate(query, parameters);

        //        List<PolizaData> poliza = GenerarDatosPoliza(encabezado);
        //        var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), encabezado, poliza);
        //        RegistrarCarteras(resultadoP[0].idPoliza, GetUserId(User.Identity.Name));

        //        return Json(new { icon = "success", title = "Servicio cerrado exitosamente", html = "El servicio ha sido consumido y ya no estara disponible", shoCancelButton = false });
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { icon = "error", title = "Ocurrio un error al cerrar el servicio", html = ex.Message, shoCancelButton = false });
        //    }
        //}
    }
}