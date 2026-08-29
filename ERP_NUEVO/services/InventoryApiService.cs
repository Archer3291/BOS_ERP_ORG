using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;

public class InventoryApiService : Utilities
{
    public List<ProductApi> RegisterInventory(List<ProductApi> products, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
    {
        LogErrorHelper.RegistrarLog(
            "API",
            "INVENTARIO",
            $"Entrando al servicio de registrar Inventario",
            nivel: "DEBUG"
        );

        var parameters = new Dictionary<string, object>();
        var productsInventory = new List<Dictionary<string, object>>();
        List<ProductApi> productsReturn = null;
        List<int> ids = new List<int>();

        string query = "SELECT ct.id_tarima FROM catalmacenes c " +
            "INNER JOIN catsucursales cs ON cs.id_sucursal = c.sucursal_id " +
            "INNER JOIN empresas e ON e.empresaid = cs.empresa_id " +
            "INNER JOIN catracks cr ON cr.almacen_id = c.id_almacen " +
            "INNER JOIN catcolumnas cc ON cc.rack_id = cr.id_rack " +
            "INNER JOIN catniveles cn ON cn.columna_id = cc.id_columna " +
            "INNER JOIN cattarimas ct ON ct.nivel_id = cn.id_nivel " +
            "WHERE e.empresaid = 1 AND c.tipo = 'Maquinado'";
        int destino = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

        foreach (var producto in products)
        {
            parameters = new Dictionary<string, object>();
            parameters.Add("cve", producto.Clave);
            query = "SELECT id_catproductos FROM catproductos WHERE cve_prod = @cve AND empresa_id = 1";
            int id = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
            ids.Add(id);

            var productInventory = new Dictionary<string, object>();
            productInventory["id_producto"] = id;
            productInventory["codigo"] = producto.Clave;
            productInventory["descripcion"] = producto.Descripcion;
            productInventory["cantidad"] = producto.Cantidad;

            parameters.Add("unidad", producto.Unidad);
            query = "SELECT id_udm FROM catunidades WHERE cve_udm = @unidad";
            int unidad = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
            productInventory["unidad"] = unidad;

            productsInventory.Add(productInventory);
        }

        LogErrorHelper.RegistrarLog(
            "API",
            "INVENTARIO",
            $"Tarima id: {destino} Productos Registrados: {JsonConvert.SerializeObject(productsInventory)}",
            nivel: "DEBUG"
        );

        RegistrarMovimiento(productsInventory, 1, "ingreso_api", null, destino, "Carga de inventario de maquinado", null, null, conn, tx);

        parameters = new Dictionary<string, object>();
        parameters.Add("ids", ids);

        query = "SELECT p.id_catproductos, p.cve_prod, p.descr_prod, p.lin_prod, p.tp, p.gpo, p.fmcan, p.udm, " +
            "   crl.prod_sat, crl.ud_sat, crl.obj_impto, tp.cantidad " +
            "FROM tarima_productos tp " +
            "JOIN catproductos p ON p.id_catproductos = tp.producto_id " +
            "JOIN catrelacion crl ON crl.prod_kepler = p.cve_prod " +
            "JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
            "JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
            "JOIN catcolumnas cl ON cl.id_columna = cn.columna_id " +
            "JOIN catracks cr ON cr.id_rack = cl.rack_id " +
            "JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id " +
            "JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id " +
            "JOIN empresas e ON e.empresaid = cs.empresa_id " +
            "WHERE ca.tipo = 'Maquinado' AND p.id_catproductos = ANY(@ids) AND e.empresaid = 1";

        var existing = RunQuery(query, parameters, false, conn, tx);

        if (existing.Count > 0)
        {
            productsReturn = new List<ProductApi>();

            foreach (var row in existing)
            {
                var product = new ProductApi();

                product.Id = Convert.ToInt32(row["id_catproductos"]);
                product.Clave = GetString(row["cve_prod"]);
                product.Descripcion = GetString(row["descr_prod"]);
                product.Linea = GetString(row["lin_prod"]);
                product.Tipo = GetString(row["tp"]);
                product.Grupo = GetString(row["gpo"]);
                product.Unidad = GetString(row["udm"]);
                product.Naturaleza = GetString(row["fmcan"]);
                product.Cantidad = GetDecimal(row["cantidad"], 0);

                productsReturn.Add(product);
            }
        }

        return productsReturn;
    }
}
