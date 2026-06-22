using System.Text.Json.Serialization;

namespace Neomotive.ScanTool.UI;

internal sealed class AppConfig
{
    [JsonPropertyName("updateServerUrl")]
    public string? UpdateServerUrl { get; init; }
}
