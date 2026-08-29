using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Services;
using System.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Compras
{
    public partial class RequisicionController : Utilities
    {
        #region Obtener datos generales
        public JsonResult GetRequisiciones(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "(em.gen = 'CPN' OR em.gen = 'AF') AND em.estatus_id = 2",
                columnaFecha: "em.fch1");
        }

        public JsonResult GetCotizacionesCreadas(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "em.gen = 'CPN' AND em.usr_doc = @nombre_usuario",
                new Dictionary<string, object> { ["nombre_usuario"] = User.Identity.Name },
                usuarioJoin: "em.usr0");
        }

        // Ordenes de compra vivas: pendientes de que almacen les de ingreso.
        // Es la ventana en la que todavia se pueden editar (genera variacion -> estatus 13).
        [AreaAuthorize(new string[] { "Compras Nacionales" }, "ERP_SRS")]
        public JsonResult GetOrdenesCompra(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "em.gen = 'CPN' AND em.nat IN ('OC', 'OCD', 'GTO') AND em.estatus_id = 17",
                columnaFecha: "em.fch6");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetRequisicionData(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));

            var returnResult = new Dictionary<string, object>();

            string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, em.tipo_proceso, " +
                "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS nuevo_codigo, " +
                "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                "   a.nombre AS nombre_area, tipo_producto, em.variacion " +
                "FROM " +
                "   encabezadomov em " +
                "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                "INNER JOIN areas a ON a.areaid = u.areaid " +
                "WHERE " +
                "  (em.gen = 'CPN' OR em.gen = 'AF') AND " +
                "   em.estatus_id = 2 and em.id_encabezado = @id";

            var requisicion = RunQuery(query, parameters);

            if (requisicion == null || requisicion.Count == 0)
            {
                return Json(new { success = false, message = "No se encontro este documento." });
            }

            returnResult.Add("requisicion", requisicion[0]);

            query = "SELECT p.fol_doc, p.cve_prod, p.descr_prod, p.id_partidas, p.cant_ud, p.ud, p.pv_prod, " +
                "   p.cto_vta_part AS precioventa, COALESCE(p.dto1, 0) AS descuento " +
                "FROM partidasdoc p WHERE p.encabezado_id = @id";
            var partidas = RunQuery(query, parameters);
            returnResult.Add("partidas", partidas);

            query = "SELECT partidas_id, proveedor_nombre, producto, precio, descripcion, cantidad, " +
                "   ruta, uuid, COALESCE(extencion, 'default') AS extencion, nombre_original, precio_venta, unidad, " +
                "   COALESCE(descuento, 0) AS descuento, " +
                "   ROUND((cantidad * precio)::numeric, 2) AS subtotal, " +
                "   ROUND((cantidad * precio)::numeric * (1 - COALESCE(descuento, 0)::numeric / 100), 2) AS total " +
                "FROM productos_opciones " +
                "WHERE encabezado_id = @id";

            var opciones = RunQuery(query, parameters);
            returnResult.Add("opciones", opciones);

            return Json(new { success = true, returnResult });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetCotizacionPendienteData(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id", Convert.ToInt32(fc["id"].ToString()));

                var returnResult = new Dictionary<string, object>();

                string query = "SELECT  em.suc, em.gen, em.nat, em.nro_gpo_doc,  em.nro_tp_doc, em.fol_doc, " +
                    "   em.fch, em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, " +
                    "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS nuevo_codigo, " +
                    "   u.nombre || ' ' || u.apellido AS nombre_usuario, " +
                    "   a.nombre AS nombre_area, encabezados_padre, variacion " +
                    "FROM " +
                    "   encabezadomov em " +
                    "INNER JOIN usuarios u ON u.usuarioid = em.usr1 " +
                    "INNER JOIN areas a ON a.areaid = u.areaid " +
                    "WHERE " +
                    "   (em.gen = 'CPN' OR em.gen = 'AF' ) AND " +
                    "   em.estatus_id = 5 and em.id_encabezado = @id";

                var requisicion = RunQuery(query, parameters)[0];
                returnResult.Add("requisicion", requisicion);

                parameters.Add("encabezados_padre", int.Parse(requisicion["encabezados_padre"].ToString()));
                query = "SELECT p.fol_doc, p.cve_prod, p.descr_prod, p.id_partidas, p.pv_prod, p.imp_part, p.cant_ud, " +
                        "   p.ud, p.refe, p.cve_vdr_cpr, COALESCE(p.dto1, 0) AS descuento, " +
                        "   ROUND(COALESCE(p.imp_part, 0)::numeric * COALESCE(p.dto1, 0)::numeric / 100, 2) AS importeDescuento, " +
                        "   ROUND(COALESCE(p.imp_part, 0)::numeric * (1 - COALESCE(p.dto1, 0)::numeric / 100), 2) AS totalDescuento " +
                        "FROM partidasdoc p " +
                        "WHERE p.encabezado_id = @id AND p.variacion = (" +
                        "   SELECT MAX(variacion) FROM partidasdoc WHERE encabezado_id = @id" +
                        ")";
                var partidas = RunQuery(query, parameters);
                returnResult.Add("partidas", partidas);

                query = "SELECT  " +
                        "    usuarioid,  " +
                        "    nombre || ' ' || apellido AS nombre_completo " +
                        "FROM  " +
                        "    usuarios " +
                        "WHERE  " +
                        "    activo = true  " +
                        "    AND rolid = 11;";
                var director = RunQuery(query);
                returnResult.Add("director", director);

                if (Convert.ToInt32(requisicion["variacion"]) > 0)
                {
                    query = "select distinct producto, descripcion from productos_opciones where encabezado_id = @id";
                    var nombresAnteriores = RunQuery(query, parameters);
                    returnResult.Add("nombresanteriores", nombresAnteriores);
                }
                else
                {
                    query = "select distinct producto, descripcion from productos_opciones where encabezado_id = @encabezados_padre";
                    var nombresAnteriores = RunQuery(query, parameters);
                    returnResult.Add("nombresanteriores", nombresAnteriores);
                }

                query = "select distinct cve_prod, descr_prod from partidasdoc where encabezado_id = @id;";
                var nombreActual = RunQuery(query, parameters);
                returnResult.Add("nombresactuales", nombreActual);

                query = "SELECT id_f_pago, descripcion FROM cat_f_pago";
                var formasPago = RunQuery(query, parameters);
                returnResult.Add("formasPago", formasPago);

                return Json(new { success = true, returnResult });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error al obtener remisiones: " + ex.Message });

            }
        }

        #endregion

        #region Acciones opciones

        [HttpGet]
        public JsonResult getListaProveedores()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT id_prov id, cve_prov clave, n_prov nombre FROM catproveedores WHERE id_empresa = @id_empresa";
            parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var proveedores = RunQuery(query, parameters);

            return Json(proveedores);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Agregar opciones de proveedor y crear solicitud de cotizacion")]
        public JsonResult crearOpcionesCompras(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                int encabezadoAnterior = Convert.ToInt32(fc["encabezadoId"].ToString());

                // Obtener info de usuario para crear nuevo encabezado
                parameters.Add("id", encabezadoAnterior);
                string query = "SELECT em.usr0, em.fch0, em.usr1, em.fch1, variacion, usr_dep, en_presupuesto, em.nat, centro_costos " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";
                var usrId = RunQuery(query, parameters)[0];

                // Crear encabezado nuevo
                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 2;
                encabezado.IdTpDoc = 10;
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                encabezado.Fch = DateTime.Now;
                encabezado.TpMov = "CTZ";
                encabezado.UsrDep = usrId["usr_dep"].ToString();
                encabezado.ComentAut = fc["comentario"].ToString();
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = Convert.ToInt32(usrId["usr0"]);
                encabezado.Fch0 = (DateTime)usrId["fch0"];
                encabezado.Usr1 = Convert.ToInt32(usrId["usr1"]);
                encabezado.Fch1 = (DateTime)usrId["fch1"];
                encabezado.Usr2 = Convert.ToInt32(GetUserId(User.Identity.Name));
                encabezado.Fch2 = DateTime.Now;
                encabezado.Usr3 = Convert.ToInt32(usrId["usr1"]);
                encabezado.Imp = 0;
                encabezado.CliProv = "srs";
                encabezado.EncabezadoPadre = encabezadoAnterior;
                encabezado.TipoPoceso = fc["tipoProceso"].ToString();
                //TipoProducto = fc["tipoProducto"].ToString();
                encabezado.Estatus = 3;
                encabezado.EnPresupuesto = Convert.ToBoolean(usrId["en_presupuesto"]);
                encabezado.CentroCostos = GetInt(usrId["centro_costos"]);

                if (new List<string> { "SGTO" }.Contains(usrId["nat"].ToString()))
                {
                    encabezado.EsServicio = true;
                    encabezado.IdArea = 4;
                    encabezado.IdTpDoc = 80;
                    encabezado.TpMov = "CTZG";
                }

                var documento = new Dictionary<string, object>();

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {

                            if (Convert.ToInt32(usrId["variacion"]) > 0)
                            {
                                query = "UPDATE encabezadomov SET estatus_id = 3 WHERE id_encabezado = @id";
                                RunUpdate(query, parameters, false, conn, tx);

                                query = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                                    "FROM encabezadomov em " +
                                    "WHERE id_encabezado = @id";
                                var folio = RunScalar(query, parameters, false, conn, tx);
                                documento.Add("IdEncabezado", encabezadoAnterior);
                                documento.Add("folio_generado", folio);
                            }
                            else
                            {
                                documento = GenerarDocumentoConPartidas(encabezado, new List<PartidaDocumento>(), conn, tx);
                            }

                            int nuevoEncabezadoId = Convert.ToInt32(documento["IdEncabezado"]);

                            // Parsear partidas nuevas
                            var partidasNuevasIds = JsonConvert.DeserializeObject<List<int>>(fc["partidaIds"].ToString());
                            var partidas = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["partidasJson"].ToString());

                            string insertOpciones = "INSERT INTO productos_opciones ( " +
                                    "   encabezado_id, partidas_id, prov_id, proveedor_nombre, " +
                                    "   producto, precio, descripcion, cantidad, uuid, " +
                                    "   nombre_original, extencion, ruta, descuento, precio_venta, unidad " +
                                    ") VALUES ( " +
                                    "   @encabezadoId, @partidaId, @prov_id, @proveedorNombre, " +
                                    "   @producto, @costoUnitario, @descripcion, @cantidad, @uuid, " +
                                    "   @nombreArchivo, @extension, @ruta, @descuento, @venta, @unidad " +
                                    ")";

                            for (int i = 0; i < partidas.Count; i++)
                            {
                                var partida = partidas[i];
                                object proveedorId = DBNull.Value;
                                if (partida.ContainsKey("proveedorId") && partida["proveedorId"] != null)
                                {
                                    proveedorId = Convert.ToInt32(partida["proveedorId"]);
                                }

                                var p = new Dictionary<string, object>();
                                p["encabezadoId"] = nuevoEncabezadoId;
                                p["partidaId"] = Convert.ToInt32(partida["partidaId"]);
                                p["prov_id"] = proveedorId;
                                p["proveedorNombre"] = partida["proveedorNombre"];
                                p["producto"] = partida["producto"];
                                p["costoUnitario"] = Convert.ToDecimal(partida["costoUnitario"]);
                                p["descripcion"] = partida["descripcion"];
                                p["descuento"] = DescuentosService.Porcentaje(
                                    partida.ContainsKey("descuento") ? GetDecimal(partida["descuento"], 0) : 0);
                                p["venta"] = partida["venta"];
                                p["unidad"] = partida["unidad"];

                                var paramCantidad = new Dictionary<string, object>
                                {
                                    ["partidaId"] = Convert.ToInt32(partida["partidaId"])
                                };
                                query = "SELECT cant_ud FROM partidasdoc WHERE id_partidas = @partidaId";
                                var cantidad = RunScalar(query, paramCantidad, false, conn, tx);

                                p["cantidad"] = Convert.ToDecimal(cantidad);

                                string uuid = null, extension = null, ruta = null, nombreOriginal = null;

                                if (partida.ContainsKey("tieneArchivo") && (bool)partida["tieneArchivo"])
                                {
                                    IFormFile? archivo = Request.Form.Files[$"partidas[{i}][archivo]"];
                                    if (archivo != null && archivo.Length > 0)
                                    {
                                        nombreOriginal = archivo.FileName;
                                        extension = Path.GetExtension(archivo.FileName);
                                        ruta = "content/opciones_compra/";
                                        uuid = Guid.NewGuid().ToString();
                                        UploadFormFileToPath(ruta, archivo, uuid, extension);
                                    }
                                }

                                p["nombreArchivo"] = nombreOriginal;
                                p["uuid"] = uuid;
                                p["extension"] = extension;
                                p["ruta"] = ruta + uuid + extension;

                                RunUpdate(insertOpciones, p, false, conn, tx);
                            }

                            // Actualizar encabezado_id para partidas con opciones anteriores
                            parameters = new Dictionary<string, object>
                            {
                                ["encabezadoAnterior"] = encabezadoAnterior
                            };

                            query = "SELECT partidas_id FROM productos_opciones WHERE encabezado_id = @encabezadoAnterior";
                            var partidasExistentes = RunQuery(query, parameters, false, conn, tx).Select(p => Convert.ToInt32(p["partidas_id"])).ToList();

                            var partidasAActualizar = partidasExistentes.Except(partidasNuevasIds).ToList();

                            foreach (var partidaId in partidasAActualizar)
                            {
                                var updateParams = new Dictionary<string, object>
                                {
                                    ["encabezadoCreado"] = nuevoEncabezadoId,
                                    ["partidaId"] = partidaId,
                                    ["encabezadoAnterior"] = encabezadoAnterior
                                };

                                query = "UPDATE productos_opciones SET encabezado_id = @encabezadoCreado " +
                                        "WHERE encabezado_id = @encabezadoAnterior AND partidas_id = @partidaId";

                                RunUpdate(query, updateParams, false, conn, tx);
                            }

                            parameters = new Dictionary<string, object>
                            {
                                ["encabezadoId"] = encabezadoAnterior,
                                ["fechaCompras"] = DateTime.Now
                            };
                            // Actualizar estatus del documento anterior
                            if (Convert.ToInt32(usrId["variacion"]) > 0)
                            {
                                query = "UPDATE encabezadomov SET estatus_id = 3, fch2 = @fechaCompras WHERE id_encabezado = @encabezadoId";
                                RunUpdate(query, parameters, false, conn, tx);
                            }
                            else
                            {
                                query = "UPDATE encabezadomov SET estatus_id = 11, fch2 = @fechaCompras WHERE id_encabezado = @encabezadoId";
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

                // Notificación
                var userInfo = RunQuery("SELECT UsuarioId, AreaId, Email, nombre || apellido AS usuario FROM Usuarios WHERE NombreUsuario = @nombre_usuario", new Dictionary<string, object> {
                        { "nombre_usuario", User.Identity.Name }
                        })[0];

                query = "SELECT u.nombre || ' ' || u.apellido  AS usuario, u.nombreusuario, u.email " +
                    "FROM encabezadomov e " +
                    "INNER JOIN usuarios u ON u.usuarioid = e.usr1 " +
                    "WHERE e.id_encabezado = @encabezado";
                var jefeArea = RunQuery(query, new Dictionary<string, object>
                    {
                        { "encabezado", Convert.ToInt32(encabezadoAnterior) }
                    }
                )[0];

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/GestionSolicitudes";
                string urlCot = $"{urlBase}{path}?id={documento["folio_generado"].ToString()}";

                _ = SendNotificationInterno(jefeArea["nombreusuario"].ToString(), jefeArea["email"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva cotización creada",
                    message = $"El usuario {userInfo["usuario"]} ha creado una nueva cotización con el folio {documento["folio_generado"].ToString()} que necesita su aprobación.",
                    buttons = new[] {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCot}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0
                });

                return Json(new { success = true, folio = documento["folio_generado"] });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al procesar las opciones.",
                    error = ex.Message
                });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GuardarOpciones(IFormCollection fc)
        {
            try
            {
                var partidaIds = JsonConvert.DeserializeObject<List<int>>(fc["partidaIds"].ToString());
                var parameters = new Dictionary<string, object>();
                parameters.Add("encabezadoId", Convert.ToInt32(fc["encabezadoId"].ToString()));
                var inClause = new List<string>();

                for (int i = 0; i < partidaIds.Count; i++)
                {
                    string paramName = $"id{i}";
                    parameters.Add(paramName, partidaIds[i]);
                    inClause.Add("@" + paramName);
                }

                string query = $"SELECT COUNT(*) FROM productos_opciones " +
                    $"WHERE encabezado_id = @encabezadoId AND partidas_id IN ({string.Join(",", inClause)})";
                var qty = RunScalar(query, parameters);

                if (Convert.ToInt32(qty) > 0)
                {
                    return Json(new
                    {
                        success = false,
                        icon = "error",
                        title = "Alto",
                        text = "Estas intentando realizar una accion prohibida por el sistema, no puedes editar o agergar opciones a partidas ya guardadas.",
                        showCancelButton = false,
                    });
                }

                // 1. Leer el JSON de las partidas
                string partidasJson = Request.Form["partidasJson"].ToString();
                var partidas = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["partidasJson"].ToString());
                var _parameters = new List<Dictionary<string, object>>();
                int documentoId = Convert.ToInt32(Convert.ToInt32(fc["encabezadoId"].ToString()));

                // 2. Recorrer cada partida
                for (int i = 0; i < partidas.Count; i++)
                {
                    var partida = partidas[i];
                    string uuid = null;
                    string nombreOriginal = null;
                    string extension = null;
                    string ruta = null;

                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezadoId", documentoId);
                    parameters.Add("partidaId", Convert.ToInt32(partida["partidaId"]));

                    if (partida.ContainsKey("proveedorId") && partida["proveedorId"] != null)
                        parameters.Add("prov_id", Convert.ToInt32(partida["proveedorId"]));
                    else
                        parameters.Add("prov_id", DBNull.Value);

                    parameters.Add("proveedorNombre", partida["proveedorNombre"]);
                    parameters.Add("producto", partida["producto"]);
                    parameters.Add("costoUnitario", Convert.ToDecimal(partida["costoUnitario"]));
                    parameters.Add("descripcion", partida["descripcion"]);
                    parameters.Add("venta", partida["venta"]);
                    parameters.Add("unidad", partida["unidad"]);
                    parameters.Add("descuento", DescuentosService.Porcentaje(
                        partida.ContainsKey("descuento") ? GetDecimal(partida["descuento"], 0) : 0));

                    var param = new Dictionary<string, object>();
                    param.Add("partidaId", Convert.ToInt32(partida["partidaId"]));
                    query = "SELECT cant_ud FROM partidasdoc WHERE id_partidas = @partidaId";
                    var cantidad = RunScalar(query, param);

                    parameters.Add("cantidad", Convert.ToDecimal(cantidad));


                    if (partida.ContainsKey("tieneArchivo") && (bool)partida["tieneArchivo"])
                    {
                        IFormFile? archivo = Request.Form.Files[$"partidas[{i}][archivo]"];

                        if (archivo != null && archivo.Length > 0)
                        {
                            nombreOriginal = archivo.FileName;
                            extension = Path.GetExtension(archivo.FileName);
                            ruta = "content/opciones_compra/";
                            uuid = Guid.NewGuid().ToString();

                            var result = UploadFormFileToPath(ruta, archivo, uuid, extension);
                        }
                    }

                    parameters.Add("nombreArchivo", nombreOriginal);
                    parameters.Add("uuid", uuid);
                    parameters.Add("extension", extension);
                    parameters.Add("ruta", ruta + uuid + extension);

                    _parameters.Add(parameters);
                }

                query = "INSERT INTO productos_opciones (" +
                    "   encabezado_id, partidas_id, prov_id, proveedor_nombre, producto, precio, descripcion, cantidad, uuid, " +
                    "   nombre_original, extencion, ruta, precio_venta, unidad, descuento" +
                    ") VALUES (" +
                    "   @encabezadoId, @partidaId, @prov_id, @proveedorNombre, @producto, @costoUnitario, @descripcion, @cantidad, @uuid, " +
                    "   @nombreArchivo, @extension, @ruta, @venta, @unidad, @descuento)";

                RunUpdate(query, _parameters);

                return Json(new { success = true, message = "Las partidas se guardaron correctamente, pero el documento no se creo y el estatus no se actualizo." });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al procesar las opciones.",
                    error = ex.Message
                });
            }
        }

        [HttpPost]
        public JsonResult VerificarPartidasConOpciones(int encabezadoId)
        {
            string query = "SELECT DISTINCT partidas_id FROM productos_opciones WHERE encabezado_id = @encabezadoId";
            var parameters = new Dictionary<string, object> { { "encabezadoId", encabezadoId } };
            var partidas = RunQuery(query, parameters);
            return Json(partidas);
        }

        public JsonResult GetImpuestosData()
        {
            string query = "SELECT id_impuesto, cve_impuesto, tasa_imp FROM cat_impuestos;";
            var impuestos = RunQuery(query);
            return Json(new
            {
                success = true,
                data = impuestos
            });
        }

        #endregion

        #region Acciones para orden de compra y solicitud de gasto
        [RoleAuthorize(new string[] { "Gerente", "Administrador", "Super Administrador" }, "ERP_SRS")]
        [AreaAuthorize(new string[] { "Compras Nacionales" }, "ERP_SRS")]
        [AuditAction(Modulo = "Compras Nacionales", Accion = "Gerente acepto orden de compra / solicitud de gasto")]
        public JsonResult AceptarOrdenGerente(string datos, string comentario, int id_encabezado, int centro_costos)
        {
            try
            {
                var queryDB = "SELECT cve_prod, cant_ud, pv_prod, imp_part, COALESCE(dto1, 0) AS dto1 " +
                    "FROM partidasdoc " +
                    "WHERE encabezado_id = @id_encabezado";
                var parameters = new Dictionary<string, object>();
                var _parameters = new List<Dictionary<string, object>>();
                parameters.Add("id_encabezado", id_encabezado);

                var datosOriginales = RunQuery(queryDB, parameters);

                bool hayCambios = false;
                var partidas = JsonConvert.DeserializeObject<List<PartidaDocumento>>(datos.ToString())
                    ?? new List<PartidaDocumento>();

                foreach (var enviado in partidas)
                {
                    var original = datosOriginales.FirstOrDefault(x => x["cve_prod"].ToString() == enviado.CveProd);
                    if (original == null) continue;

                    decimal cantidad = GetDecimal(enviado.CantUd, 0).Value;
                    decimal precio = GetDecimal(enviado.PvProd, 0).Value;
                    decimal dto1 = DescuentosService.Porcentaje(enviado.Dto1);

                    bool cambio =
                        GetDecimal(original["cant_ud"], 0) != cantidad ||
                        GetDecimal(original["pv_prod"], 0) != DescuentosService.Redondear(precio) ||
                        GetDecimal(original["dto1"], 0) != dto1;

                    if (!cambio) continue;

                    _parameters.Add(new Dictionary<string, object>
                    {
                        ["cantidad"] = cantidad,
                        ["precio"] = precio,
                        ["codigo"] = enviado.CveProd,
                        ["total"] = DescuentosService.Bruto(cantidad, precio),
                        ["dto1"] = dto1,
                        ["id_encabezado"] = id_encabezado
                    });
                    hayCambios = true;
                }

                parameters = new Dictionary<string, object>();
                parameters.Add("id_encabezado", id_encabezado);
                string query;
                if (hayCambios)
                {
                    query = "UPDATE partidasdoc SET cant_ud = @cantidad, pv_prod = @precio, imp_part = @total, dto1 = @dto1 " +
                        "   WHERE cve_prod = @codigo AND encabezado_id = @id_encabezado";
                    RunUpdate(query, _parameters);

                    query = "SELECT COALESCE(SUM(imp_part), 0) AS subtotal, " +
                        "   COALESCE(SUM(ROUND(imp_part::numeric * COALESCE(dto1, 0)::numeric / 100, 2)), 0) AS descuento " +
                        "FROM partidasdoc WHERE encabezado_id = @id_encabezado";
                    var suma = RunQuery(query, parameters)[0];

                    var totales = new TotalesDocumento
                    {
                        Subtotal = GetDecimal(suma["subtotal"], 0).Value,
                        Descuento = GetDecimal(suma["descuento"], 0).Value
                    };

                    GuardarTotalesDocumento(id_encabezado, totales, totales.Base);

                    // Editada: vuelve con el director para que la apruebe de nuevo
                    query = "UPDATE encabezadomov SET estatus_id = 13, centro_costos = @centro_costos, " +
                        "   fecha_edicion = @fecha_edicion, editado_por = @editado_por " +
                        "WHERE id_encabezado = @id_encabezado";
                    parameters.Add("centro_costos", centro_costos);
                    parameters.Add("fecha_edicion", DateTime.Now);
                    parameters.Add("editado_por", GetUserId(User.Identity.Name));
                }
                else
                {
                    // Sin cambios sigue pendiente de ingreso (17): solo se confirma el centro de costos
                    query = "UPDATE encabezadomov SET centro_costos = @centro_costos " +
                        "WHERE id_encabezado = @id_encabezado";
                    parameters.Add("centro_costos", centro_costos);
                }
                RunUpdate(query, parameters);

                if (hayCambios)
                {
                    query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio, " +
                    "   (SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = @nombreUsuario) AS comprador " +
                    "FROM encabezadomov e " +
                    "INNER JOIN usuarios u ON u.usuarioid = e.usr6 " +
                    "WHERE e.id_encabezado = @id_encabezado";
                    parameters.Add("nombreUsuario", User.Identity.Name);
                    var userResult = RunQuery(query, parameters)[0];

                    var urlBase = $"{Request.Scheme}://{Request.Host}";
                    string path = "/Compras/GestionSolicitudes";
                    string urlCotizacion = $"{urlBase}{path}?id={userResult["folio"].ToString()}";

                    _ = SendNotificationInterno(userResult["aliasusuario"].ToString(), userResult["emailusuario"].ToString(), new
                    {
                        icon = "info",
                        title = "Se edito una orden de compra creada",
                        message = $"El usuario {userResult["comprador"]} edito la orden de compra con el folio {userResult["folio"]} y necesita revicion.",
                        buttons = new[]
                        {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                        timer = 0,
                    });

                    return Json(new { success = true, message = "La orden de compra fue editada y regreso al director para su aprobacion." });
                }
                else
                {
                    query = "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, " +
                    "   u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "   e.folio || CASE WHEN e.variacion > 0 THEN '-' || num_to_letters(e.variacion) ELSE '' END AS folio, " +
                    "   (SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = @nombreUsuario) AS comprador " +
                    "FROM encabezadomov e " +
                    "INNER JOIN usuarios u ON u.usuarioid = e.usr4 " +
                    "WHERE e.id_encabezado = @id_encabezado";
                    parameters.Add("nombreUsuario", User.Identity.Name);
                    var userResult = RunQuery(query, parameters)[0];

                    //var poliza = GenerarDatosPoliza(id_encabezado);
                    //RegistrarPolizas(GetUserId(User.Identity.Name), id_encabezado, poliza);
                    //RegistrarCompra(id_encabezado, GetUserId(User.Identity.Name));
                    //RegistrarCarteras(id_encabezado, GetUserId(User.Identity.Name));

                    var urlBase = $"{Request.Scheme}://{Request.Host}";
                    string path = "/Compras/GestionSolicitudes";
                    string urlCotizacion = $"{urlBase}{path}?id={userResult["folio"].ToString()}";

                    _ = SendNotificationInterno(userResult["aliasusuario"].ToString(), userResult["emailusuario"].ToString(), new
                    {
                        icon = "info",
                        title = "Nueva orden de compra creada",
                        message = $"El usuario {userResult["comprador"]} a generado una orden de compra con el folio {userResult["folio"]} que necesita de su aprobacion.",
                        buttons = new[]
                        {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                        timer = 0,
                    });

                    return Json(new { success = true, message = "La orden de compra quedo confirmada y sigue pendiente de ingreso a almacen." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ocurrio un error al actualizar, si el problema persiste, por favor, notificar a sistemas" });
            }
        }


        #endregion

        #region Paso 6 - Definir metodo de pago (estatus 5 -> 6)
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetCotizacionesPorConfigurar(IFormCollection fc)
        {
            return ConsultarDocumentos(fc,
                "(em.gen = 'CPN' OR em.gen = 'AF') AND em.estatus_id = 5",
                columnaFecha: "em.fch4");
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Administración y Finanzas", Accion = "Definir metodo de pago de la cotizacion")]
        public JsonResult AceptarCotizacionCompras(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "";
                string message = "La requisicion fue enviada al director seleccionado y esta en espera de ser aprovada.";
                string proveedorMessage = "Un proveedor no se encontraba en nuestros registros, por lo que fue creado automáticamente. " +
                    "<br><br>Se registró con los siguientes datos:<br><br>" +
                    "<table style='width:100%; border-collapse:collapse; text-align:left;'> " +
                    "   <thead>" +
                    "       <tr>" +
                    "           <th style='border:1px solid #ccc; padding:6px;'>Campo</th>" +
                    "           <th style='border:1px solid #ccc; padding:6px;'>Valor</th>" +
                    "       </tr>" +
                    "   </thead>" +
                    "   <tbody>";

                string partidasJson = fc["partidas"].ToString();
                if (string.IsNullOrWhiteSpace(partidasJson))
                {
                    return Json(new { success = false, message = "No se recibieron partidas." });
                }

                JArray proveedores = JArray.Parse(partidasJson);
                if (!proveedores.Any())
                {
                    return Json(new { success = false, message = "Debes seleccionar al menos un proveedor con partidas antes de guardar." });
                }

                int idEncabezado = Convert.ToInt32(fc["IdEncabezado"].ToString());

                decimal brutoDocumento = 0;
                decimal descuentoDocumento = 0;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // FOR UPDATE bloquea la fila hasta el commit: si llegan dos
                            // peticiones a la vez, la segunda espera aqui y despues ve
                            // que el documento ya no esta en 5.
                            int estatusActual = Convert.ToInt32(RunScalar(
                                "SELECT estatus_id FROM encabezadomov WHERE id_encabezado = @id FOR UPDATE",
                                new Dictionary<string, object> { ["id"] = idEncabezado },
                                false, conn, tx));

                            if (estatusActual != 5)
                            {
                                tx.Rollback();
                                return Json(new
                                {
                                    icon = "info",
                                    title = "Esta cotizacion ya fue procesada",
                                    html = "El metodo de pago de este documento ya se habia guardado. Actualiza la tabla para ver su estado actual.",
                                    yaProcesado = true
                                });
                            }

                            foreach (JObject proveedor in proveedores)
                            {
                                string proveedorNombre = proveedor["proveedor"]?.ToString();
                                string formaPago = proveedor["formaPago"]?.ToString();

                                if (string.IsNullOrEmpty(formaPago))
                                {
                                    return Json(new { success = false, message = $"Falta la forma de pago del proveedor {proveedorNombre}." });
                                }

                                parameters = new Dictionary<string, object>();
                                query = "SELECT COUNT(*) FROM catproveedores WHERE LOWER(n_prov) = @nombre AND id_empresa = @id_empresa";
                                parameters.Add("nombre", proveedorNombre.ToLower(System.Globalization.CultureInfo.InvariantCulture));
                                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                                int qty = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                                if (qty == 0)
                                {
                                    query = "SELECT " +
                                        "   CASE WHEN SPLIT_PART(cve_prov, '-', 2)::int >= 9999 THEN " +
                                        "       LPAD((SPLIT_PART(cve_prov, '-', 1)::int + 1)::text, 2, '0') || '-0001' " +
                                        "   ELSE " +
                                        "       LPAD(SPLIT_PART(cve_prov, '-', 1), 2, '0') || '-' || " +
                                        "       LPAD((SPLIT_PART(cve_prov, '-', 2)::int + 1)::text, 4, '0') " +
                                        "   END AS siguiente_cve_prov " +
                                        "FROM catproveedores " +
                                        "WHERE cve_prov ~ '^[0-9]+-[0-9]+$' AND id_empresa = @id_empresa " +
                                        "ORDER BY SPLIT_PART(cve_prov, '-', 1)::int DESC, SPLIT_PART(cve_prov, '-', 2)::int DESC " +
                                        "LIMIT 1;";
                                    string nvo_cve_prov = RunScalar(query, parameters, false, conn, tx).ToString();

                                    parameters = new Dictionary<string, object>();
                                    query = "INSERT INTO catproveedores(n_prov, cve_prov, id_empresa) VALUES (@n_prov, @cve_prov, @id_empresa) RETURNING id_prov";
                                    parameters.Add("n_prov", proveedorNombre);
                                    parameters.Add("cve_prov", nvo_cve_prov);
                                    parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                                    var nvo_prov = RunScalar(query, parameters, false, conn, tx);

                                    proveedorMessage += $"" +
                                        $"      <tr>" +
                                        $"          <td style='border:1px solid #ccc; padding:6px;'>{proveedorNombre}</td>" +
                                        $"      </tr>" +
                                        $"      <tr>" +
                                        $"          <td style='border:1px solid #ccc; padding:6px;'>{nvo_cve_prov}</td>" +
                                        $"      </tr>";

                                    string nvo_codigo = $"2-1-01-{nvo_cve_prov}";
                                    parameters = new Dictionary<string, object>();
                                    query = "INSERT INTO cuentas_finanzas(codigo, nombre, creada_por, proveedor_id) " +
                                        "VALUES (@nvo_codigo, @n_prov, @user, @prov_id)";
                                    parameters.Add("nvo_codigo", nvo_codigo);
                                    parameters.Add("n_prov", proveedorNombre);
                                    parameters.Add("@user", GetUserId(User.Identity.Name));
                                    parameters.Add("prov_id", nvo_prov);
                                    RunQuery(query, parameters, false, conn, tx);
                                }

                                JArray partidas = (JArray)proveedor["partidas"];
                                decimal brutoProveedor = 0;
                                decimal descuentoProveedor = 0;

                                parameters = new Dictionary<string, object>();
                                query = "SELECT id_prov FROM catproveedores WHERE LOWER(n_prov) = @nombre AND id_empresa = @id_empresa";
                                parameters.Add("nombre", proveedorNombre.ToLower(System.Globalization.CultureInfo.InvariantCulture));
                                parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                                int prov_id = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

                                parameters = new Dictionary<string, object>();
                                query = "SELECT n_prov FROM catproveedores WHERE id_prov = @prov_id AND id_empresa = @id_empresa";
                                parameters.Add("prov_id", prov_id);
                                proveedorNombre = RunScalar(query, parameters, false, conn, tx).ToString();

                                foreach (JObject p in partidas)
                                {
                                    if (!p.ContainsKey("id_partidas"))
                                        return Json(new { success = false, message = $"Falta el ID de una partida del proveedor {proveedorNombre}." });

                                    int idPartida = p["id_partidas"]?.ToObject<int>() ?? 0;
                                    string clave = p["clave"]?.ToString();
                                    string descripcion = p["descripcion"]?.ToString();
                                    decimal precio = p["precio_unitario"]?.ToObject<decimal>() ?? 0;
                                    decimal cantidad = p["cantidad"]?.ToObject<decimal>() ?? 0;

                                    parameters.Clear();
                                    parameters.Add("id_partidas", idPartida);

                                    // El descuento vive en la partida; si el modal lo reenvia, gana el valor capturado.
                                    decimal descuento = p.ContainsKey("descuento")
                                        ? DescuentosService.Porcentaje(p["descuento"]?.ToObject<decimal?>())
                                        : DescuentosService.Porcentaje(GetDecimal(
                                            RunScalar("SELECT dto1 FROM partidasdoc WHERE id_partidas = @id_partidas",
                                                parameters, false, conn, tx), 0));

                                    query = "UPDATE partidasdoc " +
                                            "SET f_pago_id = @f_pago_id, cve_prod = @clave, descr_prod = @descripcion, " +
                                            "    refe = @refe, cve_vdr_cpr = @n_prov, dto1 = @dto1, imp_part = @imp_part " +
                                            "WHERE id_partidas = @id_partidas;";

                                    parameters.Add("f_pago_id", int.Parse(formaPago));
                                    parameters.Add("clave", clave ?? "");
                                    parameters.Add("descripcion", descripcion ?? "");
                                    parameters.Add("refe", prov_id);
                                    parameters.Add("n_prov", proveedorNombre);
                                    parameters.Add("dto1", descuento);
                                    parameters.Add("imp_part", DescuentosService.Bruto(cantidad, precio));

                                    RunUpdate(query, parameters, false, conn, tx);

                                    brutoProveedor += DescuentosService.Bruto(cantidad, precio);
                                    descuentoProveedor += DescuentosService.Descuento(cantidad, precio, descuento);
                                }

                                decimal subtotalProveedor = brutoProveedor - descuentoProveedor;
                                brutoDocumento += brutoProveedor;
                                descuentoDocumento += descuentoProveedor;

                                // Archivos del proveedor (cotizacion, factura, xml)
                                string[] tipos = new[] { "cotizacion", "factura", "xml" };
                                foreach (var tipo in tipos)
                                {
                                    string proveedorKey = $"{tipo}_{proveedorNombre}";
                                    IFormFile? archivo = Request.Form.Files[proveedorKey];
                                    if (archivo != null && archivo.Length > 0)
                                    {
                                        string nombreOriginal = archivo.FileName;
                                        string ruta = "content/archivos_oc_compras/";
                                        string uuid = Guid.NewGuid().ToString();
                                        string extension = Path.GetExtension(nombreOriginal);
                                        var result = UploadFormFileToPath(ruta, archivo, uuid, extension);

                                        query = "INSERT INTO archivos_compras_oc (nombre_original, path, uuid, extencion, encabezado_id, proveedor_nombre) " +
                                                "VALUES (@nombreOriginal, @ruta, @uuid, @extension, @encabezado_id, @proveedor_nombre)";
                                        var parametersArchivos = new Dictionary<string, object>
                                        {
                                            { "nombreOriginal", nombreOriginal },
                                            { "ruta", ruta + uuid + extension },
                                            { "uuid", uuid },
                                            { "extension", extension },
                                            { "encabezado_id", idEncabezado },
                                            { "proveedor_nombre", proveedorNombre }
                                        };
                                        RunUpdate(query, parametersArchivos, false, conn, tx);
                                    }
                                }

                                // Procesar impuestos (ya con subtotal calculado)
                                JArray impuestos = (JArray)proveedor["impuestos"];
                                List<Dictionary<string, object>> listaImpuestos = new List<Dictionary<string, object>>();
                                int orden = 0;

                                foreach (JObject imp in impuestos)
                                {
                                    int tipo = imp["tipo"]?.ToObject<int>() ?? 0;
                                    decimal tasa = imp["tasa"]?.ToObject<decimal>() ?? 0;

                                    // Asignar orden_apl manualmente
                                    orden += 1;

                                    decimal importe = subtotalProveedor * (tasa / 100);

                                    var param = new Dictionary<string, object>
                                    {
                                        { "encabezado_id", idEncabezado },
                                        { "impuesto_id", tipo },
                                        { "subtotal", subtotalProveedor },
                                        { "importe", importe },
                                        { "orden_apl", orden },
                                        { "imp_variable", tasa },
                                        { "prov_nom", proveedorNombre },
                                        { "mdp", Convert.ToInt32(formaPago) }
                                    };

                                    listaImpuestos.Add(param);
                                }

                                // Ordenar por orden_apl y ejecutar insert
                                foreach (var parametersImpuestos in listaImpuestos.OrderBy(x => (int)x["orden_apl"]))
                                {
                                    string queryImp = "INSERT INTO imp_oc " +
                                                      "(encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
                                                      "VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @mdp);";

                                    RunUpdate(queryImp, parametersImpuestos, false, conn, tx);
                                }

                            }
                            query = "UPDATE encabezadomov SET estatus_id = 6, usr5 = @comprasRevision, fch5 = @comprasfechas, " +
                               "   firma5 = @firma, sub = @sub, dto = @dto " +
                               "WHERE id_encabezado = @id_encabezado;";
                            parameters = new Dictionary<string, object>();
                            parameters.Add("id_encabezado", idEncabezado);
                            parameters.Add("comprasRevision", GetUserId(User.Identity.Name));
                            parameters.Add("comprasfechas", DateTime.Now);
                            parameters.Add("firma", fc["firma"].ToString());
                            parameters.Add("sub", DescuentosService.Redondear(brutoDocumento));
                            parameters.Add("dto", DescuentosService.Redondear(descuentoDocumento));
                            RunUpdate(query, parameters, false, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                proveedorMessage += "</tbody></table><br>El proveedor puede editarse en cualquier momento desde el apartado de <b>Proveedores</b>.";

                if (proveedorMessage.Contains("<td>"))
                {
                    message += "<br><br>" + proveedorMessage;
                }

                // Obtener folio del documento
                parameters.Clear();
                parameters.Add("id_encabezado", idEncabezado);
                string getDocQuery = "SELECT em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS nuevo_codigo " +
                                     "FROM encabezadomov em WHERE id_encabezado = @id_encabezado";
                var docResult = RunQuery(getDocQuery, parameters)[0];

                // Notificar al director
                parameters.Clear();
                parameters.Add("usrDirector", Convert.ToInt32(fc["UsuarioDirectorId"].ToString()));
                string getDirectorQuery = "SELECT usuarioid, email, nombreusuario FROM usuarios WHERE usuarioid = @usrDirector";
                var directorResult = RunQuery(getDirectorQuery, parameters)[0];

                parameters.Clear();
                parameters.Add("nombre", User.Identity.Name);
                query = "SELECT nombre || ' ' || apellido FROM usuarios WHERE nombreusuario = @nombre";
                var user = RunScalar(query, parameters);

                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string path = "/Compras/GestionSolicitudes";
                string urlCotizacion = $"{urlBase}{path}?id={docResult["nuevo_codigo"].ToString()}";

                _ = SendNotificationInterno(directorResult["nombreusuario"].ToString(), directorResult["email"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva cotización creada",
                    message = $"El usuario {user} ha creado una nueva requisicion de material con el folio {docResult["nuevo_codigo"]} que necesita su aprobación.",
                    buttons = new[]
                    {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0
                });

                return Json(new { icon = "success", title = "Requisicion aceptada correctamente", html = message });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error inesperado", html = ex.Message });
            }
        }
        #endregion
    }
}
