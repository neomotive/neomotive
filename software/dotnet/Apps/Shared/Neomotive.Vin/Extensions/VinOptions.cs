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

    /// <summary>
    /// Where <c>vds-patterns.json</c> is read from and written to. Unlike the catalogs this file is
    /// written at runtime — the decoder records what it learns from an online decode so the next
    /// vehicle of the same type resolves offline — so it belongs in a writable directory that
    /// survives an update, not next to the app binary. Falls back to
    /// <see cref="ExternalCatalogPath"/>; when neither is set, patterns are read-only.
    /// </summary>
    public string? PatternStorePath { get; set; }
}
