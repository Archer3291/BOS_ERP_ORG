using Npgsql;
using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;

public class ImpuestoService : Utilities
{
    public decimal? RecalcularImpuestos(int idEncabezado, decimal? subtotal, List<Impuesto> impuestos, string prov, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var parameters = new Dictionary<string, object>();

        parameters.Add("id_encabezado", idEncabezado);

        string query = "SELECT COUNT(*) FROM imp_oc WHERE encabezado_id = @id_encabezado";
        int qty = Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));

        decimal? importeFinal = 0m;

        if (qty > 0 || (impuestos?.Count ?? 0) > 0)
        {
            query = "DELETE FROM imp_oc WHERE encabezado_id = @id_encabezado";
            RunUpdate(query, parameters, false, conn, tx);

            if (impuestos == null || impuestos.Count == 0)
                return 0m;

            query = @"INSERT INTO imp_oc
                (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom)
                VALUES (@id_encabezado, @impuesto_id, @subtotal, @importe, @orden, @tasa, @prov)";

            int orden = 0;

            foreach (var imp in impuestos)
            {
                orden++;

                parameters = new Dictionary<string, object>
                {
                    { "id_encabezado", idEncabezado },
                    { "impuesto_id", imp.IdImpuesto },
                    { "subtotal", subtotal },
                    { "importe", subtotal * (imp.ImpVariable / 100m) },
                    { "orden", orden },
                    { "tasa", imp.ImpVariable },
                    { "prov", prov }
                };

                RunUpdate(query, parameters, false, conn, tx);

                importeFinal += imp.EsRetencion
                    ? -(subtotal * (imp.ImpVariable / 100m))
                    : (subtotal * (imp.ImpVariable / 100m));
            }
        }

        return importeFinal;
    }
}
