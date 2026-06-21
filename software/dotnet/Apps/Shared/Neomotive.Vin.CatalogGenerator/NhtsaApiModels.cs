using System.Text.Json.Serialization;

namespace Neomotive.Vin.CatalogGenerator;

internal sealed class NhtsaEnvelope<T>
{
    [JsonPropertyName("Results")]
    public List<T> Results { get; set; } = [];
}

internal sealed class NhtsaMakeResult
{
    [JsonPropertyName("Make_ID")]
    public int MakeId { get; set; }

    [JsonPropertyName("Make_Name")]
    public string MakeName { get; set; } = "";
}

internal sealed class NhtsaModelResult
{
    [JsonPropertyName("Make_Name")]
    public string MakeName { get; set; } = "";

    [JsonPropertyName("Model_Name")]
    public string ModelName { get; set; } = "";
}

internal sealed class NhtsaWmiResult
{
    [JsonPropertyName("WMI")]
    public string Wmi { get; set; } = "";

    // NHTSA uses "Name" for the legal manufacturer name in the WMI endpoint
    [JsonPropertyName("Name")]
    public string ManufacturerName { get; set; } = "";

    [JsonPropertyName("VehicleType")]
    public string VehicleType { get; set; } = "";

    [JsonPropertyName("Country")]
    public string? Country { get; set; }
}
