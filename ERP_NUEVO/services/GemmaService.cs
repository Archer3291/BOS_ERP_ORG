using System.Runtime.CompilerServices;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;


namespace BOS_ERP.Services
{
    public class GemmaService : IGemmaService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GemmaService> _logger;

        private const string MODEL_NAME = "gemma4:12b-srs";
        private const string SYSTEM_PROMPT =
            "Eres un asistente de Sellos y Retenes de San Luis. " +
            "Responde en español de forma clara y profesional. Tus Respuestas deben de ser rapidas, no debe de tardar mas de 5 segundos en contestar. " +
            "A cualquiero cosa que no tenga que ver con sellos y retenes hidraulicos contesta con un \"No tengo informacion para lo que solicitas\" ";

        public GemmaService(IHttpClientFactory httpClientFactory, ILogger<GemmaService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<string> ConsultarAsync(string pregunta)
        {
            var mensajes = new List<OllamaMessage>
            {
                new() { Role = "system", Content = SYSTEM_PROMPT },
                new() { Role = "user",   Content = pregunta }
            };

            return await EnviarSolicitudAsync(mensajes);
        }

        public async Task<string> ConsultarConHistorialAsync(List<OllamaMessage> historial)
        {
            // Aseguramos que el system prompt siempre esté presente al inicio
            if (historial.Count == 0 || historial[0].Role != "system")
            {
                historial.Insert(0, new OllamaMessage { Role = "system", Content = SYSTEM_PROMPT });
            }

            return await EnviarSolicitudAsync(historial);
        }

        public async IAsyncEnumerable<string> ConsultarConHistorialStreamAsync(
    List<OllamaMessage> historial,
    [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (historial.Count == 0 || historial[0].Role != "system")
            {
                historial.Insert(0, new OllamaMessage { Role = "system", Content = SYSTEM_PROMPT });
            }

            var client = _httpClientFactory.CreateClient("Ollama");

            var request = new OllamaChatRequest
            {
                Model = MODEL_NAME,
                Messages = historial,
                Stream = true,
                Think = false,
                KeepAlive = "30m"
            };

            var json = JsonSerializer.Serialize(request);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error al comunicarse con Ollama (stream)");
                throw new ApplicationException("No se pudo conectar con el servicio de IA.", ex);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                OllamaChatStreamChunk? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<OllamaChatStreamChunk>(line);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Chunk NDJSON inválido, se ignora: {line}", line);
                    continue;
                }

                if (chunk?.Message?.Content is { Length: > 0 } fragmento)
                    yield return fragmento;

                if (chunk?.Done == true)
                    yield break;
            }
        }

        public async Task<bool> VerificarEstadoAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("Ollama");
                var response = await client.GetAsync("/");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama no está disponible");
                return false;
            }
        }

        private async Task<string> EnviarSolicitudAsync(List<OllamaMessage> mensajes)
        {
            var client = _httpClientFactory.CreateClient("Ollama");

            var request = new OllamaChatRequest
            {
                Model = MODEL_NAME,
                Messages = mensajes,
                Stream = false,
                Think = false
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync("/api/chat", content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaChatResponse>(responseBody);

                return result?.Message?.Content ?? "No se recibió respuesta del modelo.";
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error al comunicarse con Ollama");
                throw new ApplicationException("No se pudo conectar con el servicio de IA. Verifica que Ollama esté corriendo.", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout al esperar respuesta de Ollama");
                throw new ApplicationException("El asistente tardó demasiado en responder. Intenta de nuevo.", ex);
            }
        }
    }
}