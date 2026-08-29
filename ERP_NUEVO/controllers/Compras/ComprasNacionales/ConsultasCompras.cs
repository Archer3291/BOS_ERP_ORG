using Microsoft.AspNetCore.Mvc;
using Npgsql;
using BOS_ERP.Models;
using BOS_ERP.Services;

namespace BOS_ERP.Controllers.Compras
{
    public partial class RequisicionController : Utilities
    {
        private sealed class ParametrosTabla
        {
            public int Page { get; set; } = 1;
            // 0 = sin paginar: lo usan las vistas que aun consumen el endpoint por GET
            public int PageSize { get; set; }
            public string Busqueda { get; set; } = "";
            public string Folio { get; set; } = "";
            public string SortColumn { get; set; } = "fch";
            public string SortDir { get; set; } = "desc";
            public int Offset => (Page - 1) * PageSize;
        }

        private static readonly Dictionary<string, string> ColumnasOrdenables = new()
        {
            ["nuevo_codigo"] = "em.folio",
            ["folio"] = "em.folio",
            ["responsable"] = "em.usr_doc",
            ["imp"] = "em.imp",
            ["sub"] = "em.sub",
            ["dto"] = "em.dto",
            ["coment_aut"] = "em.coment_aut",
            ["nombre_area"] = "a.nombre",
        };

        private ParametrosTabla LeerParametrosTabla(IFormCollection fc)
        {
            // TableBuilder envia los filtros por POST; las vistas que aun consumen
            // estos endpoints por GET los mandan (o no) en el query string.
            string Valor(string clave)
            {
                var enForm = fc?[clave].ToString();
                if (!string.IsNullOrWhiteSpace(enForm)) return enForm.Trim();

                var enQuery = Request.Query[clave].ToString();
                return string.IsNullOrWhiteSpace(enQuery) ? "" : enQuery.Trim();
            }

            var p = new ParametrosTabla();

            if (int.TryParse(Valor("pageSize"), out int pageSize) && pageSize > 0)
            {
                p.PageSize = pageSize;
                if (int.TryParse(Valor("page"), out int page) && page > 0) p.Page = page;
            }

            p.Busqueda = Valor("nombre");
            p.Folio = Valor("folio");

            string sortColumn = Valor("sortColumn");
            if (ColumnasOrdenables.ContainsKey(sortColumn))
                p.SortColumn = sortColumn;

            p.SortDir = Valor("sortDir").ToLowerInvariant() == "asc" ? "ASC" : "DESC";

            return p;
        }

        private JsonResult ConsultarDocumentos(IFormCollection fc,string filtro,Dictionary<string, object> parametros = null,string columnaFecha = "em.fch",string usuarioJoin = "em.usr1",string columnasExtra = "")
        {
            try
            {
                var p = LeerParametrosTabla(fc);
                parametros ??= new Dictionary<string, object>();

                string orden = p.SortColumn == "fch" ? columnaFecha : ColumnasOrdenables[p.SortColumn];
                string where = $"({filtro})";

                if (!string.IsNullOrEmpty(p.Busqueda))
                {
                    where += " AND (em.folio ILIKE '%' || @busqueda || '%' " +
                             "   OR em.coment_aut ILIKE '%' || @busqueda || '%' " +
                             "   OR em.usr_doc ILIKE '%' || @busqueda || '%' " +
                             "   OR u.nombre || ' ' || u.apellido ILIKE '%' || @busqueda || '%') ";
                    parametros["busqueda"] = p.Busqueda;
                }

                if (!string.IsNullOrEmpty(p.Folio))
                {
                    where += " AND em.folio ILIKE '%' || @folioFiltro || '%' ";
                    parametros["folioFiltro"] = p.Folio;
                }

                string from = "FROM encabezadomov em " +
                    $"INNER JOIN usuarios u ON u.usuarioid = {usuarioJoin} " +
                    "INNER JOIN areas a ON a.areaid = u.areaid " +
                    $"WHERE {where} ";

                string paginado = p.PageSize > 0
                    ? "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY"
                    : "";

                string query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.nro_tp_doc, em.fol_doc, " +
                    "   em.cli_prov, em.iva, em.imp, em.sub, em.dto, em.coment_aut, em.usr1, em.id_encabezado, " +
                    "   em.encabezados_padre, em.variacion, em.tipo_producto, em.tipo_proceso, " +
                    "   em.coment1 AS motivo_rechazo, " +
                    "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS nuevo_codigo, " +
                    "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                    "   (SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = em.usr_doc) AS responsable, " +
                    "   a.nombre AS nombre_area, " +
                    $"   {columnaFecha} AS fch, NOW() AS fechaactual{columnasExtra} " +
                    from +
                    $"ORDER BY {orden} {p.SortDir} NULLS LAST " +
                    paginado;

                if (p.PageSize > 0)
                {
                    parametros["offset"] = p.Offset;
                    parametros["pageSize"] = p.PageSize;
                }

                var data = RunQuery(query, parametros);
                int total = p.PageSize > 0
                    ? Convert.ToInt32(RunScalar($"SELECT COUNT(*) {from}", parametros))
                    : data.Count;

                return Json(new { data, total });
            }
            catch (Exception ex)
            {
                return Json(new { data = new List<object>(), total = 0, success = false, error = ex.Message });
            }
        }

        private void GuardarTotalesDocumento(int idEncabezado, TotalesDocumento totales, decimal? total,
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            string query = "UPDATE encabezadomov SET sub = @sub, dto = @dto, imp = COALESCE(@imp, imp) " +
                "WHERE id_encabezado = @id_encabezado";

            RunUpdate(query, new Dictionary<string, object>
            {
                ["sub"] = totales.Subtotal,
                ["dto"] = totales.Descuento,
                ["imp"] = (object)total ?? DBNull.Value,
                ["id_encabezado"] = idEncabezado
            }, false, conn, tx);
        }
    }
}
