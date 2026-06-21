using System.Reflection;
using System.Text.Json;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Data;

internal sealed class ModelCatalogProvider : IModelCatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string ResourceName = "Neomotive.Vin.Resources.model-catalog.json";

    private readonly Lazy<IReadOnlyList<MakeCatalogEntry>> _entries = new(Load);

    private static IReadOnlyList<MakeCatalogEntry> Load()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return [];

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<List<MakeCatalogEntry>>(json, JsonOptions) ?? [];
    }

    private MakeCatalogEntry? FindMake(string make) =>
        _entries.Value.FirstOrDefault(e => e.Make.Equals(make, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> GetMakes() =>
        [.. _entries.Value.Select(e => e.Make)];

    public IReadOnlyList<string> GetModels(string make) =>
        FindMake(make)?.Models.Select(m => m.Name).ToList() ?? [];

    public IReadOnlyList<int> GetYears(string make, string model)
    {
        var entry = FindMake(make)?.Models
            .FirstOrDefault(m => m.Name.Equals(model, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return [];
        int end = entry.YearEnd == 9999 ? DateTime.UtcNow.Year : entry.YearEnd;
        return [.. Enumerable.Range(entry.YearStart, end - entry.YearStart + 1)];
    }

    public IReadOnlyList<string> GetPackages(string make, string model, int year)
    {
        var entry = FindMake(make)?.Models
            .FirstOrDefault(m => m.Name.Equals(model, StringComparison.OrdinalIgnoreCase)
                              && m.YearStart <= year && year <= (m.YearEnd == 9999 ? int.MaxValue : m.YearEnd));
        return entry?.Packages ?? [];
    }

    public VehicleModelInfo? GetModel(string make, string model) =>
        FindMake(make)?.Models
            .FirstOrDefault(m => m.Name.Equals(model, StringComparison.OrdinalIgnoreCase));

    public string? GetWmiForMake(string make) => FindMake(make)?.Wmi;
}
