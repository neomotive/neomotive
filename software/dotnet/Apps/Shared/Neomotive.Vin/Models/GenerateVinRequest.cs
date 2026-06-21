namespace Neomotive.Vin.Models;

public sealed class GenerateVinRequest
{
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required int Year { get; init; }
    public string? Package { get; init; }

    /// <summary>6-character production sequence. Null generates a random numeric value.</summary>
    public string? SequenceNumber { get; init; }
}
