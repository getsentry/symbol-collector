using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SymbolCollector.Core;

namespace SymbolCollector.Server.Models;

public class BatchStartRequestModel
{
    [Required]
    [StringLength(1000, ErrorMessage = "A batch friendly name can't be longer than 1000 characters.")]
    [Display(Name = "Batch friendly name")]
    public string BatchFriendlyName { get; set; } = default!; // model validation

    // TODO: Linux will need to be broken into distros
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Range((int)BatchType.WatchOS, (int)BatchType.Linux)]
    public BatchType BatchType { get; set; }
}