using System.Text.Json;

namespace SymbolCollector.Server.Tests
{
    internal static class ContentExtensions
    {
        public static async ValueTask<JsonElement> ToJsonElement(this HttpContent content)
        {
            var responseStream = await content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
        }
    }
}
