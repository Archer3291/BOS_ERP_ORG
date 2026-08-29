using BOS_ERP.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class ContabilidadController : Utilities
    {
        public JsonResult GetFacturas(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            string where = "";
            string codigo = GetString(fc["codigo"].ToString());
            parameters.Add("nombre", GetString(fc["nombre"].ToString()));
            parameters.Add("page", Convert.ToInt32(fc["page"].ToString()));
            parameters.Add("pageSize", GetInt(fc["pageSize"].ToString(), 50));
            parameters.Add("offset", (Convert.ToInt32(fc["page"].ToString()) - 1) * GetInt(fc["pageSize"].ToString(), 50));

            if (codigo != null && codigo != "null")
            {
                parameters.Add("codigo", codigo);
                where = " AND cf.codigo = @codigo ";
            }

            string query = "SELECT f.id_encabezado, f.folio AS factura_folio, f.imp AS importe_factura, f.tipo_proceso tipo_proceso_factura, " +
                "   COUNT(cp.id_encabezado) AS cantidad_complementos, COALESCE(SUM(cp.imp),0) AS importe_complemento, f.fch fecha_factura, " +
                "   COALESCE(f.imp - SUM(cp.imp) , f.imp) AS diferencia, cf.codigo, cf.empresa_id " +
                "FROM encabezadomov f " +
                "LEFT JOIN encabezadomov cp ON cp.encabezados_padre = f.id_encabezado " +
                "   AND cp.tipo_proceso IN ('complemento_pago','cobro_cliente') " +
                "LEFT JOIN cuentas_finanzas cf ON cf.cliente_id = f.refe " +
                "WHERE f.nat IN ('VIFAC', 'VSFAC', 'VNFAC', 'VINFAC', 'RICD', 'FACLIB') " +
                "   AND f.tipo_proceso IN ('registro_carteras','factura_credito') " +
                $" {where} " +
                "GROUP BY f.id_encabezado, f.folio, f.imp, f.tipo_proceso, cf.codigo, cf.empresa_id, f.fch " +
                "ORDER BY cantidad_complementos DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*)" +
                "FROM encabezadomov f " +
                "LEFT JOIN cuentas_finanzas cf ON cf.cliente_id = f.refe AND cf.empresa_id = @empresa " +
                "WHERE f.nat IN ('VIFAC', 'VSFAC', 'VNFAC', 'VINFAC', 'RICD', 'FACLIB') " +
                "   AND f.tipo_proceso IN ('registro_carteras', 'factura_credito') " +
                $" {where} ";

            int total = Convert.ToInt32(RunScalar(query, parameters));

            query = "SELECT SUM(imp) " +
                "FROM encabezadomov f " +
                "LEFT JOIN cuentas_finanzas cf ON cf.cliente_id = f.refe AND cf.empresa_id = @empresa " +
                "WHERE f.nat IN ('VIFAC', 'VSFAC', 'VNFAC', 'VINFAC', 'RICD', 'FACLIB') " +
                "   AND f.tipo_proceso IN ('registro_carteras', 'factura_credito') " +
                $" {where} ";
            var totalPreFac = RunScalar(query, parameters);

            query = "SELECT COALESCE(SUM(cp.imp),0) " +
                "FROM encabezadomov f " +
                "LEFT JOIN encabezadomov cp ON cp.encabezados_padre = f.id_encabezado " +
                "   AND cp.tipo_proceso IN ('complemento_pago','cobro_cliente') " +
                "LEFT JOIN cuentas_finanzas cf ON cf.cliente_id = f.refe AND cf.empresa_id = @empresa " +
                "WHERE f.nat IN ('VIFAC', 'VSFAC', 'VNFAC', 'VINFAC', 'RICD', 'FACLIB') " +
                "   AND f.tipo_proceso IN ('registro_carteras','factura_credito') " +
                $" {where} ";

            var totalPreComp = RunScalar(query, parameters);

            return Json(new { data, total, totales = new { totalPreFac, totalPreComp } });
        }

        public JsonResult GetComplementosFactura(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("factura", GetInt(fc["parentId"].ToString()));

            string query = "SELECT f.id_encabezado AS complemento_id, f.folio AS complemento_folio, f.imp importe_complemento, " +
                " f.tipo_proceso, f.fch fecha_complemento, u.nombre || ' ' || u.apellido usuario " +
                "FROM encabezadomov f " +
                "INNER JOIN usuarios u ON u.nombreusuario = f.usr_doc " +
                "WHERE encabezados_padre = @factura";
            var data = RunQuery(query, parameters);

            return Json(data);
        }

        public JsonResult GetClientes()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("empresa", HttpContext.Session.GetInt32("Empresa"));
            string query = "SELECT nombre, codigo, cliente_id, id_cuenta_contable FROM cuentas_finanzas WHERE cliente_id IS NOT NULL AND empresa_id = @empresa";

            var clientes = RunQuery(query, parameters);

            return Json(clientes);
        }

        public JsonResult GetFacturasExcel(IFormCollection fc)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("codigo", GetString(fc["codigo"].ToString()));
            parameters.Add("empresaId", HttpContext.Session.GetInt32("Empresa"));

            string query = "SELECT f.id_encabezado id_factura, f.folio AS factura_folio, f.imp AS importe_factura, f.tipo_proceso tipo_proceso_factura, " +
               "   COUNT(cp.id_encabezado) AS cantidad_complementos, COALESCE(SUM(cp.imp),0) AS importe_complemento, f.fch fecha_factura, " +
               "   COALESCE(f.imp - SUM(cp.imp) , f.imp) AS diferencia, cf.codigo " +
               "FROM encabezadomov f " +
               "LEFT JOIN encabezadomov cp ON cp.encabezados_padre = f.id_encabezado " +
               "   AND cp.tipo_proceso IN ('complemento_pago','cobro_cliente') " +
               "LEFT JOIN cuentas_finanzas cf ON cf.cliente_id = f.refe AND cf.empresa_id = @empresaId " +
               "WHERE f.nat IN ('VIFAC', 'VSFAC', 'VNFAC', 'VINFAC', 'RICD', 'FACLIB') " +
               "   AND f.tipo_proceso IN ('registro_carteras','factura_credito') " +
               "   AND cf.codigo = @codigo " +
               "GROUP BY f.id_encabezado, f.folio, f.imp, f.tipo_proceso, cf.codigo, cf.empresa_id, f.fch " +
               "ORDER BY cantidad_complementos DESC ";
            var facturas = RunQuery(query, parameters);


            query = "SELECT f.id_encabezado id_factura, cp.folio complemento_folio, cp.imp importe_complemento, cp.tipo_proceso, cp.fch fecha_complemento, " +
                "   f.folio factura_folio " +
                "FROM encabezadomov f " +
                "INNER JOIN encabezadomov cp ON cp.encabezados_padre = f.id_encabezado " +
                "   AND cp.tipo_proceso IN ('complemento_pago','cobro_cliente') " +
                "LEFT JOIN cuentas_finanzas cf ON cf.cliente_id = f.refe AND cf.empresa_id = @empresaId " +
                "WHERE f.nat IN ('VIFAC', 'VSFAC', 'VNFAC', 'VINFAC', 'RICD', 'FACLIB') " +
                "   AND f.tipo_proceso IN ('registro_carteras','factura_credito')" +
            "   AND cf.codigo = @codigo ";
            var complemento = RunQuery(query, parameters);

            return Json(new { facturas, complemento });
        }
    }
}