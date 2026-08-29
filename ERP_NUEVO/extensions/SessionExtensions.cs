// Extensions/SessionExtensions.cs
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace BOS_ERP.Extensions
{
    // ✅ ASP.NET Core Session solo soporta string/int32/byte[] de forma nativa.
    // Estos métodos de extensión permiten guardar/leer objetos complejos
    // (como UsuarioTema) serializándolos a JSON, tal como se acordó.
    public static class SessionExtensions
    {
        public static void SetObjectAsJson(this ISession session, string key, object value)
        {
            session.SetString(key, JsonSerializer.Serialize(value));
        }

        public static T? GetObjectFromJson<T>(this ISession session, string key)
        {
            var value = session.GetString(key);
            return value == null ? default : JsonSerializer.Deserialize<T>(value);
        }
    }
}
