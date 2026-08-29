using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Models;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class ContabilidadController : Utilities
    {
        private readonly IConfiguration _configuration;

        public ContabilidadController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        #region Reporte de Balanza de Comprobacion
        public JsonResult GetBalanzaComprobacion(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            var fechaInicio = GetDate(fc["fechaInicio"].ToString());
            var fechaFin = GetDate(fc["fechaFin"].ToString());
            var cuentaIni = GetString(fc["cuentaIni"].ToString());
            var cuentaFin = GetString(fc["cuentaFin"].ToString());
            bool mostrarCero = Convert.ToBoolean(fc["quitarCero"].ToString());
            string filtroCuenta = "";
            string quitarCero = "";
            var returnResult = new Dictionary<string, object>();

            if (fechaInicio != null && fechaFin != null)
            {
                parameters.Add("fechaInicio", fechaInicio);
                parameters.Add("fechaFin", fechaFin);
            }

            if (cuentaIni != null)
            {
                parameters.Add("cuentaIni", cuentaIni);
                filtroCuenta += " AND c.codigo >= @cuentaIni ";
            }

            if (cuentaFin != null)
            {
                parameters.Add("cuentaFin", cuentaFin);
                filtroCuenta += " AND c.codigo <= @cuentaFin ";
            }

            if (mostrarCero)
            {
                quitarCero = " HAVING (" +
                    "   COALESCE(SUM(CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) ELSE (m.haber_inicial - m.debe_inicial) END),0) != 0 " + // saldo inicial
                    "   OR COALESCE(SUM(m.debe_periodo),0) != 0 " + // debe
                    "   OR COALESCE(SUM(m.haber_periodo),0) != 0 " + // haber
                    "   OR COALESCE(SUM(CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) + (m.debe_periodo - m.haber_periodo) ELSE (m.haber_inicial - m.debe_inicial) + (m.haber_periodo - m.debe_periodo) END),0) != 0 " + // saldo final
                    ")";
            }

            string query = "WITH movimientos AS (" +
                "   SELECT cuenta_finanzas_id, " +
                "       SUM(CASE WHEN dt.fecha < @fechaInicio THEN dt.debe ELSE 0 END) AS debe_inicial, " +
                "       SUM(CASE WHEN dt.fecha < @fechaInicio THEN dt.haber ELSE 0 END) AS haber_inicial, " +
                "       SUM(CASE WHEN dt.fecha >= @fechaInicio AND dt.fecha <= @fechaFin THEN dt.debe ELSE 0 END) AS debe_periodo, " +
                "       SUM(CASE WHEN dt.fecha >= @fechaInicio AND dt.fecha <= @fechaFin THEN dt.haber ELSE 0 END) AS haber_periodo " +
                "   FROM detalles_polizas dt " +
                "   INNER JOIN polizas p ON p.id_poliza = dt.poliza_id " +
                "   WHERE p.empresa_id = @empresa AND p.cancelada = false " +
                "   GROUP BY cuenta_finanzas_id " +
                ") " +
                "SELECT c.codigo, c.nombre, " +
                "   COALESCE(SUM(" +
                "       CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) ELSE (m.haber_inicial - m.debe_inicial) END" +
                "   ),0) AS saldo_inicial," +
                "   COALESCE(SUM(m.debe_periodo),0) AS debe, " +
                "   COALESCE(SUM(m.haber_periodo),0) AS haber, " +
                "   COALESCE(SUM(" +
                "       CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) + (m.debe_periodo - m.haber_periodo) ELSE (m.haber_inicial - m.debe_inicial) + (m.haber_periodo - m.debe_periodo) END" +
                "   ),0) AS saldo_final," +
                "   EXISTS ( " +
                "       SELECT 1 " +
                "       FROM cuentas_finanzas c3 " +
                "       WHERE c3.ruta <@ c.ruta AND c3.ruta != c.ruta " +
                "   ) AS es_padre " +
                "FROM cuentas_finanzas c " +
                "JOIN cuentas_finanzas c2 ON c2.ruta <@ c.ruta " +
                "LEFT JOIN movimientos m ON m.cuenta_finanzas_id = c2.id_cuenta_contable " +
                $"WHERE c.empresa_id = @empresa {filtroCuenta} " +
                "GROUP BY c.codigo, c.nombre, c.empresa_id, c.ruta " +
                $" {quitarCero} " +
                "ORDER BY c.ruta;";
            var balanza = RunQuery(query, parameters);

            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            returnResult.Add("datos_empresa", datosEmpresa);
            returnResult.Add("balanza", balanza);

            return Json(returnResult);
        }
        #endregion

        #region Reporte de Balance General
        public JsonResult GetBalanzaGeneral(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            var mes = GetInt(fc["mes"].ToString());
            var anio = GetInt(fc["anio"].ToString());
            var cuentaIni = GetString(fc["cuentaIni"].ToString());
            var cuentaFin = GetString(fc["cuentaFin"].ToString());
            bool mostrarCero = Convert.ToBoolean(fc["quitarCero"].ToString());
            string filtroCuenta = "";
            string quitarCero = "";
            var returnResult = new Dictionary<string, object>();

            if (mes == null || anio == null)
                throw new Exception("Mes y año son obligatorios");

            if (cuentaIni != null)
            {
                parameters.Add("cuentaIni", cuentaIni);
                filtroCuenta += " AND c.codigo >= @cuentaIni ";
            }

            if (cuentaFin != null)
            {
                parameters.Add("cuentaFin", cuentaFin);
                filtroCuenta += " AND c.codigo <= @cuentaFin ";
            }

            if (mostrarCero)
            {
                quitarCero = " HAVING (" +
                    "   COALESCE(SUM(CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) ELSE (m.haber_inicial - m.debe_inicial) END),0) != 0 " + // saldo inicial
                    "   OR COALESCE(SUM(m.debe_periodo),0) != 0 " + // debe
                    "   OR COALESCE(SUM(m.haber_periodo),0) != 0 " + // haber
                    "   OR COALESCE(SUM(CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) + (m.debe_periodo - m.haber_periodo) ELSE (m.haber_inicial - m.debe_inicial) + (m.haber_periodo - m.debe_periodo) END),0) != 0 " + // saldo final
                    ")";
            }

            var fechaInicio = new DateTime(anio.Value, mes.Value, 1);
            var fechaFin = fechaInicio.AddMonths(1);

            parameters.Add("fecha_inicio", fechaInicio);
            parameters.Add("fecha_fin", fechaFin);

            string query = "WITH movimientos AS (" +
                "   SELECT cuenta_finanzas_id, " +
                "       SUM(CASE WHEN dt.fecha < @fecha_inicio THEN dt.debe ELSE 0 END) AS debe_inicial, " +
                "       SUM(CASE WHEN dt.fecha < @fecha_inicio THEN dt.haber ELSE 0 END) AS haber_inicial, " +
                "       SUM(CASE WHEN dt.fecha >= @fecha_inicio AND dt.fecha < @fecha_fin THEN dt.debe ELSE 0 END) AS debe_periodo, " +
                "       SUM(CASE WHEN dt.fecha >= @fecha_inicio AND dt.fecha < @fecha_fin THEN dt.haber ELSE 0 END) AS haber_periodo " +
                "   FROM detalles_polizas dt " +
                "   INNER JOIN polizas p ON p.id_poliza = dt.poliza_id " +
                "   WHERE p.empresa_id = @empresa AND p.cancelada = false " +
                "   GROUP BY cuenta_finanzas_id " +
                ") " +
                "SELECT c.codigo, c.nombre, " +
                "   COALESCE(SUM(" +
                "       CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) ELSE (m.haber_inicial - m.debe_inicial) END" +
                "   ),0) AS saldo_inicial," +
                "   COALESCE(SUM(m.debe_periodo),0) AS debe, " +
                "   COALESCE(SUM(m.haber_periodo),0) AS haber, " +
                "   COALESCE(SUM(" +
                "       CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) + (m.debe_periodo - m.haber_periodo) ELSE (m.haber_inicial - m.debe_inicial) + (m.haber_periodo - m.debe_periodo) END" +
                "   ),0) AS saldo_final," +
                "   EXISTS ( " +
                "       SELECT 1 " +
                "       FROM cuentas_finanzas c3 " +
                "       WHERE c3.ruta <@ c.ruta AND c3.ruta != c.ruta " +
                "   ) AS es_padre " +
                "FROM cuentas_finanzas c " +
                "JOIN cuentas_finanzas c2 ON c2.ruta <@ c.ruta " +
                "LEFT JOIN movimientos m ON m.cuenta_finanzas_id = c2.id_cuenta_contable " +
                $"WHERE c.empresa_id = @empresa AND nlevel(c.ruta) <= 3 AND LEFT(c.codigo, 1) IN ('1','2','3') " +
                "GROUP BY c.codigo, c.nombre, c.empresa_id, c.ruta " +
                $" {quitarCero} " +
                "ORDER BY c.ruta;";
            var balanza = RunQuery(query, parameters);

            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            returnResult.Add("datos_empresa", datosEmpresa);
            returnResult.Add("balanza", balanza);

            return Json(returnResult);
        }
        #endregion

        #region Reporte Mayor Auxiliar
        public JsonResult GetMayorAuxiliar(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            var fechaInicio = GetDate(fc["fechaInicio"].ToString());
            var fechaFin = GetDate(fc["fechaFin"].ToString());
            var cuentaIni = GetString(fc["cuentaIni"].ToString());
            var cuentaFin = GetString(fc["cuentaFin"].ToString());
            bool mostrarCero = Convert.ToBoolean(fc["quitarCero"].ToString());
            string filtroCuenta = "";
            string quitarCero = "";
            var returnResult = new Dictionary<string, object>();

            if (fechaInicio != null && fechaFin != null)
            {
                parameters.Add("fechaInicio", fechaInicio);
                parameters.Add("fechaFin", fechaFin);
            }

            if (cuentaIni != null)
            {
                parameters.Add("cuentaIni", cuentaIni);
                filtroCuenta += " AND c.codigo >= @cuentaIni ";
            }

            if (cuentaFin != null)
            {
                parameters.Add("cuentaFin", cuentaFin);
                filtroCuenta += " AND c.codigo <= @cuentaFin ";
            }

            if (mostrarCero)
            {
                quitarCero = " HAVING (" +
                    "   COALESCE(SUM(CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) ELSE (m.haber_inicial - m.debe_inicial) END),0) != 0 " + // saldo inicial
                    "   OR COALESCE(SUM(m.debe_periodo),0) != 0 " + // debe
                    "   OR COALESCE(SUM(m.haber_periodo),0) != 0 " + // haber
                    "   OR COALESCE(SUM(CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) + (m.debe_periodo - m.haber_periodo) ELSE (m.haber_inicial - m.debe_inicial) + (m.haber_periodo - m.debe_periodo) END),0) != 0 " + // saldo final
                    ")";
            }

            string query = "WITH movimientos AS (" +
                "   SELECT cuenta_finanzas_id, " +
                "       SUM(CASE WHEN dt.fecha < @fechaInicio THEN dt.debe ELSE 0 END) AS debe_inicial, " +
                "       SUM(CASE WHEN dt.fecha < @fechaInicio THEN dt.haber ELSE 0 END) AS haber_inicial, " +
                "       SUM(CASE WHEN dt.fecha >= @fechaInicio AND dt.fecha <= @fechaFin THEN dt.debe ELSE 0 END) AS debe_periodo, " +
                "       SUM(CASE WHEN dt.fecha >= @fechaInicio AND dt.fecha <= @fechaFin THEN dt.haber ELSE 0 END) AS haber_periodo " +
                "   FROM detalles_polizas dt " +
                "   INNER JOIN polizas p ON p.id_poliza = dt.poliza_id " +
                "   WHERE p.empresa_id = @empresa AND p.cancelada = false " +
                "   GROUP BY cuenta_finanzas_id " +
                ") " +
                "SELECT c.id_cuenta_contable, c.codigo, c.nombre, " +
                "   COALESCE(SUM(" +
                "       CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) ELSE (m.haber_inicial - m.debe_inicial) END" +
                "   ),0) AS saldo_inicial," +
                "   COALESCE(SUM(m.debe_periodo),0) AS debe, " +
                "   COALESCE(SUM(m.haber_periodo),0) AS haber, " +
                "   COALESCE(SUM(" +
                "       CASE WHEN c2.naturaleza = 'D' THEN (m.debe_inicial - m.haber_inicial) + (m.debe_periodo - m.haber_periodo) ELSE (m.haber_inicial - m.debe_inicial) + (m.haber_periodo - m.debe_periodo) END" +
                "   ),0) AS saldo_final," +
                "   EXISTS ( " +
                "       SELECT 1 " +
                "       FROM cuentas_finanzas c3 " +
                "       WHERE c3.ruta <@ c.ruta AND c3.ruta != c.ruta " +
                "   ) AS es_padre " +
                "FROM cuentas_finanzas c " +
                "JOIN cuentas_finanzas c2 ON c2.ruta <@ c.ruta " +
                "LEFT JOIN movimientos m ON m.cuenta_finanzas_id = c2.id_cuenta_contable " +
                $"WHERE c.empresa_id = @empresa {filtroCuenta} " +
                "GROUP BY c.codigo, c.nombre, c.empresa_id, c.ruta, c.id_cuenta_contable " +
                $" {quitarCero} " +
                "ORDER BY c.ruta";
            var row = RunQuery(query, parameters);

            List<CuentaResumen> resumen = new List<CuentaResumen>();
            foreach (var item in row)
            {
                CuentaResumen res = new CuentaResumen();
                res.Id = Convert.ToInt32(item["id_cuenta_contable"]);
                res.Codigo = GetString(item["codigo"]);
                res.Nombre = GetString(item["nombre"]);
                res.SaldoInicial = Convert.ToDecimal(item["saldo_inicial"]);
                res.Debe = Convert.ToDecimal(item["debe"]);
                res.Haber = Convert.ToDecimal(item["haber"]);
                res.SaldoFinal = Convert.ToDecimal(item["saldo_final"]);
                res.EsPadre = Convert.ToBoolean(item["es_padre"]);

                resumen.Add(res);
            }

            query = "SELECT c.id_cuenta_contable, c.codigo, c.nombre, p.folio AS poliza, em.folio AS documento, dt.debe, dt.haber, dt.fecha, " +
                "   dt.descripcion, cl.nombre clasificacion, p.id_poliza " +
                "FROM cuentas_finanzas c " +
                "JOIN detalles_polizas dt ON dt.cuenta_finanzas_id = c.id_cuenta_contable " +
                "JOIN polizas p ON p.id_poliza = dt.poliza_id AND p.cancelada = false " +
                "JOIN clasificacion_poliza cl ON cl.id_clasificacion_poliza = p.tipo " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "WHERE c.empresa_id = @empresa " +
                "   AND NOT EXISTS (" +
                "       SELECT 1 FROM cuentas_finanzas c2 WHERE c2.ruta <@ c.ruta AND c2.ruta != c.ruta" +
                $"   ) AND dt.fecha BETWEEN @fechaInicio AND @fechaFin {filtroCuenta} " +
                "ORDER BY c.codigo, dt.fecha";
            row = RunQuery(query, parameters);

            List<CuentaDetalle> detalles = new List<CuentaDetalle>();
            foreach (var item in row)
            {
                CuentaDetalle de = new CuentaDetalle();
                de.CuentaId = Convert.ToInt32(item["id_cuenta_contable"]);
                de.Poliza = GetString(item["poliza"]);
                de.PolizaId = GetInt(item["id_poliza"]);
                de.Documento = GetString(item["documento"]);
                de.Debe = Convert.ToDecimal(item["debe"]);
                de.Haber = Convert.ToDecimal(item["haber"]);
                de.Fecha = (DateTime)item["fecha"];
                de.Descripcion = GetString(item["descripcion"]);
                de.Clasificacion = GetString(item["clasificacion"]);

                detalles.Add(de);
            }
            var detalleCuenta = detalles.GroupBy(x => x.CuentaId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var cuenta in resumen)
            {
                if (detalleCuenta.ContainsKey(cuenta.Id))
                {
                    cuenta.Detalle = detalleCuenta[cuenta.Id];
                }
                else
                {
                    cuenta.Detalle = new List<CuentaDetalle>();
                }
            }

            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            returnResult.Add("datos_empresa", datosEmpresa);
            returnResult.Add("resumen", resumen);

            return Json(returnResult);
        }

        public JsonResult GetPolizaData(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("poliza", GetInt(fc["poliza"].ToString()));

            string query = "SELECT cp.nombre tipo, p.fecha, p.descripcion, p.referencia, p.estado, " +
                "   u.nombre || ' ' || u.apellido AS usuario, p.uuid, ROUND(SUM(dp.debe), 2) AS total_debe, ROUND(SUM(dp.haber), 2) AS total_haber, " +
                "   ROUND(SUM(dp.debe) - SUM(dp.haber), 2) AS saldo, p.id_poliza, " +
                "   em.folio, p.fecha_creacion, em.par, p.es_manual, p.folio folio_poliza " +
                "FROM polizas p " +
                "INNER JOIN usuarios u ON u.usuarioid = p.creado_por " +
                "INNER JOIN detalles_polizas dp ON dp.poliza_id = p.id_poliza " +
                "LEFT JOIN encabezadomov em ON em.id_encabezado = p.referencia " +
                "INNER JOIN areas a ON a.areaid = dp.centro_costos " +
                "INNER JOIN clasificacion_poliza cp ON cp.id_clasificacion_poliza = p.tipo " +
                "WHERE p.id_poliza = @poliza AND p.cancelada = false " +
                "GROUP BY cp.nombre, em.par, em.folio, p.tipo, p.fecha, p.descripcion, p.referencia, p.estado, u.nombre, u.apellido, p.uuid, em.gen, em.nat, em.fch, em.fol_doc, em.variacion, p.id_poliza " +
                "ORDER BY p.fecha_creacion DESC ";

            var poliza = RunQuery(query, parameters)[0];

            return Json(poliza);
        }
        #endregion

        #region Reporte de Estado de Resultados
        #region Estatico
        public JsonResult GetEstadoResultadosGeneral(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            var mes = GetInt(fc["mes"].ToString());
            var anio = GetInt(fc["anio"].ToString());
            int? empresa = GetInt(fc["empresa"].ToString());
            int? sucursal = GetInt(fc["sucursal"].ToString());
            int? centro = GetInt(fc["centro"].ToString());
            string filtro = "";

            if (empresa != null && empresa > 0)
            {
                filtro += " AND p.empresa_id = @empresa ";
            }

            if (sucursal != null && sucursal > 0)
            {
                filtro += " AND e.suc = @sucursal ";
            }

            if (centro != null && centro > 0)
            {
                filtro += " AND dp.centro_costos = @centro ";
            }

            if (mes == null || anio == null)
                throw new Exception("Mes y año son obligatorios");

            var fechaInicio = new DateTime(anio.Value, mes.Value, 1);
            var fechaFin = fechaInicio.AddMonths(1);

            parameters.Add("fecha_inicio", fechaInicio);
            parameters.Add("fecha_fin", fechaFin);
            parameters.Add("fecha_anio", GetDate("2026-01-01"));
            parameters.Add("empresa", empresa);
            parameters.Add("sucursal", sucursal);
            parameters.Add("centro", centro);

            var returnResult = new Dictionary<string, object>();

            string query = $@"
                WITH ingresos  AS (
	                SELECT 
    	                (
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) +
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-3%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) -
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '4-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0)
    	                ) AS total_ingresos,
		                (
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) +
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) -
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '4-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0)
	                    ) AS total_ingresos_anteriores,
		                (
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) +
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) -
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '4-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0)
	                    ) AS total_ingresos_acumulados
	                FROM detalles_polizas dp
                    INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
	                WHERE cf.codigo ILIKE '4-%' AND p.cancelada = false {filtro}
                ),
                costos AS (
	                SELECT
                        (
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) +
	                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0)
	                    ) AS total_costos,
                        (
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) +
	                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0)
	                    ) AS total_costos_anteriores,
                        (
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) +
	                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0)
	                    ) AS total_costos_acumulados
                    FROM detalles_polizas dp
                    INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
                    WHERE cf.codigo ILIKE '5-%' AND p.cancelada = false {filtro}
                ),
                utilidad_bruta AS (
	                SELECT COALESCE((total_ingresos - total_costos), 0) utilidad_bruta,
		                COALESCE((total_ingresos_anteriores - total_costos_anteriores), 0) utilidad_bruta_anterior,
		                COALESCE((total_ingresos_acumulados - total_costos_acumulados), 0) utilidad_bruta_acumulada
	                FROM ingresos
	                CROSS JOIN costos
                ),
                gastos AS (
                    SELECT
    	                (
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '6-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) + 
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo = '6-7-01' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0)
    	                ) total_gastos,
    	                (
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '6-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) + 
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo = '6-7-01' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0)
    	                ) total_gastos_anteriores,	
    	                (
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '6-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) + 
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo = '6-7-01' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0)
    	                ) total_gastos_acumulados
	                FROM detalles_polizas dp
	                INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
	                WHERE cf.codigo ILIKE '6-%' AND p.cancelada = false {filtro}
                ),
                utilidad_operativa AS(
	                SELECT COALESCE((utilidad_bruta - total_gastos), 0) utilidad_operativa,
		                COALESCE((utilidad_bruta_anterior - total_gastos_anteriores), 0) utilidad_operativa_anterior,
		                COALESCE((utilidad_bruta_acumulada - total_gastos_acumulados), 0) utilidad_operativa_acumulada
	                FROM utilidad_bruta
	                CROSS JOIN gastos
                ),
                financieros AS (
                    SELECT
	                    COALESCE(SUM(
	                        CASE 
	                            WHEN cf.codigo ILIKE '7-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            WHEN cf.codigo ILIKE '7-3%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-4%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            ELSE 0
	                        END
	                    ), 0) AS total_financiero,
	                    COALESCE(SUM(
	                        CASE 
	                            WHEN cf.codigo ILIKE '7-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.haber - dp.debe
	                            WHEN cf.codigo ILIKE '7-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-4%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.haber - dp.debe
	                            ELSE 0
	                        END
	                    ), 0) AS total_financiero_anterior,
	                    COALESCE(SUM(
	                        CASE 
	                            WHEN cf.codigo ILIKE '7-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            WHEN cf.codigo ILIKE '7-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-4%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            ELSE 0
	                        END
	                    ), 0) AS total_financiero_acumulado
	                FROM detalles_polizas dp 
	                INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
	                WHERE cf.codigo ILIKE '7-%' AND p.cancelada = false {filtro}
                ),
                utilidad_neta AS (
	                SELECT COALESCE((utilidad_operativa + total_financiero), 0) utilidad_neta,
		                COALESCE((utilidad_operativa_anterior + total_financiero_anterior), 0) utilidad_neta_anterior,
		                COALESCE((utilidad_operativa_acumulada + total_financiero_acumulado), 0) utilidad_neta_acumulada
	                FROM utilidad_operativa
	                CROSS JOIN financieros
                )
                SELECT i.total_ingresos_anteriores, i.total_ingresos, i.total_ingresos_acumulados,
	                c.total_costos_anteriores, c.total_costos, c.total_costos_acumulados,
	                ub.utilidad_bruta_anterior, ub.utilidad_bruta, ub.utilidad_bruta_acumulada,
	                g.total_gastos_anteriores, g.total_gastos, g.total_gastos_acumulados,
	                uo.utilidad_operativa_anterior, uo.utilidad_operativa, uo.utilidad_operativa_acumulada,
	                f.total_financiero_anterior, f.total_financiero, f.total_financiero_acumulado,
 	                un.utilidad_neta_anterior, un.utilidad_neta, un.utilidad_neta_acumulada
                FROM ingresos i
                CROSS JOIN costos c
                CROSS JOIN utilidad_bruta ub
                CROSS JOIN gastos g
                CROSS JOIN utilidad_operativa uo
                CROSS JOIN financieros f
                CROSS JOIN utilidad_neta un";

            var estado = RunQuery(query, parameters);

            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            returnResult.Add("datos_empresa", datosEmpresa);
            returnResult.Add("estado", estado);

            return Json(returnResult);
        }

        public JsonResult GetEstadoResultadosDetallado(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            var mes = GetInt(fc["mes"].ToString());
            var anio = GetInt(fc["anio"].ToString());
            int? empresa = GetInt(fc["empresa"].ToString());
            int? sucursal = GetInt(fc["sucursal"].ToString());
            int? centro = GetInt(fc["centro"].ToString());
            string filtro = "";

            if (empresa != null && empresa > 0)
            {
                filtro += " AND p.empresa_id = @empresa ";
            }

            if (sucursal != null && sucursal > 0)
            {
                filtro += " AND e.suc = @sucursal ";
            }

            if (centro != null && centro > 0)
            {
                filtro += " AND dp.centro_costos = @centro ";
            }

            if (mes == null || anio == null)
                throw new Exception("Mes y año son obligatorios");

            var fechaInicio = new DateTime(anio.Value, mes.Value, 1);
            var fechaFin = fechaInicio.AddMonths(1);

            parameters.Add("fecha_inicio", fechaInicio);
            parameters.Add("fecha_fin", fechaFin);
            parameters.Add("fecha_anio", GetDate("2026-01-01"));
            parameters.Add("empresa", empresa);
            parameters.Add("sucursal", sucursal);
            parameters.Add("centro", centro);

            var returnResult = new Dictionary<string, object>();

            string query = $@"
                WITH ingresos  AS (
	                SELECT
    	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS ventas,
    	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-3%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS otros_ingresos,
    	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '4-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS descuentos,
    	                (
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) +
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-3%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) -
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '4-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0)
    	                ) AS total_ingresos,
		                (
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) +
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) -
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '4-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0)
	                    ) AS total_ingresos_anteriores,
		                (
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) +
        	                COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '4-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) -
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '4-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0)
	                    ) AS total_ingresos_acumulados
	                FROM detalles_polizas dp
	                INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
	                WHERE cf.codigo ILIKE '4-%' AND p.cancelada = false {filtro}
                ),
                costos AS (
	                SELECT 
                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS costo_venta,
                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS compras,
                        (
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) +
	                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0)
	                    ) AS total_costos,
                        (
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) +
	                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0)
	                    ) AS total_costos_anteriores,
                        (
        	                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) +
	                        COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '5-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0)
	                    ) AS total_costos_acumulados
                    FROM detalles_polizas dp
                    INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
                    WHERE cf.codigo ILIKE '5-%' AND p.cancelada = false {filtro}
                ),
                utilidad_bruta AS (
	                SELECT COALESCE((total_ingresos - total_costos), 0) utilidad_bruta,
		                COALESCE((total_ingresos_anteriores - total_costos_anteriores), 0) utilidad_bruta_anterior,
		                COALESCE((total_ingresos_acumulados - total_costos_acumulados), 0) utilidad_bruta_acumulada
	                FROM ingresos
	                CROSS JOIN costos
                ),
                gastos AS (
                    SELECT COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '6-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) gastos_generales,
    	                (
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '6-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) + 
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo = '6-7-01' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0)
    	                ) total_gastos,
    	                (
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '6-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0) + 
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo = '6-7-01' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio), 0)
    	                ) total_gastos_anteriores,	
    	                (
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '6-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0) + 
    		                COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo = '6-7-01' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin), 0)
    	                ) total_gastos_acumulados
	                FROM detalles_polizas dp
	                INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
	                WHERE cf.codigo ILIKE '6-%' AND p.cancelada = false {filtro}
                ),
                utilidad_operativa AS(
	                SELECT COALESCE((utilidad_bruta - total_gastos), 0) utilidad_operativa,
		                COALESCE((utilidad_bruta_anterior - total_gastos_anteriores), 0) utilidad_operativa_anterior,
		                COALESCE((utilidad_bruta_acumulada - total_gastos_acumulados), 0) utilidad_operativa_acumulada
	                FROM utilidad_bruta
	                CROSS JOIN gastos
                ),
                financieros AS (
                    SELECT 
	                    COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '7-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS gastos_financieros,
	                    COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '7-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS productos_financieros,
	                    COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo ILIKE '7-3%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS otros_gastos,
	                    COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo ILIKE '7-4%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin), 0) AS otros_productos,
	                    COALESCE(SUM(
	                        CASE 
	                            WHEN cf.codigo ILIKE '7-1%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-2%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            WHEN cf.codigo ILIKE '7-3%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-4%' AND dp.fecha >= @fecha_inicio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            ELSE 0
	                        END
	                    ), 0) AS total_financiero,
	                    COALESCE(SUM(
	                        CASE 
	                            WHEN cf.codigo ILIKE '7-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.haber - dp.debe
	                            WHEN cf.codigo ILIKE '7-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-4%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_inicio THEN dp.haber - dp.debe
	                            ELSE 0
	                        END
	                    ), 0) AS total_financiero_anterior,
	                    COALESCE(SUM(
	                        CASE 
	                            WHEN cf.codigo ILIKE '7-1%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-2%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            WHEN cf.codigo ILIKE '7-3%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.debe - dp.haber
	                            WHEN cf.codigo ILIKE '7-4%' AND dp.fecha >= @fecha_anio AND dp.fecha < @fecha_fin THEN dp.haber - dp.debe
	                            ELSE 0
	                        END
	                    ), 0) AS total_financiero_acumulado
	                FROM detalles_polizas dp 
	                INNER JOIN polizas p ON p.id_poliza = dp.poliza_id
	                INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id 
                    INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id
	                WHERE cf.codigo ILIKE '7-%' AND p.cancelada = false {filtro}
                ),
                utilidad_neta AS (
	                SELECT COALESCE((utilidad_operativa + total_financiero), 0) utilidad_neta,
		                COALESCE((utilidad_operativa_anterior + total_financiero_anterior), 0) utilidad_neta_anterior,
		                COALESCE((utilidad_operativa_acumulada + total_financiero_acumulado), 0) utilidad_neta_acumulada
	                FROM utilidad_operativa
	                CROSS JOIN financieros
                )
                SELECT i.ventas, i.otros_ingresos, i.descuentos, i.total_ingresos_anteriores, i.total_ingresos, i.total_ingresos_acumulados,
	                c.compras, c.costo_venta, c.total_costos_anteriores, c.total_costos, c.total_costos_acumulados,
	                ub.utilidad_bruta_anterior, ub.utilidad_bruta, ub.utilidad_bruta_acumulada,
	                g.gastos_generales, g.total_gastos_anteriores, g.total_gastos, g.total_gastos_acumulados,
	                uo.utilidad_operativa_anterior, uo.utilidad_operativa, uo.utilidad_operativa_acumulada,
	                f.gastos_financieros, f.productos_financieros, f.otros_gastos, f.otros_productos, f.total_financiero_anterior, f.total_financiero, f.total_financiero_acumulado,
 	                un.utilidad_neta_anterior, un.utilidad_neta, un.utilidad_neta_acumulada
                FROM ingresos i
                CROSS JOIN costos c
                CROSS JOIN utilidad_bruta ub
                CROSS JOIN gastos g
                CROSS JOIN utilidad_operativa uo
                CROSS JOIN financieros f
                CROSS JOIN utilidad_neta un";

            var estado = RunQuery(query, parameters);

            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            returnResult.Add("datos_empresa", datosEmpresa);
            returnResult.Add("estado", estado);

            return Json(returnResult);
        }
        #endregion

        #region Personalizado
        public JsonResult GetEstadoResultadosPersonalizado(IFormCollection fc)
        {
            var mes = GetInt(fc["mes"].ToString());
            var anio = GetInt(fc["anio"].ToString());
            var jsonSecciones = fc["secciones"].ToString();
            int? empresa = GetInt(fc["empresa"].ToString());
            int? sucursal = GetInt(fc["sucursal"].ToString());
            int? centro = GetInt(fc["centro"].ToString());
            string filtro = "";

            if (empresa != null && empresa > 0)
            {
                filtro += " AND p.empresa_id = @empresa ";
            }

            if (sucursal != null && sucursal > 0)
            {
                filtro += " AND e.suc = @sucursal ";
            }

            if (centro != null && centro > 0)
            {
                filtro += " AND dp.centro_costos = @centro ";
            }

            if (mes == null || anio == null || string.IsNullOrEmpty(jsonSecciones))
                throw new Exception("Parámetros incompletos");

            var secciones = JsonConvert.DeserializeObject<List<SeccionER>>(jsonSecciones);

            var fechaInicio = new DateTime(anio.Value, mes.Value, 1);
            var fechaFin = fechaInicio.AddMonths(1);
            var fechaAnio = new DateTime(anio.Value, 1, 1);

            // ── Construir CTEs dinámicamente ─────────────────────────────────────────
            var ctes = new StringBuilder();
            var cteNames = new List<string>();
            var parameters = new Dictionary<string, object>
            {
                { "fecha_inicio", fechaInicio },
                { "fecha_fin", fechaFin },
                { "fecha_anio", fechaAnio },
                { "empresa", empresa },
                { "sucursal", sucursal },
                { "centro", centro },
            };

            for (int s = 0; s < secciones.Count; s++)
            {
                var seccion = secciones[s];
                var cteName = $"seccion_{s}";
                cteNames.Add(cteName);

                var selectMes = new List<string>();
                var selectAnt = new List<string>();
                var selectAcum = new List<string>();

                for (int f = 0; f < seccion.Filas.Count; f++)
                {
                    var fila = seccion.Filas[f];
                    var paramIni = $"cuenta_ini_{s}_{f}";
                    var paramFin = $"cuenta_fin_{s}_{f}";

                    var cuentaFin = string.IsNullOrEmpty(fila.CuentaFin) ? fila.CuentaIni : fila.CuentaFin;

                    parameters[paramIni] = fila.CuentaIni;
                    parameters[paramFin] = cuentaFin;

                    // signo: "haber" → haber-debe (cuenta acreedora, positivo)
                    //        "debe"  → debe-haber (cuenta deudora,  positivo)
                    string expr(string desde, string hasta) => fila.Signo == "haber"
                        ? $"COALESCE(SUM(dp.haber - dp.debe) FILTER (WHERE cf.codigo BETWEEN @{paramIni} AND @{paramFin} AND dp.fecha >= {desde} AND dp.fecha < {hasta}), 0)"
                        : $"COALESCE(SUM(dp.debe - dp.haber) FILTER (WHERE cf.codigo BETWEEN @{paramIni} AND @{paramFin} AND dp.fecha >= {desde} AND dp.fecha < {hasta}), 0)";


                    var aliasF = $"fila_{f}";
                    selectMes.Add($"{expr("@fecha_inicio", "@fecha_fin")}   AS {aliasF}");
                    selectAnt.Add($"{expr("@fecha_anio", "@fecha_inicio")} AS {aliasF}_ant");
                    selectAcum.Add($"{expr("@fecha_anio", "@fecha_fin")}   AS {aliasF}_acum");
                }

                // Total de la sección = suma de todas las filas
                var totalMes = string.Join(" ", seccion.Filas.Select((fila, f) =>
                {
                    var op = fila.Signo == "debe" ? "-" : "+";
                    return $"{op} fila_{f}";
                })).TrimStart('+', ' ');

                var totalAnt = string.Join(" ", seccion.Filas.Select((fila, f) =>
                {
                    var op = fila.Signo == "debe" ? "-" : "+";
                    return $"{op} fila_{f}_ant";
                })).TrimStart('+', ' ');

                var totalAcum = string.Join(" ", seccion.Filas.Select((fila, f) =>
                {
                    var op = fila.Signo == "debe" ? "-" : "+";
                    return $"{op} fila_{f}_acum";
                })).TrimStart('+', ' ');

                ctes.AppendLine(s > 0 ? "," : "WITH");
                ctes.AppendLine($"{cteName} AS (");
                ctes.AppendLine("    SELECT");
                ctes.AppendLine("        base.*,");
                ctes.AppendLine($"       ({totalMes})    AS total_seccion,");
                ctes.AppendLine($"       ({totalAnt})    AS total_seccion_ant,");
                ctes.AppendLine($"       ({totalAcum})   AS total_seccion_acum");
                ctes.AppendLine("    FROM (");
                ctes.AppendLine("        SELECT");
                ctes.AppendLine("            " + string.Join(",\n            ", selectMes.Concat(selectAnt).Concat(selectAcum)));
                ctes.AppendLine("        FROM detalles_polizas dp");
                ctes.AppendLine("        INNER JOIN polizas p ON p.id_poliza = dp.poliza_id");
                ctes.AppendLine("        INNER JOIN cuentas_finanzas cf ON cf.id_cuenta_contable = dp.cuenta_finanzas_id");
                ctes.AppendLine("        INNER JOIN encabezadomov e ON e.id_encabezado = dp.encabezado_id");
                ctes.AppendLine($"       WHERE 1=1 AND p.cancelada = false {filtro}");
                ctes.AppendLine("    ) base");
                ctes.AppendLine(")");
            }

            // ── SELECT final — une todos los CTEs ────────────────────────────────────
            var fromClause = string.Join("\nCROSS JOIN ", cteNames);
            var selectCols = string.Join(",\n    ",
                cteNames.Select((n, i) =>
                    string.Join(", ",
                        secciones[i].Filas.Select((_, f) => $"{n}.fila_{f} AS s{i}_fila_{f}")
                        .Concat(new[] {
                            $"{n}.total_seccion AS s{i}_total",
                            $"{n}.total_seccion_ant AS s{i}_total_ant",
                            $"{n}.total_seccion_acum AS s{i}_total_acum"
                        })
                    )
                )
            );
            var utilidadMes = string.Join(" + ", cteNames.Select(n => $"{n}.total_seccion"));
            var utilidadAnt = string.Join(" + ", cteNames.Select(n => $"{n}.total_seccion_ant"));
            var utilidadAcum = string.Join(" + ", cteNames.Select(n => $"{n}.total_seccion_acum"));

            var query = $@"
                {ctes}
                SELECT
                    {selectCols},
                    ({utilidadMes})  AS utilidad_neta,
                    ({utilidadAnt})  AS utilidad_neta_anterior,
                    ({utilidadAcum}) AS utilidad_neta_acumulada
                FROM {fromClause}";

            var estado = RunQuery(query, parameters);

            // ── Enriquecer resultado con metadata de secciones ────────────────────────
            // Para que el frontend pueda reconstruir etiquetas y valores por fila
            string perfil = HttpContext.Session.GetString("EmpresaFactura");
            var datosEmpresa = new Dictionary<string, string>();
            string GetEmisores(string campo) => _configuration[$"emisores:{perfil}:{campo}"] ?? "";
            datosEmpresa.Add("razon_social", GetEmisores("RazonSocial"));
            datosEmpresa.Add("RFC", GetEmisores("Rfc"));
            datosEmpresa.Add("direccion", GetEmisores("Direccion"));
            datosEmpresa.Add("telefono", GetEmisores("Telefono"));

            var resultado = new
            {
                secciones_meta = secciones,   // etiquetas y orden
                estado = estado,
                datos_empresa = datosEmpresa
            };

            return Json(resultado);
        }

        // ── DTOs ──────────────────────────────────────────────────────────────────────
        public class SeccionER
        {
            public string Nombre { get; set; }
            public List<FilaER> Filas { get; set; }
        }

        public class FilaER
        {
            public string Etiqueta { get; set; }
            public string CuentaIni { get; set; }   // antes: Cuenta
            public string CuentaFin { get; set; }   // nuevo
            public string Signo { get; set; }   // "debe" | "haber"
            public bool Mostrar { get; set; } = true;
        }
        #endregion

        #region Guardar Configuracion
        public JsonResult GuardarConfiguracionER(IFormCollection fc)
        {
            try
            {
                var json = fc["json"].ToString();
                if (string.IsNullOrEmpty(json))
                    throw new Exception("No mandaste nada");

                var config = JsonConvert.DeserializeObject<ConfiguracionER>(json);
                bool esActualizacion = config.Id.HasValue && config.Id.Value > 0;

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            int configId;

                            if (esActualizacion)
                            {
                                // Borrar secciones y filas antiguas (CASCADE desde secciones)
                                var secc = RunQuery("SELECT id_seccion FROM secciones_estado_resultados WHERE configuracion_id = @id",
                                    new Dictionary<string, object> { { "id", config.Id.Value } },
                                    false, conn, tx);

                                foreach (var i in secc)
                                {
                                    RunUpdate(
                                        "DELETE FROM filas_estado_resultado WHERE seccion_id = @id",
                                        new Dictionary<string, object> { { "id", Convert.ToInt32(i["id_seccion"]) } },
                                        false, conn, tx);
                                }

                                RunUpdate(
                                    "DELETE FROM secciones_estado_resultados WHERE configuracion_id = @id",
                                    new Dictionary<string, object> { { "id", config.Id.Value } },
                                    false, conn, tx);

                                // Actualizar nombre/fecha
                                RunUpdate(
                                    "UPDATE configuracion_estado_resultado SET nombre_configuracion = @nombre, fecha_creacion = @fecha WHERE id_configuracion = @id",
                                    new Dictionary<string, object> {
                                        { "nombre", config.Nombre },
                                        { "fecha",  config.Fecha  },
                                        { "id",     config.Id.Value }
                                    },
                                    false, conn, tx);

                                configId = config.Id.Value;
                            }
                            else
                            {
                                // INSERT nuevo
                                var p = new Dictionary<string, object> {
                                    { "nombre",   config.Nombre },
                                    { "fecha",    config.Fecha  },
                                    { "empresa",  HttpContext.Session.GetInt32("Empresa") }
                                };
                                configId = Convert.ToInt32(RunScalar(
                                    "INSERT INTO configuracion_estado_resultado(nombre_configuracion, fecha_creacion, empresa_id) " +
                                    "VALUES(@nombre, @fecha, @empresa) RETURNING id_configuracion",
                                    p, false, conn, tx));
                            }

                            // Insertar secciones y filas (igual que antes)
                            foreach (var seccion in config.Secciones)
                            {
                                var p2 = new Dictionary<string, object> {
                                    { "nombre",   seccion.Nombre },
                                    { "configId", configId }
                                };
                                var seccionId = Convert.ToInt32(RunScalar(
                                    "INSERT INTO secciones_estado_resultados(nombre, configuracion_id) " +
                                    "VALUES(@nombre, @configId) RETURNING id_seccion",
                                    p2, false, conn, tx));

                                foreach (var fila in seccion.Filas)
                                {
                                    RunUpdate(
                                        "INSERT INTO filas_estado_resultado(etiqueta, cuenta_inicial, cuenta_final, tipo, mostrar, seccion_id) " +
                                        "VALUES(@etiqueta, @ini, @fin, @tipo, @mostrar, @seccionId)",
                                        new Dictionary<string, object> {
                                            { "etiqueta",  fila.Etiqueta  },
                                            { "ini",       fila.CuentaIni },
                                            { "fin",       fila.CuentaFin },
                                            { "tipo",      fila.Signo     },
                                            { "mostrar",   fila.Mostrar   },
                                            { "seccionId", seccionId      }
                                        },
                                        false, conn, tx);
                                }
                            }

                            tx.Commit();
                            // ← Devuelve el id para que el frontend lo registre
                            return Json(new { icon = "success", title = "Configuración guardada", id = configId });
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Error al guardar", html = ex.Message });
            }
        }

        public JsonResult CargarConfiguracion(int id)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("id", id);

            string query = " SELECT c.id_configuracion, c.nombre_configuracion, c.fecha_creacion, s.id_seccion, s.nombre AS nombre_seccion, " +
                "   f.id_fila, f.etiqueta, f.cuenta_inicial, f.cuenta_final, f.tipo, f.mostrar " +
                "FROM configuracion_estado_resultado c " +
                "LEFT JOIN secciones_estado_resultados s ON s.configuracion_id = c.id_configuracion " +
                "LEFT JOIN filas_estado_resultado f ON f.seccion_id = s.id_seccion " +
                "WHERE c.id_configuracion = @id " +
                "ORDER BY s.id_seccion, f.id_fila";
            var rows = RunQuery(query, parameters);

            if (rows.Count == 0)
                throw new Exception("No existe esa config");

            // ── reconstrucción ─────────────────────────────────────

            var config = new
            {
                nombre = rows[0]["nombre_configuracion"],
                fecha = rows[0]["fecha_creacion"],
                secciones = new List<object>()
            };

            var seccionesDict = new Dictionary<int, dynamic>();

            foreach (var row in rows)
            {
                if (row["id_seccion"] == null)
                    continue;

                int seccionId = (int)row["id_seccion"];

                if (!seccionesDict.ContainsKey(seccionId))
                {
                    var nuevaSeccion = new
                    {
                        id = seccionId,
                        nombre = row["nombre_seccion"],
                        filas = new List<object>()
                    };

                    seccionesDict[seccionId] = nuevaSeccion;
                    config.secciones.Add(nuevaSeccion);
                }

                if (row["id_fila"] != null)
                {
                    ((List<object>)seccionesDict[seccionId].filas).Add(new
                    {
                        etiqueta = row["etiqueta"],
                        cuentaIni = row["cuenta_inicial"],
                        cuentaFin = row["cuenta_final"],
                        signo = row["tipo"],
                        mostrar = row["mostrar"]
                    });
                }
            }

            return Json(config);
        }

        public JsonResult GetConfiguraciones()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

            string query = "SELECT nombre_configuracion, id_configuracion " +
                "FROM configuracion_estado_resultado " +
                "WHERE empresa_id = @empresa";
            var config = RunQuery(query, parameters);

            return Json(config);
        }
        #endregion
        #endregion

        #region Reporte de costo de inventario
        public JsonResult ReporteCostoInventario(IFormCollection fc)
        {
            string nombre = fc["nombre"].ToString();
            int? page = GetInt(fc["page"].ToString());
            int? pageSize = GetInt(fc["pageSize"].ToString());
            string sortColumn = fc["sortColumn"].ToString();
            string sortDir = fc["sortDir"].ToString();

            // "producto" viene del filtro del reporte; "nombre" viene del buscador interno
            // del TableBuilder. Los dos hacen lo mismo: filtrar por clave/descripción.
            // Usamos el que venga con valor, priorizando "producto".
            string busqueda = !string.IsNullOrWhiteSpace(fc["producto"].ToString())
                ? fc["producto"].ToString()
                : nombre;

            string where = "";

            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                parameters.Add("busqueda", busqueda);
                where = " AND (c.cve_prod   ILIKE '%' || @busqueda || '%'" +
                        "   OR c.descr_prod ILIKE '%' || @busqueda || '%') ";
            }

            //string query =
            //    "SELECT rc.producto_id, c.cve_prod, c.descr_prod, " +
            //    "       SUM(rc.cantidad)   AS total_piezas, " +
            //    "       SUM(rc.costo_total) AS costo_total, " +
            //    "       SUM(rc.costo_total)::numeric / NULLIF(SUM(rc.cantidad), 0) AS costo_promedio " +
            //    "FROM registro_compras rc " +
            //    "INNER JOIN catproductos c ON c.id_catproductos = rc.producto_id " +
            //    $"WHERE rc.revertido = false AND rc.empresa_id = @empresa {where} " +
            //    "GROUP BY rc.producto_id, c.cve_prod, c.descr_prod " +
            //    "HAVING SUM(rc.cantidad) > 0 " +
            //    "ORDER BY costo_total DESC " +
            //    "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            string query = "WITH costo_promedio AS ( " +
                "   SELECT producto_id, SUM(costo_total)::NUMERIC / NULLIF(SUM(cantidad), 0) AS costo_promedio, SUM(costo_total) costo_total, SUM(cantidad) cantidad " +
                "   FROM registro_compras " +
                "   WHERE revertido = FALSE AND empresa_id = @empresa AND cantidad > 0 " +
                "   GROUP BY producto_id " +
                "), " +
                "ultimo_costo AS ( " +
                "   SELECT DISTINCT ON (producto_id) producto_id, costo_unitario AS ultimo_costo, fecha " +
                "   FROM registro_compras " +
                "   WHERE revertido = FALSE AND empresa_id = @empresa AND encabezado_venta IS NULL AND cantidad > 0 " +
                "   ORDER BY producto_id, fecha DESC, id_registro_compra DESC " +
                ") " +
                "SELECT c.id_catproductos, c.cve_prod, c.descr_prod, SUM(rc.cantidad_restante) AS existencia_actual, cp.costo_promedio, " +
                "   uc.ultimo_costo, uc.fecha AS fecha_ultima_compra, SUM(rc.cantidad_restante) * cp.costo_promedio AS valor_inventario " +
                "FROM catproductos c " +
                "JOIN registro_compras rc ON rc.producto_id = c.id_catproductos " +
                "JOIN costo_promedio cp ON cp.producto_id = c.id_catproductos " +
                "LEFT JOIN ultimo_costo uc ON uc.producto_id = c.id_catproductos " +
                $"WHERE rc.revertido = FALSE AND rc.empresa_id = @empresa AND rc.cantidad_restante > 0 {where} " +
                "GROUP BY c.id_catproductos, c.cve_prod, c.descr_prod, cp.costo_promedio, uc.ultimo_costo, uc.fecha;";

            var result = RunQuery(query, parameters);

            // Los indicadores del reporte son de TODO el filtro, no de la página
            // que se está mostrando, así que se calculan aparte sobre el mismo
            // where. De aquí sale también el COUNT que usa la paginación.
            // No se necesitan offset/pageSize, pero sí el where.
            string queryTotales =
                "SELECT COUNT(*) AS productos, COALESCE(SUM(t.costo_total), 0) AS costo_total, COALESCE(SUM(t.total_piezas), 0) AS total_piezas " +
                "FROM ( " +
                "   SELECT SUM(rc.cantidad) AS total_piezas, " +
                "          SUM(rc.costo_total) AS costo_total " +
                "   FROM registro_compras rc " +
                "   INNER JOIN catproductos c ON c.id_catproductos = rc.producto_id " +
                $"  WHERE rc.revertido = false AND rc.empresa_id = @empresa {where} " +
                "   GROUP BY rc.producto_id, c.cve_prod, c.descr_prod " +
                "   HAVING SUM(rc.cantidad) > 0 " +
                ") t";

            var totales = RunQuery(queryTotales, parameters).FirstOrDefault() ?? new Dictionary<string, object>();

            int total = Convert.ToInt32(totales.GetValueOrDefault("productos", 0));

            return Json(new { data = result, total, totales });
        }

        public JsonResult ReporteProductosVendidos(IFormCollection fc)
        {
            string nombre = fc["nombre"].ToString();
            int? page = GetInt(fc["page"].ToString());
            int? pageSize = GetInt(fc["pageSize"].ToString());
            string sortColumn = fc["sortColumn"].ToString();
            string sortDir = fc["sortDir"].ToString();

            string producto = fc["producto"].ToString();
            string fechaIni = fc["fechaIni"].ToString();
            string fechaFin = fc["fechaFin"].ToString();
            string sucursal = fc["sucursal"].ToString();
            string almacen = fc["almacen"].ToString();

            string where = "";

            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                parameters.Add("nombre", nombre);
                where = " AND (c.cve_prod ILIKE '%' || @nombre || '%' OR" +
                    "   C.descr_prod ILIKE '%' || @nombre || '%' " +
                    ") ";
            }

            string query = "SELECT tm.producto_id, c.cve_prod, c.descr_prod, SUM(tm.cantidad) AS total_vendido, cu.cve_udm unidad " +
                "FROM tarimas_mov tm " +
                "INNER JOIN catproductos c ON c.id_catproductos = tm.producto_id " +
                "INNER JOIN catunidades cu ON cu.id_udm = tm.unidad " +
                $"WHERE tm.tipo_movimiento = 'venta' {where} " +
                "GROUP BY tm.producto_id, c.cve_prod, c.descr_prod, tm.unidad, cu.cve_udm " +
                "ORDER BY total_vendido DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            var result = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM( " +
                "   SELECT tm.producto_id, c.cve_prod, c.descr_prod, SUM(tm.cantidad) AS total_vendido, cu.cve_udm unidad " +
                "   FROM tarimas_mov tm " +
                "   INNER JOIN catproductos c ON c.id_catproductos = tm.producto_id " +
                "   INNER JOIN catunidades cu ON cu.id_udm = tm.unidad " +
                $"  WHERE tm.tipo_movimiento = 'venta' {where} " +
                "   GROUP BY tm.producto_id, c.cve_prod, c.descr_prod, tm.unidad, cu.cve_udm " +
                "   ORDER BY total_vendido DESC " +
                ") t";
            int total = Convert.ToInt32(RunScalar(query, parameters));

            return Json(new { data = result, total });
        }

        public JsonResult GetUltimosCostos(IFormCollection fc)
        {
            string productoId = fc["productoId"].ToString();
            int? limite = GetInt(fc["limite"].ToString());
            string fechaIni = fc["fechaIni"].ToString();
            string fechaFin = fc["fechaFin"].ToString();
            string where = "";

            var parameters = new Dictionary<string, object>();
            parameters.Add("productoId", fc["productoId"].ToString());
            parameters.Add("limite", limite);
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

            if (!string.IsNullOrWhiteSpace(fechaIni))
            {
                where += " AND fecha >= @fechaIni";
                parameters.Add("fechaIni", DateTime.Parse(fechaIni));
            }

            if (!string.IsNullOrWhiteSpace(fechaFin))
            {
                where += " AND fecha < @fechaFinMasUno";
                parameters.Add("fechaFinMasUno", DateTime.Parse(fechaFin).AddDays(1));
            }


            string query = "SELECT rc.producto_id, c.cve_prod, c.descr_prod, SUM(rc.cantidad) AS total_piezas, SUM(rc.costo_total) AS costo_total, " +
                "   (" +
                "       SELECT json_agg( " +
                "           json_build_object( " +
                "               'fecha', x.fecha, " +
                "               'costo', x.costo_unitario " +
                "           ) " +
                "       ) " +
                "   FROM ( " +
                "       SELECT costo_unitario, fecha " +
                "       FROM registro_compras rc2 " +
                "       WHERE rc2.producto_id = rc.producto_id AND rc2.revertido = FALSE AND rc2.encabezado_venta IS NULL AND rc2.empresa_id = @empresa " +
                "       ORDER BY fecha ASC, id_registro_compra DESC " +
                "       LIMIT @limite " +
                "   ) x " +
                ") AS ultimos_costos " +
                "FROM registro_compras rc " +
                "INNER JOIN catproductos c ON c.id_catproductos = rc.producto_id " +
                $"WHERE rc.revertido = FALSE AND rc.encabezado_venta IS NULL AND rc.empresa_id = @empresa AND c.cve_prod = @productoId {where} " +
                "GROUP BY rc.producto_id, c.cve_prod, c.descr_prod " +
                "HAVING SUM(rc.cantidad) > 0 " +
                "ORDER BY costo_total DESC;";

            var result = RunQuery(query, parameters);
            return Json(result);
        }

        public JsonResult GetCostoAlmacen(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

            string nombre = fc["nombre"].ToString();
            int? page = GetInt(fc["page"].ToString());
            int? pageSize = GetInt(fc["pageSize"].ToString());
            string sortColumn = fc["sortColumn"].ToString();
            string sortDir = fc["sortDir"].ToString();

            string producto = fc["producto"].ToString();
            int? sucursal = GetInt(fc["sucursal"].ToString());
            int? almacen = GetInt(fc["almacen"].ToString());

            string where = "";

            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                where += @" AND (
                    cs.cve_sucursal ILIKE '%' || @nombre || '%'
                    OR cs.descripcion ILIKE '%' || @nombre || '%'
                    OR ca.cve_almacen ILIKE '%' || @nombre || '%'
                    OR ca.descripcion ILIKE '%' || @nombre || '%'
                )";
                parameters.Add("nombre", nombre);
            }

            if (sucursal > 0)
            {
                where += " AND cs.id_sucursal = @sucursal";
                parameters.Add("sucursal", sucursal);
            }

            if (almacen > 0)
            {
                where += " AND ca.id_almacen = @almacen";
                parameters.Add("almacen", almacen);
            }

                string query = "WITH costo_producto AS ( " +
                "   SELECT rc.producto_id, (SUM(rc.costo_total::numeric(28,6)) / NULLIF(SUM(rc.cantidad::numeric(28,6)), 0))::numeric(28,6) AS costo_promedio " +
                "   FROM registro_compras rc " +
                "   WHERE rc.revertido = FALSE AND rc.encabezado_venta IS NULL " +
                "   GROUP BY rc.producto_id " +
                ")" +
                "SELECT ca.id_almacen, ca.tipo, SUM(tp.cantidad)::numeric(28,6) AS piezas, COALESCE(SUM(tp.cantidad::numeric(28,6) * cp.costo_promedio), 0)::numeric(28,6) AS valor_inventario, " +
                "   COALESCE(SUM(tp.cantidad::numeric(28,6) * cp.costo_promedio) / NULLIF(SUM(tp.cantidad::numeric(28,6)), 0), 0)::numeric(28,6) AS costo_promedio_almacen, " +
                "   cs.cve_sucursal, cs.descripcion AS descripcion_sucursal, ca.cve_almacen, ca.descripcion AS descripcion_almacen, cs.id_sucursal " +
                "FROM tarima_productos tp " +
                "INNER JOIN costo_producto cp ON cp.producto_id = tp.producto_id " +
                "INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "INNER JOIN catcolumnas cl ON cl.id_columna = cn.columna_id " +
                "INNER JOIN catracks cr ON cr.id_rack = cl.rack_id " +
                "INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                $"WHERE cs.empresa_id = @empresa {where} " +
                "GROUP BY ca.id_almacen, ca.tipo, cs.cve_sucursal, cs.descripcion, cs.id_sucursal " +
                "ORDER BY cs.descripcion ASC, ca.cve_almacen, valor_inventario DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var data = RunQuery(query, parameters);

            // Mismo criterio que la consulta de arriba (empresa de sesión + filtros)
            // para que el COUNT de la paginación y los indicadores correspondan a
            // lo que el usuario está viendo, no a toda la tabla.
            query = "WITH costo_producto AS ( " +
                "   SELECT rc.producto_id, (SUM(rc.costo_total::numeric(28,6)) / NULLIF(SUM(rc.cantidad::numeric(28,6)), 0))::numeric(28,6) AS costo_promedio " +
                "   FROM registro_compras rc " +
                "   WHERE rc.revertido = FALSE AND rc.encabezado_venta IS NULL " +
                "   GROUP BY rc.producto_id " +
                ") " +
                "SELECT COUNT(*)                              AS almacenes, " +
                "       COALESCE(SUM(t.valor_inventario), 0)  AS valor_inventario, " +
                "       COALESCE(SUM(t.piezas), 0)            AS piezas " +
                "FROM ( " +
                "   SELECT ca.id_almacen, " +
                "          SUM(tp.cantidad)::numeric(28,6) AS piezas, " +
                "          COALESCE(SUM(tp.cantidad::numeric(28,6) * cp.costo_promedio), 0)::numeric(28,6) AS valor_inventario " +
                "   FROM tarima_productos tp " +
                "   INNER JOIN costo_producto cp ON cp.producto_id = tp.producto_id " +
                "   INNER JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
                "   INNER JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
                "   INNER JOIN catcolumnas cl ON cl.id_columna = cn.columna_id " +
                "   INNER JOIN catracks cr ON cr.id_rack = cl.rack_id " +
                "   INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
                "   INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                $"  WHERE cs.empresa_id = @empresa {where} " +
                "   GROUP BY ca.id_almacen, ca.tipo, cs.cve_sucursal, cs.descripcion, cs.id_sucursal " +
                ") t";

            var totales = RunQuery(query, parameters).FirstOrDefault()
                          ?? new Dictionary<string, object>();

            int total = Convert.ToInt32(totales.GetValueOrDefault("almacenes", 0));

            return Json(new { data = data, total, totales });
        }

        /// <summary>
        /// Catálogo completo de sucursales y almacenes de la empresa en sesión.
        /// Los selects del reporte se llenaban con las filas de la tabla, que
        /// vienen paginadas: solo aparecían las ubicaciones de la página actual.
        /// </summary>
        public JsonResult GetUbicacionesInventario()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));

            var result = new Dictionary<string, object>();

            string query = "SELECT id_sucursal, cve_sucursal, descripcion " +
                "FROM catsucursales " +
                "WHERE empresa_id = @empresa " +
                "ORDER BY descripcion";
            result.Add("sucursales", RunQuery(query, parameters));

            query = "SELECT ca.id_almacen, ca.cve_almacen, ca.descripcion, ca.tipo, ca.sucursal_id AS id_sucursal " +
                "FROM catalmacenes ca " +
                "INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
                "WHERE cs.empresa_id = @empresa " +
                "ORDER BY ca.cve_almacen";
            result.Add("almacenes", RunQuery(query, parameters));

            return Json(result);
        }
        #endregion

        #region Filtros
        public JsonResult GetEmpresas()
        {
            string query = "SELECT nombre, rfc, empresaid " +
                "FROM empresas";

            var empresas = RunQuery(query);

            return Json(empresas);
        }

        public JsonResult GetSucursales(int empresa)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", empresa);
            string query = "SELECT id_sucursal, cve_sucursal, descripcion " +
                "FROM catsucursales " +
                "WHERE empresa_id = @empresa";

            var sucursales = RunQuery(query, parameters);
            return Json(sucursales);
        }

        public JsonResult GetCentroCostos()
        {
            string query = "SELECT areaid, nombre, descripcion " +
                "FROM areas ";

            var centroCostos = RunQuery(query);
            return Json(centroCostos);
        }
        #endregion
    }
}