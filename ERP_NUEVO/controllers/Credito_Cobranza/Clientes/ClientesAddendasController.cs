using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// CRUD de addendas CFDI por cliente (cfdi_addenda_def).
    /// El borrado es lógico: sólo se marca activo = false.
    /// </summary>
    public partial class ClientesController : Utilities
    {
        /// <summary>Columnas que devuelven todas las consultas de addenda.</summary>
        private const string ColumnasAddenda = @"
                id_addenda,
                id_cliente,
                nombre,
                xml_namespace,
                xml_schema,
                xml_prefix,
                usar_conceptos,
                version,
                data_template,
                activo,
                created_at,
                updated_at";

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetAddendas(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id_cliente", Convert.ToInt32(fc["id_cliente"].ToString()));

                string query = $@"
            SELECT {ColumnasAddenda}
            FROM cfdi_addenda_def
            WHERE id_cliente = @id_cliente
            AND activo = true
            ORDER BY nombre";

                var addendas = RunQuery(query, parameters);

                return Json(new { icon = "success", data = addendas });
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetAddenda(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id_addenda", Convert.ToInt32(fc["id_addenda"].ToString()));

                string query = $@"
            SELECT {ColumnasAddenda}
            FROM cfdi_addenda_def
            WHERE id_addenda = @id_addenda";

                var addenda = RunQuery(query, parameters);

                if (addenda.Count == 0)
                {
                    return Json(new { icon = "error", message = "Addenda no encontrada" });
                }

                return Json(new { icon = "success", data = addenda[0] });
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult CrearAddenda(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["id_cliente"].ToString()))
                    return Json(new { icon = "error", message = "El ID del cliente es obligatorio." });

                var error = ValidarAddenda(fc);
                if (error != null) return error;

                var parameters = ParametrosAddenda(fc);
                parameters.Add("id_cliente", Convert.ToInt32(fc["id_cliente"].ToString()));
                parameters.Add("activo", true);
                parameters.Add("created_at", DateTime.Now);

                string query = @"
            INSERT INTO cfdi_addenda_def (
                id_cliente,
                nombre,
                xml_namespace,
                xml_schema,
                xml_prefix,
                usar_conceptos,
                version,
                data_template,
                activo,
                created_at,
                updated_at
            ) VALUES (
                @id_cliente,
                @nombre,
                @xml_namespace,
                @xml_schema,
                @xml_prefix,
                @usar_conceptos,
                @version,
                @data_template::jsonb,
                @activo,
                @created_at,
                @updated_at
            ) RETURNING id_addenda";

                var idAddenda = RunScalar(query, parameters);

                return Json(new
                {
                    icon = "success",
                    title = "Addenda creada exitosamente",
                    id_addenda = idAddenda
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    icon = "error",
                    title = "Error al crear addenda",
                    html = ex.Message
                });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult ActualizarAddenda(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["id_addenda"].ToString()))
                    return Json(new { icon = "error", message = "El ID de la addenda es obligatorio." });

                var error = ValidarAddenda(fc);
                if (error != null) return error;

                var parameters = ParametrosAddenda(fc);
                parameters.Add("id_addenda", Convert.ToInt32(fc["id_addenda"].ToString()));

                string query = @"
            UPDATE cfdi_addenda_def SET
                nombre = @nombre,
                xml_namespace = @xml_namespace,
                xml_schema = @xml_schema,
                xml_prefix = @xml_prefix,
                usar_conceptos = @usar_conceptos,
                version = @version,
                data_template = @data_template::jsonb,
                updated_at = @updated_at
            WHERE id_addenda = @id_addenda";

                RunUpdate(query, parameters);

                return Json(new
                {
                    icon = "success",
                    title = "Addenda actualizada exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    icon = "error",
                    title = "Error al actualizar addenda",
                    html = ex.Message
                });
            }
        }

        /// <summary>Borrado lógico de la addenda.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult EliminarAddenda(IFormCollection fc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fc["id_addenda"].ToString()))
                    return Json(new { icon = "error", message = "El ID de la addenda es obligatorio." });

                var parameters = new Dictionary<string, object>();
                parameters.Add("id_addenda", Convert.ToInt32(fc["id_addenda"].ToString()));
                parameters.Add("updated_at", DateTime.Now);

                string query = @"
            UPDATE cfdi_addenda_def SET
                activo = false,
                updated_at = @updated_at
            WHERE id_addenda = @id_addenda";

                RunUpdate(query, parameters);

                return Json(new
                {
                    icon = "success",
                    title = "Addenda eliminada exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    icon = "error",
                    title = "Error al eliminar addenda",
                    html = ex.Message
                });
            }
        }

        /// <returns>El error a devolver al cliente, o null si todo está bien.</returns>
        private JsonResult ValidarAddenda(IFormCollection fc)
        {
            var obligatorios = new (string Campo, string Mensaje)[]
            {
                ("nombre",        "El nombre de la addenda es obligatorio."),
                ("xml_namespace", "El namespace XML es obligatorio."),
                ("xml_prefix",    "El prefijo XML es obligatorio."),
                ("data_template", "El template de datos es obligatorio."),
            };

            foreach (var (campo, mensaje) in obligatorios)
            {
                if (string.IsNullOrWhiteSpace(fc[campo].ToString()))
                    return Json(new { icon = "error", message = mensaje });
            }

            try
            {
                JsonConvert.DeserializeObject(fc["data_template"].ToString());
            }
            catch
            {
                return Json(new { icon = "error", message = "El template de datos no es un JSON válido." });
            }

            return null;
        }

        /// <summary>
        /// Parámetros comunes al alta y la actualización. El template se
        /// re-serializa para normalizarlo antes de guardarlo como jsonb.
        /// </summary>
        private Dictionary<string, object> ParametrosAddenda(IFormCollection fc)
        {
            var parsedTemplate = JsonConvert.DeserializeObject(fc["data_template"].ToString().Trim());

            var parameters = new Dictionary<string, object>();
            parameters.Add("nombre", fc["nombre"].ToString().Trim());
            parameters.Add("xml_namespace", fc["xml_namespace"].ToString().Trim());
            parameters.Add("xml_schema", GetString(fc["xml_schema"].ToString(), null));
            parameters.Add("xml_prefix", fc["xml_prefix"].ToString().Trim());
            parameters.Add("usar_conceptos", Convert.ToBoolean(fc["usar_conceptos"].ToString()));
            parameters.Add("version", GetString(fc["version"].ToString(), "1.0"));
            parameters.Add("data_template", JsonConvert.SerializeObject(parsedTemplate));
            parameters.Add("updated_at", DateTime.Now);

            return parameters;
        }
    }
}
