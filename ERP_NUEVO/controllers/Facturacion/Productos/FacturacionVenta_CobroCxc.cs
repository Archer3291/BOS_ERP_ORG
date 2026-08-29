using BOS_ERP.Models;
using BOS_ERP.Models.Carteras.Cliente;
using BOS_ERP.Models.CuentasContables;
using Npgsql;

namespace BOS_ERP.Controllers.Facturacion.Productos
{
    /// <summary>
    /// Cobro de una factura con su propio documento CXC.
    ///
    /// El cobro no puede colgar de la póliza de la factura. Esa póliza registra el ingreso
    /// devengado —la venta— y de ella nace la cartera; el cobro es un hecho distinto, el
    /// dinero entrando, y necesita su propio documento y su propia póliza. Cuando ambos
    /// salían de la misma póliza, la factura quedaba siendo a la vez el cargo y el abono, y
    /// no había documento que respaldara la entrada de efectivo.
    ///
    /// Es el mismo camino que ya recorre PagosClienteController al registrar un pago manual:
    ///     documento CXC → imp_oc → póliza del CXC → cobro → aplicación contra la cartera
    /// La diferencia es que aquí el cobro es automático y total, porque el punto de venta
    /// cobra al contado.
    /// </summary>
    public partial class FacturacionVentaController
    {
        // Los documentos de cobro a cliente viven en Crédito y Cobranza (área 20) con el
        // tipo de documento 70; así están dados de alta los 112 CXC existentes.
        private const int AreaCobroCliente = 20;
        private const int TpDocCobroCliente = 70;
        private const string NatCobroCliente = "CXC";

        private static decimal ADecimal(object valor, decimal porDefecto = 0m) =>
            valor == null || valor == DBNull.Value ? porDefecto : Convert.ToDecimal(valor);

        /// <summary>
        /// Crea el documento CXC del cobro, su póliza y el cobro, y lo aplica a la cartera.
        /// Todo dentro de la transacción que recibe.
        /// </summary>
        /// <param name="idFactura">Encabezado de la factura que se está cobrando; queda como padre del CXC.</param>
        /// <param name="idCartera">Cartera generada por la póliza de esa factura.</param>
        /// <param name="cuentaBanco">Cuenta de destino del dinero, para el asiento de la póliza.</param>
        protected CobroClienteResult RegistrarCobroConDocumentoCxc(
            int idFactura,
            int idCartera,
            string cuentaBanco,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            // Sin cuenta de banco, GenerarPolizaCobroCliente resuelve la cuenta contable a 0
            // y escribe un asiento contra una cuenta inexistente sin quejarse. Mejor cortar
            // aquí, dentro de la transacción, que dejar una póliza corrupta.
            if (string.IsNullOrWhiteSpace(cuentaBanco))
                throw new Exception(
                    "No se indicó la cuenta de banco del cobro; sin ella la póliza del " +
                    "documento CXC quedaría sin cuenta contable de destino.");

            var facturaData = RunQuery(@"
                SELECT em.suc, em.cli_prov, em.refe, em.centro_costos, em.ccy, em.par,
                       em.f_pago, em.veh, em.mdp, em.imp, em.coment1, em.alm
                FROM encabezadomov em
                WHERE em.id_encabezado = @id",
                new Dictionary<string, object> { { "id", idFactura } },
                false, conn, tx).FirstOrDefault();

            if (facturaData == null)
                throw new Exception($"No se encontró el documento de factura {idFactura} para generar su cobro.");

            int idCliente = GetInt(facturaData["refe"]) ?? 0;
            if (idCliente <= 0)
                throw new Exception(
                    $"El documento de factura {idFactura} no tiene cliente (refe vacío); " +
                    "sin él la póliza del cobro no puede resolver la cuenta contable.");

            decimal total = ADecimal(facturaData["imp"]);
            if (total <= 0)
                throw new Exception(
                    $"El documento de factura {idFactura} tiene importe {total:C2}; no se puede cobrar.");

            // El IVA se separa del total cobrado igual que en el pago manual: aquí el cobro
            // siempre es por el importe completo del documento, así que la proporción es 1.
            decimal subtotal = Math.Round(total / 1.16m, 2, MidpointRounding.AwayFromZero);
            decimal iva = total - subtotal;

            var encabezado = new DocumentoEncabezado
            {
                EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                IdArea = AreaCobroCliente,
                IdTpDoc = TpDocCobroCliente,
                UsrDep = GetAreaName(User.Identity.Name),
                Anio = DateTime.Now.Year,
                Suc = Convert.ToInt32(facturaData["suc"]),
                Alm = facturaData["alm"]?.ToString() ?? "",
                Fch = DateTime.Now,
                TpMov = NatCobroCliente,
                UsrDoc = User.Identity.Name,
                FchCap = DateTime.Now,
                Usr0 = GetUserId(User.Identity.Name),
                Fch0 = DateTime.Now,
                CliProv = facturaData["cli_prov"]?.ToString(),
                Ref = idCliente,
                Ccy = facturaData["ccy"]?.ToString() ?? "PESOS",
                Par = ADecimal(facturaData["par"], 1m) > 0 ? ADecimal(facturaData["par"], 1m) : 1m,
                Estatus = 1,
                Sub = subtotal,
                Imp = total,
                // Se conservan las formas de pago del documento cobrado: son las que el
                // cajero capturó y las que debe cuadrar el corte.
                FPago = GetInt(facturaData["f_pago"]),
                Veh = facturaData["veh"]?.ToString(),
                Mdp = facturaData["mdp"]?.ToString() ?? "PUE",
                TipoPoceso = "cobro_cliente",
                CentroCostos = GetInt(facturaData["centro_costos"]),
                EncabezadoPadre = idFactura,
                IdCartera = idCartera,
                Coment1 = facturaData["coment1"]?.ToString()
            };

            var documento = GenerarDocumentoConPartidas(encabezado, new List<PartidaDocumento>(), conn, tx);
            int idCxc = Convert.ToInt32(documento["IdEncabezado"]);

            // La póliza del CXC lee sus impuestos de imp_oc: sin este renglón el asiento
            // saldría sin IVA y no cuadraría contra la cartera.
            RunUpdate(@"
                INSERT INTO imp_oc
                    (encabezado_id, impuesto_id, subtotal, importe, orden_apl, imp_variable, prov_nom, f_pago_id)
                VALUES
                    (@encabezado_id, @impuesto, @subtotal, @importe, 1, 16, @prov_nom, @f_pago_id)",
                new Dictionary<string, object>
                {
                    { "encabezado_id", idCxc },
                    { "impuesto",      Convert.ToInt32(GetSetting("impuesto")) },
                    { "subtotal",      subtotal },
                    { "importe",       iva },
                    { "prov_nom",      encabezado.CliProv ?? "" },
                    { "f_pago_id",     encabezado.FPago ?? 0 }
                }, false, conn, tx);

            var polizaCxc = GenerarDatosPoliza(idCxc, cuentaBanco, null, conn, tx);
            var registrada = RegistrarPolizas(
                GetUserId(User.Identity.Name), idCxc, polizaCxc, false, null, conn, tx);

            if (registrada == null || registrada.Count == 0)
                throw new Exception($"No se generó la póliza del documento de cobro {idCxc}.");

            var cobro = CrearCobroCliente(
                registrada[0].idPoliza, GetUserId(User.Identity.Name), TipoCobroCliente.Normal, conn, tx);

            var aplicacion = new AplicarCobros { UsuarioId = GetUserId(User.Identity.Name), ClienteId = idCliente };
            aplicacion.CarteraIds.Add(idCartera);
            aplicacion.CobrosIds.Add(cobro.PagoId);
            AplicarCobrosCliente(aplicacion, conn, tx);

            return cobro;
        }
    }
}
