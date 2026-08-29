using BOS_ERP.Filters;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using static BOS_ERP.Controllers.Ventas.Ventas_Industriales.VTPedidoController;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    public class CorteOperacionController : Utilities
    {
        // ─── 1. Listar cortes pendientes para el operador ────────────────────────
        [HttpGet]
        public IActionResult ObtenerCortesPendientes(DateTime? fechaDesde, DateTime? fechaHasta)
        {
            try
            {
                var hoy = DateTime.Today;
                DateTime desde = (fechaDesde ?? hoy).Date;
                DateTime hasta = (fechaHasta ?? hoy).Date.AddDays(1); // límite exclusivo

                var parameters = new Dictionary<string, object>
        {
            { "fechaDesde", desde },
            { "fechaHasta", hasta }
        };

                string query = @"
            SELECT
                a.id AS asignacion_id,
                a.pedido_detalle_corte_id,
                a.tarima_producto_corte_id,
                a.longitud_origen,
                a.cantidad_asignada,
                a.sobrante,
                a.estatus,
                a.fecha_confirmacion,
                a.usuario_confirmacion,
                c.longitud AS longitud_solicitada,
                c.cantidad AS cantidad_solicitada,
                c.comentario,
                tpc.folio,
                pz.codigo AS codigo_pieza,
                pd.cve_prod,
                pd.descr_prod,
                e.id_encabezado,
                e.folio AS folio_pedido,
                e.cli_prov
            FROM pedido_detalle_corte_asignacion a
            INNER JOIN pedido_detalle_corte c ON c.id = a.pedido_detalle_corte_id
            INNER JOIN tarima_productos_cortes tpc ON tpc.id_corte = a.tarima_producto_corte_id
            INNER JOIN partidasdoc pd ON pd.id_partidas = c.pedido_detalle_id
            INNER JOIN encabezadomov e ON e.id_encabezado = pd.encabezado_id
            LEFT JOIN corte_piezas pz ON pz.asignacion_id = a.id
            WHERE e.fch >= @fechaDesde AND e.fch < @fechaHasta
            ORDER BY e.id_encabezado, pd.nro_part, a.id;";

                var rows = RunQuery(query, parameters);

                // ★ Ya NO se inicializa fecha_inicio_surtido automáticamente al listar.
                // Ahora arranca cuando se imprime el ticket (ver IniciarSurtido más abajo).

                return Json(new { success = true, items = rows });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "CorteOperacion /ObtenerCortesPendientes");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Resolver un corte por su asignación, SIN filtro de fecha ─────────────
        // Lo usa el escaneo del QR del ticket: la pistola manda CO-{asignacion_id} y el
        // corte puede ser de cualquier día, no solo del rango cargado en pantalla.
        [HttpGet]
        public IActionResult ObtenerCortePorAsignacion(int asignacionId)
        {
            try
            {
                string query = @"
            SELECT
                a.id AS asignacion_id,
                a.pedido_detalle_corte_id,
                a.tarima_producto_corte_id,
                a.longitud_origen,
                a.cantidad_asignada,
                a.sobrante,
                a.estatus,
                a.fecha_confirmacion,
                a.usuario_confirmacion,
                c.longitud AS longitud_solicitada,
                c.cantidad AS cantidad_solicitada,
                c.comentario,
                tpc.folio,
                pz.codigo AS codigo_pieza,
                pd.cve_prod,
                pd.descr_prod,
                e.id_encabezado,
                e.folio AS folio_pedido,
                e.cli_prov
            FROM pedido_detalle_corte_asignacion a
            INNER JOIN pedido_detalle_corte c ON c.id = a.pedido_detalle_corte_id
            INNER JOIN tarima_productos_cortes tpc ON tpc.id_corte = a.tarima_producto_corte_id
            INNER JOIN partidasdoc pd ON pd.id_partidas = c.pedido_detalle_id
            INNER JOIN encabezadomov e ON e.id_encabezado = pd.encabezado_id
            LEFT JOIN corte_piezas pz ON pz.asignacion_id = a.id
            WHERE a.id = @asignacionId
            LIMIT 1;";

                var rows = RunQuery(query, new Dictionary<string, object> { { "asignacionId", asignacionId } });

                if (rows == null || rows.Count == 0)
                    return Json(new { success = false, message = "El corte escaneado no existe." });

                return Json(new { success = true, item = rows[0] });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "CorteOperacion /ObtenerCortePorAsignacion");
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult IniciarSurtido(int idEncabezado)
        {
            try
            {
                IniciarFechaInicioSurtidoTubo(idEncabezado);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "CorteOperacion /IniciarSurtido");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private void IniciarFechaInicioSurtidoTubo(int idEncabezado)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            using var conn = new NpgsqlConnection(connStr);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                int usuarioId = GetUserId(User.Identity.Name);

                var partidas = RunQuery(@"
            SELECT pd.id_partidas
            FROM partidasdoc pd
            WHERE pd.encabezado_id = @idEncabezado;",
                    new Dictionary<string, object> { { "idEncabezado", idEncabezado } }, false, conn, tx);

                foreach (var p in partidas)
                {
                    int idPartida = Convert.ToInt32(p["id_partidas"]);

                    var existente = RunQuery(@"
                SELECT id, fecha_inicio_surtido
                FROM verificacion_detalle
                WHERE id_partida = @idPartida AND encabezado_verificacion_id IS NULL
                ORDER BY id DESC LIMIT 1;",
                        new Dictionary<string, object> { { "idPartida", idPartida } }, false, conn, tx);

                    if (existente?.Count > 0)
                    {
                        bool sinFecha = existente[0]["fecha_inicio_surtido"] == null
                                      || existente[0]["fecha_inicio_surtido"] == DBNull.Value;

                        if (sinFecha)
                        {
                            RunQuery(@"
                        UPDATE verificacion_detalle
                           SET fecha_inicio_surtido = NOW(), usuario_inicio_id = @usr
                         WHERE id = @id;",
                                new Dictionary<string, object>
                                { { "id", Convert.ToInt32(existente[0]["id"]) }, { "usr", usuarioId } },
                                false, conn, tx);
                        }
                    }
                    else
                    {
                        RunQuery(@"
                    INSERT INTO verificacion_detalle
                        (encabezado_verificacion_id, id_partida, cve_almacen, n_almacen,
                         cantidad_verificada, lote, ubicacion,
                         usuario_id, fecha_inicio_surtido, usuario_inicio_id)
                    VALUES
                        (NULL, @idPartida, '', '', 0, '', '', @usr, NOW(), @usr);",
                            new Dictionary<string, object> { { "idPartida", idPartida }, { "usr", usuarioId } },
                            false, conn, tx);
                    }
                }

                tx.Commit();
            }
            catch
            {
                // ★ Side-effect: si falla, no debe tumbar la consulta principal de cortes pendientes
                try { tx.Rollback(); } catch { }
            }
        }
        // ─── 2. Confirmar: el operador respeta la sugerencia del sistema ─────────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Confirmacion de corte")]
        public IActionResult ConfirmarCorte([FromBody] ConfirmarCorteDto dto)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;
            try
            {
                if (dto.FechaCorte == null)
                    return Json(new { success = false, message = "Debes indicar la fecha escrita en el ticket antes de confirmar." });

                var utils = new Utilities(true);
                conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS"));
                conn.Open();
                trx = conn.BeginTransaction();

                var paramsLock = new Dictionary<string, object> { { "id", dto.AsignacionId } };
                var fila = RunQuery(@"
    SELECT a.id, a.estatus, c.pedido_detalle_id
    FROM pedido_detalle_corte_asignacion a
    INNER JOIN pedido_detalle_corte c ON c.id = a.pedido_detalle_corte_id
    WHERE a.id = @id FOR UPDATE;", paramsLock, false, conn, trx);

                if (fila.Count == 0)
                    throw new InvalidOperationException("La asignación no existe.");

                string estatusActual = fila[0]["estatus"].ToString();
                if (estatusActual == "confirmado")
                    throw new InvalidOperationException("Este corte ya fue confirmado previamente.");

                RunQuery(@"
    UPDATE pedido_detalle_corte_asignacion
    SET estatus = 'confirmado',
        fecha_confirmacion = now(),
        usuario_confirmacion = @usuario,
        fecha_corte_manual = @fechaCorte
    WHERE id = @id;",
                    new Dictionary<string, object>
                    {
                { "id", dto.AsignacionId },
                { "usuario", User.Identity.Name },
                { "fechaCorte", (object)dto.FechaCorte ?? DBNull.Value }
                    },
                    false, conn, trx);

                int idPartida = Convert.ToInt32(fila[0]["pedido_detalle_id"]);
                SincronizarVerificacionDetalleTubo(idPartida, User.Identity.Name, conn, trx);

                trx.Commit();
                return Json(new { success = true, message = "Corte confirmado." });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "CorteOperacion /ConfirmarCorte");
                try { trx?.Rollback(); } catch { }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }

        // ─── 3. Reasignar: el operador elige una pieza distinta a la sugerida ────
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industrial", Accion = "Reasignacion de corte")]
        public IActionResult ReasignarCorte([FromBody] ReasignarCorteDto dto)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trx = null;
            try
            {
                if (dto.FechaCorte == null)
                    return Json(new { success = false, message = "Debes indicar la fecha escrita en el ticket antes de confirmar." });
                var utils = new Utilities(true);
                conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS"));
                conn.Open();
                trx = conn.BeginTransaction();

                // 1. Bloquear y leer la asignación original
                var filaAsig = RunQuery(@"
                    SELECT id, pedido_detalle_corte_id, tarima_producto_corte_id, cantidad_asignada, estatus
                    FROM pedido_detalle_corte_asignacion
                    WHERE id = @id FOR UPDATE;",
                    new Dictionary<string, object> { { "id", dto.AsignacionId } }, false, conn, trx);

                if (filaAsig.Count == 0)
                    throw new InvalidOperationException("La asignación no existe.");
                if (filaAsig[0]["estatus"].ToString() == "confirmado")
                    throw new InvalidOperationException("No se puede reasignar un corte ya confirmado.");

                int corteLogicoId = Convert.ToInt32(filaAsig[0]["pedido_detalle_corte_id"]);
                int idCorteOriginal = Convert.ToInt32(filaAsig[0]["tarima_producto_corte_id"]);
                decimal cantidadAsignada = Convert.ToDecimal(filaAsig[0]["cantidad_asignada"]);

                // 2. Traer la longitud que originalmente pidió el cliente (para recalcular sobrante nuevo)
                var filaCorte = RunQuery(@"
    SELECT longitud, pedido_detalle_id FROM pedido_detalle_corte WHERE id = @id;",
                    new Dictionary<string, object> { { "id", corteLogicoId } }, false, conn, trx);
                decimal longitudSolicitada = Convert.ToDecimal(filaCorte[0]["longitud"]);
                int idPartida = Convert.ToInt32(filaCorte[0]["pedido_detalle_id"]);

                // La barra física que esta asignación tenía marcada ya no es la que se va a
                // cortar: se desvincula. El renglón de corte_piezas se queda 'usada' tal cual
                // (el trigger no la revive; el "+cantidad" de abajo crea una pieza NUEVA para
                // el stock que se libera), solo deja de apuntar a un pedido que ya no la usa.
                DesetiquetarPieza(conn, trx, dto.AsignacionId);

                // 3. LIBERAR la pieza original: regresar la cantidad reservada
                RunQuery(@"
                    UPDATE tarima_productos_cortes
                    SET cantidad = cantidad + @cant
                    WHERE id_corte = @id;",
                    new Dictionary<string, object> { { "cant", cantidadAsignada }, { "id", idCorteOriginal } },
                    false, conn, trx);

                // 4. Revertir el retazo fantasma que se había generado por la reserva original
                //    (solo si sigue intacto, es decir, nadie más lo usó todavía)
                var retazoFantasma = RunQuery(@"
                    SELECT id_corte, cantidad, cantidad_original
                    FROM tarima_productos_cortes
                    WHERE generado_por_asignacion_id = @asigId AND activo = true
                    FOR UPDATE;",
                    new Dictionary<string, object> { { "asigId", dto.AsignacionId } }, false, conn, trx);

                if (retazoFantasma.Count > 0)
                {
                    int idRetazo = Convert.ToInt32(retazoFantasma[0]["id_corte"]);
                    decimal cantidadActual = Convert.ToDecimal(retazoFantasma[0]["cantidad"]);
                    decimal cantidadOriginalRetazo = Convert.ToDecimal(retazoFantasma[0]["cantidad_original"]);

                    if (cantidadActual < cantidadOriginalRetazo)
                        throw new InvalidOperationException(
                            "No se puede reasignar: parte del retazo generado por esta reserva ya fue " +
                            "consumido por otro pedido. Se requiere intervención manual de almacén.");

                    // Nadie lo tocó todavía: se desactiva por completo (se anula la reserva original)
                    RunQuery(@"
                        UPDATE tarima_productos_cortes
                        SET activo = false, cantidad = 0
                        WHERE id_corte = @id;",
                        new Dictionary<string, object> { { "id", idRetazo } }, false, conn, trx);
                }

                // 5. Bloquear y validar la NUEVA pieza elegida por el operador
                var filaNueva = RunQuery(@"
                    SELECT id_corte, cantidad, longitud
                    FROM tarima_productos_cortes
                    WHERE id_corte = @idCorte AND folio = @folio AND activo = true
                    FOR UPDATE;",
                    new Dictionary<string, object> { { "idCorte", dto.NuevoIdCorte }, { "folio", dto.NuevoFolio ?? "" } },
                    false, conn, trx);

                if (filaNueva.Count == 0)
                    throw new InvalidOperationException($"La pieza con folio {dto.NuevoFolio} ya no existe o no coincide.");

                decimal cantidadDisponibleNueva = Convert.ToDecimal(filaNueva[0]["cantidad"]);
                decimal longitudNueva = Convert.ToDecimal(filaNueva[0]["longitud"]);

                if (cantidadDisponibleNueva < cantidadAsignada)
                    throw new InvalidOperationException(
                        $"Stock insuficiente en el folio elegido. Disponible: {cantidadDisponibleNueva}, " +
                        $"requerido: {cantidadAsignada}.");

                if (longitudNueva < longitudSolicitada - 0.001m)
                    throw new InvalidOperationException(
                        $"La pieza elegida ({longitudNueva}m) es más corta que lo solicitado ({longitudSolicitada}m).");

                // 6. Descontar la nueva pieza
                RunQuery(@"
                    UPDATE tarima_productos_cortes SET cantidad = cantidad - @cant WHERE id_corte = @id;",
                    new Dictionary<string, object> { { "cant", cantidadAsignada }, { "id", dto.NuevoIdCorte } },
                    false, conn, trx);

                // Enlaza la asignación con la barra física que el UPDATE de arriba acaba de
                // marcar 'usada' del nuevo folio elegido por el operador.
                EtiquetarPiezaConsumida(conn, trx, dto.NuevoIdCorte, dto.AsignacionId);

                // 7. Generar el retazo real de la nueva pieza, si aplica.
                // Mismo umbral mínimo que en AdminCortes/Crear: solo descarta ruido de
                // redondeo decimal, no retazos reales de piezas chicas.
                decimal sobranteNuevo = Math.Max(0, longitudNueva - longitudSolicitada);
                if (sobranteNuevo > 0.001m)
                {
                    string folioRetazoNuevo = RunScalar("SELECT fn_generar_folio_corte();",
                        new Dictionary<string, object>(), false, conn, trx).ToString();

                    RunQuery(@"
                        INSERT INTO tarima_productos_cortes
                            (folio, tarima_producto_id, longitud, cantidad, cantidad_original,
                             usuario_creacion, comentario, generado_por_asignacion_id)
                        SELECT @folio, tarima_producto_id, @longitud, @cantidadRetazo, @cantidadRetazo,
                               @usuario, 'Retazo generado por reasignación de corte', @asigId
                        FROM tarima_productos_cortes WHERE id_corte = @idOrigen;",
                        new Dictionary<string, object>
                        {
                            { "folio", folioRetazoNuevo },
                            { "longitud", sobranteNuevo },
                            { "cantidadRetazo", cantidadAsignada },
                            { "usuario", User.Identity.Name },
                            { "asigId", dto.AsignacionId },
                            { "idOrigen", dto.NuevoIdCorte }
                        }, false, conn, trx);
                }

                // 8. Actualizar la asignación para que apunte a la nueva pieza, y confirmarla
                //    (reasignar implica que el operador ya decidió y cortó de ahí)
                RunQuery(@"
    UPDATE pedido_detalle_corte_asignacion
    SET tarima_producto_corte_id = @nuevoId,
        longitud_origen = @longitudNueva,
        sobrante = @sobranteNuevo,
        estatus = 'confirmado',
        fecha_confirmacion = now(),
        usuario_confirmacion = @usuario,
        fecha_corte_manual = @fechaCorte
    WHERE id = @id;",
                    new Dictionary<string, object>
                    {
        { "nuevoId", dto.NuevoIdCorte },
        { "longitudNueva", longitudNueva },
        { "sobranteNuevo", sobranteNuevo },
        { "usuario", User.Identity.Name },
        { "id", dto.AsignacionId },
        { "fechaCorte", (object)dto.FechaCorte ?? DBNull.Value }
                    }, false, conn, trx);

                SincronizarVerificacionDetalleTubo(idPartida, User.Identity.Name, conn, trx);
                trx.Commit();
                return Json(new { success = true, message = "Corte reasignado y confirmado correctamente." });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "CorteOperacion /?");
                try { trx?.Rollback(); } catch { }
                return Json(new { success = false, message = ex.Message });
            }
            finally
            {
                trx?.Dispose();
                conn?.Dispose();
            }
        }


        // ─── Sincroniza verificacion_detalle cuando se completan TODOS los cortes de una partida ──
        private void SincronizarVerificacionDetalleTubo(int idPartida, string usuario, NpgsqlConnection conn, NpgsqlTransaction trx)
        {
            var pendientes = RunQuery(@"
        SELECT COUNT(*) AS c
        FROM pedido_detalle_corte c
        WHERE c.pedido_detalle_id = @idPartida
          AND NOT EXISTS (
              SELECT 1 FROM pedido_detalle_corte_asignacion a
              WHERE a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado'
          );",
                new Dictionary<string, object> { { "idPartida", idPartida } }, false, conn, trx);

            bool todoConfirmado = pendientes?.Count > 0 && Convert.ToInt32(pendientes[0]["c"]) == 0;
            if (!todoConfirmado) return; // aún faltan cortes de esta partida, no tocar nada

            // ★ La partida del tubo se mide en METROS (cant_ud/ud = MTR), no en piezas.
            //   Cada asignación confirmada representa UNA pieza cortada de longitud c.longitud,
            //   así que los metros surtidos = SUM(c.longitud) sobre las asignaciones confirmadas.
            //   (Usar SUM(c.cantidad) contaba PIEZAS —p. ej. 2 cortes de 0.5 m daban 2 en vez de
            //    1.0 m— y ese valor terminaba como cantidad de la remisión, inflando precio y el
            //    descuento de inventario en RegistrarMovimiento. Además el JOIN con asignaciones
            //    multiplica las filas cuando un corte tiene cantidad>1, por lo que se suma la
            //    longitud por pieza, no longitud*cantidad.)
            var resumen = RunQuery(@"
        SELECT
            COALESCE(SUM(c.longitud), 0)  AS cantidad_total,
            MIN(a.fecha_confirmacion)     AS fecha_min,
            MAX(a.fecha_confirmacion)     AS fecha_max
        FROM pedido_detalle_corte c
        INNER JOIN pedido_detalle_corte_asignacion a
            ON a.pedido_detalle_corte_id = c.id AND a.estatus = 'confirmado'
        WHERE c.pedido_detalle_id = @idPartida;",
                new Dictionary<string, object> { { "idPartida", idPartida } }, false, conn, trx);

            if (resumen == null || resumen.Count == 0) return;

            decimal cantidadTotal = Convert.ToDecimal(resumen[0]["cantidad_total"]);
            DateTime fechaInicio = Convert.ToDateTime(resumen[0]["fecha_min"]);
            DateTime fechaFin = Convert.ToDateTime(resumen[0]["fecha_max"]);
            int usuarioId = GetUserId(usuario);

            var vdExistente = RunQuery(@"
        SELECT id FROM verificacion_detalle
        WHERE id_partida = @idPartida AND encabezado_verificacion_id IS NULL
        ORDER BY id DESC LIMIT 1;",
                new Dictionary<string, object> { { "idPartida", idPartida } }, false, conn, trx);

            if (vdExistente?.Count > 0)
            {
                RunQuery(@"
            UPDATE verificacion_detalle
               SET cantidad_verificada  = @cant,
                   fecha_inicio_surtido = COALESCE(fecha_inicio_surtido, @finicio),
                   fecha_fin_surtido    = @ffin,
                   usuario_fin_id       = @usr
             WHERE id = @id;",
                    new Dictionary<string, object>
                    {
                { "cant", cantidadTotal }, { "finicio", fechaInicio },
                { "ffin", fechaFin }, { "usr", usuarioId }, { "id", Convert.ToInt32(vdExistente[0]["id"]) }
                    }, false, conn, trx);
            }
            else
            {
                RunQuery(@"
            INSERT INTO verificacion_detalle
                (encabezado_verificacion_id, id_partida, cve_almacen, n_almacen,
                 cantidad_verificada, lote, ubicacion,
                 usuario_id, fecha_inicio_surtido, usuario_inicio_id,
                 fecha_fin_surtido, usuario_fin_id)
            VALUES
                (NULL, @idPartida, '', '',
                 @cant, '', '',
                 @usr, @finicio, @usr,
                 @ffin, @usr);",
                    new Dictionary<string, object>
                    {
                { "idPartida", idPartida }, { "cant", cantidadTotal },
                { "usr", usuarioId }, { "finicio", fechaInicio }, { "ffin", fechaFin }
                    }, false, conn, trx);
            }
        }

        // Ata una asignación de pedido a la barra física (corte_piezas) que el trigger de
        // sincronía acaba de marcar 'usada' para ese id_corte. Si el módulo de piezas no está
        // instalado (sql/cortes_piezas.sql sin correr), la tabla no existe y esto se ignora: la
        // operación de corte sigue funcionando igual, solo sin el código de barras de la pieza.
        private void EtiquetarPiezaConsumida(NpgsqlConnection conn, NpgsqlTransaction trx, int idCorteFisico, int idAsignacion)
        {
            try
            {
                RunQuery(@"
            UPDATE corte_piezas
            SET asignacion_id = @idAsignacion
            WHERE id_pieza = (
                SELECT id_pieza FROM corte_piezas
                WHERE corte_id = @idCorteFisico AND estado = 'usada' AND asignacion_id IS NULL
                ORDER BY fecha_baja DESC, id_pieza ASC
                LIMIT 1
            );",
                    new Dictionary<string, object>
                    {
                        { "idAsignacion", idAsignacion }, { "idCorteFisico", idCorteFisico }
                    }, false, conn, trx);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { /* corte_piezas no existe todavía */ }
        }

        // Contraparte de arriba: al reasignar, la pieza que esta asignación tenía marcada deja
        // de ser la que se va a cortar. Se desvincula (no se borra ni se revive su estado).
        private void DesetiquetarPieza(NpgsqlConnection conn, NpgsqlTransaction trx, int idAsignacion)
        {
            try
            {
                RunQuery(@"UPDATE corte_piezas SET asignacion_id = NULL WHERE asignacion_id = @idAsignacion;",
                    new Dictionary<string, object> { { "idAsignacion", idAsignacion } }, false, conn, trx);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { /* corte_piezas no existe todavía */ }
        }

        public class ConfirmarCorteDto
        {
            public int AsignacionId { get; set; }
            public DateTime? FechaCorte { get; set; }   // ★ fecha escrita a mano en el ticket
        }

        public class ReasignarCorteDto
        {
            public int AsignacionId { get; set; }
            public int NuevoIdCorte { get; set; }
            public string NuevoFolio { get; set; }
            public DateTime? FechaCorte { get; set; }   // ★ fecha escrita a mano en el ticket
        }
    }
}