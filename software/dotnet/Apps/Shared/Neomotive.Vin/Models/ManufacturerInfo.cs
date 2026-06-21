using System.Text.Json.Serialization;

namespace Neomotive.Vin.Models;

public sealed class ManufacturerInfo
{
    [JsonPropertyName("wmi")]
    public required string Wmi { get; init; }

    [JsonPropertyName("manufacturer")]
    public required string Manufacturer { get; init; }

    [JsonPropertyName("country")]
    public required string Country { get; init; }

    [JsonPropertyName("vehicleType")]
    public required string VehicleType { get; init; }

    [JsonPropertyName("makes")]
    public IReadOnlyList<string> Makes { get; init; } = [];
}
