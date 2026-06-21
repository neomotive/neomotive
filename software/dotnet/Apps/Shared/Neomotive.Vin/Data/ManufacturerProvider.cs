using System.Reflection;
using System.Text.Json;
using Neomotive.Vin.Contracts;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Data;

public sealed class ManufacturerProvider : IManufacturerProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string ResourceName = "Neomotive.Vin.Resources.manufacturers.json";

    private readonly Lazy<IReadOnlyDictionary<string, ManufacturerInfo>> _byWmi = new(Load);

    private static IReadOnlyDictionary<string, ManufacturerInfo> Load()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return new Dictionary<string, ManufacturerInfo>();

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var list = JsonSerializer.Deserialize<List<ManufacturerInfo>>(json, JsonOptions) ?? [];
        return list.ToDictionary(m => m.Wmi, StringComparer.OrdinalIgnoreCase);
    }

    internal ManufacturerInfo? TryGetByWmi(string wmi) =>
        _byWmi.Value.TryGetValue(wmi, out var m) ? m : null;

    public Task<ManufacturerInfo?> GetByWmiAsync(string wmi, CancellationToken cancellationToken = default) =>
        Task.FromResult(TryGetByWmi(wmi));

    public Task<IReadOnlyList<ManufacturerInfo>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ManufacturerInfo>>([.. _byWmi.Value.Values]);
}
