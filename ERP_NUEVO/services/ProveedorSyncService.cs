using Npgsql;
using BOS_ERP.Controllers;
using BOS_ERP.Helpers;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

using System.Data;

namespace BOS_ERP.Services
{
    public class ProveedorSyncService : Utilities
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public ProveedorSyncService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public JsonResult ObtenerConexion()
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
                                totalInsertados += SincronizarProveedor(emp.EmpresaId, connString, new List<string>(), conn, tx);
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

        public int? SincronizarProveedor(int? empresaId, string connectionString, List<string> claves, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            BuildDblinkConnection builder = new BuildDblinkConnection();
            var safeConn = builder.BuildDblink(connectionString);

            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext == null)
                throw new InvalidOperationException("No existe un contexto HTTP activo.");

            var identity = httpContext.User?.Identity;

            if (identity?.IsAuthenticated != true)
                throw new InvalidOperationException("El usuario no está autenticado.");

            var userName = identity.Name;

            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("La identidad autenticada no contiene un nombre de usuario.");

            int? usuarioId = GetUserId(userName);

            if (!usuarioId.HasValue)
                throw new InvalidOperationException($"No se encontró el usuario '{userName}' en la base de datos.");

            var parametros = new Dictionary<string, object>();
            parametros.Add("empresaId", empresaId);
            parametros.Add("usuario", usuarioId);

            string whereClause = "";

            if (claves != null && claves.Any())
            {
                if (claves.Count == 1 &&
                    !string.IsNullOrWhiteSpace(claves[0]) &&
                    claves[0].TrimStart().StartsWith("["))
                {
                    claves = System.Text.Json.JsonSerializer
                        .Deserialize<List<string>>(claves[0]) ?? new List<string>();
                }

                var clavesSql = string.Join(",",
                    claves.Select(x => $"''{x.Replace("'", "''")}''"));

                whereClause = $"WHERE c2 IN ({clavesSql})";
            }

            string sql = $@"
                WITH insertados AS (
                    INSERT INTO catproveedores (
                        suc, cve_prov, n_prov, dir, col, pob, rfc, cve_agcy_cpr, cve_gpo, cve_tp_prov, lim_crd, pl_crd, dto, coment1, coment2, cp, tp_prov, opr_prov,
                        nacl, cve_pais_prov, n_cml_prov, cve_pais, cve_edo, cve_mpio, lada, id_fisc, bco1_prov, cta_bco1_prov, bco2_prov, cta_bco2_prov, bco3_prov,
                        cta_bco3_prov, uso_cfdi, fp, mdp, id_empresa, creado_por
                    )
                    SELECT
                        c1, c2, c3, c4, c5, c6, c10, c12, c13, c14, NULLIF(c15, '')::numeric, NULLIF(c16, '')::numeric, c17, c24, c25, c27,
                        c34, c35, c36, c37, c39, c40, c41, c42, c43, c46, c50, c51, c52, c53, c54, c55, c69, c70, c71, @empresaId, @usuario
                    FROM public.dblink(
                        '{safeConn}',
                        '
                        SELECT
                            c1,c2,c3,c4,c5,c6,c10,c12,c13,c14,c15,
                            c16,c17,c24,c25,c27,c34,c35,c36,c37,
                            c39,c40,c41,c42,c43,c46,c50,c51,c52,
                            c53,c54,c55,c69,c70,c71
                        FROM kdxd
                        {whereClause}
                        '
                    ) AS t(
                        c1 text, c2 text, c3 text, c4 text, c5 text, c6 text, c10 text, c12 text, c13 text, c14 text, c15 text, c16 text, c17 text, c24 text, c25 text, c27 text, c34 text, 
                        c35 text, c36 text, c37 text, c39 text, c40 text, c41 text, c42 text, c43 text, c46 text, c50 text, c51 text, c52 text, c53 text, c54 text, c55 text,
                        c69 text, c70 text, c71 text
                    )
                    ON CONFLICT DO NOTHING
                    RETURNING id_prov, cve_prov, n_prov
                ),
                contactos AS (
                    INSERT INTO contactos_prov (nombre, prov_id)
                    SELECT i.n_prov, i.id_prov
                    FROM insertados i
                    WHERE trim(coalesce(i.n_prov, '')) <> ''
                    RETURNING id_contactos_prov, prov_id
                )
                SELECT count(*)
                FROM insertados;
            ";

            int? insertados =
                GetInt(RunScalar(sql, parametros, false, conn, tx, "ERP_SRS"), 0);

            // CORREOS
            sql = $@"
                INSERT INTO contactos_prov_correos (
                    contactos_prov_id,
                    correo
                )
                SELECT
                    cp.id_contactos_prov,
                    lower(trim(c.correo))
                FROM public.dblink(
                    '{safeConn}',
                    '
                    SELECT c2, c11
                    FROM kdxd
                    '
                ) AS k(
                    c2 text,
                    c11 text
                )
                INNER JOIN catproveedores p
                    ON p.cve_prov = k.c2
                    AND p.id_empresa = @empresaId
                INNER JOIN contactos_prov cp
                    ON cp.prov_id = p.id_prov
                CROSS JOIN LATERAL regexp_split_to_table(
                    coalesce(k.c11, ''),
                    '[,;]+'
                ) AS c(correo)
                WHERE trim(c.correo) ~*
                    '^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{{2,}}$'
                ON CONFLICT DO NOTHING;
            ";

            RunUpdate(sql, parametros, false, conn, tx, "ERP_SRS");

            // TELEFONOS
            sql = $@"
                INSERT INTO contactos_prov_tel ( contacto_prov_id, tel)
                SELECT cp.id_contactos_prov, regexp_replace(k.c7, '[^0-9+]', '', 'g')
                FROM public.dblink(
                    '{safeConn}',
                    '
                    SELECT c2, c7
                    FROM kdxd
                    '
                ) AS k(
                    c2 text,
                    c7 text
                )

                INNER JOIN catproveedores p ON p.cve_prov = k.c2 AND p.id_empresa = @empresaId

                INNER JOIN contactos_prov cp ON cp.prov_id = p.id_prov

                WHERE regexp_replace(k.c7, '[^0-9+]', '', 'g') ~ '^\+?[0-9]{{8,15}}$'

                ON CONFLICT DO NOTHING;
            ";

            RunUpdate(sql, parametros, false, conn, tx, "ERP_SRS");

            return insertados;
        }
    }
}