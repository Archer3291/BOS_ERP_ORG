using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace BOS_ERP.Filters
{
    public class ApiKeyAuthorizationFilter : IAsyncAuthorizationFilter
    {
        private readonly string _apiKey;
        private readonly IMemoryCache _cache;

        // Aquí sí funciona la inyección de dependencias
        public ApiKeyAuthorizationFilter(IConfiguration config, IMemoryCache cache)
        {
            _apiKey = config["ApiKey"]!;
            _cache = cache;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var request = context.HttpContext.Request;

            var auth = request.Headers["Authorization"].ToString();

            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
            {
                Deny(context, "Authorization inválido");
                return;
            }

            var apiKeyFromClient = auth.Replace("Bearer ", "");

            if (apiKeyFromClient != _apiKey)
            {
                Deny(context, "Token inválido");
                return;
            }

            if (!request.Headers.ContainsKey("X-Timestamp") ||
                !request.Headers.ContainsKey("X-Signature"))
            {
                Deny(context, "Headers de seguridad faltantes");
                return;
            }

            var timestamp = request.Headers["X-Timestamp"].ToString();
            var signature = request.Headers["X-Signature"].ToString();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!long.TryParse(timestamp, out var requestTime) ||
                Math.Abs(now - requestTime) > 300)
            {
                Deny(context, "Request expirada");
                return;
            }

            var nonce = request.Headers["X-Nonce"].ToString();

            if (string.IsNullOrEmpty(nonce))
            {
                Deny(context, "Nonce faltante");
                return;
            }

            // Si el nonce ya fue usado, rechaza
            if (_cache.TryGetValue($"nonce:{nonce}", out _))
            {
                Deny(context, "Request duplicada");
                return;
            }

            // Guarda el nonce por 5 minutos (igual que tu ventana de tiempo)
            _cache.Set($"nonce:{nonce}", true, TimeSpan.FromMinutes(5));

            request.EnableBuffering();
            request.Body.Position = 0;

            string body = "";
            using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
                request.Body.Position = 0;
            }

            var fullPath = request.Path + request.QueryString;
            var payload = request.Method + fullPath + (request.Method == "GET" ? "" : body) + timestamp;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

            if (!FixedTimeEquals(signature, computedSignature))
            {
                Deny(context, "Firma inválida");
                return;
            }
        }

        private void Deny(AuthorizationFilterContext context, string message)
        {
            context.Result = new JsonResult(new
            {
                error = message,
                status = 401,
                cat = "https://http.cat/401"
            })
            {
                StatusCode = 401
            };
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;

            var result = 0;
            for (int i = 0; i < a.Length; i++)
                result |= a[i] ^ b[i];

            return result == 0;
        }
    }
}