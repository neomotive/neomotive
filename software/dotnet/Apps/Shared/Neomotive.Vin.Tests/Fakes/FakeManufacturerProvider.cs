using Neomotive.Vin.Contracts;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Tests.Fakes;

internal sealed class FakeManufacturerProvider : IManufacturerProvider
{
    private readonly Dictionary<string, ManufacturerInfo> _data;

    public FakeManufacturerProvider(params ManufacturerInfo[] entries)
        => _data = entries.ToDictionary(e => e.Wmi, StringComparer.OrdinalIgnoreCase);

    public Task<ManufacturerInfo?> GetByWmiAsync(string wmi, CancellationToken cancellationToken = default)
        => Task.FromResult(_data.TryGetValue(wmi, out var m) ? m : null);

    public Task<IReadOnlyList<ManufacturerInfo>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ManufacturerInfo>>([.. _data.Values]);
}
