using Newtonsoft.Json;
using Npgsql;
using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;

public class ProductApiService : Utilities
{
    public List<ProductApi> CreateProduct(List<ProductApi> productos, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
    {
        LogErrorHelper.RegistrarLog(
            "API",
            "PRODUCTO",
            $"Entrando al servicio create product",
            nivel: "DEBUG"
        );

        var parameters = new Dictionary<string, object>();

        foreach (var product in productos)
        {
            parameters = new Dictionary<string, object>();
            parameters.Add("cve", product.Clave);
            parameters.Add("desc", product.Descripcion);
            parameters.Add("linea", product.Linea);
            parameters.Add("tipo", product.Tipo);
            parameters.Add("grupo", product.Grupo);
            parameters.Add("unidad", product.Unidad);
            parameters.Add("nat", product.Naturaleza);

            string query = "SELECT id_catproductos, cve_prod, descr_prod, lin_prod, tp, gpo, udm, fmcan " +
                "FROM catproductos " +
                "WHERE cve_prod = @cve AND empresa_id = 1";

            var existing = RunQuery(query, parameters, false, conn, tx);

            if (existing.Count > 0)
            {
                var row = existing[0];

                product.Id = Convert.ToInt32(row["id_catproductos"]);

                string updateQuery = "UPDATE catproductos SET descr_prod = @desc, lin_prod = @linea, tp = @tipo, " +
                    "gpo = @grupo, udm = @unidad, fmcan = @nat " +
                    "WHERE id_catproductos = @id";
                parameters.Add("id", product.Id);
                RunUpdate(updateQuery, parameters, false, conn, tx);

                query = "SELECT id_catproductos, cve_prod, descr_prod, lin_prod, tp, gpo, udm, fmcan " +
                "FROM catproductos " +
                "WHERE id_catproductos = @id AND empresa_id = 1";
                row = RunQuery(query, parameters)[0];

                product.Id = Convert.ToInt32(row["id_catproductos"]);
                product.Clave = GetString(row["cve_prod"]);
                product.Descripcion = GetString(row["descr_prod"]);
                product.Linea = GetString(row["lin_prod"]);
                product.Tipo = GetString(row["tp"]);
                product.Grupo = GetString(row["gpo"]);
                product.Unidad = GetString(row["udm"]);
                product.Naturaleza = GetString(row["fmcan"]);
                
                LogErrorHelper.RegistrarLog(
                    "API",
                    "PRODUCTO",
                    $"Producto editado: {JsonConvert.SerializeObject(product)}",
                    nivel: "DEBUG"
                );

                continue; // saltamos el insert
            }

            query = "INSERT INTO catproductos (cve_prod, descr_prod, lin_prod, tp, gpo, udm, fmcan, empresa_id) " +
                "VALUES (@cve, @desc, @linea, @tipo, @grupo, @unidad, @nat, 1) " +
                "RETURNING id_catproductos";

            var newId = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
            product.Id = newId;

            query = "SELECT COUNT(*) FROM catrelacion WHERE prod_kepler = @cve";
            int qty = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

            if (qty == 0)
            {
                query = "INSERT INTO catrelacion (prod_kepler, prod_sat, ud_sat, obj_impto) " +
                   "VALUES(@cve, @prod_sat, @ud_sat, @obj_impto);";

                parameters.Add("prod_sat", product.ProductoSat);
                parameters.Add("ud_sat", product.UnidadSat);
                parameters.Add("obj_impto", product.ObjetoImpuesto);
                RunUpdate(query, parameters, false, conn, tx);
            }
        }

        LogErrorHelper.RegistrarLog(
            "API",
            "PRODUCTO",
            $"Productos creados: {JsonConvert.SerializeObject(productos)}",
            nivel: "DEBUG"
        );

        return productos;
    }

    public ProductApi GetProduct(string cve)
    {
        LogErrorHelper.RegistrarLog(
            "API",
            "PRODUCTO",
            $"Entrando al servicio de consulta de productos: {cve}",
            nivel: "DEBUG"
        );

        ProductApi product = null;
        var parameters = new Dictionary<string, object>();
        parameters.Add("cve", cve);

        string query = "SELECT p.id_catproductos, p.cve_prod, p.descr_prod, p.lin_prod, p.tp, p.gpo, p.fmcan, p.udm, crl.prod_sat, " +
            "   crl.ud_sat, crl.obj_impto, COALESCE(SUM(tp.cantidad), 0) AS cantidad " +
            "FROM catproductos p " +
            "LEFT JOIN tarima_productos tp ON tp.producto_id = p.id_catproductos " +
            "LEFT JOIN cattarimas ct ON ct.id_tarima = tp.tarima_id " +
            "LEFT JOIN catniveles cn ON cn.id_nivel = ct.nivel_id " +
            "LEFT JOIN catcolumnas cl ON cl.id_columna = cn.columna_id " +
            "LEFT JOIN catracks cr ON cr.id_rack = cl.rack_id " +
            "LEFT JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id AND ca.tipo = 'Maquinado' " +
            "LEFT JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id AND cs.empresa_id = 1 " +
            "LEFT JOIN catrelacion crl ON crl.prod_kepler = p.cve_prod " +
            "WHERE p.cve_prod = @cve " +
            "GROUP BY p.id_catproductos, p.cve_prod, p.descr_prod, p.lin_prod, p.tp, p.gpo, p.fmcan, p.udm, crl.prod_sat, crl.ud_sat, " +
            "   crl.obj_impto";

        var prod = RunQuery(query, parameters);

        if (prod.Count > 0)
        {
            product = new ProductApi();
            var row = prod[0];

            product.Id = Convert.ToInt32(row["id_catproductos"]);
            product.Clave = GetString(row["cve_prod"]);
            product.Descripcion = GetString(row["descr_prod"]);
            product.Linea = GetString(row["lin_prod"]);
            product.Tipo = GetString(row["tp"]);
            product.Grupo = GetString(row["gpo"]);
            product.Unidad = GetString(row["udm"]);
            product.ProductoSat = GetString(row["prod_sat"]);
            product.UnidadSat = GetString(row["ud_sat"]);
            product.ObjetoImpuesto = GetString(row["obj_impto"]);
            product.Naturaleza = GetString(row["fmcan"]);
            product.Cantidad = GetDecimal(row["cantidad"], 0);
        }

        return product;
    }
}
