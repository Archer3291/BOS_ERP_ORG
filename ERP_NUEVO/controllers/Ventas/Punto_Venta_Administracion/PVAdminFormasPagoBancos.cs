using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Ventas.Punto_Venta_Administracion
{
    /// <summary>
    /// Administración de las cuentas de banco que puede usar cada forma de pago del
    /// punto de venta (tabla pos_formas_pago_bancos, ver sql/pos_formas_pago_bancos.sql).
    ///
    /// Antes de esto la cuenta bancaria del cobro se elegía a mano contra una lista
    /// hardcodeada en la vista del POS: nada amarraba una tarjeta a la cuenta donde de
    /// verdad cae ese dinero. Aquí se configura, y el POS sólo ofrece lo configurado.
    ///
    /// Las propiedades de los objetos anónimos van en snake_case a propósito: ASP.NET
    /// serializa en camelCase y así el JSON de los anónimos y el de las filas de RunQuery
    /// (que conserva los nombres de columna) se leen igual desde el JavaScript.
    /// </summary>
    public partial class PuntoVentaAdministracionController
    {
        /// <summary>
        /// Formas de pago que el punto de venta sabe cobrar (PuntoDeVenta.js:
        /// paymentMethodsData). Es una lista blanca a propósito: el catálogo SAT completo
        /// tiene ~22 claves que la caja nunca usa y sólo estorbarían en la pantalla.
        /// El nombre es el respaldo si la clave no está en cat_f_pago; el icono es de
        /// presentación.
        /// </summary>
        private static readonly (string Cve, string Nombre, string Icono)[] FormasPagoPos =
        {
            ("01", "Efectivo",           "fa-money-bill"),
            ("02", "Cheque nominativo",  "fa-money-check"),
            ("03", "Transferencia",      "fa-exchange-alt"),
            ("04", "Tarjeta de crédito", "fa-credit-card"),
            ("28", "Tarjeta de débito",  "fa-credit-card"),
            ("99", "Nota de crédito",    "fa-ticket-alt"),
        };

        /// <summary>Alcance "todas las sucursales": se guarda como sucursal_id NULL.</summary>
        private const int SucursalTodas = 0;

        // ════════════════════════════════════════════════════════════════════
        //  Pantalla de administración
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Todo lo que necesita la pantalla: sucursales, catálogo de bancos y, por cada
        /// forma de pago del POS, las cuentas asignadas en el alcance consultado.
        /// </summary>
        [HttpPost]
        public IActionResult ObtenerConfiguracionFormasPago(IFormCollection fc)
        {
            try
            {
                int empresa = HttpContext.Session.GetInt32("Empresa") ?? 0;
                if (empresa <= 0)
                    return Json(new { success = false, message = "La sesión no tiene empresa asignada. Vuelve a iniciar sesión." });

                int sucursal = GetInt(fc["sucursal"].ToString(), SucursalTodas) ?? SucursalTodas;

                var sucursales = RunQuery(
                    "SELECT id_sucursal, cve_sucursal, descripcion FROM catsucursales WHERE empresa_id = @empresa ORDER BY cve_sucursal;",
                    new Dictionary<string, object> { { "empresa", empresa } });

                var bancos = ObtenerCatalogoBancos(empresa);
                var formas = ObtenerFormasPagoPos();
                bool configurable = ExisteTablaFormasPagoBancos();

                // Si el script todavía no se corre en esta base, la pantalla se pinta
                // igual pero avisa, en vez de reventar con un 42P01.
                var asignadas = configurable
                    ? ConsultarAsignaciones(empresa, sucursal)
                    : new List<Dictionary<string, object>>();

                // Al ver una sucursal concreta también se muestra lo que heredaría de
                // "Todas las sucursales", para que se entienda de dónde sale la cuenta
                // que el POS va a ofrecer cuando la forma de pago no tiene nada propio.
                var globales = configurable && sucursal != SucursalTodas
                    ? ConsultarAsignaciones(empresa, SucursalTodas)
                    : new List<Dictionary<string, object>>();

                var resultado = formas.Select(f => new
                {
                    id_f_pago = f.IdFPago,
                    cve_sat = f.CveSat,
                    descripcion = f.Descripcion,
                    icono = f.Icono,
                    cuentas = asignadas.Where(a => (GetInt(a["f_pago_id"], 0) ?? 0) == f.IdFPago).ToList(),
                    heredadas = globales.Where(a => (GetInt(a["f_pago_id"], 0) ?? 0) == f.IdFPago).ToList()
                });

                return Json(new
                {
                    success = true,
                    configurable,
                    message = configurable
                        ? null
                        : "Falta ejecutar sql/pos_formas_pago_bancos.sql en esta base de datos.",
                    sucursal,
                    sucursales,
                    bancos,
                    formas = resultado
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PVAdminFormasPagoBancos/ObtenerConfiguracionFormasPago");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Reemplaza el juego completo de cuentas de una forma de pago en un alcance.
        /// Se hace por reemplazo y no por altas/bajas sueltas porque la pantalla manda
        /// siempre la lista final: así no quedan filas huérfanas si el usuario quita una
        /// cuenta y agrega otra en la misma edición.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult GuardarCuentasFormaPago(IFormCollection fc)
        {
            try
            {
                int empresa = HttpContext.Session.GetInt32("Empresa") ?? 0;
                if (empresa <= 0)
                    return Json(new { icon = "error", title = "Sesión sin empresa", html = "Vuelve a iniciar sesión." });

                if (!ExisteTablaFormasPagoBancos())
                    return Json(new { icon = "error", title = "Falta correr el script", html = "Ejecuta sql/pos_formas_pago_bancos.sql antes de configurar las cuentas." });

                int fPagoId = GetInt(fc["fPagoId"].ToString(), 0) ?? 0;
                if (fPagoId <= 0)
                    return Json(new { icon = "error", title = "Forma de pago inválida" });

                int sucursal = GetInt(fc["sucursal"].ToString(), SucursalTodas) ?? SucursalTodas;

                var bancos = fc["bancos"]
                    .Select(b => GetInt(b, 0) ?? 0)
                    .Where(b => b > 0)
                    .Distinct()
                    .ToList();

                int predeterminada = GetInt(fc["predeterminada"].ToString(), 0) ?? 0;

                // Si la predeterminada no está entre las cuentas enviadas, se toma la
                // primera: dejarla apuntando a una cuenta que ya no está asignada haría
                // que el POS no preseleccionara nada.
                if (bancos.Count > 0 && !bancos.Contains(predeterminada))
                    predeterminada = bancos[0];

                using (var conn = AbrirConexion())
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        // NULLIF(@sucursal, 0) traduce el alcance "todas" al NULL que
                        // guarda la columna, sin mandar un DBNull sin tipo que Npgsql no
                        // sabría inferir.
                        RunUpdate(@"
                            DELETE FROM pos_formas_pago_bancos
                            WHERE empresa_id = @empresa
                              AND COALESCE(sucursal_id, 0) = @sucursal
                              AND f_pago_id = @f_pago;",
                            new Dictionary<string, object>
                            {
                                { "empresa",  empresa },
                                { "sucursal", sucursal },
                                { "f_pago",   fPagoId }
                            },
                            false, conn, tx);

                        foreach (int bancoId in bancos)
                        {
                            RunUpdate(@"
                                INSERT INTO pos_formas_pago_bancos
                                    (empresa_id, sucursal_id, f_pago_id, banco_id, predeterminada, creada_por)
                                VALUES
                                    (@empresa, NULLIF(@sucursal, 0), @f_pago, @banco, @predeterminada, @usuario);",
                                new Dictionary<string, object>
                                {
                                    { "empresa",        empresa },
                                    { "sucursal",       sucursal },
                                    { "f_pago",         fPagoId },
                                    { "banco",          bancoId },
                                    { "predeterminada", bancoId == predeterminada },
                                    { "usuario",        GetUserId(User.Identity.Name) }
                                },
                                false, conn, tx);
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }

                return Json(new
                {
                    icon = "success",
                    title = bancos.Count == 0
                        ? "Configuración eliminada"
                        : $"{bancos.Count} cuenta(s) asignada(s)"
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PVAdminFormasPagoBancos/GuardarCuentasFormaPago");
                return Json(new { icon = "error", title = "No se pudo guardar la configuración", html = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Lectura desde el punto de venta
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cuentas que el POS puede ofrecer, ya resueltas para la sucursal de la sesión.
        /// La consumen el modal de cobro, el modal de facturación y el cierre de caja.
        ///
        /// Devuelve también <c>todas</c> (el catálogo completo) porque una forma de pago
        /// sin configurar cae al catálogo: se prefiere que la caja siga facturando a
        /// dejarla bloqueada esperando a que alguien configure el módulo.
        /// </summary>
        [HttpPost]
        public IActionResult CuentasBancoFormasPago()
        {
            try
            {
                int empresa = HttpContext.Session.GetInt32("Empresa") ?? 0;
                int sucursal = HttpContext.Session.GetInt32("Sucursal") ?? 0;

                if (empresa <= 0)
                    return Json(new { success = false, message = "La sesión no tiene empresa asignada." });

                var todas = ObtenerCatalogoBancos(empresa);
                bool configurado = ExisteTablaFormasPagoBancos();

                var formas = ObtenerFormasPagoPos().Select(f => new
                {
                    id_f_pago = f.IdFPago,
                    cve_sat = f.CveSat,
                    descripcion = f.Descripcion,
                    icono = f.Icono,
                    cuentas = configurado
                        ? ResolverCuentasDeFormaPago(empresa, sucursal, f.IdFPago)
                        : new List<Dictionary<string, object>>()
                }).ToList();

                return Json(new
                {
                    success = true,
                    configurado,
                    sucursal,
                    formas,
                    todas
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PVAdminFormasPagoBancos/CuentasBancoFormasPago");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cuentas configuradas para una forma de pago en la sucursal indicada. Una fila
        /// con la sucursal concreta gana sobre la de "todas las sucursales": la global es
        /// el valor por omisión, no un agregado.
        /// </summary>
        private List<Dictionary<string, object>> ResolverCuentasDeFormaPago(
            int empresa, int sucursal, int fPagoId,
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            var filas = RunQuery(@"
                SELECT b.banco_id,
                       b.sucursal_id,
                       b.predeterminada,
                       cb.nombre,
                       cb.cuenta_contable,
                       cb.cuenta_banco,
                       (cf.id_cuenta_contable IS NOT NULL) AS contabilizable
                FROM pos_formas_pago_bancos b
                INNER JOIN catbancos cb ON cb.id_catbanco = b.banco_id
                LEFT JOIN cuentas_finanzas cf
                       ON cf.codigo = cb.cuenta_contable AND cf.empresa_id = b.empresa_id
                WHERE b.empresa_id = @empresa
                  AND b.f_pago_id  = @f_pago
                  AND (b.sucursal_id = @sucursal OR b.sucursal_id IS NULL)
                ORDER BY b.predeterminada DESC, cb.nombre;",
                new Dictionary<string, object>
                {
                    { "empresa",  empresa },
                    { "sucursal", sucursal },
                    { "f_pago",   fPagoId }
                },
                false, conn, tx);

            var propias = filas.Where(EsDeSucursal).ToList();

            return propias.Count > 0
                ? propias
                : filas.Where(f => !EsDeSucursal(f)).ToList();
        }

        private static bool EsDeSucursal(Dictionary<string, object> fila)
            => fila["sucursal_id"] != null && fila["sucursal_id"] != DBNull.Value;

        /// <summary>Asignaciones guardadas para un alcance concreto (0 = todas las sucursales).</summary>
        private List<Dictionary<string, object>> ConsultarAsignaciones(int empresa, int sucursal)
        {
            return RunQuery(@"
                SELECT b.id_pos_forma_pago_banco AS id,
                       b.f_pago_id,
                       b.banco_id,
                       b.predeterminada,
                       cb.nombre,
                       cb.cuenta_contable,
                       cb.cuenta_banco,
                       (cf.id_cuenta_contable IS NOT NULL) AS contabilizable
                FROM pos_formas_pago_bancos b
                INNER JOIN catbancos cb ON cb.id_catbanco = b.banco_id
                LEFT JOIN cuentas_finanzas cf
                       ON cf.codigo = cb.cuenta_contable AND cf.empresa_id = b.empresa_id
                WHERE b.empresa_id = @empresa
                  AND COALESCE(b.sucursal_id, 0) = @sucursal
                ORDER BY b.predeterminada DESC, cb.nombre;",
                new Dictionary<string, object>
                {
                    { "empresa",  empresa },
                    { "sucursal", sucursal }
                });
        }

        /// <summary>
        /// Catálogo de cuentas de banco. <c>contabilizable</c> marca las que tienen su
        /// cuenta en cuentas_finanzas: sin ella la póliza del cobro truena al facturar
        /// (PolizaConfigFactory.ObtenerCuentaBanco), así que conviene verlo al configurar.
        /// </summary>
        private List<Dictionary<string, object>> ObtenerCatalogoBancos(int empresa)
        {
            return RunQuery(@"
                SELECT cb.id_catbanco AS banco_id,
                       cb.nombre,
                       cb.cuenta_contable,
                       cb.cuenta_banco,
                       cb.tipo,
                       (cf.id_cuenta_contable IS NOT NULL) AS contabilizable
                FROM catbancos cb
                LEFT JOIN cuentas_finanzas cf
                       ON cf.codigo = cb.cuenta_contable AND cf.empresa_id = @empresa
                ORDER BY cb.cuenta_contable;",
                new Dictionary<string, object> { { "empresa", empresa } });
        }

        /// <summary>Las formas de pago del POS, resueltas contra cat_f_pago por clave SAT.</summary>
        private List<(int IdFPago, string CveSat, string Descripcion, string Icono)> ObtenerFormasPagoPos()
        {
            var catalogo = RunQuery(
                "SELECT id_f_pago, cve_sat, descripcion FROM cat_f_pago WHERE cve_sat::text = ANY(@claves);",
                new Dictionary<string, object> { { "claves", FormasPagoPos.Select(f => f.Cve).ToArray() } });

            var formas = new List<(int IdFPago, string CveSat, string Descripcion, string Icono)>();

            foreach (var (cve, nombre, icono) in FormasPagoPos)
            {
                var fila = catalogo.FirstOrDefault(c => c["cve_sat"]?.ToString()?.Trim() == cve);
                if (fila == null) continue;   // clave que esta base no tiene dada de alta

                formas.Add((
                    GetInt(fila["id_f_pago"], 0) ?? 0,
                    cve,
                    fila["descripcion"]?.ToString() ?? nombre,
                    icono));
            }

            return formas;
        }

        /// <summary>
        /// La tabla es opcional mientras no se corra el script: si falta, la pantalla
        /// avisa y el POS trabaja contra el catálogo completo, como lo hacía antes.
        /// </summary>
        private bool ExisteTablaFormasPagoBancos()
        {
            try
            {
                object r = RunScalar(@"
                    SELECT COUNT(*)
                    FROM   information_schema.tables
                    WHERE  table_name = 'pos_formas_pago_bancos';",
                    new Dictionary<string, object>());

                return r != null && Convert.ToInt32(r) > 0;
            }
            catch { return false; }
        }
    }
}
