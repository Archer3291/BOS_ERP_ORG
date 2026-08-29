using BOS_ERP.Filters;
using BOS_ERP.Extensions;
using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.CuentasContables;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Globalization;

namespace BOS_ERP.Controllers.Ventas
{
    /// <summary>
    /// Facturación del punto de venta. Cubre los dos destinos posibles:
    ///   · factura nominativa, emitida al cliente desde la pantalla de punto de venta;
    ///   · factura global (público en general), emitida en el cierre de caja.
    /// La cadena es la misma para ambas — pedido → remisión → factura → timbrado — y lo
    /// único que cambia es el receptor, que se resuelve en un solo lugar.
    /// </summary>
    public partial class PuntoDeVentaController
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ObtenerTodosDocumentosFacturables()
        {
            var parameters = new Dictionary<string, object>();
            try
            {
                string queryEncabezado = @"
            SELECT  
                em.folio ||
                    CASE WHEN em.variacion > 0  
                        THEN '-' || num_to_letters(em.variacion)  
                        ELSE '' END AS folio,
                em.id_encabezado,
                em.encabezados_padre,
                em.suc,
                em.alm,
                em.gen,
                em.nat,
                em.usr0,
                em.usr1,
                em.fch,
                em.fch0,
                em.fch1,
                -- Días transcurridos desde la venta: permite señalar en pantalla los
                -- documentos rezagados que antes quedaban fuera del corte.
                (CURRENT_DATE - em.fch::date) AS dias_rezago,
                em.cli_prov,
                em.coment1,
                em.coment_aut,
                em.ccy,
                em.imp,
                em.vdr_cpr,
                em.flete,
                em.incoterm,
                em.mdp,
                em.f_pago,
                em.cfdi,
                cc.rfc,
                em.par,
                cc.lim_crd,
                cc.n_cli,
                cc.pl_crd,
                cc.dir,
                cocl.correo,
                df.forma_pago,
                df.razon_social,
                df.uso_sugerido, 
                df.regimen_fiscal, 
                df.calle, 
                df.no_exterior, 
                df.no_interior, 
                df.colonia, 
                df.localidad, 
                df.municipio, 
                df.estado, 
                df.pais, 
                df.codigo_postal,
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli
            FROM encabezadomov em
            LEFT JOIN catclientes cc  
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df 
                ON df.entidad_clave = cc.cve_cli
            LEFT JOIN (
                SELECT DISTINCT ON (cliente_id) cliente_id, correo
                FROM correos_cliente
                WHERE correo IS NOT NULL
                ORDER BY cliente_id, id_correo_cli ASC   -- elige el correo con menor id (el primero)
            ) cocl ON cocl.cliente_id = cc.id_cliente
            WHERE em.nat = 'VSUC'
            AND em.suc = @suc
            AND em.variacion = 0
            AND em.estatus_id = 1
            -- Una venta a crédito ya nace facturada y nominativa; no puede irse dentro
            -- de la global, que es un comprobante al público en general.
            AND COALESCE(em.tipo_proceso, '') NOT LIKE '%_credito%'
            ORDER BY em.fch;
        ";

                parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
                parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                var encabezadoResult = RunQuery(queryEncabezado, parameters);

                if (encabezadoResult == null || encabezadoResult.Count == 0)
                {
                    return Json(new { success = true, data = new List<object>() });
                }

                AgregarCuentaDelCobro(encabezadoResult);

                return Json(new { success = true, data = encabezadoResult });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Facturacion/ObtenerTodosDocumentosFacturables");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Marca cada documento con la cuenta de banco que el cajero eligió al cobrar
        /// (la del pago más grande, cuando hubo varias formas de pago), para que el modal
        /// de facturación la preseleccione en lugar de dejar que se escoja otra cuenta.
        ///
        /// Va aparte de la consulta principal porque banco_id es una columna opcional
        /// (sql/pos_formas_pago_bancos.sql): si no está, la lista de documentos se sirve
        /// igual y el modal simplemente no preselecciona nada.
        /// </summary>
        private void AgregarCuentaDelCobro(List<Dictionary<string, object>> documentos)
        {
            if (!ExisteColumna("factura_formas_pagos", "banco_id"))
                return;

            var ids = documentos
                .Select(d => GetInt(d["id_encabezado"], 0) ?? 0)
                .Where(id => id > 0)
                .ToArray();

            if (ids.Length == 0) return;

            var cuentas = RunQuery(@"
                SELECT DISTINCT ON (ffp.encabezado_id)
                       ffp.encabezado_id,
                       ffp.banco_id,
                       cb.cuenta_contable
                FROM factura_formas_pagos ffp
                INNER JOIN catbancos cb ON cb.id_catbanco = ffp.banco_id
                WHERE ffp.encabezado_id = ANY(@ids)
                  AND ffp.banco_id IS NOT NULL
                ORDER BY ffp.encabezado_id, ffp.monto DESC;",
                new Dictionary<string, object> { { "ids", ids } });

            var porDocumento = cuentas.ToDictionary(c => GetInt(c["encabezado_id"], 0) ?? 0);

            foreach (var doc in documentos)
            {
                int id = GetInt(doc["id_encabezado"], 0) ?? 0;

                doc["banco_cobro_id"] = porDocumento.TryGetValue(id, out var cuenta)
                    ? cuenta["banco_id"]
                    : null;
                doc["cuenta_cobro"] = porDocumento.TryGetValue(id, out var c2)
                    ? c2["cuenta_contable"]
                    : null;
            }
        }

        [HttpGet]
        public JsonResult BuscarDocumentoFacturablePorFolio(string folio)
        {
            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "folio", folio },
            { "suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")) },
            { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) },
            { "natCot", NatCotizacionSucursal }
        };

                string query = @"
            SELECT
                em.folio ||
                    CASE WHEN em.variacion > 0
                        THEN '-' || num_to_letters(em.variacion)
                        ELSE '' END AS folio,
                em.id_encabezado,
                em.encabezados_padre,
                em.suc,
                em.alm,
                em.gen,
                em.nat,
                em.usr0,
                em.usr1,
                em.fch0,
                em.fch1,
                em.cli_prov,
                em.coment1,
                em.coment_aut,
                em.ccy,
                em.imp,
                em.vdr_cpr,
                em.flete,
                em.incoterm,
                em.mdp,
                em.f_pago,
                em.cfdi,
                cc.rfc,
                em.par,
                cc.lim_crd,
                cc.n_cli,
                cc.pl_crd,
                cc.dir,
                cocl.correo,
                df.forma_pago,
                df.razon_social,
                df.uso_sugerido,
                df.regimen_fiscal,
                df.calle,
                df.no_exterior,
                df.no_interior,
                df.colonia,
                df.localidad,
                df.municipio,
                df.estado,
                df.pais,
                df.codigo_postal,
                cc.dir || CHR(10) ||
                cc.col || CHR(10) ||
                cc.pob || CHR(10) ||
                cc.cp AS info_cli
            FROM encabezadomov em
            LEFT JOIN catclientes cc
                ON cc.cve_cli = em.cli_prov AND cc.empresa_id = @empresa_id
            LEFT JOIN direcciones_facturacion df
                ON df.entidad_clave = cc.cve_cli
            LEFT JOIN (
                SELECT DISTINCT ON (cliente_id) cliente_id, correo
                FROM correos_cliente
                WHERE correo IS NOT NULL
                ORDER BY cliente_id, id_correo_cli ASC
            ) cocl ON cocl.cliente_id = cc.id_cliente
            WHERE em.nat = 'VSUC'
              AND em.suc = @suc
              AND em.variacion = 0
              AND em.estatus_id = 1
              -- IN en vez de '=': la subconsulta no filtraba por sucursal ni tipo de
              -- documento, así que un folio repetido entre sucursales o ejercicios
              -- hacía reventar la consulta con 'more than one row returned'.
              AND em.encabezados_padre IN (
                  SELECT cot.id_encabezado
                  FROM encabezadomov cot
                  WHERE cot.folio = @folio
                    AND cot.suc = @suc
                    AND cot.nat = @natCot
              )
            ORDER BY em.fch
            LIMIT 1;";

                var result = RunQuery(query, parameters);

                if (result == null || result.Count == 0)
                    return Json(new { success = false, message = "No se encontró documento facturable para ese folio." });

                return Json(new { success = true, data = result.First() });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Facturacion/BuscarDocumentoFacturablePorFolio");
                return Json(new { success = false, message = ex.Message });
            }
        }


        public class TimbradoClienteDatos
        {
            public string mdp { get; set; }
            public string fpago { get; set; }
            public string cfdi { get; set; }
            public string rFiscal { get; set; }
        }

        // ── Destinos de facturación ────────────────────────────────────────────────
        // "contado": factura nominativa al cliente, desde la pantalla de punto de venta.
        // "global" : factura al público en general, desde el cierre de caja.
        private const string TipoFacturacionContado = "contado";
        private const string TipoFacturacionGlobal = "global";

        // "credito": factura nominativa PPD que nace con la venta y deja la cartera
        // abierta. Comparte todo el armado con la de contado; lo único que cambia es que
        // no se registra el cobro, porque el dinero todavía no entra.
        private const string TipoFacturacionCredito = "credito";

        // "publico": VSFAC de contado —mismo documento, misma póliza, mismo cobro— pero
        // con el receptor genérico de público en general en lugar del cliente de la
        // venta. Lo usa el cierre de caja para las ventas que no van dentro de la global
        // (tarjeta, transferencia…): el cliente no pidió factura, así que no se le puede
        // emitir una nominativa a su nombre, y a la vez no pueden entrar a la global,
        // que declara todo como efectivo.
        //
        // Ojo con lo que NO es: no lleva el nodo InformacionGlobal, porque no agrupa
        // operaciones de un periodo. Es un comprobante suelto al RFC genérico.
        private const string TipoFacturacionPublico = "publico";

        // Receptor de la factura global (público en general).
        private const string ClienteFacturaGlobal = "CONTADO";
        private const int RefClienteFacturaGlobal = 8612;
        private const string UsoCfdiFacturaGlobal = "S01";
        private const int FormaPagoEfectivoId = 1;

        // Documento de factura del punto de venta. La global lleva tipo propio (GLFAC/91)
        // para poder consultarse, cancelarse y refacturarse por separado de las
        // nominativas (VSFAC/52), que siguen su propio folio.
        private const string NatFacturaSucursal = "VSFAC";
        private const int TpDocFacturaSucursal = 52;
        private const string NatFacturaGlobal = "GLFAC";
        private const int TpDocFacturaGlobal = 91;

        /// <summary>
        /// Destino de un documento a partir de su tipo_proceso ("remision_global",
        /// "pedido_contado", "factura_global"…). Se lee del documento y no de la sesión
        /// para que un reproceso o un retimbrado posterior conserve el tipo original.
        /// </summary>
        private static bool EsProcesoGlobal(string tipoProceso) =>
            (tipoProceso ?? "").EndsWith("_" + TipoFacturacionGlobal, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Documento nacido de una venta a crédito ("factura_credito", "remision_credito"…).
        ///
        /// Se lee del tipo_proceso y NO del `mdp` del documento a propósito: existen ventas
        /// anteriores a este flujo que heredaron mdp = PPD de una cotización a crédito y aun
        /// así se cobraron en caja. Si el crédito se dedujera del mdp, esas ventas dejarían
        /// de registrar su cobro al facturarse.
        /// </summary>
        private static bool EsProcesoCredito(string tipoProceso) =>
            (tipoProceso ?? "").EndsWith("_" + TipoFacturacionCredito, StringComparison.OrdinalIgnoreCase);

        /// <summary>RFC genérico nacional: el receptor de las ventas al público en general.</summary>
        private const string RfcPublicoEnGeneral = "XAXX010101000";

        private static bool EsReceptorGenerico(string rfc) =>
            string.Equals((rfc ?? "").Trim(), RfcPublicoEnGeneral, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Lo único que distingue una factura nominativa de la global. El resto del
        /// encabezado se construye igual en ambos casos.
        /// </summary>
        private sealed record ReceptorFacturacion(
            string CveCliente,
            int? RefCliente,
            int FormaPago,
            string MetodoPago,
            string UsoCfdi);

        /// <summary>
        /// Resuelve el receptor según el destino. En la nominativa manda lo que el cajero
        /// capturó en la pantalla de facturación; si viene vacío se cae a lo que traiga el
        /// documento de venta y, como último recurso, a los valores por omisión.
        /// </summary>
        private static ReceptorFacturacion ResolverReceptor(
            bool receptorGenerico,
            Dictionary<string, object> docOrigen,
            TimbradoClienteDatos capturado)
        {
            if (receptorGenerico)
            {
                // Receptor genérico (público en general). Lo comparten la factura global
                // y las VSFAC del cierre: en ninguna de las dos el cliente pidió factura,
                // así que el comprobante no puede ir a su nombre.
                //
                // La forma de pago sale del documento y ya no es siempre efectivo: el
                // cierre agrupa en la global únicamente las ventas cobradas en efectivo y
                // manda cada venta con otra forma de pago a su propio comprobante, que
                // tiene que declarar SU forma de pago. Sin esto, una venta con tarjeta se
                // timbraba diciendo que el dinero entró en efectivo.
                int fPagoGlobal = docOrigen != null
                                  && docOrigen.TryGetValue("f_pago", out var fp)
                                  && int.TryParse(fp?.ToString(), out int idFPagoDoc)
                                  && idFPagoDoc > 0
                    ? idFPagoDoc
                    : FormaPagoEfectivoId;

                return new ReceptorFacturacion(
                    ClienteFacturaGlobal,
                    RefClienteFacturaGlobal,
                    fPagoGlobal,
                    "PUE",
                    UsoCfdiFacturaGlobal);
            }

            // El orden importa y antes estaba al revés: ganaba el valor del documento.
            // La venta VSUC guarda un uso de CFDI por omisión (G03) al capturarse, así que
            // ese valor SIEMPRE está presente y pisaba lo que se elegía en el formulario:
            // pedías D01 y se timbraba con lo que traía la venta. Lo que el cajero
            // selecciona al facturar es la decisión deliberada del momento y debe ganar.
            string PrimeroNoVacio(string delFormulario, string delDocumento, string porOmision)
                => !string.IsNullOrWhiteSpace(delFormulario) ? delFormulario
                 : !string.IsNullOrWhiteSpace(delDocumento) ? delDocumento
                 : porOmision;

            string fPagoTexto = PrimeroNoVacio(
                capturado?.fpago, docOrigen["f_pago"]?.ToString(), "0");

            return new ReceptorFacturacion(
                docOrigen["cli_prov"].ToString(),
                null,   // la remisión lo resuelve buscando el cliente por clave
                int.TryParse(fPagoTexto, out int idFPago) ? idFPago : 0,
                PrimeroNoVacio(capturado?.mdp, docOrigen["mdp"]?.ToString(), "PUE"),
                PrimeroNoVacio(capturado?.cfdi, docOrigen["cfdi"]?.ToString(), "G03"));
        }

        // Paso interno del proceso de timbrado: recibe conn/tx, no puede exponerse como ruta.
        [NonAction]
        public async Task<(bool Success, string Message, object Data)> GuardarDesdeDocumentos(string ids, NpgsqlConnection conn, NpgsqlTransaction tx, string mdp = null, string tipo = null, string fpago = null, string cfdi = null, string rFiscal = null)
        {
            try
            {
                // Se enumeran los destinos nominativos en vez de preguntar por "global"
                // para no cambiar el comportamiento de un `tipo` vacío, que hoy cae del
                // lado global y así debe seguir.
                bool esGlobal =
                    !string.Equals(tipo, TipoFacturacionContado, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tipo, TipoFacturacionCredito, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tipo, TipoFacturacionPublico, StringComparison.OrdinalIgnoreCase);

                // El tipo de DOCUMENTO y el RECEPTOR dejaron de ser la misma decisión:
                // "publico" genera un VSFAC (documento nominativo) pero dirigido al
                // receptor genérico.
                bool receptorGenerico =
                    esGlobal ||
                    string.Equals(tipo, TipoFacturacionPublico, StringComparison.OrdinalIgnoreCase);

                // Lo que se guarda en tipo_proceso es el DESTINO DOCUMENTAL, no el
                // receptor: "publico" es una venta de contado a la que sólo le cambia a
                // quién va dirigido el CFDI. Si se guardara tal cual, pedido y remisión
                // nacerían con "pedido_publico" / "remision_publico", claves que no
                // existen en poliza_cat_tipo_proceso ni tienen plantilla en
                // poliza_documento.
                string tipoDocumental =
                    string.Equals(tipo, TipoFacturacionPublico, StringComparison.OrdinalIgnoreCase)
                        ? TipoFacturacionContado
                        : tipo;

                TimbradoClienteDatos formasPago = null;
                if (!esGlobal)
                {
                    formasPago = new TimbradoClienteDatos
                    {
                        mdp = mdp,
                        fpago = fpago,
                        cfdi = cfdi,
                        rFiscal = rFiscal
                    };
                    HttpContext.Session.SetObjectAsJson("formasPago", formasPago);
                }
                else
                {
                    HttpContext.Session.Remove("formasPago");
                }

                HttpContext.Session.SetString("tipo", tipoDocumental);

                var idsOrigen = ids.Split(',')
                                            .Select(id => int.TryParse(id.Trim(), out int val) ? val : 0)
                                            .Where(val => val > 0)
                                            .ToList();


                int idEncabezadoOrigen = idsOrigen.First();

                // 🔹 Obtener datos del primer encabezado directamente desde BD
                string queryEncabezado = @"
                    SELECT 
                        id_encabezado, encabezados_padre, suc, gen, nat, nro_gpo_doc, nro_tp_doc, fol_doc, 
                        ccy, alm, fch, cli_prov, refe, vdr_cpr, dto, iva, ieps_isr, imp, pl_dias, fch_pg_entrega, 
                        sub, iva_ret, coment1, coment2, coment3, tp_mov, n_cli, cl_cli, col_cli, pob_cli, 
                        centro_costos, en_presupuesto, flete, usr_dep, par, saldo_doc, stat, cve_proy, cve_cli, 
                        mto_antic, com_vdr, cve_dpto, usr_doc, fch_cap, f_pago, mdp, coment_aut, tipo_proceso, usr0, usr1, usr2, fch0, fch1, fch2,
                        tipo_producto, cfdi
                    FROM encabezadomov
                    WHERE id_encabezado = @id;
                ";

                var parametros = new Dictionary<string, object> { { "@id", idEncabezadoOrigen } };
                var encabezadoData = RunQuery(queryEncabezado, parametros, false, conn, tx).FirstOrDefault();

                // 🔹 Encabezado del pedido. Es idéntico para ambos destinos salvo el
                //    receptor, que se resuelve en un solo lugar. Antes eran dos bloques
                //    de ~35 líneas duplicadas que había que mantener en paralelo.
                var receptor = ResolverReceptor(receptorGenerico, encabezadoData, formasPago);

                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 50,                       // tipo de documento destino: pedido
                    UsrDep = encabezadoData["usr_dep"].ToString(),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = encabezadoData["alm"]?.ToString() ?? "",
                    Fch = DateTime.Now,
                    TpMov = "VSPED",
                    ComentAut = encabezadoData["coment_aut"]?.ToString() ?? "",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(encabezadoData["usr0"]),
                    Fch0 = Convert.ToDateTime(encabezadoData["fch0"]),
                    Usr1 = Convert.ToInt32(encabezadoData["usr1"]),
                    Fch1 = Convert.ToDateTime(encabezadoData["fch1"]),
                    Usr2 = GetUserId(User.Identity.Name),
                    Fch2 = DateTime.Now,
                    Ccy = encabezadoData["ccy"]?.ToString() ?? "PESOS",
                    Estatus = 11,
                    Flete = Convert.ToDecimal(encabezadoData["flete"]),
                    VdrCpr = encabezadoData["vdr_cpr"].ToString(),
                    Coment1 = encabezadoData["coment1"]?.ToString(),
                    EncabezadoPadre = idEncabezadoOrigen,
                    PlDias = Convert.ToInt32(encabezadoData["pl_dias"]),
                    FchPgEntrega = DateTime.Now,
                    Par = Convert.ToDecimal(encabezadoData["par"]),

                    // ── Lo que cambia entre nominativa y global ──
                    CliProv = receptor.CveCliente,
                    Ref = receptor.RefCliente ?? Convert.ToInt32(encabezadoData["refe"]),
                    FPago = receptor.FormaPago,
                    Mdp = receptor.MetodoPago,
                    CFDI = receptor.UsoCfdi,

                    TipoPoceso = "pedido_" + tipoDocumental
                };

                // 🔹 Obtener partidas de todos los documentos origen
                var idsParam = string.Join(",", idsOrigen);
                string queryPartidas = $@" SELECT cve_prod, descr_prod, cant_ud, ud, pv_prod, imp_part, dto1, iva, ieps, producto_id FROM partidasdoc WHERE encabezado_id IN ({idsParam})";
                var partidasResult = RunQuery(queryPartidas, parametros, false, conn, tx);
                var partidas = new List<PartidaDocumento>();
                foreach (var row in partidasResult)
                {
                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = row["cve_prod"].ToString(),
                        DescrProd = row["descr_prod"].ToString(),
                        CantUd = Convert.ToDecimal(row["cant_ud"]),
                        PvProd = Convert.ToDecimal(row["pv_prod"]),
                        Dto1 = row.ContainsKey("dto1") ? DecimalDe(row["dto1"]) : 0,
                        ImpPart = Convert.ToDecimal(row["imp_part"]),
                        Ud = row["ud"].ToString(),
                        IdProducto = Convert.ToInt32(row["producto_id"])
                    });
                }

                // sub guardaba el NETO (suma de imp_part), no el bruto, y el IVA se
                // calculaba sobre el total sin redondear por partida.
                var totalesPedido = CalcularTotales(partidas);
                encabezado.Sub = totalesPedido.Bruto;
                encabezado.Imp = totalesPedido.Total;

                // 🔹 Insertar documento destino
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                return (true, "Documentos guardados correctamente", new
                {
                    Folio = folio["folio_generado"].ToString(),
                    IdEncabezado = Convert.ToInt32(folio["IdEncabezado"])
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Facturacion/?");
                return (false, "Error en GuardarDesdeDocumentos: " + ex.Message, null);
            }
        }

        [NonAction]
        public async Task<(bool Success, string Message, object Data)> GuardarRemisionDesdePedidos(string ids, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            int idEncabezadoGenerado = 0;
            try
            {
                var idList = ids.Split(',')
                                .Select(x => x.Trim())
                                .Where(x => int.TryParse(x, out _))
                                .Select(int.Parse)
                                .ToList();

                // 🔹 1. Tomar el encabezado base del primer documento
                int idDocumentoBase = idList.First();
                var paramEnc = new Dictionary<string, object> { { "id", idDocumentoBase } };

                string queryEncabezado = @"
    SELECT 
        id_encabezado, encabezados_padre, suc, gen, nat, nro_gpo_doc, nro_tp_doc, fol_doc, 
        ccy, alm, fch, cli_prov, refe, vdr_cpr, dto, iva, ieps_isr, imp, pl_dias, fch_pg_entrega, 
        sub, iva_ret, coment1, coment2, coment3, tp_mov, n_cli, cl_cli, col_cli, pob_cli, 
        centro_costos, en_presupuesto, flete, usr_dep, par, saldo_doc, stat, cve_proy, cve_cli, pl_dias,
        mto_antic, com_vdr, cve_dpto, usr_doc, fch_cap, f_pago, mdp, coment_aut, tipo_proceso, usr0, usr1, usr2, fch0, fch1, fch2,
        tipo_producto, cfdi
    FROM encabezadomov
    WHERE id_encabezado = @id;
";
                var encRows = RunQuery(queryEncabezado, paramEnc, false, conn, tx);


                var enc = encRows[0];
                int userIdActual = GetUserId(User.Identity.Name);
                string usuarioActual = User.Identity.Name;

                var paramCli = new Dictionary<string, object> { { "cve_cli", enc["cli_prov"]?.ToString() }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } };
                string clientId = "SELECT id_cliente FROM catclientes WHERE cve_cli = @cve_cli AND empresa_id = @empresa_id";

                var refe = RunScalar(clientId, paramCli, false, conn, tx);

                int? rf = refe as int?;
                // 🔹 2. Crear encabezado de remisión
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = 51,
                    UsrDep = GetAreaName(usuarioActual),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = enc["alm"]?.ToString() ?? "",
                    Fch = DateTime.Now,
                    TpMov = "VSREM",
                    ComentAut = enc["coment_aut"]?.ToString() ?? "",
                    UsrDoc = usuarioActual,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(enc["usr0"]),
                    Fch0 = Convert.ToDateTime(enc["fch0"]),
                    Usr1 = Convert.ToInt32(enc["usr1"]),
                    Fch1 = Convert.ToDateTime(enc["fch1"]),
                    Usr2 = Convert.ToInt32(enc["usr2"]),
                    Fch2 = Convert.ToDateTime(enc["fch2"]),
                    Usr3 = userIdActual,
                    Fch3 = DateTime.Now,
                    Imp = 0, // se recalcula más adelante
                    CliProv = enc["cli_prov"]?.ToString(),
                    Ref = rf,
                    Ccy = enc["ccy"]?.ToString() ?? "PESOS",
                    Estatus = 11,
                    Flete = Convert.ToDecimal(enc["flete"]),
                    VdrCpr = enc["vdr_cpr"]?.ToString(),
                    Coment1 = enc["coment1"]?.ToString(),
                    EncabezadoPadre = idDocumentoBase,
                    PlDias = enc["pl_dias"] != DBNull.Value ? Convert.ToInt32(enc["pl_dias"]) : 0,
                    FchPgEntrega = DateTime.Now,
                    Par = Convert.ToDecimal(enc["par"]),
                    FPago = Convert.ToInt32(enc["f_pago"]),
                    Mdp = !string.IsNullOrWhiteSpace(enc["mdp"]?.ToString()) ? enc["mdp"].ToString() : "PUE",
                    TipoPoceso = "remision_" + HttpContext.Session.GetString("tipo")?.ToString(),
                    CFDI = enc["cfdi"]?.ToString(),
                    CentroCostos = 14
                };

                // 🔹 3. Cargar TODAS las partidas de los documentos origen
                var partidas = new List<PartidaDocumento>();
                foreach (int idOrigen in idList)
                {
                    var paramPart = new Dictionary<string, object> { { "id", idOrigen } };
                    string queryPartidas = @"
                SELECT cve_prod, descr_prod, cant_ud, pv_prod, dto1, imp_part, ud, producto_id
                FROM partidasdoc
                WHERE encabezado_id = @id";
                    var partes = RunQuery(queryPartidas, paramPart, false, conn, tx);

                    foreach (var p in partes)
                    {
                        partidas.Add(new PartidaDocumento
                        {
                            CveProd = p["cve_prod"].ToString(),
                            DescrProd = p["descr_prod"].ToString(),
                            CantUd = Convert.ToDecimal(p["cant_ud"]),
                            PvProd = Convert.ToDecimal(p["pv_prod"]),
                            Dto1 = Convert.ToDecimal(p["dto1"]),
                            ImpPart = Convert.ToDecimal(p["imp_part"]),
                            Ud = p["ud"].ToString(),
                            IdProducto = Convert.ToInt32(p["producto_id"])
                        });
                    }
                }

                // 🔹 4. Calcular los totales de la remisión
                // Antes: imp = suma de imp_part, es decir el NETO SIN IVA, mientras el
                // pedido y la factura sí lo incluían; y sub no se asignaba nunca. Eso
                // dejaba la remisión un 16% por debajo en cualquier reporte que agregue
                // por imp.
                var totalesRemision = CalcularTotales(partidas);
                encabezado.Sub = totalesRemision.Bruto;
                encabezado.Imp = totalesRemision.Total;

                // 🔹 5. Crear el documento en base de datos
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
                idEncabezadoGenerado = Convert.ToInt32(folio["IdEncabezado"]);

                // 🔹 6. Inventario: la remisión YA NO descuenta.
                // El movimiento se registra cuando la mercancía sale físicamente:
                // en Guardar (venta) y en EntregarPendientes (entrega posterior).
                // Descontar aquí la cantidad total pedida provocaba, según hubiera
                // resurtido o no antes del corte, o una fuga (lo entregado después
                // nunca salía del inventario) o un faltante fantasma (se descontaba
                // mercancía que seguía en bodega). Este paso queda documental.

                //        // 🔹 7. Impuestos, compras y pólizas
                //        var inpuestosParameter = new Dictionary<string, object>
                //{
                //    {"encabezado_id", idEncabezadoGenerado},
                //    {"impuesto_id", 1},
                //    {"subtotal", encabezado.Imp / 1.16m},
                //    {"importe", encabezado.Imp - (encabezado.Imp / 1.16m)},
                //    {"orden_apl", 1},
                //    {"imp_variable", 16},
                //    {"prov_nom", encabezado.CliProv},
                //    {"f_pago_id", encabezado.FPago }
                //};
                //        string insertImpuesto = @"INSERT INTO imp_oc 
                //    (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
                //    VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);";
                //        RunQuery(insertImpuesto, inpuestosParameter);

                RegistrarCompra(idEncabezadoGenerado, userIdActual, conn, tx);
                //List<PolizaData> poliza = GenerarDatosPoliza(idEncabezadoGenerado);
                //RegistrarPolizas(userIdActual, idEncabezadoGenerado, poliza);

                return (true, "Documentos guardados correctamente", new
                {
                    Folio = folio["folio_generado"].ToString(),
                    IdEncabezado = Convert.ToInt32(folio["IdEncabezado"])
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Facturacion/?");
                //if (idEncabezadoGenerado > 0)
                    //BorradoFacturasIncorrectas(idEncabezadoGenerado, "remision", "VS");
                return (false, "Error en GuardarDesdeDocumentos: " + ex.Message, null);
            }
        }
        [NonAction]
        public async Task<(bool Success, string Message, object Data)> GuardarFacturaDesdeDocs(string ids, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            try
            {


                var idsOrigen = ids.Split(',')
                                            .Select(id => int.TryParse(id.Trim(), out int val) ? val : 0)
                                            .Where(val => val > 0)
                                            .ToList();

                // 🔹 Tomar encabezado base del primer documento
                string queryEncabezado = @"
            SELECT 
                id_encabezado, suc, alm, fch, cli_prov, vdr_cpr, ccy, flete, coment1, coment2, coment3,
                pl_dias, fch_pg_entrega, tp_mov, usr_dep, par, cve_proy, f_pago, mdp, coment_aut,
                tipo_proceso, tipo_producto, cfdi, usr0, fch0, usr1, fch1, usr2, fch2, usr3, fch3, refe
            FROM encabezadomov
            WHERE id_encabezado = @idOrigen;
        ";

                var parametros = new Dictionary<string, object> { { "@idOrigen", idsOrigen.First() } };
                var encabezadoBase = RunQuery(queryEncabezado, parametros, false, conn, tx).FirstOrDefault();

                // El destino manda el tipo de documento: la factura global usa GLFAC/91 y la
                // nominativa VSFAC/52. Se deduce de la remisión origen (remision_global /
                // remision_contado); la sesión sólo sirve de respaldo. Antes GLFAC estaba
                // fijo y las facturas nominativas del punto de venta salían marcadas como
                // globales, mezclándose en la consulta y en el folio.
                bool esGlobal =
                    EsProcesoGlobal(encabezadoBase["tipo_proceso"]?.ToString()) ||
                    string.Equals(HttpContext.Session.GetString("tipo"), TipoFacturacionGlobal,
                        StringComparison.OrdinalIgnoreCase);

                // 🔹 Crear encabezado nuevo (Factura)
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 14,
                    IdTpDoc = esGlobal ? TpDocFacturaGlobal : TpDocFacturaSucursal,
                    UsrDep = encabezadoBase["usr_dep"].ToString(),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Alm = encabezadoBase["alm"]?.ToString() ?? "",
                    Fch = DateTime.Now,
                    TpMov = esGlobal ? NatFacturaGlobal : NatFacturaSucursal,
                    ComentAut = encabezadoBase["coment_aut"]?.ToString() ?? "",
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = Convert.ToInt32(encabezadoBase["usr0"]),
                    Fch0 = Convert.ToDateTime(encabezadoBase["fch0"]),
                    Usr1 = Convert.ToInt32(encabezadoBase["usr1"]),
                    Fch1 = Convert.ToDateTime(encabezadoBase["fch1"]),
                    Usr2 = Convert.ToInt32(encabezadoBase["usr2"]),
                    Fch2 = Convert.ToDateTime(encabezadoBase["fch2"]),
                    Usr3 = Convert.ToInt32(encabezadoBase["usr3"]),
                    Fch3 = Convert.ToDateTime(encabezadoBase["fch3"]),
                    Usr4 = GetUserId(User.Identity.Name),
                    Fch4 = DateTime.Now,
                    Estatus = 11,
                    CliProv = encabezadoBase["cli_prov"].ToString(),
                    Ccy = encabezadoBase["ccy"].ToString(),
                    Flete = Convert.ToDecimal(encabezadoBase["flete"]),
                    VdrCpr = encabezadoBase["vdr_cpr"].ToString(),
                    Ref = GetInt(encabezadoBase["refe"]),
                    Coment1 = encabezadoBase["coment1"]?.ToString() ?? "",
                    PlDias = Convert.ToInt32(encabezadoBase["pl_dias"]),
                    FchPgEntrega = Convert.ToDateTime(encabezadoBase["fch_pg_entrega"]),
                    Par = Convert.ToDecimal(encabezadoBase["par"]),
                    FPago = Convert.ToInt32(encabezadoBase["f_pago"]),
                    Mdp = encabezadoBase["mdp"].ToString(),
                    // Con la sesión vacía esto guardaba "factura_" a secas y el documento
                    // quedaba sin destino reconocible para la consulta ni el retimbrado.
                    // El destino se toma de la sesión, que la fija GuardarDesdeDocumentos
                    // con el `tipo` recibido: contado, credito o global. Con la sesión vacía
                    // se queda en contado, que es como se comportaba antes.
                    TipoPoceso = "factura_" + (
                        esGlobal ? TipoFacturacionGlobal
                        : string.Equals(HttpContext.Session.GetString("tipo"), TipoFacturacionCredito,
                                        StringComparison.OrdinalIgnoreCase) ? TipoFacturacionCredito
                        : TipoFacturacionContado),
                    CFDI = encabezadoBase["cfdi"]?.ToString(),
                    EncabezadoPadre = idsOrigen.First(),
                    CentroCostos = 14
                };

                // 🔹 Obtener TODAS las partidas de los documentos origen
                string idsParam = string.Join(",", idsOrigen);
                string queryPartidas = $@"
            SELECT 
                cve_prod, descr_prod, cant_ud, ud, pv_prod, imp_part, dto1, iva, ieps, producto_id
            FROM partidasdoc 
            WHERE encabezado_id IN ({idsParam});
        ";

                var partidasResult = RunQuery(queryPartidas, parametros, false, conn, tx);

                var partidas = new List<PartidaDocumento>();

                foreach (var row in partidasResult)
                {
                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = row["cve_prod"].ToString(),
                        DescrProd = row["descr_prod"].ToString(),
                        CantUd = Convert.ToDecimal(row["cant_ud"]),
                        PvProd = Convert.ToDecimal(row["pv_prod"]),
                        Dto1 = row.ContainsKey("dto1") ? DecimalDe(row["dto1"]) : 0,
                        ImpPart = Convert.ToDecimal(row["imp_part"]),
                        Ud = row["ud"].ToString(),
                        IdProducto = Convert.ToInt32(row["producto_id"])
                    });
                }

                var totalesFactura = CalcularTotales(partidas);
                decimal subtotalDocumento = totalesFactura.Bruto;
                decimal iva = totalesFactura.Iva;

                encabezado.Sub = subtotalDocumento;
                encabezado.Imp = totalesFactura.Total;

                // 🔹 Guardar documento con partidas
                var folio = GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);

                // 🔹 Registrar impuestos (como en GuardarFactura original)
                string insertImpuesto = @"
            INSERT INTO imp_oc (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
            VALUES(@encabezado_id, @impuesto_id, @subtotal, @importe, @orden_apl, @imp_variable, @prov_nom, @f_pago_id);
        ";

                var impParams = new Dictionary<string, object>
        {
            { "encabezado_id", Convert.ToInt32(folio["IdEncabezado"]) },
            { "impuesto_id", Convert.ToInt32(GetSetting("impuesto")) }, // IVA
            { "subtotal", subtotalDocumento },
            { "importe", iva },
            { "orden_apl", 1 },
            { "imp_variable", 16 },
            { "prov_nom", encabezado.CliProv },
            { "f_pago_id", encabezado.FPago }
        };
                RunQuery(insertImpuesto, impParams, false, conn, tx);

                // Rastro del origen: qué remisión(es) y qué cantidad de cada partida quedó
                // amparada por esta factura. El punto de venta no lo escribía —sólo lo hacían
                // VI/VN/VIN cuando se facturaba desde el modal de remisiones—, así que las 28
                // VSFAC y las 2 GLFAC existentes quedaron sin origen y la cancelación no tenía
                // de dónde partir. Aquí siempre es consumo total: la remisión se crea desde el
                // pedido y se factura completa en el mismo cierre.
                RegistrarOrigenFacturaTotal(
                    Convert.ToInt32(folio["IdEncabezado"]), idsOrigen, conn, tx);

                return (true, "Documentos guardados correctamente", new
                {
                    Folio = folio["folio_generado"].ToString(),
                    IdEncabezado = Convert.ToInt32(folio["IdEncabezado"])
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Facturacion/?");
                return (false, "Error en GuardarDesdeDocumentos: " + ex.Message, null);
            }
        }


        /// <summary>
        /// Arma el objeto <see cref="Factura"/> y registra póliza, cartera y cobro DENTRO de la
        /// transacción. NO timbra: el timbrado es una llamada externa al PAC que no se puede
        /// revertir con un rollback, así que lo hace el llamador una vez commiteada la transacción.
        /// </summary>
        [NonAction]
        public async Task<(bool Success, string Message, Factura Factura)> PrepararFacturaDesdeDocs(string ids, NpgsqlConnection conn, NpgsqlTransaction tx, string cuentaBanco = null)
        {
            try
            {
                string perfil = Convert.ToBoolean(GetSetting("perfil_factura"))
                ? HttpContext.Session.GetString("EmpresaFactura") ?? "pruebas"
                : "pruebas";

                // Datos del emisor con la clave plana "Emisores:{perfil}:{campo}".
                // Antes se hacía GetSection($"Emisores:{perfil}")[$"{perfil}.Rfc"], que
                // apunta a "Emisores:{perfil}:{perfil}.Rfc" — clave inexistente — y
                // devolvía null: el CFDI salía sin RFC, razón social ni régimen del
                // emisor. El mismo error ya se había corregido en VNFactura.
                string GetEmisor(string campo) => _configuration[$"Emisores:{perfil}:{campo}"] ?? "";

                if (string.IsNullOrWhiteSpace(GetEmisor("Rfc")))
                    return (false, $"No hay datos de emisor configurados para el perfil '{perfil}' (Emisores:{perfil} en appsettings).", null);

                var idsOrigen = ids
                    .Split(',')
                    .Select(x => int.TryParse(x.Trim(), out int id) ? id : 0)
                    .Where(x => x > 0)
                    .ToList();

                // 🔹 Obtener encabezado base (usamos el primero)
                var queryEncabezado = @"
            SELECT em.*, c.n_cli, c.cp, c.rfc 
            FROM encabezadomov em
            LEFT JOIN catclientes c ON em.cli_prov = c.cve_cli AND c.empresa_id = @empresa_id
            WHERE em.id_encabezado = @id;";
                var parameters = new Dictionary<string, object> { { "id", idsOrigen.First() }, { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } };
                var enc = RunQuery(queryEncabezado, parameters, false, conn, tx).FirstOrDefault();


                // 🔹 Crear lista combinada de partidas
                string idsConcat = string.Join(",", idsOrigen);
                string queryPartidas = $@"
            SELECT pd.cve_prod, pd.descr_prod, pd.cant_ud, pd.ud, pd.pv_prod, pd.imp_part, pd.dto1, pd.producto_id
            FROM partidasdoc pd
            WHERE pd.encabezado_id IN ({idsConcat});";
                var partidas = RunQuery(queryPartidas, parameters, false, conn, tx);

                // 🔹 Calcular totales (XmlBuilderService los recalcula a partir de Tproductos,
                //    esto es sólo el valor previo del objeto Factura).
                decimal subtotal = 0, neto = 0;
                foreach (var p in partidas)
                {
                    decimal cantidad = Convert.ToDecimal(p["cant_ud"]);
                    decimal precio = Convert.ToDecimal(p["pv_prod"]);
                    decimal imp = Convert.ToDecimal(p["imp_part"]);
                    subtotal += cantidad * precio;
                    neto += imp;
                }
                decimal iva = Math.Round(neto * 0.16m, 2);
                decimal total = neto + iva;

                // 🔹 Determinar moneda (por código)
                string ccy = enc["ccy"]?.ToString() ?? "";
                string moneda = "MXN"; // valor por defecto

                if (ccy == "DLLS")
                    moneda = "USD";
                else if (ccy == "EURO")
                    moneda = "EUR";
                else if (ccy == "PESOS")
                    moneda = "MXN";


                // 🔹 Consultar descripciones de forma y método de pago
                string mdp = RunScalar("SELECT descripcion FROM mdp WHERE cve_mdp = @cve;", new Dictionary<string, object> { { "cve", enc["mdp"] } }, false, conn, tx)?.ToString() ?? "";
                string tp = RunScalar("SELECT descripcion FROM cat_f_pago WHERE id_f_pago = @cve;", new Dictionary<string, object> { { "cve", enc["f_pago"] } }, false, conn, tx)?.ToString() ?? "";
                string usoCFDItext = RunScalar("SELECT descripcion FROM catusocfdi WHERE clave = @cve;", new Dictionary<string, object> { { "cve", enc["cfdi"] } }, false, conn, tx)?.ToString() ?? "";
                string regimenText = RunScalar("SELECT descripcion FROM catregimenfiscal WHERE clave = @cve;", new Dictionary<string, object> { { "cve", "601" } }, false, conn, tx)?.ToString() ?? "General de Ley Personas Morales";

                // 🔹 Crear objeto Factura
                var factura = new Factura();

                factura.Serie = "VS";
                // Con conn/tx: el documento de factura se acaba de crear en esta misma
                // transacción, así que desde otra conexión todavía no existe y el folio
                // salía vacío en el CFDI.
                factura.Folio = GetDocumentFolio(idsOrigen.First(), "ERP_SRS", conn, tx);
                factura.IdTipoPago = RunScalar("SELECT cve_sat FROM cat_f_pago WHERE id_f_pago = @cve;", new Dictionary<string, object> { { "cve", enc["f_pago"] } }, false, conn, tx)?.ToString() ?? "99";
                factura.Moneda = moneda;
                factura.CpE = GetEmisor("CpE");
                factura.RfcEmisor = GetEmisor("Rfc");
                factura.RsoEmisor = GetEmisor("RazonSocial");
                factura.Rege = GetEmisor("Regimen");
                factura.RfcCliente = enc["rfc"].ToString();
                factura.RsoCliente = enc["n_cli"].ToString();
                factura.CpR = enc["cp"].ToString();
                factura.IdUsoCFDI = enc["cfdi"].ToString();
                factura.CFDIText = usoCFDItext;
                // 616 "Sin obligaciones fiscales" es el único régimen válido para el RFC
                // genérico; el del cliente sólo aplica cuando la factura va a su nombre.
                string regimenCapturado = HttpContext.Session.GetObjectFromJson<TimbradoClienteDatos>("formasPago")?.rFiscal;
                factura.Regc = EsReceptorGenerico(factura.RfcCliente) || string.IsNullOrWhiteSpace(regimenCapturado)
                    ? "616"
                    : regimenCapturado;
                factura.regimenEText = regimenText;
                factura.Subtotal = subtotal;
                factura.IVA = iva;
                factura.Total = total;
                factura.TipoCambio = 1m;
                // El lugar de expedición es el CP del emisor, no un valor fijo: con el
                // perfil de pruebas (CP 42501) el CFDI salía declarando 78394.
                factura.LugarExpedicion = !string.IsNullOrWhiteSpace(GetEmisor("CpE"))
                    ? GetEmisor("CpE")
                    : "78394";
                factura.metodoPagoTexto = enc["mdp"].ToString();
                factura.MdpFactura = mdp;
                factura.TipoDeComprobante = "I";
                factura.Observaciones = enc["coment1"]?.ToString() ?? "";
                factura.formaPagoTexto = tp;
                factura.Fecha = DateTime.Now;
                // "documentos" NO era un tipo que XmlBuilderService supiera generar: su switch
                // sólo acepta contado/credito/anticipo/nc_*/arrendamiento/complemento/global,
                // así que caía en el default y lanzaba "Tipo de facturación no soportado".
                // El corte de caja emite la factura global; el resto, un ingreso PUE normal.
                // El tipo se deduce del propio documento (tipo_proceso = "factura_global",
                // que fija GuardarFacturaDesdeDocs), no de la sesión, para que un retimbrado
                // posterior siga tomando la decisión correcta.
                bool esFacturaGlobal = EsProcesoGlobal(enc["tipo_proceso"]?.ToString());
                bool esFacturaCredito = EsProcesoCredito(enc["tipo_proceso"]?.ToString());

                factura.TipoFacturacion =
                    esFacturaGlobal ? TipoFacturacionGlobal
                    : esFacturaCredito ? TipoFacturacionCredito
                    : TipoFacturacionContado;
                factura.EncabezadoId = idsOrigen.First();

                // 🔹 Crear DataTable de productos
                factura.Tproductos = new DataTable();
                factura.Tproductos.Columns.AddRange(new[]
                {
                    new DataColumn("numero", typeof(string)),
                    new DataColumn("claveProdServ", typeof(string)),
                    new DataColumn("claveUnidad", typeof(string)),
                    new DataColumn("unidad", typeof(string)),
                    new DataColumn("descripcion", typeof(string)),
                    new DataColumn("cantidad", typeof(double)),
                    new DataColumn("precioUnit", typeof(double)),
                    new DataColumn("importe", typeof(double)),
                    new DataColumn("objetoImp", typeof(string)),
                    // XmlBuilderService lee esta columna como PORCENTAJE de descuento.
                    // Se calcula por partida más abajo; no es dto1.
                    new DataColumn("descuento", typeof(decimal))
                });

                parameters = new Dictionary<string, object>();
                parameters.Add("encabezado", factura.EncabezadoId);

                string query = "SELECT uuid FROM encabezadomov WHERE id_encabezado = @encabezado";
                var result = RunScalar(query, parameters, false, conn, tx);

                if (result == null)
                {
                    throw new Exception("UUID no encontrado para el encabezado.");
                }

                string uuid = result.ToString();

                if (uuid.Length < 8)
                {
                    throw new Exception("UUID inválido para el encabezado.");
                }

                string uuidCorto = uuid.Substring(0, 8);

                factura.FolioCorto = uuidCorto;

                var sinClaveSat = new List<string>();

                foreach (var prod in partidas)
                {
                    var prodData = RunQuery(
                        "SELECT prod_sat, ud_sat, obj_impto FROM catrelacion WHERE prod_kepler = @cve",
                        new Dictionary<string, object> { { "cve", prod["cve_prod"].ToString() } }, false, conn, tx);

                    // Sin fila en catrelacion no hay clave de producto/unidad SAT y el CFDI
                    // no se puede armar. Antes esto reventaba con IndexOutOfRange y el
                    // cierre de caja fallaba sin decir qué producto era el culpable.
                    if (prodData.Count == 0)
                    {
                        sinClaveSat.Add(prod["cve_prod"].ToString());
                        continue;
                    }

                    // El descuento del CFDI se deduce de la diferencia entre el importe a
                    // precio de lista (cantidad × pv_prod) y el imp_part que ya guardó el
                    // documento. NO se toma de dto1: en estos documentos pv_prod ya viene
                    // con el descuento aplicado e imp_part == cantidad × pv_prod, de modo
                    // que mandar dto1 —que sólo deja constancia del descuento concedido— lo
                    // aplicaba una segunda vez y el comprobante salía por debajo del
                    // documento, de la póliza y de la cartera. Calculado así, el total del
                    // CFDI siempre coincide con el `imp` del encabezado, venga el descuento
                    // ya incorporado al precio o restado del imp_part.
                    decimal cantidad = DecimalDe(prod["cant_ud"]);
                    decimal precio = DecimalDe(prod["pv_prod"]);
                    decimal brutoPartida = Math.Round(cantidad * precio, 2);
                    decimal netoPartida = DecimalDe(prod["imp_part"]);
                    decimal descuentoPct = brutoPartida > 0m && netoPartida < brutoPartida
                        ? Math.Round((brutoPartida - netoPartida) / brutoPartida * 100m, 6)
                        : 0m;

                    factura.Tproductos.Rows.Add(
                        prod["cve_prod"].ToString(),
                        prodData[0]["prod_sat"].ToString(),
                        prodData[0]["ud_sat"].ToString(),
                        prod["ud"].ToString(),
                        prod["descr_prod"].ToString(),
                        Convert.ToDouble(cantidad),
                        Convert.ToDouble(precio),
                        Convert.ToDouble(brutoPartida),
                        prodData[0]["obj_impto"].ToString(),
                        descuentoPct
                    );
                }

                if (sinClaveSat.Count > 0)
                    return (false,
                        "No se puede timbrar: falta la clave SAT (catrelacion) de " +
                        string.Join(", ", sinClaveSat.Distinct()) +
                        ". Da de alta la relación del producto y vuelve a intentar el cierre.",
                        null);

                if (factura.Tproductos.Rows.Count == 0)
                    return (false, "La factura no tiene conceptos que timbrar.", null);

                // 🔹 Registrar póliza y cartera  PuntoDeVenta
                int encId = Convert.ToInt32(factura.EncabezadoId);

                List<PolizaData> poliza = GenerarDatosPoliza(encId, cuentaBanco, null, conn, tx);
                var resultadoP = RegistrarPolizas(GetUserId(User.Identity.Name), encId, poliza, false, null, conn, tx);

                // La póliza de la factura sólo genera la cartera: es el cargo al cliente.
                // El cobro sale de su propio documento CXC, con póliza aparte, para que la
                // entrada de dinero tenga respaldo documental propio. Antes ambos nacían de
                // esta misma póliza y la factura quedaba siendo cargo y abono a la vez.
                RegistrarCarteraResult cartera = RegistrarCartera(resultadoP[0].idPoliza, GetUserId(User.Identity.Name), conn, tx);

                // A crédito la cartera se queda abierta: el cobro llega después por
                // Crédito y Cobranza, con su complemento de pago. Registrarlo aquí daría
                // por cobrada una venta cuyo dinero todavía no existe.
                if (!esFacturaCredito)
                    RegistrarCobroConDocumentoCxc(encId, cartera.CarteraId, cuentaBanco, conn, tx);

                // El timbrado NO va aquí: lo ejecuta el llamador después del commit.
                return (true, "Factura preparada correctamente", factura);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PuntoDeVenta_Facturacion/?");
                return (false, "Error en PrepararFacturaDesdeDocs: " + ex.Message, null);
            }
        }

        /// <summary>
        /// Factura una venta a crédito recién registrada, DENTRO de la transacción de la
        /// venta: pedido → remisión → factura → póliza → cartera → timbrado.
        ///
        /// Es el mismo recorrido que hace <see cref="ProcesarDocumentosAsync"/> para una
        /// venta de contado, con dos diferencias: el destino es "credito" (así el CFDI sale
        /// nominativo y PPD, y la cartera se queda abierta) y no se pide cuenta de banco,
        /// porque no hay dinero que depositar todavía.
        ///
        /// Va en la misma transacción a propósito. Si el PAC rechaza el comprobante, se
        /// revierte TODO —incluida la salida de inventario—, en vez de dejar mercancía
        /// entregada a crédito sin CFDI ni cartera que respalde la deuda.
        /// </summary>
        /// <param name="idVenta">Encabezado VSUC de la venta a crédito.</param>
        /// <param name="usoCfdi">Uso de CFDI del receptor (del catálogo del cliente).</param>
        /// <param name="regimenReceptor">Régimen fiscal del receptor.</param>
        /// <param name="idFormaPago">Forma de pago del documento; en PPD es la clave SAT 99.</param>
        [NonAction]
        public async Task<(bool Ok, string Paso, string Error, string Folio, string Uuid)>
            FacturarVentaCreditoAsync(
                int idVenta,
                string usoCfdi,
                string regimenReceptor,
                int idFormaPago,
                NpgsqlConnection conn,
                NpgsqlTransaction tx)
        {
            string ids = idVenta.ToString();

            // 1️⃣ Pedido
            var pedido = await GuardarDesdeDocumentos(
                ids, conn, tx,
                mdp: MetodoPagoCreditoSat,
                tipo: TipoFacturacionCredito,
                fpago: idFormaPago.ToString(),
                cfdi: usoCfdi,
                rFiscal: regimenReceptor);

            if (!pedido.Success)
                return (false, "GuardarDesdeDocumentos", pedido.Message, null, null);

            dynamic dataPedido = pedido.Data;
            int idPedido = dataPedido.IdEncabezado;

            // 2️⃣ Remisión
            var remision = await GuardarRemisionDesdePedidos(idPedido.ToString(), conn, tx);
            if (!remision.Success)
                return (false, "GuardarRemisionDesdePedidos", remision.Message, null, null);

            dynamic dataRemision = remision.Data;
            int idRemision = dataRemision.IdEncabezado;

            // 3️⃣ Factura
            var facturaDoc = await GuardarFacturaDesdeDocs(idRemision.ToString(), conn, tx);
            if (!facturaDoc.Success)
                return (false, "GuardarFacturaDesdeDocs", facturaDoc.Message, null, null);

            dynamic dataFactura = facturaDoc.Data;
            int idFacturaDoc = dataFactura.IdEncabezado;
            string folioFactura = dataFactura.Folio;

            // 4️⃣ Póliza y cartera. Sin cuenta de banco: la póliza de una factura a crédito
            //    carga al cliente, no a un banco, y PrepararFacturaDesdeDocs omite el cobro
            //    al ver que el documento es "factura_credito".
            var preparada = await PrepararFacturaDesdeDocs(idFacturaDoc.ToString(), conn, tx, null);
            if (!preparada.Success)
                return (false, "PrepararFacturaDesdeDocs", preparada.Message, null, null);

            // 5️⃣ La venta queda cerrada y apuntando a su factura, igual que en el corte.
            RunUpdate(
                "UPDATE encabezadomov SET encabezado_hijo = @id, estatus_id = 11 WHERE id_encabezado = @venta;",
                new Dictionary<string, object> { { "id", idFacturaDoc }, { "venta", idVenta } },
                false, conn, tx);

            // 6️⃣ Timbrado, todavía dentro de la transacción.
            var timbrado = GenerarXml(preparada.Factura, conn, tx);

            if (!timbrado.Success)
            {
                LogErrorHelper.RegistrarLog("PuntoDeVenta/VentaCredito", "SIN_FOLIO",
                    $"El PAC rechazó la factura de la venta a crédito {idVenta}; se revierte la venta completa: {timbrado.Message}",
                    User.Identity?.Name);

                return (false, "Timbrado", timbrado.Message, null, null);
            }

            return (true, null, null, folioFactura, timbrado.UUID);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Route ("PuntoDeVenta/ProcesarDocumentosAsync")]
        [AuditAction(Modulo = "Ventas Sucursales", Accion = "Timbrado de documentos de punto de venta")]
        public async Task<JsonResult> ProcesarDocumentosAsync(IFormCollection fc)
        {
            (bool Success, string Message, object Data) resultado1 = (false, string.Empty, null);
            string folioPedido = "";
            string folioRemision = "";
            string folioDactura = "";

            // Los IDs se validan y normalizan ANTES de tocar la base: la consulta final
            // los concatenaba en el SQL tal cual venían del form.
            var idsDocs = fc["idsDocumentos"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim(), out int v) ? v : 0)
                .Where(v => v > 0)
                .Distinct()
                .ToList();

            if (idsDocs.Count == 0)
                return Json(new { success = false, message = "No se recibieron IDs de documentos válidos." });

            string idsDocumentos = string.Join(",", idsDocs.Select(i => i.ToString()));
            int cantidadDocumentos = idsDocs.Count;

            Factura facturaPorTimbrar = null;
            int idFacturaGenerada = 0;
            TimbradoResult resultadoTimbrado = null;

            using (var conn = AbrirConexion())
            {

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        try
                        {
                            // 1️⃣ Guardar desde documentos base
                            resultado1 = await GuardarDesdeDocumentos(idsDocumentos, conn, tx, fc["mdp"].ToString(), fc["tipo"].ToString(), fc["fpago"].ToString(), fc["cfdi"].ToString(), fc["rFiscal"].ToString());
                            if (!resultado1.Success)
                                return Json(new { success = false, step = "GuardarDesdeDocumentos", error = resultado1.Message });

                            dynamic data = resultado1.Data;
                            int idEncabezado = data.IdEncabezado;

                            // 2️⃣ Crear remisiones desde pedidos
                            var resultado2 = await GuardarRemisionDesdePedidos(idEncabezado.ToString(), conn, tx);
                            if (!resultado2.Success)
                            {
                                //BorradoFacturasIncorrectas(idEncabezado, "pedido_desde_docs_lote", "");
                                return Json(new { success = false, step = "GuardarRemisionDesdePedidos", error = resultado2.Message });
                            }

                            dynamic data2 = resultado2.Data;
                            int idEncabezado2 = data2.IdEncabezado;
                            // 3️⃣ Crear facturas desde documentos
                            var resultado3 = await GuardarFacturaDesdeDocs(idEncabezado2.ToString(), conn, tx);
                            if (!resultado3.Success)
                            {
                                //BorradoFacturasIncorrectas(idEncabezado, "pedido_desde_docs_lote", "");
                                //BorradoFacturasIncorrectas(idEncabezado2, "remision", "VS");
                                return Json(new { success = false, step = "GuardarFacturaDesdeDocs", error = resultado3.Message });
                            }
                            dynamic data3 = resultado3.Data;
                            int idEncabezado3 = data3.IdEncabezado;

                            // 4️⃣ Preparar la factura + póliza/cartera/cobro (SIN timbrar)
                            var resultado4 = await PrepararFacturaDesdeDocs(idEncabezado3.ToString(), conn, tx, fc["banco"].ToString());
                            if (!resultado4.Success)
                                return Json(new { success = false, step = "PrepararFacturaDesdeDocs", error = resultado4.Message });

                            facturaPorTimbrar = resultado4.Factura;
                            idFacturaGenerada = idEncabezado3;

                            // 5️⃣ Marcar los documentos origen DENTRO de la transacción.
                            //    Antes esto corría en otra conexión: si el commit fallaba, los
                            //    VSUC quedaban en estatus 11 apuntando a un hijo inexistente.
                            RunUpdate(
                                "UPDATE encabezadomov SET encabezado_hijo = @id, estatus_id = 11 WHERE id_encabezado = ANY(@ids);",
                                new Dictionary<string, object>
                                {
                                    { "id",  idEncabezado3 },
                                    { "ids", idsDocs.ToArray() }
                                },
                                false, conn, tx);

                            //Folios
                            folioPedido = data.Folio;
                            folioRemision = data2.Folio;
                            folioDactura = data3.Folio;

                            // 6️⃣ Timbrado, DENTRO de la transacción.
                            //    El PAC valida el comprobante y puede rechazarlo (uso de CFDI
                            //    inválido, clave SAT ausente, RFC mal formado…). En ese caso no
                            //    se timbró nada, así que no debe quedar rastro en la base: el
                            //    rollback se lleva pedido, remisión, factura y póliza.
                            //    Es posible porque `factura` no tiene llaves foráneas y
                            //    GuardarFactura acepta la misma conn/tx.
                            resultadoTimbrado = GenerarXml(facturaPorTimbrar, conn, tx);

                            if (!resultadoTimbrado.Success)
                            {
                                RegistrarRechazoPac(resultadoTimbrado.Message, "Ventas/PuntoDeVenta");
                                // Al salir del using sin commit, Npgsql revierte todo.
                                LogErrorHelper.RegistrarLog("PuntoDeVenta/ProcesarDocumentos", "SIN_FOLIO",
                                    $"El PAC rechazó el comprobante; se revirtieron los documentos " +
                                    $"(docs {idsDocumentos}): {resultadoTimbrado.Message}", User.Identity?.Name);

                                return Json(new
                                {
                                    success = false,
                                    step = "Timbrado",
                                    error = resultadoTimbrado.Message,
                                    message = "El timbrado falló y no se guardó nada: corrige el problema " +
                                              "y vuelve a intentar la facturación."
                                });
                            }

                            // Timbrado exitoso: de aquí en adelante el commit debe completarse.
                            // Si fallara, quedaría un CFDI vivo en el SAT sin respaldo local, así
                            // que se deja el UUID en el log para poder recuperarlo o cancelarlo.
                            try
                            {
                                tx.Commit();
                            }
                            catch (Exception exCommit)
                            {
                                LogErrorHelper.RegistrarLog("PuntoDeVenta/ProcesarDocumentos",
                                    resultadoTimbrado.UUID,
                                    "TIMBRADO OK PERO FALLÓ EL COMMIT. Hay un CFDI vivo en el SAT sin " +
                                    $"documentos en el ERP. UUID: {resultadoTimbrado.UUID}. {exCommit}",
                                    User.Identity?.Name);
                                throw;
                            }
                        }

                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "PuntoDeVenta_Facturacion/?");
                        LogErrorHelper.RegistrarLog("PuntoDeVenta/ProcesarDocumentos", "SIN_FOLIO",
                            $"Fallo antes del timbrado (docs {idsDocumentos}): {ex}", User.Identity?.Name);

                        return Json(new
                        {
                            success = false,
                            message = "Error general en el proceso: " + ex.Message
                        });
                    }
                }
            }

            // Aquí ya está todo commiteado y timbrado.
            return Json(new
            {
                success = true,
                message = "Todos los documentos fueron procesados y timbrados correctamente",
                resumen = new
                {
                    documentosProcesados = cantidadDocumentos,
                    tiempoFinalizacion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                },
                detalles = new
                {
                    pedidoId = folioPedido,
                    remisionId = folioRemision,
                    facturaId = folioDactura,

                    // Encabezado de la VENTA (no de la factura): es de donde cuelgan los
                    // cobros y los pendientes, y con él se arma el ticket de mostrador.
                    ventaId = idsDocs.FirstOrDefault(),

                    // Datos de facturación
                    uuid = resultadoTimbrado.UUID,
                    total = facturaPorTimbrar.Total,
                    subtotal = facturaPorTimbrar.Subtotal,
                    iva = facturaPorTimbrar.IVA,

                    // Datos del cliente
                    rfcCliente = facturaPorTimbrar.RfcCliente,
                    razonSocial = facturaPorTimbrar.RsoCliente,

                    // Datos del comprobante
                    serie = facturaPorTimbrar.Serie,
                    folio = facturaPorTimbrar.Folio,
                    fecha = facturaPorTimbrar.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                    cantidadProductos = facturaPorTimbrar.Tproductos.Rows.Count,

                    // URLs de descarga
                    pdfUrl = Url.Content($"~/Facturacion/facturas/{resultadoTimbrado.UUID}.pdf"),
                    xmlUrl = Url.Content($"~/Facturacion/xml_timbrados/{resultadoTimbrado.UUID}.xml")
                }
            });
        }
    }
}
