namespace Neomotive.Vin.Models;

public sealed class NhtsaDecodeResponse
{
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? ModelYear { get; init; }
    public string? Manufacturer { get; init; }
    public string? VehicleType { get; init; }
    public string? BodyClass { get; init; }
    public string? EngineModel { get; init; }
    public string? Trim { get; init; }
    public string? DriveType { get; init; }
    public string? FuelType { get; init; }
    public string? PlantCity { get; init; }
    public string? PlantCountry { get; init; }
}
