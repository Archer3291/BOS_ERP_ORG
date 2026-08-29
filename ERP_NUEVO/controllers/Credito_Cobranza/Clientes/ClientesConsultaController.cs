using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Consulta de clientes: listado paginado de la tabla principal
    /// y detalle completo que alimenta los modales de edición.
    /// </summary>
    public partial class ClientesController : Utilities
    {
        /// <summary>
        /// Página de clientes ordenada por saldo pendiente, con el total
        /// de registros y el saldo global por cobrar del filtro aplicado.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetClientes(IFormCollection fc)
        {
            try
            {
                int page = Convert.ToInt32(fc["page"].ToString());
                int pageSize = Convert.ToInt32(fc["pageSize"].ToString());

                var parameters = new Dictionary<string, object>();
                parameters.Add("nombre", $"%{fc["nombre"].ToString()}%");
                parameters.Add("offset", (page - 1) * pageSize);
                parameters.Add("pageSize", pageSize);
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                string query = "SELECT c.id_cliente, c.cve_cli, c.n_cli, c.rfc , c.lim_crd, es_internacional, c.idf " +
                    "FROM catclientes c " +
                    "LEFT JOIN cartera_clientes cc ON cc.cliente_id = c.id_cliente " +
                    "WHERE c.empresa_id = @empresa_id AND (LOWER(c.n_cli) LIKE LOWER(@nombre) OR LOWER(c.cve_cli) LIKE LOWER(@nombre))" +
                    "GROUP BY c.id_cliente, c.cve_cli, c.n_cli, c.rfc " +
                    "ORDER BY COALESCE(SUM(cc.saldo_pendiente), 0) DESC " +
                    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var data = RunQuery(query, parameters);

                query = "SELECT COUNT(*) " +
                    "FROM catclientes c " +
                    "WHERE c.empresa_id = @empresa_id AND (LOWER(c.n_cli) LIKE LOWER(@nombre) OR LOWER(c.cve_cli) LIKE LOWER(@nombre))";
                var total = RunScalar(query, parameters);

                query = "SELECT COALESCE(SUM(cc.saldo_pendiente), 0) saldo_cobrar " +
                    "FROM cartera_clientes cc " +
                    "LEFT JOIN catclientes c ON c.id_cliente = cc.cliente_id AND c.empresa_id = @empresa_id " +
                    "WHERE LOWER(c.n_cli) LIKE LOWER(@nombre) OR LOWER(c.cve_cli) LIKE LOWER(@nombre) ";
                var saldoCobrar = RunScalar(query, parameters);

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { data, total, saldoCobrar, icon = "success" });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Clientes/GetClientes");
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error" });
            }
        }

        /// <summary>
        /// Detalle del cliente con todo lo que necesitan las pestañas del
        /// modal de edición: datos generales, contacto, dirección fiscal,
        /// transportes y addendas.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDetallesCliente(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id_cliente", Convert.ToInt32(fc["id_cliente"].ToString()));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = "SELECT * FROM catclientes WHERE id_cliente = @id_cliente AND empresa_id = @empresa_id";
            var cliente = RunQuery(query, parameters);

            query = "SELECT correo FROM correos_cliente WHERE cliente_id = @id_cliente";
            var correos = RunQuery(query, parameters);

            query = "SELECT telefono FROM telefonos_cliente WHERE cliente_id = @id_cliente";
            var telefonos = RunQuery(query, parameters);

            query = "SELECT id_dir_facturacion, entidad_tipo, entidad_clave, consecutivo, calle, no_exterior, no_interior, colonia, " +
                "   localidad, referencia, municipio, estado, pais, codigo_postal, forma_pago, no_cuenta_pago, uso_sugerido, extranjero, " +
                "   regimen_fiscal, razon_social " +
                "FROM direcciones_facturacion WHERE entidad_clave = (SELECT cve_cli FROM catclientes WHERE id_cliente = @id_cliente AND empresa_id = @empresa_id)";
            var datosFiscales = RunQuery(query, parameters);

            query = "SELECT id_trans_int, cliente_id, tipo_transporte tipo FROM transportes_clientes_internacionales WHERE cliente_id = @id_cliente";
            var transportes = RunQuery(query, parameters);

            query = "SELECT cdf.id_addenda, cdf.nombre, cdf.xml_namespace, cdf.xml_prefix, cdf.version, cdf.data_template, cdf.usar_conceptos,cdf.created_at, cdf.updated_at " +
                "FROM cfdi_addenda_def cdf " +
                "INNER JOIN catclientes cc ON cdf.id_cliente = cc.id_cliente AND cc.empresa_id = @empresa_id " +
                "WHERE cc.id_cliente = @id_cliente AND cdf.activo = true " +
                "ORDER BY cdf.nombre";
            var addendas = RunQuery(query, parameters);

            return Json(new { cliente, correos, telefonos, datosFiscales, transportes, addendas });
        }
    }
}
