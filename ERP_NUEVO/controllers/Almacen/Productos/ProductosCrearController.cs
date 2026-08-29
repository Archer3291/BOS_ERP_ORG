using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Almacen
{
    /// <summary>
    /// Alta y actualización de producto. El endpoint es el mismo para
    /// ambas: decide por la existencia de la clave dentro de la empresa.
    ///
    /// Un guardado toca tres tablas, siempre en la misma transacción:
    ///   catproductos       datos del producto
    ///   catrelacion        equivalencia con el catálogo del SAT
    ///   frac_arancelarias  pesos y fracción arancelaria
    /// </summary>
    public partial class ProductosController : Utilities
    {
        [Route("Almacen/producto/CrearProducto")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult CrearProducto(IFormCollection fc)
        {
            try
            {
                var parameters = LeerParametrosProducto(fc);
                string accion;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            bool existe = ExisteProducto(parameters, conn, tx);

                            int idProducto = existe
                                ? ActualizarProducto(parameters, conn, tx)
                                : InsertarProducto(parameters, conn, tx);

                            parameters.Add("producto", GetInt(idProducto));

                            UpsertSatRelacion(parameters, conn, tx);
                            GuardarImagen(parameters, conn, tx);

                            accion = existe ? "actualizado" : "creado";
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { success = true, message = $"Producto {accion} exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Ocurrio un error inesperado\n{ex.Message}" });
            }
        }

        #region Lectura del formulario

        private Dictionary<string, object> LeerParametrosProducto(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();

            // ── Identificación ───────────────────────────────────────────────
            parameters.Add("codigo", GetString(fc["nombreProducto"].ToString()));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            // ── General ──────────────────────────────────────────────────────
            parameters.Add("descripcion", GetString(fc["descripcionProducto"].ToString()));
            parameters.Add("linea", GetString(fc["lineaProducto"].ToString()));
            parameters.Add("tipo", GetString(fc["tipoProducto"].ToString()));
            parameters.Add("grupo", GetString(fc["grupoProducto"].ToString()));
            parameters.Add("naturaleza", GetString(fc["naturalezaProducto"].ToString()));
            parameters.Add("unidad", GetString(fc["unidadProducto"].ToString()));
            parameters.Add("stat", GetBool(fc["estadoProducto"].ToString()) ? "A" : "I");

            // ── Precios ──────────────────────────────────────────────────────
            parameters.Add("precio1", GetDecimal(fc["precioGuadalajara"].ToString()));
            parameters.Add("precio2", GetDecimal(fc["precioD3"].ToString()));
            parameters.Add("precio3", GetDecimal(fc["precioD2"].ToString()));
            parameters.Add("precio4", GetDecimal(fc["precioD1"].ToString()));
            parameters.Add("precio5", GetDecimal(fc["precioMayoreoGDL"].ToString()));
            parameters.Add("precio6", GetDecimal(fc["precioMedioMayoreo"].ToString()));
            parameters.Add("precio7", GetDecimal(fc["precioFrecuente"].ToString()));
            parameters.Add("precio8", GetDecimal(fc["precioMostrador"].ToString()));

            // ── Pesos ────────────────────────────────────────────────────────
            parameters.Add("peso", GetDecimal(fc["peso"].ToString()));
            parameters.Add("peso2", GetDecimal(fc["peso2"].ToString()));
            parameters.Add("peso3", GetDecimal(fc["peso3"].ToString()));

            // Se usa "fracc" del formulario: trae el valor completo con los
            // dígitos verificadores, a diferencia del fr_ar corto.
            parameters.Add("fraccion_arancelaria", GetString(fc["fracc"].ToString()));

            // ── SAT CFDI ────────────────────────────────────────────────────
            parameters.Add("prod_sat", GetString(fc["prod_sat"].ToString()));
            parameters.Add("ud_sat", GetString(fc["unidadMedida"].ToString()));
            parameters.Add("ud_sec", GetString(fc["unidadSecundaria"].ToString(), ""));
            parameters.Add("iva_ex", GetString(fc["ivaExento"].ToString(), "N"));
            parameters.Add("obj_impto", GetString(fc["objetoImpuesto"].ToString()));
            parameters.Add("clave_alternativa", GetString(fc["claveAlternativa"].ToString()));
            parameters.Add("clave_contratipo", GetString(fc["claveContratipo"].ToString()));

            return parameters;
        }

        #endregion

        #region catproductos

        private bool ExisteProducto(Dictionary<string, object> parameters, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string query = "SELECT COUNT(*) FROM catproductos WHERE cve_prod = @codigo AND empresa_id = @empresa_id";
            return Convert.ToInt32(RunScalar(query, parameters, false, conn, tx)) > 0;
        }

        private int ActualizarProducto(Dictionary<string, object> parameters, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string query = "UPDATE catproductos SET descr_prod = @descripcion, stat = @stat, lin_prod = @linea, tp = @tipo, gpo = @grupo, " +
                "   fmcan = @naturaleza, udm = @unidad, pv1 = @precio1, pv2 = @precio2, pv3 = @precio3, pv4 = @precio4, pv5 = @precio5, " +
                "   pv6 = @precio6, pv7 = @precio7, pv8 = @precio8, peso_prod = @peso, fr_ar = @fraccion_arancelaria, cve_secundaria = @clave_alternativa, cve_contratipo = @clave_contratipo " +
                "WHERE cve_prod = @codigo AND empresa_id = @empresa_id";
            RunUpdate(query, parameters, false, conn, tx);

            query = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @codigo AND empresa_id = @empresa_id";
            return Convert.ToInt32(RunScalar(query, parameters));
        }

        private int InsertarProducto(Dictionary<string, object> parameters, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string query = "INSERT INTO catproductos ( " +
                "   cve_prod, descr_prod, lin_prod, tp, gpo, fmcan, udm, stat, pv1, pv2, pv3, pv4, pv5, pv6, pv7, pv8, peso_prod, " +
                "   fr_ar, cve_secundaria, cve_contratipo, empresa_id " +
                ") " +
                "VALUES (@codigo, @descripcion, @linea, @tipo, @grupo, @naturaleza, @unidad, @stat, @precio1, @precio2, @precio3, @precio4, @precio5, " +
                "   @precio6, @precio7, @precio8, @peso, @fraccion_arancelaria, @clave_alternativa, @clave_contratipo, @empresa_id " +
                ") RETURNING id_catproductos";

            return Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
        }

        /// <summary>
        /// Sube la imagen y guarda su ruta. Si no vino archivo, se deja
        /// la que ya tuviera el producto.
        /// </summary>
        private void GuardarImagen(Dictionary<string, object> parameters, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            IFormFile archivo = Request.Form.Files["imagenProducto"];
            if (archivo == null || archivo.Length == 0) return;

            var ext = Path.GetExtension(archivo.FileName);
            var ruta = "content/imagenes_producto/";
            var uuid = Guid.NewGuid().ToString();
            UploadFormFileToPath(ruta, archivo, uuid, ext);

            parameters["ruta"] = $"{ruta}{uuid}{ext}";

            string query = "UPDATE catproductos SET n_img = @ruta WHERE cve_prod = @codigo AND empresa_id = @empresa_id";
            RunUpdate(query, parameters, false, conn, tx);
        }

        #endregion

        #region catrelacion y frac_arancelarias

        /// <summary>
        /// Sincroniza la equivalencia con el catálogo del SAT y la
        /// fracción arancelaria del producto. Inserta o actualiza según
        /// exista ya el registro.
        /// </summary>
        private void UpsertSatRelacion(Dictionary<string, object> parameters, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            try
            {
                UpsertRelacionSat(parameters, conn, tx);
                UpsertFraccionArancelaria(parameters, conn, tx);

                LogErrorHelper.RegistrarLog("Producto SAT", "SIN_FOLIO",
                    $"Upsert relacion: {parameters["codigo"]}", nivel: "DEBUG");
            }
            catch (Exception ex)
            {
                throw new Exception($"Ocurrio un error al guardar el producto: {ex.GetType().ToString()}");
            }
        }

        private void UpsertRelacionSat(Dictionary<string, object> parameters, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string query = "SELECT COUNT(*) FROM catrelacion WHERE prod_kepler = @codigo";
            int existe = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

            query = existe == 0
                ? "INSERT INTO catrelacion (prod_kepler, prod_sat, ud_sat, iva_ex, ud_sec, obj_impto) " +
                  "VALUES (@codigo, @prod_sat, @ud_sat, @iva_ex, @ud_sec, @obj_impto)"
                : "UPDATE catrelacion SET prod_sat = @prod_sat, ud_sat = @ud_sat, iva_ex = @iva_ex, ud_sec = @ud_sec, obj_impto = @obj_impto " +
                  "WHERE prod_kepler = @codigo";

            RunUpdate(query, parameters, false, conn, tx);
        }

        private void UpsertFraccionArancelaria(Dictionary<string, object> parameters, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string query = "SELECT COUNT(*) FROM frac_arancelarias WHERE cve_prod = @codigo";
            int existe = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

            query = existe == 0
                ? "INSERT INTO frac_arancelarias ( " +
                  "   cve_prod, \"desc\", gpo_marca, peso, status, frac, prod_id, gpo_marca2, gpo_marca3, peso2, peso3) " +
                  "VALUES ( " +
                  "   @codigo, @descripcion, '', @peso, @stat, @fraccion_arancelaria, @producto, '', '', @peso2, @peso3" +
                  ")"
                : "UPDATE frac_arancelarias SET \"desc\" = @codigo, gpo_marca = '', peso = @peso, status = @stat, frac =  @fraccion_arancelaria, " +
                  "   prod_id = @producto, gpo_marca2 = '', gpo_marca3 = '', peso2 = @peso2, peso3 = @peso3 " +
                  "WHERE cve_prod = @codigo";

            RunUpdate(query, parameters, false, conn, tx);
        }

        #endregion
    }
}
