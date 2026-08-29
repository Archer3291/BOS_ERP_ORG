using ClosedXML.Excel;
using BOS_ERP.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Configuration;
using System.Text;
namespace BOS_ERP.Controllers.Compras
{
    [Authorize]
    public partial class DatosGeneralesController : Utilities
    {
        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryMoneda = "SELECT clave as id, nombre as nombre FROM cat_monedas ORDER BY clave DESC";
            string queryVendedor = "SELECT clave_vendedor as id,nombre as nombre FROM vendedores ORDER BY nombre DESC";   //sellosop.kduv
            string queryFormasPago = "SELECT cve_sat as id,descripcion as nombre FROM cat_f_pago ORDER BY cve_sat DESC";
            string queryMetodoPago = "SELECT cve_mdp as id ,descripcion as nombre FROM mdp ORDER BY cve_mdp DESC";
            string queryUsoCFDI = "SELECT clave as id, descripcion as nombre FROM catusocfdi ORDER BY clave DESC";
            string queryIncoterms = "SELECT id_icoterm, codigo as id, descripcion as nombre, activo FROM catalogo_incoterms;";
            string tasaCambio = "SELECT id, fecha, dolar, euro, peso, creation_date FROM tasas_cambio ORDER BY fecha DESC LIMIT 1;";
            string sucursales = "SELECT id_sucursal as id, cve_sucursal as nombre FROM catsucursales ORDER BY cve_sucursal;";
            string almacenes = "SELECT id_almacen as id, descripcion as nombre, sucursal_id FROM catalmacenes ORDER BY descripcion;";
            string bancos = "select codigo as id, nombre from cuentas_finanzas where codigo LIKE '%1-1-02%';";
            string paises = "SELECT cve_iso as id, nombre FROM paises ORDER BY nombre ASC";
            result.Add("monedas", RunQuery(queryMoneda));
            result.Add("vendedores", RunQuery(queryVendedor));
            result.Add("formaspago", RunQuery(queryFormasPago));
            result.Add("metodopago", RunQuery(queryMetodoPago));
            result.Add("usocfdi", RunQuery(queryUsoCFDI));
            result.Add("incoterms", RunQuery(queryIncoterms));
            result.Add("tasas", RunQuery(tasaCambio));
            result.Add("sucursales", RunQuery(sucursales));
            result.Add("almacenes", RunQuery(almacenes));
            result.Add("bancos", RunQuery(bancos));
            result.Add("paises", RunQuery(paises));

            return Json(result);
        }

        /// <summary>
        /// ¿El documento (o alguno de sus padres) ya tiene una autorización de crédito
        /// aprobada por gerencia? Lo consulta el módulo CreditoVentas del front para no
        /// volver a bloquear en remisión y factura lo que ya se autorizó en el pedido.
        /// </summary>
        /// <param name="token">
        /// Token de la solicitud, para los documentos que aún no se han guardado (un pedido
        /// capturado sin cotización de origen no tiene encabezado al que ligar la autorización).
        /// </param>
        public IActionResult ConsultarAutorizacionCredito(
            int documentoId, string cliente = "", string token = "", decimal total = 0m)
        {
            try
            {
                bool autorizado = BOS_ERP.Helpers.CreditoVentasHelper
                    .ExisteAutorizacionAprobada(this, documentoId, cliente, null, null, token, total);

                return Json(new { success = true, autorizado });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/ConsultarAutorizacionCredito");
                // Ante un fallo se responde "no autorizado": el front bloquea y el backend
                // vuelve a validar al guardar, así que nunca se deja pasar de más.
                return Json(new { success = false, autorizado = false, message = ex.Message });
            }
        }

        public IActionResult DatosCentroCostos()
        {
            string query = "SELECT areaid, nombre FROM areas;";
            var result = RunQuery(query);
            return Json(new { result, success = true });
        }
        public IActionResult DatosBancos()
        {
            string query = "select codigo, nombre from cuentas_finanzas where codigo LIKE '%1-1-02%';";
            var result = RunQuery(query);
            return Json(new { result, success = true });
        }
        public IActionResult BuscarCliente(string id)
        {
            var parameters = new Dictionary<string, object>();

            // Primero obtenemos los datos del cliente
            string queryCliente = @"SELECT cc.cve_cli AS id, cc.n_cli AS descripcion, cc.dir, cc.idf, 
        cc.col, cc.pob, cc.rfc, cc.cve_vdr, cc.cp, cc.cod_ant, 
        df.forma_pago, df.uso_sugerido, df.regimen_fiscal, df.calle, df.no_exterior, cc.estatus_cliente::text AS estatus_cliente,
        df.no_interior, df.colonia, df.localidad, df.municipio, df.estado, df.pais, cc.clasificacion::text AS clasificacion,
        df.codigo_postal, cc.es_especial, cc.lim_crd, cc.cod_ant, cc.id_cliente,
        COALESCE((SELECT SUM(ca.saldo_pendiente) FROM cartera_clientes ca WHERE ca.cliente_id = cc.id_cliente AND ca.cancelada = false), 0) AS credito_usado
        FROM catclientes cc 
        LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli  
        WHERE cve_cli = @id AND cc.empresa_id = @empresa_id";

            parameters.Add("id", id);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var cliente = RunQuery(queryCliente, parameters);


            if (cliente != null && cliente.Count > 0)
            {
                var row = cliente[0];

                decimal limite = row["lim_crd"] != null ? Convert.ToDecimal(row["lim_crd"]) : 0;
                decimal usado = row["credito_usado"] != null ? Convert.ToDecimal(row["credito_usado"]) : 0;

                string estatusCredito = "";
                decimal porcentajeUso = 0;

                if (limite <= 0)
                {
                    estatusCredito = "SIN_LIMITE";
                }
                else
                {
                    porcentajeUso = (usado / limite) * 100;

                    if (usado >= limite)
                    {
                        estatusCredito = "EXCEDIDO";
                    }
                    else if (porcentajeUso >= 80)
                    {
                        estatusCredito = "POR_VENCER"; // o "PROXIMO_LIMITE"
                    }
                    else
                    {
                        estatusCredito = "DISPONIBLE";
                    }
                }

                row["credito_usado"] = usado;
                row["credito_disponible"] = limite - usado;
                row["porcentaje_uso"] = porcentajeUso;
                row["estatus_credito"] = estatusCredito;

                // lo que ya tenías
                if (row.ContainsKey("cod_ant"))
                {
                   HttpContext.Session.SetString("idCliente", row["id_cliente"]?.ToString());
                }

                //row["adendas"] = adendas;
            }


            // Luego obtenemos las adendas ACTIVAS del cliente
            string queryAdendas = @"SELECT 
        cdf.id_addenda, 
        cdf.nombre, 
        cdf.xml_namespace, 
        cdf.xml_prefix, 
        cdf.version, 
        cdf.data_template,
        cdf.usar_conceptos,
        cdf.created_at,
        cdf.updated_at
        FROM cfdi_addenda_def cdf 
        INNER JOIN catclientes cc ON cdf.id_cliente = cc.id_cliente AND cc.empresa_id = @empresa_id 
        WHERE cc.cve_cli = @id AND cdf.activo = true
        ORDER BY cdf.nombre";

            var adendas = RunQuery(queryAdendas, parameters);

            // Combinamos los resultados
            if (cliente != null && cliente.Count > 0)
            {
                var row = cliente[0];
                if (row.ContainsKey("cod_ant"))
                {
                    HttpContext.Session.SetString("idCliente", row["id_cliente"]?.ToString());
                }

                // Agregamos las adendas al resultado
                row["adendas"] = adendas;
            }

            return Json(cliente);
        }

        public IActionResult BuscarC(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            // Consulta para obtener los datos de la página
            string query = "SELECT cve_cli AS id, n_cli AS descripcion FROM catclientes " +
                           "WHERE (LOWER(cve_cli) LIKE LOWER(@nombre) " +
                           "OR LOWER(n_cli) LIKE LOWER(@nombre)) AND empresa_id = @empresa_id AND  (estatus_cliente = 'activo' OR estatus_cliente = 'suspendido')" +
                           "ORDER BY cve_cli " +
                           "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Consulta para obtener el total de registros
            string queryTotal = "SELECT COUNT(*) AS total FROM catclientes " +
                                "WHERE (LOWER(cve_cli) LIKE LOWER(@nombre) " +
                                "OR LOWER(n_cli) LIKE LOWER(@nombre)) AND empresa_id = @empresa_id";
            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        //public IActionResult BuscarP(string nombre, int page = 1, int pageSize = 50)
        //{
        //    var parameters = new Dictionary<string, object>();

        //    // Consulta para obtener los datos de la página
        //    string query = "SELECT cve_prod AS id, descr_prod AS descripcion FROM catproductos " +
        //                   "WHERE LOWER(descr_prod) LIKE LOWER(@nombre) " +
        //                   "OR LOWER(cve_prod) LIKE LOWER(@nombre) " +
        //                   "ORDER BY cve_prod " +
        //                   "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

        //    parameters.Add("nombre", $"%{nombre}%");
        //    parameters.Add("offset", (page - 1) * pageSize);
        //    parameters.Add("pageSize", pageSize);

        //    var items = RunQuery(query, parameters);

        //    // Consulta para obtener el total de registros
        //    string queryTotal = "SELECT COUNT(*) AS total FROM catproductos " +
        //                        "WHERE LOWER(descr_prod) LIKE LOWER(@nombre) " +
        //                        "OR LOWER(cve_prod) LIKE LOWER(@nombre)";
        //    var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
        //    int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

        //    return Json(new { items, total });
        //}

        public IActionResult BuscarP(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            // 'cantidad' se mantiene por compatibilidad (varias remisiones lo leen como
            // existencia), pero ahora sí respeta sucursal y tipo de almacén: antes era un
            // SUM sin filtros que mezclaba Stock, Modula y todas las sucursales.
            string query = $@"SELECT
                                cp.id_catproductos AS id,
                                cp.cve_prod,
                                cp.descr_prod AS descripcion,
                                cp.udm AS unidadproducto,
                                cp.es_tubo,
                                cp.prod_modula,
                                cun.descripcion AS unidadnombre,
                                {SqlExistencia("cp.id_catproductos", "Stock")}  AS existencia_stock,
                                {SqlExistencia("cp.id_catproductos", "Modula")} AS existencia_modula,
                                {SqlExistencia("cp.id_catproductos", "Stock")}
                                    + {SqlExistencia("cp.id_catproductos", "Modula")} AS cantidad
                            FROM catproductos cp
                            LEFT JOIN catunidades cun ON cun.cve_udm = cp.udm
                            WHERE cp.empresa_id = @empresa_id
                            AND (
                                  LOWER(cp.descr_prod) LIKE LOWER(@nombre)
                                  OR LOWER(cp.cve_prod) LIKE LOWER(@nombre)
                                )
                            ORDER BY cp.cve_prod
                            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
                            ";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var items = RunQuery(query, parameters);

            // El conteo debe usar exactamente el mismo criterio que la lista; antes filtraba
            // por almacén 'Stock' con existencia > 0 mientras la lista no filtraba nada, así
            // que la paginación nunca correspondía a los renglones devueltos.
            string queryTotal = @"SELECT COUNT(*) AS total
                                FROM catproductos cp
                                WHERE cp.empresa_id = @empresa_id
                                  AND (
                                        LOWER(cp.descr_prod) LIKE LOWER(@nombre)
                                        OR LOWER(cp.cve_prod) LIKE LOWER(@nombre)
                                      );";
            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarPCotizacion(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            // Consulta para obtener los datos de la página


            string query = "SELECT cve_prod AS id, descr_prod AS descripcion FROM catproductos " +
                                "WHERE (LOWER(descr_prod) LIKE LOWER(@nombre) " +
                                "OR LOWER(cve_prod) LIKE LOWER(@nombre))  " +
                                "AND empresa_id = @empresa_id " +
                                "ORDER BY cve_prod " +
                                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";


            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var items = RunQuery(query, parameters);

            // Consulta para obtener el total de registros
            string queryTotal = "SELECT COUNT(*) AS total FROM catproductos " +
                                  "WHERE LOWER(descr_prod) LIKE LOWER(@nombre) " +
                                  "OR LOWER(cve_prod) LIKE LOWER(@nombre) " +
                                  "AND empresa_id = @empresa_id";
            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }


        public IActionResult BuscarPCotizacionTubos(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            // Consulta para obtener los datos de la página


            string query = "SELECT cve_prod AS id, descr_prod AS descripcion FROM catproductos " +
                                "WHERE (LOWER(descr_prod) LIKE LOWER(@nombre) " +
                                "OR LOWER(cve_prod) LIKE LOWER(@nombre))  " +
                                "AND empresa_id = @empresa_id AND es_tubo = true " +
                                "ORDER BY cve_prod " +
                                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";


            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            var items = RunQuery(query, parameters);

            // Consulta para obtener el total de registros
            string queryTotal = "SELECT COUNT(*) AS total FROM catproductos " +
                                  "WHERE LOWER(descr_prod) LIKE LOWER(@nombre) " +
                                  "OR LOWER(cve_prod) LIKE LOWER(@nombre) " +
                                  "AND empresa_id = @empresa_id";
            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" }, { "sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        /// <summary>
        /// Cliente contra el que se calculan los precios. Prioriza el que manda el documento;
        /// la sesión queda solo como respaldo para los canales que todavía no lo envían.
        /// Session["idCliente"] es global al usuario, así que dos pestañas con clientes
        /// distintos se pisaban entre sí y podían cotizar con la lista de precios ajena.
        /// </summary>
        private int ClienteParaPrecios(int? clienteId)
        {
            // Si el documento mandó el parámetro, manda él — incluso si es 0. Un 0 explícito
            // significa "todavía no hay cliente", y entonces el precio correcto es el de
            // lista base, no el del cliente que quedó en la sesión.
            //
            // Antes el 0 caía a la sesión igual que la ausencia del parámetro, así que
            // agregar un producto antes de elegir cliente cotizaba con el último cliente
            // consultado por ese usuario en cualquier pestaña: de ahí que el mismo producto
            // saliera con un precio distinto en cada documento.
            if (clienteId.HasValue) return clienteId.Value > 0 ? clienteId.Value : 0;

            // Sin parámetro: canal que todavía no lo manda (nacionales, sucursales,
            // internacionales). Se conserva el comportamiento anterior para no cambiarles
            // los precios sin querer.
            int.TryParse(HttpContext.Session.GetString("idCliente"), out int deSesion);
            return deSesion;
        }

        public IActionResult BuscarProducto(string id, int? clienteId = null)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                // Se acepta tanto id_catproductos como cve_prod, y se compara como texto.
                // El endpoint recibe las dos cosas: el modal de búsqueda manda
                // id_catproductos (BuscarP lo devuelve como 'id'), pero las partidas ya
                // capturadas guardan cve_prod, porque esta misma consulta devuelve
                // 'p.cve_prod AS id'. Con el Convert.ToInt32 anterior, recotizar una
                // partida reventaba (o peor: con claves numéricas encontraba OTRO producto
                // y traía su precio), así que al cambiar de cliente no se actualizaba nada.
                parameters.Add("id", (id ?? "").Trim());
                parameters.Add("sucursal", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                parameters.Add("cliente_id", ClienteParaPrecios(clienteId));

                // 'existencia' sigue siendo el total (Stock + Modula) para no romper a los
                // consumidores existentes, pero ahora se devuelve además el desglose, que es
                // lo que necesita el pedido para decidir el ruteo Modula/Stock.
                string queryProducto = $@"
        SELECT
            p.cve_prod AS id,
            p.descr_prod AS descripcion,
            p.cod_prov,
            p.udm,
            p.pv_prov,
            p.iva_prod,
            p.es_tubo,
            p.prod_modula,
            rel.prod_sat AS clavesat,
            rel.ud_sat AS udmsat,

            -- MOTOR DE PRECIOS
            -- obtener_precio_final es RETURNS TABLE: invocarla en el SELECT devolvía un
            -- record compuesto, no un número, y 'precio' llegaba al front como texto
            -- ('(1453.63,0,...)'). Se consulta con LATERAL, igual que BuscarProductoCotizacion.
            pr.precio_final     AS precio,
            pr.precio_minimo,
            pr.descuento_sugerido,
            pr.descuento_maximo,
            pr.aplicar_automatico,
            pr.tipo_regla,

            {SqlExistencia("p.id_catproductos", "Stock")}  AS existencia_stock,
            {SqlExistencia("p.id_catproductos", "Modula")} AS existencia_modula,
            {SqlExistencia("p.id_catproductos", "Stock")}
                + {SqlExistencia("p.id_catproductos", "Modula")} AS existencia,
            {SqlExistenciaPorAlmacen("p.id_catproductos", "Stock")}  AS existencia_por_almacen_stock,
            {SqlExistenciaPorAlmacen("p.id_catproductos", "Modula")} AS existencia_por_almacen_modula

        FROM catproductos p
        LEFT JOIN catrelacion rel ON rel.prod_kepler = p.cve_prod
        CROSS JOIN LATERAL obtener_precio_final(@empresa_id, @cliente_id, p.cve_prod) AS pr
        WHERE p.empresa_id = @empresa_id
          AND (p.cve_prod = @id OR p.id_catproductos::text = @id)
        -- La clave gana sobre el id: si en el catálogo hay claves numéricas, un mismo
        -- valor podría casar con las dos columnas y el resultado sería impredecible.
        ORDER BY CASE WHEN p.cve_prod = @id THEN 0 ELSE 1 END
        LIMIT 1;
        ";

                var result = RunQuery(queryProducto, parameters);

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/BuscarProducto");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <param name="clienteId">
        /// Cliente del documento que se está capturando. Determina la lista de precios y las
        /// reglas por cliente. Si no se manda se cae a la sesión, que es lo que hacían todos
        /// los canales: con dos pestañas abiertas con clientes distintos, la última búsqueda
        /// de cliente se llevaba los precios de la otra.
        /// </param>
        public IActionResult BuscarProductoCotizacion(string id, int? clienteId = null)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id", id);
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                parameters.Add("sucursalId", HttpContext.Session.GetInt32("Sucursal"));

                parameters.Add("cliente_id", ClienteParaPrecios(clienteId));

                string queryProducto = @"
                                        SELECT
                                            p.cve_prod          AS id,
                                            p.descr_prod        AS descripcion,
                                            p.cod_prov,
                                            p.udm,
                                            p.pv_prov,
                                            p.iva_prod,
                                            rel.prod_sat        AS clavesat,
                                            rel.ud_sat          AS udmsat,
                                            pr.precio_final     AS precio,
                                            pr.precio_minimo,
                                            pr.descuento_sugerido,
                                            pr.descuento_maximo,
                                            pr.aplicar_automatico,
                                            pr.tipo_regla,
                                            COALESCE((
                                                SELECT SUM(tp.cantidad)
                                                FROM tarima_productos tp
                                                INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                                                INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                                                INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                                                INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                                                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                                                INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                                                WHERE ca.tipo = 'Stock' 
                                                  AND cs.id_sucursal = @sucursalId
                                                  AND tp.producto_id = p.id_catproductos
                                                GROUP BY tp.producto_id
                                            ), 0) AS existencia
                                        FROM catproductos p
                                        LEFT JOIN catrelacion rel ON rel.prod_kepler = p.cve_prod
                                        CROSS JOIN LATERAL obtener_precio_final(
                                            @empresa_id, @cliente_id, p.cve_prod
                                        ) AS pr
                                        WHERE p.cve_prod = @id
                                          AND p.empresa_id = @empresa_id;
                ";

                var result = RunQuery(queryProducto, parameters);

                // ── Stock desde la base SRS ──────────────────────────────────────
                if (result != null && result.Count > 0)
                {
                    decimal stockSRS = 0;
                    var stockDetalle = new List<Dictionary<string, object>>();


                    string stockQuery = @"
                        SELECT 
                            k.c1            AS clave_sucursal,
                            k2.c2           AS sucursal,
                            k.c2            AS clave_almacen,
                            k3.c3           AS almacen,
                            COALESCE(SUM(k.c8 - k.c9), 0) AS existencia
                        FROM sellosop.kdil k
                        LEFT JOIN sellosop.kdms k2 ON k2.c1 = k.c1
                        LEFT JOIN sellosop.kdiq k3 ON k3.c2 = k.c2 AND k2.c1 = k3.c1
                        WHERE k.c3 = @cve_prod
                        GROUP BY k.c1, k.c2, k.c3, k2.c2, k3.c3
                        ORDER BY k.c1, k.c2";



                    parameters.Add("cve_prod", id.ToString());

                    stockDetalle = RunQuery(stockQuery, parameters, false, null, null, "SRS");

                    foreach (var item in stockDetalle)
                    {
                        if (item.ContainsKey("existencia") && item["existencia"] != null)
                        {
                            stockSRS += Convert.ToDecimal(item["existencia"]);
                        }
                    }
                    result[0]["stock_srs"] = stockSRS;
                    result[0]["stock_detalle"] = stockDetalle;   // lista completa por sucursal/almacén
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/BuscarProductoCotizacion");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult BuscarProductoCotizacionTubos(string id, int? clienteId = null)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters.Add("id", id);
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                parameters.Add("sucursalId", HttpContext.Session.GetInt32("Sucursal"));

                parameters.Add("cliente_id", ClienteParaPrecios(clienteId));

                string queryProducto = @"
                                        SELECT
                                            p.cve_prod          AS id,
                                            p.descr_prod        AS descripcion,
                                            p.cod_prov,
                                            p.udm,
                                            p.pv_prov,
                                            p.iva_prod,
                                            rel.prod_sat        AS clavesat,
                                            rel.ud_sat          AS udmsat,
                                            pr.precio_final     AS precio,
                                            pr.precio_minimo,
                                            pr.descuento_sugerido,
                                            pr.descuento_maximo,
                                            pr.aplicar_automatico,
                                            pr.tipo_regla,
                                            COALESCE((
                                                SELECT SUM(tp.cantidad)
                                                FROM tarima_productos tp
                                                INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                                                INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                                                INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                                                INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                                                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                                                INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                                                WHERE ca.tipo = 'Stock' 
                                                  AND cs.id_sucursal = @sucursalId
                                                  AND tp.producto_id = p.id_catproductos
                                                GROUP BY tp.producto_id
                                            ), 0) AS existencia
                                        FROM catproductos p
                                        LEFT JOIN catrelacion rel ON rel.prod_kepler = p.cve_prod
                                        CROSS JOIN LATERAL obtener_precio_final(
                                            @empresa_id, @cliente_id, p.cve_prod
                                        ) AS pr
                                        WHERE p.cve_prod = @id
                                          AND p.empresa_id = @empresa_id;
                ";

                var result = RunQuery(queryProducto, parameters);

                // ── Stock desde la base SRS ──────────────────────────────────────
                if (result != null && result.Count > 0)
                {
                    decimal stockSRS = 0;
                    var stockDetalle = new List<Dictionary<string, object>>();


                    string stockQuery = @"
                        SELECT 
                            k.c1            AS clave_sucursal,
                            k2.c2           AS sucursal,
                            k.c2            AS clave_almacen,
                            k3.c3           AS almacen,
                            COALESCE(SUM(k.c8 - k.c9), 0) AS existencia
                        FROM sellosop.kdil k
                        LEFT JOIN sellosop.kdms k2 ON k2.c1 = k.c1
                        LEFT JOIN sellosop.kdiq k3 ON k3.c2 = k.c2 AND k2.c1 = k3.c1
                        WHERE k.c3 = @cve_prod
                        GROUP BY k.c1, k.c2, k.c3, k2.c2, k3.c3
                        ORDER BY k.c1, k.c2";



                    parameters.Add("cve_prod", id.ToString());

                    stockDetalle = RunQuery(stockQuery, parameters, false, null, null, "SRS");

                    foreach (var item in stockDetalle)
                    {
                        if (item.ContainsKey("existencia") && item["existencia"] != null)
                        {
                            stockSRS += Convert.ToDecimal(item["existencia"]);
                        }
                    }
                    result[0]["stock_srs"] = stockSRS;
                    result[0]["stock_detalle"] = stockDetalle;   // lista completa por sucursal/almacén
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/BuscarProductoCotizacionTubos");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ====================================================================
        // VALIDAR DESCUENTO / PRECIO — genera token de un solo uso
        // ====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ValidarDescuento(IFormCollection fc)
        {
            return ValidarYGenerarToken(fc, "DESCUENTO");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ValidarPrecio(IFormCollection fc)
        {
            return ValidarYGenerarToken(fc, "CAMBIO DE PRECIO");
        }

        private JsonResult ValidarYGenerarToken(IFormCollection fc, string modulo)
        {
            try
            {
                TokenStore.LimpiarExpirados();

                string usuario = User.Identity.Name;

                //if (GetUserRole(usuario, "ERP_SRS") != "Gerente")
                //    return Json(new
                //    {
                //        success = false,
                //        message = "Solo un Gerente puede autorizar cambios de precio o descuentos."
                //    });

                string contrasena = fc["Password"].ToString();

                if (string.IsNullOrWhiteSpace(contrasena))
                    return Json(new { success = false, message = "La contraseña es obligatoria." });

                var rUser = RunQuery(
                    "SELECT usuarioid FROM usuarios WHERE nombreusuario = @user LIMIT 1",
                    new Dictionary<string, object> { { "@user", usuario } });

                if (rUser == null || rUser.Count == 0)
                    return Json(new { success = false, message = "Usuario no encontrado." });

                int usuarioId = Convert.ToInt32(((IDictionary<string, object>)rUser[0])["usuarioid"]);

                var resultado = ValidarAcceso(usuarioId, modulo, contrasena);

                if (!resultado.Exito)
                    return Json(new { success = false, message = resultado.Mensaje });

                string token = Guid.NewGuid().ToString("N");

                TokenStore.Guardar(token, new TokenStore.AuthToken
                {
                    Usuario = usuario,
                    Tipo = modulo,
                    Expira = DateTime.UtcNow.AddMinutes(60),
                    Usado = false
                });

                return Json(new { success = true, token = token });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/ValidarYGenerarToken");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult ObtenerPermisosUsuario()
        {
            try
            {
                string usuario = User.Identity.Name;

                var rUser = RunQuery(
                    "SELECT usuarioid FROM usuarios WHERE nombreusuario = @user LIMIT 1",
                    new Dictionary<string, object> { { "@user", usuario } });

                if (rUser == null || rUser.Count == 0)
                    return Json(new { tienePrecio = false, tieneDescuento = false });

                int usuarioId = Convert.ToInt32(((IDictionary<string, object>)rUser[0])["usuarioid"]);

                // Solo aplica si acceso_directo = true — el permiso de contraseña es irrelevante aquí
                var query = @"
            SELECT cm.modulo_id
            FROM clave_modulo_usuarios cmu
            INNER JOIN claves_modulo cm ON cm.id = cmu.clave_id
            WHERE cmu.usuario_id = @uid
              AND cmu.activo = true
              AND cmu.acceso_directo = true          -- ★ solo acceso sin contraseña
              AND cm.activo = true
              AND (cmu.bloqueado_hasta IS NULL OR cmu.bloqueado_hasta < NOW())";

                var permisos = RunQuery(query, new Dictionary<string, object> { { "@uid", usuarioId } });

                var modulosDirectos = permisos?
                    .Select(r => ((IDictionary<string, object>)r)["modulo_id"]?.ToString())
                    .ToHashSet() ?? new HashSet<string>();

                bool tienePrecio = modulosDirectos.Contains("CAMBIO DE PRECIO");
                bool tieneDescuento = modulosDirectos.Contains("DESCUENTO");

                string tokenPrecio = null;
                string tokenDescuento = null;

                if (tienePrecio)
                {
                    tokenPrecio = Guid.NewGuid().ToString("N");
                    TokenStore.Guardar(tokenPrecio, new TokenStore.AuthToken
                    {
                        Usuario = usuario,
                        Tipo = "CAMBIO DE PRECIO",
                        Expira = DateTime.UtcNow.AddHours(8),
                        Usado = false
                    });
                }

                if (tieneDescuento)
                {
                    tokenDescuento = Guid.NewGuid().ToString("N");
                    TokenStore.Guardar(tokenDescuento, new TokenStore.AuthToken
                    {
                        Usuario = usuario,
                        Tipo = "DESCUENTO",
                        Expira = DateTime.UtcNow.AddHours(8),
                        Usado = false
                    });
                }

                return Json(new { tienePrecio, tieneDescuento, tokenPrecio, tokenDescuento });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/ObtenerPermisosUsuario");
                return Json(new { tienePrecio = false, tieneDescuento = false, error = ex.Message });
            }
        }

        public IActionResult BuscarD(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VNCOT'
        AND em.suc =  @suc
        AND em.estatus_id = 1
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VNCOT'";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }
        public IActionResult BuscarDocumento(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id", id },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"))}
                };

                // 🔹 Consulta del encabezado
                string queryEncabezado = @"
            SELECT  
                em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0  
                         THEN '-' || num_to_letters(em.variacion)  
                         ELSE '' END AS folio,
                em.id_encabezado,
                em.encabezados_padre,
                em.suc,
                em.alm,
                em.gen,
                em.nat,
                em.usr0,
                em.fch0,
                em.cli_prov,
                em.coment1,
                em.coment_aut,
                em.ccy,
                em.vdr_cpr,
                em.flete,
                em.incoterm,
                em.mdp,
                em.tipo_proceso,
                cfp.cve_sat as f_pago,
                em.cfdi,
                cc.rfc,
                em.par,
                cc.lim_crd,
                cc.n_cli,
                cc.pl_crd,
                -- Condiciones de pago propias del documento. pl_crd es el plazo del CATÁLOGO
                -- del cliente, no el que se capturó: sin estos dos campos el siguiente paso
                -- del flujo (remisión / factura) no tiene de dónde heredar lo configurado en
                -- el pedido y termina recalculando la fecha como hoy + plazo del catálogo.
                -- TO_CHAR porque el input es de tipo date y necesita YYYY-MM-DD sin hora.
                em.pl_dias,
                TO_CHAR(em.fch_pg_entrega, 'YYYY-MM-DD') AS fecha_pago,
                cc.dir,
                cc.id_cliente,
                f.uuid,
                df.forma_pago, 
                df.uso_sugerido, 
                df.regimen_fiscal, 
                df.calle, 
                df.no_exterior, 
                df.no_interior, 
                df.colonia, 
                df.localidad, 
                df.municipio, 
                df.estado,                
                em.orden_compra as ordenCompra, 
                df.pais, 
                df.codigo_postal,
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli,
                cc.estatus_cliente::text AS estatus_cliente,
                cc.clasificacion::text AS clasificacion,
                COALESCE((
                    SELECT SUM(ca.saldo_pendiente)
                    FROM cartera_clientes ca
                    WHERE ca.cliente_id = cc.id_cliente
                    AND ca.cancelada = false
                ), 0) AS credito_usado
            FROM encabezadomov em
            LEFT JOIN catclientes cc  
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
            LEFT JOIN factura f ON f.encabezado_id = em.id_encabezado
            LEFT JOIN cat_f_pago cfp ON cfp.id_f_pago = em.f_pago
            WHERE em.id_encabezado = @id;
        ";

                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encabezadoResult.First();

                decimal limite = 0;
                decimal usado = 0;
                decimal disponible = 0;
                decimal porcentajeUso = 0;
                string estatusCredito = "SIN_LIMITE";
                string estatus_cliente = encabezado["estatus_cliente"].ToString();
                string clasificacion = encabezado["clasificacion"].ToString();

                // 🔹 Obtener valores seguros
                if (encabezado.ContainsKey("lim_crd") && encabezado["lim_crd"] != null)
                    limite = Convert.ToDecimal(encabezado["lim_crd"]);

                if (encabezado.ContainsKey("credito_usado") && encabezado["credito_usado"] != null)
                    usado = Convert.ToDecimal(encabezado["credito_usado"]);

                // 🔹 Calcular disponible
                disponible = limite - usado;

                // 🔹 Evaluar estatus
                if (limite <= 0)
                {
                    estatusCredito = "SIN_LIMITE";
                }
                else
                {
                    porcentajeUso = (usado / limite) * 100;

                    if (usado >= limite)
                        estatusCredito = "EXCEDIDO";
                    else if (porcentajeUso >= 80)
                        estatusCredito = "POR_VENCER";
                    else
                        estatusCredito = "DISPONIBLE";
                }

                // 🔹 Agregar al resultado
                encabezado["credito_usado"] = usado;
                encabezado["credito_disponible"] = disponible;
                encabezado["porcentaje_credito"] = porcentajeUso;
                encabezado["estatus_credito"] = estatusCredito;
                encabezado["estatus_cliente"] = estatus_cliente;
                encabezado["clasificacion"] = clasificacion;

                // 🔹 Obtener TODAS las adendas del cliente
                if (encabezado.ContainsKey("id_cliente") && encabezado["id_cliente"] != null)
                {
                    var clienteId = encabezado["id_cliente"];
                    var parametersAdendas = new Dictionary<string, object>
            {
                { "id_cliente", clienteId }
            };

                    string queryAdendas = @"
                SELECT 
                    id_addenda, 
                    nombre, 
                    xml_namespace, 
                    xml_prefix, 
                    version, 
                    data_template,
                    usar_conceptos,
                    created_at,
                    updated_at
                FROM cfdi_addenda_def 
                WHERE id_cliente = @id_cliente 
                  AND activo = true
                ORDER BY nombre";

                    var adendasResult = RunQuery(queryAdendas, parametersAdendas);
                    encabezado["adendas"] = adendasResult;
                }

                // 🔹 Parámetro de sucursal
                int sucursal = Convert.ToInt32(encabezado["suc"]);
                parameters.Add("sucursal", sucursal);

                // 🔹 Consulta de partidas con EXISTENCIAS
                // Antes 'existencia' sumaba Stock y Modula en un solo número, mientras que al
                // agregar un producto a mano (BuscarProducto) sólo traía Stock: la misma
                // columna significaba cosas distintas según cómo llegara el renglón.
                string queryPartidas = $@"
            SELECT
    pd.id_partidas AS id,
    pd.cve_prod AS producto_id,
    pd.descr_prod AS descripcion,
    pd.cant_ud AS cantidad,
    pd.pv_prod AS precio,
    pd.dto1 AS descuento,
    pd.ud AS unidad,
    pd.imp_part AS importe,
    pd.iva,
    pd.ieps,
    pd.fch AS fecha,
    pd.cve_alm AS almacen,
    pd.cto_vta_part AS costo,
    pd.ccy AS moneda,
    pd.pedimento,
    pd.tp_doc_ant AS comentario,
    cprod.es_tubo,
    cprod.prod_modula,
    {SqlExistencia("pd.producto_id", "Stock")}  AS existencia_stock,
    {SqlExistencia("pd.producto_id", "Modula")} AS existencia_modula,
    {SqlExistencia("pd.producto_id", "Stock")}
        + {SqlExistencia("pd.producto_id", "Modula")} AS existencia,

    -- 🔹 NUEVO: pedimentos agrupados
    COALESCE(peds.pedimentos, '[]') AS pedimentos

FROM partidasdoc pd

LEFT JOIN encabezadomov em
    ON em.id_encabezado = pd.encabezado_id

LEFT JOIN catproductos cprod
    ON cprod.id_catproductos = pd.producto_id

LEFT JOIN LATERAL (
    SELECT json_agg(
        json_build_object(
            'pedimento', rc.pedimento,
            'cantidad', ABS(rc.cantidad)
        )
    ) AS pedimentos
    FROM registro_compras rc
    WHERE rc.producto_id = pd.producto_id
      AND rc.encabezado_venta = pd.encabezado_id
) peds ON true

WHERE pd.encabezado_id = @id
ORDER BY pd.nro_part;
        ";

                var partidasResult = RunQuery(queryPartidas, parameters);

                // 🔹 Unimos encabezado + productos
                encabezado["productos"] = partidasResult;

                // ✅ Envolvemos en lista para mantener el formato con índice 0
                var result = new List<Dictionary<string, object>> { encabezado };

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/?");
                return Json(new { success = false, message = ex.Message });
            }
        }


        public IActionResult BuscarDocumentoEspecialInternacional(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "id", id }
        };

                // 🔹 Consulta del encabezado
                string queryEncabezado = @"
            SELECT  
                em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0  
                         THEN '-' || num_to_letters(em.variacion)  
                         ELSE '' END AS folio,
                em.id_encabezado,
                em.encabezados_padre,
                em.suc,
                em.alm,
                em.gen,
                em.nat,
                em.usr0,
                em.fch0,
                em.cli_prov,
                em.coment1,
                em.coment_aut,
                em.ccy,
                em.vdr_cpr,
                em.flete,
                em.incoterm,
                em.mdp,
                cfp.cve_sat as f_pago,
                em.cfdi,
                cc.rfc,
                em.par,
                cc.lim_crd,
                cc.n_cli,
                cc.pl_crd,
                cc.dir,
                cc.idf,
                f.uuid,
                em.orden_compra as ordenCompra, 
                df.forma_pago, 
                df.uso_sugerido, df.regimen_fiscal, df.calle, df.no_exterior, df.no_interior, 
                df.colonia, df.localidad, df.municipio, df.estado, df.pais, df.codigo_postal,
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli
            FROM encabezadomov em
            LEFT JOIN catclientes cc  
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
            LEFT JOIN factura f ON f.encabezado_id = em.id_encabezado
            LEFT JOIN cat_f_pago cfp ON cfp.id_f_pago = em.f_pago
            WHERE em.id_encabezado = @id;
        ";
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encabezadoResult.First();

                // 🔹 Parámetro de sucursal (lo obtenemos del encabezado)
                int sucursal = Convert.ToInt32(encabezado["suc"]);
                parameters.Add("sucursal", sucursal);

                // 🔹 Consulta de partidas con EXISTENCIAS
                string queryPartidas = @"
            SELECT 
                pd.id_partidas AS id,
                pd.cve_prod AS producto_id,
                pd.descr_prod AS descripcion,
                pd.cant_ud AS cantidad,
                pd.pv_prod AS precio,
                pd.dto1 AS descuento,
                pd.ud AS unidad,
                pd.imp_part AS importe,
                pd.iva,
                pd.ieps,
                pd.fch AS fecha,
                pd.cve_alm AS almacen,
                pd.cto_vta_part AS costo,
                pd.ccy AS moneda,
                pd.pedimento,     
                pd.tp_doc_ant AS comentario,
                COALESCE(stk.cantidadStock, 0) AS existencia
            FROM partidasdoc pd
            LEFT JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
            LEFT JOIN (
                SELECT 
                    tp.producto_id, 
                    cr.almacen_id, 
                    cs.id_sucursal, 
                    SUM(tp.cantidad) AS cantidadStock
                FROM tarima_productos tp
                INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                WHERE ca.tipo = 'Temporal'
                GROUP BY tp.producto_id, cr.almacen_id, cs.id_sucursal
            ) stk 
                ON stk.producto_id = pd.producto_id 
               AND stk.id_sucursal = @sucursal
            WHERE pd.encabezado_id = @id
            ORDER BY pd.nro_part;
        ";

                var partidasResult = RunQuery(queryPartidas, parameters);

                // 🔹 Unimos encabezado + productos
                encabezado["productos"] = partidasResult;

                // ✅ Envolvemos en lista para mantener el formato con índice 0
                var result = new List<Dictionary<string, object>> { encabezado };

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/?");
                return Json(new { success = false, message = ex.Message });
            }
        }


        public IActionResult BuscarDped(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            em.incoterm,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VNPED'
        AND em.suc =  @suc
        AND em.estatus_id = 1
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VNPED'";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarDrem(string nombre, int page = 1, int pageSize = 50, string cliente = "")
        {
            var parameters = new Dictionary<string, object>();

            // Filtro opcional de cliente (igual que BuscarDVIremAgrupadas).
            string filtroCliente = !string.IsNullOrWhiteSpace(cliente)
                ? "AND em.cli_prov = @cliente"
                : "";

            // Mismo filtrado que la versión industrial BuscarDVIremAgrupadas:
            //   · nat = 'VNREM' (remisión nacional)
            //   · em.suc = @suc            → solo la sucursal actual
            //   · em.estatus_id = 1        → solo remisiones ABIERTAS (no las ya facturadas)
            //   · NOT EXISTS (...)         → excluir las que ya se facturaron por completo (tracking)
            // Antes BuscarDrem no filtraba por estatus/sucursal, así que listaba remisiones
            // ya facturadas (por eso "filtraba mal").
            string query = $@"
        SELECT
            em.id_encabezado,
            em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                CASE WHEN em.variacion > 0
                     THEN '-' || num_to_letters(em.variacion)
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            em.incoterm,
            cc.n_cli
        FROM encabezadomov em
        INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
        WHERE em.nat = 'VNREM'
          AND em.suc = @suc
          AND em.estatus_id = 1
          AND (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
            OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
          )
          AND NOT EXISTS (
              -- Excluir las que ya fueron facturadas completamente
              SELECT 1
              FROM remision_partidas_facturadas rpf2
              WHERE rpf2.encabezado_remision_id = em.id_encabezado
                AND (
                    (SELECT COUNT(*) FROM remision_partidas_facturadas rpf3
                     WHERE rpf3.encabezado_remision_id = em.id_encabezado) > 0
                    AND
                    (SELECT COUNT(*) FROM remision_partidas_facturadas rpf4
                     WHERE rpf4.encabezado_remision_id = em.id_encabezado
                       AND rpf4.estatus != 'completa') = 0
                )
              LIMIT 1
          )
          {filtroCliente}
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            if (!string.IsNullOrWhiteSpace(cliente))
                parameters.Add("cliente", cliente);

            var items = RunQuery(query, parameters);

            // Total de registros — MISMO filtrado (antes faltaba el JOIN de empresa y los
            // filtros de sucursal/estatus, por lo que el total no cuadraba con los items).
            string queryTotal = $@"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
        WHERE em.nat = 'VNREM'
          AND em.suc = @suc
          AND em.estatus_id = 1
          AND (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
            OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
          )
          AND NOT EXISTS (
              SELECT 1
              FROM remision_partidas_facturadas rpf2
              WHERE rpf2.encabezado_remision_id = em.id_encabezado
                AND (
                    (SELECT COUNT(*) FROM remision_partidas_facturadas rpf3
                     WHERE rpf3.encabezado_remision_id = em.id_encabezado) > 0
                    AND
                    (SELECT COUNT(*) FROM remision_partidas_facturadas rpf4
                     WHERE rpf4.encabezado_remision_id = em.id_encabezado
                       AND rpf4.estatus != 'completa') = 0
                )
              LIMIT 1
          )
          {filtroCliente}";

            var totalParameters = new Dictionary<string, object>
            {
                { "nombre", $"%{nombre}%" },
                { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
                { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
            };
            if (!string.IsNullOrWhiteSpace(cliente))
                totalParameters.Add("cliente", cliente);

            var totalResult = RunQuery(queryTotal, totalParameters);
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }


        public IActionResult BuscarAnt(string nombre, int page = 1, int pageSize = 50, string cliente = "0")
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.folio ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            cc.n_cli,
            f.saldo
        FROM encabezadomov em
        INNER JOIN catclientes cc ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
        INNER JOIN factura f ON f.encabezado_id = em.id_encabezado
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat IN ('VNFAC', 'VIFAC', 'VSFAC', 'VINFAC')
        AND f.saldo > 0
        AND tipo_proceso = 'factura_anticipo'
        AND (cli_prov = @cliente)  -- 🔥 Filtro por cliente
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("cliente", cliente);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros (también debe considerar el filtro de cliente)
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        INNER JOIN factura f ON f.encabezado_id = em.id_encabezado
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))
        -- Mismos filtros que la consulta principal. Antes contaba solo VNFAC y sin saldo:
        -- en los demás canales el total salía 0 y la paginación del modal no aparecía.
        AND em.nat IN ('VNFAC', 'VIFAC', 'VSFAC', 'VINFAC')
        AND f.saldo > 0
        AND em.tipo_proceso = 'factura_anticipo'
        AND (em.cli_prov = @cliente)";

            var totalParameters = new Dictionary<string, object>
            {
        { "nombre", $"%{nombre}%" },
        { "cliente", cliente }
            };

            var totalResult = RunQuery(queryTotal, totalParameters);
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarAnticipo(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "id", id }
        };

                // 🔹 Consulta del encabezado
                string queryEncabezado = @"
            SELECT  
                em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0  
                         THEN '-' || num_to_letters(em.variacion)  
                         ELSE '' END AS folio,
                em.id_encabezado,
                em.encabezados_padre,
                em.suc,
                em.alm,
                em.gen,
                em.nat,
                em.usr0,
                em.fch0,
                em.cli_prov,
                em.coment1,
                em.coment_aut,
                em.ccy,
                em.vdr_cpr,
                em.flete,
                em.incoterm,
                em.mdp,
                em.f_pago,
                em.cfdi,
                em.sub,
                em.imp,
                f.saldo,
                cc.rfc,
                em.par,
                cc.lim_crd,
                cc.pl_crd,
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli
            FROM encabezadomov em
            INNER JOIN catclientes cc  
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            INNER JOIN factura f ON f.encabezado_id = em.id_encabezado
            WHERE em.id_encabezado = @id;
        ";
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encabezadoResult.First();

                // 🔹 Parámetro de sucursal (lo obtenemos del encabezado)
                int sucursal = Convert.ToInt32(encabezado["suc"]);
                parameters.Add("sucursal", sucursal);

                var result = new List<Dictionary<string, object>> { encabezado };

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/BuscarAnticipo");
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult DatosCambioDolar()
        {
            string query = "SELECT dolar FROM tasas_cambio ORDER BY creation_date DESC LIMIT 1;";
            var result = RunQuery(query); // ← Devuelve List<Dictionary<string, object>>

            if (result == null || result.Count == 0)
            {
                return Json(new { success = false, message = "No hay tasas registradas." });
            }

            // Tomamos el primer registro (última tasa)
            var dolar = result[0]["dolar"];

            return Json(new { success = true, valor = dolar });
        }



        public IActionResult BuscarFacturaDetalle(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "id", id }
        };

                // 🔹 Consulta del encabezado
                string queryEncabezado = @"
SELECT  
    -- FOLIO ARMADO
    em.folio ||
        CASE WHEN em.variacion > 0  
             THEN '-' || num_to_letters(em.variacion)  
             ELSE '' END AS folio,

    -- DATOS PRINCIPALES
    em.id_encabezado,
    em.encabezados_padre,
    em.suc,
    em.alm,
    em.gen,
    em.nat,
    em.usr0,
    em.fch0,
    em.cli_prov,
    em.coment1,
    em.coment_aut,
    em.ccy,
    em.vdr_cpr,
    em.flete,
    em.incoterm,
    em.mdp,
    em.f_pago,
    em.cfdi,
    cc.rfc,
    em.par,
    cc.lim_crd,
    cc.n_cli,
    cc.pl_crd,
    cc.dir,

    -- CFDI
    f.uuid,
    f.total,

    -- Datos fiscales cliente
    df.forma_pago, 
    df.uso_sugerido,
    df.regimen_fiscal,
    df.calle, df.no_exterior, df.no_interior, 
    df.colonia, df.localidad, df.municipio, df.estado, df.pais, df.codigo_postal,

    -- Información cliente unificada
    cc.dir || CHR(10) ||
    cc.col || CHR(10) ||
    cc.pob || CHR(10) ||
    cc.cp AS info_cli,

    -- CARTERA ACTUAL
    ccx.id_cartera_cliente,
    ccx.fecha_emision,
    ccx.fecha_vencimiento,
    ccx.monto_total,

    em.ccy AS moneda_cartera,
    ccx.estado AS estado_cartera,
    ccx.fecha_vencimiento,

    -- ⭐ SALDO PENDIENTE REAL (SUMADO CON HIJOS)
    ccx.saldo_pendiente as saldo_pendiente_real

FROM encabezadomov em
INNER JOIN catclientes cc  ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id

LEFT JOIN direcciones_facturacion df 
    ON df.entidad_clave = cc.cve_cli

LEFT JOIN factura f 
    ON f.encabezado_id = em.id_encabezado

LEFT JOIN cartera_clientes ccx
    ON ccx.encabezado_id = em.id_encabezado
WHERE em.id_encabezado = @id;
";

                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encabezadoResult.First();

                // 🔹 Parámetro de sucursal (lo obtenemos del encabezado)
                int sucursal = Convert.ToInt32(encabezado["suc"]);
                parameters.Add("sucursal", sucursal);

                // 🔹 Consulta de partidas con EXISTENCIAS
                string queryPartidas = @"
            SELECT 
                pd.id_partidas AS id,
                pd.cve_prod AS producto_id,
                pd.descr_prod AS descripcion,
                pd.cant_ud AS cantidad,
                pd.pv_prod AS precio,
                pd.dto1 AS descuento,
                pd.ud AS unidad,
                pd.imp_part AS importe,
                pd.iva,
                pd.ieps,
                pd.fch AS fecha,
                pd.cve_alm AS almacen,
                pd.cto_vta_part AS costo,
                pd.ccy AS moneda,
                COALESCE(stk.cantidadStock, 0) AS existencia
            FROM partidasdoc pd
            LEFT JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
            LEFT JOIN (
                SELECT 
                    tp.producto_id, 
                    cr.almacen_id, 
                    cs.id_sucursal, 
                    SUM(tp.cantidad) AS cantidadStock
                FROM tarima_productos tp
                INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id
                INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id
                INNER JOIN catcolumnas cc ON cc.id_columna = cn.columna_id
                INNER JOIN catracks cr ON cr.id_rack = cc.rack_id
                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                WHERE ca.tipo = 'Stock'
                GROUP BY tp.producto_id, cr.almacen_id, cs.id_sucursal
            ) stk 
                ON stk.producto_id = pd.producto_id 
               AND stk.id_sucursal = @sucursal
            WHERE pd.encabezado_id = @id
            ORDER BY pd.nro_part;
        ";

                var partidasResult = RunQuery(queryPartidas, parameters);

                // 🔹 Unimos encabezado + productos
                encabezado["productos"] = partidasResult;

                // ✅ Envolvemos en lista para mantener el formato con índice 0
                var result = new List<Dictionary<string, object>> { encabezado };

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/?");
                return Json(new { success = false, message = ex.Message });
            }
        }



        public IActionResult BuscarFacturasPendientes(string busqueda = "", string cliente = "", int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT
            f.id,
            f.serie,
            em.folio,
            f.uuid,
            f.fecha,
            f.total,
            em.f_pago,
            em.mdp,
            em.id_encabezado,
            em.cli_prov,
            cc.saldo_pendiente as saldo_pendiente_real
        FROM factura f
        LEFT JOIN encabezadomov em 
            ON em.id_encabezado = f.encabezado_id 
        INNER JOIN cartera_clientes cc ON cc.encabezado_id = em.id_encabezado
        WHERE em.f_pago = 22
          AND em.mdp = 'PPD'
          AND cc.saldo_pendiente > 0
    ";

            // Filtro cliente
            if (!string.IsNullOrEmpty(cliente))
            {
                query += " AND em.cli_prov = @cliente";
                parameters.Add("cliente", cliente);
            }

            // Filtro búsqueda
            if (!string.IsNullOrEmpty(busqueda))
            {
                query += @" AND (
            f.folio ILIKE '%' || @busqueda || '%'
            OR CAST(f.uuid AS TEXT) ILIKE '%' || @busqueda || '%'
            OR CAST(f.fecha AS TEXT) ILIKE '%' || @busqueda || '%'
            OR CAST(f.total AS TEXT) ILIKE '%' || @busqueda || '%'
        )";
                parameters.Add("busqueda", $"%{busqueda}%");
            }

            // ==================================
            // Query total con misma lógica
            // ==================================
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM factura f
        LEFT JOIN encabezadomov em 
            ON em.id_encabezado = f.encabezado_id
         INNER JOIN cartera_clientes cc ON cc.encabezado_id = em.id_encabezado
        WHERE em.f_pago = 22
          AND em.mdp = 'PPD'
          AND cc.saldo_pendiente > 0
    ";

            if (!string.IsNullOrEmpty(cliente))
                queryTotal += " AND em.cli_prov = @cliente";

            if (!string.IsNullOrEmpty(busqueda))
                queryTotal += @" AND (
            f.folio ILIKE '%' || @busqueda || '%'
            OR CAST(f.uuid AS TEXT) ILIKE '%' || @busqueda || '%'
            OR CAST(f.fecha AS TEXT) ILIKE '%' || @busqueda || '%'
            OR CAST(f.total AS TEXT) ILIKE '%' || @busqueda || '%'
        )";

            // Total
            var resultTotal = RunQuery(queryTotal, parameters);
            int total = resultTotal != null && resultTotal.Count > 0
                ? Convert.ToInt32(resultTotal[0]["total"])
                : 0;

            // Paginación
            query += " ORDER BY f.fecha DESC OFFSET @offset LIMIT @pageSize ";
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);

            var items = RunQuery(query, parameters);

            return Json(new { items, total });
        }


        public IActionResult BuscarFacturasVenta(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>();

            string query = @"
        SELECT 
            em.id_encabezado,
            em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                CASE WHEN em.variacion > 0 
                     THEN '-' || num_to_letters(em.variacion) 
                     ELSE '' END AS folio,
            em.gen,
            em.nat,
            em.fch,
            em.imp,
            em.cli_prov,
            em.usr0,
            cc.n_cli
        FROM encabezadomov em
        INNER join catclientes cc on cc.id_cliente =  em.refe AND cc.empresa_id = @empresa_id
        WHERE (
               LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
            OR LOWER(em.gen) LIKE LOWER(@nombre)
            OR LOWER(em.nat) LIKE LOWER(@nombre)
            OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        )
        AND em.nat = 'VIFAC'
        AND em.suc =  @suc
        AND em.estatus_id = 1
        ORDER BY em.fch DESC
        OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            var items = RunQuery(query, parameters);

            // Total de registros
            string queryTotal = @"
        SELECT COUNT(*) AS total
        FROM encabezadomov em
        WHERE (LOWER(em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc) LIKE LOWER(@nombre)
           OR LOWER(em.gen) LIKE LOWER(@nombre)
           OR LOWER(em.nat) LIKE LOWER(@nombre)
           OR LOWER(em.cli_prov) LIKE LOWER(@nombre))  AND em.nat = 'VIFAC'";

            var totalResult = RunQuery(queryTotal, new Dictionary<string, object> { { "nombre", $"%{nombre}%" } });
            int total = totalResult != null && totalResult.Count > 0 ? Convert.ToInt32(totalResult[0]["total"]) : 0;

            return Json(new { items, total });
        }

        public IActionResult BuscarPartidasRemision(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "id", id } };

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");

                // ── Inicializar tracking (idempotente) ───────────────
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        var insParam = new Dictionary<string, object>
                {
                    { "enc",     id },
                    { "usuario", User.Identity.Name ?? "sistema" }
                };

                        RunQuery(@"
                    INSERT INTO remision_partidas_facturadas
                        (encabezado_remision_id, id_partida_remision, cve_prod,
                         cantidad_original, cantidad_facturada, precio_unitario,
                         descuento, usuario_registro)
                    SELECT
                        @enc,
                        pd.id_partidas,
                        pd.cve_prod,
                        pd.cant_ud,
                        0,
                        pd.pv_prod,
                        COALESCE(pd.dto1, 0),
                        @usuario
                    FROM partidasdoc pd
                    WHERE pd.encabezado_id = @enc
                    ON CONFLICT (encabezado_remision_id, id_partida_remision)
                    DO NOTHING",
                            insParam, false, conn, tx);

                        tx.Commit();
                    }
                }

                // ── Partidas: solo las pendientes/parciales ──────────
                // (El JS solo necesita las que aún tienen saldo; las
                //  completas no deben aparecer en el modal de selección)
                string queryPartidas = @"
    SELECT
        pd.id_partidas                                AS id,
        rpf.id                                        AS id_tracking,
        pd.id_partidas                                AS id_partida_remision, -- ← ANTES ERA rpf.id
        pd.cve_prod,
        pd.descr_prod                                 AS descripcion,
        pd.ud                                         AS unidad,
        rpf.cantidad_original,
        rpf.cantidad_facturada,
        rpf.cantidad_pendiente,
        rpf.precio_unitario,
        rpf.descuento,
        rpf.estatus,
        pd.pedimento,  
        pd.tp_doc_ant AS comentario,
        -- 🔹 NUEVO: pedimentos agrupados
        COALESCE(peds.pedimentos, '[]') AS pedimentos,
        ROUND(
            rpf.cantidad_pendiente
            * rpf.precio_unitario
            * (1 - rpf.descuento / 100.0), 2
        ) AS importe_pendiente_partida
    FROM partidasdoc pd
LEFT JOIN LATERAL (
    SELECT json_agg(
        json_build_object(
            'pedimento', rc.pedimento,
            'cantidad', ABS(rc.cantidad)
        )
    ) AS pedimentos
    FROM registro_compras rc
    WHERE rc.producto_id = pd.producto_id
      AND rc.encabezado_venta = pd.encabezado_id
) peds ON true
    INNER JOIN remision_partidas_facturadas rpf
        ON rpf.encabezado_remision_id = @id
       AND rpf.id_partida_remision    = pd.id_partidas  -- ← join por id_partidas real
    WHERE pd.encabezado_id = @id
      AND rpf.estatus IN ('pendiente', 'parcial')
    ORDER BY pd.nro_part";

                var partidas = RunQuery(queryPartidas, parameters);

                // ── Encabezado ───────────────────────────────────────
                string queryEnc = @"
            SELECT
                em.gen || '-' || em.nat || '-' ||
                    EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
                    CASE WHEN em.variacion > 0
                         THEN '-' || num_to_letters(em.variacion)
                         ELSE '' END AS folio,
                em.cli_prov,
                em.id_encabezado,
                cc.n_cli
            FROM encabezadomov em
            INNER JOIN catclientes cc
                ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
            WHERE em.id_encabezado = @id";

                var encParam = new Dictionary<string, object>
        {
            { "id",         id },
            { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
        };
                var encabezado = RunQuery(queryEnc, encParam);

                return Json(new
                {
                    success = true,
                    result = new[]
                    {
                new
                {
                    id_encabezado = id,
                    folio    = encabezado.FirstOrDefault()?["folio"]?.ToString()    ?? "",
                    cli_prov = encabezado.FirstOrDefault()?["cli_prov"]?.ToString() ?? "",
                    n_cli    = encabezado.FirstOrDefault()?["n_cli"]?.ToString()    ?? "",
                    productos = partidas
                }
            }
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Remisiones agrupadas por documento padre, para el modal "Buscar Documentos" de la
        // factura. Antes solo existía la versión industrial con 'VIREM' incrustado; ahora el
        // nat entra por parámetro y Sucursales usa exactamente la misma consulta.
        public IActionResult BuscarDVIremAgrupadas(
            string nombre = "", int page = 1, int pageSize = 25, string cliente = "")
            => BuscarRemisionesAgrupadasJson("VIREM", nombre, page, pageSize, cliente);

        public IActionResult BuscarDVSremAgrupadas(
            string nombre = "", int page = 1, int pageSize = 25, string cliente = "")
            => BuscarRemisionesAgrupadasJson("VSREM", nombre, page, pageSize, cliente);

        private IActionResult BuscarRemisionesAgrupadasJson(
            string nat, string nombre, int page, int pageSize, string cliente)
        {
            var parameters = new Dictionary<string, object>();

            // Filtro opcional de cliente
            string filtroCliente = !string.IsNullOrWhiteSpace(cliente)
                ? "AND em.cli_prov = @cliente"
                : "";

            // ── Query principal ──────────────────────────────────────────────────
            // Agrupa por "clave de factura":
            //   · Si la remisión tiene encabezados_padre > 0  → la clave es encabezados_padre
            //   · Si no tiene padre                           → la clave es su propio id
            //
            // De cada grupo expone:
            //   · grupo_id          → id a usar en el detalle (el padre si existe, sino el propio)
            //   · remisiones_ids    → todos los id_encabezado separados por coma
            //   · total_remisiones  → cuántas remisiones forman el grupo
            //   · folio             → folio de la primera (o único) remisión
            //   · cli_prov, n_cli   → datos del cliente
            //   · fch, imp          → fecha e importe TOTAL del grupo
            //   · partidas_pendientes / partidas_parciales / importe_pendiente
            //     (suma de todas las remisiones del grupo)
            string query = $@"
WITH grupos AS (
    SELECT
        COALESCE(
            NULLIF(em.encabezados_padre, 0),
            em.id_encabezado
        ) AS grupo_id,
        em.id_encabezado,
        em.folio,
        em.fch,
        em.imp,
        em.cli_prov,
        em.usr0,
        cc.n_cli,
        -- Conteos de tracking (igual que BuscarDVIrem)
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'pendiente') AS partidas_pendientes,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'parcial')   AS partidas_parciales,
        COUNT(rpf.id) FILTER (WHERE rpf.estatus = 'completa')  AS partidas_completas,
        COUNT(rpf.id)                                          AS total_partidas_tracking,
        COALESCE(
            SUM(
                (rpf.cantidad_pendiente * rpf.precio_unitario
                 * (1 - rpf.descuento / 100.0))::numeric(18,2)
            ) FILTER (WHERE rpf.estatus IN ('pendiente','parcial')),
            0
        ) AS importe_pendiente
    FROM encabezadomov em
    INNER JOIN catclientes cc
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    LEFT JOIN remision_partidas_facturadas rpf
        ON rpf.encabezado_remision_id = em.id_encabezado
    WHERE em.nat   = @nat
      AND em.suc   = @suc
      AND em.estatus_id = 1
      AND (
           LOWER(em.gen || '-' || em.nat || '-' ||
                 EXTRACT(YEAR FROM em.fch)::text || '-' ||
                 em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov)  LIKE LOWER(@nombre)
        OR LOWER(cc.n_cli)     LIKE LOWER(@nombre)
      )
      AND NOT EXISTS (
          -- Excluir las que ya fueron facturadas completamente
          SELECT 1
          FROM remision_partidas_facturadas rpf2
          WHERE rpf2.encabezado_remision_id = em.id_encabezado
            AND (
                (SELECT COUNT(*) FROM remision_partidas_facturadas rpf3
                 WHERE rpf3.encabezado_remision_id = em.id_encabezado) > 0
                AND
                (SELECT COUNT(*) FROM remision_partidas_facturadas rpf4
                 WHERE rpf4.encabezado_remision_id = em.id_encabezado
                   AND rpf4.estatus != 'completa') = 0
            )
          LIMIT 1
      )
      {filtroCliente}
    GROUP BY
        em.id_encabezado, em.folio, em.fch, em.fol_doc, em.variacion,
        em.imp, em.cli_prov, em.usr0, cc.n_cli,
        em.encabezados_padre
)
SELECT
    grupo_id,
    -- Todos los ids del grupo separados por coma
    STRING_AGG(id_encabezado::text, ',' ORDER BY id_encabezado) AS remisiones_ids,
    COUNT(id_encabezado)     AS total_remisiones,
    -- Folio representativo: si hay varios mostramos el primero
    MIN(folio)               AS folio,
    cli_prov,
    n_cli,
    MIN(fch)                 AS fch,
    SUM(imp)                 AS imp,
    SUM(partidas_pendientes) AS partidas_pendientes,
    SUM(partidas_parciales)  AS partidas_parciales,
    SUM(partidas_completas)  AS partidas_completas,
    SUM(total_partidas_tracking) AS total_partidas_tracking,
    SUM(importe_pendiente)   AS importe_pendiente,
    -- Folios de TODAS las remisiones del grupo (para mostrar en UI)
    STRING_AGG(folio, ' | ' ORDER BY id_encabezado) AS todos_folios
FROM grupos
GROUP BY grupo_id, cli_prov, n_cli
ORDER BY MIN(fch) DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nat", nat);
            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            if (!string.IsNullOrWhiteSpace(cliente))
                parameters.Add("cliente", cliente);

            var items = RunQuery(query, parameters);

            // ── Total de grupos ──────────────────────────────────────────────────
            string queryTotal = $@"
SELECT COUNT(*) AS total
FROM (
    SELECT COALESCE(NULLIF(em.encabezados_padre, 0), em.id_encabezado) AS grupo_id
    FROM encabezadomov em
    INNER JOIN catclientes cc
        ON cc.id_cliente = em.refe AND cc.empresa_id = @empresa_id
    WHERE em.nat = @nat
      AND em.suc = @suc
      AND em.estatus_id = 1
      AND (
           LOWER(em.gen || '-' || em.nat || '-' ||
                 EXTRACT(YEAR FROM em.fch)::text || '-' ||
                 em.fol_doc) LIKE LOWER(@nombre)
        OR LOWER(em.cli_prov) LIKE LOWER(@nombre)
        OR LOWER(cc.n_cli)    LIKE LOWER(@nombre)
      )
      {filtroCliente}
    GROUP BY COALESCE(NULLIF(em.encabezados_padre, 0), em.id_encabezado)
) t";

            var totalParams = new Dictionary<string, object>
    {
        { "nat",        nat },
        { "nombre",     $"%{nombre}%" },
        { "suc",        Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
        { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
    };
            if (!string.IsNullOrWhiteSpace(cliente))
                totalParams.Add("cliente", cliente);

            var totalResult = RunQuery(queryTotal, totalParams);
            int total = totalResult?.Count > 0
                ? Convert.ToInt32(totalResult[0]["total"])
                : 0;

            return Json(new { items, total });
        }


        public IActionResult BuscarDocumentoMultiple(string ids)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ids))
                    return Json(new { success = false, message = "No se proporcionaron IDs." });

                // Validar y parsear ids (anti-injection)
                var listaIds = ids
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .Distinct()
                    .ToList();

                if (listaIds.Count == 0)
                    return Json(new { success = false, message = "IDs inválidos." });

                int idPrincipal = listaIds.First();
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

                // ── Encabezado del primer documento (datos del cliente, etc.) ──
                var encParam = new Dictionary<string, object>
        {
            { "id",         idPrincipal },
            { "empresa_id", empresaId }
        };

                string queryEncabezado = @"
SELECT
    em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
        CASE WHEN em.variacion > 0
             THEN '-' || num_to_letters(em.variacion)
             ELSE '' END AS folio,
    em.id_encabezado,
    em.encabezados_padre,
    em.suc,
    em.alm,
    em.gen,
    em.nat,
    em.usr0, em.fch0,
    em.cli_prov,
    em.coment1, em.coment_aut,
    em.ccy,
    em.vdr_cpr,
    em.flete,
    em.incoterm,
    em.mdp,
    cfp.cve_sat AS f_pago,
    em.cfdi,
    cc.rfc,
    em.par,
    cc.lim_crd,
    cc.n_cli,
    cc.pl_crd,
    cc.dir,
    cc.id_cliente,
    df.forma_pago, df.uso_sugerido, df.regimen_fiscal,
    df.calle, df.no_exterior, df.no_interior,
    df.colonia, df.localidad, df.municipio, df.estado, df.pais,
    df.codigo_postal,
    cc.dir || CHR(10) || cc.col || CHR(10) || cc.pob || CHR(10) || cc.cp AS info_cli,
    em.orden_compra AS ordenCompra,
    COALESCE((
        SELECT SUM(ca.saldo_pendiente)
        FROM cartera_clientes ca
        WHERE ca.cliente_id = cc.id_cliente AND ca.cancelada = false
    ), 0) AS credito_usado
FROM encabezadomov em
LEFT JOIN catclientes cc
    ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
LEFT JOIN direcciones_facturacion df ON df.entidad_clave = cc.cve_cli
LEFT JOIN cat_f_pago cfp ON cfp.id_f_pago = em.f_pago
WHERE em.id_encabezado = @id";

                var encResult = RunQuery(queryEncabezado, encParam);
                if (encResult == null || encResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encResult.First();

                // Agregar crédito calculado
                decimal limite = Convert.ToDecimal(encabezado["lim_crd"] ?? 0);
                decimal usado = Convert.ToDecimal(encabezado["credito_usado"] ?? 0);
                decimal disponible = limite - usado;
                decimal pctUso = limite > 0 ? (usado / limite) * 100 : 0;
                string estatusCredito = limite <= 0 ? "SIN_LIMITE"
                    : usado >= limite ? "EXCEDIDO"
                    : pctUso >= 80 ? "POR_VENCER"
                    : "DISPONIBLE";

                encabezado["credito_disponible"] = disponible;
                encabezado["porcentaje_credito"] = pctUso;
                encabezado["estatus_credito"] = estatusCredito;

                // Adendas del cliente
                if (encabezado.ContainsKey("id_cliente") && encabezado["id_cliente"] != null)
                {
                    var adendasParam = new Dictionary<string, object>
                { { "id_cliente", encabezado["id_cliente"] } };
                    var adendasResult = RunQuery(@"
                SELECT id_addenda, nombre, xml_namespace, xml_prefix, version,
                       data_template, usar_conceptos, created_at, updated_at
                FROM cfdi_addenda_def
                WHERE id_cliente = @id_cliente AND activo = true
                ORDER BY nombre", adendasParam);
                    encabezado["adendas"] = adendasResult;
                }

                // ── Partidas: consolida TODAS las remisiones del grupo ──────────
                // Construir placeholders seguros para IN (…)
                var inPlaceholders = listaIds
                    .Select((_, i) => $"@enc_{i}")
                    .ToList();
                string inClause = string.Join(", ", inPlaceholders);

                var partidasParam = new Dictionary<string, object>
            { { "sucursal", Convert.ToInt32(encabezado["suc"]) } };
                for (int i = 0; i < listaIds.Count; i++)
                    partidasParam.Add($"enc_{i}", listaIds[i]);

                string queryPartidas = $@"
SELECT
    pd.id_partidas                            AS id,
    pd.encabezado_id                          AS remision_origen_id,
    em.gen || '-' || em.nat || '-' ||
        EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc ||
        CASE WHEN em.variacion > 0
             THEN '-' || num_to_letters(em.variacion) ELSE '' END  AS folio_remision,
    pd.cve_prod                               AS producto_id,
    pd.descr_prod                             AS descripcion,
    pd.cant_ud                                AS cantidad,
    pd.pv_prod                                AS precio,
    pd.dto1                                   AS descuento,
    pd.ud                                     AS unidad,
    pd.imp_part                               AS importe,
    pd.iva,
    pd.ieps,
    pd.fch                                    AS fecha,
    pd.cve_alm                                AS almacen,
    pd.cto_vta_part                           AS costo,
    pd.ccy                                    AS moneda,
    pd.pedimento,
    pd.tp_doc_ant                             AS comentario,
    COALESCE(stk.cantidadStock, 0)            AS existencia,
    COALESCE(peds.pedimentos, '[]')           AS pedimentos
FROM partidasdoc pd
INNER JOIN encabezadomov em ON em.id_encabezado = pd.encabezado_id
LEFT JOIN LATERAL (
    SELECT json_agg(json_build_object(
        'pedimento', rc.pedimento,
        'cantidad',  ABS(rc.cantidad)
    )) AS pedimentos
    FROM registro_compras rc
    WHERE rc.producto_id    = pd.producto_id
      AND rc.encabezado_venta = pd.encabezado_id
) peds ON true
LEFT JOIN (
    SELECT tp.producto_id, cs.id_sucursal, SUM(tp.cantidad) AS cantidadStock
    FROM tarima_productos tp
    INNER JOIN cattarimas  ct  ON ct.id_tarima   = tp.tarima_id
    INNER JOIN catniveles  cn  ON cn.id_nivel     = ct.nivel_id
    INNER JOIN catcolumnas cc2 ON cc2.id_columna  = cn.columna_id
    INNER JOIN catracks    cr  ON cr.id_rack      = cc2.rack_id
    INNER JOIN catalmacenes ca ON ca.id_almacen   = cr.almacen_id
    INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
    WHERE ca.tipo = 'Stock'
    GROUP BY tp.producto_id, cs.id_sucursal
) stk ON stk.producto_id = pd.producto_id
      AND stk.id_sucursal = @sucursal
WHERE pd.encabezado_id IN ({inClause})
ORDER BY pd.encabezado_id, pd.nro_part";

                var partidas = RunQuery(queryPartidas, partidasParam);

                encabezado["productos"] = partidas;
                // Exponer los ids originales para que el JS los registre en vi-fac-remisiones-ids
                encabezado["remisiones_ids"] = string.Join(",", listaIds);
                encabezado["total_remisiones"] = listaIds.Count;

                var result = new List<Dictionary<string, object>> { encabezado };
                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "DatosGenerales/?");
                return Json(new { success = false, message = ex.Message });
            }
        }

    }
}