using Neomotive.Uds;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Neomotive.Uds.Tests;

/// <summary>
/// The catalog is the part users extend without a rebuild, so these cover the file layering,
/// import/export and the failure modes of hand-edited JSON.
/// </summary>
public class UdsCatalogTests : IDisposable
{
    private readonly string _dir;

    public UdsCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "uds-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private void WriteOverlay(string name, string json) => File.WriteAllText(Path.Combine(_dir, name), json);

    [Fact]
    public void SeedCatalog_ResolvesTheStandardVocabulary()
    {
        var catalog = new UdsCatalog();

        Assert.Equal("Vehicle Identification Number (VIN)", catalog.GetDidName(0xF190));
        Assert.Equal("ECU Spare Part Number", catalog.GetDidName(0xF187));
        Assert.Equal("Circuit Open", catalog.GetFaultTypeDescription(0x13));
        Assert.Equal("Request Out Of Range", catalog.GetNrcDescription(0x31));
    }

    [Fact]
    public void UnknownEntries_DegradeToHexNames()
    {
        var catalog = new UdsCatalog();

        Assert.Equal("DID 0x1234", catalog.GetDidName(0x1234));
        Assert.Equal("Failure Type 0xEE", catalog.GetFaultTypeDescription(0xEE));
        Assert.Equal("Unknown NRC (0xAB)", catalog.GetNrcDescription(0xAB));
    }

    [Fact]
    public void SeedEncodings_FormatValuesTheWayTheStandardDidsExpect()
    {
        var catalog = new UdsCatalog();

        Assert.Equal("1HGCR2F83HA000000",
            catalog.FormatDidValue(0xF190, Encoding.ASCII.GetBytes("1HGCR2F83HA000000")));
        Assert.Equal("Extended Diagnostic Session (0x03)", catalog.FormatDidValue(0xF186, [0x03]));
        Assert.Equal("0A 0B", catalog.FormatDidValue(0xF180, [0x0A, 0x0B]));
    }

    [Fact]
    public void OverlayFile_AddsANewDidWithoutARebuild()
    {
        WriteOverlay("uds-catalog.mfg.json", """
        { "dids": [ { "did": "0x2A01", "name": "Turbo Boost Target", "encoding": "uInt",
                      "scale": 0.1, "units": "kPa", "source": "acme" } ] }
        """);

        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);

        Assert.Empty(catalog.LoadErrors);
        Assert.Equal("Turbo Boost Target", catalog.GetDidName(0x2A01));
        Assert.Equal("25.6 kPa", catalog.FormatDidValue(0x2A01, [0x01, 0x00]));
    }

    [Fact]
    public void OverlayFile_OverridesASeedName()
    {
        WriteOverlay("uds-catalog.json", """
        { "dids": [ { "did": "0xF190", "name": "Chassis Number", "encoding": "ascii" } ],
          "faultTypes": { "0x13": "Open Circuit (shop term)" },
          "nrcs": { "0x31": "No such thing here" } }
        """);

        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);

        Assert.Equal("Chassis Number", catalog.GetDidName(0xF190));
        Assert.Equal("Open Circuit (shop term)", catalog.GetFaultTypeDescription(0x13));
        Assert.Equal("No such thing here", catalog.GetNrcDescription(0x31));
    }

    [Fact]
    public void OverlayFiles_ApplyInFilenameOrder()
    {
        WriteOverlay("uds-catalog.a.json", """{ "dids": [ { "did": "0x3000", "name": "First" } ] }""");
        WriteOverlay("uds-catalog.b.json", """{ "dids": [ { "did": "0x3000", "name": "Second" } ] }""");

        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);

        Assert.Equal("Second", catalog.GetDidName(0x3000));
    }

    [Fact]
    public void RemoveFlag_DeletesAnInheritedEntry()
    {
        WriteOverlay("uds-catalog.json", """{ "dids": [ { "did": "0xF190", "remove": true } ] }""");

        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);

        Assert.Equal("DID 0xF190", catalog.GetDidName(0xF190));
    }

    [Fact]
    public void MalformedOverlay_IsReportedAndTheRestStillLoads()
    {
        WriteOverlay("uds-catalog.bad.json", "{ this is not json ");
        WriteOverlay("uds-catalog.good.json", """{ "dids": [ { "did": "0x4000", "name": "Still Here" } ] }""");

        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);

        Assert.Single(catalog.LoadErrors);
        Assert.Contains("uds-catalog.bad.json", catalog.LoadErrors[0]);
        Assert.Equal("Still Here", catalog.GetDidName(0x4000));
        Assert.Equal("Vehicle Identification Number (VIN)", catalog.GetDidName(0xF190));
    }

    [Fact]
    public void UpsertThenSave_PersistsAcrossAReload()
    {
        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);
        catalog.Upsert(new UdsDidDefinition { Did = "0x5000", Name = "Runtime Entry", Encoding = UdsDidEncoding.Ascii });
        catalog.Save();

        var reloaded = new UdsCatalog();
        reloaded.SetDataDir(_dir);

        Assert.Equal("Runtime Entry", reloaded.GetDidName(0x5000));
    }

    [Fact]
    public void Save_WritesOnlyTheOverrides()
    {
        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);
        catalog.Upsert(new UdsDidDefinition { Did = "0x5001", Name = "Only Me" });
        catalog.Save();

        var saved = JsonSerializer.Deserialize<UdsCatalogFile>(
            File.ReadAllText(Path.Combine(_dir, "uds-catalog.json")), UdsCatalog.FileJsonOptions);

        // The seed stays in the assembly, so a future seed update still reaches the user.
        Assert.NotNull(saved);
        Assert.Equal(["0x5001"], saved.Dids.Select(d => d.Did).ToArray());
    }

    [Fact]
    public void ExportAll_ContainsSeedAndCustomEntries()
    {
        var catalog = new UdsCatalog();
        catalog.Upsert(new UdsDidDefinition { Did = "0x5002", Name = "Custom" });

        var path = Path.Combine(_dir, "export.json");
        catalog.Export(path, UdsCatalogScope.All);

        var exported = JsonSerializer.Deserialize<UdsCatalogFile>(
            File.ReadAllText(path), UdsCatalog.FileJsonOptions)!;

        Assert.Contains(exported.Dids, d => d.Did == "0x5002");
        Assert.Contains(exported.Dids, d => d.Did == "0xF190");
        Assert.NotEmpty(exported.FaultTypes);
        Assert.NotEmpty(exported.Nrcs);
    }

    [Fact]
    public void ExportThenImport_RoundTrips()
    {
        var source = new UdsCatalog();
        source.Upsert(new UdsDidDefinition
        {
            Did = "0x5003",
            Name = "Round Trip",
            Encoding = UdsDidEncoding.Enum,
            Values = new() { ["0x01"] = "On", ["0x00"] = "Off" }
        });

        var path = Path.Combine(_dir, "round-trip.json");
        source.Export(path, UdsCatalogScope.Overrides);

        var target = new UdsCatalog();
        Assert.Equal(1, target.Import(path));
        Assert.Equal("Round Trip", target.GetDidName(0x5003));
        Assert.Equal("On", target.FormatDidValue(0x5003, [0x01]));
    }

    [Fact]
    public void ImportCsv_AddsNamesForQuickEdits()
    {
        var path = Path.Combine(_dir, "dids.csv");
        File.WriteAllText(path, "did,name\n0x6000,Oil Life Remaining\n0x6001,\"Cabin Filter Hours\"\n");

        var catalog = new UdsCatalog();
        Assert.Equal(2, catalog.Import(path));
        Assert.Equal("Oil Life Remaining", catalog.GetDidName(0x6000));
        Assert.Equal("Cabin Filter Hours", catalog.GetDidName(0x6001));
    }

    [Fact]
    public void ImportWithoutMerge_DropsPriorRuntimeEdits()
    {
        var catalog = new UdsCatalog();
        catalog.Upsert(new UdsDidDefinition { Did = "0x7000", Name = "Temporary" });

        var path = Path.Combine(_dir, "replacement.json");
        File.WriteAllText(path, """{ "dids": [ { "did": "0x7001", "name": "Replacement" } ] }""");

        catalog.Import(path, merge: false);

        Assert.Equal("DID 0x7000", catalog.GetDidName(0x7000));
        Assert.Equal("Replacement", catalog.GetDidName(0x7001));
        // Reset means back to the seed, not back to empty.
        Assert.Equal("Vehicle Identification Number (VIN)", catalog.GetDidName(0xF190));
    }

    [Fact]
    public void Reload_PicksUpAFileDroppedAfterStartup()
    {
        var catalog = new UdsCatalog();
        catalog.SetDataDir(_dir);
        Assert.Equal("DID 0x8000", catalog.GetDidName(0x8000));

        WriteOverlay("uds-catalog.late.json", """{ "dids": [ { "did": "0x8000", "name": "Arrived Late" } ] }""");
        catalog.Reload();

        Assert.Equal("Arrived Late", catalog.GetDidName(0x8000));
    }
}
