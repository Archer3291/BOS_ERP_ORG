using Microsoft.AspNetCore.SignalR;

namespace BOS_ERP.Hubs
{
    /// <summary>
    /// Canal en vivo de movimientos de inventario. Cada conexión entra al grupo de su
    /// empresa, tomado de la sesión y no de lo que mande el cliente, para que un evento
    /// solo llegue a las pestañas de esa empresa.
    /// </summary>
    public class InventarioHub : Hub
    {
        public const string Ruta = "/hubs/inventario";

        /// <summary>Nombre del método que escuchan las vistas.</summary>
        public const string EventoInventarioActualizado = "InventarioActualizado";

        public static string GrupoEmpresa(int empresaId) => $"empresa-{empresaId}";

        public override async Task OnConnectedAsync()
        {
            var session = Context.GetHttpContext()?.Session;

            if (session != null)
            {
                // La sesión se carga bajo demanda; en el handshake del hub hay que
                // pedirla explícitamente antes de leerla.
                await session.LoadAsync();

                var empresaId = session.GetInt32("Empresa");
                if (empresaId.HasValue)
                    await Groups.AddToGroupAsync(Context.ConnectionId, GrupoEmpresa(empresaId.Value));
            }

            await base.OnConnectedAsync();
        }
    }

    /// <summary>
    /// Lo que viaja al navegador cuando la migración Kepler toca inventario. Lleva
    /// sucursal y almacén para que cada vista decida si el cambio le aplica según sus
    /// filtros actuales, en vez de recargar a ciegas.
    /// </summary>
    public class InventarioActualizadoEvento
    {
        public string Origen { get; set; } = "MigracionKepler";
        public int Sucursal { get; set; }
        public int? Almacen { get; set; }
        public int ConCompra { get; set; }
        public int SoloStock { get; set; }
        public int? EncabezadoCompra { get; set; }
        public List<string> Codigos { get; set; } = new();
        public string Usuario { get; set; } = string.Empty;
        public DateTime Fecha { get; set; } = DateTime.Now;

        public string Resumen()
        {
            var partes = new List<string>();

            if (ConCompra > 0)
                partes.Add($"{ConCompra} con registro de compra");

            if (SoloStock > 0)
                partes.Add($"{SoloStock} con aumento de stock");

            return partes.Count == 0
                ? "Migración sin cambios en inventario"
                : string.Join(" y ", partes);
        }
    }
}
