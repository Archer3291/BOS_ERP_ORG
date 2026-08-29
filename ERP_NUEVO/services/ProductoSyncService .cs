using Npgsql;
using BOS_ERP.Controllers;
using BOS_ERP.Helpers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Services
{
    public class ProductoSyncService : Utilities
    {
        public JsonResult SincronizarProductos()
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            int? totalInsertados = 0;
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        try
                        {
                            var empresas = new List<(int EmpresaId, string DatabaseName)>
                            {
                                (Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")), GetEmpresaName(User.Identity.Name)),
                            };

                            foreach (var emp in empresas)
                            {
                                var connString = utils._configuration.GetConnectionString(emp.DatabaseName);
                                totalInsertados += SincronizarEmpresa(emp.EmpresaId, connString, new List<string>(), conn, tx);
                            }
                            tx.Commit();
                        }

                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                        return Json(new
                        {
                            success = true,
                            message = $"Sincronización completada. Total insertados: {totalInsertados}"
                        });
                    }
                    catch (Exception ex)
                    {
                        return Json(new
                        {
                            success = false,
                            message = ex.Message
                        });
                    }
                }
            }
        }

        public int? SincronizarEmpresa(int? empresaId, string connectionString, List<string> codigos, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            BuildDblinkConnection builder = new BuildDblinkConnection();
            var safeConn = builder.BuildDblink(connectionString);

            var parametros = new Dictionary<string, object>();
            parametros.Add("empresaId", empresaId);

            var whereClause = "";

            if (codigos != null && codigos.Any())
            {
                var codigosSql = string.Join(",",
                    codigos.Select(x => $"''{x.Replace("'", "''")}''"));

                whereClause = $"WHERE prod.c1 IN ({codigosSql})";
            }

            string sql = $@"
                WITH insertados AS (
                    INSERT INTO catproductos (
                        cve_prod, descr_prod, lin_prod, tp, gpo, cod_prov, cbr, cve_prov_ppal,
                        cve_secundaria, udm, ud_alt, conv_ud, pv1, pv2, pv3, pv4,
                        iva_prod, impto_by_s, cve_ccy_pv, sn, ctr_lot, pv_prov, dto_prov,
                        cve_prod_sust, cve_prod_equiv, imp, fr_ar, dto_ad_val, cve_pais_orig,
                        cve_ccy_comp, descr_prod_idioma, niv_inv_min, cant_min_oc, niv_inv_max,
                        med_talla_prod, coment_prod1, coment_prod2, lt_comp, ub, nro_cpo,
                        cve_dib, cve_rt_mfg, fmcan, tp_mov, stat, tpo_entrega, min_estn2,
                        max_estn2, pto_ro_estn2, ctmc, lt_transf, prod_modula, n_img,
                        dto_vol1, dto_vol2, dto_vol3, dto_vol4, pv5, pv6, pv7, pv8,
                        uti1, uti2, dto_x_vol, tp_cto, empresa_id, fecha_actualizado, peso_prod
                    )
                    SELECT 
                        C1, C2, COALESCE(C3, '0171')::text, COALESCE(C4, '09')::text, COALESCE(C5, '032')::text, C6, C7, C8,
                        c9, c11, c12, c13::double precision, c14::numeric, c15::numeric, c16::numeric,
                        c17::numeric, c18::double precision, c19::double precision, c20, c21, c22, c23::numeric,
                        c24::double precision, c25, c26, c27, c28, c29::double precision, c30,
                        c31, c32, c33::numeric, c34::numeric, c35::numeric, c36::numeric,
                        c39, c40, c42::double precision, c43, c44::numeric, c45, c46,
                        C48, C49, C51, C52::numeric, C53::numeric, C54::numeric, C55::numeric,
                        C67::numeric, C68::numeric, 
                        CASE
                            WHEN NULLIF(BTRIM(C69), '') IS NULL THEN FALSE
                            WHEN LOWER(BTRIM(C69)) IN ('1', 'true', 't', 'yes', 'y', 'si', 'sí') THEN TRUE
                            WHEN LOWER(BTRIM(C69)) IN ('0', 'false', 'f', 'no', 'n') THEN FALSE
                            ELSE FALSE
                        END,
                        C70, C71, C72, C73,
                        C74, C81::numeric, C82::numeric, C83::numeric, C84::numeric,
                        c91::numeric, c92::numeric, c99, c100,
                        @empresaId, now(), c115::numeric
                    FROM public.dblink(
                        '{safeConn}',
                        'SELECT C1, C2, C3, C4, C5, C6, C7, C8,
                                c9, c11, c12, c13, c14, c15, c16,
                                c17, c18, c19, c20, c21, c22, c23,
                                c24, c25, c26, c27, c28, c29, c30,
                                c31, c32, c33, c34, c35, c36,
                                c39, c40, c42, c43, c44, c45, c46,
                                C48, C49, C51, C52, C53, C54, C55,
                                C67, C68, C69, C70, C71, C72, C73,
                                C74, C81, C82, C83, C84,
                                c91, c92, c99, c100, c115
                        FROM kdii prod 
                        {whereClause}'
                    ) AS t(
                        C1 text, C2 text, C3 text, C4 text, C5 text, C6 text, C7 text, C8 text,
                        c9 text, c11 text, c12 text, c13 text, c14 text, c15 text, c16 text,
                        c17 text, c18 text, c19 text, c20 text, c21 text, c22 text, c23 text,
                        c24 text, c25 text, c26 text, c27 text, c28 text, c29 text, c30 text,
                        c31 text, c32 text, c33 text, c34 text, c35 text, c36 text,
                        c39 text, c40 text, c42 text, c43 text, c44 text, c45 text, c46 text,
                        C48 text, C49 text, C51 text, C52 text, C53 text, C54 text, C55 text,
                        C67 text, C68 text, C69 text, C70 text, C71 text, C72 text, C73 text,
                        C74 text, C81 text, C82 text, C83 text, C84 text,
                        c91 text, c92 text, c99 text, c100 text, c115 text
                    )
                    ON CONFLICT (cve_prod, empresa_id)
                    DO UPDATE SET
                        descr_prod = EXCLUDED.descr_prod,
                        lin_prod = EXCLUDED.lin_prod,
                        tp = EXCLUDED.tp,
                        gpo = EXCLUDED.gpo,
                        cod_prov = EXCLUDED.cod_prov,
                        cbr = EXCLUDED.cbr,
                        cve_prov_ppal = EXCLUDED.cve_prov_ppal,
                        cve_secundaria = EXCLUDED.cve_secundaria,
                        udm = EXCLUDED.udm,
                        ud_alt = EXCLUDED.ud_alt,
                        conv_ud = EXCLUDED.conv_ud,
                        pv1 = EXCLUDED.pv1,
                        pv2 = EXCLUDED.pv2,
                        pv3 = EXCLUDED.pv3,
                        pv4 = EXCLUDED.pv4,
                        iva_prod = EXCLUDED.iva_prod,
                        impto_by_s = EXCLUDED.impto_by_s,
                        cve_ccy_pv = EXCLUDED.cve_ccy_pv,
                        sn = EXCLUDED.sn,
                        ctr_lot = EXCLUDED.ctr_lot,
                        pv_prov = EXCLUDED.pv_prov,
                        dto_prov = EXCLUDED.dto_prov,
                        cve_prod_sust = EXCLUDED.cve_prod_sust,
                        cve_prod_equiv = EXCLUDED.cve_prod_equiv,
                        imp = EXCLUDED.imp,
                        fr_ar = EXCLUDED.fr_ar,
                        dto_ad_val = EXCLUDED.dto_ad_val,
                        cve_pais_orig = EXCLUDED.cve_pais_orig,
                        cve_ccy_comp = EXCLUDED.cve_ccy_comp,
                        descr_prod_idioma = EXCLUDED.descr_prod_idioma,
                        niv_inv_min = EXCLUDED.niv_inv_min,
                        cant_min_oc = EXCLUDED.cant_min_oc,
                        niv_inv_max = EXCLUDED.niv_inv_max,
                        med_talla_prod = EXCLUDED.med_talla_prod,
                        coment_prod1 = EXCLUDED.coment_prod1,
                        coment_prod2 = EXCLUDED.coment_prod2,
                        lt_comp = EXCLUDED.lt_comp,
                        ub = EXCLUDED.ub,
                        nro_cpo = EXCLUDED.nro_cpo,
                        cve_dib = EXCLUDED.cve_dib,
                        cve_rt_mfg = EXCLUDED.cve_rt_mfg,
                        fmcan = EXCLUDED.fmcan,
                        tp_mov = EXCLUDED.tp_mov,
                        stat = EXCLUDED.stat,
                        tpo_entrega = EXCLUDED.tpo_entrega,
                        min_estn2 = EXCLUDED.min_estn2,
                        max_estn2 = EXCLUDED.max_estn2,
                        pto_ro_estn2 = EXCLUDED.pto_ro_estn2,
                        ctmc = EXCLUDED.ctmc,
                        lt_transf = EXCLUDED.lt_transf,
                        prod_modula = EXCLUDED.prod_modula,
                        n_img = EXCLUDED.n_img,
                        dto_vol1 = EXCLUDED.dto_vol1,
                        dto_vol2 = EXCLUDED.dto_vol2,
                        dto_vol3 = EXCLUDED.dto_vol3,
                        dto_vol4 = EXCLUDED.dto_vol4,
                        pv5 = EXCLUDED.pv5,
                        pv6 = EXCLUDED.pv6,
                        pv7 = EXCLUDED.pv7,
                        pv8 = EXCLUDED.pv8,
                        uti1 = EXCLUDED.uti1,
                        uti2 = EXCLUDED.uti2,
                        dto_x_vol = EXCLUDED.dto_x_vol,
                        tp_cto = EXCLUDED.tp_cto,
                        fecha_actualizado = NOW(),
                        peso_prod = EXCLUDED.peso_prod
                    RETURNING cve_prod
                ),
                log_insert AS (
                    INSERT INTO log_sincronizacion_productos (
                        empresa_id,
                        total_insertados,
                        ids_insertados
                    )
                    SELECT
                        @empresaId,
                        COUNT(i.cve_prod),
                        COALESCE(jsonb_agg(i.cve_prod), '[]'::jsonb)
                    FROM (SELECT 1) base
                    LEFT JOIN insertados i ON TRUE
                    RETURNING total_insertados
                )
                SELECT total_insertados FROM log_insert;
            ";

            int? insertados = GetInt(RunScalar(sql, parametros, false, conn, tx, "ERP_SRS"), 0);

            sql = $@"
                WITH insertados AS (
                    INSERT INTO catrelacion (
                        prod_kepler, prod_sat, ud_sat, iva_ex, obj_impto, fecha_actualizado
                    )
                    SELECT C1, C2, C3, C4, C6, now()
                    FROM public.dblink(
                        '{safeConn}',
                        'SELECT C1, C2, C3, C4, C6
                        FROM kdfe33relprd'
                    ) AS t(
                        C1 text, C2 text, C3 text, C4 text, C6 text
                    )
                    ON CONFLICT (prod_kepler) DO NOTHING
                    RETURNING prod_kepler
                ),
                log_insert AS (
                    INSERT INTO log_sincronizacion_productos (
                        empresa_id,
                        total_insertados,
                        ids_insertados
                    )
                    SELECT
                        @empresaId,
                        COUNT(i.prod_kepler),
                        COALESCE(jsonb_agg(i.prod_kepler), '[]'::jsonb)
                    FROM (SELECT 1) base
                    LEFT JOIN insertados i ON TRUE
                    RETURNING total_insertados
                )
                SELECT total_insertados FROM log_insert;
            ";

            RunUpdate(sql, parametros, false, conn, tx, "ERP_SRS");

            sql = $@"
                INSERT INTO frac_arancelarias (
                    cve_prod, ""desc"", peso, status, frac
                )
                SELECT 
                    t.clave,
                    t.descripcion,
                    NULLIF(t.peso, '')::numeric,
                    t.status,
                    t.fraccion
                FROM public.dblink(
                    '{safeConn}',
                    'SELECT 
                        prod.c1 AS clave,
                        prod.c2 AS descripcion,
                        prod.c115 AS peso,
                        prod.c51 AS status,
                        kc.c2 AS fraccion
                    FROM kdfe33cefracaraprod kc
                    INNER JOIN kdii prod 
                        ON prod.c1 = kc.c1
                    {whereClause}'
                ) AS t(
                    clave text,
                    descripcion text,
                    peso text,
                    status text,
                    fraccion text
                )
                ON CONFLICT (cve_prod) 
                DO UPDATE SET
                    ""desc"" = EXCLUDED.""desc"",
                    peso = EXCLUDED.peso,
                    status = EXCLUDED.status,
                    frac = EXCLUDED.frac
            ";

            RunUpdate(sql, parametros, false, conn, tx, "ERP_SRS");

            return insertados;
        }
    }
}
