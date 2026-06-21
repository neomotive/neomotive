using System.Text.Json.Serialization;

namespace Neomotive.Vin.Models;

public sealed class VehicleModelInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("yearStart")]
    public int YearStart { get; init; }

    [JsonPropertyName("yearEnd")]
    public int YearEnd { get; init; } = 9999;

    [JsonPropertyName("packages")]
    public IReadOnlyList<string> Packages { get; init; } = [];

    [JsonPropertyName("plants")]
    public IReadOnlyDictionary<string, string> Plants { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("vdsTemplate")]
    public VdsTemplate? VdsTemplate { get; init; }
}

public sealed class VdsTemplate
{
    [JsonPropertyName("modelCode")]
    public string? ModelCode { get; init; }

    [JsonPropertyName("bodyStyle")]
    public string? BodyStyle { get; init; }

    [JsonPropertyName("engineCodes")]
    public IReadOnlyDictionary<string, string> EngineCodes { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("restraintCode")]
    public string? RestraintCode { get; init; }
}
