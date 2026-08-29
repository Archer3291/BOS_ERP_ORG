using Npgsql;
using BOS_ERP.Models;
using BOS_ERP.Services;
using System.Configuration;
using System.Data;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Operaciones
{
    public class ProveedorController : Utilities
    {
        private readonly ProveedorSyncService _proveedorSyncService;

        public ProveedorController(ProveedorSyncService proveedorSyncService)
        {
            _proveedorSyncService = proveedorSyncService;
        }

        #region proceso normal de crear un proveedor nuevo
        public JsonResult getProveedores(IFormCollection fc)
        {
            string nombre = fc["nombre"].ToString();
            int page = Convert.ToInt32(fc["page"].ToString());
            int pageSize = Convert.ToInt32(fc["pageSize"].ToString());
            string sortColumn = fc["sortColumn"].ToString();
            string sortDir = fc["sortDir"].ToString();
            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre ?? "" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
                { "sortColumn", sortColumn ?? "" },
                { "sortDir", sortDir ?? "asc" },
            };

            var allowedColumns = new HashSet<string> {
                "cve_prov", "n_prov",
                "dir", "col", "rfc",
            };

            if (!allowedColumns.Contains(sortColumn))
                sortColumn = "cve_prov";

            string where = "";

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where = " AND ( " +
                    "   cve_prov ILIKE '%' || @nombre || '%' " +
                    "   OR n_prov ILIKE '%' || @nombre || '%' " +
                    "   OR rfc ILIKE '%' || @nombre || '%' " +
                    ") ";
            }

            string query = "SELECT id_prov, cve_prov, n_prov, dir, col, pob, tel, rfc " +
                "FROM catproveedores " +
                $"WHERE id_empresa = @empresa {where} " +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var categories = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM catproveedores " +
                $"WHERE id_empresa = @empresa ";
            var total = RunScalar(query, parameters);

            return Json(new { data = categories, total });
        }

        [HttpGet]
        public JsonResult GetByClave(string cve_prov)
        {
            try
            {
                string query = "SELECT * FROM catproveedores WHERE cve_prov = @cve_prov AND id_empresa = @id_empresa";
                var result = RunQuery(query, new Dictionary<string, object> { { "cve_prov", cve_prov }, { "id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } });

                if (result.Count == 0)
                    return Json(new { success = false, message = "Proveedor no encontrado." });

                return Json(new { success = true, proveedor = result[0] });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Create(ProveedorModel prov)
        {
            var parameters = new Dictionary<string, object>();

            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new
                    {
                        success = false,
                        errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                    });
                }

                // Validación: verificar si ya existe por clave de proveedor
                string query = "SELECT COUNT(*) FROM catproveedores WHERE cve_prov = @cve_prov AND id_empresa = @id_empresa";
                parameters.Add("cve_prov", prov.Cve_Prov);
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) > 0)
                {
                    return Json(new { success = false, message = "El proveedor ya existe con esa clave." });
                }

                // Insertar nuevo proveedor
                query = @"
                    INSERT INTO catproveedores (
                        cve_prov, n_prov, dir, col, pob, cp, rfc,
                        cve_agcy_cpr, cve_gpo, cve_tp_prov, lim_crd, pl_crd, dto,
                        coment1, cve_pais, cve_edo, cve_mpio, uso_cfdi, fp, mdp, cve_pais_prov, id_empresa
                    ) VALUES (
                        @cve_prov, @n_prov, @dir, @col, @pob, @cp, @rfc,
                        @cve_agcy_cpr, @cve_gpo, @cve_tp_prov, @lim_crd, @pl_crd, @dto,
                        @coment1, @cve_pais, @cve_edo, @cve_mpio, @uso_cfdi, @fp, @mdp, @cve_pais_prov, @id_empresa
                    );
                ";

                parameters.Clear();
                parameters.Add("cve_prov", prov.Cve_Prov);
                parameters.Add("n_prov", prov.N_Prov);
                parameters.Add("dir", prov.Dir);
                parameters.Add("col", prov.Col);
                parameters.Add("pob", prov.Pob);
                parameters.Add("cp", prov.Cp);
                parameters.Add("rfc", prov.Rfc);
                parameters.Add("cve_agcy_cpr", prov.Cve_Agcy_Cpr);
                parameters.Add("cve_gpo", prov.Cve_Gpo);
                parameters.Add("cve_tp_prov", prov.Cve_Tp_Prov);
                parameters.Add("lim_crd", prov.Lim_Crd ?? (object)DBNull.Value);
                parameters.Add("pl_crd", prov.Pl_Crd ?? (object)DBNull.Value);
                parameters.Add("dto", prov.Dto);
                parameters.Add("coment1", prov.Coment1);
                parameters.Add("cve_pais", prov.Cve_Pais);
                parameters.Add("cve_edo", prov.Cve_Edo);
                parameters.Add("cve_mpio", prov.Cve_Mpio);
                parameters.Add("uso_cfdi", prov.Uso_Cfdi);
                parameters.Add("fp", prov.Fp);
                parameters.Add("mdp", prov.Mdp);
                parameters.Add("cve_pais_prov", prov.Cve_Pais_Prov);
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Proveedor creado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al crear el proveedor: " + ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Update(ProveedorModel prov)
        {
            var parameters = new Dictionary<string, object>();

            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new
                    {
                        success = false,
                        errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                    });
                }

                // Validar que el proveedor exista
                string query = "SELECT COUNT(*) FROM catproveedores WHERE cve_prov = @cve_prov AND id_empresa = @id_empresa";
                parameters.Add("cve_prov", prov.Cve_Prov);
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var count = RunScalar(query, parameters);

                if (Convert.ToInt32(count) == 0)
                {
                    return Json(new { success = false, message = "El proveedor no existe." });
                }

                // Actualizar proveedor existente
                query = @"
            UPDATE catproveedores SET
                n_prov = @n_prov,
                dir = @dir,
                col = @col,
                pob = @pob,
                cp = @cp,
                rfc = @rfc,
                cve_agcy_cpr = @cve_agcy_cpr,
                cve_gpo = @cve_gpo,
                cve_tp_prov = @cve_tp_prov,
                lim_crd = @lim_crd,
                pl_crd = @pl_crd,
                dto = @dto,
                coment1 = @coment1,
                cve_pais = @cve_pais,
                cve_edo = @cve_edo,
                cve_mpio = @cve_mpio,
                uso_cfdi = @uso_cfdi,
                fp = @fp,
                mdp = @mdp,
                cve_pais_prov = @cve_pais_prov
            WHERE cve_prov = @cve_prov AND id_empresa = @id_empresa;
        ";

                parameters.Clear();
                parameters.Add("cve_prov", prov.Cve_Prov);
                parameters.Add("n_prov", prov.N_Prov);
                parameters.Add("dir", prov.Dir);
                parameters.Add("col", prov.Col);
                parameters.Add("pob", prov.Pob);
                parameters.Add("cp", prov.Cp);
                parameters.Add("rfc", prov.Rfc);
                parameters.Add("cve_agcy_cpr", prov.Cve_Agcy_Cpr);
                parameters.Add("cve_gpo", prov.Cve_Gpo);
                parameters.Add("cve_tp_prov", prov.Cve_Tp_Prov);
                parameters.Add("lim_crd", prov.Lim_Crd ?? (object)DBNull.Value);
                parameters.Add("pl_crd", prov.Pl_Crd ?? (object)DBNull.Value);
                parameters.Add("dto", prov.Dto);
                parameters.Add("coment1", prov.Coment1);
                parameters.Add("cve_pais", prov.Cve_Pais);
                parameters.Add("cve_edo", prov.Cve_Edo);
                parameters.Add("cve_mpio", prov.Cve_Mpio);
                parameters.Add("uso_cfdi", prov.Uso_Cfdi);
                parameters.Add("fp", prov.Fp);
                parameters.Add("mdp", prov.Mdp);
                parameters.Add("cve_pais_prov", prov.Cve_Pais_Prov);
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                RunUpdate(query, parameters);

                return Json(new { success = true, message = "Proveedor actualizado exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al actualizar el proveedor: " + ex.Message });
            }
        }
        public JsonResult ContactosCreate(ContactoProveedorModel cpm, List<ContactoProveedorCorreoModel> correos, List<ContactoProveedorTelefonoModel> telefonos)

        {
            var parameters = new Dictionary<string, object>();
            var _parameters = new List<Dictionary<string, object>>();
            try
            {
                string insertQuery = @"
            INSERT INTO contactos_prov (nombre, puesto, comentarios, prov_id)
            VALUES (@nombre, @puesto, @comentarios, @prov_id)
            RETURNING id_contactos_prov;";

                parameters.Add("nombre", cpm.Nombre);
                parameters.Add("puesto", cpm.Puesto);
                parameters.Add("comentarios", cpm.Comentarios);
                parameters.Add("prov_id", int.Parse(cpm.ProvId));

                var newContactoId = RunScalar(insertQuery, parameters);
                if (Convert.ToInt32(newContactoId) <= 0)
                {
                    return Json(new { success = false, message = "No se pudo obtener el ID del nuevo contacto." });
                }

                if (correos != null && correos.Any())
                {
                    string insertCorreo = "INSERT INTO contactos_prov_correos (contactos_prov_id, correo) VALUES (@id, @correo);";
                    foreach (var correo in correos)
                    {
                        if (!string.IsNullOrWhiteSpace(correo.Correo))
                        {
                            parameters = new Dictionary<string, object>();
                            parameters.Add("id", newContactoId);
                            parameters.Add("correo", correo.Correo);
                            _parameters.Add(parameters);
                        }
                    }
                    RunUpdate(insertCorreo, _parameters);
                }

                if (telefonos != null && telefonos.Any())
                {
                    _parameters.Clear();
                    string insertTel = "INSERT INTO contactos_prov_tel (contacto_prov_id, tel) VALUES (@id, @tel);";
                    foreach (var tel in telefonos)
                    {
                        if (!string.IsNullOrWhiteSpace(tel.Tel))
                        {
                            parameters = new Dictionary<string, object>();
                            parameters.Add("id", newContactoId);
                            parameters.Add("tel", tel.Tel);
                            _parameters.Add(parameters);
                        }
                    }
                    RunUpdate(insertTel, _parameters);
                }

                return Json(new { success = true, message = "Contacto creado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult ContactosEdit(ContactoProveedorModel cpm, List<ContactoProveedorCorreoModel> correos, List<ContactoProveedorTelefonoModel> telefonos)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                var _parameters = new List<Dictionary<string, object>>();
                int idContacto = cpm.IdContactosProv ?? 0;
                if (idContacto <= 0)
                    return Json(new { success = false, message = "ID de contacto inválido." });

                // 1. Actualizar datos principales del contacto
                string updateQuery = "UPDATE contactos_prov " +
                                    "SET nombre = @nombre, " +
                                    "    puesto = @puesto, " +
                                    "    comentarios = @comentarios " +
                                    "WHERE id_contactos_prov = @id;";

                parameters.Add("id", idContacto);
                parameters.Add("nombre", cpm.Nombre);
                parameters.Add("puesto", cpm.Puesto);
                parameters.Add("comentarios", cpm.Comentarios);

                RunUpdate(updateQuery, parameters);

                // 2. Eliminar correos y teléfonos anteriores (para reemplazar completamente)
                RunUpdate("DELETE FROM contactos_prov_correos WHERE contactos_prov_id = @id;", new Dictionary<string, object> { { "id", idContacto } });
                RunUpdate("DELETE FROM contactos_prov_tel WHERE contacto_prov_id = @id;", new Dictionary<string, object> { { "id", idContacto } });

                // 3. Insertar nuevos correos
                if (correos != null && correos.Any())
                {
                    string insertCorreo = "INSERT INTO contactos_prov_correos (contactos_prov_id, correo) VALUES (@id, @correo);";

                    foreach (var correo in correos.Where(c => !string.IsNullOrWhiteSpace(c.Correo)))
                    {
                        parameters = new Dictionary<string, object>();
                        parameters.Add("id", idContacto);
                        parameters.Add("correo", correo.Correo);
                        _parameters.Add(parameters);
                    }

                    RunUpdate(insertCorreo, _parameters);
                }

                // 4. Insertar nuevos teléfonos
                if (telefonos != null && telefonos.Any())
                {
                    _parameters.Clear();
                    string insertTel = "INSERT INTO contactos_prov_tel (contacto_prov_id, tel) VALUES (@id, @tel);";

                    foreach (var tel in telefonos.Where(t => !string.IsNullOrWhiteSpace(t.Tel)))
                    {
                        parameters = new Dictionary<string, object>();
                        parameters.Add("id", idContacto);
                        parameters.Add("tel", tel.Tel);
                        _parameters.Add(parameters);
                    }

                    RunUpdate(insertTel, _parameters);
                }

                return Json(new { success = true, message = "Contacto actualizado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al editar contacto: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetContactosByProveedor(int id_prov)
        {
            try
            {
                // 1. Obtener ID del proveedor desde clave
                string queryIdProv = "SELECT id_prov FROM catproveedores WHERE id_prov = @id_prov AND id_empresa = @id_empresa";
                var provResult = RunQuery(queryIdProv, new Dictionary<string, object> { { "id_prov", id_prov }, { "id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } });

                if (provResult.Count == 0)
                    return Json(new { success = false, message = "Proveedor no encontrado." });

                int prov_id = Convert.ToInt32(provResult[0]["id_prov"]);

                // 2. Obtener contactos principales
                string queryContactos = @"
            SELECT id_contactos_prov, nombre, puesto, comentarios 
            FROM contactos_prov 
            WHERE prov_id = @prov_id";

                var contactos = RunQuery(queryContactos, new Dictionary<string, object> { { "prov_id", prov_id } });

                // 3. Obtener correos y teléfonos por cada contacto
                foreach (var contacto in contactos)
                {
                    int contactoId = Convert.ToInt32(contacto["id_contactos_prov"]);

                    // Correos
                    string queryCorreos = "SELECT correo FROM contactos_prov_correos WHERE contactos_prov_id = @id";
                    var correos = RunQuery(queryCorreos, new Dictionary<string, object> { { "id", contactoId } })
                                    .Select(c => c["correo"].ToString()).ToList();
                    contacto["correos"] = correos;

                    // Teléfonos
                    string queryTelefonos = "SELECT tel FROM contactos_prov_tel WHERE contacto_prov_id = @id";
                    var telefonos = RunQuery(queryTelefonos, new Dictionary<string, object> { { "id", contactoId } })
                                    .Select(t => t["tel"].ToString()).ToList();
                    contacto["telefonos"] = telefonos;
                }

                return Json(new { success = true, contactos = contactos });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        public JsonResult DatosFormulario()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            var parameters = new Dictionary<string, object>();
            parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT id, nombre FROM categorias_prov";
            result.Add("categorias", RunQuery(query));
            query = "SELECT id_f_pago, descripcion FROM cat_f_pago";
            result.Add("formaspago", RunQuery(query));
            query = "SELECT id_mdp, cve_mdp, descripcion FROM mdp";
            result.Add("mdp", RunQuery(query));
            query = "SELECT clave, descripcion FROM catusocfdi";
            result.Add("catusocfdi", RunQuery(query));
            query = "SELECT id, nombre FROM vendedores";
            result.Add("vendedores", RunQuery(query));
            query = "SELECT id_pais, nombre FROM paises";
            result.Add("paises", RunQuery(query));
            query = "SELECT " +
                "   CASE WHEN SPLIT_PART(cve_prov, '-', 2)::int >= 9999 THEN " +
                "       LPAD((SPLIT_PART(cve_prov, '-', 1)::int + 1)::text, 2, '0') || '-0001' " +
                "   ELSE " +
                "       LPAD(SPLIT_PART(cve_prov, '-', 1), 2, '0') || '-' || " +
                "       LPAD((SPLIT_PART(cve_prov, '-', 2)::int + 1)::text, 4, '0') " +
                "   END AS siguiente_cve_prov " +
                "FROM catproveedores " +
                "WHERE cve_prov ~ '^[0-9]+-[0-9]+$'  AND id_empresa = @id_empresa " +
                "ORDER BY SPLIT_PART(cve_prov, '-', 1)::int DESC, SPLIT_PART(cve_prov, '-', 2)::int DESC " +
                "LIMIT 1;";
            result.Add("tproveedores", RunQuery(query, parameters));

            // Ya NO cargamos estados ni municipios aquí
            return Json(new { success = true, data = result });
        }

        // Nuevo método para obtener estados por país
        public JsonResult GetEstadosByPais(int paisId)
        {
            try
            {
                string query = "SELECT id_estado, nombre, pais_id FROM estados WHERE pais_id = @paisId";
                var estados = RunQuery(query, new Dictionary<string, object> { { "paisId", paisId } });
                return Json(new { success = true, estados = estados });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Nuevo método para obtener municipios por estado
        public JsonResult GetMunicipiosByEstado(int estadoId)
        {
            try
            {
                string query = "SELECT id_municipio, nombre, estado_id FROM municipios WHERE estado_id = @estadoId";
                var municipios = RunQuery(query, new Dictionary<string, object> { { "estadoId", estadoId } });
                return Json(new { success = true, municipios = municipios });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public JsonResult SiguienteFolio(int cve_tp_prov)
        {
            try
            {
                string query = "";
                var parameters = new Dictionary<string, object>();
                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                if (cve_tp_prov == 1) // Nacional
                {
                    query = "SELECT " +
                            "   CASE WHEN SPLIT_PART(cve_prov, '-', 2)::int >= 9999 THEN " +
                            "       LPAD((SPLIT_PART(cve_prov, '-', 1)::int + 1)::text, 2, '0') || '-0001' " +
                            "   ELSE " +
                            "       LPAD(SPLIT_PART(cve_prov, '-', 1), 2, '0') || '-' || " +
                            "       LPAD((SPLIT_PART(cve_prov, '-', 2)::int + 1)::text, 4, '0') " +
                            "   END AS siguiente_cve_prov " +
                            "FROM catproveedores " +
                            "WHERE cve_prov ~ '^01-[0-9]{4}$' AND id_empresa = @id_empresa " + // Proveedores nacionales empiezan con 01-
                            "ORDER BY SPLIT_PART(cve_prov, '-', 1)::int DESC, SPLIT_PART(cve_prov, '-', 2)::int DESC " +
                            "LIMIT 1;";
                }
                else if (cve_tp_prov == 2) // Internacional
                {
                    query = "SELECT " +
                            "   CASE WHEN SPLIT_PART(cve_prov, '-', 2)::int >= 9999 THEN " +
                            "       LPAD((SPLIT_PART(cve_prov, '-', 1)::int + 1)::text, 2, '0') || '-0001' " +
                            "   ELSE " +
                            "       LPAD(SPLIT_PART(cve_prov, '-', 1), 2, '0') || '-' || " +
                            "       LPAD((SPLIT_PART(cve_prov, '-', 2)::int + 1)::text, 4, '0') " +
                            "   END AS siguiente_cve_prov " +
                            "FROM catproveedores " +
                            "WHERE cve_prov ~ '^02-[0-9]{4}$' AND id_empresa = @id_empresa " + // Proveedores internacionales empiezan con 02-
                            "ORDER BY SPLIT_PART(cve_prov, '-', 1)::int DESC, SPLIT_PART(cve_prov, '-', 2)::int DESC " +
                            "LIMIT 1;";
                }

                var resultado = RunQuery(query, parameters);

                // Si no hay registros, retornar el inicial según el tipo
                string claveInicial = cve_tp_prov == 1 ? "01-0001" : "02-0001";
                string clave = resultado != null && resultado.Count > 0 ? resultado[0]["siguiente_cve_prov"].ToString() : claveInicial;

                return Json(new { success = true, cve_proveedor = clave });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        #endregion

        #region sincronizar proveedores de kepler
        public JsonResult GetProveedoresKepler(IFormCollection fc)
        {
            int page = Convert.ToInt32(fc["page"].ToString());
            int pageSize = Convert.ToInt32(fc["pageSize"].ToString());
            var parameters = new Dictionary<string, object>();
            string emrpesa = fc["empresa"].ToString();
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            string where = "";

            if (!string.IsNullOrWhiteSpace(fc["nombre"].ToString()))
            {
                parameters.Add("nombre", $"%{fc["nombre"].ToString()}%");
                where = " AND (" +
                    "   c2 ILIKE '%' || @nombre || '%' " +
                    "   OR c3 ILIKE '%' || @nombre || '%' " +
                    "   OR c10 ILIKE '%' || @nombre || '%' " +
                    ") ";
            }

            string query = "SELECT c2 clave, c3 nombre, c4 direccion , c5 colonia, c6 poblacion, c10 rfc, c12 clave_vendedor, c13 clave_grupo, c14 clave_zona, c15 limite_credito, " +
                "   c16 plazo_credito, c17 descento, c18 descuento2, c24 comentario1 " +
                "FROM kdxd k " +
                $"WHERE 1=1 {where} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var proveedores = RunQuery(query, parameters, false, null, null, emrpesa);

            query = "SELECT COUNT(*) " +
                "FROM kdxd k ";

            int total = Convert.ToInt32(RunScalar(query, parameters, false, null, null, emrpesa));

            return Json(new { data = proveedores, total });
        }

        public JsonResult SyncProveedoresAll(string empresa)
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
                        try
                        {
                            if (string.IsNullOrEmpty(empresa))
                                throw new Exception("EmpresaFactura no definida.");

                            var connString = utils._configuration.GetConnectionString(empresa);
                            if (string.IsNullOrWhiteSpace(connString))
                                throw new Exception($"No se encontró la cadena de conexión '{empresa}'.");

                            int? empresaId = GetInt(HttpContext.Session.GetInt32("Empresa"));
                            if (!empresaId.HasValue)
                                throw new Exception("No se encontró la empresa en la sesión.");

                            //ProveedorSyncService service = new ProveedorSyncService();
                            //service.SincronizarProveedor(empresaId, connString, new List<string>(), conn, tx);
                            _proveedorSyncService.SincronizarProveedor(empresaId.Value, connString, new List<string>(), conn, tx);

                            tx.Commit();
                        }

                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        return Json(new { icon = "error", title = "Ocurrio un error con la sincronizacion", html = ex.Message });
                    }
                }
            }

            return Json(new { icon = "success", title = "Proveedores sincrinizados correctamente", html = "Por favor corrobora la informacion de los proveedores sincronizados, revisar y mantener congruencia en los datos" });
        }

        public JsonResult SyncProveedores(string empresa, List<string> claves)
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
                        try
                        {
                            if (claves == null || claves.Count == 0)
                                throw new Exception("Debes seleccionar por lo menos un proveedor para sincronizar.");

                            if (string.IsNullOrEmpty(empresa))
                                throw new Exception("EmpresaFactura no definida.");

                            var connString = utils._configuration.GetConnectionString(empresa);
                            if (string.IsNullOrWhiteSpace(connString))
                                throw new Exception($"No se encontró la cadena de conexión '{empresa}'.");

                            int? empresaId = GetInt(HttpContext.Session.GetInt32("Empresa"));
                            if (!empresaId.HasValue)
                                throw new Exception("No se encontró la empresa en la sesión.");

                            //ProveedorSyncService service = new ProveedorSyncService();
                            //service.SincronizarProveedor(empresaId, connString, claves, conn, tx);
                            _proveedorSyncService.SincronizarProveedor(empresaId.Value, connString, claves, conn, tx);

                            tx.Commit();
                        }

                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        return Json(new { icon = "error", title = "Ocurrio un error con la sincronizacion", html = ex.Message });
                    }
                }
            }

            return Json(new { icon = "success", title = "Proveedores sincrinizados correctamente", html = "Por favor corrobora la informacion de los proveedores sincronizados, revisar y mantener congruencia en los datos" });
        }
        #endregion
    }
}