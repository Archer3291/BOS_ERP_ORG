using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BOS_ERP.Services
{
    public interface IConversacionService
    {
        Task<int> CrearConversacionAsync(int usuarioId, string? ipOrigen);
        Task<List<MensajeChat>> ObtenerMensajesAsync(int conversacionId, int usuarioId);
        Task<long> GuardarMensajeAsync(int conversacionId, int usuarioId, string rol, string contenido, string? modelo = null, int? tiempoRespuestaMs = null);
        Task ActualizarActividadAsync(int conversacionId);
        Task<List<Conversacion>> ObtenerConversacionesUsuarioAsync(int usuarioId, int limite = 20);
    }
}
