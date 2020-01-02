using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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
