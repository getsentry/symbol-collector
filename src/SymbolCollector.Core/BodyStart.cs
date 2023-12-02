using System.Text.Json.Serialization;

namespace SymbolCollector.Core;

public class BodyStart
{
    public string? BatchFriendlyName { get; set; }
    // [JsonConverter(typeof(JsonStringEnumConverter))]
    public BatchType BatchType { get; set; }
}

public class BodyStop
{
    public ClientMetrics? ClientMetrics { get; set; }
}



[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BodyStart))]
[JsonSerializable(typeof(BatchType))]

[JsonSerializable(typeof(BodyStop))]
[JsonSerializable(typeof(ClientMetrics))]

[JsonSerializable(typeof(SymbolClientOptions))]
[JsonSerializable(typeof(Uri))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(HashSet<string>))]
public partial class SourceGenerationContext : JsonSerializerContext
{
}
