using System;
using System.Collections.Concurrent;
using System.Linq;

namespace BOS_ERP.Infrastructure
{
    public static class TokenStore
    {
        private static readonly ConcurrentDictionary<string, AuthToken> _store
            = new ConcurrentDictionary<string, AuthToken>();

        public class AuthToken
        {
            public string Usuario { get; set; }
            public string Tipo { get; set; }  // "DESCUENTO" | "CAMBIO DE PRECIO"
            public DateTime Expira { get; set; }
            public bool Usado { get; set; }
        }

        public static void Guardar(string token, AuthToken data)
            => _store[token] = data;

        public static bool Validar(string token, string usuario, string tipo)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (!_store.TryGetValue(token, out var t)) return false;

            return t.Usuario == usuario
                && t.Tipo == tipo
                && t.Expira > DateTime.UtcNow
                && !t.Usado;
        }

        public static void Invalidar(string token)
        {
            if (!string.IsNullOrWhiteSpace(token) && _store.TryGetValue(token, out var t))
                t.Usado = true;
        }

        public static void LimpiarExpirados()
        {
            foreach (var key in _store
                .Where(kv => kv.Value.Expira < DateTime.UtcNow || kv.Value.Usado)
                .Select(kv => kv.Key)
                .ToList())
                _store.TryRemove(key, out _);
        }
    }
}