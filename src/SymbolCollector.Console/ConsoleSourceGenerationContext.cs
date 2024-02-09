using System.Text.Json.Serialization;

namespace SymbolCollector.Console;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MetaContent))]
public partial class ConsoleSourceGenerationContext : JsonSerializerContext
{
}

public class MetaContent
{
    public string? name { get; set; }
    public string? arch { get; set; }
    public string? file_format { get; set; }
}
