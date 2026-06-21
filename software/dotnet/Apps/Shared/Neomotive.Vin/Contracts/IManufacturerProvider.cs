using Neomotive.Vin.Models;

namespace Neomotive.Vin.Contracts;

public interface IManufacturerProvider
{
    Task<ManufacturerInfo?> GetByWmiAsync(string wmi, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManufacturerInfo>> GetAllAsync(CancellationToken cancellationToken = default);
}
