using Neomotive.Vin.Core;
using Neomotive.Vin.Data;
using Neomotive.Vin.Extensions;
using Neomotive.Vin.Models;
using Neomotive.Vin.Tests.Fakes;
using Xunit;

namespace Neomotive.Vin.Tests;

/// <summary>
/// The offline path for Make and Model. Before this, a 2021 Explorer decoded to nothing useful:
/// its WMI was missing from the catalog, and Model was only ever filled in by the NHTSA fallback —
/// a network the tool does not have when it is plugged into a car.
/// </summary>
public sealed class VdsPatternTests : IDisposable
{
    private const string ExplorerStVin = "1FM5K8GC1MGA00001";

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "neomotive-vdspatterns-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a leaked temp directory must not fail a test run */ }
    }

    private static readonly ManufacturerInfo FordSuvWmi = new()
    {
        Wmi = "1FM",
        Manufacturer = "FORD MOTOR COMPANY",
        Country = "UNITED STATES (USA)",
        VehicleType = "Multipurpose Passenger Vehicle (MPV)",
        Makes = ["Ford"]
    };

    private VinDecoder BuildDecoder(FakeNhtsaClient? nhtsa = null, bool nhtsaEnabled = true)
    {
        var options = new VinOptions
        {
            EnableNhtsaFallback = nhtsaEnabled,
            PatternStorePath = _dir
        };

        return new VinDecoder(
            new VinValidator(),
            new FakeManufacturerProvider([FordSuvWmi]),
            nhtsa ?? new FakeNhtsaClient(),
            options,
            new VdsPatternProvider(options));
    }

    [Fact]
    public void ExplorerWmiResolvesMakeAndCountryOffline()
    {
        // The catalog regeneration is what makes this pass: 1FM used to be absent entirely, so
        // Make and Country came back null and the Vehicle page showed only the VIN and the year.
        var result = BuildDecoder(nhtsaEnabled: false).DecodeLocal(ExplorerStVin);

        Assert.True(result.Validation.IsValid);
        Assert.Equal("Ford", result.Make);
        Assert.Equal("UNITED STATES (USA)", result.Country);
        Assert.Equal(2021, result.Year);
    }

    [Fact]
    public void AnUnlearnedVinHasNoModelOffline()
    {
        var result = BuildDecoder(nhtsaEnabled: false).DecodeLocal(ExplorerStVin);
        Assert.Null(result.Model);
    }

    [Fact]
    public async Task AnOnlineDecodeTeachesTheNextVehicleOfTheSameType()
    {
        var nhtsa = new FakeNhtsaClient(new NhtsaDecodeResponse
        {
            Make = "Ford",
            Model = "Explorer",
            Trim = "ST",
            ModelYear = 2021
        });

        // First vehicle: network available, so NHTSA names the model and the pattern is recorded.
        var online = await BuildDecoder(nhtsa).DecodeAsync(ExplorerStVin);
        Assert.Equal("Explorer", online.Model);
        Assert.Equal(1, nhtsa.CallCount);

        // Second vehicle of the same type, no network at all. Different sequence number, same
        // WMI + VDS + year code, which is the whole basis of the class key.
        const string secondVin = "1FM5K8GC3MGA99999";
        var offline = BuildDecoder(nhtsaEnabled: false).DecodeLocal(secondVin);

        Assert.Equal("Explorer", offline.Model);
        Assert.Equal("ST", offline.Trim);
        Assert.Equal("Ford", offline.Make);
    }

    [Fact]
    public async Task ALearnedPatternStopsTheNetworkBeingCalledAgain()
    {
        var nhtsa = new FakeNhtsaClient(new NhtsaDecodeResponse
        {
            Make = "Ford",
            Model = "Explorer",
            Trim = "ST",
            ModelYear = 2021
        });

        await BuildDecoder(nhtsa).DecodeAsync(ExplorerStVin);

        var second = new FakeNhtsaClient(new NhtsaDecodeResponse { Model = "Should not be asked" });
        var result = await BuildDecoder(second).DecodeAsync("1FM5K8GC3MGA99999");

        Assert.Equal("Explorer", result.Model);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public void ADifferentSeriesDoesNotInheritTheLearnedModel()
    {
        var provider = new VdsPatternProvider(new VinOptions { PatternStorePath = _dir });
        provider.Learn("1FM", "5K8GC", 2021, "Explorer", "ST");

        // A base Explorer has a different VDS, so it must not pick up the ST's answer.
        Assert.Null(provider.Match("1FM", "SK7DH", 2021));
        Assert.Equal("Explorer", provider.Match("1FM", "5K8GC", 2021)?.Model);
    }

    [Fact]
    public void AWildcardPatternMatchesButLosesToAnExactOne()
    {
        var provider = new VdsPatternProvider(new VinOptions { PatternStorePath = _dir });
        provider.Learn("1FM", "5K8GC", 2021, "Explorer", "ST");

        var match = provider.Match("1FM", "5K8GC", 2021);
        Assert.Equal("ST", match?.Trim);

        // A hand-written broad rule still answers for a VDS the exact entry does not cover.
        var broad = new VdsPattern { Wmi = "1FM", Vds = "5K8??", Model = "Explorer" };
        Assert.True(broad.Matches("1FM", "5K8ZZ", 2021));
        Assert.False(broad.Matches("1FM", "SK7DH", 2021));
    }

    [Fact]
    public void LearnedPatternsSurviveAReload()
    {
        var options = new VinOptions { PatternStorePath = _dir };
        new VdsPatternProvider(options).Learn("1FM", "5K8GC", 2021, "Explorer", "ST");

        // A fresh provider over the same directory reads what the last one wrote.
        var reloaded = new VdsPatternProvider(options);
        Assert.Equal("Explorer", reloaded.Match("1FM", "5K8GC", 2021)?.Model);
    }

    [Fact]
    public void ACorruptPatternFileDoesNotThrow()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "vds-patterns.json"), "{ not json at all");

        var provider = new VdsPatternProvider(new VinOptions { PatternStorePath = _dir });

        // Resolving less is the correct failure; refusing to decode is not.
        Assert.Null(provider.Match("1FM", "5K8GC", 2021));
        Assert.NotNull(provider.LastError);
    }

    [Fact]
    public void WithNowhereToWriteLearningIsANoOp()
    {
        var provider = new VdsPatternProvider(new VinOptions());
        provider.Learn("1FM", "5K8GC", 2021, "Explorer", "ST");

        Assert.Null(provider.Match("1FM", "5K8GC", 2021));
    }
}
