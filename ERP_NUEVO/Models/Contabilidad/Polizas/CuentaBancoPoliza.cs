using System.Collections.Generic;

namespace BOS_ERP.Models
{
    /// <summary>
    /// Cuenta de banco por la que entra o sale el dinero de un documento, junto con el
    /// importe que le toca.
    ///
    /// Existe porque un cobro puede repartirse entre varias cuentas —el pago mixto del
    /// punto de venta: una parte en efectivo, otra en la cuenta de la terminal— y cada
    /// parte necesita su propio asiento en la póliza. Antes solo viajaba el código de una
    /// cuenta como string y la partida BANCO de la plantilla generaba un único asiento.
    /// </summary>
    public class CuentaBancoPoliza
    {
        /// <summary>Código de la cuenta, tal como está en cuentas_finanzas.codigo.</summary>
        public string Cuenta { get; set; }

        /// <summary>
        /// Importe que se mueve por esta cuenta. En null el asiento toma el monto que marca
        /// la plantilla (origen_monto × factor), que es como se comportaba cuando la póliza
        /// solo admitía una cuenta. Con dos o más cuentas el importe es obligatorio: repartir
        /// el monto de la plantilla entre todas descuadraría la póliza.
        /// </summary>
        public decimal? Importe { get; set; }

        public CuentaBancoPoliza() { }

        public CuentaBancoPoliza(string cuenta, decimal? importe = null)
        {
            Cuenta = cuenta;
            Importe = importe;
        }
    }

    /// <summary>
    /// Cuentas de banco que trae un documento para su póliza.
    ///
    /// Es una lista y no un List&lt;CuentaBancoPoliza&gt; a secas por la conversión implícita
    /// desde string: las decenas de llamadas a GenerarDatosPoliza que ya pasaban el código de
    /// una sola cuenta —"1-1-02-01-0002", fc["banco"].ToString(), etc.— siguen compilando y
    /// comportándose igual, y solo quien reparte un cobro entre varias cuentas arma la lista.
    /// </summary>
    public class CuentasBancoPoliza : List<CuentaBancoPoliza>
    {
        public CuentasBancoPoliza() { }

        public CuentasBancoPoliza(IEnumerable<CuentaBancoPoliza> cuentas) : base(cuentas) { }

        /// <summary>
        /// Una sola cuenta sin importe: se lleva el monto que marque la plantilla. Un código
        /// vacío da una lista vacía, para que el caso "no se indicó banco" siga fallando donde
        /// fallaba —al resolver la cuenta contable— y no antes.
        /// </summary>
        public static implicit operator CuentasBancoPoliza(string cuenta)
        {
            var lista = new CuentasBancoPoliza();

            if (!string.IsNullOrWhiteSpace(cuenta))
                lista.Add(new CuentaBancoPoliza(cuenta));

            return lista;
        }
    }
}
