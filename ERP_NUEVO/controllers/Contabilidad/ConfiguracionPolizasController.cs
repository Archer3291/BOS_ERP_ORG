using Newtonsoft.Json;
using Npgsql;
using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Contabilidad
{
    public class ConfiguracionPolizasController : Utilities
    {
        #region Agregar datos
        public JsonResult RegistrarPoliza(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                var datosGenerales = JsonConvert.DeserializeObject<Dictionary<string, object>>(fc["general"].ToString());
                var partidas = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["partidas"].ToString());
                var documentos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["documentos"].ToString());

                var util = new Utilities(true);
                string connectionString = util._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            string query = "DELETE FROM poliza_partida WHERE id_tipo_poliza = @tipo_poliza";
                            parameters.Add("tipo_poliza", Convert.ToInt32(fc["tipo_poliza"].ToString()));
                            RunUpdate(query, parameters, false, conn, tx);

                            query = "UPDATE poliza_tipo SET id_categoria = @categoria, descripcion = @descripcion, clasificacion_id = @clasificacion, " +
                                "   activo = @activo " +
                                "WHERE id_tipo_poliza = @tipo_poliza";
                            parameters.Add("categoria", Convert.ToInt32(datosGenerales["id_categoria"]));
                            parameters.Add("descripcion", datosGenerales["descripcion"].ToString());
                            parameters.Add("clasificacion", Convert.ToInt32(datosGenerales["id_clasificacion"]));
                            parameters.Add("activo", Convert.ToBoolean(datosGenerales["activo"]));
                            RunUpdate(query, parameters);

                            query = "INSERT INTO poliza_partida (id_tipo_poliza, orden, tipo_cuenta, id_cuenta_contable, " +
                                "   lado, origen_monto, factor, descripcion_template, impuesto_id) " +
                                "VALUES(@tipo_poliza, @orden, @tipo_cuenta::tipo_cuenta_poliza, @cuenta_contable, @lado::lado_poliza, " +
                                "   @origen_monto::origen_monto_poliza, @factor, @descripcion, @impuesto)";

                            foreach (var par in partidas)
                            {
                                parameters = new Dictionary<string, object>();
                                parameters.Add("tipo_poliza", GetInt(fc["tipo_poliza"].ToString(), null));
                                parameters.Add("orden", GetInt(par["orden"], null));
                                parameters.Add("tipo_cuenta", par["tipo_cuenta"].ToString());
                                parameters.Add("cuenta_contable", GetInt(par["id_cuenta_contable"], null));
                                parameters.Add("lado", par["lado"].ToString());
                                parameters.Add("origen_monto", par["origen_monto"].ToString());
                                parameters.Add("factor", GetDecimal(par["factor"]));
                                parameters.Add("descripcion", par["descripcion_template"].ToString());
                                parameters.Add("impuesto", GetInt(par["id_impuesto"], null));

                                RunUpdate(query, parameters, false, conn, tx);
                            }

                            parameters = new Dictionary<string, object>();
                            parameters.Add("tipo_poliza", Convert.ToInt32(fc["tipo_poliza"].ToString()));

                            query = "DELETE FROM poliza_documento WHERE id_tipo_poliza = @tipo_poliza";
                            RunUpdate(query, parameters, false, conn, tx);

                            query = "INSERT INTO poliza_documento " +
                                "(idtpdoc, id_tipo_poliza, evento, orden, activo, tipo_proceso) " +
                                "VALUES(@idtpdoc, @tipo_poliza, 'AL_CREAR'::evento_poliza, 1, true, @tipo_proceso);";

                            foreach (var doc in documentos)
                            {
                                parameters = new Dictionary<string, object>();
                                parameters.Add("tipo_poliza", GetInt(fc["tipo_poliza"].ToString()));
                                parameters.Add("idtpdoc", GetInt(doc["idtpdoc"]));
                                parameters.Add("tipo_proceso", GetString(doc["tipo_proceso"]));

                                RunUpdate(query, parameters, false, conn, tx);
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { icon = "success", title = "Poliza registrada correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", html = ex.Message });
            }
        }

        public JsonResult GuardarCategoria(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("nombre", fc["nombre"].ToString());
                parameters.Add("descripcion", fc["descripcion"].ToString());
                parameters.Add("activo", Convert.ToBoolean(fc["activo"].ToString()));

                string query;

                if (!string.IsNullOrEmpty(fc["id_categoria"].ToString()))
                {
                    parameters.Add("id_categoria", Convert.ToInt32(fc["id_categoria"].ToString()));
                    query = "UPDATE poliza_categoria SET nombre = @nombre, descripcion = @descripcion, activo = @activo " +
                            "WHERE id_categoria = @id_categoria";
                }
                else
                {
                    query = "INSERT INTO poliza_categoria (nombre, descripcion, activo) " +
                            "VALUES (@nombre, @descripcion, @activo)";
                }

                RunUpdate(query, parameters);

                return Json(new { success = true, icon = "success", title = "Categoría guardada correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, icon = "error", title = "Error al guardar: " + ex.Message });
            }
        }

        public JsonResult GuardarTipoPoliza(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("nombre", fc["nombre"].ToString());
                parameters.Add("id_categoria", Convert.ToInt32(fc["id_categoria"].ToString()));
                parameters.Add("clasificacion_id", Convert.ToInt32(fc["clasificacion_id"].ToString()));
                parameters.Add("descripcion", fc["descripcion"].ToString());
                parameters.Add("activo", Convert.ToBoolean(fc["activo"].ToString()));
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"))); // De la sesión

                string query;

                if (!string.IsNullOrEmpty(fc["id_tipo_poliza"].ToString())) // EDITAR
                {
                    parameters.Add("id_tipo_poliza", Convert.ToInt32(fc["id_tipo_poliza"].ToString()));
                    query = "UPDATE poliza_tipo SET nombre = @nombre, id_categoria = @id_categoria, " +
                            "clasificacion_id = @clasificacion_id, descripcion = @descripcion, activo = @activo " +
                            "WHERE id_tipo_poliza = @id_tipo_poliza";
                }
                else // CREAR
                {
                    query = "INSERT INTO poliza_tipo (nombre, id_categoria, clasificacion_id, descripcion, activo, empresa_id) " +
                            "VALUES (@nombre, @id_categoria, @clasificacion_id, @descripcion, @activo, @empresa_id)";
                }

                RunUpdate(query, parameters);

                return Json(new { success = true, icon = "success", title = "Tipo de póliza guardado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, icon = "error", title = "Error al guardar: " + ex.Message });
            }
        }
        #endregion

        #region GetData
        public JsonResult GetPolizas(string nombre, int page = 1, int pageSize = 50)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                parameters.Add("nombre", GetString(nombre, ""));
                parameters.Add("offset", (page - 1) * pageSize);
                parameters.Add("pageSize", pageSize);

                string where = "";

                if (!string.IsNullOrWhiteSpace(nombre))
                {

                    where = " AND (pc.nombre ILIKE '%' || @nombre || '%' OR pt.descripcion ILIKE '%' || @nombre || '%' OR cp.nombre ILIKE '%' || @nombre || '%') ";
                }

                string query = "SELECT pc.nombre categoria, pt.descripcion, cp.nombre clasificacion, e.nombre empresa, pt.activo, " +
                    "   pt.id_tipo_poliza, e.empresaid " +
                    "FROM poliza_tipo pt " +
                    "INNER JOIN poliza_categoria pc ON pc.id_categoria = pt.id_categoria " +
                    "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = pt.clasificacion_id " +
                    "INNER JOIN empresas e ON e.empresaid = pt.empresa_id " +
                    $"WHERE pt.empresa_id = @empresa_id {where} " +
                    $"OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
                var partidas = RunQuery(query, parameters);

                query = "SELECT COUNT(*) FROM poliza_tipo pt " +
                    "INNER JOIN poliza_categoria pc ON pc.id_categoria = pt.id_categoria " +
                    "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = pt.clasificacion_id " +
                    "INNER JOIN empresas e ON e.empresaid = pt.empresa_id " +
                    $"WHERE pt.empresa_id = @empresa_id {where} ";
                var total = RunScalar(query, parameters);

                return Json(new { total = total, data = partidas });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Contabilidad/GetPolizas");
                return Json(new { icon = "error", html = "Ocurrio un error con la carga de datos" });
            }
        }

        public JsonResult GetPolizaConfig(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id_poliza", Convert.ToInt32(fc["id_tipo_poliza"].ToString()));
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                string query = "SELECT pp.orden, pp.tipo_cuenta, cf.codigo cuenta_contable, cf.id_cuenta_contable, pp.lado, pp.origen_monto, " +
                    "   pp.factor, pp.descripcion_template, ci.cve_impuesto, ci.id_impuesto " +
                    "FROM poliza_tipo pt " +
                    "INNER JOIN poliza_partida pp ON pp.id_tipo_poliza = pt.id_tipo_poliza " +
                    "LEFT JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = pp.id_cuenta_contable " +
                    "LEFT JOIN cat_impuestos ci ON ci.id_impuesto = pp.impuesto_id " +
                    "WHERE pp.id_tipo_poliza = @id_poliza " +
                    "ORDER BY pp.orden ASC";
                var partidas = RunQuery(query, parameters);

                query = "SELECT pc.nombre categoria, pt.descripcion, cp.nombre clasificacion, e.nombre empresa, pt.activo, pt.id_tipo_poliza, " +
                    "   e.empresaid, pc.id_categoria, cp.id_clasificacion_poliza " +
                    "FROM poliza_tipo pt " +
                    "INNER JOIN poliza_categoria pc ON pc.id_categoria = pt.id_categoria " +
                    "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = pt.clasificacion_id " +
                    "INNER JOIN empresas e ON e.empresaid = pt.empresa_id " +
                    "WHERE pt.id_tipo_poliza = @id_poliza";
                var infoGeneral = RunQuery(query, parameters);

                query = "SELECT t.tpdoc, t.abreviaturatpdoc, t.tpdoc, pd.id_tipo_poliza, pd.id_poliza_documento, t.idtpdoc, pd.tipo_proceso " +
                    "FROM poliza_documento pd " +
                    "INNER JOIN tpdoc t ON t.idtpdoc = pd.idtpdoc " +
                    "WHERE pd.id_tipo_poliza = @id_poliza";
                var documentosPoliza = RunQuery(query, parameters);

                query = "SELECT idtpdoc, tpdoc, abreviaturatpdoc FROM tpdoc ORDER BY abreviaturatpdoc ASC";
                var documentos = RunQuery(query);

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { partidas, general = infoGeneral, documentosPoliza, documentos });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Contabilidad/GetPolizaConfig");
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", html = "Ocurrio un error con la carga de datos", showCancelButton = false });
            }
        }

        public JsonResult GetDatosComplementarios(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(fc["empresa_id"].ToString()));

            // ---- PARA CADA PARTIDA ----
            string query = "SELECT id_cuenta_contable, codigo, nombre " +
                "FROM cuentas_finanzas cf " +
                "WHERE cf.empresa_id = @empresa_id";
            var cuentas = RunQuery(query, parameters);

            query = "SELECT ci.id_impuesto, ci.cve_impuesto, ci.desc " +
                "FROM cat_impuestos ci";
            var impuestos = RunQuery(query);
            // ----------------------------

            // ---- PARA LA CONFIGURACION GENERAL DE LA POLIZA ----
            query = "SELECT id_categoria, nombre, descripcion, activo " +
                "FROM poliza_categoria";
            var categorias = RunQuery(query);

            query = "SELECT id_tipo_poliza, id_categoria, nombre, descripcion, activo, empresa_id, clasificacion_id " +
                "FROM poliza_tipo pt " +
                "WHERE pt.empresa_id = @empresa_id";
            var tipos = RunQuery(query, parameters);

            query = "SELECT id_clasificacion_poliza, nombre, descripcion " +
                "FROM clasificacion_poliza";
            var clasificaciones = RunQuery(query);
            // -----------------------------------------------------

            query = "SELECT empresaid, rfc, nombre FROM empresas";
            var empresas = RunQuery(query);

            query = "SELECT idtpdoc, tpdoc, abreviaturatpdoc FROM tpdoc";
            var documentos = RunQuery(query);

            Response.StatusCode = (int)HttpStatusCode.OK;
            return Json(new { cuentas, impuestos, categorias, tipos, clasificaciones, empresas, documentos });
        }

        public JsonResult GetCategorias(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT id_categoria, nombre, descripcion, activo " +
                "FROM poliza_categoria";
            var categorias = RunQuery(query);
            return Json(categorias);
        }

        public JsonResult GetClasificaciones()
        {
            string query = "SELECT id_clasificacion_poliza, nombre, descripcion " +
                "FROM clasificacion_poliza";
            var clasificaciones = RunQuery(query);

            return Json(clasificaciones);
        }

        /// <summary>
        /// Catálogos que alimentan los selects de la pantalla: tipos de cuenta, lados,
        /// orígenes de monto y tipos de proceso. Antes venían escritos a mano en el
        /// JavaScript de ConfiguracionPolizas.cshtml; ahora salen de las tablas
        /// poliza_cat_* que crea sql/poliza_catalogos.sql.
        ///
        /// Ojo: solo tipos de proceso crece con un simple INSERT. Los otros tres son
        /// valores de ENUM que además llevan un case en PolizaConfigFactory, así que
        /// dar de alta una clave nueva ahí requiere ALTER TYPE y tocar el motor.
        /// </summary>
        public JsonResult GetCatalogosPoliza()
        {
            try
            {
                string query = "SELECT clave, descripcion, etiqueta_cuenta, requiere_cuenta, requiere_impuesto, " +
                    "   origen_monto_forzado " +
                    "FROM poliza_cat_tipo_cuenta " +
                    "WHERE activo = true " +
                    "ORDER BY orden, clave";
                var tiposCuenta = RunQuery(query);

                query = "SELECT clave, descripcion " +
                    "FROM poliza_cat_lado " +
                    "WHERE activo = true " +
                    "ORDER BY orden, clave";
                var lados = RunQuery(query);

                query = "SELECT clave, descripcion, monto_ejemplo " +
                    "FROM poliza_cat_origen_monto " +
                    "WHERE activo = true " +
                    "ORDER BY orden, clave";
                var origenesMonto = RunQuery(query);

                query = "SELECT clave, descripcion, modulo " +
                    "FROM poliza_cat_tipo_proceso " +
                    "WHERE activo = true " +
                    "ORDER BY orden, descripcion";
                var tiposProceso = RunQuery(query);

                Response.StatusCode = (int)HttpStatusCode.OK;
                return Json(new { tiposCuenta, lados, origenesMonto, tiposProceso });
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new { icon = "error", html = "No se pudieron cargar los catálogos de pólizas: " + ex.Message, showCancelButton = false });
            }
        }

        public JsonResult GetTiposPoliza(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = "SELECT id_tipo_poliza, id_categoria, nombre, descripcion, activo, empresa_id, clasificacion_id " +
                "FROM poliza_tipo pt " +
                "WHERE pt.empresa_id = @empresa_id";
            var tipos = RunQuery(query, parameters);

            query = "SELECT empresaid, rfc, nombre FROM empresas";
            var empresas = RunQuery(query);

            return Json(new { tipos, empresas });
        }
        #endregion
    }
}
