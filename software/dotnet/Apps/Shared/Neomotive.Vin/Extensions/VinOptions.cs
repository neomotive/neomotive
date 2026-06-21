namespace Neomotive.Vin.Extensions;

public sealed class VinOptions
{
    public Uri NhtsaBaseAddress { get; set; } = new("https://vpic.nhtsa.dot.gov/api/");
    public bool EnableNhtsaFallback { get; set; } = true;
    public TimeSpan NhtsaTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
