using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class InventarioController : Utilities
    {
        public JsonResult GetLocaciones()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT cp.cve_prod AS codigo, cp.descr_prod AS descripcion, cp.udm AS unidadproducto, ct.codigo AS tarima, " +
                "       cn.ulocation, tp.cantidad, cun.descripcion AS unidadnombre, ct.id_tarima, csu.descripcion, ct.id_tarima " +
                "FROM cattarimas ct " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cc.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales csu ON csu.id_sucursal = ca.sucursal_id " +
                "LEFT JOIN tarima_productos tp ON tp.tarima_id = ct.id_tarima " +
                "LEFT JOIN catproductos cp ON cp.id_catproductos = tp.producto_id AND cp.empresa_id = @empresa_id" +
                "LEFT JOIN catunidades cun ON cun.id_udm = tp.unidad " +
                "WHERE ca.tipo = 'Stock' AND csu.id_sucursal = @sucursal";
            parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var ubicaciones = RunQuery(query, parameters);

            return Json(new { ubicaciones });
        }

        [AuditAction(Modulo = "Inventarios", Accion = "Procesar productos en cuarentena, se genera documento de recepcion a stock o de donacion para producto que sale")]
        public JsonResult ProcesarCuarentena(IFormCollection fc)
        {
            try
            {
                var products = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(fc["productos"].ToString());
                var discrepancias = products.Where(p => p.ContainsKey("status") && !Convert.ToBoolean(p["status"])).ToList();
                var aceptados = products.Where(p => p.ContainsKey("status") && Convert.ToBoolean(p["status"])).ToList();
                var parameters = new Dictionary<string, object>();
                var docsGenerados = new List<string>();
                string html = "<ul>";

                if (aceptados.Count() > 0)
                {
                    parameters = new Dictionary<string, object>();
                    string query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Cuarentena'";
                    parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                    int origen = Convert.ToInt32(RunScalar(query, parameters));

                    query = "SELECT fch1, fch2, fch3, fch4, fch5, fch6, firma1, firma6, " +
                            "   usr1, usr2, usr3, usr4, usr5, usr6 " +
                            "FROM encabezadomov " +
                            "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    string motivo = "recepcion";
                    var encabezado = new DocumentoEncabezado();
                    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                    encabezado.IdArea = 5;
                    encabezado.IdTpDoc = 26;
                    encabezado.Anio = DateTime.Now.Year;
                    encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                    encabezado.Fch = DateTime.Now;
                    encabezado.TpMov = "INGINV";
                    encabezado.UsrDoc = User.Identity.Name;
                    encabezado.FchCap = DateTime.Now;
                    encabezado.Usr0 = GetUserId(User.Identity.Name);
                    encabezado.Fch0 = DateTime.Now;
                    encabezado.Usr1 = GetInt(usr["usr1"]);
                    encabezado.Fch1 = GetDate(usr["fch1"]);
                    encabezado.Firma1 = GetString(usr["firma1"]);
                    encabezado.Usr2 = GetInt(usr["usr2"]);
                    encabezado.Fch2 = GetDate(usr["fch2"]);
                    encabezado.Usr3 = GetInt(usr["usr3"]);
                    encabezado.Fch3 = GetDate(usr["fch3"]);
                    encabezado.Usr4 = GetInt(usr["usr4"]);
                    encabezado.Fch4 = GetDate(usr["fch4"]);
                    encabezado.Usr5 = GetInt(usr["usr5"]);
                    encabezado.Fch5 = GetDate(usr["fch5"]);
                    encabezado.Usr6 = GetInt(usr["usr6"]);
                    encabezado.Fch6 = GetDate(usr["fch6"]);
                    encabezado.Firma6 = GetString(usr["firma6"]);
                    encabezado.CliProv = GetString(aceptados[0]["proveedor_nombre"], "srs");
                    encabezado.Ref = GetInt(aceptados[0]["proveedor"]);
                    encabezado.EncabezadoPadre = GetInt(aceptados[0]["encabezado"]);
                    encabezado.Estatus = 11;
                    encabezado.UsrDep = GetAreaName(User.Identity.Name);
                    encabezado.TipoPoceso = "ingreso_almacen";
                    encabezado.Coment1 = fc["comentario"].ToString();

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();
                    var precioTotal = discrepancias.Sum(p => Convert.ToDecimal(p["idp"]));

                    foreach (var disc in aceptados)
                    {
                        motivo = "ingreso";
                        decimal? cantUd = GetDecimal(disc["cantidad"], 0);
                        decimal? pvProd = GetDecimal(disc["idp"], 0);

                        if (Convert.ToInt32(disc["cantidad"]) > 0)
                        {
                            partidas.Add(new PartidaDocumento
                            {
                                NroPart = nro++,
                                CveProd = GetString(disc["codigo"]),
                                DescrProd = GetString(disc["descripcion"]),
                                CantUd = cantUd,
                                CveVdrCpr = GetString(disc["proveedor_nombre"]),
                                Ref = GetInt(disc["proveedor"]),
                                Ud = disc["cve_unidad"].ToString(),
                                FolDocAnt = motivo,
                                PvProd = pvProd,
                                ImpPart = cantUd * pvProd,
                                //FPagoId = GetInt(disc["fp"]),
                                //Dto1 = GetDecimal(disc["dto"], 0),
                            });
                        }
                    }

                    // Crear una OC con las partidas aceptadas
                    //parameters = new Dictionary<string, object>();
                    //query = "SELECT * FROM clone_documento(@p_id_original, @p_total, @p_observaciones, @p_usuario, @p_partidas::jsonb)";
                    //parameters.Add("p_id_original", Convert.ToInt32(aceptados[0]["encabezado_padre"]));
                    //parameters.Add("p_total", precioTotal);
                    //parameters.Add("p_observaciones", fc["comentario"].ToString());
                    //parameters.Add("p_usuario", GetUserId(User.Identity.Name));
                    //parameters.Add("p_partidas", JsonConvert.SerializeObject(+));
                    //var poClonada = RunQuery(query, parameters)[0];
                    //docsGenerados.Add(poClonada["folio"].ToString());

                    // Revisar si la cantidad fue cambiara para crear el documento clon con las diferencias de cantidad
                    var variaciones = new List<Dictionary<string, object>>();
                    var documento = new Dictionary<string, object>();

                    foreach (var prod in aceptados.Concat(discrepancias))
                    {
                        int cantidadOriginal = Convert.ToInt32(prod["cantidad_original"]);
                        int cantidadRecibida = Convert.ToInt32(prod["cantidad"]);
                        int diferencia = cantidadRecibida - cantidadOriginal;

                        if (diferencia != 0) // hay exceso o faltante
                        {
                            var clon = new Dictionary<string, object>(prod);
                            clon["cantidad"] = diferencia; // la variación en positivo
                            clon["pv_prod"] = GetDecimal(prod["idp"]);
                            clon["tipo_variacion"] = diferencia > 0 ? "EXCESO" : "FALTANTE";
                            variaciones.Add(clon);
                        }
                    }

                    // Crear el documento clon del original
                    if (variaciones.Any())
                    {
                        var partidasVariacion = new List<PartidaDocumento>();
                        nro = 1;

                        foreach (var v in variaciones)
                        {
                            parameters = new Dictionary<string, object>();
                            query = "UPDATE tarima_productos SET cantidad = cantidad + (@diferencia) WHERE producto_id = @producto AND tarima_id = @ubicacion";
                            parameters.Add("diferencia", Convert.ToDecimal(v["cantidad"]));
                            parameters.Add("producto", Convert.ToInt32(v["id_producto"]));
                            parameters.Add("ubicacion", origen);
                            RunUpdate(query, parameters);

                            decimal? cantUd = GetDecimal(v["cantidad"], 0);
                            decimal? pvProd = GetDecimal(v["pv_prod"], 0);

                            partidasVariacion.Add(new PartidaDocumento
                            {
                                NroPart = nro++,
                                CveProd = GetString(v["codigo"]),
                                DescrProd = GetString(v["descripcion"]),
                                CantUd = cantUd, // la diferencia
                                PvProd = pvProd,
                                ImpPart = cantUd * pvProd,
                                CveVdrCpr = GetString(v["proveedor_nombre"]),
                                Ref = GetInt(v["proveedor"]),
                                Ud = GetString(v["cve_unidad"]),
                                FolDocAnt = "VARIACION-" + v["tipo_variacion"] // marcar si fue exceso o faltante
                            });
                        }

                        documento = GenerarDocumentoConPartidas(encabezado, partidas);
                        docsGenerados.Add(documento["folio_generado"].ToString());

                        encabezado.EncabezadoPadre = Convert.ToInt32(documento["IdEncabezado"]);
                        var documentoClon = GenerarDocumentoConPartidas(encabezado, partidasVariacion);

                        parameters = new Dictionary<string, object>();
                        query = "UPDATE encabezadomov SET fol_doc = 'E' || fol_doc WHERE id_encabezado = @id";
                        parameters.Add("id", Convert.ToInt32(documentoClon["IdEncabezado"]));
                        RunUpdate(query, parameters);
                        query = "SELECT gen || '-' || nat || '-' || fol_doc AS folio FROM encabezadomov " +
                            "WHERE id_encabezado = @id";
                        var folioClon = RunScalar(query, parameters);
                        docsGenerados.Add(folioClon.ToString());
                    }
                    else
                    {
                        documento = GenerarDocumentoConPartidas(encabezado, partidas);
                        docsGenerados.Add(documento["folio_generado"].ToString());
                    }

                    var productosPorDestino = aceptados.Where(p => p.ContainsKey("destino")).GroupBy(p => Convert.ToInt32(p["destino"])).ToList();

                    foreach (var grupo in productosPorDestino)
                    {
                        if (grupo.Any(p => Convert.ToInt32(p["cantidad"]) > 0))
                        {
                            int destino = grupo.Key;
                            var listaProductos = grupo.ToList();

                            RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name), "Movimiento", origen, destino, motivo, Convert.ToInt32(documento["IdEncabezado"]), null);
                        }
                    }

                    // Actualizar la descripcion del producto
                    foreach (var a in aceptados)
                    {
                        parameters = new Dictionary<string, object>();
                        query = "SELECT * FROM catproductos c WHERE c.cve_prod = @codigo AND c.empresa_id = @empresa_id";
                        parameters.Add("codigo", a["codigo"]);
                        parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                        var prod = RunQuery(query, parameters)[0];

                        if (prod["descr_prod"].ToString() != a["descripcion"].ToString())
                        {
                            parameters = new Dictionary<string, object>();
                            query = "UPDATE catproductos SET descr_prod = @descripcion WHERE cve_prod = @codigo AND empresa_id = @empresa_id";
                            parameters.Add("descripcion", a["descripcion"].ToString());
                            parameters.Add("codigo", a["codigo"]);
                            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                            RunUpdate(query, parameters);
                        }
                    }

                    foreach (var doc in docsGenerados)
                    {
                        html += $"<li>{doc}</li>";
                    }
                }
                else if (discrepancias.Count() > 0)
                {
                    var sinStock = new List<Dictionary<string, object>>();
                    string query = "";

                    foreach (var prod in discrepancias)
                    {
                        int productoId = Convert.ToInt32(prod["id_producto"]);
                        int cantidadSolicitada = Convert.ToInt32(prod["cantidad"]);

                        // Traemos el stock real disponible en cuarentena
                        query = @"
                            SELECT COALESCE(SUM(tp.cantidad), 0) 
                            FROM tarima_productos tp
                            INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                            INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                            INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                            INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                            INNER JOIN catalmacenes c ON c.id_almacen = cr.almacen_id
                            INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id
                            WHERE cs.id_sucursal = @sucursal
                            AND c.tipo = 'Cuarentena'
                            AND tp.producto_id = @productoId";

                        parameters = new Dictionary<string, object>
                        {
                            { "sucursal", HttpContext.Session.GetInt32("Sucursal") },
                            { "productoId", productoId }
                        };

                        int stockDisponible = Convert.ToInt32(RunScalar(query, parameters));

                        if (stockDisponible < cantidadSolicitada)
                        {
                            // Añadimos la info del producto y stock disponible
                            prod.Add("stockDisponible", stockDisponible);
                            sinStock.Add(prod);
                        }
                    }

                    if (sinStock.Any())
                    {
                        return Json(new
                        {
                            icon = "error",
                            title = "Algunos productos no tienen stock suficiente",
                            productos = sinStock
                        });
                    }
 
                    parameters = new Dictionary<string, object>();
                    query = "SELECT ct.id_tarima FROM catalmacenes c " +
                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Cuarentena'";
                    parameters.Add("sucursal", HttpContext.Session.GetInt32("Sucursal"));
                    int cuarentena = Convert.ToInt32(RunScalar(query, parameters));

                    query = "SELECT fch1, fch2, fch3, fch4, fch5, fch6, firma1, firma6, " +
                            "   usr1, usr2, usr3, usr4, usr5, usr6 " +
                            "FROM encabezadomov " +
                            "WHERE id_encabezado = @encabezado";
                    parameters = new Dictionary<string, object>();
                    parameters.Add("encabezado", Convert.ToInt32(fc["encabezado"].ToString()));
                    var usr = RunQuery(query, parameters)[0];

                    string motivo = "recepcion";

                    var encabezado = new DocumentoEncabezado();
                    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                    encabezado.IdArea = 5;
                    encabezado.IdTpDoc = 39;
                    encabezado.Anio = DateTime.Now.Year;
                    encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
                    encabezado.Fch = DateTime.Now;
                    encabezado.TpMov = "DONINV";
                    encabezado.UsrDoc = User.Identity.Name;
                    encabezado.FchCap = DateTime.Now;
                    encabezado.Usr0 = GetUserId(User.Identity.Name);
                    encabezado.Fch0 = DateTime.Now;
                    encabezado.Usr1 = GetInt(usr["usr1"]);
                    encabezado.Fch1 = GetDate(usr["fch1"]); // ----> aqui
                    encabezado.Firma1 = GetString(usr["firma1"]);
                    encabezado.Usr2 = GetInt(usr["usr2"]);
                    encabezado.Fch2 = GetDate(usr["fch2"]);
                    encabezado.Usr3 = GetInt(usr["usr3"]);
                    encabezado.Fch3 = GetDate(usr["fch3"]);
                    encabezado.Usr4 = GetInt(usr["usr4"]);
                    encabezado.Fch4 = GetDate(usr["fch4"]);
                    encabezado.Usr5 = GetInt(usr["usr5"]);
                    encabezado.Fch5 = GetDate(usr["fch5"]);
                    encabezado.Usr6 = GetInt(usr["usr6"]);
                    encabezado.Fch6 = GetDate(usr["fch6"]);
                    encabezado.Firma6 = GetString(usr["firma6"]);
                    encabezado.CliProv = GetString(discrepancias[0]["proveedor_nombre"], "srs");
                    encabezado.Ref = GetInt(discrepancias[0]["proveedor"]);
                    encabezado.EncabezadoPadre = GetInt(discrepancias[0]["encabezado"]);
                    encabezado.Estatus = 11;
                    encabezado.UsrDep = GetAreaName(User.Identity.Name);
                    encabezado.TipoPoceso = "salida-donacion";
                    encabezado.Coment1 = fc["comentario"].ToString();

                    int nro = 1;
                    var partidas = new List<PartidaDocumento>();
                    var precioTotal = discrepancias.Sum(p => Convert.ToDecimal(p["idp"]));

                    foreach (var disc in discrepancias)
                    {
                        motivo = "ingreso";
                        decimal? cantUd = GetDecimal(disc["cantidad"], 0);
                        decimal? pvProd = GetDecimal(disc["idp"], 0);
                        PartidaDocumento partida = new PartidaDocumento();

                        partida.NroPart = nro++;
                        partida.CveProd = GetString(disc["codigo"]);
                        partida.DescrProd = GetString(disc["descripcion"]);
                        partida.CantUd = cantUd;
                        partida.CveVdrCpr = GetString(disc["proveedor_nombre"]);
                        partida.Ref = GetInt(disc["proveedor"]);
                        partida.Ud = disc["cve_unidad"].ToString();
                        partida.FolDocAnt = motivo;
                        partida.PvProd = pvProd;
                        partida.ImpPart = cantUd * pvProd;
                        //partida.FPagoId = GetInt(disc["fp"]);
                        //partida.Dto1 = GetDecimal(disc["dto"], 0);

                        partidas.Add(partida);
                    }

                    var documento = GenerarDocumentoConPartidas(encabezado, partidas);
                    docsGenerados.Add(documento["folio_generado"].ToString());

                    var productosPorDestino = discrepancias.Where(p => p.ContainsKey("destino")).GroupBy(p => Convert.ToInt32(p["destino"])).ToList();

                    foreach (var grupo in productosPorDestino)
                    {
                        int destino = grupo.Key;
                        var listaProductos = grupo.ToList();

                        RegistrarMovimiento(listaProductos, GetUserId(User.Identity.Name), "Movimiento", cuarentena, null, motivo, Convert.ToInt32(documento["IdEncabezado"]), null);
                    }

                    foreach (var doc in docsGenerados)
                    {
                        html += $"<li>{doc}</li>";
                    }
                }

                html += "</ul>";
                return Json(new { icon = "success", title = "Cuarentena procesada exitosamente", html = $"Se crearon correctamente los folios: {html}" });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error al procesar la cuarentena", html = $"{ex.Message}", showCancelButton = false });
            }
        }
    }
}