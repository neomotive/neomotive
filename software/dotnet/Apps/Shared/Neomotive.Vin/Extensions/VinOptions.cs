namespace Neomotive.Vin.Extensions;

public sealed class VinOptions
{
    public Uri NhtsaBaseAddress { get; set; } = new("https://vpic.nhtsa.dot.gov/api/");
    public bool EnableNhtsaFallback { get; set; } = true;
    public TimeSpan NhtsaTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Optional directory to load manufacturers.json and model-catalog.json from
    /// before falling back to the embedded resources. Enables config-only updates
    /// without redeploying the app binary.
    /// </summary>
    public string? ExternalCatalogPath { get; set; }
}
