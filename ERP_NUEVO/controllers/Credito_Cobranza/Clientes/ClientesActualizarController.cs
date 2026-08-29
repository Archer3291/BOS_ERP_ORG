using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;

namespace BOS_ERP.Controllers.Credito_Cobranza
{
    /// <summary>
    /// Actualización de un cliente existente. Todo ocurre dentro de una
    /// sola transacción, en este orden:
    ///
    ///   1. datos generales (catclientes)
    ///   2. dirección de facturación (direcciones_facturacion)
    ///   3. correos, teléfonos y tipos de transporte
    /// </summary>
    public partial class ClientesController : Utilities
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ActualizarCliente(IFormCollection fc)
        {
            try
            {
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(fc["id_cliente"].ToString()))
                                return Json(new { success = false, message = "El ID del cliente es obligatorio." });

                            int idCliente = Convert.ToInt32(fc["id_cliente"].ToString());
                            bool esInternacional = fc["es_internacional"].ToString() == "true";

                            var errorDatosGenerales = ValidarDatosGenerales(fc, esInternacional);
                            if (errorDatosGenerales != null) return errorDatosGenerales;

                            ActualizarDatosGenerales(fc, idCliente, esInternacional, conn, tx);

                            var errorFacturacion = ValidarDireccionFacturacion(fc, esInternacional);
                            if (errorFacturacion != null) return errorFacturacion;

                            GuardarDireccionFacturacion(fc, esInternacional, conn, tx);

                            SincronizarCorreos(fc, idCliente, conn, tx);
                            SincronizarTelefonos(fc, idCliente, conn, tx);
                            SincronizarTransportes(fc, idCliente, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new { icon = "success", title = "Cliente actualizado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error al actualizar el cliente", html = "Error al actualizar cliente: " + ex.Message });
            }
        }

        #region Datos generales

        /// <returns>El error a devolver al cliente, o null si todo está bien.</returns>
        private JsonResult ValidarDatosGenerales(IFormCollection fc, bool esInternacional)
        {
            if (string.IsNullOrWhiteSpace(fc["n_cli"].ToString()))
                return Json(new { success = false, message = "El nombre del cliente es obligatorio." });

            if (esInternacional && string.IsNullOrWhiteSpace(fc["tax_id"].ToString()))
                return Json(new { success = false, message = "El campo Tax ID es obligatorio para clientes internacionales." });

            if (!esInternacional && string.IsNullOrWhiteSpace(fc["rfc"].ToString()))
                return Json(new { success = false, message = "El RFC es obligatorio para clientes nacionales." });

            // La dirección llega como ids de catálogo y se convierte a entero
            // para resolverla: sin ellos la conversión reventaría con un error
            // genérico en vez de decir qué falta.
            if (esInternacional && string.IsNullOrWhiteSpace(fc["estado"].ToString()))
                return Json(new { success = false, message = "El estado o provincia del domicilio es obligatorio." });

            if (!esInternacional && string.IsNullOrWhiteSpace(fc["colonia"].ToString()))
                return Json(new { success = false, message = "La colonia del domicilio es obligatoria." });

            return null;
        }

        private void ActualizarDatosGenerales(
            IFormCollection fc, int idCliente, bool esInternacional, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id_cliente", idCliente);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            parameters.Add("cve_cli", GetString(fc["cve_cli"].ToString(), null));
            parameters.Add("n_cli", GetString(fc["n_cli"].ToString(), null));
            parameters.Add("rfc", GetString(fc["rfc"].ToString(), null));
            parameters.Add("curp_cli", GetString(fc["curp_cli"].ToString(), null));
            parameters.Add("idf", GetString(fc["tax_id"].ToString(), null));
            parameters.Add("cp", GetString(fc["cp"].ToString(), null));
            parameters.Add("dir", GetString(fc["dir"].ToString(), null));
            parameters.Add("numero_int_domicilio", fc["numero_int"].ToString() == "" ? null : GetInt(fc["numero_int"].ToString(), null));
            parameters.Add("numero_ext_domiclio", GetInt(fc["numero_ext"].ToString(), null));
            parameters.Add("lim_crd", GetDecimal(fc["lim_crd"].ToString(), null));
            parameters.Add("pl_crd", GetDecimal(fc["pl_crd"].ToString(), null));
            parameters.Add("dto", GetString(fc["dto"].ToString(), null));
            parameters.Add("dto2", GetString(fc["dto2"].ToString(), null));
            parameters.Add("com_por", GetDecimal(fc["com_por"].ToString(), null));
            parameters.Add("coment1", GetString(fc["coment1"].ToString(), null));
            parameters.Add("bco_cli1", GetString(fc["bco_cli1"].ToString(), null));
            parameters.Add("cta_bco_cli1", GetString(fc["cta_bco_cli1"].ToString(), null));
            parameters.Add("bco_cli2", GetString(fc["bco_cli2"].ToString(), null));
            parameters.Add("cta_bco_cli2", GetString(fc["cta_bco_cli2"].ToString(), null));
            parameters.Add("bco_cli3", GetString(fc["bco_cli3"].ToString(), null));
            parameters.Add("cta_bco_cli3", GetString(fc["cta_bco_cli3"].ToString(), null));
            parameters.Add("es_internacional", Convert.ToBoolean(fc["es_internacional"].ToString(), null));
            parameters.Add("cve_vdr", GetString(fc["cve_vdr"].ToString(), null));
            parameters.Add("incoterm", fc["incoterm"].ToString() == "" ? null : GetInt(fc["incoterm"].ToString()));
            parameters.Add("clasificacion", string.IsNullOrEmpty(fc["clasificacion"].ToString()) ? (object)DBNull.Value : fc["clasificacion"].ToString());
            parameters.Add("estatus_cliente", string.IsNullOrEmpty(fc["estatus_cliente"].ToString()) ? (object)DBNull.Value : fc["estatus_cliente"].ToString());

            // La dirección capturada llega como ids de catálogo: hay que
            // expandirla a los nombres que guarda catclientes.
            if (esInternacional)
            {
                var direcciones = ResolverDireccionInternacional(
                    fc["estado"].ToString(), fc["municipio"].ToString(), conn, tx);

                parameters.Add("pob", direcciones["estado"].ToString());
                parameters.Add("mpio", GetString(direcciones["municipio"]));
                parameters.Add("cve_pais", GetString(direcciones["pais"]));
                parameters.Add("cve_est", DBNull.Value);
                parameters.Add("col", fc["colonia"].ToString());
            }
            else
            {
                var direcciones = ResolverDireccionNacional(
                    Convert.ToInt32(fc["colonia"].ToString()), false, conn, tx);

                parameters.Add("cve_est", direcciones["cve_estado"].ToString());
                parameters.Add("pob", direcciones["estado"].ToString());
                parameters.Add("mpio", direcciones["municipio"].ToString());
                parameters.Add("col", direcciones["colonia"].ToString());
                parameters.Add("cve_pais", "MEX");
            }

            string query = "UPDATE catclientes SET rfc = @rfc, curp_cli = @curp_cli, n_cli = @n_cli, " +
                "   idf = @idf, cve_pais = @cve_pais, pob = @pob, cve_est = @cve_est, mpio = @mpio, municipio = @mpio, cp = @cp, " +
                "   col = @col, dir = @dir, numero_int_domicilio = @numero_int_domicilio, numero_ext_domiclio = @numero_ext_domiclio, " +
                "   lim_crd = @lim_crd, pl_crd = @pl_crd, dto = @dto, dto2 = @dto2, com_por = @com_por, coment1 = @coment1, " +
                "   bco_cli1 = @bco_cli1, cta_bco_cli1 = @cta_bco_cli1, bco_cli2 = @bco_cli2, cta_bco_cli2 = @cta_bco_cli2, " +
                "   bco_cli3 = @bco_cli3, cta_bco_cli3 = @cta_bco_cli3, es_internacional = @es_internacional, cve_vdr = @cve_vdr, " +
                "   incoterm_id = @incoterm, clasificacion = @clasificacion::clasificacion_cliente, estatus_cliente = @estatus_cliente::estatus_cliente_tipo " +
                "WHERE id_cliente = @id_cliente AND empresa_id = @empresa_id";

            RunUpdate(query, parameters, false, conn, tx);
        }

        #endregion

        #region Dirección de facturación

        /// <returns>El error a devolver al cliente, o null si todo está bien.</returns>
        private JsonResult ValidarDireccionFacturacion(IFormCollection fc, bool esInternacional)
        {
            var obligatorios = new List<(string Campo, string Detalle)>
            {
                ("fact_dir",            "La calle de facturación es obligatoria."),
                ("fact_cp",             "El código postal de facturación es obligatorio."),
                ("fact_uso_cfdi",       "El uso CFDI es obligatorio."),
                ("fact_regimen_fiscal", "El régimen fiscal es obligatorio."),
                ("fact_razon_social",   "La razón social es obligatoria."),
                ("fact_forma_pago",     "La forma de pago es obligatoria."),
                ("fact_estado",         "El estado es obligatorio."),
            };

            // En nacionales la colonia identifica toda la dirección fiscal
            // (de ella se derivan municipio, cp y estado), así que sin ella
            // no se puede resolver. En el extranjero se captura a mano.
            if (!esInternacional)
                obligatorios.Add(("fact_colonia", "La colonia de facturación es obligatoria."));

            foreach (var (campo, detalle) in obligatorios)
            {
                if (string.IsNullOrWhiteSpace(fc[campo].ToString()))
                    return Json(new
                    {
                        icon = "error",
                        title = "Error al agregar la direccion de facturacion",
                        html = "Se actualizaron los datos generales del cliente. Fallo al agregar la direccion de facturacion. " + detalle
                    });
            }

            return null;
        }

        /// <summary>
        /// Inserta o actualiza la dirección fiscal del cliente
        /// (entidad_tipo 3, consecutivo 1).
        /// </summary>
        private void GuardarDireccionFacturacion(
            IFormCollection fc, bool esInternacional, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            // El CFDI nacional exige el número de municipio, no su nombre.
            var direcciones = esInternacional
                ? ResolverDireccionInternacional(fc["fact_estado"].ToString(), fc["fact_municipio"].ToString(), conn, tx)
                : ResolverDireccionNacional(Convert.ToInt32(fc["fact_colonia"].ToString()), true, conn, tx);

            var parameters = new Dictionary<string, object>();
            parameters.Add("entidad_tipo", 3);
            parameters.Add("entidad_clave", GetString(fc["cve_cli"].ToString()));
            parameters.Add("consecutivo", 1);
            parameters.Add("calle", GetString(fc["fact_dir"].ToString()));
            parameters.Add("no_exterior", GetInt(fc["fact_numero_ext"].ToString()));
            parameters.Add("no_interior", fc["fact_numero_int"].ToString() == "" ? null : GetInt(fc["fact_numero_int"].ToString()));
            parameters.Add("colonia", esInternacional ? GetString(fc["colonia"].ToString()) : GetString(direcciones["colonia"]));
            parameters.Add("municipio", GetString(direcciones["municipio"]));
            parameters.Add("estado", esInternacional ? GetString(direcciones["estado"]) : GetString(direcciones["cve_estado"]));
            parameters.Add("pais", esInternacional ? GetString(direcciones["pais"]) : "MEX");
            parameters.Add("codigo_postal", GetString(fc["fact_cp"].ToString()));
            parameters.Add("forma_pago", GetString(fc["fact_forma_pago"].ToString()));
            parameters.Add("uso_sugerido", GetString(fc["fact_uso_cfdi"].ToString()));
            parameters.Add("extranjero", esInternacional);
            parameters.Add("regimen_fiscal", GetString(fc["fact_regimen_fiscal"].ToString()));
            parameters.Add("razon_social", GetString(fc["fact_razon_social"].ToString()));
            parameters.Add("fecha_creacion", DateTime.Now);
            parameters.Add("fecha_actualizacion", DateTime.Now);
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

            string query = @"
                INSERT INTO direcciones_facturacion (
                    entidad_tipo, entidad_clave, consecutivo, calle,
                    no_exterior, no_interior, colonia, municipio,
                    estado, pais, codigo_postal, forma_pago,
                    uso_sugerido, extranjero, regimen_fiscal,
                    razon_social, fecha_creacion, fecha_actualizacion, empresa_id
                ) VALUES (
                    @entidad_tipo, @entidad_clave, @consecutivo, @calle,
                    @no_exterior, @no_interior, @colonia, @municipio,
                    @estado, @pais, @codigo_postal, @forma_pago,
                    @uso_sugerido, @extranjero, @regimen_fiscal,
                    @razon_social, @fecha_creacion, @fecha_actualizacion, @empresa
                )
                ON CONFLICT (entidad_clave, consecutivo, empresa_id)
                DO UPDATE SET
                    calle = EXCLUDED.calle,
                    no_exterior = EXCLUDED.no_exterior,
                    no_interior = EXCLUDED.no_interior,
                    colonia = EXCLUDED.colonia,
                    municipio = EXCLUDED.municipio,
                    estado = EXCLUDED.estado,
                    pais = EXCLUDED.pais,
                    codigo_postal = EXCLUDED.codigo_postal,
                    forma_pago = EXCLUDED.forma_pago,
                    uso_sugerido = EXCLUDED.uso_sugerido,
                    extranjero = EXCLUDED.extranjero,
                    regimen_fiscal = EXCLUDED.regimen_fiscal,
                    razon_social = EXCLUDED.razon_social,
                    fecha_actualizacion = NOW(),
                    empresa_id = @empresa;
                ";

            RunUpdate(query, parameters, false, conn, tx);
        }

        #endregion

        #region Colecciones del cliente (contacto y transportes)

        private void SincronizarCorreos(IFormCollection fc, int idCliente, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            ReemplazarColeccion(
                fc["correos"].ToString(), idCliente, conn, tx,
                tabla: "correos_cliente",
                columna: "correo",
                claveJson: "correo");
        }

        private void SincronizarTelefonos(IFormCollection fc, int idCliente, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            ReemplazarColeccion(
                fc["telefonos"].ToString(), idCliente, conn, tx,
                tabla: "telefonos_cliente",
                columna: "telefono",
                claveJson: "telefono");
        }

        private void SincronizarTransportes(IFormCollection fc, int idCliente, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            ReemplazarColeccion(
                fc["transportes"].ToString(), idCliente, conn, tx,
                tabla: "transportes_clientes_internacionales",
                columna: "tipo_transporte",
                claveJson: "tipo");
        }

        /// <summary>
        /// Borra los registros actuales del cliente y vuelve a insertar los
        /// que llegaron en el JSON. Si el JSON viene vacío no se toca nada,
        /// para no borrar por accidente lo que ya estaba guardado.
        /// </summary>
        private void ReemplazarColeccion(
            string json, int idCliente, NpgsqlConnection conn, NpgsqlTransaction tx,
            string tabla, string columna, string claveJson)
        {
            if (string.IsNullOrEmpty(json)) return;

            var elementos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
            if (elementos.Count == 0) return;

            var parameters = new Dictionary<string, object>();
            parameters.Add("id_cliente", idCliente);
            RunQuery($"DELETE FROM {tabla} WHERE cliente_id = @id_cliente", parameters, false, conn, tx);

            string insert = $"INSERT INTO {tabla} (cliente_id, {columna}) VALUES (@id_cliente, @valor)";

            foreach (var elemento in elementos)
            {
                parameters = new Dictionary<string, object>();
                parameters.Add("id_cliente", idCliente);
                parameters.Add("valor", elemento[claveJson]);

                RunUpdate(insert, parameters, false, conn, tx);
            }
        }

        #endregion
    }
}
