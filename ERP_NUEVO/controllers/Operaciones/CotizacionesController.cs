//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using BOS_ERP.Models;
//using System.Data;
//using System.Data.SqlClient;
//using Microsoft.AspNetCore.Mvc;
//using BOS_ERP.Controllers;

//namespace BOS_ERP.Controllers.Operaciones
//{
//    public class CotizacionController : Utilities
//    {
//        [HttpPost]
//        public IActionResult FacturaNacional([FromBody] JObject data)
//        {
//            try
//            {
//                string[] requiredFields = { "cliente", "moneda", "metodopago", "usocfdi" };
//                foreach (var field in requiredFields)
//                {
//                    if (data[field] == null || string.IsNullOrEmpty((string)data[field]))
//                        return Json(new { resultado = false, mensaje = $"{field} no especificado." });
//                }

//                // Inicializar factura con valores básicos
//                var factura = new Factura
//                {
//                    Serie = "A",
//                    Folio = "1205"
//                };

//                string clienteId = (string)data["cliente"];

//                // Parámetros para consultas
//                var parameters = new Dictionary<string, object> { { "id", clienteId } };

//                // Consultas necesarias
//                var resultCliente = RunQuery("SELECT c1,c2,c3,c10, c4, c5 ,c6, c12, c27, c60 FROM sellosop.kdud WHERE c2 = @id", parameters, false, null, null, "ERP");
//                var resultDirCliente = RunQuery("SELECT c19, c18, c13 FROM sellosop.kdfedir WHERE c2 = @id", parameters, false, null, null, "ERP");

//                if (resultCliente == null || resultCliente.Count == 0 || resultDirCliente == null || resultDirCliente.Count == 0)
//                    return Json(new { resultado = false, mensaje = "No se encontraron datos del cliente." });

//                // Consultas dependientes de datos anteriores
//                parameters["regimen"] = resultDirCliente[0]["c18"].ToString();
//                parameters["cfdi"] = (string)data["usocfdi"];

//                var resultRegimen = RunQuery("SELECT c1, c2 FROM sellosop.kdfe33satrf WHERE c1 = @regimen", parameters, false, null, null, "ERP");
//                var resultCFDI = RunQuery("SELECT c1, c2 FROM sellosop.kdfe33satuso WHERE c1 = @cfdi", parameters, false, null, null, "ERP");

//                // Asignar datos a factura
//                factura.RsoCliente = resultDirCliente[0]["c19"].ToString();
//                factura.RfcCliente = resultCliente[0]["c10"].ToString();
//                factura.CpR = resultDirCliente[0]["c13"].ToString();
//                factura.Regc = resultDirCliente[0]["c18"].ToString();
//                factura.regimenEText = resultRegimen[0]["c2"].ToString();

//                factura.IdTipoPago = (string)data["formapago"];
//                factura.formaPagoTexto = (string)data["formaPagoTexto"];
//                factura.metodoPagoTexto = (string)data["metodoPagoTexto"];

//                factura.Moneda = (string)data["moneda"];
//                factura.MdpFactura = (string)data["metodopago"];
//                factura.TipoCambio = 1;

//                // Datos del emisor
//                factura.RsoEmisor = "ESCUELA KEMPER URGATE";
//                factura.RfcEmisor = "EKU9003173C9";
//                factura.CpE = "42501";
//                factura.Rege = "601";
//                factura.IdUsoCFDI = (string)data["usocfdi"];
//                factura.CFDIText = resultCFDI[0]["c2"].ToString();

//                // Crear DataTable para productos
//                factura.Tproductos = new System.Data.DataTable();
//                factura.Tproductos.Columns.AddRange(new[]
//                {
//            new DataColumn("articulo", typeof(string)),
//            new DataColumn("cantidad", typeof(double)),
//            new DataColumn("ClaveUnidad", typeof(string)),
//            new DataColumn("Unidad", typeof(string)),
//            new DataColumn("precio", typeof(double)),
//            new DataColumn("descuento", typeof(double)),
//            new DataColumn("importe", typeof(double)),
//            new DataColumn("descripcion", typeof(string)),
//            new DataColumn("iva", typeof(double)),
//            new DataColumn("objeto", typeof(string)),
//            new DataColumn("claveprod", typeof(string))
//        });

//                // Validar y agregar productos
//                foreach (var prod in data["articulos"])
//                {
//                    if (prod["cantidad"] == null || prod["precio"] == null || prod["descripcion"] == null)
//                        return Json(new { resultado = false, mensaje = "Uno o más productos no tienen los datos necesarios." });

//                    double cantidad = (double)prod["cantidad"];
//                    double precio = (double)prod["precio"];
//                    double ivaPct = (double)prod["iva"];

//                    factura.Tproductos.Rows.Add(
//                        (string)prod["articulo"],
//                        cantidad,
//                        (string)prod["ClaveUnidad"],
//                        (string)prod["Unidad"],
//                        precio,
//                        (double)prod["descuento"],
//                        (double)prod["importe"],
//                        (string)prod["descripcion"],
//                        Math.Round(cantidad * precio * (ivaPct / 100), 2),
//                        (string)prod["objeto"],
//                        (string)prod["claveprod"]
//                    );
//                }

//                // Generar XML y timbrar
//                var resultadoTimbrado = GenerarXml(factura);
//                if (!resultadoTimbrado.Success)
//                {
//                    return Json(new { resultado = false, mensaje = resultadoTimbrado.Message });
//                }

//                return Json(new
//                {
//                    resultado = true,
//                    mensaje = resultadoTimbrado.Message,
//                    uuid = resultadoTimbrado.UUID,
//                    rutaQr = factura.RutaQr
//                });
//            }
//            catch (Exception ex)
//            {
//                return Json(new { resultado = false, mensaje = "Error al guardar la factura: " + ex.Message });
//            }
//        }


//        //    [Route("Operacion/Cotizacion/Datos")]
//        //    [HttpGet]
//        //    public IActionResult Datos()
//        //    {
//        //        var returnResult = new Dictionary<string, List<Dictionary<string, object>>>();

//        //        string queryMarcas = "SELECT * FROM sellosop.kdif";
//        //        string queryTipos = "SELECT * FROM sellosop.kdie";
//        //        string queryLineas = "SELECT * FROM sellosop.kdig";
//        //        //string queryNaturaleza = "SELECT idnaturaleza, nombre FROM naturalezas";
//        //        string queryUnidades = "SELECT * FROM sellosop.kdid";
//        //        var resultMDP = RunQuery(queryMarcas);
//        //        var resultTipos = RunQuery(queryTipos);
//        //        var resultLineas = RunQuery(queryLineas);
//        //        //var resultNaturaleza = RunQuery(queryNaturaleza);
//        //        var resultadoUDM = RunQuery(queryUnidades);

//        //        returnResult.Add("marcas", resultMDP);
//        //        returnResult.Add("tipos", resultTipos);
//        //        returnResult.Add("lineas", resultLineas);
//        //        //returnResult.Add("naturaleza", resultNaturaleza);
//        //        returnResult.Add("udm", resultadoUDM);
//        //        return Json(returnResult);
//        //    }

//        //    [Route("Operacion/Cotizacion/BuscarProducto")]
//        //    [HttpGet]
//        //    public IActionResult BuscarProducto(string idproducto)
//        //    {
//        //        var parameters = new Dictionary<string, object>();
//        //        string queryProducto = "SELECT * FROM sellosop.kdii WHERE c1 = @idproducto";
//        //        parameters.Add("idproducto", idproducto);
//        //        var result = RunQuery(queryProducto, parameters);
//        //        return Json(result);
//        //    }

//        //    [Route("Operacion/Cotizacion/Buscar")]
//        //    [HttpGet]
//        //    public IActionResult Buscar(string nombre, int page = 1, int pageSize = 50)
//        //    {
//        //        var parameters = new Dictionary<string, object>();
//        //        string query = " SELECT c1 AS id, c2 As descripcion " +
//        //                        "FROM sellosop.kdii " +
//        //                        "WHERE LOWER(c2) LIKE LOWER(@nombre) " +
//        //                        "OR LOWER(c1) LIKE LOWER(@nombre) " +
//        //                        "ORDER BY c1 " +
//        //                        "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

//        //        parameters.Add("nombre", $"%{nombre}%");
//        //        parameters.Add("offset", (page - 1) * pageSize); // Calcula el offset
//        //        parameters.Add("pageSize", pageSize); // Número de resultados por página

//        //        var result = RunQuery(query, parameters);
//        //        return Json(result);
//        //    }


//        [HttpPost]
//        public JsonResult GuardarCotizacion(IFormCollection form, string tipoDocto)
//        {
//            try
//            {
//                var parameters = new Dictionary<string, object>();
//                string cliente = form["cliente"];
//                string fecha = form["fecha"];
//                string moneda = form["moneda"];
//                string usuario = "admin"; // puedes obtenerlo de sesión si aplica
//                int idarea = int.Parse(form["idarea"]);
//                int tpdoc = int.Parse(form["tpdoc"]);
//                decimal importe = !string.IsNullOrWhiteSpace(form["importe"]) ? decimal.Parse(form["importe"]) : 0;
//                decimal iva = !string.IsNullOrWhiteSpace(form["iva"]) ? decimal.Parse(form["iva"]) : 0;
//                string rfc = form["rfc"];
//                string fol = form["folio"];
//                string consecutivo = fol.Split('-').Last();
//                string usr1 = User.Identity.Name;

//                // Obtener ID del usuario desde la BD
//                string idUserQuery = "SELECT usuarioid FROM usuarios WHERE nombreusuario = @nombre_usuario";
//                parameters.Add("nombre_usuario", usr1);
//                var userResult = RunQuery(idUserQuery, parameters);

//                // Crear encabezado
//                var encabezado = new DocumentoEncabezado
//                {
//                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
//                    IdArea = idarea,
//                    IdTpDoc = tpdoc,
//                    Anio = DateTime.Now.Year,
//                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
//                    Fch = DateTime.Parse(fecha),
//                    CliProv = cliente,
//                    TpMov = "COT",
//                    Coment1 = "Cotización generada desde el formulario web",
//                    UsrDoc = usuario,
//                    FchCap = DateTime.Now,
//                    Usr1 = Convert.ToInt32(userResult[0]["usuarioid"]),
//                    Imp = importe
//                };

//                // Parsear partidas JSON
//                string partidasJson = form["partidas"];
//                var partidasRaw = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(partidasJson);
//                var partidas = new List<PartidaDocumento>();
//                int num = 1;
//                foreach (var partida in partidasRaw)
//                {
//                    partidas.Add(new PartidaDocumento
//                    {
//                        NroPart = num++,
//                        CveProd = partida.ContainsKey("articulo") ? partida["articulo"] : "",
//                        DescrProd = partida.ContainsKey("descripcion") ? partida["descripcion"] : "",
//                        CantUd = partida.ContainsKey("cantidad") ? decimal.Parse(partida["cantidad"]) : 0,
//                        Ud = partida.ContainsKey("unidad") ? partida["unidad"] : "",
//                        PvProd = partida.ContainsKey("precioLista") ? decimal.Parse(partida["precioLista"]) : 0,
//                        ImpPart = (partida.ContainsKey("cantidad") ? decimal.Parse(partida["cantidad"]) : 0) *
//                                  (partida.ContainsKey("precioLista") ? decimal.Parse(partida["precioLista"]) : 0)
//                    });
//                }

//                switch (tipoDocto?.ToLower())
//                {
//                    case "alta":
//                        // ✅ Llamada adaptada a PostgreSQL
//                        var folio = GenerarDocumentoConPartidas(encabezado, partidas);
//                        string folio2 = folio["folio_generado"].ToString();
//                        return Json(new { ok = true, tipoDocto = "creado", folio2 });

//                    case "editar":
//                        EditarDocumentoConPartidas(encabezado, partidas, fol); // Adaptar esta también
//                        return Json(new { ok = true, tipoDocto = "editado" });

//                    case "eliminar":
//                        // EliminarDocumentoPostgres(encabezado); // Implementar si aplica
//                        return Json(new { ok = true, tipoDocto = "eliminado" });

//                    case "consultar":
//                        // var doc = ObtenerDocumentoPostgres(encabezado); // Implementar si aplica
//                        // return Json(new { ok = true, tipoDocto = "consultado", documento = doc });
//                        return Json(new { ok = false, message = "Consulta no implementada aún." });

//                    default:
//                        return Json(new { ok = false, message = "Acción no válida" });
//                }
//            }
//            catch (Exception ex)
//            {
//                return Json(new { ok = false, message = "Error al procesar: " + ex.Message });
//            }
//        }

//        public string EditarDocumentoConPartidas(DocumentoEncabezado encabezado, List<PartidaDocumento> partidas, string Folio)
//        {
//            using (var conn = new SqlConnection(_configuration.GetConnectionString("FacturacionDbContext3")))
//            {
//                conn.Open();
//                var transaction = conn.BeginTransaction();

//                try
//                {
//                    // Actualizar encabezado
//                    string updateEncabezado = @"
//                UPDATE sellosop.encabezadomov SET 
//                    fch = @fch,
//                    cli_prov = @cliProv,
//                    coment1 = @coment1,
//                    usr_doc = @usrDoc,
//                    fch_cap = @fchCap
//                WHERE 
//                    suc = @suc AND 
//                    nro_gpo_doc = @idArea AND 
//                    nro_tp_doc = @idTpDoc AND 
//                    anio = @anio";

//                    using (var cmd = new SqlCommand(updateEncabezado, conn, transaction))
//                    {
//                        cmd.Parameters.AddWithValue("@fch", encabezado.Fch);
//                        cmd.Parameters.AddWithValue("@cliProv", encabezado.CliProv);
//                        cmd.Parameters.AddWithValue("@coment1", encabezado.Coment1 ?? "");
//                        cmd.Parameters.AddWithValue("@usrDoc", encabezado.UsrDoc);
//                        cmd.Parameters.AddWithValue("@fchCap", encabezado.FchCap);
//                        cmd.Parameters.AddWithValue("@suc", encabezado.Suc);
//                        cmd.Parameters.AddWithValue("@idArea", encabezado.IdArea);
//                        cmd.Parameters.AddWithValue("@idTpDoc", encabezado.IdTpDoc);
//                        cmd.Parameters.AddWithValue("@anio", encabezado.Anio);

//                        cmd.ExecuteNonQuery();
//                    }

//                    // Eliminar partidas existentes
//                    string deletePartidas = @"
//                DELETE FROM sellosop.partidasdoc
//                WHERE 
//                    cve_suc = @suc AND 
//                    nro_gpo_mov = @idArea AND 
//                    nro_tp_mov = @idTpDoc AND 
//                    fol_doc = @folio";

//                    using (var cmd = new SqlCommand(deletePartidas, conn, transaction))
//                    {
//                        cmd.Parameters.AddWithValue("@suc", encabezado.Suc);
//                        cmd.Parameters.AddWithValue("@idArea", encabezado.IdArea);
//                        cmd.Parameters.AddWithValue("@idTpDoc", encabezado.IdTpDoc);
//                        cmd.Parameters.AddWithValue("@folio", Folio); // asegúrate de tener esta propiedad

//                        cmd.ExecuteNonQuery();
//                    }

//                    // Insertar nuevas partidas
//                    string insertPartida = @"
//                INSERT INTO sellosop.partidasdoc (
//                    cve_suc, nro_gpo_mov, nro_tp_mov, fol_doc,
//                    nro_part, cve_prod, descr_prod, cant_ud, ud, pv_prod, imp_part, fch
//                ) VALUES (
//                    @suc, @idArea, @idTpDoc, @folio,
//                    @nroPart, @cveProd, @descrProd, @cantUd, @ud, @pvProd, @impPart, @fch
//                )";

//                    foreach (var partida in partidas)
//                    {
//                        using (var cmd = new SqlCommand(insertPartida, conn, transaction))
//                        {
//                            cmd.Parameters.AddWithValue("@suc", encabezado.Suc);
//                            cmd.Parameters.AddWithValue("@idArea", encabezado.IdArea);
//                            cmd.Parameters.AddWithValue("@idTpDoc", encabezado.IdTpDoc);
//                            cmd.Parameters.AddWithValue("@folio", Folio);
//                            cmd.Parameters.AddWithValue("@nroPart", partida.NroPart);
//                            cmd.Parameters.AddWithValue("@cveProd", partida.CveProd);
//                            cmd.Parameters.AddWithValue("@descrProd", partida.DescrProd);
//                            cmd.Parameters.AddWithValue("@cantUd", partida.CantUd);
//                            cmd.Parameters.AddWithValue("@ud", partida.Ud);
//                            cmd.Parameters.AddWithValue("@pvProd", partida.PvProd);
//                            cmd.Parameters.AddWithValue("@impPart", partida.ImpPart);
//                            cmd.Parameters.AddWithValue("@fch", encabezado.Fch);

//                            cmd.ExecuteNonQuery();
//                        }
//                    }

//                    transaction.Commit();
//                    return Folio; // o un mensaje de éxito
//                }
//                catch (Exception ex)
//                {
//                    transaction.Rollback();
//                    throw new Exception("Error al editar el documento: " + ex.Message);
//                }
//            }
//        }




//        [HttpGet]
//        public IActionResult BuscarProducto(string idproducto)
//        {
//            var parameters = new Dictionary<string, object>();
//            string queryProducto = "SELECT * FROM sellosop.kdii WHERE c1 = @idproducto";
//            parameters.Add("idproducto", idproducto);
//            var result = RunQuery(queryProducto, parameters);
//            return Json(result);
//        }

//        [HttpGet]
//        public IActionResult Buscar(string nombre, int page = 1, int pageSize = 50)
//        {
//            var parameters = new Dictionary<string, object>();
//            string query = "SELECT gen, nat, fol_doc, cli_prov, imp, fch_cap " +
//                            "FROM encabezadomov " +
//                            "WHERE LOWER(fol_doc) LIKE LOWER(@nombre) " +
//                            "   OR LOWER(cli_prov) LIKE LOWER(@nombre) " +
//                            "ORDER BY fch_cap " +
//                            "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";


//            parameters.Add("nombre", $"%{nombre}%");
//            parameters.Add("offset", (page - 1) * pageSize); // Calcula el offset
//            parameters.Add("pageSize", pageSize); // Número de resultados por página

//            var result = RunQuery(query, parameters, false, null, null, "ERP_SRS");
//            return Json(result);
//        }

//        public JsonResult ObtenerDetalle(string folio, string nat, string gen)
//        {
//            var parameters = new Dictionary<string, object>();
//            var result = new Dictionary<string, List<Dictionary<string, object>>>();
//            string cotizacionQuery = "SELECT suc, gen, nat, nro_gpo_doc, nro_tp_doc, fol_doc,  " +
//                "ccy, alm, fch, cli_prov, refe, vdr_cpr, dto, iva, ieps_isr,  " +
//                "imp, pl_dias, fch_pg_entrega, dto1, dto2, dto3, rfc, iva_ret,  " +
//                "coment1, coment2, coment3, ped_adu, nro_coment_x_part,  " +
//                "nro_car_coment_x_part, tp_mov, n_cli, cl_cli, col_cli, " +
//                "pob_cli, nat_doc_anex, gpo_doc_anex, tp_doc_anex, fol_doc_anex, par,  " +
//                "fch_ref, saldo_doc, stat, cve_proy, cva_bco, cve_cli, dest_ch, n_pers,  " +
//                "mto_antic, com_vdr, mt_extra1, mt_extra2, mt_extra3, mt_extra4,  " +
//                "mt_extra5, mt_extra6, mt_extra7, mt_extra8, mt_extra9, mt_extra10, " +
//                "cve_dpto, hr_pg_entrega, bas_fol, usr_doc, fch_cap, hr_cap, nro_cot_prev,  " +
//                "ped_orig, stat_soltd_gto, cve_pais, cve_edo, cve_mpio, cve_conf, ped,  " +
//                "veh, cto_fte, cto_ad_fte, incoterm, tc_fte, peso_teor, usr_aut, coment_aut, " +
//                "mdp, fch_repgm, suc_doc_padre_char, gen_doc_padre_char, nat_doc_padre_char,  " +
//                "gpo_doc_padre_num, tpo_doc_padre_num, fol_doc_padre_char, doc_padre_compl,  " +
//                "cve_veh_emb, fch1, hr1, bda1, usr1, fch2, hr2, bda2, usr2, fch3, hr3, bda3, " +
//                "usr3, fch4, hr4, bda4, usr4, fch5, hr5, bda5, usr5, fch6, hr6, bda6, usr6 " +
//                "FROM encabezadomov " +
//                "WHERE fol_doc = @folio AND gen = @gen AND nat = @nat;";

//            string queryPartidas = "SELECT cve_suc, gen, nat, nro_gpo_mov, nro_tp_mov, fol_doc, nro_part,  " +
//                "cve_prod, cant_ud, descr_prod, ud, pv_prod, imp_part, dto1, iva, ieps,  " +
//                "nat_doc_ant, gpo_doc_ant, tp_doc_ant, fol_doc_ant, part_doc_ant, saldo_ud_part,  " +
//                "cve_cli, cto_vta_part, cve_vdr_cpr, refe, cve_alm, fch, mt_cto_, exis_prev_u,  " +
//                "exis_prev_peso, ccy, cto_ccy, vta_ccy, ot, conc " +
//                "FROM partidasdoc " +
//                "WHERE fol_doc = @folio AND gen = @gen AND nat = @nat;";

//            parameters.Add("folio", folio);
//            parameters.Add("gen", gen);
//            parameters.Add("nat", nat);

//            var cotizacionResult = RunQuery(cotizacionQuery, parameters);
//            var partidasResult = RunQuery(queryPartidas, parameters);

//            result.Add("cotizacionResult", cotizacionResult);
//            result.Add("partidasResult", partidasResult);

//            return Json(result);
//        }



//    }

//}