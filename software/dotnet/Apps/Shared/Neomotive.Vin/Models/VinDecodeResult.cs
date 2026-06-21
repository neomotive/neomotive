namespace Neomotive.Vin.Models;

public sealed class VinDecodeResult
{
    public required string Vin { get; init; }
    public required ValidationResult Validation { get; init; }
    public WmiInfo? Wmi { get; init; }
    public VdsInfo? Vds { get; init; }
    public VisInfo? Vis { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? Year { get; init; }
    public string? Trim { get; init; }
    public string? EngineType { get; init; }
    public string? PlantCity { get; init; }
    public string? Country { get; init; }
    public bool IsFromNhtsa { get; init; }
}
