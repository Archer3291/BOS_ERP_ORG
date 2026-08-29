using BOS_ERP.Helpers;
using MySql.Data.MySqlClient;
using Npgsql;

namespace BOS_ERP.Services
{
    public sealed class ResultadoPublicacion
    {
        public bool Exito { get; init; } = true;
        public int Actualizados { get; init; }
        public int Insertados { get; init; }
        public int SinCambio { get; init; }

        /// <summary>Clientes que no existían en Socio Tiburón y se dieron de alta.</summary>
        public int DadosDeAlta { get; init; }

        /// <summary>Altas que sólo pudieron crear el cliente, no el usuario del portal.</summary>
        public int SinUsuario { get; init; }

        /// <summary>Canjes hechos en el portal que se registraron en el ledger del ERP.</summary>
        public int Canjes { get; init; }

        public string Mensaje { get; init; } = "";
    }

    /// <summary>Datos de contacto de un cliente tal como los tiene el ERP.</summary>
    public sealed class ContactoCliente
    {
        public bool Encontrado { get; init; }
        public string CveCli { get; init; } = "";
        public string Nombre { get; init; } = "";
        public string Telefono { get; init; } = "";
        public string Correo { get; init; } = "";
    }

    /// <summary>
    /// Presencia de un cliente en las dos tablas de Socio Tiburón. Son dos porque el
    /// programa las usa para cosas distintas: `clientes` es el padrón —la clave de
    /// Kepler con sus datos de contacto— y `usuarios` es la cuenta con la que esa
    /// persona entra al portal. Se puede estar en una y no en la otra.
    /// </summary>
    public sealed class PresenciaSocioTiburon
    {
        /// <summary>
        /// Se pudo hablar con el MySQL. En false el resto no significa nada: no es que
        /// el cliente falte, es que no se sabe.
        /// </summary>
        public bool Consultado { get; init; }

        public bool EnClientes { get; init; }
        public bool EnUsuarios { get; init; }

        public bool Completo => EnClientes && EnUsuarios;

        /// <summary>Lo que Socio Tiburón ya tiene guardado, si el cliente está.</summary>
        public string Nombre { get; init; } = "";
        public string Telefono { get; init; } = "";
        public string Correo { get; init; } = "";

        /// <summary>
        /// Nivel asignado en `cliente_membresia`, con el nombre tal cual lo escribe Socio
        /// Tiburón. Vacío significa que nadie se lo ha asignado —no es un error—, y el
        /// cliente vale como el nivel de entrada.
        /// </summary>
        public string Membresia { get; init; } = "";

        public string Mensaje { get; init; } = "";
    }

    public sealed class ResultadoAlta
    {
        public bool Exito { get; init; }
        public bool CreoCliente { get; init; }
        public bool CreoUsuario { get; init; }

        /// <summary>Lo que no se pudo hacer aunque el alta haya salido bien.</summary>
        public string Aviso { get; init; } = "";

        public string Mensaje { get; init; } = "";
    }

    /// <summary>
    /// Publica el saldo del ERP en la tabla `puntos` de Socio Tiburón, que es de donde
    /// el sitio lee lo que le muestra al cliente.
    ///
    /// POR QUÉ ESTE CAMINO
    /// El sitio podría consultar al ERP, pero eso obligaría a modificarlo. En su lugar
    /// el ERP deja el número escrito donde el sitio ya lo busca: cambia quién escribe,
    /// no quién lee, y la página no se entera. Es lo mismo que hacía la aplicación de
    /// escritorio, sólo que con el saldo bien calculado y con el detalle respaldado en
    /// el ledger del ERP.
    ///
    /// `puntos` es una CACHÉ, no la verdad. La verdad es v_puntos_saldo. Si las dos
    /// difieren, se republica; nunca al revés.
    ///
    /// UNA FILA POR CLIENTE. `puntos.idcliente` es único desde que se depuró el esquema.
    /// Antes no lo era —arrastraba dos renglones por cliente de dos cargas históricas y
    /// el sitio leía uno cualquiera—, y por eso este código actualizaba todas las filas
    /// sin LIMIT y tenía que averiguar a mano si un 0 filas significaba "no existe" o
    /// "ya valía eso". Ahora eso lo impone la base y basta un upsert.
    ///
    /// ANTES DEL SALDO, EL ALTA
    /// Un renglón en `puntos` cuyo `idcliente` no está en `clientes` es un saldo que el
    /// sitio no le puede mostrar a nadie: no hay a quién colgarlo. Por eso publicar
    /// empieza asegurando que el cliente exista allá —y su usuario del portal— y sólo
    /// después escribe el número.
    /// </summary>
    public class PuntosSyncService
    {
        private readonly string _cadenaMysql;
        private readonly string _cadenaErp;

        private const string LogTag = "PuntosSync";

        // Topes de las columnas de Socio Tiburón. Se recorta aquí en vez de dejar que
        // MySQL trunque: en modo estricto un valor largo aborta el INSERT y con él el
        // alta entera, y un teléfono largo no vale una excepción.
        //
        // Son mucho más ajustados que antes —el esquema se depuró y las columnas pasaron
        // de varchar(450) a tamaños reales—, así que recortar dejó de ser teoría: un
        // nombre de razón social de 160 caracteres ya no cabe.
        private const int MaxClave = 50;      // clientes.cliente / usuarios.idkep
        private const int MaxNombre = 150;    // clientes.nombre / usuarios.usuario
        private const int MaxCorreo = 150;    // usuarios.correo
        private const int MaxTelefono = 20;   // clientes.telefono / usuarios.telefono

        /// <summary>
        /// Con qué contraseña nace el usuario del portal cuando nadie la escribe: el
        /// alta en lote no tiene a quién preguntarle. La pantalla la ofrece prellenada
        /// y editable, así que este valor sólo gobierna la publicación masiva.
        /// </summary>
        public const string ClaveInicial = "1234";

        /// <summary>
        /// Costo del bcrypt. Va explícito y no por omisión del paquete: es lo que
        /// tarda verificar un login, y que cambie sola con una actualización de
        /// BCrypt.Net dejaría hashes de dos costos distintos sin que nadie lo decidiera.
        /// </summary>
        private const int CostoBcrypt = 11;

        public PuntosSyncService(IConfiguration config)
        {
            _cadenaMysql = config.GetConnectionString("SocioTiburon") ?? "";
            _cadenaErp = config.GetConnectionString("ERP_SRS") ?? "";
        }

        // ════════════════════════════════════════════════════════════════
        // Alta del cliente en Socio Tiburón
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Dice si el cliente ya está en Socio Tiburón, en cuál de las dos tablas y con
        /// qué datos. Nunca lanza: si el servidor externo no responde devuelve
        /// Consultado=false, que la pantalla trata como "no se sabe" y no como "falta".
        /// </summary>
        public async Task<PresenciaSocioTiburon> ConsultarClienteAsync(string cveCli)
        {
            if (string.IsNullOrWhiteSpace(cveCli))
                return new PresenciaSocioTiburon { Mensaje = "Falta la clave del cliente." };

            try
            {
                await using var conn = new MySqlConnection(_cadenaMysql);
                await conn.OpenAsync();

                return await LeerPresenciaAsync(conn, cveCli.Trim());
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"No se pudo consultar al cliente en Socio Tiburón: {ex.Message}", nivel: "WARN");

                return new PresenciaSocioTiburon
                {
                    Consultado = false,
                    Mensaje = "No se pudo consultar Socio Tiburón: " + ex.Message
                };
            }
        }

        /// <summary>
        /// Da de alta al cliente con los datos que confirmó el operador. Es el camino
        /// bueno: los datos vienen revisados en pantalla, así que aquí un fallo SÍ se
        /// reporta —a diferencia del alta automática de <see cref="PublicarAsync"/>,
        /// que no tiene a quién preguntarle y sigue adelante.
        /// </summary>
        public async Task<ResultadoAlta> AltaClienteAsync(
            string cveCli, string nombre, string telefono, string correo, string contrasena)
        {
            cveCli = (cveCli ?? "").Trim();
            nombre = (nombre ?? "").Trim();
            telefono = (telefono ?? "").Trim();
            correo = (correo ?? "").Trim();
            contrasena = contrasena ?? "";

            if (cveCli.Length == 0)
                return new ResultadoAlta { Mensaje = "Falta la clave del cliente." };

            if (nombre.Length == 0)
                return new ResultadoAlta { Mensaje = "Falta el nombre del cliente." };

            // El correo no es un dato de contacto más: es la llave única con la que el
            // cliente entra al portal (`usuarios.correo` es NOT NULL UNIQUE). Sin él no
            // hay usuario que crear, y dejarlo vacío no es una opción que la base admita.
            if (correo.Length == 0)
                return new ResultadoAlta
                {
                    Mensaje = "El correo es obligatorio: es con lo que el cliente entra al portal de Socio Tiburón."
                };

            // Vacía se toma como "la de siempre" en vez de rechazar el alta: el campo
            // viene prellenado en pantalla y borrarlo se lee como "déjala como venía",
            // no como "créalo sin contraseña" —que además la base no admite—.
            if (contrasena.Trim().Length == 0) contrasena = ClaveInicial;

            try
            {
                await using var conn = new MySqlConnection(_cadenaMysql);
                await conn.OpenAsync();

                var antes = await LeerPresenciaAsync(conn, cveCli);

                if (antes.Completo)
                    return new ResultadoAlta
                    {
                        Exito = true,
                        Mensaje = "El cliente ya estaba dado de alta en Socio Tiburón."
                    };

                // El acumulado se trae del ledger en vez de nacer en cero. Normalmente da
                // cero —el alta va antes de otorgarle nada—, pero no siempre: un cliente
                // al que ya se le otorgaron puntos y que se está reponiendo en Socio
                // Tiburón llegaría con su historial en cero y, con él, en el nivel de
                // entrada. Si el ledger no responde no se aborta el alta por esto: el
                // número lo corrige la primera publicación.
                decimal acumulado = 0m;

                try
                {
                    var enElErp = await LeerSaldosDelErpAsync(new[] { cveCli });
                    if (enElErp.Count > 0) acumulado = enElErp[0].Acumulado;
                }
                catch (Exception ex)
                {
                    LogErrorHelper.RegistrarLog(LogTag, cveCli,
                        $"No se pudo leer el acumulado al dar de alta: {ex.Message}", nivel: "WARN");
                }

                var alta = await AsegurarAltaAsync(
                    conn, cveCli, nombre, telefono, correo, HashClave(contrasena), acumulado, antes);

                // Que el cliente quede sin usuario del portal no es un detalle: es la
                // mitad de lo que se pidió. Se reporta como error para que el operador lo
                // corrija —normalmente cambiando el correo— en vez de darlo por hecho.
                if (!antes.EnUsuarios && !alta.CreoUsuario)
                    return new ResultadoAlta
                    {
                        Exito = false,
                        CreoCliente = alta.CreoCliente,
                        Aviso = alta.Aviso,
                        Mensaje = alta.CreoCliente
                            ? $"El cliente se creó en el padrón, pero no su usuario del portal: {alta.Aviso}"
                            : $"No se pudo crear el usuario del portal: {alta.Aviso}"
                    };

                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"Alta en Socio Tiburón: cliente={alta.CreoCliente}, usuario={alta.CreoUsuario} " +
                    $"({nombre}, {correo})", nivel: "INFO");

                return new ResultadoAlta
                {
                    Exito = true,
                    CreoCliente = alta.CreoCliente,
                    CreoUsuario = alta.CreoUsuario,
                    Aviso = alta.Aviso,
                    Mensaje = "Cliente dado de alta en Socio Tiburón."
                };
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"No se pudo dar de alta al cliente: {ex.Message}", nivel: "ERROR");

                return new ResultadoAlta
                {
                    Exito = false,
                    Mensaje = "No se pudo dar de alta en Socio Tiburón: " + ex.Message
                };
            }
        }

        /// <summary>
        /// Datos de contacto del ERP con los que se prellena el alta. El teléfono y el
        /// correo salen de sus tablas de colección, tomando el de menor id: es el
        /// primero que se capturó, que en la práctica es el principal.
        /// </summary>
        public async Task<ContactoCliente> LeerContactoDelErpAsync(int clienteId)
        {
            if (clienteId <= 0) return new ContactoCliente();

            await using var conn = new NpgsqlConnection(_cadenaErp);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                SELECT c.cve_cli,
                       COALESCE(c.n_cli, '') AS nombre,
                       COALESCE((SELECT t.telefono FROM telefonos_cliente t
                                  WHERE t.cliente_id = c.id_cliente
                                    AND COALESCE(t.telefono, '') <> ''
                                  ORDER BY t.id_telefono_cli LIMIT 1), '') AS telefono,
                       COALESCE((SELECT co.correo FROM correos_cliente co
                                  WHERE co.cliente_id = c.id_cliente
                                    AND COALESCE(co.correo, '') <> ''
                                  ORDER BY co.id_correo_cli LIMIT 1), '') AS correo
                FROM catclientes c
                WHERE c.id_cliente = @id", conn);

            cmd.Parameters.AddWithValue("id", clienteId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync()) return new ContactoCliente();

            return new ContactoCliente
            {
                Encontrado = true,
                CveCli = Texto(reader["cve_cli"]),
                Nombre = Texto(reader["nombre"]),
                Telefono = Texto(reader["telefono"]),
                Correo = Texto(reader["correo"])
            };
        }

        /// <summary>Presencia del cliente sobre una conexión ya abierta.</summary>
        private static async Task<PresenciaSocioTiburon> LeerPresenciaAsync(MySqlConnection conn, string cve)
        {
            // Las dos tablas se consultan por separado y no con JOIN porque no cubren a la
            // misma población: puede haber cliente en el padrón sin cuenta de portal y al
            // revés. Cada una tiene su propia clave única sobre la clave del ERP
            // —`clientes.cliente` es la llave primaria y `usuarios.idkep` es UNIQUE—, así
            // que a lo más hay una fila de cada lado.
            //
            // El correo se lee de `usuarios` y no de `clientes`: el padrón ya no lo
            // guarda. Tiene sentido —el correo identifica a la CUENTA, no al cliente— y
            // es lo que hace que un cliente pueda existir sin poder entrar al portal.
            //
            // La membresía vive en `cliente_membresia` (una fila por cliente, con FK al
            // catálogo `membresia`). Sin fila no es un error: significa que nadie le ha
            // asignado nivel y le toca el de entrada.
            await using var cmd = new MySqlCommand(@"
                SELECT
                    (SELECT COUNT(*) FROM clientes WHERE cliente = @cve) AS en_clientes,
                    (SELECT COUNT(*) FROM usuarios WHERE idkep   = @cve) AS en_usuarios,
                    (SELECT c.nombre   FROM clientes c WHERE c.cliente = @cve LIMIT 1) AS nombre,
                    (SELECT c.telefono FROM clientes c WHERE c.cliente = @cve LIMIT 1) AS telefono,
                    (SELECT u.correo   FROM usuarios u WHERE u.idkep   = @cve LIMIT 1) AS correo,
                    (SELECT m.nombre
                       FROM cliente_membresia cm
                       JOIN membresia m ON m.idmembresia = cm.idmembresia
                      WHERE cm.idkep = @cve
                      LIMIT 1) AS membresia", conn);

            cmd.Parameters.AddWithValue("@cve", cve);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return new PresenciaSocioTiburon { Consultado = true };

            return new PresenciaSocioTiburon
            {
                Consultado = true,
                EnClientes = Convert.ToInt64(reader["en_clientes"]) > 0,
                EnUsuarios = Convert.ToInt64(reader["en_usuarios"]) > 0,
                Nombre = Texto(reader["nombre"]),
                Telefono = Texto(reader["telefono"]),
                Correo = Texto(reader["correo"]),
                Membresia = Texto(reader["membresia"])
            };
        }

        /// <summary>
        /// Crea lo que falte de las dos tablas y no toca lo que ya existe: los datos que
        /// Socio Tiburón tenga son suyos, y pisarlos con los del ERP borraría el correo
        /// con el que el cliente entra hoy al portal.
        ///
        /// El usuario del portal puede quedarse sin crear por dos motivos que no son
        /// fallas del sistema —no hay correo, o ese correo ya es de otro usuario— y por
        /// eso vuelven como aviso y no como excepción: quien llama decide si eso es
        /// grave. Al dar de alta a mano lo es; al publicar en lote, no.
        ///
        /// La <paramref name="presencia"/> se recibe hecha en vez de consultarla aquí
        /// porque quien publica en lote ya la sabe para todos de una sola consulta;
        /// releerla por cliente serían dos viajes más al servidor externo por cada uno.
        ///
        /// La contraseña llega ya en <paramref name="hashContrasena"/>, no en claro, por
        /// la misma razón: bcrypt con costo 11 tarda a propósito, y calcularlo aquí lo
        /// pondría dentro del bucle del lote una vez por cliente.
        /// </summary>
        private static async Task<(bool CreoCliente, bool CreoUsuario, string Aviso)> AsegurarAltaAsync(
            MySqlConnection conn, string cve, string nombre, string telefono, string correo,
            string hashContrasena, decimal acumulado, PresenciaSocioTiburon presencia)
        {
            bool creoCliente = false;
            bool creoUsuario = false;
            string aviso = "";

            if (!presencia.EnClientes)
            {
                // `cliente` es la clave del ERP (cve_cli) y es la LLAVE PRIMARIA de la
                // tabla, así que dos publicaciones simultáneas no pueden duplicar el
                // padrón: el ON DUPLICATE deja ganar a la primera sin reventar a la
                // segunda. Se actualiza la propia clave por ser un no-op que MySQL
                // reporta como 0 filas, que es justo lo que distingue "lo creé" de
                // "ya estaba".
                //
                // El correo no va aquí sino en `usuarios`: el padrón guarda identidad,
                // teléfono y el acumulado histórico del que sale el nivel de membresía.
                //
                // El acumulado nace con su valor y no en cero: darlo de alta en cero
                // dejaría al cliente en el nivel de entrada hasta la siguiente
                // publicación, y quien mirara la pantalla en ese hueco vería a un cliente
                // con años de compras como si acabara de llegar.
                await using var ins = new MySqlCommand(@"
                    INSERT INTO clientes (cliente, nombre, telefono, total_acumulado)
                    VALUES (@cve, @nombre, @telefono, @acumulado)
                    ON DUPLICATE KEY UPDATE cliente = cliente", conn);

                ins.Parameters.AddWithValue("@cve", Recortar(cve, MaxClave));
                ins.Parameters.AddWithValue("@nombre", Recortar(nombre, MaxNombre));
                ins.Parameters.AddWithValue("@telefono", Opcional(telefono, MaxTelefono));
                ins.Parameters.AddWithValue("@acumulado", acumulado);

                creoCliente = await ins.ExecuteNonQueryAsync() > 0;
            }

            if (!presencia.EnUsuarios)
            {
                if (correo.Length == 0)
                {
                    aviso = "el cliente no tiene correo y el portal lo usa como identificador único.";
                }
                else
                {
                    await using var repetido = new MySqlCommand(
                        "SELECT COUNT(*) FROM usuarios WHERE correo = @correo", conn);
                    repetido.Parameters.AddWithValue("@correo", Recortar(correo, MaxCorreo));

                    if (Convert.ToInt64(await repetido.ExecuteScalarAsync()) > 0)
                    {
                        aviso = $"el correo {correo} ya pertenece a otro usuario del portal.";
                    }
                    else
                    {
                        // Se escriben las cinco columnas que definen al usuario. El rol de
                        // cliente, `activo` y `contrasenaoriginal` los pone la tabla por
                        // omisión, que es el criterio del portal y no del ERP.
                        //
                        // La contraseña va explícita porque ya NO tiene valor por omisión
                        // —la columna es NOT NULL a secas—, y entra cifrada con bcrypt.
                        await using var ins = new MySqlCommand(@"
                            INSERT INTO usuarios (usuario, correo, telefono, idkep, contrasena)
                            VALUES (@nombre, @correo, @telefono, @cve, @clave)", conn);

                        ins.Parameters.AddWithValue("@nombre", Recortar(nombre, MaxNombre));
                        ins.Parameters.AddWithValue("@correo", Recortar(correo, MaxCorreo));
                        ins.Parameters.AddWithValue("@telefono", Opcional(telefono, MaxTelefono));
                        ins.Parameters.AddWithValue("@cve", Recortar(cve, MaxClave));
                        ins.Parameters.AddWithValue("@clave", hashContrasena);

                        try
                        {
                            await ins.ExecuteNonQueryAsync();
                            creoUsuario = true;
                        }
                        catch (MySqlException ex) when (ex.Number == 1062)
                        {
                            // Carrera entre el COUNT y el INSERT. Ahora hay DOS índices
                            // únicos que puede tocar —el correo y la clave del cliente—,
                            // así que el mensaje distingue cuál: "ese correo ya es de
                            // alguien" y "este cliente ya tenía cuenta" mandan al operador
                            // a hacer cosas distintas.
                            aviso = ex.Message.Contains("idkep", StringComparison.OrdinalIgnoreCase)
                                ? "este cliente ya tenía una cuenta de portal creada por otra vía."
                                : $"el correo {correo} ya pertenece a otro usuario del portal.";
                        }
                    }
                }
            }

            return (creoCliente, creoUsuario, aviso);
        }

        // ════════════════════════════════════════════════════════════════
        // Publicación del saldo
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Publica el saldo de las claves indicadas; sin claves, publica todos los
        /// clientes que tengan movimientos. Antes de escribir el saldo de cada uno se
        /// asegura de que exista allá, dándolo de alta con los datos del ERP si falta.
        /// </summary>
        public async Task<ResultadoPublicacion> PublicarAsync(IEnumerable<string> claves = null)
        {
            try
            {
                await using var conn = new MySqlConnection(_cadenaMysql);
                await conn.OpenAsync();

                // PRIMERO los canjes, DESPUÉS leer el saldo. El orden no es cosmético: lo
                // que se publica es el saldo del ledger, así que si un canje entrara
                // después de leerlo se escribiría el número de antes de gastarlo y el
                // cliente recuperaría sus puntos.
                //
                // Y si la importación falla, NO se publica. Es la única dirección segura:
                // no publicar deja el número viejo, que a lo sumo está desactualizado;
                // publicar sin los canjes regala puntos que ya se cobraron.
                int canjes = await ImportarCanjesAsync(conn);

                var saldos = await LeerSaldosDelErpAsync(claves);

                if (saldos.Count == 0)
                    return new ResultadoPublicacion
                    {
                        Exito = true,
                        Canjes = canjes,
                        Mensaje = canjes > 0
                            ? $"No hay saldos que publicar. Se registraron {canjes} canje(s) del portal."
                            : "No hay saldos que publicar."
                    };

                int actualizados = 0, insertados = 0, sinCambio = 0, altas = 0, sinUsuario = 0;

                // Quiénes ya están, de una vez y no cliente por cliente. Publicar a todos
                // recorre miles de claves contra un servidor ajeno con 8 s de tope por
                // consulta: preguntar por cada una convertiría un par de viajes en varios
                // miles. Son listas de claves, no de filas, y caben de sobra.
                //
                // Del padrón se trae además el acumulado que ya tiene guardado, para
                // poder no escribirlo cuando no cambió: en una corrida de reparación casi
                // ningún cliente cambia, y un UPDATE por cliente sería el viaje que se
                // acaba de ahorrar.
                var enPadron = await LeerAcumuladosAsync(conn);
                var enPortal = await LeerClavesAsync(conn, "SELECT idkep FROM usuarios");

                // Aquí no hay operador que escriba una contraseña, así que todos los
                // usuarios que nazcan en esta corrida arrancan con la inicial. Se cifra
                // UNA vez y no por cliente: bcrypt con costo 11 tarda ~0.1 s a
                // propósito, y en una publicación de miles serían minutos de puro
                // cifrado. Comparten hash porque comparten la misma clave conocida —el
                // salt sólo protegería un secreto que aquí no lo es—, y en cuanto el
                // cliente la cambia desde el portal deja de compartirlo.
                string hashInicial = HashClave(ClaveInicial);

                foreach (var cliente in saldos)
                {
                    string cve = cliente.Cve;

                    // El alta va primero y su fallo no detiene la publicación: un saldo
                    // sin padrón es un dato que el sitio no muestra, pero perder el saldo
                    // de los 500 clientes siguientes por un correo repetido en uno sería
                    // peor. Lo que no se pudo crear queda contado en el mensaje.
                    if (!enPadron.ContainsKey(cve) || !enPortal.Contains(cve))
                    {
                        try
                        {
                            var alta = await AsegurarAltaAsync(
                                conn, cve, cliente.Nombre, cliente.Telefono, cliente.Correo,
                                hashInicial, cliente.Acumulado,
                                new PresenciaSocioTiburon
                                {
                                    Consultado = true,
                                    EnClientes = enPadron.ContainsKey(cve),
                                    EnUsuarios = enPortal.Contains(cve)
                                });

                            // Se anota el acumulado con el que quedó recién creado para
                            // que el UPDATE de más abajo lo vea al día y no lo reescriba.
                            if (alta.CreoCliente) { altas++; enPadron[cve] = cliente.Acumulado; }
                            if (alta.CreoUsuario) enPortal.Add(cve);

                            if (alta.Aviso.Length > 0)
                            {
                                sinUsuario++;
                                LogErrorHelper.RegistrarLog(LogTag, cve,
                                    "Alta parcial al publicar: " + alta.Aviso, nivel: "WARN");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogErrorHelper.RegistrarLog(LogTag, cve,
                                $"No se pudo dar de alta antes de publicar el saldo: {ex.Message}",
                                nivel: "WARN");
                        }
                    }

                    // El acumulado histórico del padrón sólo se toca cuando cambió. Va
                    // aparte del saldo porque son dos números distintos que viven en dos
                    // tablas: `puntos` es lo que le queda al cliente y `total_acumulado`
                    // es lo que ha ganado en total, de donde sale su nivel de membresía.
                    if (enPadron.TryGetValue(cve, out decimal acumuladoActual)
                        && acumuladoActual != cliente.Acumulado)
                    {
                        await using var updAcum = new MySqlCommand(
                            "UPDATE clientes SET total_acumulado = @a WHERE cliente = @c", conn);

                        updAcum.Parameters.AddWithValue("@a", cliente.Acumulado);
                        updAcum.Parameters.AddWithValue("@c", Recortar(cve, MaxClave));

                        await updAcum.ExecuteNonQueryAsync();
                        enPadron[cve] = cliente.Acumulado;
                    }

                    // `puntos.puntos` es int en el origen: los saldos con decimales
                    // —posibles vía ajuste manual— se redondean al publicar. El valor
                    // exacto sigue vivo en el ledger.
                    int valor = (int)Math.Round(cliente.Saldo, 0, MidpointRounding.AwayFromZero);

                    // Antes esto eran hasta tres viajes —UPDATE, contar, INSERT— porque la
                    // tabla admitía renglones repetidos por cliente y había que distinguir
                    // "no existe" de "ya valía eso", que MySQL reporta igual: 0 filas.
                    // Ahora `idcliente` es único, así que una sola sentencia basta y es la
                    // base la que garantiza que no haya duplicados, no el orden del código.
                    //
                    // Las filas afectadas dicen qué pasó: 1 = insertó, 2 = actualizó,
                    // 0 = ya tenía ese valor. Es la convención de MySQL para ON DUPLICATE
                    // KEY UPDATE y es justo la que se necesita para el resumen.
                    await using var upsert = new MySqlCommand(@"
                        INSERT INTO puntos (idcliente, puntos)
                        VALUES (@c, @p)
                        ON DUPLICATE KEY UPDATE puntos = VALUES(puntos)", conn);

                    upsert.Parameters.AddWithValue("@c", Recortar(cve, MaxClave));
                    upsert.Parameters.AddWithValue("@p", valor);

                    switch (await upsert.ExecuteNonQueryAsync())
                    {
                        case 1: insertados++; break;
                        case 2: actualizados++; break;
                        default: sinCambio++; break;
                    }
                }

                string mensaje =
                    $"Saldos publicados en Socio Tiburón: {actualizados} actualizado(s), " +
                    $"{insertados} nuevo(s), {sinCambio} sin cambio.";

                if (canjes > 0)
                    mensaje += $" Se registraron {canjes} canje(s) hechos en el portal.";

                if (altas > 0)
                    mensaje += $" Se dieron de alta {altas} cliente(s) que no existían allá.";

                if (sinUsuario > 0)
                    mensaje += $" {sinUsuario} quedaron sin usuario del portal por falta de " +
                               "correo o por tenerlo repetido.";

                LogErrorHelper.RegistrarLog(LogTag, "", mensaje, nivel: "INFO");

                return new ResultadoPublicacion
                {
                    Exito = true,
                    Actualizados = actualizados,
                    Insertados = insertados,
                    SinCambio = sinCambio,
                    DadosDeAlta = altas,
                    SinUsuario = sinUsuario,
                    Canjes = canjes,
                    Mensaje = mensaje
                };
            }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, "",
                    $"No se pudieron publicar los saldos: {ex.Message}", nivel: "ERROR");

                return new ResultadoPublicacion
                {
                    Exito = false,
                    Mensaje = "No se pudo publicar en Socio Tiburón: " + ex.Message
                };
            }
        }

        /// <summary>
        /// Publica un solo cliente. Se llama después de otorgar o ajustar, para que el
        /// cliente vea el número nuevo enseguida. Si falla, NO se propaga: el movimiento
        /// del ERP ya quedó bien guardado y la publicación se puede reintentar.
        /// </summary>
        public async Task PublicarClienteAsync(string cveCli)
        {
            if (string.IsNullOrWhiteSpace(cveCli)) return;

            try { await PublicarAsync(new[] { cveCli }); }
            catch (Exception ex)
            {
                LogErrorHelper.RegistrarLog(LogTag, cveCli,
                    $"No se pudo publicar el saldo tras el movimiento: {ex.Message}", nivel: "WARN");
            }
        }

        /// <summary>
        /// Compara lo que el ERP calcula contra lo que Socio Tiburón muestra. Sirve para
        /// ver qué clientes quedarían con otro número antes de publicar.
        /// </summary>
        public async Task<List<(string Cve, decimal SaldoErp, decimal SaldoWeb)>> CompararAsync()
        {
            var saldos = await LeerSaldosDelErpAsync(null);
            var resultado = new List<(string, decimal, decimal)>();

            if (saldos.Count == 0) return resultado;

            var enWeb = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            await using (var conn = new MySqlConnection(_cadenaMysql))
            {
                await conn.OpenAsync();

                // Una fila por cliente: `idcliente` es único. Antes esto llevaba un
                // GROUP BY con una subconsulta que se quedaba con el último idpuntos,
                // para adivinar cuál de los renglones repetidos leería el sitio.
                await using var cmd = new MySqlCommand(
                    "SELECT idcliente, puntos AS valor FROM puntos", conn);

                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string cve = reader["idcliente"]?.ToString()?.Trim() ?? "";
                    if (cve.Length == 0) continue;

                    decimal.TryParse(reader["valor"]?.ToString(), out decimal v);
                    enWeb[cve] = v;
                }
            }

            foreach (var cliente in saldos)
                resultado.Add((cliente.Cve, cliente.Saldo, enWeb.GetValueOrDefault(cliente.Cve)));

            return resultado.OrderByDescending(x => Math.Abs(x.Item2 - x.Item3)).ToList();
        }

        /// <summary>
        /// Trae al ledger del ERP los canjes que ocurrieron en Socio Tiburón.
        ///
        /// POR QUÉ EXISTE ESTO
        /// Los regalos se canjean en el sitio, que apunta la entrega en `entregaregalos`
        /// y le baja el saldo al cliente. El ERP no se enteraba, y como publicar
        /// SOBREESCRIBE `puntos` con lo que dice su ledger, la siguiente publicación le
        /// devolvía al cliente los puntos que ya se había gastado —y podía volver a
        /// canjear, sin fin—. Registrando el canje de este lado el saldo vuelve a ser uno
        /// solo: al publicar no hay nada que descontar porque ya viene descontado.
        ///
        /// Se importa TODO y no sólo los clientes que se van a publicar. Un canje sin
        /// registrar es un regalo que se paga dos veces, y eso no depende de a quién le
        /// tocaba publicar hoy.
        ///
        /// Se cuentan las entregas estén marcadas como entregadas o no, igual que el
        /// contador de canjes del portal: los puntos se gastan al canjear, y esperar a la
        /// entrega física dejaría una ventana en la que el cliente puede canjear otra vez
        /// lo que ya no tiene.
        ///
        /// La idempotencia es la del resto del módulo: `doc_clave` único, aquí con el
        /// formato `socio|canje|&lt;identregaregalos&gt;`. Correrlo mil veces registra el
        /// canje una sola vez, y lo impone el índice, no el cuidado de quien llama.
        /// </summary>
        private async Task<int> ImportarCanjesAsync(MySqlConnection conn)
        {
            var entregas = new List<(long Id, string Cve, DateTime Fecha, string Clave, int Costo)>();

            await using (var cmd = new MySqlCommand(@"
                SELECT identregaregalos,
                       idkep,
                       fecha,
                       COALESCE(clave, '')       AS clave,
                       COALESCE(puntos_costo, 0) AS puntos_costo
                FROM entregaregalos
                WHERE idkep IS NOT NULL AND idkep <> ''", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    entregas.Add((
                        Convert.ToInt64(reader["identregaregalos"]),
                        Texto(reader["idkep"]),
                        Convert.ToDateTime(reader["fecha"]),
                        Texto(reader["clave"]),
                        Convert.ToInt32(reader["puntos_costo"])));
            }

            if (entregas.Count == 0) return 0;

            await using var erp = new NpgsqlConnection(_cadenaErp);
            await erp.OpenAsync();

            // Qué canjes ya están en el ledger, de una sola consulta. Sin esto habría un
            // INSERT por entrega en cada publicación —todos rebotando contra el índice
            // único— y publicar se volvería más lento con cada regalo entregado.
            var yaEstan = new HashSet<string>(StringComparer.Ordinal);

            await using (var cmd = new NpgsqlCommand(
                "SELECT doc_clave FROM puntos_movimientos WHERE doc_clave LIKE 'socio|canje|%'", erp))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) yaEstan.Add(reader.GetString(0));
            }

            int importados = 0, sinCosto = 0, sinCliente = 0;

            foreach (var entrega in entregas)
            {
                string clave = $"socio|canje|{entrega.Id}";

                if (yaEstan.Contains(clave)) continue;

                // Sin costo no se sabe cuánto restar. Registrar cero sería peor que no
                // registrar: dejaría el canje marcado como ya importado y nadie volvería
                // a mirarlo.
                if (entrega.Costo <= 0) { sinCosto++; continue; }

                // El cliente se resuelve dentro del propio INSERT. Si la clave no existe
                // en catclientes el SELECT no devuelve filas, no se inserta nada y el
                // RETURNING viene vacío: se cuenta y se sigue, en vez de reventar la
                // publicación entera por un canje de un cliente que el ERP no conoce.
                //
                // `origen` se queda en NULL a propósito: sus únicos valores válidos son
                // 'kepler' y 'erp', que dicen de qué sistema salió la VENTA. Un canje no
                // es una venta. La procedencia va en doc_clave, que sí lo identifica.
                await using var ins = new NpgsqlCommand(@"
                    INSERT INTO puntos_movimientos
                        (cliente_id, cve_cli, tipo, puntos, doc_clave,
                         doc_fecha, doc_folio, comentario)
                    SELECT c.id_cliente, c.cve_cli, 'canje', @puntos, @clave,
                           @fecha, @folio, @comentario
                    FROM catclientes c
                    WHERE c.cve_cli = @cve
                    ORDER BY c.empresa_id, c.id_cliente
                    LIMIT 1
                    ON CONFLICT DO NOTHING
                    RETURNING id_movimiento", erp);

                ins.Parameters.AddWithValue("puntos", -(decimal)entrega.Costo);
                ins.Parameters.AddWithValue("clave", clave);
                ins.Parameters.AddWithValue("fecha", entrega.Fecha);
                ins.Parameters.AddWithValue("folio", entrega.Id.ToString());
                ins.Parameters.AddWithValue("cve", entrega.Cve);
                ins.Parameters.AddWithValue("comentario",
                    entrega.Clave.Length > 0
                        ? $"Canje de regalo en Socio Tiburón: {entrega.Clave}"
                        : "Canje de regalo en Socio Tiburón");

                object id = await ins.ExecuteScalarAsync();

                if (id is not null and not DBNull) importados++;
                else sinCliente++;
            }

            if (importados > 0 || sinCosto > 0 || sinCliente > 0)
                LogErrorHelper.RegistrarLog(LogTag, "",
                    $"Canjes de Socio Tiburón importados al ledger: {importados}" +
                    (sinCosto > 0 ? $"; {sinCosto} sin puntos_costo, sin registrar" : "") +
                    (sinCliente > 0 ? $"; {sinCliente} de claves que el ERP no reconoce" : ""),
                    nivel: importados > 0 ? "INFO" : "WARN");

            return importados;
        }

        /// <summary>Saldo del ERP junto con el contacto que necesita el alta.</summary>
        private sealed record SaldoCliente(
            string Cve, decimal Saldo, decimal Acumulado,
            string Nombre, string Telefono, string Correo);

        private async Task<List<SaldoCliente>> LeerSaldosDelErpAsync(IEnumerable<string> claves)
        {
            var lista = new List<SaldoCliente>();
            var filtro = claves?.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();

            await using var conn = new NpgsqlConnection(_cadenaErp);
            await conn.OpenAsync();

            // El contacto viaja junto con el saldo en la misma consulta: publicar en lote
            // recorre miles de clientes y preguntar por cada uno sería una ida al ERP por
            // cliente para un dato que casi nunca se usa —sólo hace falta cuando el
            // cliente todavía no existe en Socio Tiburón.
            // `total_acumulado` es LO GANADO, no lo que queda: canjear no des-gana puntos,
            // así que los canjes no se restan. Lo que sí se resta es lo que nunca llegó a
            // ganarse —una nota de crédito o una factura cancelada deshacen la venta que
            // los otorgó—, y los ajustes entran con su signo porque corrigen justamente
            // cuánto se otorgó. De ahí sale el nivel de membresía, y contarlo de menos
            // bajaría a un cliente de categoría por haber canjeado sus regalos.
            //
            // Los FILTER de la vista devuelven NULL cuando el cliente no tiene ese tipo
            // de movimiento —que es el caso de casi todos—, de ahí los COALESCE.
            string sql = @"
                SELECT s.cve_cli,
                       s.saldo,
                       COALESCE(s.acumulados,   0)
                         - COALESCE(s.devoluciones, 0)
                         - COALESCE(s.reversos,     0)
                         + COALESCE(s.ajustes,      0) AS acumulado,
                       COALESCE(c.n_cli, '') AS nombre,
                       COALESCE((SELECT t.telefono FROM telefonos_cliente t
                                  WHERE t.cliente_id = s.cliente_id
                                    AND COALESCE(t.telefono, '') <> ''
                                  ORDER BY t.id_telefono_cli LIMIT 1), '') AS telefono,
                       COALESCE((SELECT co.correo FROM correos_cliente co
                                  WHERE co.cliente_id = s.cliente_id
                                    AND COALESCE(co.correo, '') <> ''
                                  ORDER BY co.id_correo_cli LIMIT 1), '') AS correo
                FROM v_puntos_saldo s
                LEFT JOIN catclientes c ON c.id_cliente = s.cliente_id";

            if (filtro is { Length: > 0 }) sql += " WHERE s.cve_cli = ANY(@claves)";

            await using var cmd = new NpgsqlCommand(sql, conn);
            if (filtro is { Length: > 0 }) cmd.Parameters.AddWithValue("claves", filtro);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string cve = reader["cve_cli"]?.ToString()?.Trim() ?? "";
                if (cve.Length == 0) continue;

                lista.Add(new SaldoCliente(
                    cve,
                    reader["saldo"] is DBNull ? 0m : Convert.ToDecimal(reader["saldo"]),
                    reader["acumulado"] is DBNull ? 0m : Convert.ToDecimal(reader["acumulado"]),
                    Texto(reader["nombre"]),
                    Texto(reader["telefono"]),
                    Texto(reader["correo"])));
            }

            return lista;
        }

        /// <summary>
        /// La contraseña como se guarda: bcrypt `$2a$` con costo 11. Es el mismo
        /// algoritmo que ya usa el ERP para sus propios usuarios, así que el hash no
        /// depende de nada nuevo ni obliga a recordar un formato aparte.
        /// </summary>
        public static string HashClave(string clave) =>
            BCrypt.Net.BCrypt.HashPassword(clave ?? "", CostoBcrypt);

        private static string Texto(object valor) =>
            valor is null or DBNull ? "" : valor.ToString()?.Trim() ?? "";

        /// <summary>
        /// Todas las claves de una columna, en un conjunto que ignora mayúsculas —como
        /// las compara MySQL con su colación por omisión—. Las vacías se descartan: una
        /// clave en blanco haría parecer dado de alta a cualquier cliente sin clave.
        /// </summary>
        private static async Task<HashSet<string>> LeerClavesAsync(MySqlConnection conn, string sql)
        {
            var claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new MySqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string clave = Texto(reader[0]);
                if (clave.Length > 0) claves.Add(clave);
            }

            return claves;
        }

        /// <summary>
        /// El padrón entero como clave → acumulado histórico. Sirve para las dos cosas
        /// que necesita publicar: saber quién ya existe y saber a quién le cambió el
        /// acumulado, sin una consulta por cliente para ninguna de las dos.
        /// </summary>
        private static async Task<Dictionary<string, decimal>> LeerAcumuladosAsync(MySqlConnection conn)
        {
            var padron = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new MySqlCommand(
                "SELECT cliente, total_acumulado FROM clientes", conn);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string clave = Texto(reader["cliente"]);
                if (clave.Length == 0) continue;

                padron[clave] = reader["total_acumulado"] is DBNull
                    ? 0m
                    : Convert.ToDecimal(reader["total_acumulado"]);
            }

            return padron;
        }

        private static string Recortar(string valor, int largo)
        {
            valor = (valor ?? "").Trim();
            return valor.Length <= largo ? valor : valor[..largo];
        }

        /// <summary>Columna que admite NULL: vacío se guarda como NULL, no como "".</summary>
        private static object Opcional(string valor, int largo)
        {
            string v = Recortar(valor, largo);
            return v.Length == 0 ? DBNull.Value : v;
        }
    }
}
