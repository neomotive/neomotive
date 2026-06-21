using Neomotive.Vin.Contracts;
using Neomotive.Vin.Data;
using Neomotive.Vin.Models;

namespace Neomotive.Vin.Core;

public sealed class VinGenerator : IVinGenerator
{
    private static readonly Random Rng = Random.Shared;
    private readonly IModelCatalogProvider _catalog;

    public VinGenerator(IModelCatalogProvider catalog) => _catalog = catalog;

    public Task<IReadOnlyList<string>> GetMakesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog.GetMakes());

    public Task<IReadOnlyList<string>> GetModelsAsync(string make, CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog.GetModels(make));

    public Task<IReadOnlyList<int>> GetYearsAsync(string make, string model, CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog.GetYears(make, model));

    public Task<IReadOnlyList<string>> GetPackagesAsync(string make, string model, int year, CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog.GetPackages(make, model, year));

    public Task<string> GenerateAsync(GenerateVinRequest request, CancellationToken cancellationToken = default)
    {
        var wmi = _catalog.GetWmiForMake(request.Make)
            ?? throw new InvalidOperationException($"Make '{request.Make}' not found in catalog.");

        var modelEntry = _catalog.GetModel(request.Make, request.Model);

        // Build VDS (positions 4–8, indices 3–7)
        char vds0 = BuildModelCode(request.Model, modelEntry, request.Package);
        char vds1 = BuildBodyStyle(modelEntry, request.Package);
        char vds2 = BuildEngineCode(modelEntry, request.Package);
        char vds3 = '0';
        char vds4 = '0';

        // Model year code (position 10, index 9)
        char yearCode = VinCharTable.EncodeModelYear(request.Year);

        // Plant code (position 11, index 10)
        char plantCode = modelEntry?.Plants.Keys.FirstOrDefault()?[0] ?? '0';

        // Sequence number (positions 12–17, indices 11–16)
        var seq = BuildSequence(request.SequenceNumber);

        // Assemble with '0' placeholder at check digit position (index 8)
        var raw = $"{wmi}{vds0}{vds1}{vds2}{vds3}{vds4}0{yearCode}{plantCode}{seq}";
        var check = VinCharTable.ComputeCheckDigit(raw);
        var vin = raw[..8] + check + raw[9..];

        return Task.FromResult(vin);
    }

    private static char BuildModelCode(string model, VehicleModelInfo? entry, string? package)
    {
        if (entry?.VdsTemplate?.ModelCode is { Length: > 0 } mc)
            return char.ToUpperInvariant(mc[0]);
        // Use first letter of model name as a placeholder
        char c = char.ToUpperInvariant(model.FirstOrDefault(char.IsLetter));
        return VinCharTable.IsValidVinChar(c) ? c : 'A';
    }

    private static char BuildBodyStyle(VehicleModelInfo? entry, string? package) =>
        entry?.VdsTemplate?.BodyStyle is { Length: > 0 } bs ? bs[0] : '0';

    private static char BuildEngineCode(VehicleModelInfo? entry, string? package)
    {
        if (package is not null && entry?.VdsTemplate?.EngineCodes.TryGetValue(package, out var ec) == true)
            return ec[0];
        return '0';
    }

    private static string BuildSequence(string? requested)
    {
        if (requested is not null)
        {
            if (requested.Length == 6 && requested.All(char.IsAsciiDigit))
                return requested;
            throw new ArgumentException("SequenceNumber must be exactly 6 ASCII digits.", nameof(requested));
        }
        return Rng.Next(0, 999999).ToString("D6");
    }
}
