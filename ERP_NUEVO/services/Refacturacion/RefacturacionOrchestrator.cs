// Services/Refacturacion/RefacturacionOrchestrator.cs
using BOS_ERP.Controllers;
using BOS_ERP.Controllers.Facturacion.Productos;
using BOS_ERP.Models;
using BOS_ERP.services.Facturacion;
using Npgsql;
using System.IO;

namespace BOS_ERP.Services.Refacturacion
{
    public class RefacturacionOrchestrator : Utilities
    {
        private readonly IPacService _pac;
        private readonly RefacturacionHandlerFactory _handlerFactory;

        // Dependencias para re-emitir la NOTA DE CRÉDITO por aplicación de anticipo contra el
        // CFDI sustituto (mismas que construye VIFactura para NotaCreditoController).
        // OJO: _configuration NO se redeclara — es heredado de Utilities; redeclararlo lo ocultaría
        // y dejaría en null el que usan los helpers base.
        private readonly BOS_ERP.Services.EmailSender _emailSender;
        private readonly IWebHostEnvironment _env;
        private readonly Microsoft.AspNetCore.Mvc.Razor.IRazorViewEngine _viewEngine;
        private readonly Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider _tempDataProvider;
        private readonly XmlBuilderService _xmlBuilderService;
        private readonly IComprobanteFiscalService _comprobanteFiscal;

        public RefacturacionOrchestrator(
            IPacService pac,
            RefacturacionHandlerFactory handlerFactory,
            IConfiguration configuration,
            BOS_ERP.Services.EmailSender emailSender,
            IWebHostEnvironment env,
            Microsoft.AspNetCore.Mvc.Razor.IRazorViewEngine viewEngine,
            Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider tempDataProvider,
            XmlBuilderService xmlBuilderService,
            IComprobanteFiscalService comprobanteFiscal)
        {
            _pac = pac; _handlerFactory = handlerFactory;
            _configuration = configuration;
            _emailSender = emailSender;
            _env = env;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _xmlBuilderService = xmlBuilderService;
            _comprobanteFiscal = comprobanteFiscal;
        }

        public async Task<RefacturacionResultado> EjecutarAsync(RefacturacionRequest req, NpgsqlConnection conn, NpgsqlTransaction tx, string usuario)
        {
            int usuarioId = Utilities.GetUserId(usuario);
            var handler = _handlerFactory.ObtenerHandler(req.TipoId);

            var validacion = await handler.ValidarAsync(req.EncabezadoIdOriginal, req.Cambios);
            if (!validacion.EsValido)
                return RefacturacionResultado.Fallido(string.Join(" | ", validacion.Errores));

            // ── Camino A: Adenda "solo-corregir" — no cancela, no timbra ──
            if (!validacion.RequiereTimbradoNuevo)
            {
                string uuidRegenerado = await handler.RegenerarSinTimbrarAsync(req.EncabezadoIdOriginal, req.Cambios, conn, tx);
                RegistrarBitacoraRefacturacion(req.TipoId, req.UuidOriginal, uuidRegenerado, usuario, conn, tx, "solo-corregir");
                return RefacturacionResultado.Exitoso(uuidRegenerado);
            }

            // ── Camino B: sustitución (cancelar-reemitir) ──
            // Orden correcto SAT: se timbra PRIMERO el nuevo (relacionado 04 → original) y
            // DESPUÉS se cancela el original pasando folioSustitucion = UUID nuevo (motivo 01).
            string uuidOriginal = req.UuidOriginal;
            string tipoRelacion = string.IsNullOrWhiteSpace(req.TipoRelacion) ? "04" : req.TipoRelacion;

            // 1. Reconstruir Factura + armar XML del nuevo CFDI.
            var construido = await handler.ConstruirXmlNuevoAsync(
                req.EncabezadoIdOriginal, req.Cambios, req.Motivo, tipoRelacion, uuidOriginal);

            // 2. Clonar el encabezado ANTES de timbrar: si el esquema/constraints fallan, abortamos
            //    sin haber tocado el SAT. Si el timbrado falla luego, el rollback deshace este clon.
            int nuevoEncabezadoId = ClonarEncabezado(req.EncabezadoIdOriginal, conn, tx);

            // 2.5 Ajuste de inventario + remisión de variación (tipos que cambian partidas, p.ej.
            //     conceptos): revierte las remisiones de origen, genera la remisión-variación con las
            //     partidas nuevas, re-descuenta y cancela las anteriores. Se hace ANTES de timbrar
            //     para que un fallo de stock (o de timbrado) deje TODO en rollback.
            if (validacion.AjustaInventario)
            {
                // Qué hacer con las partidas pendientes de facturar de las remisiones de origen
                // (lo elige el usuario en el wizard): 'descartar' | 'facturar' | 'nueva-remision'.
                string estrategiaPendientes = "descartar";
                if (req.Cambios != null && req.Cambios.TryGetValue("pendientesEstrategia", out var estRaw) && estRaw != null)
                    estrategiaPendientes = estRaw is System.Text.Json.JsonElement je
                        ? (je.ValueKind == System.Text.Json.JsonValueKind.String ? je.GetString() : je.ToString())
                        : estRaw.ToString();

                string errInv = AjustarInventarioConceptos(
                    req.EncabezadoIdOriginal, nuevoEncabezadoId, construido.Factura.Tproductos,
                    usuarioId, usuario, estrategiaPendientes, conn, tx);
                if (!string.IsNullOrEmpty(errInv))
                    return RefacturacionResultado.Fallido(errInv);
            }

            // 3. Timbrar el nuevo ANTES de cancelar (necesitamos su UUID como sustituto).
            var timbrado = await _pac.TimbrarAsync(construido.Xml, Guid.NewGuid().ToString());
            if (!timbrado.Success)
                return RefacturacionResultado.Fallido("Error al timbrar nuevo CFDI: " + timbrado.Message);

            // A partir de aquí el CFDI nuevo ya existe en el SAT (irreversible): no volvemos a
            // hacer rollback del timbrado; los fallos posteriores se resuelven dejando estados
            // consistentes y avisando, nunca "perdiendo" el CFDI real.

            // 4. Completar la Factura con los datos del timbre y persistirla con TODAS sus columnas.
            var factura = construido.Factura;
            int facturaOriginalId = factura.IdFactura;   // id de la factura ORIGINAL (para re-relacionar anticipos)
            // "TIMBRADA" en mayúsculas: es el valor canónico que escribe FacturaRepository y
            // el que compara con igualdad exacta todo el circuito de notas de crédito
            // (VentaNotaCredito, SolicitudNC, NCApplicationService, ConsultaNotasCredito) y
            // el analizador de refacturación. Escribirlo capitalizado dejaba a las facturas
            // refacturadas fuera de todos esos módulos: no se les podía emitir NC ni
            // refacturarlas de nuevo, y no sumaban en los totales.
            factura.StatusFactura = "TIMBRADA";
            factura.EncabezadoId = nuevoEncabezadoId;
            factura.TipoRelacion = tipoRelacion;         // '04' sustitución
            factura.UUIDsRelacionados = uuidOriginal;    // el CFDI original (para el PDF)
            AplicarDatosTimbre(factura, timbrado);
            int nuevaFacturaId = GuardarFacturaNueva(factura, conn, tx);

            // 5. Contabilidad del nuevo CFDI (póliza de ingreso + cartera CxC).
            //    Reusa los helpers del ERP, que derivan todo a partir del encabezado clonado.
            RegistrarContabilidadNuevaFactura(nuevoEncabezadoId, usuarioId, conn, tx);

            // 5.5 Cancelar las NOTAS DE CRÉDITO POR APLICACIÓN DE ANTICIPO (nat='NT') de la factura
            //     original. Van ANTES de cancelar la factura porque son CFDI hijos que la referencian
            //     (primero los hijos, luego el padre). Motivo 02 = emitido con errores SIN relación:
            //     la nota sustituta aún no existe (se emite después, contra el CFDI nuevo).
            var notasAnticipoOriginales = NotasAplicacionAnticipo(req.EncabezadoIdOriginal, conn, tx);
            foreach (var nt in notasAnticipoOriginales)
            {
                string ntUuid = nt["uuid"]?.ToString() ?? "";
                string ntRfc = nt["rfcemisor"]?.ToString();
                if (string.IsNullOrWhiteSpace(ntRfc)) ntRfc = req.RfcEmisor;
                if (string.IsNullOrWhiteSpace(ntUuid)) continue;

                var cancelNt = await _pac.CancelarAsync(ntRfc, ntUuid, "02", null);
                if (!cancelNt.Success)
                {
                    // El CFDI nuevo ya está timbrado (irreversible): no se cancela el original si su
                    // nota de anticipo sigue viva, para no dejar la cadena inconsistente.
                    RegistrarBitacoraRefacturacion(req.TipoId, uuidOriginal, timbrado.UUID, usuario, conn, tx, "nc-anticipo-cancelacion-fallida");
                    return new RefacturacionResultado
                    {
                        Success = false,
                        UuidNuevo = timbrado.UUID,
                        FacturaNueva = factura,
                        Message = $"Se timbró el CFDI sustituto ({timbrado.UUID}) pero NO se pudo cancelar la nota de crédito " +
                                  $"por aplicación de anticipo {ntUuid}: {cancelNt.Message}. La factura original NO se canceló."
                    };
                }

                RunUpdate("UPDATE factura SET statusfactura='Cancelada' WHERE uuid=@uuid",
                    new Dictionary<string, object> { { "uuid", Guid.Parse(ntUuid) } }, false, conn, tx);
                RunUpdate("UPDATE encabezadomov SET estatus_id=27 WHERE id_encabezado=@enc",
                    new Dictionary<string, object> { { "enc", nt["id_encabezado"] } }, false, conn, tx);
            }

            // 6. Cancelar el original en el PAC con folioSustitucion = UUID nuevo.
            if (validacion.RequiereCancelacion)
            {
                var cancelacion = await _pac.CancelarAsync(req.RfcEmisor, uuidOriginal, req.Motivo, timbrado.UUID);
                if (!cancelacion.Success)
                {
                    // El nuevo ya está timbrado; dejamos el original marcado como pendiente de
                    // cancelación (se reintenta manualmente) y confirmamos la parte ya realizada.
                    RunUpdate(
                        "UPDATE factura SET statusfactura='Pendiente Cancelación' WHERE uuid=@uuid",
                        new Dictionary<string, object> { { "uuid", Guid.Parse(uuidOriginal) } }, false, conn, tx);

                    RegistrarRelacionCfdi(uuidOriginal, timbrado.UUID, tipoRelacion, conn, tx);
                    RegistrarBitacoraRefacturacion(req.TipoId, uuidOriginal, timbrado.UUID, usuario, conn, tx, "reemitido-cancelacion-pendiente");

                    return new RefacturacionResultado
                    {
                        Success = true,
                        UuidNuevo = timbrado.UUID,
                        FacturaNueva = factura,
                        Message = "CFDI nuevo timbrado correctamente, pero la cancelación del original quedó PENDIENTE: " + cancelacion.Message
                    };
                }

                // 7. Cancelación aceptada → revertir la contabilidad del original.
                RevertirContabilidadOriginal(req.EncabezadoIdOriginal, nuevoEncabezadoId, facturaOriginalId, uuidOriginal, usuarioId, conn, tx);
            }

            // 7.5 Transferir la APLICACIÓN de anticipos en factura_anticipos: se cancela la del
            //     original (movimiento compensatorio) y se crea el reemplazo contra el sustituto.
            TransferirAnticiposAlSustituto(facturaOriginalId, nuevaFacturaId, usuarioId, conn, tx);

            // 8. Re-emitir la NOTA DE CRÉDITO por aplicación de anticipo, ahora contra el CFDI
            //    sustituto (último paso del flujo: nueva → cancelar notas → cancelar original → nueva nota).
            if (notasAnticipoOriginales.Count > 0)
            {
                string errorNc = RecrearNotasAnticipo(notasAnticipoOriginales, factura, nuevoEncabezadoId, timbrado.UUID, conn, tx);
                if (!string.IsNullOrEmpty(errorNc))
                {
                    RegistrarBitacoraRefacturacion(req.TipoId, uuidOriginal, timbrado.UUID, usuario, conn, tx, "nc-anticipo-reemision-fallida");
                    return new RefacturacionResultado
                    {
                        Success = false,
                        UuidNuevo = timbrado.UUID,
                        FacturaNueva = factura,
                        Message = "La factura se sustituyó y las notas de anticipo se cancelaron, pero NO se pudo " +
                                  "re-emitir la nota de crédito por aplicación de anticipo: " + errorNc
                    };
                }
            }

            RegistrarRelacionCfdi(uuidOriginal, timbrado.UUID, tipoRelacion, conn, tx);
            RegistrarBitacoraRefacturacion(req.TipoId, uuidOriginal, timbrado.UUID, usuario, conn, tx, "cancelar-reemitir");

            var resultadoOk = RefacturacionResultado.Exitoso(timbrado.UUID);
            resultadoOk.FacturaNueva = factura;
            return resultadoOk;
        }

        // ── Completar la Factura con UUID, sellos, cadena original y fecha del timbre. ──
        private void AplicarDatosTimbre(Factura factura, TimbradoPacResult timbrado)
        {
            factura.UUID = timbrado.UUID;
            factura.XmlFactura = timbrado.XmlTimbrado;

            // Reusa el lector oficial del ERP para poblar sellos/cadena desde el XML timbrado.
            if (!string.IsNullOrWhiteSpace(timbrado.RutaXmlLocal) && System.IO.File.Exists(timbrado.RutaXmlLocal))
                _comprobanteFiscal.ComplementarDesdeXml(factura, timbrado.RutaXmlLocal);
        }

        // ── Crea el encabezadomov del sustituto clonando el del original. ──
        // Copia TODAS las columnas reales de la tabla (salvo la PK) y sobreescribe solo
        // los campos propios de la sustitución. Así GenerarDatosPoliza —que deriva la póliza
        // del encabezado— produce la contrapartida de ingreso equivalente sin cablear cuentas.
        private int ClonarEncabezado(int encabezadoOriginal, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            // Resuelve la tabla vía search_path (robusto al esquema) y lista sus columnas reales.
            var columnas = RunQuery(
                @"SELECT a.attname AS column_name
                  FROM pg_attribute a
                  WHERE a.attrelid = 'encabezadomov'::regclass
                    AND a.attnum > 0
                    AND NOT a.attisdropped
                    AND a.attname <> 'id_encabezado'
                  ORDER BY a.attnum",
                new Dictionary<string, object>(), false, conn, tx)
                .Select(r => r["column_name"].ToString())
                .ToList();

            // Expresión SELECT por columna: valor propio salvo overrides de la sustitución.
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "encabezados_padre", "@orig" },   // liga el sustituto al CFDI original
                { "fch",              "NOW()" },
                { "variacion",        "COALESCE(variacion,0) + 1" }, // re-emisión: nueva variación del folio
            };

            var selectExprs = columnas.Select(c =>
                overrides.TryGetValue(c, out var expr) ? $"{expr} AS {c}" : $"\"{c}\"");

            string sql =
                $"INSERT INTO encabezadomov ({string.Join(", ", columnas.Select(c => $"\"{c}\""))}) " +
                $"SELECT {string.Join(", ", selectExprs)} " +
                $"FROM encabezadomov WHERE id_encabezado = @orig " +
                $"RETURNING id_encabezado";

            return Convert.ToInt32(RunScalar(sql,
                new Dictionary<string, object> { { "orig", encabezadoOriginal } }, false, conn, tx));
        }

        // ── Inserta la nueva factura (todas las columnas fiscales) + sus partidas. ──
        // Mismo conjunto de columnas que FacturacionVentaController.GuardarFactura.
        private int GuardarFacturaNueva(Factura factura, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            string strSQL = @"
INSERT INTO factura
(serie, folio, idtipofactura, idcliente, rfccliente, rsocliente, emlcliente, idemisor, rfcemisor,
 rsoemisor, idexpedicion, idusuario, fecha, fechatimbrado, statusfactura, mdpfactura, textfactura,
 idlugarexp, idtipopago, uuid, importe, descuento, subtotal, iva, total, saldo, idpedido,
 retisr, retiva, moneda, observaciones, idvendedor, usocfdi, idusocfdi, cbb, parcialidad,
 sellosat, sellocfdi, cadenaoriginal, oc, tdc, anticipo, reg_fisr, reg_fise, cpr, cpe, tipo, encabezado_id)
VALUES
(@serie, @folio, @idtipofactura, @idcliente, @rfccliente, @rsocliente, @emlcliente, @idemisor, @rfcemisor,
 @rsoemisor, @idexpedicion, @idusuario, @fecha, @fechatimbrado, @statusfactura, @mdpfactura, @xmlfactura,
 @idlugarexp, @idtipopago, @uuid, @importe, @descuento, @subtotal, @iva, @total, @saldo, @idpedido,
 @retisr, @retiva, @moneda, @observaciones, @idvendedor, @usocfdi, @idusocfdi, @cbb, @parcialidad,
 @sellosat, @sellocfdi, @cadenaoriginal, @oc, @tdc, @anticipo, @reg_fisr, @reg_fise, @cpr, @cpe, @tipo, @encabezado_id)
RETURNING id;";

            var parameters = new Dictionary<string, object>
            {
                ["serie"] = factura.Serie,
                ["folio"] = factura.Folio,
                ["idtipofactura"] = factura.IdTipoFactura,
                ["idcliente"] = factura.IdCliente,
                ["rfccliente"] = factura.RfcCliente,
                ["rsocliente"] = factura.RsoCliente,
                ["emlcliente"] = factura.EmlCliente,
                ["idemisor"] = factura.IdEmisor,
                ["rfcemisor"] = factura.RfcEmisor,
                ["rsoemisor"] = factura.RsoEmisor,
                ["idexpedicion"] = factura.IdExpedicion,
                ["idusuario"] = factura.IdUsuario,
                ["fecha"] = factura.Fecha,
                ["fechatimbrado"] = string.IsNullOrEmpty(factura.FechaTimbrado)
                    ? factura.Fecha
                    : DateTime.Parse(factura.FechaTimbrado),
                ["statusfactura"] = factura.StatusFactura,
                ["mdpfactura"] = factura.MdpFactura,
                ["xmlfactura"] = factura.XmlFactura,
                ["idlugarexp"] = factura.IdLugarExp,
                ["idtipopago"] = int.TryParse(factura.IdTipoPago, out var itp) ? itp : 0,
                ["uuid"] = string.IsNullOrWhiteSpace(factura.UUID)
                    ? Guid.NewGuid()
                    : Guid.Parse(factura.UUID),
                ["importe"] = factura.Importe,
                ["descuento"] = factura.Descuento,
                ["subtotal"] = factura.Subtotal,
                ["iva"] = factura.IVA,
                ["total"] = factura.Total,
                ["saldo"] = factura.Saldo,
                ["idpedido"] = factura.IdPedido,
                ["retisr"] = factura.RetISR,
                ["retiva"] = factura.RetIVA,
                ["moneda"] = factura.Moneda,
                ["observaciones"] = factura.Observaciones,
                ["idvendedor"] = factura.IdVendedor,
                ["usocfdi"] = factura.UsoCFDI,
                ["idusocfdi"] = factura.IdUsoCFDI,
                ["cbb"] = factura.Cbb,
                ["parcialidad"] = 0,
                ["sellosat"] = factura.SelloSAT,
                ["sellocfdi"] = factura.SelloCFDI,
                ["cadenaoriginal"] = factura.CadenaOriginal,
                ["oc"] = factura.Oc,
                ["tdc"] = factura.Tdc,
                ["anticipo"] = factura.Anticipo ? 1m : 0m,
                ["reg_fisr"] = factura.Rege,
                ["reg_fise"] = factura.Regc,
                ["cpr"] = factura.CpR,
                ["cpe"] = factura.CpE,
                ["tipo"] = (factura.TipoFacturacion ?? "").ToUpper(),
                ["encabezado_id"] = factura.EncabezadoId
            };

            int id = Convert.ToInt32(RunScalar(strSQL, parameters, false, conn, tx));

            foreach (System.Data.DataRow row in factura.Tproductos.Rows)
            {
                string sqlDetalle = @"
INSERT INTO dfactura
(idfac, idproducto, descripcion, cantidad, precio, cpr, descuento, saldo, udm,
 claveprodserv, claveprod, idndv, lote, pedimento, cant_ndc, comentario)
VALUES
(@idfac, @idproducto, @descripcion, @cantidad, @precio, @cpr, @descuento, @saldo, @udm,
 @claveprodserv, @claveprod, @idndv, @lote, @pedimento, @cant_ndc, @comentario);";

                var parametrosDetalle = new Dictionary<string, object>
                {
                    ["idfac"] = id,
                    ["idproducto"] = 1,
                    ["descripcion"] = row["descripcion"],
                    ["cantidad"] = row["cantidad"],
                    ["precio"] = row["precioUnit"],
                    ["cpr"] = 0,
                    ["descuento"] = row.Table.Columns.Contains("descuento") ? row["descuento"] : 0,
                    ["saldo"] = 0,
                    ["udm"] = row["unidad"],
                    ["claveprodserv"] = row["claveProdServ"],
                    ["claveprod"] = row["numero"],
                    ["idndv"] = 0,
                    ["lote"] = "-",
                    ["pedimento"] = "-",
                    ["cant_ndc"] = 0,
                    ["comentario"] = row.Table.Columns.Contains("comentario")
                        ? (row["comentario"]?.ToString() ?? "")
                        : ""
                };

                RunUpdate(sqlDetalle, parametrosDetalle, false, conn, tx);
            }

            return id;
        }

        // ── Contabilidad del CFDI sustituto: póliza de ingreso + cartera (CxC). ──
        // NOTA: para una sustitución NO se toca inventario (la mercancía sigue entregada).
        private void RegistrarContabilidadNuevaFactura(int nuevoEncabezadoId, int usuarioId, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var datosPoliza = GenerarDatosPoliza(nuevoEncabezadoId, null, null, conn, tx);
            var polizas = RegistrarPolizas(usuarioId, nuevoEncabezadoId, datosPoliza, false, null, conn, tx);
            var x =RegistrarCartera(polizas[0].idPoliza, usuarioId, conn, tx);
        }

        // ── Revertir la contabilidad del CFDI original al aceptarse la cancelación. ──
        // Espeja FacturacionVentaController.CancelarFactura, PERO sin revertir inventario:
        // en una sustitución la mercancía no se devuelve.
        private void RevertirContabilidadOriginal(int encabezadoOriginal, int encabezadoNuevo, int facturaOriginalId, string uuidOriginal, int usuarioId, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            // El encabezado del sustituto se recibe directo desde EjecutarAsync (ClonarEncabezado);
            // NO se re-deriva por `encabezados_padre = encabezadoOriginal`, porque el sustituto NO
            // cuelga de la factura original sino de su REMISIÓN (con el ajuste de inventario, de la
            // remisión-variación) → esa query devolvía 0 y refacturar_cartera_cliente no movía nada.
            RunQuery("SELECT refacturar_cartera_cliente(@factura_original, @factura_nueva, @id_usuario)", new Dictionary<string, object> { { "factura_original", encabezadoOriginal }, { "factura_nueva", encabezadoNuevo }, { "id_usuario", usuarioId } }, false, conn, tx);

            RunUpdate("UPDATE factura SET statusfactura='Cancelada' WHERE uuid=@uuid",
                new Dictionary<string, object> { { "uuid", Guid.Parse(uuidOriginal) } }, false, conn, tx);

            RunUpdate("UPDATE encabezadomov SET estatus_id=27 WHERE id_encabezado=@enc",
                new Dictionary<string, object> { { "enc", encabezadoOriginal } }, false, conn, tx);

            RunUpdate("UPDATE polizas SET cancelada=true, estado='cancelada' WHERE referencia=@enc",
                new Dictionary<string, object> { { "enc", encabezadoOriginal } }, false, conn, tx);

            var cobros = RunQuery(
                "SELECT id_encabezado FROM encabezadomov WHERE encabezados_padre=@enc AND nat='CXC'",
                new Dictionary<string, object> { { "enc", encabezadoOriginal } }, false, conn, tx);

        }

        // Stock por tarima (misma query que VIRemision): prioriza la tarima que cubre la cantidad
        // exacta, luego la de mayor stock. Devuelve id_tarima, stock, id_almacen, cve_alm, udm.
        private const string SqlStockTarima = @"
            SELECT ct.id_tarima, tp.cantidad AS stock, c.id_almacen, c.cve_almacen AS cve_alm, cp.udm
            FROM   catalmacenes c
            INNER JOIN catsucursales    cs ON cs.id_sucursal      = c.sucursal_id
            INNER JOIN catracks         cr ON cr.almacen_id       = c.id_almacen
            INNER JOIN catcolumnas      cc ON cc.rack_id          = cr.id_rack
            INNER JOIN catniveles       cn ON cn.columna_id       = cc.id_columna
            INNER JOIN cattarimas       ct ON ct.nivel_id         = cn.id_nivel
            INNER JOIN tarima_productos tp ON tp.tarima_id        = ct.id_tarima
            INNER JOIN catproductos     cp ON cp.id_catproductos  = tp.producto_id
            WHERE cs.id_sucursal = @sucursal AND c.tipo = 'Stock'
              AND tp.producto_id = @producto_id AND tp.cantidad > 0
            ORDER BY CASE WHEN tp.cantidad >= @cantidad THEN 0 ELSE 1 END, tp.cantidad DESC";

        private const int ESTATUS_CANCELADO = 27;

        // ── Ajuste de inventario + REMISIÓN DE VARIACIÓN para refacturación de conceptos. ──
        // En este ERP la FACTURA no mueve inventario: quien lo descuenta es la REMISIÓN (VIRemision),
        // ligado al encabezado de la remisión. La factura puede provenir de VARIAS remisiones
        // (relación en `factura_remisiones_origen`, por encabezado).
        //
        // Flujo (todo en la MISMA transacción; se ejecuta ANTES de timbrar, así que cualquier fallo
        // deja todo en rollback):
        //   1. Revertir el inventario de TODAS las remisiones de origen (restaura stock a sus tarimas).
        //   2. Crear SIEMPRE una remisión nueva como VARIACIÓN (`srs.clone_documento`, variacion+1 y
        //      variacion_padre = remisión principal) con TODAS las partidas de la refacturación.
        //   3. Re-descontar esas partidas contra la remisión-variación (valida stock disponible).
        //   4. Cancelar las remisiones anteriores y dejar la variación como respaldo de la factura
        //      sustituta (factura_remisiones_origen + remision_partidas_facturadas = 'completa').
        //
        // Las partidas eliminadas en el wizard simplemente no se re-descuentan → su stock regresa.
        // Devuelve null si OK, o el mensaje de error que aborta la refacturación. No mueven inventario
        // los servicios (ClaveUnidad E48) ni las partidas cuya clave no exista en catproductos.
        private string AjustarInventarioConceptos(int encOriginal, int nuevoEncabezadoId,
            System.Data.DataTable partidas, int usuarioId, string usuario, string estrategiaPendientes,
            NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            // ── Remisiones de origen de la factura (pueden ser VARIAS) ──
            List<Dictionary<string, object>> remisiones;
            try
            {
                remisiones = RunQuery(
                    @"SELECT DISTINCT encabezado_remision_id
                      FROM factura_remisiones_origen
                      WHERE encabezado_factura_id = @enc
                      ORDER BY encabezado_remision_id",
                    new Dictionary<string, object> { { "enc", encOriginal } }, false, conn, tx);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                return "No se pudo verificar la remisión de origen (falta la tabla factura_remisiones_origen). " +
                       "Ajusta el inventario manualmente antes de refacturar.";
            }

            if (remisiones.Count == 0)
                return "No se identificó la remisión de origen de esta factura, por lo que no es posible ajustar " +
                       "el inventario ni generar la remisión de variación. Realiza el ajuste manualmente.";

            var remIds = remisiones.Select(r => Convert.ToInt32(r["encabezado_remision_id"])).ToList();

            // Ninguna remisión de origen puede respaldar OTRA factura: se van a cancelar, y eso dejaría
            // a esa otra factura sin respaldo (además de devolverle stock que no le corresponde).
            foreach (int rid in remIds)
            {
                int otras = Convert.ToInt32(RunScalar(
                    @"SELECT COUNT(DISTINCT encabezado_factura_id)
                      FROM factura_remisiones_origen
                      WHERE encabezado_remision_id = @rem",
                    new Dictionary<string, object> { { "rem", rid } }, false, conn, tx));
                if (otras > 1)
                    return $"La remisión (encabezado {rid}) también respalda otras facturas; al refacturar se " +
                           "cancelaría y esas facturas quedarían sin respaldo. Ajusta el inventario manualmente.";
            }

            // La variación se clona de la PRIMERA remisión de origen; ahí se consolidan todas las partidas.
            int remPrincipal = remIds[0];

            int sucursal = Convert.ToInt32(RunScalar(
                "SELECT suc FROM encabezadomov WHERE id_encabezado = @e",
                new Dictionary<string, object> { { "e", remPrincipal } }, false, conn, tx));

            // ── Normalizar las partidas de la refacturación (el producto se resuelve UNA sola vez) ──
            var lineas = new List<PartidaAjuste>();
            if (partidas != null)
            {
                foreach (System.Data.DataRow row in partidas.Rows)
                {
                    string cveProd = partidas.Columns.Contains("numero") ? row["numero"]?.ToString() ?? "" : "";
                    decimal cant = Convert.ToDecimal(row["cantidad"]);
                    decimal precio = Convert.ToDecimal(row["precioUnit"]);
                    decimal dtoPct = partidas.Columns.Contains("descuento") && row["descuento"] != DBNull.Value
                        ? Convert.ToDecimal(row["descuento"]) : 0m;
                    decimal ivaLinea = partidas.Columns.Contains("iva") && row["iva"] != DBNull.Value
                        ? Convert.ToDecimal(row["iva"]) : 0m;

                    int idProd = 0;
                    if (!string.IsNullOrWhiteSpace(cveProd))
                    {
                        var idRaw = RunScalar(
                            @"SELECT id_catproductos FROM catproductos
                              WHERE cve_prod = @cve_prod
                                AND empresa_id = (SELECT empresa_id FROM catsucursales WHERE id_sucursal = @sucursal)",
                            new Dictionary<string, object> { { "cve_prod", cveProd }, { "sucursal", sucursal } },
                            false, conn, tx);
                        if (idRaw != null && idRaw != DBNull.Value) idProd = Convert.ToInt32(idRaw);
                    }

                    lineas.Add(new PartidaAjuste
                    {
                        IdProducto = idProd,
                        CveProd = cveProd,
                        Descripcion = row["descripcion"]?.ToString() ?? "",
                        Unidad = partidas.Columns.Contains("unidad") ? row["unidad"]?.ToString() ?? "" : "",
                        ClaveUnidad = partidas.Columns.Contains("claveUnidad") ? row["claveUnidad"]?.ToString() ?? "" : "",
                        Cantidad = cant,
                        PrecioUnit = precio,
                        DescuentoPct = dtoPct,
                        ImporteNeto = Math.Round(cant * precio * (1 - dtoPct / 100m), 2),
                        Iva = ivaLinea
                    });
                }
            }

            if (lineas.Count == 0)
                return "No hay partidas para generar la remisión de variación.";

            // 1) Revertir el inventario de TODAS las remisiones de origen.
            foreach (int rid in remIds)
            {
                try { RevertirMovimientoInventario(rid, usuarioId, conn, tx); }
                catch (Exception ex) { return $"No se pudo revertir el inventario de la remisión {rid}: " + ex.Message; }
            }

            // 2) Generar SIEMPRE la remisión de variación con todas las partidas de la refacturación.
            int fPago = 0;
            var fpRaw = RunScalar("SELECT COALESCE(f_pago, 0) FROM encabezadomov WHERE id_encabezado = @e",
                new Dictionary<string, object> { { "e", encOriginal } }, false, conn, tx);
            if (fpRaw != null && fpRaw != DBNull.Value) fPago = Convert.ToInt32(fpRaw);

            int remNueva;
            try
            {
                remNueva = ClonarRemision(remPrincipal, lineas, fPago, usuarioId,
                    $"Variación por refacturación de la factura (encabezado {encOriginal})", conn, tx);
            }
            catch (Exception ex)
            {
                return "No se pudo generar la remisión de variación: " + ex.Message;
            }

            // 3) Re-descontar las partidas nuevas contra la remisión-variación (valida stock).
            string errDesc = DescontarLineas(lineas, sucursal, remNueva, usuarioId,
                "Ajuste por refacturación de conceptos", conn, tx);
            if (errDesc != null) return errDesc;

            // 3.5 PARTIDAS PENDIENTES de facturar de las remisiones de origen. Como esas remisiones
            //     se cancelan, hay que decidir su destino (lo elige el usuario en el wizard):
            //       'facturar'       → el front ya las agregó como conceptos, van en `lineas`: nada que hacer.
            //       'descartar'      → no se re-descuentan: su stock se queda devuelto al almacén.
            //       'nueva-remision' → se genera OTRA remisión solo con las pendientes y se les
            //                          re-descuenta el stock, para poder facturarlas después.
            if (string.Equals(estrategiaPendientes, "nueva-remision", StringComparison.OrdinalIgnoreCase))
            {
                var pendientes = PendientesDeRemisiones(remIds, sucursal, conn, tx);
                if (pendientes.Count > 0)
                {
                    // OJO: se clona desde `remNueva`, NO desde remPrincipal. clone_documento asigna
                    // `variacion = original.variacion + 1`, así que clonar dos veces el mismo origen
                    // produciría DOS documentos con el mismo folio-variación. Encadenando desde la
                    // variación queda: remisión original → variación (facturada) → variación pendientes.
                    int remPend;
                    try
                    {
                        remPend = ClonarRemision(remNueva, pendientes, fPago, usuarioId,
                            $"Pendientes de facturar tras refacturación (factura enc. {encOriginal})", conn, tx);
                    }
                    catch (Exception ex)
                    {
                        return "No se pudo generar la remisión de pendientes: " + ex.Message;
                    }

                    // Esa mercancía sí salió del almacén: se vuelve a descontar contra su remisión.
                    // No se le crean filas en remision_partidas_facturadas: así queda como remisión
                    // nueva, totalmente pendiente de facturar (VIFactura las inicializa al facturarla).
                    string errPend = DescontarLineas(pendientes, sucursal, remPend, usuarioId,
                        "Pendientes de facturar tras refacturación", conn, tx);
                    if (errPend != null) return errPend;
                }
            }

            // 4) Cancelar las remisiones anteriores (su inventario YA se revirtió en el paso 1).
            foreach (int rid in remIds)
                RunUpdate("UPDATE encabezadomov SET estatus_id = @cancel WHERE id_encabezado = @rem",
                    new Dictionary<string, object> { { "cancel", ESTATUS_CANCELADO }, { "rem", rid } },
                    false, conn, tx);

            // La factura sustituta debe colgar de la remisión-variación, no de la cancelada
            // (VIFactura hace lo mismo: encabezados_padre = remisión de origen).
            RunUpdate("UPDATE encabezadomov SET encabezados_padre = @rem WHERE id_encabezado = @enc",
                new Dictionary<string, object> { { "rem", remNueva }, { "enc", nuevoEncabezadoId } },
                false, conn, tx);

            // Dejar la variación ligada a la factura SUSTITUTA y marcada como ya facturada; si no,
            // volvería a aparecer como remisión pendiente de facturar.
            try
            {
                RunQuery(@"
                    INSERT INTO remision_partidas_facturadas
                        (encabezado_remision_id, id_partida_remision, cve_prod, cantidad_original,
                         cantidad_facturada, precio_unitario, descuento, usuario_registro, estatus)
                    SELECT @rem, pd.id_partidas, pd.cve_prod, pd.cant_ud, pd.cant_ud,
                           pd.pv_prod, COALESCE(pd.dto1, 0), @usuario, 'completa'
                    FROM partidasdoc pd
                    WHERE pd.encabezado_id = @rem
                    ON CONFLICT (encabezado_remision_id, id_partida_remision) DO NOTHING",
                    new Dictionary<string, object> { { "rem", remNueva }, { "usuario", usuario ?? "" } },
                    false, conn, tx);

                RunQuery(@"
                    INSERT INTO factura_remisiones_origen
                        (encabezado_factura_id, encabezado_remision_id, id_partida_remision, cve_prod,
                         cantidad_facturada, precio_unitario, descuento, importe, usuario_facturo)
                    SELECT @enc_fac, @rem, pd.id_partidas, pd.cve_prod, pd.cant_ud,
                           pd.pv_prod, COALESCE(pd.dto1, 0), pd.imp_part, @usuario
                    FROM partidasdoc pd
                    WHERE pd.encabezado_id = @rem
                    ON CONFLICT (encabezado_factura_id, encabezado_remision_id, id_partida_remision) DO NOTHING",
                    new Dictionary<string, object>
                    { { "enc_fac", nuevoEncabezadoId }, { "rem", remNueva }, { "usuario", usuario ?? "" } },
                    false, conn, tx);
            }
            catch (PostgresException) { /* trazabilidad: no aborta la refacturación */ }

            return null;
        }

        // Crea una remisión nueva como VARIACIÓN de `remOrigen` con las partidas dadas, usando la
        // función de BD srs.clone_documento (variacion+1, variacion_padre = remOrigen). Se llama con
        // 5 argumentos para que p_estatus tome su default NULL → hereda el estatus del original, por
        // eso hay que clonar ANTES de cancelar la remisión de origen. Devuelve el encabezado nuevo.
        private int ClonarRemision(int remOrigen, List<PartidaAjuste> lineas, int fPago, int usuarioId,
            string observaciones, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            // Claves EXACTAS que lee srs.clone_documento del jsonb (las ausentes van null: `->>`
            // sobre JSON null devuelve SQL NULL, seguro para sus casts ::NUMERIC).
            string partidasJson = Newtonsoft.Json.JsonConvert.SerializeObject(
                lineas.Select(l => new Dictionary<string, object>
                {
                    { "CveProd",    l.CveProd },
                    { "DescrProd",  l.Descripcion },
                    { "Ud",         l.Unidad },
                    { "CveVdrCpr",  null },
                    { "IdProducto", l.IdProducto > 0 ? (object)l.IdProducto : null },
                    { "Ref",        null },
                    { "CantUd",     l.Cantidad },
                    { "PvProd",     l.PrecioUnit },
                    { "ImpPart",    l.ImporteNeto },
                    { "FPagoId",    fPago > 0 ? (object)fPago : null },
                    { "Dto1",       l.DescuentoPct }
                }));

            decimal total = Math.Round(lineas.Sum(l => l.ImporteNeto) + lineas.Sum(l => l.Iva), 2);

            var clon = RunQuery(
                @"SELECT idencabezado, foliodoc
                  FROM srs.clone_documento(@id_original, @total, @obs, @usuario, @partidas::jsonb)",
                new Dictionary<string, object>
                {
                    { "id_original", remOrigen },
                    { "total",       total },
                    { "obs",         observaciones },
                    { "usuario",     usuarioId },
                    { "partidas",    partidasJson }
                }, false, conn, tx);

            if (clon.Count == 0 || clon[0]["idencabezado"] == DBNull.Value)
                throw new Exception("clone_documento no devolvió encabezado.");

            return Convert.ToInt32(clon[0]["idencabezado"]);
        }

        // Descuenta de inventario (salida) las líneas dadas contra `encDestino`, resolviendo tarimas
        // de forma greedy con SqlStockTarima. Devuelve null si OK o el mensaje de falta de stock.
        // No mueven inventario los servicios (E48) ni las líneas sin producto de catálogo.
        private string DescontarLineas(List<PartidaAjuste> lineas, int sucursal, int encDestino,
            int usuarioId, string motivo, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            foreach (var l in lineas)
            {
                if (l.IdProducto <= 0 || l.Cantidad <= 0) continue;
                if (l.ClaveUnidad.Equals("E48", StringComparison.OrdinalIgnoreCase)) continue;

                var tarimas = RunQuery(SqlStockTarima, new Dictionary<string, object>
                {
                    { "sucursal", sucursal }, { "producto_id", l.IdProducto }, { "cantidad", l.Cantidad }
                }, false, conn, tx);

                decimal disponible = tarimas.Sum(t => Convert.ToDecimal(t["stock"]));
                if (disponible < l.Cantidad)
                    return $"Sin stock suficiente para el producto '{l.CveProd}': disponible {disponible}, requerido {l.Cantidad}.";

                decimal restante = l.Cantidad;
                foreach (var t in tarimas)
                {
                    if (restante <= 0) break;
                    decimal tomar = Math.Min(Convert.ToDecimal(t["stock"]), restante);

                    var prod = new Dictionary<string, object>
                    {
                        { "id_producto",  l.IdProducto },
                        { "codigo",       l.CveProd },
                        { "descripcion",  l.Descripcion },
                        { "cantidad",     tomar },
                        { "unidad",       Convert.ToInt32(RunScalar(
                              "SELECT id_udm FROM catunidades WHERE cve_udm = @cve_udm",
                              new Dictionary<string, object> { { "cve_udm", t["udm"] } }, false, conn, tx)) },
                        { "tarima_id",    Convert.ToInt32(t["id_tarima"]) },
                        { "tipo",         "venta" },
                        { "movimiento",   "salida" }
                    };
                    RegistrarMovimiento(new List<Dictionary<string, object>> { prod }, usuarioId, "venta",
                        Convert.ToInt32(t["id_tarima"]), null, "salida", encDestino, motivo, conn, tx);

                    restante -= tomar;
                }
            }
            return null;
        }

        // Partidas que quedaron PENDIENTES de facturar en las remisiones de origen
        // (remision_partidas_facturadas: cantidad_original > cantidad_facturada).
        private List<PartidaAjuste> PendientesDeRemisiones(List<int> remIds, int sucursal,
            NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var lista = new List<PartidaAjuste>();

            foreach (int rid in remIds)
            {
                List<Dictionary<string, object>> filas;
                try
                {
                    filas = RunQuery(@"
                        SELECT rpf.cve_prod,
                               COALESCE(pd.descr_prod, '') AS descr_prod,
                               COALESCE(pd.ud, '')         AS ud,
                               (rpf.cantidad_original - rpf.cantidad_facturada) AS pendiente,
                               rpf.precio_unitario,
                               COALESCE(rpf.descuento, 0)  AS descuento
                        FROM remision_partidas_facturadas rpf
                        LEFT JOIN partidasdoc pd ON pd.id_partidas = rpf.id_partida_remision
                        WHERE rpf.encabezado_remision_id = @rem
                          AND rpf.cantidad_original > rpf.cantidad_facturada",
                        new Dictionary<string, object> { { "rem", rid } }, false, conn, tx);
                }
                catch (PostgresException) { continue; }

                foreach (var f in filas)
                {
                    decimal cant = Convert.ToDecimal(f["pendiente"]);
                    if (cant <= 0) continue;

                    string cve = f["cve_prod"]?.ToString() ?? "";
                    decimal precio = f["precio_unitario"] != DBNull.Value ? Convert.ToDecimal(f["precio_unitario"]) : 0m;
                    decimal dto = f["descuento"] != DBNull.Value ? Convert.ToDecimal(f["descuento"]) : 0m;

                    int idProd = 0;
                    if (!string.IsNullOrWhiteSpace(cve))
                    {
                        var idRaw = RunScalar(
                            @"SELECT id_catproductos FROM catproductos
                              WHERE cve_prod = @cve_prod
                                AND empresa_id = (SELECT empresa_id FROM catsucursales WHERE id_sucursal = @sucursal)",
                            new Dictionary<string, object> { { "cve_prod", cve }, { "sucursal", sucursal } },
                            false, conn, tx);
                        if (idRaw != null && idRaw != DBNull.Value) idProd = Convert.ToInt32(idRaw);
                    }

                    lista.Add(new PartidaAjuste
                    {
                        IdProducto = idProd,
                        CveProd = cve,
                        Descripcion = f["descr_prod"]?.ToString() ?? "",
                        Unidad = f["ud"]?.ToString() ?? "",
                        ClaveUnidad = "",
                        Cantidad = cant,
                        PrecioUnit = precio,
                        DescuentoPct = dto,
                        ImporteNeto = Math.Round(cant * precio * (1 - dto / 100m), 2),
                        Iva = 0m
                    });
                }
            }

            return lista;
        }

        // Partida ya normalizada (producto resuelto) que se usa tanto para el jsonb de
        // clone_documento como para el re-descuento de inventario.
        private class PartidaAjuste
        {
            public int IdProducto { get; set; }
            public string CveProd { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public string Unidad { get; set; } = "";
            public string ClaveUnidad { get; set; } = "";
            public decimal Cantidad { get; set; }
            public decimal PrecioUnit { get; set; }
            public decimal DescuentoPct { get; set; }
            public decimal ImporteNeto { get; set; }
            public decimal Iva { get; set; }
        }

        // ── Transferir la aplicación de anticipos de la factura original al sustituto. ──
        // Regla: NUNCA se edita una fila de factura_anticipos; se inserta el movimiento
        // compensatorio (negativo) para cancelar la aplicación anterior y luego el reemplazo
        // (positivo) contra la factura nueva. Igual que hace VentaNotaCredito con sus reversos.
        //
        // IMPORTANTE — el `saldo` del ANTICIPO (tabla factura) NO se toca: el anticipo sigue
        // consumido, la aplicación solo cambia de factura. Volver a restarlo lo duplicaría
        // (el descuento se hizo por código al aplicarlo originalmente, no hay trigger).
        private void TransferirAnticiposAlSustituto(int facturaOriginalId, int facturaNuevaId,
            int usuarioId, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            if (facturaOriginalId <= 0 || facturaNuevaId <= 0) return;

            List<Dictionary<string, object>> aplicaciones;
            try
            {
                aplicaciones = RunQuery(
                    @"SELECT id_factura_anticipo, SUM(monto_aplicado) AS monto
                      FROM factura_anticipos
                      WHERE id_factura_principal = @orig
                      GROUP BY id_factura_anticipo
                      HAVING SUM(monto_aplicado) > 0",
                    new Dictionary<string, object> { { "orig", facturaOriginalId } }, false, conn, tx);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                return; // sin tabla de anticipos: nada que transferir
            }

            const string insertAplicacion = @"
                INSERT INTO factura_anticipos
                    (id_factura_principal, id_factura_anticipo, monto_aplicado, fecha_aplicacion,
                     usuario_aplica, observaciones, saldo_antes, saldo_despues)
                VALUES
                    (@principal, @anticipo, @monto, NOW(), @usuario, @obs, @saldo_antes, @saldo_despues)";

            foreach (var a in aplicaciones)
            {
                int idAnticipo = Convert.ToInt32(a["id_factura_anticipo"]);
                decimal monto = Convert.ToDecimal(a["monto"]);

                // Saldo vigente del anticipo: se registra igual en antes/después porque la
                // transferencia NO lo modifica (queda constancia explícita de que no se movió).
                var saldoRaw = RunScalar("SELECT COALESCE(saldo, 0) FROM factura WHERE id = @ant",
                    new Dictionary<string, object> { { "ant", idAnticipo } }, false, conn, tx);
                decimal saldoAnticipo = (saldoRaw != null && saldoRaw != DBNull.Value)
                    ? Convert.ToDecimal(saldoRaw) : 0m;

                // 1) Cancelar la aplicación de la factura ORIGINAL (compensatorio negativo).
                RunUpdate(insertAplicacion, new Dictionary<string, object>
                {
                    { "principal", facturaOriginalId },
                    { "anticipo", idAnticipo },
                    { "monto", -monto },
                    { "usuario", usuarioId },
                    { "obs", $"Reverso por refacturación: la aplicación pasa a la factura {facturaNuevaId}" },
                    { "saldo_antes", saldoAnticipo },
                    { "saldo_despues", saldoAnticipo }
                }, false, conn, tx);

                // 2) Crear el reemplazo contra la factura SUSTITUTA (mismo monto).
                RunUpdate(insertAplicacion, new Dictionary<string, object>
                {
                    { "principal", facturaNuevaId },
                    { "anticipo", idAnticipo },
                    { "monto", monto },
                    { "usuario", usuarioId },
                    { "obs", $"Re-aplicación por refacturación (sustituye a la factura {facturaOriginalId})" },
                    { "saldo_antes", saldoAnticipo },
                    { "saldo_despues", saldoAnticipo }
                }, false, conn, tx);
            }
        }

        // ── Re-emitir la(s) nota(s) de crédito por aplicación de anticipo contra el sustituto. ──
        // Espeja VIFacturaController.ConstruirFacturaParaNC + NotaCreditoController.GenerarXml, pero
        // derivando los importes/centro de costos/forma de pago de la NOTA ORIGINAL en lugar del
        // formulario HTTP (el orquestador corre sin Request.Form).
        // Devuelve null si todo salió bien, o el mensaje de error de la primera nota que falle.
        private string RecrearNotasAnticipo(List<Dictionary<string, object>> notasOriginales,
            Factura facturaNueva, int nuevoEncabezadoId, string uuidFacturaNueva,
            NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            foreach (var nt in notasOriginales)
            {
                decimal monto = DecDe(nt, "total");
                if (monto <= 0) continue;

                decimal subtotal = DecDe(nt, "subtotal");
                decimal iva = DecDe(nt, "iva");
                if (subtotal <= 0)
                {
                    subtotal = Math.Round(monto / 1.16m, 2, MidpointRounding.AwayFromZero);
                    iva = monto - subtotal;
                }

                // UUIDs de los ANTICIPOS que relacionaba la nota original (CfdiRelacionados 07).
                string uuidsAnticipos = UuidsRelacionados07(
                    nt.TryGetValue("textfactura", out var xmlNt) ? xmlNt?.ToString() : null);

                // ── 1) Documento interno de la nota (TpMov="NT" → nat='NT'), colgado del encabezado NUEVO ──
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 4,
                    IdTpDoc = 81,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "NT",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr3 = GetUserId(User.Identity.Name),
                    Fch3 = DateTime.Now,
                    Imp = monto,
                    Dto = 0m,
                    Sub = subtotal,
                    CliProv = facturaNueva.RsoCliente,
                    Ref = nuevoEncabezadoId,
                    Ccy = facturaNueva.Moneda,
                    Estatus = 11,
                    Flete = 0m,
                    Coment1 = $"Nota de crédito por anticipo. Factura: {uuidFacturaNueva}",
                    EncabezadoPadre = nuevoEncabezadoId,   // cuelga del CFDI sustituto
                    PlDias = 0,
                    FchPgEntrega = DateTime.Now,
                    Par = facturaNueva.TipoCambio,
                    Mdp = "PUE",
                    TipoPoceso = "aplicacion_anticipo",
                    CentroCostos = IntDe(nt, "centro_costos"),
                };

                // Hereda la forma de pago de la nota original (f_pago es FK: 0 la violaría).
                int fPagoNota = IntDe(nt, "f_pago");
                if (fPagoNota > 0)
                    encabezado.FPago = fPagoNota;

                var partidasNC = new List<PartidaDocumento>
                {
                    new PartidaDocumento
                    {
                        CveProd   = "ANTICIPO",
                        DescrProd = $"Aplicación de anticipo a factura {uuidFacturaNueva}",
                        CantUd    = 1m,
                        PvProd    = subtotal,
                        Dto1      = 0m,
                        ImpPart   = subtotal,
                        Ud        = "ACT",
                        TpDocAnt  = "NC_ANTICIPO"
                    }
                };

                var folio = GenerarDocumentoConPartidas(encabezado, partidasNC, conn, tx);
                int idEncNota = Convert.ToInt32(folio["IdEncabezado"]);

                RunQuery(@"
                    INSERT INTO imp_oc
                        (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
                    VALUES
                        (@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id)",
                    new Dictionary<string, object>
                    {
                        { "encabezado_id", idEncNota },
                        { "impuesto_id",   Convert.ToInt32(GetSetting("impuesto")) },
                        { "subtotal",      subtotal },
                        { "importe",       iva },
                        { "orden_apl",     1 },
                        { "imp_variable",  16 },
                        { "prov_nom",      facturaNueva.RsoCliente },
                        { "f_pago_id",     IntDe(nt, "f_pago") }
                    }, false, conn, tx);

                var polizaNc = GenerarDatosPoliza(idEncNota, null, null, conn, tx);
                RegistrarPolizas(GetUserId(User.Identity.Name), idEncNota, polizaNc, false, null, conn, tx);

                // ── 2) CFDI de egreso de la nota (relación 07 hacia los anticipos) ──
                var nc = new Factura
                {
                    Serie = "NC",
                    FolioCorto = facturaNueva.Folio,
                    Folio = facturaNueva.Folio,
                    TipoDeComprobante = "E",
                    TipoRelacion = "07",
                    UUIDsRelacionados = uuidsAnticipos,
                    TipoFacturacion = "APLICACION_ANTICIPO",
                    RfcEmisor = facturaNueva.RfcEmisor,
                    RsoEmisor = facturaNueva.RsoEmisor,
                    Rege = facturaNueva.Rege,
                    CpE = facturaNueva.CpE,
                    RfcCliente = facturaNueva.RfcCliente,
                    RsoCliente = facturaNueva.RsoCliente,
                    CpR = facturaNueva.CpR,
                    Regc = facturaNueva.Regc,
                    IdUsoCFDI = "CP01",
                    Moneda = facturaNueva.Moneda,
                    TipoCambio = facturaNueva.TipoCambio,
                    Subtotal = subtotal,
                    IVA = iva,
                    Total = monto,
                    Saldo = monto,
                    IdTipoPago = facturaNueva.IdTipoPago,
                    metodoPagoTexto = "PUE",
                    MdpFactura = facturaNueva.MdpFactura,
                    Fecha = DateTime.Now,
                    LugarExpedicion = facturaNueva.LugarExpedicion,
                    EncabezadoId = idEncNota,   // la nota cuelga de SU propio encabezado
                    IdCliente = facturaNueva.IdCliente,
                    Observaciones = $"Nota de crédito por aplicación de anticipos a factura UUID: {uuidFacturaNueva}",
                };

                nc.Tproductos = new System.Data.DataTable();
                nc.Tproductos.Columns.AddRange(new[]
                {
                    new System.Data.DataColumn("numero",        typeof(string)),
                    new System.Data.DataColumn("claveProdServ", typeof(string)),
                    new System.Data.DataColumn("claveUnidad",   typeof(string)),
                    new System.Data.DataColumn("unidad",        typeof(string)),
                    new System.Data.DataColumn("descripcion",   typeof(string)),
                    new System.Data.DataColumn("cantidad",      typeof(double)),
                    new System.Data.DataColumn("precioUnit",    typeof(double)),
                    new System.Data.DataColumn("importe",       typeof(double)),
                    new System.Data.DataColumn("objetoImp",     typeof(string)),
                    new System.Data.DataColumn("descuento",     typeof(double)),
                    new System.Data.DataColumn("iva",           typeof(double)),
                    new System.Data.DataColumn("ieps",          typeof(double)),
                });
                nc.Tproductos.Rows.Add(
                    "ANTICIPO", "84111506", "ACT", "Actividad",
                    $"Aplicación de anticipo a factura {uuidFacturaNueva}",
                    1.0, (double)subtotal, (double)subtotal, "02", 0.0, 16.0, 0.0);

                var ncController = new NotaCreditoController(
                    _configuration, _emailSender, _env, _viewEngine, _tempDataProvider, _xmlBuilderService);
                ncController.ControllerContext = this.ControllerContext;

                var resultadoNc = ncController.GenerarXml(nc);
                if (!resultadoNc.Success)
                    return resultadoNc.Message;
            }

            return null;
        }

        // UUIDs del nodo CfdiRelacionados con TipoRelacion="07" (anticipos) de un XML timbrado.
        private static string UuidsRelacionados07(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return "";
            try
            {
                var xdoc = System.Xml.Linq.XDocument.Parse(xml);
                System.Xml.Linq.XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";
                var nodo = xdoc.Descendants(cfdi + "CfdiRelacionados")
                    .FirstOrDefault(n => (string)n.Attribute("TipoRelacion") == "07");
                if (nodo == null) return "";
                return string.Join(",", nodo.Descendants(cfdi + "CfdiRelacionado")
                    .Select(r => (string)r.Attribute("UUID"))
                    .Where(u => !string.IsNullOrWhiteSpace(u)));
            }
            catch { return ""; }
        }

        private static decimal DecDe(Dictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToDecimal(v) : 0m;

        private static int IntDe(Dictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToInt32(v) : 0;

        // Notas de crédito por APLICACIÓN DE ANTICIPO colgadas de una factura:
        // encabezadomov con nat='NT' y encabezados_padre = encabezado de la factura.
        // Se usan para el flujo: emitir sustituto → cancelar estas notas → cancelar la factura
        // original → crear la nota nueva contra el sustituto.
        private List<Dictionary<string, object>> NotasAplicacionAnticipo(int encabezadoFactura, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            try
            {
                // Se filtra por el ENCABEZADO (estatus_id 27 = cancelado) y se hace LEFT JOIN a
                // factura, porque ConstruirFacturaParaNC deja nc.EncabezadoId apuntando al encabezado
                // de la FACTURA (no al de la nota), así que la fila puede no colgar de este encabezado.
                return RunQuery(
                    @"SELECT em.id_encabezado, em.centro_costos, em.f_pago, em.mdp,
                             fa.id AS factura_id, fa.uuid::text AS uuid, fa.folio,
                             fa.total, fa.subtotal, fa.iva, fa.rfcemisor, fa.textfactura
                      FROM encabezadomov em
                      LEFT JOIN factura fa ON fa.encabezado_id = em.id_encabezado
                      WHERE em.encabezados_padre = @enc
                        AND em.nat = 'NT'
                        AND COALESCE(em.estatus_id, 0) <> 27
                      ORDER BY em.id_encabezado DESC",
                    new Dictionary<string, object> { { "enc", encabezadoFactura } }, false, conn, tx);
            }
            catch { return new List<Dictionary<string, object>>(); }
        }

        private void RegistrarRelacionCfdi(string uuidOriginal, string uuidNuevo, string tipoRelacion,
            NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            // Tolerante: si la tabla refacturacion_relaciones aún no existe, no rompe la operación.
            try
            {
                RunUpdate(
                    @"INSERT INTO refacturacion_relaciones
                      (uuid_original, uuid_nuevo, tipo_relacion, fecha)
                      VALUES (@orig, @nuevo, @tipo, NOW())",
                    new Dictionary<string, object>
                    {
                        { "orig", Guid.Parse(uuidOriginal) },
                        { "nuevo", Guid.Parse(uuidNuevo) },
                        { "tipo", tipoRelacion }
                    }, false, conn, tx);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                LogErrorHelper.RegistrarLog("Refacturacion_Relacion", uuidNuevo,
                    "Tabla refacturacion_relaciones no existe; se omitió el registro.", nivel: "WARN");
            }
        }

        private void RegistrarBitacoraRefacturacion(string tipoId, string uuidOriginal, string uuidNuevo,
            string usuario, NpgsqlConnection conn, NpgsqlTransaction tx, string modo)
        {
            // Tolerante: si la tabla refacturacion_log aún no existe, no rompe la operación.
            try
            {
                RunUpdate(
                    @"INSERT INTO refacturacion_log
                      (tipo, uuid_original, uuid_nuevo, usuario, estatus, modo, fecha)
                      VALUES (@tipo, @orig, @nuevo, @usr, 'completado', @modo, NOW())",
                    new Dictionary<string, object>
                    {
                        { "tipo", tipoId },
                        { "orig", Guid.Parse(uuidOriginal) },
                        { "nuevo", string.IsNullOrWhiteSpace(uuidNuevo) ? (object)DBNull.Value : Guid.Parse(uuidNuevo) },
                        { "usr", usuario },
                        { "modo", modo }
                    }, false, conn, tx);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                LogErrorHelper.RegistrarLog("Refacturacion_Bitacora", uuidNuevo,
                    "Tabla refacturacion_log no existe; se omitió el registro.", nivel: "WARN");
            }
        }
    }
}
