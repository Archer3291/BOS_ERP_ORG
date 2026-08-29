using Npgsql;
using BOS_ERP.Controllers;
using BOS_ERP.Helpers;
using System.Configuration;
using System.Data;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Services
{
    public class ClientSyncService : Utilities
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public ClientSyncService(IHttpContextAccessor httpContextAccessor)
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
                                totalInsertados += SincronizarCliente(emp.EmpresaId, connString, new List<string>(), conn, tx);
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

        public int? SincronizarCliente(int? empresaId, string connectionString, List<string> codigos, NpgsqlConnection conn, NpgsqlTransaction tx)
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

            var whereClause = "";

            if (codigos != null && codigos.Any())
            {
                var codigosLimpios = codigos
                    .Select(x =>
                    {
                        try
                        {
                            var arr = System.Text.Json.JsonSerializer.Deserialize<List<string>>(x);

                            if (arr != null && arr.Any())
                                return arr[0];

                            return x;
                        }
                        catch
                        {
                            return x;
                        }
                    })
                    .ToList();

                var codigosSql = string.Join(",",
                    codigosLimpios.Select(x => $"''{x.Replace("'", "''")}''"));

                whereClause = $"WHERE c2 IN ({codigosSql})";
            }

            string sql = $@"
                WITH insertados AS (
                    INSERT INTO catclientes (
                        cve_cli, n_cli, dir, col, pob, rfc, cve_vdr, cve_gpo, cve_zona, lim_crd, pl_crd, dto, dto2, coment1, coment1_, cp, com_por, curp_cli, n_cml, idf, cve_pais, 
                        cve_est, mpio, cod_lada, bco_cli1, cta_bco_cli1, bco_cli2, cta_bco_cli2, bco_cli3, cta_bco_cli3, stat_cli, fch_in, cod_ant, empresa_id, es_internacional, fecha_actualizacion, creado_por
                    )
                    SELECT 
                        c2, c3, c4, c5, c6, c10, c12, c13, c14, c15::numeric, c16::numeric,
	                    c17, c18, c24, c25, c27, c28::numeric, c30, c31, c32, c40, c41,
	                    c42, c43, c50, c51, c52, c53, c54, c55, c57, c58::timestamp, c60, @empresaId, (c2 LIKE '02%') AS es_internacional, now(), @usuario
                    FROM public.dblink(
                        '{safeConn}',
                        'SELECT c2, c3, c4, c5, c6, c10, c12, c13, c14, c15, c16,
	                        c17, c18, c24, c25, c27, c28, c30, c31, c32, c40, c41,
	                        c42, c43, c50, c51, c52, c53, c54, c55, c57, c58, c60
                        FROM kdud 
                        {whereClause}'
                    ) AS t(
                        c2 text, c3 text, c4 text, c5 text, c6 text, c10 text, c12 text, c13 text, c14 text, c15 text, c16 text,
	                    c17 text, c18 text, c24 text, c25 text, c27 text, c28 text, c30 text, c31 text, c32 text, c40 text, c41 text,
	                    c42 text, c43 text, c50 text, c51 text, c52 text, c53 text, c54 text, c55 text, c57 text, c58 text, c60 text
                    )
                    ON CONFLICT (cve_cli, empresa_id) DO NOTHING
                    RETURNING cve_cli
                ),
                log_insert AS (
                    INSERT INTO log_sincronizacion_clientes (
                        empresa_id,
                        total_insertados,
                        ids_insertados
                    )
                    SELECT
                        @empresaId,
                        COUNT(i.cve_cli),
                        COALESCE(jsonb_agg(i.cve_cli), '[]'::jsonb)
                    FROM (SELECT 1) base
                    LEFT JOIN insertados i ON TRUE
                    RETURNING total_insertados
                )
                SELECT total_insertados FROM log_insert;
            ";

            int? insertados = GetInt(RunScalar(sql, parametros, false, conn, tx, "ERP_SRS"), 0);

            sql = $@"
                INSERT INTO correos_cliente (cliente_id, correo)
                SELECT
                    c.id_cliente,
                    lower(trim(correo)) AS correo_valido
                FROM public.dblink(
                    '{safeConn}',
                    'SELECT c2, c11
                    FROM kdud'
                ) AS k(
                    c2 text,
                    c11 text
                )
                INNER JOIN catclientes c 
                    ON c.cve_cli = k.c2
                CROSS JOIN LATERAL regexp_split_to_table(k.c11, '[,;]+') AS correo
                WHERE trim(correo) ~* '^[A-Z0-9_%+-]+(\.[A-Z0-9_%+-]+)*@[A-Z0-9.-]+\.[A-Z]{{2,}}$'
                AND c.empresa_id = @empresaId
                ON CONFLICT DO NOTHING;
            ";

            RunUpdate(sql, parametros, false, conn, tx, "ERP_SRS");

            sql = $@"
                INSERT INTO telefonos_cliente (cliente_id, telefono)
                SELECT
                    c.id_cliente,
                    regexp_replace(k.c8, '[^0-9+]', '', 'g') AS telefono_limpio
                FROM public.dblink(
                    '{safeConn}',
                    'SELECT c2, c8
                    FROM kdud'
                ) AS k(
                    c2 text,
                    c8 text
                )
                INNER JOIN catclientes c 
                    ON c.cve_cli = k.c2
                WHERE regexp_replace(k.c8, '[^0-9+]', '', 'g')
                    ~ '^\+?[0-9]{{8,15}}$'
                AND c.empresa_id = @empresaId
                ON CONFLICT DO NOTHING;
                ";

            RunUpdate(sql, parametros, false, conn, tx, "ERP_SRS");

            return insertados;
        }
    }
}