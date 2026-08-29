using BOS_ERP.Hubs;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.CuentasContables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Npgsql;
using QRCoder;
using System.Collections.Specialized;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BOS_ERP.Controllers
{
    public partial class Utilities : Controller
    {
        private IConfiguration _configuracionInterna;

        /// <summary>
        /// Configuración de la aplicación.
        /// El constructor sin parámetros —el que usa el contenedor de dependencias al
        /// instanciar los controladores— dejaba este campo en null, de modo que cualquier
        /// controlador que leyera appsettings directamente (datos del emisor, rutas de
        /// facturación) reventaba con NullReference; sólo funcionaba por el rodeo de
        /// crear un <c>new Utilities(true)</c>. Ahora se construye a demanda y los dos
        /// caminos se comportan igual. Si alguien la inyecta, la asignación gana.
        /// </summary>
        public IConfiguration _configuration
        {
            get => _configuracionInterna ??= ConstruirConfiguracion();
            set => _configuracionInterna = value;
        }

        /// <summary>
        /// Cadena de conexión por nombre. Falla con un mensaje que dice cuál falta: Npgsql
        /// sólo reporta "The ConnectionString property has not been initialized", que no
        /// indica ni el nombre buscado ni desde dónde se pidió.
        /// </summary>
        protected string ObtenerCadenaConexion(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                connectionName = "ERP_SRS";

            string connStr = _configuration.GetConnectionString(connectionName);

            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException(
                    $"No se encontró la cadena de conexión '{connectionName}' en appsettings.json (sección ConnectionStrings).");

            return connStr;
        }

        /// <summary>
        /// Credenciales del PAC. Viven en la sección "Timbrado" de appsettings.json — la
        /// misma que enlaza TimbradoOptions y que ya usan PacService y TimbradoService.
        /// Se centraliza aquí porque en Crédito y Cobranza se leían mal: un módulo las
        /// buscaba en una sección "AppSettings" que no existe (y mandaba null al PAC) y
        /// otro las traía escritas en el código fuente.
        /// </summary>
        protected (string Usuario, string Password) ObtenerCredencialesPac()
        {
            string usuario = _configuration["Timbrado:User"];
            string password = _configuration["Timbrado:Password"];

            if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    "Faltan las credenciales del PAC: revisa la sección \"Timbrado\" " +
                    "(User / Password) en appsettings.json.");

            return (usuario, password);
        }

        /// <summary>
        /// Conexión abierta al ERP, para envolver varios pasos en una sola transacción.
        /// Sustituye al patrón `new Utilities(true)` que se usaba sólo para alcanzar la
        /// cadena de conexión cuando _configuration llegaba null.
        /// </summary>
        protected NpgsqlConnection AbrirConexion(string nombre = "ERP_SRS")
        {
            var conn = new NpgsqlConnection(ObtenerCadenaConexion(nombre));
            conn.Open();
            return conn;
        }

        /// <summary>
        /// ¿La tabla tiene esa columna? Sirve para que una funcionalidad nueva conviva
        /// con bases donde su script todavía no se ha corrido, en vez de reventar con un
        /// 42703 en producción. Devuelve false ante cualquier error, que es el lado
        /// seguro: se comporta como si la columna no existiera.
        /// </summary>
        protected bool ExisteColumna(string tabla, string columna,
            NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null)
        {
            try
            {
                object r = RunScalar(@"
                    SELECT COUNT(*)
                    FROM   information_schema.columns
                    WHERE  table_name  = @tabla
                      AND  column_name = @columna;",
                    new Dictionary<string, object>
                    {
                        { "tabla",   tabla },
                        { "columna", columna }
                    },
                    false, conn, tx);

                return r != null && Convert.ToInt32(r) > 0;
            }
            catch { return false; }
        }

        private static IConfiguration ConstruirConfiguracion() =>
            new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

        public Utilities()
        {
        }
        public Utilities(bool manual)
        {
            _configuration = ConstruirConfiguracion();
        }

        public void RunUpdate(string query, bool storedProcedure = false, string connectionName = "ERP_SRS")
        {
            RunUpdate(query, new Dictionary<string, object>(), storedProcedure, null, null, connectionName);
        }

        public void RunUpdate(string query, Dictionary<string, object> parameters, bool storedProcedure = false, NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null, string connectionName = "ERP_SRS")
        {
            bool ownsConnection = false;

            if (conn == null)
            {
                conn = new NpgsqlConnection(ObtenerCadenaConexion(connectionName));
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.CommandType = storedProcedure
                        ? CommandType.StoredProcedure
                        : CommandType.Text;

                    if (tx != null)
                        cmd.Transaction = tx;

                    foreach (var p in parameters)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
            catch (PostgresException ex)
            {
                // Observa y relanza: "throw;" desnudo conserva el stack original.
                // Ver RegistrarErrorDeDatos en Utilities.Errores.cs.
                RegistrarErrorDeDatos(ex, query);
                throw;
            }
            finally
            {
                if (ownsConnection)
                    conn.Close();
            }
        }

        public void RunUpdate(string query, List<Dictionary<string, object>> batchParameters,
                       bool storedProcedure = false, string connectionName = "ERP_SRS")
        {
            if (batchParameters == null || batchParameters.Count == 0)
            {
                RunUpdate(query, storedProcedure, connectionName);
                return;
            }

            var utils = new Utilities(true);
            var connStr = utils._configuration.GetConnectionString(connectionName);
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                using (var cmd = new NpgsqlCommand(query, conn, transaction))
                {
                    if (storedProcedure)
                        cmd.CommandType = CommandType.StoredProcedure;

                    foreach (var kv in batchParameters[0])
                    {
                        cmd.Parameters.Add(new NpgsqlParameter(kv.Key, kv.Value ?? DBNull.Value));
                    }

                    try
                    {
                        foreach (var paramSet in batchParameters)
                        {
                            foreach (NpgsqlParameter p in cmd.Parameters)
                            {
                                p.Value = paramSet.ContainsKey(p.ParameterName) ? paramSet[p.ParameterName] ?? DBNull.Value : DBNull.Value;
                            }
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit(); // ✅ Confirma todos los cambios si todo sale bien
                    }
                    catch (PostgresException ex)
                    {
                        // Este overload por lotes se quedó fuera de la primera pasada
                        // de instrumentación: un fallo en una carga masiva no dejaba
                        // rastro aunque las otras tres vías sí lo dejaran.
                        RegistrarErrorDeDatos(ex, query);
                        transaction.Rollback();
                        throw;
                    }
                    catch
                    {
                        transaction.Rollback(); // ❌ Revierte todo si hay error
                        throw;
                    }
                }
            }
        }

        public List<Dictionary<string, object>> RunQuery(string query, bool storedProcedure = false, string connectionName = "ERP_SRS")
        {
            return RunQuery(query, new Dictionary<string, object>(), storedProcedure, null, null, connectionName);
        }

        public List<Dictionary<string, object>> RunQuery(string query, Dictionary<string, object> parameters, bool storedProcedure = false, NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null, string connectionName = "ERP_SRS")
        {
            var result = new List<Dictionary<string, object>>();
            bool ownsConnection = false;

            if (conn == null)
            {
                conn = new NpgsqlConnection(ObtenerCadenaConexion(connectionName));
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.CommandType = storedProcedure
                        ? CommandType.StoredProcedure
                        : CommandType.Text;

                    if (tx != null)
                        cmd.Transaction = tx;

                    foreach (var p in parameters)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var algo = reader.GetName(i);
                                var value = reader.GetValue(i);

                                row[reader.GetName(i)] = reader.IsDBNull(i)
                                    ? null
                                    : reader.GetValue(i);
                            }
                            result.Add(row);
                        }
                    }
                }

                return result;
            }
            catch (PostgresException ex)
            {
                RegistrarErrorDeDatos(ex, query);
                throw;
            }
            finally
            {
                if (ownsConnection)
                    conn.Close();
            }
        }

        public object RunScalar(string query, Dictionary<string, object> parameters, bool storedProcedure = false, NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null, string connectionName = "ERP_SRS")
        {
            bool ownsConnection = false;

            if (conn == null)
            {
                conn = new NpgsqlConnection(ObtenerCadenaConexion(connectionName));
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.CommandType = storedProcedure
                        ? CommandType.StoredProcedure
                        : CommandType.Text;

                    if (tx != null)
                        cmd.Transaction = tx;

                    foreach (var p in parameters)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);

                    return cmd.ExecuteScalar();
                }
            }
            catch (PostgresException ex)
            {
                RegistrarErrorDeDatos(ex, query);
                throw;
            }
            finally
            {
                if (ownsConnection)
                    conn.Close();
            }
        }

        // El nombre de conexión iba vacío: GetConnectionString("") devuelve null y Npgsql
        // reventaba con "The ConnectionString property has not been initialized" en la
        // primera consulta que hiciera ControlProcesoHelper — es decir, al arrancar
        // cualquier cancelación. Se deja el valor por omisión, igual que RunUpdateWrapper.
        public List<Dictionary<string, object>> RunQueryWrapper(string query, Dictionary<string, object> parameters)
        {
            return RunQuery(query, parameters, false, null, null);
        }

        public int RunUpdateWrapper(string query, Dictionary<string, object> parameters)
        {
            RunUpdate(query, parameters, false);
            return 1; // Siempre devolver un entero, aunque no lo necesites
        }

        public List<List<Dictionary<string, object>>> CreateReturnResult(List<string> queries)
        {
            var all = new List<List<Dictionary<string, object>>>();
            foreach (var q in queries)
                all.Add(RunQuery(q));
            return all;
        }

        public static string GetUserRole(string userName, string connectionStringName = "ERP_SRS")
        {
            var util = new Utilities(true);
            string connectionString = util._configuration.GetConnectionString(connectionStringName);

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception($"No se encontró la cadena de conexión '{connectionStringName}' en el archivo web.config.");
            }

            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();

                // Llamada correcta a la función PostgreSQL por posición, no por nombre
                string query = "SELECT getuserrole(@userName);";

                using (var command = new NpgsqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("userName", userName); // Este nombre se ignora; se usa por posición

                    object result = command.ExecuteScalar();
                    return result?.ToString() ?? string.Empty;
                }
            }
        }

        public static bool DoesUserHasRole(string userName, string role, string connectionStringName = "ERP_SRS")
        {
            var util = new Utilities(true);
            string connectionString = util._configuration.GetConnectionString(connectionStringName);

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception($"No se encontró la cadena de conexión '{connectionStringName}' en el archivo web.config.");
            }

            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();

                // Llamada correcta a la función PostgreSQL por posición, no por nombre
                string query = "SELECT getuserrole(@userName);";

                using (var command = new NpgsqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("userName", userName); // Este nombre se ignora; se usa por posición

                    object result = command.ExecuteScalar();

                    if (new List<string> { "Super Administrador", "Sistemas", role }.Contains(result?.ToString()))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        public static bool DoesUserHasArea(string userName, string area)
        {
            string result = GetAreaName(userName);

            return result == "Direccion" || result == "Sistemas" || result == area;
        }

        public static bool DoesUserHasArea(string userName, List<string> areas)
        {
            string result = GetAreaName(userName);

            // Áreas por defecto
            var defaultAreas = new List<string> { "Direccion", "Sistemas" };

            return defaultAreas.Contains(result) || areas.Contains(result);
        }

        public bool DoesUserHasSucursal(string userName, string area)
        {
            string result = GetSucursalName(userName);

            return result == area;
        }

        public bool DoesUserHasSucursal(string userName, List<string> areas)
        {
            string result = GetSucursalName(userName);

            // Áreas por defecto
            var defaultAreas = new List<string> { "CEDIS", "SRS" };

            return defaultAreas.Contains(result) || areas.Contains(result);
        }

        public static string GetAreaName(object areaName)
        {
            if (areaName == null) return "IT APPS";
            return Regex.Replace(areaName.ToString(), "[a-z][A-Z]", m => $"{m.Value[0]} {m.Value[1]}");
        }

        public bool MissingParams(NameValueCollection form, params string[] keys)
        {
            foreach (var key in keys)
            {
                var val = form[key];
                if (string.IsNullOrWhiteSpace(val))
                    return true;
            }
            return false;
        }

        public string GetUser()
        {
            var name = User.Identity.Name ?? "";
            var idx = name.IndexOf("@", StringComparison.Ordinal);
            return idx > 0
                ? name.Substring(0, idx).ToLower()
                : name.ToLower();
        }



        /// <summary>
        /// Registra un movimiento del ticket en hst_est.
        /// Acepta conexion y transaccion para que el alta pueda escribir el historial
        /// dentro de la misma transaccion que crea el ticket.
        /// </summary>
        public void RegistrarCambioEstado(
            int idTicket,
            int? estadoAnterior,
            int? estadoNuevo,
            int idUsuario,
            int? idCategoria,
            int? idPrioridad,
            NpgsqlConnection conn = null,
            NpgsqlTransaction tx = null)
        {
            string query = "INSERT INTO hst_est ( " +
                           "    id_tkts, " +
                           "    id_stat_tkt_ant, " +
                           "    id_stat_tkt_nuev, " +
                           "    fch_cambio, " +
                           "    id_usr, " +
                           "    id_cat, " +
                           "    id_prio " +
                           ") " +
                           "VALUES (" +
                           "    @id_tkts, " +
                           "    @id_stat_tkt_ant, " +
                           "    @id_stat_tkt_nuev, " +
                           "    CURRENT_TIMESTAMP, " +
                           "    @id_usr, " +
                           "    @id_cat, " +
                           "    @id_prio " +
                           ")";

            var parametros = new Dictionary<string, object>
    {
        { "id_tkts", idTicket },
        { "id_stat_tkt_ant", estadoAnterior.HasValue ? (object)estadoAnterior.Value : DBNull.Value },
        { "id_stat_tkt_nuev", estadoNuevo.HasValue ? (object)estadoNuevo.Value : DBNull.Value },
        { "id_usr", idUsuario },
        { "id_cat", idCategoria.HasValue ? (object)idCategoria.Value : DBNull.Value },
        { "id_prio", idPrioridad.HasValue ? (object)idPrioridad.Value : DBNull.Value }
    };

            RunQuery(query, parametros, false, conn, tx, "ERP_SRS");
        }

        /// <summary>
        /// Reasigna un ticket de soporte y deja constancia en tkt_asig, en una transaccion.
        ///
        /// tkt_asig esta pensada como bitacora (tiene act, fch_asig y fch_ult_act) y asi la
        /// lee el "Historial de Tickets" de la vista. El codigo anterior hacia UPDATE de la
        /// fila activa, con lo que cada reasignacion pisaba la anterior y el historial solo
        /// podia mostrar la ultima. Aqui se cierra la asignacion vigente y se abre una nueva.
        ///
        /// Vive aqui, junto a RegistrarCambioEstado, porque la usan EstatusController
        /// (cambio individual) y TicketController (asignacion masiva); antes cada uno
        /// llevaba su copia, y la de EstatusController estaba escrita en T-SQL
        /// (SELECT TOP 1, GETDATE(), act = 1) contra PostgreSQL, asi que fallaba siempre.
        /// </summary>
        public void AsignarTicketSoporte(int idTicket, int nuevoResponsable, int asignadorId)
        {
            using var conn = AbrirConexion();
            using var tx = conn.BeginTransaction();

            try
            {
                // Cerrar la asignacion vigente, si la hay.
                RunUpdate(
                    "UPDATE tkt_asig SET act = FALSE, fch_ult_act = CURRENT_TIMESTAMP " +
                    "WHERE id_tkt = @id_tkt AND act = TRUE",
                    new Dictionary<string, object> { { "id_tkt", idTicket } },
                    false, conn, tx);

                // Abrir la nueva. Antes, si la ultima asignacion estaba inactiva se
                // respondia "el ticket ya esta resuelto o inactivo" y el ticket quedaba
                // imposible de reasignar para siempre; con la bitacora ese caso no existe.
                RunUpdate(
                    "INSERT INTO tkt_asig (id_tkt, id_usr, fch_asig, act, asig_por) " +
                    "VALUES (@id_tkt, @id_usr, CURRENT_TIMESTAMP, TRUE, @asig_por)",
                    new Dictionary<string, object>
                    {
                        { "id_tkt", idTicket },
                        { "id_usr", nuevoResponsable },
                        { "asig_por", asignadorId }
                    },
                    false, conn, tx);

                RunUpdate(
                    "UPDATE tkts SET id_usr_asig = @id_usr WHERE id_tkts = @id_tkt",
                    new Dictionary<string, object>
                    {
                        { "id_tkt", idTicket },
                        { "id_usr", nuevoResponsable }
                    },
                    false, conn, tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public (string resultado, Guid? loteOperacion) EjecutarMovimientosInventario(
    List<MovimientoInventario> movimientos,
    bool permitirNegativo,
    int usuarioId,
    string connectionStringName = "ERP_SRS")
        {
            string resultado = "";
            Guid? loteOperacion = null;

            string connStr = _configuration.GetConnectionString(connectionStringName);

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                using (SqlCommand cmd = new SqlCommand("sp_RegistrarLoteMovimientosInventario", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    // Parámetros de entrada
                    cmd.Parameters.AddWithValue("@PermitirNegativo", permitirNegativo);
                    cmd.Parameters.AddWithValue("@UsuarioId", usuarioId);

                    // Parámetro de salida - Resultado
                    SqlParameter pResultado = new SqlParameter("@Resultado", SqlDbType.NVarChar, -1)
                    {
                        Direction = ParameterDirection.Output
                    };
                    cmd.Parameters.Add(pResultado);

                    // Parámetro de salida - LoteOperacion
                    SqlParameter pLoteOperacion = new SqlParameter("@LoteOperacion", SqlDbType.UniqueIdentifier)
                    {
                        Direction = ParameterDirection.Output
                    };
                    cmd.Parameters.Add(pLoteOperacion);

                    // Crear el TVP
                    DataTable tvp = new DataTable();
                    tvp.Columns.Add("producto_id", typeof(int));
                    tvp.Columns.Add("cantidad", typeof(decimal));
                    tvp.Columns.Add("almacen_id", typeof(int));
                    tvp.Columns.Add("tipo_movimiento_id", typeof(int));
                    tvp.Columns.Add("referencia_externa", typeof(string));
                    tvp.Columns.Add("origen_proceso", typeof(string));
                    tvp.Columns.Add("usuario_id", typeof(int));
                    tvp.Columns.Add("observaciones", typeof(string));
                    tvp.Columns.Add("responsiva_id", typeof(int));

                    foreach (var m in movimientos)
                    {
                        tvp.Rows.Add(m.ProductoId, m.Cantidad, m.AlmacenId, m.TipoMovimientoId,
                                     m.ReferenciaExterna, m.OrigenProceso, m.UsuarioId,
                                     m.Observaciones, m.ResponsivaId); // NULL-safe
                    }

                    SqlParameter pTVP = new SqlParameter("@Movimientos", SqlDbType.Structured)
                    {
                        TypeName = "dbo.t_TVP_MovimientoInventario",
                        Value = tvp
                    };
                    cmd.Parameters.Add(pTVP);

                    // Ejecutar
                    conn.Open();
                    cmd.ExecuteNonQuery();

                    resultado = pResultado.Value?.ToString();
                    if (pLoteOperacion.Value != DBNull.Value)
                        loteOperacion = (Guid)pLoteOperacion.Value;
                }
            }

            return (resultado, loteOperacion);
        }

        public string RegistrarResponsiva(int usuarioId, string observaciones, List<ProductoResponsiva> productos, string connectionStringName = "ERP_SRS")
        {
            string resultado = "";
            string connStr = _configuration.GetConnectionString(connectionStringName);

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                using (SqlCommand cmd = new SqlCommand("sp_RegistrarResponsiva", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@UsuarioId", usuarioId);
                    cmd.Parameters.AddWithValue("@Observaciones", observaciones ?? "");

                    // Parametro de salida
                    SqlParameter pResultado = new SqlParameter("@Resultado", SqlDbType.NVarChar, -1)
                    {
                        Direction = ParameterDirection.Output
                    };
                    cmd.Parameters.Add(pResultado);

                    // Crear el TVP
                    DataTable tvp = new DataTable();
                    tvp.Columns.Add("producto_id", typeof(int));
                    tvp.Columns.Add("cantidad", typeof(decimal));
                    tvp.Columns.Add("almacen_id", typeof(int));

                    foreach (var p in productos)
                    {
                        tvp.Rows.Add(p.ProductoId, p.Cantidad, p.AlmacenId);
                    }

                    SqlParameter pTVP = new SqlParameter("@Productos", SqlDbType.Structured)
                    {
                        TypeName = "dbo.TVP_ResponsivaDetalle",
                        Value = tvp
                    };
                    cmd.Parameters.Add(pTVP);

                    // Ejecutar
                    conn.Open();
                    cmd.ExecuteNonQuery();

                    resultado = pResultado.Value.ToString();
                }
            }

            return resultado;
        }

        public string GenerarQRBase64ConLogo(string texto, string logoPath, string outputPath = null)
        {
            if (string.IsNullOrWhiteSpace(texto))
                throw new ArgumentException("El contenido del QR no puede estar vacío.");

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(texto, QRCodeGenerator.ECCLevel.H))
            using (QRCode qrCode = new QRCode(qrCodeData))
            {
                // Cargar y redimensionar logo
                using (Bitmap originalLogo = new Bitmap(logoPath))
                {
                    int logoSize = 150;
                    Bitmap resizedLogo = new Bitmap(logoSize, logoSize);
                    using (Graphics g = Graphics.FromImage(resizedLogo))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(originalLogo, 0, 0, logoSize, logoSize);
                    }

                    // Aplicar un color uniforme (tinte azul) al logo
                    Color tintColor = Color.FromArgb(1, 19, 49);
                    Bitmap tintedLogo = new Bitmap(logoSize, logoSize);
                    using (Graphics g = Graphics.FromImage(tintedLogo))
                    {
                        ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                        {
                    new float[] {tintColor.R / 255f, 0, 0, 0, 0},
                    new float[] {0, tintColor.G / 255f, 0, 0, 0},
                    new float[] {0, 0, tintColor.B / 255f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                        });

                        ImageAttributes attributes = new ImageAttributes();
                        attributes.SetColorMatrix(colorMatrix);

                        g.DrawImage(resizedLogo, new Rectangle(0, 0, logoSize, logoSize), 0, 0, logoSize, logoSize, GraphicsUnit.Pixel, attributes);
                    }

                    // Crear logo con borde blanco circular
                    int borderSize = 10;
                    int totalSize = logoSize + borderSize * 2;
                    Bitmap logoWithBorder = new Bitmap(totalSize, totalSize);
                    using (Graphics g = Graphics.FromImage(logoWithBorder))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.White);

                        GraphicsPath path = new GraphicsPath();
                        path.AddEllipse(0, 0, totalSize, totalSize);
                        g.SetClip(path);

                        g.DrawImage(tintedLogo, borderSize, borderSize);
                    }

                    // Generar QR con el logo
                    using (Bitmap qrCodeImage = qrCode.GetGraphic(
                        pixelsPerModule: 20,
                        darkColor: tintColor,
                        lightColor: Color.WhiteSmoke,
                        icon: logoWithBorder,
                        iconSizePercent: 30,
                        iconBorderWidth: 1,
                        drawQuietZones: true
                    ))
                    {
                        if (!string.IsNullOrEmpty(outputPath))
                        {
                            qrCodeImage.Save(outputPath, ImageFormat.Png);
                        }

                        using (MemoryStream ms = new MemoryStream())
                        {
                            qrCodeImage.Save(ms, ImageFormat.Png);
                            return Convert.ToBase64String(ms.ToArray());
                        }
                    }
                }
            }
        }

        public (string folio, string error) GenerarFolioDocumento(int empresa_id, int idarea, int idtpdoc, int anio, string connectionStringName = "ERP_SRS")
        {
            string folioGenerado = "";
            string error = "";

            try
            {
                string connStr = _configuration.GetConnectionString(connectionStringName);

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    using (SqlCommand cmd = new SqlCommand("GenerarFolioDocumento", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        // Parámetros de entrada
                        cmd.Parameters.AddWithValue("@empresa_id", empresa_id);
                        cmd.Parameters.AddWithValue("@idarea", idarea);
                        cmd.Parameters.AddWithValue("@idtpdoc", idtpdoc);
                        cmd.Parameters.AddWithValue("@anio", anio);

                        // Parámetro de salida
                        SqlParameter pFolio = new SqlParameter("@folioGenerado", SqlDbType.VarChar, 50)
                        {
                            Direction = ParameterDirection.Output
                        };
                        cmd.Parameters.Add(pFolio);

                        conn.Open();
                        cmd.ExecuteNonQuery();

                        folioGenerado = pFolio.Value?.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            return (folioGenerado, error);
        }

        public Dictionary<string, object> GenerarDocumentoConPartidas(DocumentoEncabezado encabezado, List<PartidaDocumento> partidas, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;

            string folioGenerado = string.Empty;
            int idDocumentoGenerado = 0;
            var returnResult = new Dictionary<string, object>();

            if (conn == null)
            {
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString(connectionStringName);
                conn = new NpgsqlConnection(connStr);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand("SELECT * FROM sp_generar_documento_con_partidas(@empresa_id, @idarea,  " +
                "@idtpdoc, @anio, @suc, @fch, @cli_prov, @partidas_json, @ccy, @alm, @refe, @vdr_cpr, @dto, @firma3, @firma4,  " +
                "@imp, @pl_dias, @fch_pg_entrega, @sub, @firma2, @coment1, @coment2, @coment3, @firma5, @nro_cot_prev, @ped_orig,  " +
                "@id_cartera, @cve_pais, @cve_edo, @cve_mpio, @cve_conf, @cfdi, @veh, @f_pago, @cto_ad_fte, @incoterm, @tc_fte,  " +
                "@peso_teor, @usr_doc, @fch_cap, @hr_pg_entrega, @bas_fol, @nro_coment_x_part, @nro_car_coment_x_part, @suc_origen, @tp_mov,  " +
                "@n_cli, @cl_cli, @col_cli, @pob_cli, @centro_costos, @en_presupuesto, @flete, @usr_dep, @par, @fch_ref, @saldo_doc,  " +
                "@stat, @cve_proy, @cva_bco, @cve_cli, @dest_ch, @n_pers, @mto_antic, @mt_extra1, @mt_extra2, @mt_extra3, @tiene_pendientes,  " +
                "@mt_extra5, @mt_extra6, @mt_extra7, @mt_extra8, @mt_extra9, @mdp, @coment_aut, " +
                "@fecha_edicion, @editado_por, @tipo_producto, @tipo_proceso, " +
                "@firma0, @usr0, @fch0, @usr1, @firma1, @fch1, @usr2, @fch2, @usr3, @fch3, @usr4, @fch4, @usr5, @fch5, @usr6, @firma6, @fch6, @enc_padre, @status, " +
                "@es_servicio, @orden_compra)", conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.CommandType = CommandType.Text;

                    // Parámetros requeridos
                    cmd.Parameters.AddWithValue("empresa_id", encabezado.EmpresaId);
                    cmd.Parameters.AddWithValue("idarea", encabezado.IdArea);
                    cmd.Parameters.AddWithValue("idtpdoc", encabezado.IdTpDoc);
                    cmd.Parameters.AddWithValue("anio", encabezado.Anio);
                    cmd.Parameters.AddWithValue("suc", encabezado.Suc);
                    cmd.Parameters.AddWithValue("fch", encabezado.Fch);
                    cmd.Parameters.AddWithValue("cli_prov", encabezado.CliProv);

                    // Partidas como JSON
                    var partidasConvertidas = partidas.Select(p => new
                    {
                        nro_part = p.NroPart,
                        cve_prod = p.CveProd,
                        cant_ud = p.CantUd,
                        descr_prod = p.DescrProd,
                        ud = p.Ud,
                        pv_prod = p.PvProd,
                        imp_part = p.ImpPart,
                        dto1 = p.Dto1,
                        iva = p.Iva,
                        ieps = p.Ieps,
                        f_pago_id = p.FPagoId,
                        gpo_doc_ant = p.GpoDocAnt,
                        tp_doc_ant = p.TpDocAnt,
                        fol_doc_ant = p.FolDocAnt,
                        part_doc_ant = p.PartDocAnt,
                        saldo_ud_part = p.SaldoUdPart,
                        cve_cli = p.CveCli,
                        cto_vta_part = p.CtoVtaPart,
                        cve_vdr_cpr = p.CveVdrCpr,
                        refe = p.Ref,
                        cve_alm = p.CveAlm,
                        fch = p.Fch,
                        mt_cto_ = p.MtCto,
                        exis_prev_u = p.ExisPrevU,
                        exis_prev_peso = p.ExisPrevPeso,
                        ccy = p.Ccy,
                        cto_ccy = p.CtoCcy,
                        vta_ccy = p.VtaCcy,
                        pedimento = p.Pedimento,
                        conc = p.Conc,
                        producto_id = p.IdProducto
                    }).ToList();

                    var partidasJson = JsonConvert.SerializeObject(partidasConvertidas);

                    cmd.Parameters.AddWithValue("ccy", (object)encabezado.Ccy ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("alm", (object)encabezado.Alm ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("refe", (object)encabezado.Ref ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("vdr_cpr", (object)encabezado.VdrCpr ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("dto", (object)encabezado.Dto ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("firma3", (object)encabezado.Firma3 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("firma4", (object)encabezado.Firma4 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("imp", (object)encabezado.Imp ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("pl_dias", (object)encabezado.PlDias ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch_pg_entrega", encabezado.FchPgEntrega.HasValue ? encabezado.FchPgEntrega.Value : (object)DBNull.Value);

                    cmd.Parameters.AddWithValue("sub", (object)encabezado.Sub ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("firma2", (object)encabezado.Firma2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("coment1", (object)encabezado.Coment1 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("coment2", (object)encabezado.Coment2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("coment3", (object)encabezado.Coment3 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("firma5", (object)encabezado.Firma5 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("nro_cot_prev", (object)encabezado.NroCotPrev ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("ped_orig", (object)encabezado.PedOrig ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("id_cartera", (object)encabezado.IdCartera ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cve_pais", (object)encabezado.CvePais ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cve_edo", (object)encabezado.CveEdo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cve_mpio", (object)encabezado.CveMpio ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cve_conf", (object)encabezado.CveConf ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cfdi", (object)encabezado.CFDI ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("veh", (object)encabezado.Veh ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("f_pago", (object)encabezado.FPago ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cto_ad_fte", (object)encabezado.CtoAdFte ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("incoterm", (object)encabezado.Incoterm ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("tc_fte", (object)encabezado.TcFte ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("peso_teor", (object)encabezado.PesoTeor ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr_doc", (object)encabezado.UsrDoc ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch_cap", (object)encabezado.FchCap ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("hr_pg_entrega", (object)encabezado.HrPgEntrega ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("bas_fol", (object)encabezado.BasFol ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("nro_coment_x_part", (object)encabezado.NroComentXPart ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("nro_car_coment_x_part", (object)encabezado.NroCarComentXPart ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("suc_origen", (object)encabezado.SucOrigen ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("tp_mov", (object)encabezado.TpMov ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("n_cli", (object)encabezado.NCli ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cl_cli", (object)encabezado.ClCli ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("col_cli", (object)encabezado.ColCli ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("pob_cli", (object)encabezado.PobCli ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("centro_costos", (object)encabezado.CentroCostos ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("en_presupuesto", (object)encabezado.EnPresupuesto ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("flete", (object)encabezado.Flete ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr_dep", (object)encabezado.UsrDep ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("par", (object)encabezado.Par ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch_ref", (object)encabezado.FchRef ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("saldo_doc", (object)encabezado.SaldoDoc ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("stat", (object)encabezado.Stat ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cve_proy", (object)encabezado.CveProy ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cva_bco", (object)encabezado.CvaBco ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cve_cli", (object)encabezado.CveCli ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("dest_ch", (object)encabezado.DestCh ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("n_pers", (object)encabezado.NPers ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mto_antic", (object)encabezado.MtoAntic ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra1", (object)encabezado.MtExtra1 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra2", (object)encabezado.MtExtra2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra3", (object)encabezado.MtExtra3 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("tiene_pendientes", (object)encabezado.TienePendientes ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra5", (object)encabezado.MtExtra5 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra6", (object)encabezado.MtExtra6 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra7", (object)encabezado.MtExtra7 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra8", (object)encabezado.MtExtra8 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mt_extra9", (object)encabezado.MtExtra9 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("mdp", (object)encabezado.Mdp ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("coment_aut", (object)encabezado.ComentAut ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fecha_edicion", (object)encabezado.FechaEdicion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("editado_por", (object)encabezado.EditadoPor ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("tipo_producto", (object)encabezado.TipoProducto ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("tipo_proceso", (object)encabezado.TipoPoceso ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("firma0", (object)encabezado.Firma0 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr0", (object)encabezado.Usr0 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch0", (object)encabezado.Fch0 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr1", (object)encabezado.Usr1 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("firma1", (object)encabezado.Firma1 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch1", (object)encabezado.Fch1 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr2", (object)encabezado.Usr2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch2", (object)encabezado.Fch2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr3", (object)encabezado.Usr3 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch3", (object)encabezado.Fch3 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr4", (object)encabezado.Usr4 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch4", (object)encabezado.Fch4 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr5", (object)encabezado.Usr5 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch5", (object)encabezado.Fch5 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("usr6", (object)encabezado.Usr6 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("firma6", (object)encabezado.Firma6 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("fch6", (object)encabezado.Fch6 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("enc_padre", (object)encabezado.EncabezadoPadre ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("status", (object)encabezado.Estatus ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("status", (object)encabezado.NatDocPadreChar ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("es_servicio", (object)encabezado.EsServicio ?? false);
                    cmd.Parameters.AddWithValue("orden_compra", (object)encabezado.NatDocPadreChar ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("partidas_json", NpgsqlTypes.NpgsqlDbType.Jsonb, partidasJson);

                    if (conn.State != ConnectionState.Open)
                        conn.Open();
                    Console.WriteLine(cmd == null);
                    Console.WriteLine(cmd.CommandText);
                    Console.WriteLine(cmd.Parameters.Count);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            folioGenerado = reader.GetString(reader.GetOrdinal("folio_generado"));
                            idDocumentoGenerado = reader.GetInt32(reader.GetOrdinal("IdEncabezado"));

                            returnResult.Add("folio_generado", folioGenerado);
                            returnResult.Add("IdEncabezado", idDocumentoGenerado);
                        }
                    }
                }
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }

            return returnResult;
        }

        public Dictionary<string, List<MovimientoDetalle>> RegistrarMovimiento( List<Dictionary<string, object>> productos, int usuario, string tipoMov, int? origen, int? destino, string motivo, int? encabezado, string comentario, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            // === VALIDACIONES PREVIAS ===
            if (usuario <= 0)
                throw new ArgumentException("El ID de usuario debe ser mayor que cero.", nameof(usuario));

            if (string.IsNullOrWhiteSpace(tipoMov))
                throw new ArgumentException("El tipo de movimiento es obligatorio.", nameof(tipoMov));

            if (productos == null || productos.Count == 0)
                throw new ArgumentException("Debe especificar al menos un producto.", nameof(productos));

            foreach (var prod in productos)
            {
                if (!prod.ContainsKey("id_producto") || prod["id_producto"] == null)
                    throw new ArgumentException("Cada producto debe tener un 'id_producto' válido.");

                if (!prod.ContainsKey("cantidad") || prod["cantidad"] == null)
                    throw new ArgumentException("Cada producto debe tener una 'cantidad' válida.");

                if (!decimal.TryParse(prod["cantidad"].ToString(), out var cantidad) || cantidad <= 0)
                    throw new ArgumentException("La cantidad del producto debe ser un número positivo.");
            }

            if (tipoMov.Equals("Movimiento", StringComparison.OrdinalIgnoreCase))
            {
                if (!origen.HasValue || !destino.HasValue)
                    throw new ArgumentException("Para transferencias se requiere origen y destino.");

                if (origen.Value == destino.Value)
                    throw new ArgumentException("El origen y destino no pueden ser iguales.");
            }

            motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();

            bool ownsConnection = false;

            if (conn == null)
            {
                var utils = new Utilities(true);
                var connSetting = utils._configuration.GetConnectionString(connectionStringName)
                    ?? throw new InvalidOperationException(
                        $"No se encontró la cadena de conexión '{connectionStringName}'.");

                conn = new NpgsqlConnection(connSetting);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                // Ahora es una FUNCTION: se invoca con SELECT, no con CALL.
                using (var cmd = new NpgsqlCommand(
                    "SELECT srs.registrar_movimiento(@usuario, @tipoMov, @productos, @origen, @destino, @motivo, @encabezado, @comentario)",
                    conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("usuario", usuario);
                    cmd.Parameters.AddWithValue("tipoMov", tipoMov);

                    var productosJson = JsonConvert.SerializeObject(productos);
                    cmd.Parameters.AddWithValue("productos", NpgsqlTypes.NpgsqlDbType.Jsonb, productosJson);

                    cmd.Parameters.AddWithValue("origen", origen.HasValue ? (object)origen.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("destino", destino.HasValue ? (object)destino.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("encabezado", encabezado.HasValue ? (object)encabezado.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("motivo", motivo != null ? (object)motivo : DBNull.Value);
                    cmd.Parameters.AddWithValue("comentario", comentario != null ? (object)comentario : DBNull.Value);

                    var raw = cmd.ExecuteScalar();

                    if (raw == null || raw == DBNull.Value)
                        return new Dictionary<string, List<MovimientoDetalle>>();

                    var json = raw as string ?? raw.ToString();

                    return JsonConvert.DeserializeObject<Dictionary<string, List<MovimientoDetalle>>>(json) ?? new Dictionary<string, List<MovimientoDetalle>>();
                }
            }
            catch (PostgresException ex)
            {
                throw new Exception(
                    $"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }
        }

        //public void RegistrarMovimiento(List<Dictionary<string, object>> productos, int usuario, string tipoMov, int? origen, int? destino, string motivo, int? encabezado, string comentario,
        //     NpgsqlConnection conn = null,
        //     NpgsqlTransaction tx = null,
        //     string connectionStringName = "ERP_SRS")
        //{
        //    // === VALIDACIONES PREVIAS ===
        //    if (usuario <= 0)
        //        throw new ArgumentException("El ID de usuario debe ser mayor que cero.", nameof(usuario));

        //    if (string.IsNullOrWhiteSpace(tipoMov))
        //        throw new ArgumentException("El tipo de movimiento es obligatorio.", nameof(tipoMov));

        //    if (productos == null || productos.Count == 0)
        //        throw new ArgumentException("Debe especificar al menos un producto.", nameof(productos));

        //    foreach (var prod in productos)
        //    {
        //        if (!prod.ContainsKey("id_producto") || prod["id_producto"] == null)
        //            throw new ArgumentException("Cada producto debe tener un 'id_producto' válido.");

        //        if (!prod.ContainsKey("cantidad") || prod["cantidad"] == null)
        //            throw new ArgumentException("Cada producto debe tener una 'cantidad' válida.");

        //        if (!decimal.TryParse(prod["cantidad"].ToString(), out var cantidad) || cantidad <= 0)
        //            throw new ArgumentException("La cantidad del producto debe ser un número positivo.");
        //    }

        //    if (tipoMov.Equals("Movimiento", StringComparison.OrdinalIgnoreCase))
        //    {
        //        if (!origen.HasValue || !destino.HasValue)
        //            throw new ArgumentException("Para transferencias se requiere origen y destino.");

        //        if (origen.Value == destino.Value)
        //            throw new ArgumentException("El origen y destino no pueden ser iguales.");
        //    }

        //    motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();

        //    bool ownsConnection = false;

        //    if (conn == null)
        //    {
        //        var utils = new Utilities(true);
        //        var connSetting = utils._configuration.GetConnectionString(connectionStringName)
        //            ?? throw new InvalidOperationException(
        //                $"No se encontró la cadena de conexión '{connectionStringName}'.");

        //        conn = new NpgsqlConnection(connSetting);
        //        conn.Open();
        //        ownsConnection = true;
        //    }

        //    try
        //    {
        //        using (var cmd = new NpgsqlCommand(
        //            "CALL registrar_movimiento(@usuario, @tipoMov, @productos, @origen, @destino, @motivo, @encabezado, @comentario)",
        //            conn))
        //        {
        //            if (tx != null)
        //                cmd.Transaction = tx;

        //            cmd.Parameters.AddWithValue("usuario", usuario);
        //            cmd.Parameters.AddWithValue("tipoMov", tipoMov);

        //            var productosJson = JsonConvert.SerializeObject(productos);
        //            cmd.Parameters.AddWithValue("productos", NpgsqlTypes.NpgsqlDbType.Jsonb, productosJson);

        //            cmd.Parameters.AddWithValue("origen", origen.HasValue ? (object)origen.Value : DBNull.Value);
        //            cmd.Parameters.AddWithValue("destino", destino.HasValue ? (object)destino.Value : DBNull.Value);
        //            cmd.Parameters.AddWithValue("encabezado", encabezado.HasValue ? (object)encabezado.Value : DBNull.Value);
        //            cmd.Parameters.AddWithValue("motivo", motivo != null ? (object)motivo : DBNull.Value);
        //            cmd.Parameters.AddWithValue("comentario", comentario != null ? (object)comentario : DBNull.Value);

        //            cmd.ExecuteNonQuery();
        //        }
        //    }
        //    catch (PostgresException ex)
        //    {
        //        throw new Exception(
        //            $"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
        //    }
        //    finally
        //    {
        //        if (ownsConnection)
        //            conn.Dispose();
        //    }
        //}

        public void RevertirMovimientoInventario(int id_encabezado, int usuario, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;

            if (conn == null)
            {
                var connSetting = _configuration.GetConnectionString(connectionStringName)
                    ?? throw new InvalidOperationException(
                        $"No se encontró la cadena de conexión '{connectionStringName}'.");

                conn = new NpgsqlConnection(connSetting);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand("SELECT revertir_movimiento_inventario(@encabezado, @usuario)", conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("encabezado", id_encabezado);
                    cmd.Parameters.AddWithValue("usuario", usuario);
                    cmd.ExecuteScalar();
                }
            }
            catch (PostgresException ex)
            {
                throw new Exception(
                    $"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }
        }

        public void AplicarCobrosCliente(AplicarCobros cobro, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;

            if (conn == null)
            {
                var connSetting = _configuration.GetConnectionString(connectionStringName)
                    ?? throw new InvalidOperationException(
                        $"No se encontró la cadena de conexión '{connectionStringName}'.");

                conn = new NpgsqlConnection(connSetting);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand("SELECT aplicar_cobros_cliente(@carteras, @cobros, @usuario)", conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("cliente", cobro.ClienteId);
                    cmd.Parameters.AddWithValue("carteras", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, cobro.CarteraIds.ToArray());
                    cmd.Parameters.AddWithValue("cobros", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, cobro.CobrosIds.ToArray());
                    cmd.Parameters.AddWithValue("usuario", cobro.UsuarioId);
                    cmd.ExecuteScalar();
                }
            }
            catch (PostgresException ex)
            {
                throw new Exception(
                    $"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }
        }

        public ResultadoAplicacionAnticipos AplicarAnticipos(AplicarAnticipos anticipo, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;

            if (conn == null)
            {
                var connSetting = _configuration.GetConnectionString(connectionStringName)
                    ?? throw new InvalidOperationException($"No se encontró la cadena de conexión '{connectionStringName}'.");

                conn = new NpgsqlConnection(connSetting);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand("SELECT * FROM aplicar_anticipos_cliente(@cartera, @anticipos, @usuario)", conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("cartera", anticipo.CarteraId);
                    cmd.Parameters.Add(new NpgsqlParameter("anticipos",
                        NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer)
                    { Value = anticipo.Anticipos.ToArray() });

                    cmd.Parameters.AddWithValue("usuario", anticipo.UsuarioId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new ResultadoAplicacionAnticipos
                            {
                                TotalAplicado = reader.GetDecimal(0),
                                SaldoRestante = reader.GetDecimal(1),
                                CarteraSaldada = reader.GetBoolean(2)
                            };
                        }
                    }
                }
            }
            catch (PostgresException ex)
            {
                throw new Exception($"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }

            return null;
        }

        public RegistrarCarteraResult RegistrarCartera(int id_poliza, int id_usuario, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;

            if (conn == null)
            {
                var connSetting = _configuration.GetConnectionString(connectionStringName)
                    ?? throw new InvalidOperationException($"No se encontró la cadena de conexión '{connectionStringName}'.");

                conn = new NpgsqlConnection(connSetting);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand("SELECT * FROM registrar_cartera(@poliza, @usuario, @empresa)", conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("poliza", id_poliza);
                    cmd.Parameters.AddWithValue("usuario", id_usuario);
                    cmd.Parameters.AddWithValue("empresa", GetEmpresaId(id_usuario));

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new RegistrarCarteraResult
                            {
                                CarteraId = reader.GetInt32(reader.GetOrdinal("cartera_id")),
                                MontoTotal = reader.GetDecimal(reader.GetOrdinal("monto_total")),
                                SaldoPendiente = reader.GetDecimal(reader.GetOrdinal("saldo_pendiente")),
                                Tipo = reader.GetString(reader.GetOrdinal("tipo"))
                            };
                        }
                    }
                }
            }
            catch (PostgresException ex)
            {
                throw new Exception($"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }

            return null;
        }

        public PagoProveedorResult CrearPagoProveedor(int id_poliza, int id_usuario, int? id_cartera = null, bool es_parcial = false, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;

            if (conn == null)
            {
                var connSetting = _configuration.GetConnectionString(connectionStringName)
                    ?? throw new InvalidOperationException($"No se encontró la cadena de conexión '{connectionStringName}'.");

                conn = new NpgsqlConnection(connSetting);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand("SELECT * FROM registrar_pago_proveedor(@poliza, @usuario, @cartera, @parcial)", conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("poliza", id_poliza);
                    cmd.Parameters.AddWithValue("usuario", id_usuario);
                    cmd.Parameters.AddWithValue("cartera", id_cartera != null ? (object)id_cartera : DBNull.Value);
                    cmd.Parameters.AddWithValue("parcial", es_parcial);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new PagoProveedorResult
                            {
                                PagoId = reader.GetInt32(reader.GetOrdinal("pago_id")),
                                MontoTotal = reader.GetDecimal(reader.GetOrdinal("monto_total")),
                                MontoAplicado = reader.GetDecimal(reader.GetOrdinal("monto_aplicado")),
                                SaldoDisponible = reader.GetDecimal(reader.GetOrdinal("saldo_disponible"))
                            };
                        }
                    }
                }
            }
            catch (PostgresException ex)
            {
                throw new Exception($"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }

            return null;
        }

        //public CobroClienteResult CrearCobroCliente(int id_poliza, int id_usuario, int? id_cartera = null, bool es_anticipo = false, bool es_parcial = false, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        //{
        //    bool ownsConnection = false;

        //    if (conn == null)
        //    {
        //        var connSetting = _configuration.GetConnectionString(connectionStringName)
        //            ?? throw new InvalidOperationException($"No se encontró la cadena de conexión '{connectionStringName}'.");

        //        conn = new NpgsqlConnection(connSetting);
        //        conn.Open();
        //        ownsConnection = true;
        //    }

        //    try
        //    {
        //        using (var cmd = new NpgsqlCommand("SELECT * FROM registrar_cobro_cliente(@poliza, @usuario, @cartera, @anticipo, @parcial)", conn))
        //        {
        //            if (tx != null)
        //                cmd.Transaction = tx;

        //            cmd.Parameters.AddWithValue("poliza", id_poliza);
        //            cmd.Parameters.AddWithValue("usuario", id_usuario);
        //            cmd.Parameters.AddWithValue("cartera", id_cartera != null ? (object)id_cartera : DBNull.Value);
        //            cmd.Parameters.AddWithValue("anticipo", es_anticipo);
        //            cmd.Parameters.AddWithValue("parcial", es_parcial);

        //            using (var reader = cmd.ExecuteReader())
        //            {
        //                if (reader.Read())
        //                {
        //                    return new CobroClienteResult
        //                    {
        //                        PagoId = reader.GetInt32(reader.GetOrdinal("cobro_id")),
        //                        MontoTotal = reader.GetDecimal(reader.GetOrdinal("monto_total")),
        //                        MontoAplicado = reader.GetDecimal(reader.GetOrdinal("monto_aplicado")),
        //                        SaldoDisponible = reader.GetDecimal(reader.GetOrdinal("saldo_disponible"))
        //                    };
        //                }
        //            }
        //        }
        //    }
        //    catch (PostgresException ex)
        //    {
        //        throw new Exception($"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
        //    }
        //    finally
        //    {
        //        if (ownsConnection)
        //            conn.Dispose();
        //    }

        //    return null;
        //}

        public CobroClienteResult CrearCobroCliente(int? id_poliza, int? id_usuario, TipoCobroCliente tipo_cobro, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            if (id_poliza == null || id_poliza <= 0)
                throw new ArgumentException("No se encontro una poliza valida.");

            if (id_usuario == null || id_usuario <= 0)
                throw new ArgumentException("No se encontro un usuario valido.");

            if (!Enum.IsDefined(typeof(TipoCobroCliente), tipo_cobro))
                throw new ArgumentException("Tipo de cobro inválido.", nameof(tipo_cobro));

            bool ownsConnection = false;

            if (conn == null)
            {
                var connSetting = _configuration.GetConnectionString(connectionStringName)
                    ?? throw new InvalidOperationException($"No se encontró la cadena de conexión '{connectionStringName}'.");

                conn = new NpgsqlConnection(connSetting);
                conn.Open();
                ownsConnection = true;
            }

            try
            {
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(conn.ConnectionString);
                dataSourceBuilder.MapEnum<TipoCobroCliente>("tipo_cobro_cliente");
                var dataSource = dataSourceBuilder.Build();
                using (var cmd = new NpgsqlCommand("SELECT * FROM registrar_cobro_cliente(@poliza, @usuario, @tipo::tipo_cobro_cliente)", conn))
                {
                    if (tx != null)
                        cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("poliza", id_poliza);
                    cmd.Parameters.AddWithValue("usuario", id_usuario);
                    cmd.Parameters.AddWithValue("tipo", ObtenerTipoCobro(tipo_cobro));
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CobroClienteResult
                            {
                                PagoId = reader.GetInt32(reader.GetOrdinal("cobro_id")),
                                MontoTotal = reader.GetDecimal(reader.GetOrdinal("monto")),
                                SaldoDisponible = reader.GetDecimal(reader.GetOrdinal("monto"))
                            };
                        }
                    }
                }
            }
            catch (PostgresException ex)
            {
                throw new Exception($"Error en PostgreSQL ({ex.SqlState}): {ex.MessageText}", ex);
            }
            finally
            {
                if (ownsConnection)
                    conn.Dispose();
            }

            return null;
        }
        private static string ObtenerTipoCobro(TipoCobroCliente tipo) => tipo switch
        {
            TipoCobroCliente.Normal => "normal",
            TipoCobroCliente.Anticipo => "anticipo",
            TipoCobroCliente.Complemento => "complemento",
            TipoCobroCliente.NotaCredito => "nota_credito",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo))
        };

        public void RegistrarCompra(int id_encabezado, int id_usuario, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;
            bool ownsTransaction = false;

            if (conn == null)
            {
                string connStr = _configuration.GetConnectionString(connectionStringName);

                conn = new NpgsqlConnection(connStr);
                conn.Open();
                ownsConnection = true;
            }

            if (tx == null)
            {
                tx = conn.BeginTransaction();
                ownsTransaction = true;
            }

            try
            {
                string query = "SELECT procesar_registro_compras(@id_encabezado, @id_usuario, @empresa);";
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("id_encabezado", id_encabezado);
                    cmd.Parameters.AddWithValue("id_usuario", id_usuario);
                    cmd.Parameters.AddWithValue("empresa", HttpContext.Session.GetInt32("Empresa"));

                    cmd.ExecuteNonQuery();
                }

                if (ownsTransaction)
                    tx.Commit();
            }
            catch
            {
                if (ownsTransaction)
                    tx.Rollback();
                throw;
            }
            finally
            {
                if (ownsConnection)
                    conn.Close();
            }
        }

        #region GenerarPolizas
        public (int idPoliza, Guid uuidPoliza) RegistrarPolizas(int usuario, int? encabezado, PolizaData poliza, bool tiene_discrepancia = false, int? id_encabezado_disc = null, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            bool ownsConnection = false;
            bool ownsTransaction = false;
            var returnResult = (0, Guid.Empty);

            if (conn == null)
            {
                string connStr = _configuration.GetConnectionString(connectionStringName);
                conn = new NpgsqlConnection(connStr);
                conn.Open();
                ownsConnection = true;
            }

            if (tx == null)
            {
                tx = conn.BeginTransaction();
                ownsTransaction = true;
            }

            try
            {
                using (var cmd = new NpgsqlCommand(
                    "SELECT * FROM registrar_poliza(@usuario, @encabezado, @datos::jsonb, @empresa_id, @tiene_discrepancia, @encabezado_disc);",
                    conn))
                {
                    cmd.Transaction = tx;

                    cmd.Parameters.AddWithValue("usuario", usuario);
                    cmd.Parameters.Add("encabezado", NpgsqlTypes.NpgsqlDbType.Integer)
                        .Value = (object)encabezado ?? DBNull.Value;

                    var polizaJson = JsonConvert.SerializeObject(poliza);
                    cmd.Parameters.AddWithValue("datos", NpgsqlTypes.NpgsqlDbType.Jsonb, polizaJson);
                    cmd.Parameters.AddWithValue("empresa_id", GetEmpresaId(usuario));
                    cmd.Parameters.AddWithValue("tiene_discrepancia", tiene_discrepancia);
                    cmd.Parameters.AddWithValue("encabezado_disc", id_encabezado_disc ?? (object)DBNull.Value);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new Exception("No se devolvió ningún resultado desde registrar_poliza().");

                        returnResult = (reader.GetInt32(0), reader.GetGuid(1));
                    }
                }

                if (ownsTransaction)
                    tx.Commit();

                return returnResult;
            }
            catch
            {
                if (ownsTransaction)
                    tx.Rollback();
                throw;
            }
            finally
            {
                if (ownsConnection)
                    conn.Close();
            }
        }

        public List<(int idPoliza, Guid uuidPoliza)> RegistrarPolizas(int usuario, int? encabezado, List<PolizaData> polizas, bool tiene_discrepancia = false, int? id_encabezado_disc = null, NpgsqlConnection conn = null, NpgsqlTransaction tx = null, string connectionStringName = "ERP_SRS")
        {
            LogErrorHelper.RegistrarLog(
                       "POLIZAS",
                       "SIN_FOLIO",
                       $"Entrando a registrar polizas",
                       nivel: "DEBUG"
                   );

            bool ownsConnection = false;
            bool ownsTransaction = false;
            var resultados = new List<(int idPoliza, Guid uuidPoliza)>();

            if (conn == null)
            {
                string connStr = _configuration.GetConnectionString(connectionStringName);
                conn = new NpgsqlConnection(connStr);
                conn.Open();
                ownsConnection = true;

                LogErrorHelper.RegistrarLog(
                       "POLIZAS",
                       "SIN_FOLIO",
                       $"Existe coneccion",
                       nivel: "DEBUG"
                   );
            }

            if (tx == null)
            {
                tx = conn.BeginTransaction();
                ownsTransaction = true;

                LogErrorHelper.RegistrarLog(
                       "POLIZAS",
                       "SIN_FOLIO",
                       $"Existe transaccion",
                       nivel: "DEBUG"
                   );
            }

            try
            {
                foreach (var poliza in polizas)
                {
                    using (var cmd = new NpgsqlCommand(
                        "SELECT * FROM registrar_poliza(@usuario, @encabezado, @datos::jsonb, @empresa_id, @tiene_discrepancia, @encabezado_disc);",
                        conn))
                    {
                        cmd.Transaction = tx;

                        cmd.Parameters.AddWithValue("usuario", usuario);
                        cmd.Parameters.Add("encabezado", NpgsqlTypes.NpgsqlDbType.Integer)
                            .Value = (object)encabezado ?? DBNull.Value;

                        var polizaJson = JsonConvert.SerializeObject(poliza);
                        cmd.Parameters.AddWithValue("datos", NpgsqlTypes.NpgsqlDbType.Jsonb, polizaJson);
                        cmd.Parameters.AddWithValue("empresa_id", GetEmpresaId(usuario));
                        cmd.Parameters.AddWithValue("tiene_discrepancia", tiene_discrepancia);
                        cmd.Parameters.AddWithValue("encabezado_disc", id_encabezado_disc ?? (object)DBNull.Value);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new Exception("No se devolvió ningún resultado desde registrar_poliza().");

                            resultados.Add((reader.GetInt32(0), reader.GetGuid(1)));
                        }
                    }
                }

                if (ownsTransaction)
                {
                    tx.Commit();

                    LogErrorHelper.RegistrarLog(
                       "POLIZAS",
                       "SIN_FOLIO",
                       $"Commit desde funcion C#",
                       nivel: "DEBUG"
                   );
                }

                LogErrorHelper.RegistrarLog(
                       "POLIZAS",
                       "SIN_FOLIO",
                       $"Exito",
                       nivel: "DEBUG"
                   );

                return resultados;


            }
            catch
            {
                if (ownsTransaction)
                    tx.Rollback();
                throw;
            }
            finally
            {
                if (ownsConnection)
                    conn.Close();
            }
        }

        /// <summary>
        /// Arma los datos de la póliza de un documento.
        ///
        /// <paramref name="cuentas_banco"/> acepta el código de una sola cuenta —hay conversión
        /// implícita desde string, así que las llamadas de siempre no cambian— o una lista de
        /// CuentaBancoPoliza cuando el documento se cobra o se paga con varias cuentas. En ese
        /// caso cada cuenta debe traer su importe y la póliza sale con un asiento de banco por
        /// cada una.
        /// </summary>
        public List<PolizaData> GenerarDatosPoliza(int id_encabezado, CuentasBancoPoliza cuentas_banco = null, List<int> facturas_pagadas = null, NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            try
            {
                LogErrorHelper.RegistrarLog(
                        "POLIZAS",
                        "GenerarDatosPoliza",
                        "Entro a funcion de obtener los datos para la poliza",
                        nivel: "DEBUG"
                    );

                var parameters = new Dictionary<string, object>();
                parameters.Add("id", id_encabezado);

                string query = "SELECT em.suc, em.gen, em.nat, EXTRACT(YEAR FROM em.fch)::text AS anio, em.fol_doc, em.nro_gpo_doc, " +
                    "   em.nro_tp_doc, em.cli_prov, em.iva, em.imp, em.coment_aut, em.usr1, em.id_encabezado, em.encabezados_padre, " +
                    "   em.folio || CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                    "   u.nombre || ' ' || u.apellido AS responsable, em.fch, em.uuid, em.refe, em.centro_costos, em.flete, " +
                    "   em.tipo_proceso, em.dto AS descuento, em.estatus_id, em.sub, em.id_cartera, em.ccy " +
                    "FROM encabezadomov em " +
                    "LEFT JOIN partidasdoc p ON p.encabezado_id = em.id_encabezado " +
                    "LEFT JOIN usuarios u ON u.nombreusuario = em.usr_doc " +
                    "WHERE em.id_encabezado = @id " +
                    "GROUP BY em.suc, em.gen, em.nat, em.fch, em.fol_doc, em.nro_gpo_doc, em.nro_tp_doc, em.cli_prov, em.iva, " +
                    "em.imp, em.coment_aut, em.usr1, em.id_encabezado, em.encabezados_padre, em.variacion, u.nombre, u.apellido, " +
                    "em.uuid, em.refe, em.centro_costos, em.flete, em.tipo_proceso, em.estatus_id";
                var result = RunQuery(query, parameters, false, conn, tx);

                if (result.Count == 0)
                {
                    LogErrorHelper.RegistrarLog(
                        "GenerarDatosPoliza",
                        "Obtener documento",
                        "No se encontro el documento",
                        nivel: "ERROR"
                    );
                    throw new Exception("No se encontro el encabezado.");
                }

                var enca = result[0];
                var encabezado = new DocumentoEncabezado();
                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.Gen = enca["gen"].ToString();
                encabezado.Nat = enca["nat"].ToString();
                encabezado.Anio = Convert.ToInt32(enca["anio"]);
                encabezado.Fol = enca["fol_doc"].ToString();
                encabezado.Folio = enca["folio"].ToString();
                encabezado.IdDoc = Convert.ToInt32(enca["id_encabezado"]);
                encabezado.TipoPoceso = enca["tipo_proceso"]?.ToString();
                encabezado.CentroCostos = Convert.ToInt32(enca["centro_costos"]);
                encabezado.Ref = Convert.ToInt32(enca["refe"]);
                encabezado.CliProv = enca["cli_prov"].ToString();
                encabezado.Imp = Convert.ToDecimal(enca["imp"]);
                encabezado.Estatus = Convert.ToInt32(enca["estatus_id"]);
                encabezado.EncabezadoPadre = Convert.ToInt32(enca["encabezados_padre"]);
                encabezado.Sub = Convert.ToDecimal(enca["sub"]);
                encabezado.IdCartera = Convert.ToInt32(enca["id_cartera"]);
                encabezado.Dto = Convert.ToDecimal(enca["descuento"]);
                encabezado.Ccy = string.IsNullOrWhiteSpace(Convert.ToString(enca["ccy"])) ? "PESOS" : enca["ccy"].ToString();

                query = "SELECT io.subtotal, io.importe, io.imp_variable, ci.cve_impuesto, ci.es_retencion, ci.id_impuesto " +
                    "FROM imp_oc io " +
                    "INNER JOIN cat_impuestos ci ON ci.id_impuesto = io.impuesto_id " +
                    "INNER JOIN encabezadomov e ON e.id_encabezado = io.encabezado_id " +
                    "WHERE e.id_encabezado = @id";
                var imp = RunQuery(query, parameters, false, conn, tx);

                var impuestos = new List<Impuesto>();
                foreach (var fila in imp)
                {
                    var impuesto = new Impuesto
                    {
                        Subtotal = Convert.ToDecimal(fila["subtotal"]),
                        Importe = Convert.ToDecimal(fila["importe"]),
                        ImpVariable = Convert.ToDecimal(fila["imp_variable"]),
                        Clave = fila["cve_impuesto"].ToString(),
                        EsRetencion = Convert.ToBoolean(fila["es_retencion"]),
                        IdImpuesto = Convert.ToInt32(fila["id_impuesto"])
                    };
                    impuestos.Add(impuesto);
                }

                if (string.IsNullOrEmpty(enca["tipo_proceso"]?.ToString()))
                {
                    LogErrorHelper.RegistrarLog(
                        "GenerarDatosPoliza",
                        "Tipo Proceso",
                        "El documento no tiene un tipo de proceso",
                        nivel: "Error"
                    );
                    throw new Exception("El tipo de proceso no puede ser nulo.");
                }

                query = "WITH RECURSIVE documentos AS ( " +
                    "   SELECT id_encabezado, encabezados_padre, nat, 0 AS nivel " +
                    "   FROM encabezadomov " +
                    "   WHERE id_encabezado = @id " +
                    "" +
                    "   UNION ALL " +
                    "" +
                    "   SELECT e.id_encabezado, e.encabezados_padre, e.nat, d.nivel + 1 " +
                    "   FROM encabezadomov e " +
                    "   INNER JOIN documentos d ON e.id_encabezado = d.encabezados_padre " +
                    "   WHERE d.encabezados_padre <> 0" +
                    ") " +
                    "SELECT id_encabezado " +
                    "FROM documentos " +
                    "WHERE nat = 'VIREM' " +
                    "ORDER BY nivel DESC";
                List<int> remisionesIds = RunQuery(query, parameters, false, conn, tx).Select(x => Convert.ToInt32(x["id_encabezado"])).ToList();

                query = "SELECT producto_id, cant_ud FROM partidasdoc WHERE encabezado_id = @id";
                var partidas = RunQuery(query, parameters, false, conn, tx);

                parameters = new Dictionary<string, object>();
                query = "SELECT rc.producto_id, ABS(SUM(rc.cantidad * rc.costo_unitario)) / ABS(SUM(rc.cantidad)) AS costo_promedio " +
                    "FROM registro_compras rc " +
                    "WHERE rc.encabezado_venta = ANY(@remisiones) AND NOT rc.revertido " +
                    "GROUP BY rc.producto_id;";
                parameters.Add("remisiones", remisionesIds.ToArray());
                var costosPromedio = RunQuery(query, parameters, false, conn, tx);

                decimal costo = 0;

                foreach (var partida in partidas)
                {
                    int productoId = Convert.ToInt32(partida["producto_id"]);
                    decimal cantidad = Convert.ToDecimal(partida["cant_ud"]);

                    var producto = costosPromedio.FirstOrDefault(x =>
                        Convert.ToInt32(x["producto_id"]) == productoId);

                    if (producto == null)
                        continue;

                    decimal costoPromedio = Convert.ToDecimal(producto["costo_promedio"]);

                    costo += cantidad * costoPromedio;
                }

                if (facturas_pagadas == null)
                {
                    facturas_pagadas = new List<int>();
                }

                List<DocumentoEncabezado> facturas = new List<DocumentoEncabezado>();
                foreach (int i in facturas_pagadas)
                {
                    parameters = new Dictionary<string, object>();
                    query = "SELECT e.imp, e.folio, cc.id_cartera_cliente " +
                        "FROM cartera_clientes cc " +
                        "INNER JOIN encabezadomov e ON e.id_encabezado = cc.encabezado_id " +
                        "WHERE id_cartera_cliente = @factura";
                    parameters.Add("factura", i);
                    var fac = RunQuery(query, parameters, false, conn, tx)[0];

                    facturas.Add(new DocumentoEncabezado
                    {
                        Folio = fac["folio"].ToString(),
                        Imp = GetDecimal(fac["imp"]),
                        IdCartera = GetInt(fac["id_cartera_cliente"])
                    });
                }

                var factory = new PolizaConfigFactory();
                var polizaObj = factory.Generar(encabezado, impuestos, costo, cuentas_banco, facturas, conn, tx);

                if (polizaObj == null)
                    throw new Exception("No se encontró handler para este tipo de proceso.");

                return polizaObj;
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(
                        "Error general",
                        "GenerarDatosPoliza",
                        $"Error general en GenerarDatosPoliza {ex.Message}",
                        nivel: "ERROR"
                    );
                throw new Exception(ex.Message);
            }
        }

        public Dictionary<string, object> ToDictionary(object obj)
        {
            var dict = new Dictionary<string, object>();

            foreach (var prop in obj.GetType().GetProperties())
            {
                dict[prop.Name] = prop.GetValue(obj);
            }

            return dict;
        }
        #endregion
        //    public string EncryptString(string key, string plainText)
        //    {
        //        var data = Encoding.Default.GetBytes(plainText);
        //        var pwd = !string.IsNullOrEmpty(key)
        //                   ? Encoding.Default.GetBytes(key)
        //                   : Array.Empty<byte>();
        //        var cipher = ProtectedData.Protect(data, pwd, DataProtectionScope.LocalMachine);
        //        return Convert.ToBase64String(cipher);
        //    }

        //    public string DecryptString(string key, string cipherText)
        //    {
        //        var cipher = Convert.FromBase64String(cipherText);
        //        var pwd = !string.IsNullOrEmpty(key)
        //                     ? Encoding.Default.GetBytes(key)
        //                     : Array.Empty<byte>();
        //        var data = ProtectedData.Unprotect(cipher, pwd, DataProtectionScope.LocalMachine);
        //        return Encoding.Default.GetString(data);
        //    }

        #region funciones marranas
        public static string GetSetting(string settingName, string dbConnection = "ERP_SRS")
        {
            try
            {
                var util = new Utilities(true);
                string connStr = util._configuration.GetConnectionString(dbConnection);

                using (var conn = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT setting_value FROM settings WHERE setting_name = @setting_name";

                    using (var command = new NpgsqlCommand(query, conn))
                    {
                        command.Parameters.AddWithValue("setting_name", settingName);

                        conn.Open();
                        return command.ExecuteScalar() != null ? command.ExecuteScalar().ToString() : "";
                    }
                }
            }
            catch
            {
                return "";
            }
        }

        public int? GetInt(object value, int? defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is string s && string.IsNullOrWhiteSpace(s))
                return defaultValue;

            if (int.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }

        public DateTime? GetDate(object value, DateTime? defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is string s && string.IsNullOrWhiteSpace(s))
                return defaultValue;

            if (DateTime.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }

        public string GetString(object value, string defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            var str = value.ToString();
            return string.IsNullOrWhiteSpace(str) ? defaultValue : str.Trim();
        }

        public decimal? GetDecimal(object value, decimal? defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is string s && string.IsNullOrWhiteSpace(s))
                return defaultValue;

            if (decimal.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }

        public bool GetBool(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            value = value.Trim().ToLower();

            return value == "1" || value == "true" || value == "on";
        }

        // ─── Existencias por tipo de almacén ─────────────────────────────────────────
        // La cadena tarima_productos → cattarimas → catniveles → catcolumnas → catracks
        // → catalmacenes → catsucursales estaba repetida en cada endpoint que mostraba
        // existencias, y cada copia filtraba distinto (unas por 'Stock', otras por
        // 'Stock OR Modula', otras sin filtro). Estos helpers son la única fuente.

        /// <summary>
        /// Subconsulta escalar con la existencia de un producto en los almacenes de un
        /// tipo dado ('Stock' o 'Modula') dentro de una sucursal.
        /// </summary>
        /// <param name="productoIdExpr">Expresión SQL que resuelve el id_catproductos (ej. "cp.id_catproductos").</param>
        /// <param name="tipoAlmacen">'Stock' o 'Modula'. Valor literal, no viene del usuario.</param>
        /// <param name="paramSucursal">Nombre del parámetro de sucursal ya presente en la consulta.</param>
        public static string SqlExistencia(string productoIdExpr, string tipoAlmacen, string paramSucursal = "@sucursal")
        {
            if (tipoAlmacen != "Stock" && tipoAlmacen != "Modula")
                throw new ArgumentException($"Tipo de almacén no soportado: {tipoAlmacen}", nameof(tipoAlmacen));

            return SqlExistenciaConTipoExpr(productoIdExpr, $"'{tipoAlmacen}'", paramSucursal);
        }

        /// <summary>
        /// Variante de <see cref="SqlExistencia"/> donde el tipo de almacén es una expresión
        /// SQL (p. ej. un CASE que decide 'Modula' vs 'Stock' según el documento). El tipo
        /// debe ser SQL de confianza construido en el servidor, NUNCA entrada del usuario.
        /// </summary>
        public static string SqlExistenciaConTipoExpr(string productoIdExpr, string tipoExpr, string paramSucursal = "@sucursal")
        {
            // tarima_productos es la AUTORIDAD del total (incluye tubos: los cortes en
            // tarima_productos_cortes son un sub-desglose del bulk, no material aparte). El
            // material sólo se descuenta de tarima_productos al remisionar (RegistrarMovimiento),
            // así que la existencia = suma del bulk, sin sumar cortes (eso duplicaría).
            return $@"COALESCE((
                SELECT SUM(tp.cantidad)
                FROM tarima_productos tp
                INNER JOIN cattarimas ct    ON ct.id_tarima   = tp.tarima_id
                INNER JOIN catniveles cn    ON cn.id_nivel    = ct.nivel_id
                INNER JOIN catcolumnas col  ON col.id_columna = cn.columna_id
                INNER JOIN catracks cr      ON cr.id_rack     = col.rack_id
                INNER JOIN catalmacenes ca  ON ca.id_almacen  = cr.almacen_id
                INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                WHERE tp.producto_id = {productoIdExpr}
                  AND cs.id_sucursal = {paramSucursal}
                  AND ca.tipo        = {tipoExpr}
                  AND tp.cantidad    > 0
            ), 0)";
        }

        /// <summary>
        /// Subconsulta que devuelve el desglose por almacén (JSON como texto) de un
        /// producto para un tipo de almacén dentro de una sucursal.
        /// </summary>
        public static string SqlExistenciaPorAlmacen(string productoIdExpr, string tipoAlmacen, string paramSucursal = "@sucursal")
        {
            if (tipoAlmacen != "Stock" && tipoAlmacen != "Modula")
                throw new ArgumentException($"Tipo de almacén no soportado: {tipoAlmacen}", nameof(tipoAlmacen));

            // Desglose por almacén desde tarima_productos (la autoridad del total). Los cortes
            // de tubo son un sub-desglose del bulk, así que NO se suman aquí (duplicarían).
            return $@"COALESCE((
                SELECT json_agg(json_build_object(
                    'cve_almacen', x.cve_almacen, 'n_almacen', x.descripcion,
                    'stock', x.stock, 'tipo', '{tipoAlmacen}')
                    ORDER BY x.stock DESC)
                FROM (
                    SELECT ca.cve_almacen, ca.descripcion, SUM(tp.cantidad) AS stock
                    FROM tarima_productos tp
                    INNER JOIN cattarimas ct    ON ct.id_tarima   = tp.tarima_id
                    INNER JOIN catniveles cn    ON cn.id_nivel    = ct.nivel_id
                    INNER JOIN catcolumnas col  ON col.id_columna = cn.columna_id
                    INNER JOIN catracks cr      ON cr.id_rack     = col.rack_id
                    INNER JOIN catalmacenes ca  ON ca.id_almacen  = cr.almacen_id
                    INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id
                    WHERE tp.producto_id = {productoIdExpr}
                      AND cs.id_sucursal = {paramSucursal}
                      AND ca.tipo        = '{tipoAlmacen}'
                      AND tp.cantidad    > 0
                    GROUP BY ca.cve_almacen, ca.descripcion
                ) x
            ), '[]'::json)::text";
        }

        public static bool DoesUserHasRight(string userName, string right, string dbConnection = "ERP_SRS")
        {
            try
            {
                var util = new Utilities(true);
                string connStr = util._configuration.GetConnectionString("ERP_SRS");

                using (var connection = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT 1 " +
                        "FROM permisos_usuario pu " +
                        "INNER JOIN permisos p ON p.id_permiso = pu.permiso_id " +
                        "WHERE pu.usuario_id = @userName " +
                        "   AND p.nombre IN ('super_admin', 'sistemas', @rightName) " +
                        "LIMIT 1";
                    using (var command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("userName", GetUserId(userName));
                        command.Parameters.AddWithValue("rightName", right);

                        connection.Open();
                        var result = command.ExecuteScalar();

                        return result != null;
                    }
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public static bool DoesUserHasRight(string userName, string[] rights, string dbConnection = "ERP_SRS")
        {
            try
            {
                var util = new Utilities(true);
                string connStr = util._configuration.GetConnectionString("ERP_SRS");

                using (var conn = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT 1 " +
                        "FROM permisos_usuario pu " +
                        "INNER JOIN permisos p ON p.id_permiso = pu.permiso_id " +
                        "WHERE pu.usuario_id = @userId " +
                        "   AND (p.nombre = 'Super super_admin' OR p.nombre = 'sistemas' OR p.nombre = ANY(@rights)) " +
                        "LIMIT 1";

                    using (var command = new NpgsqlCommand(query, conn))
                    {
                        command.Parameters.AddWithValue("userId", GetUserId(userName));
                        command.Parameters.AddWithValue("rights", rights);

                        conn.Open();
                        return command.ExecuteScalar() != null;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public bool DoesUserHasRole(string userName, List<string> roles, string connectionStringName = "ERP_SRS")
        {
            string connectionString = _configuration.GetConnectionString(connectionStringName);

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception($"No se encontró la cadena de conexión '{connectionStringName}' en el archivo web.config.");
            }

            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT getuserrole(@userName);";

                using (var command = new NpgsqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("userName", userName);

                    object result = command.ExecuteScalar();
                    string userRole = result?.ToString();

                    // Crear una lista base de roles permitidos
                    var allowedRoles = new List<string> { "Super Administrador", "Sistemas" };

                    // Agregar los roles recibidos como parámetro
                    allowedRoles.AddRange(roles);

                    return allowedRoles.Contains(userRole);
                }
            }
        }

        public static int GetUserId(string userName, string dbConnection = "ERP_SRS")
        {
            try
            {

                var util = new Utilities(true);

                if (string.IsNullOrWhiteSpace(userName))
                    userName = util.GetUser();

                string connStr = util._configuration.GetConnectionString("ERP_SRS");
                using (var connection = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT usuarioid FROM usuarios WHERE nombreusuario = @userName";
                    using (var command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("userName", userName);
                        connection.Open();
                        var result = command.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        public static string GetAreaName(string nombreusuario, string dbConnection = "ERP_SRS")
        {
            try
            {
                {
                    var utils = new Utilities(true);
                    string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                    using (var connection = new NpgsqlConnection(connStr))
                    {
                        string query = "SELECT a.nombre FROM usuarios u INNER JOIN areas a ON a.areaid = u.areaid WHERE u.nombreusuario = @nombreusuario";
                        using (var command = new NpgsqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("nombreusuario", nombreusuario);
                            connection.Open();
                            var result = command.ExecuteScalar();
                            return result != null ? result.ToString() : string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }

        public static string GetSucursalName(string userName, string dbConnection = "ERP_SRS")
        {
            try
            {
                var util = new Utilities(true);
                string connStr = util._configuration.GetConnectionString("ERP_SRS");
                using (var connection = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT c.descripcion " +
                        "FROM usuarios u " +
                        "INNER JOIN catsucursales c ON c.id_sucursal = u.sucursal_id " +
                        "WHERE u.nombreusuario = @userName";
                    using (var command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("userName", userName);
                        connection.Open();
                        var result = command.ExecuteScalar();
                        return result != null ? result.ToString() : string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }

        public static string GetEmpresaName(string userName, string dbConnection = "ERP_SRS")
        {
            try
            {
                var util = new Utilities(true);
                string connStr = util._configuration.GetConnectionString("ERP_SRS");
                using (var connection = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT e.nombre " +
                        "FROM usuarios u " +
                        "INNER JOIN empresas e ON e.empresaid = u.empresaid " +
                        "WHERE u.nombreusuario = @userName";
                    using (var command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("userName", userName);
                        connection.Open();
                        var result = command.ExecuteScalar();
                        return result != null ? result.ToString() : string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }

        private int? GetEmpresaIdInternal(string whereClause, string paramName, object paramValue, string dbConnection)
        {
            try
            {

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString(dbConnection);

                using (var connection = new NpgsqlConnection(connStr))
                {

                    string query = "SELECT e.empresaid " +
                        "FROM usuarios u " +
                        "INNER JOIN empresas e ON e.empresaid = u.empresaid " +
                        $"WHERE {whereClause}";

                    using (var command = new NpgsqlCommand(query, connection))
                    {

                        command.Parameters.AddWithValue(paramName, paramValue);

                        connection.Open();
                        var result = command.ExecuteScalar();

                        if (result == null || result == DBNull.Value)
                            return null;

                        return Convert.ToInt32(result);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public int? GetEmpresaId(string userName, string dbConnection = "ERP_SRS")
            => GetEmpresaIdInternal("u.nombreusuario = @userName", "userName", userName, dbConnection);

        public int? GetEmpresaId(int userId, string dbConnection = "ERP_SRS")
            => GetEmpresaIdInternal("u.usuarioid = @userId", "userId", userId, dbConnection);


        public int GetAreaID(string nombreusuario, string dbConnection = "ERP_SRS")
        {
            try
            {
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                using (var connection = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT a.areaid FROM usuarios u INNER JOIN areas a ON a.areaid = u.areaid WHERE u.nombreusuario = @nombreusuario";
                    using (var command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("nombreusuario", nombreusuario);
                        connection.Open();
                        var result = command.ExecuteScalar();
                        return result != null ? (int)result : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                return 0;
            }
        }
        public string ToAlphabet(int number)
        {
            // Si number = 1 → A, number = 2 → B … number = 27 → AA
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            string result = "";

            while (number > 0)
            {
                number--; // Ajuste porque A = 1, no 0
                result = letters[number % 26] + result;
                number /= 26;
            }

            return result;
        }

        public (bool result, string message) UploadFormFileToPath(string path, IFormFile file, string fileId, string fileExtension, bool deleteAllExtensions = false)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return (false, "Archivo vacío o nulo.");

                string extensionsToDelete = deleteAllExtensions ? ".*" : fileExtension;
                string rootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", path);

                if (rootPath == null)
                    return (false, "No se pudo mapear la ruta base.");

                if (!Directory.Exists(rootPath))
                    Directory.CreateDirectory(rootPath);

                // Limpieza
                string[] fileNames = Directory.GetFiles(rootPath, fileId + extensionsToDelete, SearchOption.TopDirectoryOnly);
                foreach (string oldFile in fileNames)
                {
                    try { System.IO.File.Delete(oldFile); } catch { /* meh */ }
                }

                // Sanitizar nombre por si acaso
                string safeFileName = Path.GetFileName(fileId) + fileExtension;
                string uploadPath = Path.Combine(rootPath, safeFileName);

                using (var stream = new FileStream(uploadPath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, "Error al subir archivo: " + ex.Message);
            }
        }

        public (bool result, string message) DeleteFileFromPath(string fullPath, string fileId, string fileExtension = ".*", bool deleteAllExtensions = false)
        {
            try
            {
                string extensionsToDelete = deleteAllExtensions ? ".*" : fileExtension;

                // Si es ruta física, usarla directamente
                string rootPath = fullPath;
                if (!Directory.Exists(rootPath))
                    rootPath = Path.GetDirectoryName(fullPath); // Si mandaron archivo, obtén carpeta

                if (!Directory.Exists(rootPath))
                    return (false, "El directorio no existe.");

                string[] fileNames = Directory.GetFiles(rootPath, fileId + extensionsToDelete, SearchOption.TopDirectoryOnly);
                if (fileNames.Length == 0)
                    return (false, "No se encontraron archivos para borrar.");

                foreach (string _fileName in fileNames)
                    System.IO.File.Delete(_fileName);

                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Notificación interna en vivo hacia las pestañas abiertas de un usuario.
        /// Corre sobre SignalR (Hubs/NotificacionesHub.cs); antes usaba Pusher, que
        /// cobra por request. La firma no cambió para no tocar los ~29 lugares que la
        /// llaman; userName ya no rutea nada, viaja en el sobre por si el cliente
        /// quiere mostrarlo.
        /// </summary>
        public async Task SendNotificationInterno(string userName, string userEmail, object payload)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return;

            // Se resuelve del request cuando lo hay; el estático cubre el resto (jobs de
            // Hangfire y las llamadas "_ = ..." que sobreviven al request). Ojo: esto se
            // lee ANTES del primer await, mientras HttpContext sigue vivo. IHubContext es
            // singleton, así que la referencia sigue siendo válida después.
            var hub = HttpContext?.RequestServices?.GetService<IHubContext<NotificacionesHub>>()
                      ?? NotificacionesHubAccessor.Hub;

            if (hub == null)
                return;

            try
            {
                await hub.Clients
                    .Group(NotificacionesHub.GrupoUsuario(userEmail))
                    .SendAsync(NotificacionesHub.EventoNotificacionInterna, payload);
            }
            catch
            {
                // Todas las llamadas son "_ = SendNotificationInterno(...)": nadie observa
                // la tarea, así que una excepción aquí quedaría sin manejar. Un aviso
                // perdido no debe tumbar el proceso que lo disparó.
            }
        }

        /// <summary>
        /// Folio visible del documento (VSUC-VSFAC-26-19). Acepta conn/tx porque casi
        /// siempre se pide para un encabezado recién creado: con su propia conexión no
        /// veía la fila aún sin commitear y devolvía cadena vacía, con lo que el CFDI
        /// salía sin Folio y el PAC lo rechazaba con
        /// «The 'Folio' attribute is invalid - The value '' ... Pattern constraint failed».
        /// </summary>
        public string GetDocumentFolio(int idEncabezado, string dbConnection = "ERP_SRS",
            NpgsqlConnection conn = null, NpgsqlTransaction tx = null)
        {
            bool ownsConnection = false;

            try
            {
                if (conn == null)
                {
                    conn = new NpgsqlConnection(ObtenerCadenaConexion(dbConnection));
                    conn.Open();
                    ownsConnection = true;
                }

                string query = "SELECT em.folio || " +
                    "   CASE WHEN em.variacion > 0  " +
                    "      THEN '-' || num_to_letters(em.variacion)  " +
                    "   ELSE '' END AS folio " +
                    "FROM encabezadomov em " +
                    "WHERE em.id_encabezado = @id";

                using (var command = new NpgsqlCommand(query, conn))
                {
                    if (tx != null)
                        command.Transaction = tx;

                    command.Parameters.AddWithValue("id", idEncabezado);
                    var result = command.ExecuteScalar();
                    return result != null ? result.ToString() : string.Empty;
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                if (ownsConnection)
                    conn.Close();
            }
        }


        public static void RegistrarAccion(string usuario, string accion, string modulo, string descripcion, string ip, string userAgent, string detalles = null, string dbConnection = "ERP_SRS")
        {
            try
            {
                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString(dbConnection);

                using (var connection = new NpgsqlConnection(connStr))
                {
                    connection.Open();

                    string query = @"
                INSERT INTO historial_usuarios 
                (usuario, accion, modulo, descripcion, ip_cliente, user_agent, detalles)
                VALUES (@usuario, @accion, @modulo, @descripcion, @ip, @userAgent, @detalles);";

                    using (var cmd = new NpgsqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@usuario", usuario ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@accion", accion ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@modulo", modulo ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@descripcion", descripcion ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@ip", ip ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@userAgent", userAgent ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@detalles", (object)detalles ?? DBNull.Value);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                // Silenciar errores si es necesario
            }
        }

        public List<int> ObtenerIdsPermisosDeUsuario(int usuarioId)
        {
            var permisos = new List<int>();

            string query = @"
                SELECT permiso_id 
                FROM permisos_usuario 
                WHERE usuario_id = @usuarioId;
            ";

            var parametros = new Dictionary<string, object>
            {
                { "usuarioId", usuarioId }
            };

            var resultado = RunQuery(query, parametros);

            foreach (var fila in resultado)
            {
                if (fila["permiso_id"] != DBNull.Value)
                {
                    permisos.Add(Convert.ToInt32(fila["permiso_id"]));
                }
            }

            return permisos;
        }

        public List<AccionRapida> ObtenerAccionesRapidas(int usuarioId)
        {
            var acciones = new List<AccionRapida>();
            var permisos = ObtenerIdsPermisosDeUsuario(usuarioId);

            string sql = @"
                         SELECT a.id, a.nombre, a.descripcion, a.icono, a.ruta, a.permiso_id
                         FROM acciones_usuario au
                         INNER JOIN acciones_rapidas a ON a.id = au.accion_id
                         WHERE au.usuario_id = @usuarioId AND a.activo = true
                         ORDER BY au.orden";

            var parametros = new Dictionary<string, object> { { "usuarioId", usuarioId } };
            var registros = RunQuery(sql, parametros);

            foreach (var fila in registros)
            {
                int? permisoId = fila["permiso_id"] as int?;
                if (permisoId == null || permisos.Contains((int)permisoId))
                {
                    acciones.Add(new AccionRapida
                    {
                        Id = (int)fila["id"],
                        Nombre = fila["nombre"].ToString(),
                        Descripcion = fila["descripcion"].ToString(),
                        Icono = fila["icono"].ToString(),
                        Ruta = fila["ruta"].ToString(),
                        PermisoId = permisoId
                    });
                }
            }

            return acciones;
        }

        public List<AccionRapida> ObtenerAccionesDisponibles(int usuarioId)
        {
            var permisos = ObtenerIdsPermisosDeUsuario(usuarioId);

            string sql = @"
                SELECT id, nombre, descripcion, icono, ruta, permiso_id
                FROM acciones_rapidas
                WHERE activo = true;
            ";

            var resultado = RunQuery(sql);

            var disponibles = new List<AccionRapida>();
            foreach (var fila in resultado)
            {
                int? permisoId = fila["permiso_id"] as int?;
                if (permisoId == null || permisos.Contains((int)permisoId))
                {
                    disponibles.Add(new AccionRapida
                    {
                        Id = (int)fila["id"],
                        Nombre = fila["nombre"].ToString(),
                        Descripcion = fila["descripcion"].ToString(),
                        Icono = fila["icono"].ToString(),
                        Ruta = fila["ruta"].ToString(),
                        PermisoId = permisoId
                    });
                }
            }

            return disponibles;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // 🔴 SI NO ESTÁ AUTENTICADO → NO HAGAS NADA
            if (!User.Identity.IsAuthenticated)
            {
                base.OnActionExecuting(filterContext);
                return;
            }

            var usuarioId = GetUserId(User.Identity.Name);
            ViewBag.AccionesDisponibles = ObtenerAccionesDisponibles(usuarioId);
            ViewBag.AccionesUsuario = ObtenerAccionesRapidas(usuarioId);

            base.OnActionExecuting(filterContext);
        }


        public string GenerateFolio(int id, string dbConnection = "ERP_SRS")
        {
            try
            {
                string connStr = _configuration.GetConnectionString(dbConnection);
                using (var connection = new NpgsqlConnection(connStr))
                {
                    string query = "SELECT em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || em.fol_doc AS folio FROM encabezadomov em WHERE em.id_encabezado = @id";
                    using (var command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("id", id);
                        connection.Open();
                        var result = command.ExecuteScalar();
                        return result != null ? result.ToString() : string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }

        public DatosXML LeerDatosDesdeXML(Stream xmlStream)
        {
            var doc = new XmlDocument();
            doc.Load(xmlStream);  // Cargar directamente desde el stream

            // Obtener namespace desde el root
            string xmlnsCfdi = doc.DocumentElement.GetNamespaceOfPrefix("cfdi");
            if (string.IsNullOrEmpty(xmlnsCfdi))
                xmlnsCfdi = doc.DocumentElement.NamespaceURI;

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("cfdi", xmlnsCfdi);

            // Extraer nodos principales
            XmlNode comprobante = doc.SelectSingleNode("//cfdi:Comprobante", ns);
            XmlNode emisor = doc.SelectSingleNode("//cfdi:Comprobante/cfdi:Emisor", ns);
            XmlNode receptor = doc.SelectSingleNode("//cfdi:Comprobante/cfdi:Receptor", ns);

            var datos = new DatosXML
            {
                Version = comprobante?.Attributes["Version"]?.Value ?? comprobante?.Attributes["version"]?.Value,
                Serie = comprobante?.Attributes["Serie"]?.Value,
                Folio = comprobante?.Attributes["Folio"]?.Value,
                Fecha = comprobante?.Attributes["Fecha"]?.Value,
                SubTotal = comprobante?.Attributes["SubTotal"]?.Value,
                Total = comprobante?.Attributes["Total"]?.Value,
                Moneda = comprobante?.Attributes["Moneda"]?.Value,
                TipoCambio = comprobante?.Attributes["TipoCambio"]?.Value,
                EmisorRFC = emisor?.Attributes["Rfc"]?.Value,
                EmisorNombre = emisor?.Attributes["Nombre"]?.Value,
                ReceptorRFC = receptor?.Attributes["Rfc"]?.Value,
                ReceptorNombre = receptor?.Attributes["Nombre"]?.Value
            };

            return datos;
        }

        //public IActionResult RegenerarPDFDesdeXML(string uuid)
        //{
        //    if (string.IsNullOrWhiteSpace(uuid))
        //    {
        //        return Content("UUID no proporcionado.");
        //    }

        //    try
        //    {
        //        // 1. Buscar el archivo XML timbrado
        //        string rutaXML = Path.Combine(Directory.GetCurrentDirectory(), "Facturacion", "xml_timbrados", $"{uuid}.xml");

        //        if (!System.IO.File.Exists(rutaXML))
        //        {
        //            return Content($"No se encontró el XML timbrado: {uuid}.xml");
        //        }

        //        // 2. Leer y parsear el XML
        //        string xmlContent = System.IO.File.ReadAllText(rutaXML, Encoding.UTF8);
        //        Factura factura = ParsearXMLTimbrado(xmlContent);

        //        // 3. Renderizar el header dinámico
        //        string headerHtml = RenderViewToString("Header", factura);
        //        string headerPath = Path.Combine(Directory.GetCurrentDirectory(), "content", "pdf", "header.html");
        //        System.IO.File.WriteAllText(headerPath, headerHtml, Encoding.UTF8);

        //        // 4. Generar el PDF usando Rotativa
        //        var pdf = new Rotativa.ViewAsPdf("FacturaPdf", factura)
        //        {
        //            PageSize = Rotativa.Options.Size.A4,
        //            PageMargins = new Rotativa.Options.Margins(45, 10, 20, 10),
        //            FileName = $"Factura_{factura.UUID}.pdf",
        //            CustomSwitches = $"--encoding utf-8 --header-html \"{headerPath}\" --header-spacing 5 --footer-center \"Página [page] de [toPage]\" --footer-line --footer-font-size 10"
        //        };

        //        // 5. Guardar el PDF en disco
        //        string rutaPDF = Path.Combine(Directory.GetCurrentDirectory(), "Facturacion", "facturas", $"{factura.UUID}.pdf");
        //        Response.ContentEncoding = System.Text.Encoding.UTF8;
        //        byte[] pdfBytes = pdf.BuildFile(ControllerContext);
        //        System.IO.File.WriteAllBytes(rutaPDF, pdfBytes);

        //        return Content($"PDF regenerado correctamente en: {rutaPDF}");
        //    }
        //    catch (Exception ex)
        //    {
        //        return Content($"Error al regenerar PDF: {ex.Message}");
        //    }
        //}

        // Método para parsear el XML timbrado y reconstruir el objeto Factura
        // Método para parsear el XML timbrado y reconstruir el objeto Factura
        private Factura ParsearXMLTimbrado(string xmlContent)
        {
            XDocument doc = XDocument.Parse(xmlContent);
            XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";
            XNamespace tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

            var comprobante = doc.Root;
            var emisor = comprobante.Element(cfdi + "Emisor");
            var receptor = comprobante.Element(cfdi + "Receptor");
            var conceptos = comprobante.Element(cfdi + "Conceptos")?.Elements(cfdi + "Concepto");
            var impuestos = comprobante.Element(cfdi + "Impuestos");
            var timbre = comprobante.Element(cfdi + "Complemento")?.Element(tfd + "TimbreFiscalDigital");

            Factura factura = new Factura
            {
                // Datos del comprobante
                Serie = comprobante.Attribute("Serie")?.Value ?? "",
                Folio = comprobante.Attribute("Folio")?.Value ?? "",
                Fecha = DateTime.Parse(comprobante.Attribute("Fecha")?.Value ?? DateTime.Now.ToString()),
                IdTipoPago = comprobante.Attribute("FormaPago")?.Value ?? "",
                FormaPago = comprobante.Attribute("FormaPago")?.Value ?? "",
                Moneda = comprobante.Attribute("Moneda")?.Value ?? "MXN",
                TipoCambio = decimal.Parse(comprobante.Attribute("TipoCambio")?.Value ?? "1"),
                TipoDeComprobante = comprobante.Attribute("TipoDeComprobante")?.Value ?? "I",
                LugarExpedicion = comprobante.Attribute("LugarExpedicion")?.Value ?? "",
                CpE = comprobante.Attribute("LugarExpedicion")?.Value ?? "",
                Exportacion = comprobante.Attribute("Exportacion")?.Value ?? "01",

                // Datos del emisor
                RfcEmisor = emisor?.Attribute("Rfc")?.Value ?? "",
                RsoEmisor = emisor?.Attribute("Nombre")?.Value ?? "",
                Rege = emisor?.Attribute("RegimenFiscal")?.Value ?? "",
                RegFisE = emisor?.Attribute("RegimenFiscal")?.Value ?? "",

                // Datos del receptor
                RfcCliente = receptor?.Attribute("Rfc")?.Value ?? "",
                RsoCliente = receptor?.Attribute("Nombre")?.Value ?? "",
                CpR = receptor?.Attribute("DomicilioFiscalReceptor")?.Value ?? "",
                Regc = receptor?.Attribute("RegimenFiscalReceptor")?.Value ?? "",
                RegFisR = receptor?.Attribute("RegimenFiscalReceptor")?.Value ?? "",
                IdUsoCFDI = receptor?.Attribute("UsoCFDI")?.Value ?? "",
                UsoCFDI = receptor?.Attribute("UsoCFDI")?.Value ?? "",

                // Totales
                Subtotal = decimal.Parse(comprobante.Attribute("SubTotal")?.Value ?? "0"),
                Total = decimal.Parse(comprobante.Attribute("Total")?.Value ?? "0"),
                IVA = decimal.Parse(impuestos?.Attribute("TotalImpuestosTrasladados")?.Value ?? "0"),

                // Certificados y sellos
                NoCertificado = comprobante.Attribute("NoCertificado")?.Value ?? "",
                SelloCFDI = comprobante.Attribute("Sello")?.Value ?? "",
                Certificado = comprobante.Attribute("Certificado")?.Value ?? "",

                // Datos del timbre
                UUID = timbre?.Attribute("UUID")?.Value ?? "",
                FechaTimbrado = timbre?.Attribute("FechaTimbrado")?.Value ?? "",
                NoCertificadoSAT = timbre?.Attribute("NoCertificadoSAT")?.Value ?? "",
                SelloSAT = timbre?.Attribute("SelloSAT")?.Value ?? ""
            };

            // Construir DataTable de productos
            DataTable dtProductos = new DataTable();
            dtProductos.Columns.Add("numero", typeof(int));
            dtProductos.Columns.Add("claveProdServ", typeof(string));
            dtProductos.Columns.Add("noIdentificacion", typeof(string));
            dtProductos.Columns.Add("cantidad", typeof(double));
            dtProductos.Columns.Add("claveUnidad", typeof(string));
            dtProductos.Columns.Add("unidad", typeof(string));
            dtProductos.Columns.Add("descripcion", typeof(string));
            dtProductos.Columns.Add("precioUnit", typeof(double));
            dtProductos.Columns.Add("importe", typeof(double));
            dtProductos.Columns.Add("objetoImp", typeof(string));
            dtProductos.Columns.Add("cuentaPredial", typeof(string));
            dtProductos.Columns.Add("iva", typeof(double));

            int numeroConcepto = 1;
            if (conceptos != null)
            {
                foreach (var concepto in conceptos)
                {
                    double importe = double.Parse(concepto.Attribute("Importe")?.Value ?? "0");
                    string objetoImp = concepto.Attribute("ObjetoImp")?.Value ?? "02";

                    // Calcular IVA del concepto
                    double ivaConcepto = 0;
                    if (objetoImp == "02")
                    {
                        var traslado = concepto.Element(cfdi + "Impuestos")
                            ?.Element(cfdi + "Traslados")
                            ?.Element(cfdi + "Traslado");

                        if (traslado != null)
                        {
                            ivaConcepto = double.Parse(traslado.Attribute("Importe")?.Value ?? "0");
                        }
                    }

                    // Obtener cuenta predial
                    string cuentaPredial = concepto.Element(cfdi + "CuentaPredial")
                        ?.Attribute("Numero")?.Value ?? "";

                    DataRow row = dtProductos.NewRow();
                    row["numero"] = numeroConcepto++;
                    row["claveProdServ"] = concepto.Attribute("ClaveProdServ")?.Value ?? "";
                    row["noIdentificacion"] = concepto.Attribute("NoIdentificacion")?.Value ?? "";
                    row["cantidad"] = double.Parse(concepto.Attribute("Cantidad")?.Value ?? "1");
                    row["claveUnidad"] = concepto.Attribute("ClaveUnidad")?.Value ?? "";
                    row["unidad"] = concepto.Attribute("Unidad")?.Value ?? "";
                    row["descripcion"] = concepto.Attribute("Descripcion")?.Value ?? "";
                    row["precioUnit"] = double.Parse(concepto.Attribute("ValorUnitario")?.Value ?? "0");
                    row["importe"] = importe;
                    row["objetoImp"] = objetoImp;
                    row["cuentaPredial"] = cuentaPredial;
                    row["iva"] = ivaConcepto;

                    dtProductos.Rows.Add(row);
                }
            }

            factura.Tproductos = dtProductos;

            return factura;
        }

        //public string RenderViewToString(string viewName, object model)
        //{
        //    ViewData.Model = model;
        //    using (var sw = new StringWriter())
        //    {
        //        var viewResult = ViewEngines.Engines.FindPartialView(ControllerContext, viewName);
        //        var viewContext = new ViewContext(ControllerContext, viewResult.View, ViewData, TempData, sw);
        //        viewResult.View.Render(viewContext, sw);
        //        viewResult.ViewEngine.ReleaseView(ControllerContext, viewResult.View);
        //        return sw.GetStringBuilder().ToString();
        //    }
        //}

        public string BorradoFacturasIncorrectas(int idEncabezado, string tipo = "", string serie = "", string dbConnection = "ERP_SRS")
        {
            try
            {
                string connStr = _configuration.GetConnectionString(dbConnection);

                using (var connection = new NpgsqlConnection(connStr))
                {
                    connection.Open();

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 🔹 Si el tipo es "factura", eliminar las facturas y su detalle
                            if (tipo == "factura")
                            {
                                // 1️⃣ Obtener todas las facturas relacionadas al encabezado
                                List<int> facturas = new List<int>();
                                string selectFacturas = "SELECT id FROM factura WHERE encabezado_id = @idEncabezado;";

                                using (var selectCmd = new NpgsqlCommand(selectFacturas, connection, transaction))
                                {
                                    selectCmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                    using (var reader = selectCmd.ExecuteReader())
                                    {
                                        while (reader.Read())
                                            facturas.Add(reader.GetInt32(0));
                                    }
                                }

                                // 2️⃣ Eliminar partidas (dfactura)
                                if (facturas.Count > 0)
                                {
                                    string deleteDetalle = "DELETE FROM dfactura WHERE idfactura = @idFactura;";
                                    foreach (var idFactura in facturas)
                                    {
                                        using (var deleteCmd = new NpgsqlCommand(deleteDetalle, connection, transaction))
                                        {
                                            deleteCmd.Parameters.AddWithValue("idFactura", idFactura);
                                            deleteCmd.ExecuteNonQuery();
                                        }
                                    }

                                    // 3️⃣ Eliminar facturas principales
                                    string deleteFacturas = "DELETE FROM factura WHERE encabezado_id = @idEncabezado;";
                                    using (var cmdDeleteFact = new NpgsqlCommand(deleteFacturas, connection, transaction))
                                    {
                                        cmdDeleteFact.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                        cmdDeleteFact.ExecuteNonQuery();
                                    }

                                }
                                string deleteCarteras = "DELETE FROM cartera_clientes WHERE encabezado_id = @idEncabezado;";
                                //using (var cmdDeleteFact = new NpgsqlCommand(deleteCarteras, connection, transaction))
                                //{
                                //    cmdDeleteFact.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                //    cmdDeleteFact.ExecuteNonQuery();
                                //}
                            }

                            // 🔹 Si el tipo es "remision", revertir cantidades y borrar relaciones
                            if (tipo == "remision")
                            {
                                // 1️⃣ Eliminar movimientos de tarimas
                                string tarimasMov = "DELETE FROM tarimas_mov WHERE encabezado_id=@idEncabezado;";
                                using (var cmd = new NpgsqlCommand(tarimasMov, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                    cmd.ExecuteNonQuery();
                                }

                                // 2️⃣ Obtener compras afectadas por esta remisión
                                string selectCompras = @"
            SELECT id_registro_compra, cantidad, encabezado_compra
            FROM registro_compras
            WHERE encabezado_venta = @idEncabezado;
        ";

                                var comprasRelacionadas = new List<(int idRegistro, decimal cantidad, int encabezadoCompra)>();

                                using (var selectCmd = new NpgsqlCommand(selectCompras, connection, transaction))
                                {
                                    selectCmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                    using (var reader = selectCmd.ExecuteReader())
                                    {
                                        while (reader.Read())
                                        {
                                            comprasRelacionadas.Add((
                                                reader.GetInt32(0),
                                                reader.GetDecimal(1),
                                                reader.GetInt32(2)
                                            ));
                                        }
                                    }
                                }

                                // 3️⃣ Revertir las cantidades al registro padre
                                foreach (var c in comprasRelacionadas)
                                {
                                    string updateCompra = @"
                UPDATE registro_compras
                SET cantidad_restante = cantidad_restante + @cantidad
                WHERE encabezado_compra = @encabezado_compra;
            ";

                                    using (var updateCmd = new NpgsqlCommand(updateCompra, connection, transaction))
                                    {
                                        // usamos valor absoluto
                                        updateCmd.Parameters.AddWithValue("cantidad", Math.Abs(c.cantidad));
                                        updateCmd.Parameters.AddWithValue("encabezado_compra", c.encabezadoCompra);
                                        updateCmd.ExecuteNonQuery();
                                    }
                                }


                                // 4️⃣ Eliminar los registros de compra del encabezado de la remisión fallida
                                string deleteRegistro = "DELETE FROM registro_compras WHERE encabezado_venta = @idEncabezado;";
                                using (var cmdDelete = new NpgsqlCommand(deleteRegistro, connection, transaction))
                                {
                                    cmdDelete.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                    cmdDelete.ExecuteNonQuery();
                                }
                            }

                            // 🔹 5️⃣ Eliminar datos comunes a todos los tipos
                            string deleteImpOC = "DELETE FROM imp_oc WHERE encabezado_id=@idEncabezado;";
                            using (var cmd = new NpgsqlCommand(deleteImpOC, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                cmd.ExecuteNonQuery();
                            }



                            string deletePartidas = "DELETE FROM partidasdoc WHERE encabezado_id = @idEncabezado;";
                            using (var cmd = new NpgsqlCommand(deletePartidas, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                cmd.ExecuteNonQuery();
                            }

                            string deleteDPoliza = "DELETE FROM detalles_polizas WHERE encabezado_id=@idEncabezado;";
                            //using (var cmd = new NpgsqlCommand(deleteDPoliza, connection, transaction))
                            //{
                            //    cmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                            //    cmd.ExecuteNonQuery();
                            //}

                            string deletePoliza = "DELETE FROM polizas WHERE referencia=@idEncabezado;";
                            //using (var cmd = new NpgsqlCommand(deletePoliza, connection, transaction))
                            //{
                            //    cmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                            //    cmd.ExecuteNonQuery();
                            //}

                            string deleteAnticipo = "DELETE FROM factura_anticipos WHERE id_factura_principal = @idEncabezado;";
                            using (var cmd = new NpgsqlCommand(deleteAnticipo, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                cmd.ExecuteNonQuery();
                            }

                            string deleteEncabezado = "DELETE FROM encabezadomov WHERE id_encabezado = @idEncabezado;";
                            using (var cmd = new NpgsqlCommand(deleteEncabezado, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("idEncabezado", idEncabezado);
                                cmd.ExecuteNonQuery();
                            }

                            // 🔹 6️⃣ Actualizar consecutivo (solo si es factura)
                            if (tipo == "factura" && serie == "CC")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 65;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "factura" && serie == "VN")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 44;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "remision" && serie == "VN")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 43;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "factura" && serie == "VI")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 48;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "remision" && serie == "VI")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 47;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "factura" && serie == "VS")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 52;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "remision" && serie == "VS")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 51;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "factura" && serie == "VIN")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 56;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "remision" && serie == "VIN")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 55;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            if (tipo == "pedido_desde_docs_lote" && serie == "")
                            {
                                string updateFolio = "UPDATE foldoc SET ultconsec = ultconsec - 1 WHERE tpdocid = 50;";
                                using (var cmdUpdate = new NpgsqlCommand(updateFolio, connection, transaction))
                                {
                                    cmdUpdate.ExecuteNonQuery();
                                }
                            }
                            // 🔹 7️⃣ Confirmar transacción
                            transaction.Commit();

                            string msgTipo = tipo == "factura"
                                ? "factura y sus registros asociados"
                                : tipo == "remision"
                                    ? "remisión revertida y registros restaurados"
                                    : "documento y sus registros relacionados";

                            return $"✅ Se eliminaron correctamente el encabezado {idEncabezado} y su {msgTipo}.";
                        }
                        catch (Exception exTrans)
                        {
                            transaction.Rollback();
                            return $"❌ Error al eliminar registros: {exTrans.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return $"❌ Error de conexión o ejecución: {ex.Message}";
            }
        }

        public static bool EndpointHasProcess(string controlador, string accion, string connectionStringName = "ERP_SRS")
        {
            var util = new Utilities(true);
            string connectionString = util._configuration.GetConnectionString(connectionStringName);

            if (string.IsNullOrEmpty(connectionString))
                throw new Exception($"No se encontró la cadena de conexión '{connectionStringName}' en el archivo web.config.");

            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT EXISTS( " +
                    "   SELECT 1 " +
                    "   FROM proceso_endpoints " +
                    "   WHERE controlador = @controlador " +
                    "   AND accion = @accion " +
                    ")";

                using (var command = new NpgsqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("controlador", controlador);
                    command.Parameters.AddWithValue("accion", accion);

                    return (bool)command.ExecuteScalar();
                }
            }
        }

        #endregion


        // En Utilities.cs — agregar este método protegido
        protected ValidacionResultado ValidarAcceso(int usuarioId, string moduloId, string passwordIngresado)
        {
            const int MaxIntentos = 3;
            const int MinutosBloqueo = 30;

            try
            {
                // 1. Obtener clave activa del módulo
                string sqlClave = @"
                    SELECT id, password_hash, activo
                    FROM claves_modulo
                    WHERE modulo_id = @moduloId AND activo = true
                    LIMIT 1";

                var rClave = RunQuery(sqlClave,
                    new Dictionary<string, object> { { "@moduloId", moduloId } });

                if (rClave == null || rClave.Count == 0)
                    return new ValidacionResultado(false, "Módulo no configurado o inactivo.");

                var clave = (IDictionary<string, object>)rClave[0];
                int claveId = Convert.ToInt32(clave["id"]);
                string hash = clave["password_hash"]?.ToString();

                // 2. Verificar que el usuario esté asignado
                string sqlAsig = @"
                    SELECT id, activo, intentos_fallidos, bloqueado_hasta
                    FROM clave_modulo_usuarios
                    WHERE clave_id = @claveId AND usuario_id = @usuarioId";

                var rAsig = RunQuery(sqlAsig, new Dictionary<string, object>
                {
                    { "@claveId",   claveId   },
                    { "@usuarioId", usuarioId }
                });

                if (rAsig == null || rAsig.Count == 0)
                    return new ValidacionResultado(false, "No tiene acceso a este módulo.");

                var asig = (IDictionary<string, object>)rAsig[0];
                int asigId = Convert.ToInt32(asig["id"]);

                if (!Convert.ToBoolean(asig["activo"]))
                    return new ValidacionResultado(false, "Su acceso a este módulo está desactivado.");

                // 3. Verificar bloqueo individual
                DateTime? bloqueadoHasta = asig["bloqueado_hasta"] == DBNull.Value
                    ? (DateTime?)null
                    : Convert.ToDateTime(asig["bloqueado_hasta"]);

                if (bloqueadoHasta.HasValue && bloqueadoHasta.Value > DateTime.Now)
                {
                    int min = (int)(bloqueadoHasta.Value - DateTime.Now).TotalMinutes + 1;
                    return new ValidacionResultado(false,
                        $"Cuenta bloqueada. Intente en {min} minuto(s).");
                }

                // 4. Verificar contraseña
                int intentos = Convert.ToInt32(asig["intentos_fallidos"]);
                bool ok = BCrypt.Net.BCrypt.Verify(passwordIngresado, hash);

                if (!ok)
                {
                    intentos++;
                    if (intentos >= MaxIntentos)
                    {
                        RunQuery(@"UPDATE clave_modulo_usuarios SET
                                       intentos_fallidos = @i,
                                       bloqueado_hasta   = NOW() + (@mins || ' minutes')::interval
                                   WHERE id = @id",
                            new Dictionary<string, object>
                            {
                                { "@i",    intentos        },
                                { "@mins", MinutosBloqueo  },
                                { "@id",   asigId          }
                            });
                        return new ValidacionResultado(false,
                            $"Demasiados intentos. Bloqueado por {MinutosBloqueo} min.");
                    }

                    RunQuery("UPDATE clave_modulo_usuarios SET intentos_fallidos = @i WHERE id = @id",
                        new Dictionary<string, object> { { "@i", intentos }, { "@id", asigId } });

                    return new ValidacionResultado(false,
                        $"Contraseña incorrecta. {MaxIntentos - intentos} intento(s) restante(s).");
                }

                // 5. Éxito: limpiar intentos
                RunQuery(@"UPDATE clave_modulo_usuarios SET
                               intentos_fallidos = 0, bloqueado_hasta = NULL
                           WHERE id = @id",
                    new Dictionary<string, object> { { "@id", asigId } });

                return new ValidacionResultado(true, "Acceso permitido.");
            }
            catch (Exception ex)
            {
                return new ValidacionResultado(false, $"Error interno: {ex.Message}");
            }
        }
    }

    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dict,
            TKey key,
            TValue defaultValue = default)
        {
            return dict.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }

    #region SQL Helper

    public class SqlHelper
    {
        private readonly Dictionary<Type, SqlDbType> TypeMap;

        public SqlHelper() // <-- era privado, corregido a public
        {
            TypeMap = new Dictionary<Type, SqlDbType>
            {
                [typeof(string)] = SqlDbType.NVarChar,
                [typeof(char[])] = SqlDbType.NVarChar,
                [typeof(byte)] = SqlDbType.TinyInt,
                [typeof(short)] = SqlDbType.SmallInt,
                [typeof(int)] = SqlDbType.Int,
                [typeof(long)] = SqlDbType.BigInt,
                [typeof(byte[])] = SqlDbType.Image,
                [typeof(bool)] = SqlDbType.Bit,
                [typeof(DateTime)] = SqlDbType.DateTime2,
                [typeof(DateTimeOffset)] = SqlDbType.DateTimeOffset,
                [typeof(decimal)] = SqlDbType.Money,
                [typeof(float)] = SqlDbType.Real,
                [typeof(double)] = SqlDbType.Float,
                [typeof(TimeSpan)] = SqlDbType.Time
            };
        }

        public SqlDbType GetDbType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (TypeMap.TryGetValue(t, out var dbType))
                return dbType;
            throw new ArgumentException($"{t.FullName} no está soportado.");
        }

        public SqlDbType GetDbType<T>() => GetDbType(typeof(T));
    }


    // ─────────────────────────────────────────────
    // ATRIBUTO BASE: lógica compartida de DB
    // ─────────────────────────────────────────────
    public abstract class BaseAuthorizeAttribute : Attribute
    {
        protected readonly string _connectionString;

        protected BaseAuthorizeAttribute(string connectionString = "ERP_SRS")
        {
            _connectionString = connectionString;
        }

        // Cada subclase implementa su propia lógica
        public abstract void OnAuthorization(AuthorizationFilterContext context);

        protected void Forbid(AuthorizationFilterContext context)
        {
            context.Result = new StatusCodeResult(403);
        }

        protected string GetConnectionString()
        {
            var utilities = new Utilities(true);
            return utilities._configuration.GetConnectionString(_connectionString);
        }
    }


    // ─────────────────────────────────────────────
    // ROLE AUTHORIZE
    // ─────────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RoleAuthorizeAttribute : BaseAuthorizeAttribute
    {
        private readonly string _role;
        private readonly string[] _roles;
        private readonly string _storedProcedureName;

        public RoleAuthorizeAttribute(string role, string connectionString = "ERP_SRS", string storedProcedureName = "GetUserRole")
            : base(connectionString)
        {
            _role = role;
            _storedProcedureName = storedProcedureName;
            _roles = Array.Empty<string>();
        }

        public RoleAuthorizeAttribute(string[] roles, string connectionString = "ERP_SRS", string storedProcedureName = "GetUserRole")
            : base(connectionString)
        {
            _roles = roles;
            _storedProcedureName = storedProcedureName;
            _role = string.Empty;
        }

        public override void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (!user.Identity?.IsAuthenticated ?? true)
            {
                Forbid(context);
                return;
            }

            string userRole = GetUserRole(user.Identity.Name);

            bool isAuthorized =
                userRole == "Sistemas" ||
                (_roles.Length == 0 ? userRole == _role : _roles.Contains(userRole));

            if (!isAuthorized)
                Forbid(context);
        }

        public string GetUserRole(string userName)
        {
            string connStr = GetConnectionString();

            using var connection = new NpgsqlConnection(connStr);
            connection.Open();

            string query = "SELECT getuserrole(@username);";
            using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("username", userName);

            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }
    }


    // ─────────────────────────────────────────────
    // AREA AUTHORIZE
    // ─────────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class AreaAuthorizeAttribute : BaseAuthorizeAttribute
    {
        private readonly string _area;
        private readonly string[] _areas;

        public AreaAuthorizeAttribute(string area, string connectionString = "ERP_SRS")
            : base(connectionString)
        {
            _area = area;
            _areas = Array.Empty<string>();
        }

        public AreaAuthorizeAttribute(string[] areas, string connectionString = "ERP_SRS")
            : base(connectionString)
        {
            _areas = areas;
            _area = string.Empty;
        }

        public override void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (!user.Identity?.IsAuthenticated ?? true)
            {
                Forbid(context);
                return;
            }

            string userArea = GetAreaName(user.Identity.Name);

            bool isAuthorized =
                userArea == "Sistemas" ||
                (_areas.Length == 0 ? userArea == _area : _areas.Contains(userArea));

            if (!isAuthorized)
                Forbid(context);
        }

        public string GetAreaName(string userName)
        {
            string connStr = GetConnectionString();

            using var connection = new NpgsqlConnection(connStr);
            connection.Open();

            string query = "SELECT getareaname(@username);";
            using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("username", userName);

            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }
    }


    // ─────────────────────────────────────────────
    // SUCURSAL AUTHORIZE
    // ─────────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class SucursalAuthorizeAttribute : BaseAuthorizeAttribute
    {
        private readonly string _sucursal;
        private readonly string[] _sucursales;

        public SucursalAuthorizeAttribute(string sucursal, string connectionString = "ERP_SRS")
            : base(connectionString)
        {
            _sucursal = sucursal;
            _sucursales = Array.Empty<string>();
        }

        public SucursalAuthorizeAttribute(string[] sucursales, string connectionString = "ERP_SRS")
            : base(connectionString)
        {
            _sucursales = sucursales;
            _sucursal = string.Empty;
        }

        public override void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (!user.Identity?.IsAuthenticated ?? true)
            {
                Forbid(context);
                return;
            }

            string userSucursal = GetSucursal(user.Identity.Name);

            bool isAuthorized =
                userSucursal == "CEDIS" ||
                (_sucursales.Length == 0 ? userSucursal == _sucursal : _sucursales.Contains(userSucursal));

            if (!isAuthorized)
                Forbid(context);
        }

        protected string GetSucursal(string userName)
        {
            string connStr = GetConnectionString();

            using var connection = new NpgsqlConnection(connStr);
            connection.Open();

            string query =
                "SELECT c.cve_sucursal " +
                "FROM usuarios u " +
                "INNER JOIN catsucursales c ON c.id_sucursal = u.sucursal_id " +
                "WHERE u.nombreusuario = @username";

            using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("username", userName);

            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }
    }


    // ─────────────────────────────────────────────
    // RIGHT AUTHORIZE — patrón correcto .NET Core 8
    // Requirement (datos) + Handler (lógica)
    // ─────────────────────────────────────────────

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RightAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string[] _rights;
        private readonly string _connectionString;

        public RightAuthorizeAttribute(string right, string connectionString = "ERP_SRS")
        {
            _rights = new[] { right };
            _connectionString = connectionString;
        }

        public RightAuthorizeAttribute(string[] rights, string connectionString = "ERP_SRS")
        {
            _rights = rights;
            _connectionString = connectionString;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (!user.Identity?.IsAuthenticated ?? true)
            {
                context.Result = new StatusCodeResult(403);
                return;
            }

            bool autorizado = DoesUserHasRight(
                user.Identity.Name,
                _rights,
                _connectionString);

            if (!autorizado)
                context.Result = new StatusCodeResult(403);
        }

        private bool DoesUserHasRight(string userName, string[] rights, string dbConnection)
        {
            try
            {
                var utilities = new Utilities(true);
                string connStr = utilities._configuration.GetConnectionString(dbConnection);

                using var connection = new NpgsqlConnection(connStr);

                string query =
                    "SELECT 1 " +
                    "FROM permisos_usuario pu " +
                    "INNER JOIN permisos p ON p.id_permiso = pu.permiso_id " +
                    "WHERE pu.usuario_id = @userId " +
                    "  AND (p.nombre = 'super_admin' OR p.nombre = 'sistemas' OR p.nombre = ANY(@rights)) " +
                    "LIMIT 1";

                using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("userId", GetUserId(userName, connStr));
                command.Parameters.AddWithValue("rights", rights);

                connection.Open();
                return command.ExecuteScalar() != null;
            }
            catch
            {
                return false;
            }
        }

        private int GetUserId(string userName, string connStr)
        {
            try
            {
                using var connection = new NpgsqlConnection(connStr);
                string query = "SELECT usuarioid FROM usuarios WHERE nombreusuario = @userName";

                using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("userName", userName);

                connection.Open();
                var result = command.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
    #endregion


}
