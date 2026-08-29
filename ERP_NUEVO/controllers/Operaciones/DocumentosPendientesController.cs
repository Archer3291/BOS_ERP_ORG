using Microsoft.AspNetCore.Mvc;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.Operaciones
{
    public class DocumentosPendientesController : Utilities
    {
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDocumentosPendientes()
        {
            string query = "SELECT pl.id_poliza, pl.tipo, pl.descripcion, pl.estado, pl.referencia, pl.fecha, pl.creado_por, " +
                "   pl.fecha_creacion, pl.uuid, pl.fecha_edicion, pl.cliente_id, pl.proveedor_id, pl.tiene_discrepancia, " +
                "   pl.id_documento_disc, acciones, comentario_discrepancia, pl.cancelada, " +
                "   em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "       CASE WHEN em.variacion > 0 " +
                "       THEN '-' || num_to_letters(em.variacion) " +
                "   ELSE '' END AS folio " +
                "FROM polizas pl " +
                "INNER JOIN usuarios u ON u.usuarioid = pl.usuario_id " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = pl.referencia " +
                "WHERE pl.tiene_discrepancia = true AND pl.cancelada = false";

            var poliza = RunQuery(query);

            return Json(poliza);
        }
    }
}