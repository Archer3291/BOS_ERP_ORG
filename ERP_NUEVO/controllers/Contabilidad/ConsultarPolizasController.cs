using DocumentFormat.OpenXml.InkML;
using BOS_ERP.Controllers;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using BOS_ERP.Models.CuentasContables;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;

namespace BOS_ERP.Controllers
{
    public partial class ContabilidadController : Utilities
    {
        [ValidateAntiForgeryToken]
        public JsonResult GetPolizas(string nombre, string sortColumn, string sortDir, int page = 1, int pageSize = 50, int polizasCuadradas = 2,
            string tipo = null, string estado = null, DateTime? fechaInicio = null, DateTime? fechaFin = null, decimal saldoMayor = 0, int categoria = 0)
        {
            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre ?? "" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
                { "tipo", tipo },
                { "estado", estado },
                { "fechaInicio", fechaInicio },
                { "fechaFin", fechaFin },
                { "saldoMayor", saldoMayor },
                { "polizasCuadradas", polizasCuadradas },
                { "empresa", HttpContext.Session.GetInt32("Empresa") },
                { "categoria", categoria }
            };

            var allowedColumns = new HashSet<string> {
                "tipo", "p.fecha_creacion",
                "estado", "folio_poliza", "metodo",
                "total_debe", "total_haber", "usuario"
            };

            if (!allowedColumns.Contains(sortColumn))
                sortColumn = "p.fecha_creacion";

            string where = "";
            string having = "";

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where = " AND (p.descripcion ILIKE '%' || @nombre || '%' " +
                    "OR a.nombre ILIKE '%' || @nombre || '%' " +
                    "OR em.folio ILIKE '%' || @nombre || '%' " +
                    "OR u.nombre || ' ' || u.apellido ILIKE '%' || @nombre || '%' " +
                    "OR p.folio ILIKE '%' || @nombre || '%' ) ";
            }

            if (!string.IsNullOrEmpty(tipo))
                where += " AND cp.nombre = @tipo ";
            if (!string.IsNullOrEmpty(estado))
                where += " AND p.estado = @estado ";

            if (!string.IsNullOrWhiteSpace(estado) && estado == "cancelada")
                where += " AND cancelada = true ";

            if (fechaInicio.HasValue)
                where += " AND p.fecha >= @fechaInicio ";
            if (fechaFin.HasValue)
                where += " AND p.fecha <= @fechaFin ";

            if (polizasCuadradas == 0)
                having += " AND ROUND(SUM(dp.debe), 2) = ROUND(SUM(dp.haber), 2) ";
            else if (polizasCuadradas == 1)
                having += " AND ROUND(SUM(dp.debe), 2) <> ROUND(SUM(dp.haber), 2) ";

            if (saldoMayor > 0)
                having += " AND (ROUND(SUM(dp.debe), 2) - ROUND(SUM(dp.haber), 2)) >= @saldoMayor ";

            if (categoria > 0)
                where += " AND p.categoria = @categoria ";

            string query = "SELECT cp.nombre tipo, p.fecha, p.descripcion, p.referencia, p.estado estado, " +
                "   u.nombre || ' ' || u.apellido AS usuario, p.uuid, ROUND(SUM(dp.debe), 2) AS total_debe, ROUND(SUM(dp.haber), 2) AS total_haber, " +
                "   ROUND(SUM(dp.debe) - SUM(dp.haber), 2) AS saldo, p.id_poliza, " +
                "   em.folio, p.fecha_creacion, em.par, p.es_manual metodo, p.folio folio_poliza, fac.uuid factura_uuid " +
                "FROM polizas p " +
                "INNER JOIN usuarios u ON u.usuarioid = p.creado_por " +
                "INNER JOIN detalles_polizas dp ON dp.poliza_id = p.id_poliza " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = p.tipo " +
                "LEFT JOIN factura fac  ON fac.encabezado_id = em.id_encabezado " +
                $"WHERE p.empresa_id = @empresa {where} " +
                "GROUP BY cp.nombre, em.par, em.folio, p.tipo, p.fecha, p.descripcion, p.referencia, p.estado, u.nombre, u.apellido, p.uuid, em.gen, em.nat, em.fch, em.fol_doc, em.variacion, p.id_poliza, fac.uuid " +
                $"HAVING 1=1 {having} " +
                $"ORDER BY {sortColumn} {sortDir} " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM (" +
                "   SELECT DISTINCT p.id_poliza, a.nombre " +
                "   FROM polizas p " +
                "   INNER JOIN usuarios u ON u.usuarioid = p.creado_por " +
                "   INNER JOIN detalles_polizas dp ON dp.poliza_id = p.id_poliza " +
                "   INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                "   LEFT JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "   INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = p.tipo " +
                $"  WHERE p.empresa_id = @empresa {where} " +
                "   GROUP BY p.id_poliza, a.nombre " +
                $"  HAVING 1=1 {having}" +
                ") AS sub";
            var total = RunScalar(query, parameters);


            query = "SELECT COUNT(*) total_polizas FROM polizas";
            var totalPolizas = RunScalar(query, parameters);

            query = "SELECT ROUND(SUM(dp.debe), 2) total_debe, ROUND(SUM(dp.haber), 2) total_haber, ROUND(SUM(dp.debe) - SUM(dp.haber), 2) total_saldo " +
                "FROM detalles_polizas dp";
            var saldosTotales = RunQuery(query, parameters)[0];

            return Json(new { data, total, totales = new { totalPolizas, saldosTotales } });
        }

        [ValidateAntiForgeryToken]
        public JsonResult GetDetallesPoliza(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("uuid", fc["uuid"].ToString());

            string query = "SELECT p.id_poliza, u.nombre || ' ' || u.apellido AS nombre, cp.nombre tipo, p.descripcion, p.estado, " +
                "   em.folio, em.par, p.fecha, p.folio uuid, p.referencia, fac.uuid factura_uuid " +
                "FROM polizas p " +
                "INNER JOIN usuarios u ON u.usuarioid = p.usuario_id " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = p.tipo " +
                "LEFT JOIN factura fac  ON fac.encabezado_id = em.id_encabezado " +
                "WHERE p.uuid = @uuid";
            var poliza = RunQuery(query, parameters)[0];

            query = "SELECT p.id_poliza, cf.codigo, cf.nombre nombrecuenta, dp.centro_costos, a.nombre nombre_costos, ROUND(dp.debe, 2) debe, ROUND(dp.haber, 2) haber, COALESCE(dp.descripcion, '') descripcion, " +
                "   dp.uuid detalle_poliza_uuid, p.folio poliza_uuid, cf.uuid cuenta_finanzas_uuid, " +
                "   em.folio, dp.cuenta_finanzas_id, dp.id_detalle_poliza, COALESCE(em.ccy, 'PESOS') AS ccy, em.par " +
                "FROM detalles_polizas dp " +
                "INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable  = dp.cuenta_finanzas_id " +
                "INNER JOIN polizas p ON p.id_poliza = dp.poliza_id " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                "WHERE p.uuid = @uuid " +
                "ORDER BY dp.debe DESC";
            var detalles = RunQuery(query, parameters);

            var referencia = new Dictionary<string, object>();
            if (poliza["referencia"] != null && poliza["referencia"] != "null")
            {
                query = "SELECT em.suc, em.gen, em.nat, em.nro_gpo_doc, em.tipo_proceso, em.nro_tp_doc, em.fol_doc, " +
                    "   em.cli_prov, em.iva, em.imp, em.coment_aut,  em.usr1, em.id_encabezado, em.imp, " +
                    "   em.folio, " +
                    "    (SELECT nombre || ' ' || apellido AS responsable FROM usuarios WHERE nombreusuario = usr_doc) AS responsable, " +
                    "   fch, em.uuid, em.id_encabezado " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id_encabezado";
                parameters.Add("id_encabezado", poliza["referencia"]);
                referencia = RunQuery(query, parameters)[0];
            }

            return Json(new { poliza, detalles, referencia });
        }

        [AuditAction(Modulo = "Contabilidad", Accion = "Edicion de polizas")]
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult EditarPoliza(IFormCollection fc)
        {
            int usuario = GetUserId(User.Identity.Name);
            int? encabezado = null;

            if (fc["encabezado_id"].ToString() != "" && fc["encabezado_id"].ToString() != "null")
            {
                encabezado = int.Parse(fc["encabezado_id"].ToString());
            }

            int id_poliza = Convert.ToInt32(fc["id_poliza"].ToString());
            string datosPolizaJson = fc["datosPoliza"].ToString();
            var parameters = new Dictionary<string, object>();
            var datosPoliza = JsonConvert.DeserializeObject<Dictionary<string, object>>(datosPolizaJson);

            string query = "SELECT estado FROM polizas WHERE id_poliza = @id_poliza";
            parameters.Add("id_poliza", id_poliza);
            string estado = RunScalar(query, parameters).ToString();
            //if (string.IsNullOrEmpty(estado) || estado != "borrador")
            //{
            //    Response.StatusCode = (int)HttpStatusCode.BadRequest;
            //    return Json(new { icon = "error", title = "Datos incorrectos", html = "Solo se pueden editar polizas con estatus en 'Borrador', si cree que esto es un error contacte a soporte." });
            //}

            parameters = new Dictionary<string, object>();
            query = "UPDATE polizas SET estado = @estado, descripcion = @descripcion, fecha_edicion = now() " +
                "WHERE id_poliza = @id_poliza";
            parameters.Add("estado", datosPoliza["estado"].ToString());
            parameters.Add("descripcion", datosPoliza["descripcion"].ToString());
            parameters.Add("id_poliza", id_poliza);
            RunUpdate(query, parameters);

            var detallesJson = datosPoliza["detalles"].ToString();
            var detallesPoliza = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(detallesJson);

            query = "DELETE FROM detalles_polizas WHERE poliza_id = @poliza_id";
            parameters.Add("poliza_id", id_poliza);
            RunUpdate(query, parameters);

            var _parameters = new List<Dictionary<string, object>>();
            foreach (var detalle in detallesPoliza)
            {
                parameters = new Dictionary<string, object>();
                parameters.Add("id_detalle", Convert.ToInt32(detalle["id"]));
                parameters.Add("poliza", id_poliza);
                parameters.Add("cuenta", Convert.ToInt32(detalle["cuenta"]));
                parameters.Add("debe", Convert.ToDecimal(detalle["debe"]));
                parameters.Add("haber", Convert.ToDecimal(detalle["haber"]));
                parameters.Add("centro", Convert.ToInt32(detalle["centro"]));
                parameters.Add("encabezado_id", encabezado);
                parameters.Add("usuario_id", GetUserId(User.Identity.Name));
                parameters.Add("descripcion", detalle["descripcion"].ToString());
                parameters.Add("fecha", GetDate(datosPoliza["fecha"]));

                _parameters.Add(parameters);

            }
            query = "INSERT INTO detalles_polizas (poliza_id, cuenta_finanzas_id, debe, haber, centro_costos, encabezado_id, fecha, creado_por, descripcion, fecha_editado) " +
                "VALUES (@poliza, @cuenta, @debe, @haber, @centro, @encabezado_id, now(), @usuario_id, @descripcion, now())";
            RunUpdate(query, _parameters);

            Response.StatusCode = (int)HttpStatusCode.OK;
            return Json(new { icon = "success", title = "Poliza editada correctamente", html = $"La poliza vinculada al folio {fc["folio"].ToString()} se edito correctamente.", showCancelButton = false });
        }

        #region Generar PDF de polizas
        [ValidateAntiForgeryToken, HttpPost]
        public JsonResult GetPolizasPDF(string tipo = null, string estado = null, DateTime? fechaInicio = null, DateTime? fechaFin = null, decimal saldoMayor = 0, int polizasCuadradas = 2, int estatusPolizas = 2)
        {
            var parameters = new Dictionary<string, object>
            {
                { "tipo", tipo },
                { "estado", estado },
                { "fechaInicio", fechaInicio },
                { "fechaFin", fechaFin },
                { "saldoMayor", saldoMayor },
                { "polizasCuadradas", polizasCuadradas },
                { "estatusPolizas", estatusPolizas},
            };

            string where = " WHERE 1=1 ";
            string having = " HAVING 1=1 ";

            if (!string.IsNullOrEmpty(tipo))
                where += " AND p.tipo = @tipo ";
            if (!string.IsNullOrEmpty(estado))
                where += " AND p.estado = @estado ";
            if (fechaInicio.HasValue)
                where += " AND p.fecha >= @fechaInicio ";
            if (fechaFin.HasValue)
                where += " AND p.fecha <= @fechaFin ";

            if (polizasCuadradas == 0)
                having += " AND ROUND(SUM(dp.debe), 2) = ROUND(SUM(dp.haber), 2) ";
            else if (polizasCuadradas == 1)
                having += " AND ROUND(SUM(dp.debe), 2) <> ROUND(SUM(dp.haber), 2) ";

            if (estatusPolizas == 0)
                where += " AND p.cancelada = false ";
            else if (estatusPolizas == 1)
                where += " AND p.cancelada = true ";

            if (saldoMayor > 0)
                having += " AND (ROUND(SUM(dp.debe), 2) - ROUND(SUM(dp.haber), 2)) >= @saldoMayor ";

            // 🔹 1. Consulta principal (encabezados)
            string query = "SELECT p.id_poliza, cp.nombre tipo, p.fecha, p.descripcion, p.referencia, p.estado, " +
                "    u.nombre || ' ' || u.apellido AS usuario, p.uuid, em.tipo_proceso, " +
                "    ROUND(SUM(dp.debe), 2) AS total_debe, ROUND(SUM(dp.haber), 2) AS total_haber, " +
                "    ROUND(SUM(dp.debe) - SUM(dp.haber), 2) AS saldo, " +
                "    em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc || " +
                "    CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio " +
                "FROM polizas p " +
                "INNER JOIN usuarios u ON u.usuarioid = p.creado_por " +
                "INNER JOIN detalles_polizas dp ON dp.poliza_id = p.id_poliza " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = p.tipo " +
                $" {where} " +
                "GROUP BY cp.nombre, p.id_poliza, p.tipo, p.fecha, p.descripcion, p.referencia, p.estado, u.nombre, u.apellido, p.uuid, em.gen, em.nat, em.fch, em.fol_doc, em.variacion, em.tipo_proceso " +
                $" {having} " +
                "ORDER BY p.fecha DESC";

            var polizas = RunQuery(query, parameters);

            // 🔹 2. Obtener los IDs de las pólizas resultantes
            if (polizas.Count == 0)
                return Json(new { data = new List<object>(), total = 0 });

            string ids = string.Join(",", polizas.Select(p => p["id_poliza"].ToString()));

            // 🔹 3. Consultar los detalles de esas pólizas
            string detallesQuery = $"SELECT dp.poliza_id, cf.codigo, cf.nombre AS nombrecuenta, ROUND(dp.debe, 2) debe, ROUND(dp.haber, 2) haber, a.nombre AS nombre_costos, dp.descripcion, dp.fecha, u.nombre || ' ' || u.apellido AS usuario " +
                $"FROM detalles_polizas dp " +
                $"INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                $"INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id " +
                $"INNER JOIN usuarios u ON u.usuarioid = dp.creado_por " +
                $"WHERE dp.poliza_id IN ({ids})";

            var detalles = RunQuery(detallesQuery, parameters);

            // 🔹 4. Asociar detalles a cada póliza
            foreach (var poliza in polizas)
            {
                int id = Convert.ToInt32(poliza["id_poliza"]);
                poliza["detalles"] = detalles.Where(d => Convert.ToInt32(d["poliza_id"]) == id).ToList();
            }

            // Totales
            string totalPolizasQuery = "SELECT COUNT(*) FROM polizas";
            var totalPolizas = RunScalar(totalPolizasQuery, parameters);

            string saldosTotalesQuery = "SELECT ROUND(SUM(dp.debe), 2) total_debe, ROUND(SUM(dp.haber), 2) total_haber, ROUND(SUM(dp.debe) - SUM(dp.haber), 2) total_saldo FROM detalles_polizas dp";
            var saldosTotales = RunQuery(saldosTotalesQuery, parameters)[0];

            return Json(new { data = polizas, total = polizas.Count, totales = new { totalPolizas, saldosTotales } });
        }
        #endregion

        #region Polizas Manuales
        //public JsonResult GenerarPolizasManuales()
        //{
        //    string connStr = ConfigurationManager.ConnectionStrings["ERP_SRS"].ConnectionString;
        //    using (var conn = new NpgsqlConnection(connStr))
        //    {
        //        conn.Open();

        //        using (var tx = conn.BeginTransaction())
        //        {
        //            try
        //            {
        //                var parameters = new Dictionary<string, object>();
        //                List<List<(int, Guid)>> polizas = new List<List<(int, Guid)>>();


        //                //--------------------------------------------------
        //                // ELIMINAR TODO
        //                //--------------------------------------------------
        //                string query = "DELETE FROM aplicaciones_cobro_cliente";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM cobros_cliente";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM cartera_clientes";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM aplicaciones_pago_proveedor";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM pagos_proveedor";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM cartera_proveedores";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM detalles_polizas";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM polizas";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM registro_compras";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM tarimas_mov";
        //                RunUpdate(query, parameters, false, conn, tx);
        //                query = "DELETE FROM tarima_productos";
        //                RunUpdate(query, parameters, false, conn, tx);


        //                //--------------------------------------------------
        //                // REGISTRAR ORDENES DE COMPRA
        //                //--------------------------------------------------
        //                query = "SELECT id_encabezado FROM encabezadomov WHERE nat IN ('OC', 'OCD', 'OCDI', 'AIEINV') AND estatus_id = 11 AND suc = @sucursal";
        //                parameters.Add("sucursal", Session["Sucursal"]);
        //                var result = RunQuery(query, parameters, false, conn, tx);
        //                List<int> encabezadoIds = result.Select(x => Convert.ToInt32(x["id_encabezado"])).ToList();

        //                foreach (int doc in encabezadoIds)
        //                {
        //                    parameters = new Dictionary<string, object>();
        //                    var productos = new List<Dictionary<string, object>>();
        //                    query = "SELECT p.cve_prod codigo, p.descr_prod descripcion, p.cant_ud cantidad, cu.id_udm unidad, cp.id_catproductos id_producto " +
        //                        "FROM partidasdoc p " +
        //                        "INNER JOIN catunidades cu ON cu.cve_udm = p.ud " +
        //                        "INNER JOIN catproductos cp ON cp.cve_prod = p.cve_prod AND cp.empresa_id = @empresa " +
        //                        "WHERE encabezado_id = @encabezado";
        //                    parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
        //                    parameters.Add("encabezado", doc);
        //                    var prod = RunQuery(query, parameters, false, conn, tx);

        //                    foreach (var p in prod)
        //                    {
        //                        var pr = new Dictionary<string, object>();
        //                        pr.Add("id_producto", GetInt(p["id_producto"]));
        //                        pr.Add("codigo", GetString(p["codigo"]));
        //                        pr.Add("descripcion", GetString(p["descripcion"]));
        //                        pr.Add("cantidad", GetDecimal(p["cantidad"]));
        //                        pr.Add("unidad", GetInt(p["unidad"]));

        //                        productos.Add(pr);
        //                    }

        //                    parameters = new Dictionary<string, object>();
        //                    query = "SELECT ct.id_tarima FROM catalmacenes c " +
        //                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
        //                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
        //                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
        //                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
        //                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
        //                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Temporal'";
        //                    parameters.Add("sucursal", Session["Sucursal"]);
        //                    int destino = Convert.ToInt32(RunScalar(query, parameters));

        //                    RegistrarMovimiento(productos, GetUserId(User.Identity.Name), "ingreso_recepcion", null, destino, "Recepcion de material", doc, conn, tx);

        //                    //List<PolizaData> poliza = GenerarDatosPoliza(doc, null, null, conn, tx);
        //                    //var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), doc, poliza, false, null, conn, tx);
        //                    //RegistrarCompra(doc, GetUserId(User.Identity.Name), conn, tx);
        //                    //RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);
        //                }



        //                //--------------------------------------------------
        //                // REGISTRAR REMISIONES
        //                //--------------------------------------------------
        //                parameters = new Dictionary<string, object>();
        //                query = "SELECT id_encabezado FROM encabezadomov WHERE nat IN ('VIREM', 'VINREM', 'VSREM', 'VNREM') AND estatus_id = 11 AND suc = @sucursal";
        //                parameters.Add("sucursal", Session["Sucursal"]);
        //                result = RunQuery(query, parameters, false, conn, tx);
        //                encabezadoIds = result.Select(x => Convert.ToInt32(x["id_encabezado"])).ToList();

        //                foreach (int doc in encabezadoIds)
        //                {
        //                    parameters = new Dictionary<string, object>();
        //                    var productos = new List<Dictionary<string, object>>();
        //                    query = "SELECT p.cve_prod codigo, p.descr_prod descripcion, p.cant_ud cantidad, cu.id_udm unidad, cp.id_catproductos id_producto " +
        //                        "FROM partidasdoc p " +
        //                        "INNER JOIN catunidades cu ON cu.cve_udm = p.ud " +
        //                        "INNER JOIN catproductos cp ON cp.cve_prod = p.cve_prod AND cp.empresa_id = @empresa " +
        //                        "WHERE encabezado_id = @encabezado";
        //                    parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
        //                    parameters.Add("encabezado", doc);
        //                    var prod = RunQuery(query, parameters, false, conn, tx);

        //                    foreach (var p in prod)
        //                    {
        //                        var pr = new Dictionary<string, object>();
        //                        pr.Add("id_producto", GetInt(p["id_producto"]));
        //                        pr.Add("codigo", GetString(p["codigo"]));
        //                        pr.Add("descripcion", GetString(p["descripcion"]));
        //                        pr.Add("cantidad", GetDecimal(p["cantidad"]));
        //                        pr.Add("unidad", GetInt(p["unidad"]));

        //                        productos.Add(pr);
        //                    }

        //                    parameters = new Dictionary<string, object>();
        //                    query = "SELECT ct.id_tarima FROM catalmacenes c " +
        //                        "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
        //                        "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
        //                        "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
        //                        "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
        //                        "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
        //                        "WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Stock'";
        //                    parameters.Add("sucursal", Session["Sucursal"]);
        //                    int destino = Convert.ToInt32(RunScalar(query, parameters));

        //                    RegistrarMovimiento(productos, GetUserId(User.Identity.Name), "venta", destino, null, "salida", doc, conn, tx);
        //                    RegistrarCompra(doc, GetUserId(User.Identity.Name), conn, tx);
        //                }

        //                //--------------------------------------------------
        //                // REGISTRAR FACTURAS
        //                //--------------------------------------------------
        //                parameters = new Dictionary<string, object>();
        //                parameters.Add("sucursal", Session["Sucursal"]);
        //                List<int> encabezados = new List<int>();

        //                query = "SELECT id_encabezado FROM encabezadomov WHERE nat IN ('VIFAC', 'VNFAC', 'VINFAC', 'VSFAC') AND estatus_id IN (1, 11) AND suc = @sucursal AND tipo_proceso = 'factura_contado'";
        //                result = RunQuery(query, parameters);
        //                encabezados = result.Select(r => Convert.ToInt32(r["id_encabezado"])).ToList();

        //                foreach (var i in encabezados)
        //                {
        //                    List<PolizaData> poliza = GenerarDatosPoliza(i, "1-1-02-01-0002", null, conn, tx);
        //                    var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), i, poliza, false, null, conn, tx);
        //                    RegistrarCarteraResult cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

        //                    query = "SELECT cli_prov, refe, centro_costos, id_encabezado, coment1, coment2, usr_dep, usr_doc, usr0, sub, fch " +
        //                        "FROM encabezadomov " +
        //                        "WHERE id_encabezado = @i";
        //                    parameters = new Dictionary<string, object>();
        //                    parameters.Add("i", i);
        //                    var usrId = RunQuery(query, parameters)[0];

        //                    var encabezado = new DocumentoEncabezado();
        //                    encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
        //                    encabezado.IdArea = 20;
        //                    encabezado.IdTpDoc = 70;
        //                    encabezado.UsrDep = GetString(usrId["usr_dep"]);
        //                    encabezado.Anio = DateTime.Now.Year;
        //                    encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
        //                    encabezado.Fch = (DateTime)usrId["fch"];
        //                    encabezado.TpMov = "CXC";
        //                    encabezado.UsrDoc = GetString(usrId["usr_doc"]);
        //                    encabezado.FchCap = GetDate(usrId["fch"]);
        //                    encabezado.Usr0 = GetInt(usrId["usr0"]);
        //                    encabezado.Fch0 = GetDate(usrId["fch"]);
        //                    encabezado.Imp = cartera.MontoTotal;
        //                    encabezado.Sub = GetDecimal(usrId["sub"]);
        //                    encabezado.CliProv = usrId["cli_prov"].ToString();
        //                    encabezado.Ref = Convert.ToInt32(usrId["refe"]);
        //                    encabezado.Estatus = 1;
        //                    encabezado.TipoPoceso = "cobro_cliente";
        //                    encabezado.CentroCostos = Convert.ToInt32(usrId["centro_costos"]);
        //                    encabezado.EncabezadoPadre = i;
        //                    encabezado.Coment1 = GetString(usrId["coment1"]);
        //                    encabezado.Coment2 = GetString(usrId["coment2"]);
        //                    encabezado.IdCartera = cartera.CarteraId;
        //                    encabezado.Ccy = "PESOS";
        //                    var partidas = new List<PartidaDocumento>();
        //                    var documento = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

        //                    query = "SELECT * FROM encabezadomov where id_encabezado = @encabezado";
        //                    parameters.Add("encabezado", Convert.ToInt32(documento["IdEncabezado"]));
        //                    var hola = RunQuery(query, parameters, false, conn, tx);


        //                    query = "INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id) " +
        //                        "values (@encabezado, 1, @sub, @imp, 1, 16, @prov, null)";
        //                    parameters.Add("sub", GetDecimal(usrId["sub"]));
        //                    parameters.Add("prov", GetString(usrId["cli_prov"]));
        //                    parameters.Add("imp", GetDecimal(usrId["sub"]) * 0.16m);
        //                    RunUpdate(query, parameters, false, conn, tx);

        //                    poliza = GenerarDatosPoliza(Convert.ToInt32(documento["IdEncabezado"]), "1-1-02-01-0002", null, conn, tx);
        //                    resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), Convert.ToInt32(documento["IdEncabezado"]), poliza, false, null, conn, tx);

        //                    CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), cartera.CarteraId, false, false, conn, tx);
        //                }



        //                //--------------------------------------------------
        //                // REGISTRAR FACTURAS
        //                //--------------------------------------------------
        //                parameters = new Dictionary<string, object>();
        //                parameters.Add("sucursal", Session["Sucursal"]);
        //                encabezados = new List<int>();

        //                query = "SELECT id_encabezado FROM encabezadomov WHERE nat IN ('VIFAC', 'VNFAC', 'VINFAC', 'VSFAC') AND estatus_id IN (1, 11) AND suc = @sucursal AND tipo_proceso = 'factura_credito'";
        //                result = RunQuery(query, parameters);
        //                encabezados = result.Select(r => Convert.ToInt32(r["id_encabezado"])).ToList();

        //                //foreach (var i in encabezados)
        //                //{
        //                //    List<PolizaData> poliza = GenerarDatosPoliza(i, "1-1-02-01-0002", null, conn, tx);
        //                //    var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), i, poliza, false, null, conn, tx);
        //                //    RegistrarCarteraResult cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);
        //                //    //CrearCobroCliente(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), cartera.CarteraId, false, false, conn, tx);
        //                //}


        //                //----------------------------------------------------------------
        //                // REGISTRAR COMPLEMENTOS DE PAGO Y CARTERAS INICIALES DE KEPLER
        //                //----------------------------------------------------------------
        //                parameters = new Dictionary<string, object>();
        //                parameters.Add("sucursal", Session["Sucursal"]);
        //                query = "SELECT cc.id_cartera_cliente cartera, cc.encabezado_id enca_cartera, acc.cobro_id, cc2.encabezado_id enca_cobro, e.folio folio_cobro, e2.folio folio_cartera, " +
        //                    "   CASE WHEN cc.saldo_pendiente = 0 THEN 'Pago completo' ELSE 'pago pendiente' END AS pagado, e2.tipo_proceso " +
        //                    "FROM srs_prod.cartera_clientes cc " +
        //                    "LEFT JOIN srs_prod.aplicaciones_cobro_cliente acc ON acc.cartera_id = cc.id_cartera_cliente " +
        //                    "LEFT JOIN srs_prod.cobros_cliente cc2 ON cc2.id_cobro = acc.cobro_id " +
        //                    "LEFT JOIN srs_prod.encabezadomov e ON e.id_encabezado = cc2.encabezado_id " +
        //                    "LEFT JOIN srs_prod.encabezadomov e2 ON e2.id_encabezado = cc.encabezado_id " +
        //                    "WHERE e2.suc = @sucursal AND e2.tipo_proceso != 'factura_contado'";
        //                result = RunQuery(query, parameters, false, conn, tx);
        //                var conCobro = result
        //                    .Where(x => x["enca_cobro"] != DBNull.Value)
        //                    .GroupBy(x => Convert.ToInt32(x["enca_cobro"]))
        //                    .ToDictionary(
        //                        g => g.Key,
        //                        g => g.Select(x => Convert.ToInt32(x["enca_cartera"])).Distinct().ToList()
        //                    );

        //                var sinCobro = result
        //                    .Where(x => x["enca_cobro"] == DBNull.Value)
        //                    .Select(x => Convert.ToInt32(x["enca_cartera"]))
        //                    .Distinct()
        //                    .ToList();

        //                foreach (var kvp in conCobro)
        //                {
        //                    int cobro = kvp.Key;
        //                    var carteras = kvp.Value;

        //                    List<int> carteraIdGenerada = new List<int>();

        //                    foreach (var cartera in carteras)
        //                    {
        //                        List<PolizaData> datosPoliza = GenerarDatosPoliza(cartera, null, null, conn, tx);
        //                        var poliza = RegistrarPolizas(GetUserId(User.Identity.Name), cartera, datosPoliza, false, null, conn, tx);
        //                        RegistrarCarteraResult cart = RegistrarCartera(poliza[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

        //                        // guardas la cartera generada (asumo que solo necesitas una)
        //                        carteraIdGenerada.Add(cart.CarteraId);
        //                    }

        //                    if (cobro > 0)
        //                    {
        //                        // ahora el cobro se hace UNA sola vez
        //                        List<PolizaData> datosPolizaCobro = GenerarDatosPoliza(cobro, "1-1-02-01-0002", carteraIdGenerada, conn, tx);
        //                        var polizaCobro = RegistrarPolizas(GetUserId(User.Identity.Name), cobro, datosPolizaCobro, false, null, conn, tx);
        //                        CobroClienteResult cob = CrearCobroCliente(polizaCobro[0].idPoliza, GetUserId(User.Identity.Name), null, false, true, conn, tx);


        //                        AplicarCobros cobros = new AplicarCobros();
        //                        cobros.CobrosIds.Add(cob.PagoId);
        //                        cobros.ClienteId = cob.PagoId;
        //                        cobros.UsuarioId = GetUserId(User.Identity.Name);

        //                        foreach (var i in carteraIdGenerada)
        //                        {
        //                            cobros.CarteraIds.Add(i);
        //                        }
        //                        AplicarCobrosCliente(cobros, conn, tx);
        //                    }
        //                }


        //                //---------------------------
        //                // ACTUALIZAR FECHAS Y TODO
        //                //---------------------------
        //                query = "UPDATE polizas p " +
        //                    "   SET fecha = e.fch, fecha_creacion = e.fch, usuario_id = e.usr0, creado_por = e.usr0 " +
        //                    "FROM encabezadomov e " +
        //                    "WHERE e.id_encabezado = p.referencia;";
        //                RunUpdate(query, parameters, false, conn, tx);

        //                query = "UPDATE detalles_polizas dp " +
        //                    "   SET fecha = e.fch, fecha_creacion = e.fch, creado_por = e.usr0 " +
        //                    "FROM encabezadomov e " +
        //                    "WHERE e.id_encabezado = dp.encabezado_id;";
        //                RunUpdate(query, parameters, false, conn, tx);

        //                query = "UPDATE cartera_clientes cc " +
        //                    "   SET fecha_emision = f.fecha, fecha_vencimiento = f.fecha + (30 * INTERVAL '1 day'), fecha_creacion = e.fch " +
        //                    "FROM factura f " +
        //                    "INNER JOIN encabezadomov e ON e.id_encabezado = f.encabezado_id " +
        //                    "WHERE f.encabezado_id = cc.encabezado_id;";
        //                RunUpdate(query, parameters, false, conn, tx);

        //                query = "UPDATE cartera_proveedores cc " +
        //                    "   SET fecha_emision = e.fch, creado_por = e.usr0 " +
        //                    "FROM encabezadomov e " +
        //                    "WHERE e.id_encabezado = cc.encabezado_id;";
        //                RunUpdate(query, parameters, false, conn, tx);

        //                query = "UPDATE cobros_cliente cc " +
        //                    "   SET fecha_cobro = e.fch, creado_por = e.usr0 " +
        //                    "FROM encabezadomov e " +
        //                    "WHERE e.id_encabezado = cc.encabezado_id;";
        //                RunUpdate(query, parameters, false, conn, tx);

        //                query = "UPDATE aplicaciones_cobro_cliente acc " +
        //                    "   SET creado_por = e.usr0 " +
        //                    "FROM encabezadomov e " +
        //                    "WHERE e.id_encabezado = acc.encabezado_id;";
        //                RunUpdate(query, parameters, false, conn, tx);

        //                tx.Commit();
        //            }
        //            catch
        //            {
        //                tx.Rollback();
        //                throw;
        //            }
        //        }
        //    }
        //    return Json(new { });
        //}
        
        #endregion
    }
}