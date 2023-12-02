using System.Text.Json;
using MediaTypeHeaderValue = System.Net.Http.Headers.MediaTypeHeaderValue;

namespace SymbolCollector.Server.Tests
{
    internal class JsonContent : ByteArrayContent
    {
        public JsonContent(object model) : base(JsonSerializer.SerializeToUtf8Bytes(model))
        {
            Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        }
    }
}
