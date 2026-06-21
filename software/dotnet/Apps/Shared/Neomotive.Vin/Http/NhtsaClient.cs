using System.Text.Json;
using Neomotive.Vin.Contracts;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Http;

public sealed class NhtsaClient : INhtsaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public NhtsaClient(HttpClient http) => _http = http;

    public async Task<NhtsaDecodeResponse?> DecodeAsync(string vin, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"vehicles/decodevin/{Uri.EscapeDataString(vin)}?format=json";
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<NhtsaEnvelope>(json, JsonOptions);
            return envelope?.Results is null ? null : MapResults(envelope.Results);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static NhtsaDecodeResponse MapResults(IEnumerable<NhtsaResultItem> items)
    {
        var map = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Value) && i.Value != "Not Applicable")
            .ToDictionary(i => i.Variable ?? "", i => i.Value!, StringComparer.OrdinalIgnoreCase);

        return new NhtsaDecodeResponse
        {
            Make = Get(map, "Make"),
            Model = Get(map, "Model"),
            ModelYear = int.TryParse(Get(map, "Model Year"), out var y) ? y : null,
            Manufacturer = Get(map, "Manufacturer Name"),
            VehicleType = Get(map, "Vehicle Type"),
            BodyClass = Get(map, "Body Class"),
            EngineModel = Get(map, "Engine Model"),
            Trim = Get(map, "Trim"),
            DriveType = Get(map, "Drive Type"),
            FuelType = Get(map, "Fuel Type - Primary"),
            PlantCity = Get(map, "Plant City"),
            PlantCountry = Get(map, "Plant Country")
        };
    }

    private static string? Get(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) ? v : null;

    private sealed class NhtsaEnvelope
    {
        public List<NhtsaResultItem>? Results { get; set; }
    }

    private sealed class NhtsaResultItem
    {
        public string? Variable { get; set; }
        public string? Value { get; set; }
    }
}
