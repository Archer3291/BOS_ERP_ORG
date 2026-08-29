using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BOS_ERP.Services
{
    public interface IGemmaService
    {
        Task<string> ConsultarAsync(string pregunta);
        Task<string> ConsultarConHistorialAsync(List<OllamaMessage> historial);
        IAsyncEnumerable<string> ConsultarConHistorialStreamAsync(
            List<OllamaMessage> historial, CancellationToken ct = default);
        Task<bool> VerificarEstadoAsync();
    }
}