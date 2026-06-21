using System.Text.Json;

namespace Neomotive.Vin.CatalogGenerator;

internal sealed class NhtsaApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string BaseUrl = "https://vpic.nhtsa.dot.gov/api/";

    private readonly HttpClient _http;

    public NhtsaApiClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<NhtsaMakeResult>> GetAllMakesAsync(CancellationToken ct)
    {
        var json = await _http.GetStringAsync("vehicles/GetAllMakes?format=json", ct);
        return Deserialize<NhtsaMakeResult>(json);
    }

    public async Task<List<NhtsaModelResult>> GetModelsForMakeAsync(string make, CancellationToken ct)
    {
        var url = $"vehicles/GetModelsForMake/{Uri.EscapeDataString(make)}?format=json";
        var json = await _http.GetStringAsync(url, ct);
        return Deserialize<NhtsaModelResult>(json);
    }

    public async Task<List<NhtsaWmiResult>> GetWmisForManufacturerAsync(string manufacturer, CancellationToken ct)
    {
        var url = $"vehicles/GetWMIsForManufacturer/{Uri.EscapeDataString(manufacturer)}?format=json";
        var json = await _http.GetStringAsync(url, ct);
        return Deserialize<NhtsaWmiResult>(json);
    }

    private static List<T> Deserialize<T>(string json)
    {
        var envelope = JsonSerializer.Deserialize<NhtsaEnvelope<T>>(json, JsonOptions);
        return envelope?.Results ?? [];
    }

    public void Dispose() => _http.Dispose();
}
