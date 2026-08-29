using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.ServiceModel;

namespace BOS_ERP.Services
{
    public class ModulaOutboundResult
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; }
    }

    public static class ModulaOutboundHelper
    {
        public class ItemSalida
        {
            public string Codigo { get; set; }
            public decimal Cantidad { get; set; }
            public string Lote { get; set; }
        }

        // ── Envía la solicitud de salida a Modula vía postMaintenance ──
        // ⚠️ Basado en la hipótesis de que RIG_QTAR negativo = salida.
        //     CONFIRMAR con Modula/manual del integrador antes de producción.
        public static ModulaOutboundResult EnviarSalida(
            IConfiguration configuration,
            string ordenNumero,
            string ordenDescripcion,
            List<ItemSalida> items)
        {
            var login = new ServiceReference2.Login
            {
                User = configuration["Modula:Usuario"],
                Pass = configuration["Modula:Password"]
            };

            var client = new ServiceReference2.InterfaceClient();
            client.InnerChannel.OperationTimeout = TimeSpan.FromSeconds(60);

            try
            {
                var orders = items.Select(i => new ServiceReference2.Maintenance
                {
                    ORD_ORDINE = ordenNumero,
                    ORD_DES = ordenDescripcion,
                    RIG_ARTICOLO = i.Codigo,
                    // Cantidad NEGATIVA para indicar salida/descarga
                    RIG_QTAR = (-i.Cantidad).ToString(CultureInfo.InvariantCulture),
                    RIG_SUB1 = i.Lote ?? "",
                    RIG_HOSTINF = "",
                    RIG_REQ_NOTE = $"Salida por surtido - Verificación de Almacén",
                    RIG_PRIO = "1"
                }).ToArray();

                var response = client.postMaintenance(login, orders);

                if (client.State == CommunicationState.Faulted) client.Abort();
                else client.Close();

                bool exito = response.All(r => r.Status == "OK");
                string mensaje = string.Join(" | ", response.Select(r =>
                    $"Status:{r.Status} Msg:{r.Message} Rows:{r.Rows}"));

                return new ModulaOutboundResult { Exito = exito, Mensaje = mensaje };
            }
            catch (Exception ex)
            {
                try
                {
                    if (client.State == CommunicationState.Faulted) client.Abort();
                    else client.Close();
                }
                catch { }

                return new ModulaOutboundResult { Exito = false, Mensaje = "ERROR: " + ex.Message };
            }
        }
    }
}