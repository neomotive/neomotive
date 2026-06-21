using System.Text.Json.Serialization;

namespace Neomotive.Vin.Models;

public sealed class MakeCatalogEntry
{
    [JsonPropertyName("make")]
    public required string Make { get; init; }

    [JsonPropertyName("wmi")]
    public required string Wmi { get; init; }

    [JsonPropertyName("models")]
    public IReadOnlyList<VehicleModelInfo> Models { get; init; } = [];
}
