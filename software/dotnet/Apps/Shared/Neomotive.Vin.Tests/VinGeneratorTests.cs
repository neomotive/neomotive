using Neomotive.Vin.Core;
using Neomotive.Vin.Models;
using Xunit;

namespace Neomotive.Vin.Tests;

public sealed class VinGeneratorTests
{
    private static readonly MakeCatalogEntry CamaroEntry = new()
    {
        Make = "Chevrolet",
        Wmi = "1G1",
        Models =
        [
            new VehicleModelInfo
            {
                Name = "Camaro",
                YearStart = 1996,
                YearEnd = 9999,
                Packages = ["LS", "LT", "SS"],
                Plants = new Dictionary<string, string> { ["B"] = "Bowling Green, KY" }
            }
        ]
    };

    private static VinGenerator BuildGenerator(params MakeCatalogEntry[] entries) =>
        new(new InMemoryModelCatalogProvider(entries));

    [Fact]
    public async Task GenerateAsync_ProducesValidCheckDigit()
    {
        var gen = BuildGenerator(CamaroEntry);
        var vin = await gen.GenerateAsync(new GenerateVinRequest
        {
            Make = "Chevrolet", Model = "Camaro", Year = 2015
        });

        Assert.Equal(17, vin.Length);
        Assert.True(new VinValidator().HasValidCheckDigit(vin));
    }

    [Fact]
    public async Task GenerateAsync_WmiEmbeddedCorrectly()
    {
        var vin = await BuildGenerator(CamaroEntry).GenerateAsync(new GenerateVinRequest
        {
            Make = "Chevrolet", Model = "Camaro", Year = 2020
        });
        Assert.StartsWith("1G1", vin);
    }

    [Theory]
    [InlineData(1996, 'T')]
    [InlineData(2001, '1')]
    [InlineData(2010, 'A')]
    [InlineData(2015, 'F')]
    [InlineData(2024, 'R')]
    public async Task GenerateAsync_ModelYearEncodedCorrectly(int year, char expectedCode)
    {
        var vin = await BuildGenerator(CamaroEntry).GenerateAsync(new GenerateVinRequest
        {
            Make = "Chevrolet", Model = "Camaro", Year = year
        });
        Assert.Equal(expectedCode, vin[9]);
    }

    [Fact]
    public async Task GenerateAsync_FixedSequence_Embedded()
    {
        var vin = await BuildGenerator(CamaroEntry).GenerateAsync(new GenerateVinRequest
        {
            Make = "Chevrolet", Model = "Camaro", Year = 2018, SequenceNumber = "123456"
        });
        Assert.EndsWith("123456", vin);
    }

    [Fact]
    public async Task GenerateAsync_RandomSequence_IsDigitsOnly()
    {
        var vin = await BuildGenerator(CamaroEntry).GenerateAsync(new GenerateVinRequest
        {
            Make = "Chevrolet", Model = "Camaro", Year = 2018
        });
        Assert.True(vin[11..].All(char.IsAsciiDigit));
    }

    [Fact]
    public async Task GenerateAsync_RoundTrip_ValidatesAndDecodesYear()
    {
        var gen = BuildGenerator(CamaroEntry);
        var validator = new VinValidator();

        for (int year = 1996; year <= 2025; year++)
        {
            var vin = await gen.GenerateAsync(new GenerateVinRequest
            {
                Make = "Chevrolet", Model = "Camaro", Year = year
            });
            var result = validator.Validate(vin);
            Assert.True(result.IsValid, $"Year {year} produced invalid VIN {vin}: {string.Join("; ", result.Errors)}");
        }
    }

    [Fact]
    public async Task GenerateAsync_UnknownMake_ThrowsInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildGenerator(CamaroEntry).GenerateAsync(new GenerateVinRequest
            {
                Make = "Unknown", Model = "X", Year = 2020
            }));
    }

    [Fact]
    public async Task GetMakesAsync_ReturnsSeededMakes()
    {
        var makes = await BuildGenerator(CamaroEntry).GetMakesAsync();
        Assert.Contains("Chevrolet", makes);
    }

    [Fact]
    public async Task GetModelsAsync_ReturnsModels()
    {
        var models = await BuildGenerator(CamaroEntry).GetModelsAsync("Chevrolet");
        Assert.Contains("Camaro", models);
    }

    [Fact]
    public async Task GetYearsAsync_RespectsYearRange()
    {
        var years = await BuildGenerator(CamaroEntry).GetYearsAsync("Chevrolet", "Camaro");
        Assert.Contains(1996, years);
        Assert.Contains(2020, years);
        Assert.DoesNotContain(1995, years);
    }

    [Fact]
    public async Task GetPackagesAsync_ReturnsPackagesForYear()
    {
        var packages = await BuildGenerator(CamaroEntry).GetPackagesAsync("Chevrolet", "Camaro", 2015);
        Assert.Contains("SS", packages);
    }

    // In-memory catalog provider for tests
    private sealed class InMemoryModelCatalogProvider : Data.IModelCatalogProvider
    {
        private readonly IReadOnlyList<MakeCatalogEntry> _entries;
        public InMemoryModelCatalogProvider(MakeCatalogEntry[] entries) => _entries = entries;

        private MakeCatalogEntry? FindMake(string make) =>
            _entries.FirstOrDefault(e => e.Make.Equals(make, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<string> GetMakes() => [.. _entries.Select(e => e.Make)];

        public IReadOnlyList<string> GetModels(string make) =>
            FindMake(make)?.Models.Select(m => m.Name).ToList() ?? [];

        public IReadOnlyList<int> GetYears(string make, string model)
        {
            var m = FindMake(make)?.Models.FirstOrDefault(x => x.Name.Equals(model, StringComparison.OrdinalIgnoreCase));
            if (m is null) return [];
            int end = m.YearEnd == 9999 ? DateTime.UtcNow.Year : m.YearEnd;
            return [.. Enumerable.Range(m.YearStart, end - m.YearStart + 1)];
        }

        public IReadOnlyList<string> GetPackages(string make, string model, int year)
        {
            var m = FindMake(make)?.Models.FirstOrDefault(x =>
                x.Name.Equals(model, StringComparison.OrdinalIgnoreCase) &&
                x.YearStart <= year && year <= (x.YearEnd == 9999 ? int.MaxValue : x.YearEnd));
            return m?.Packages ?? [];
        }

        public VehicleModelInfo? GetModel(string make, string model) =>
            FindMake(make)?.Models.FirstOrDefault(x => x.Name.Equals(model, StringComparison.OrdinalIgnoreCase));

        public string? GetWmiForMake(string make) => FindMake(make)?.Wmi;
    }
}
