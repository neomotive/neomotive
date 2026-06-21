using Neomotive.Vin.Models;

namespace Neomotive.Vin.Contracts;

public interface IVinGenerator
{
    Task<string> GenerateAsync(GenerateVinRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetMakesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetModelsAsync(string make, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetYearsAsync(string make, string model, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetPackagesAsync(string make, string model, int year, CancellationToken cancellationToken = default);
}
