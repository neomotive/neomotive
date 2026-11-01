using System;
using System.IO;
using Neomotive.Obd2;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class DtcDatabaseTests
{
    [Theory]
    [InlineData("P0011", "Camshaft Position", "Over-Advanced")]
    [InlineData("P0012", "Camshaft Position", "Over-Retarded")]
    [InlineData("P0014", "Camshaft Position", "Over-Advanced")]
    [InlineData("P0015", "Camshaft Position", "Over-Retarded")]
    public void Standard_Camshaft_Timing_Codes_Resolve_Descriptions(string code, string part1, string part2)
    {
        var desc = DtcDescriptions.Lookup(code);
        Assert.NotEmpty(desc);
        Assert.Contains(part1, desc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(part2, desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uds_Code_With_Ftb_Subtype_Suffix_Resolves_Base_Description()
    {
        // When UDS reports P0011-00 or P0014-22, Lookup automatically resolves base code
        var descExact = DtcDescriptions.Lookup("P0011");
        var descWithFtb = DtcDescriptions.Lookup("P0011-00");
        var descWithFtb2 = DtcDescriptions.Lookup("P0011-12");

        Assert.NotEmpty(descExact);
        Assert.Equal(descExact, descWithFtb);
        Assert.Equal(descExact, descWithFtb2);
    }

    [Fact]
    public void Ford_Specific_Code_Resolves_When_Make_Is_Ford()
    {
        // P1000 is generic "Manufacturer Controlled DTC", but on Ford it means readiness incomplete
        var genericDesc = DtcDescriptions.Lookup("P1000", null);
        var fordDesc = DtcDescriptions.Lookup("P1000", "Ford");
        var lincolnDesc = DtcDescriptions.Lookup("P1000", "Lincoln");

        Assert.NotEmpty(genericDesc);
        Assert.Contains("Readiness", fordDesc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Readiness", lincolnDesc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DtcDescriptions_Supports_External_Overlay_Directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dtc-overlay-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var customCsv = Path.Combine(tempDir, "dtc-descriptions.custom.csv");
            File.WriteAllText(customCsv, "P9999,\"Custom Test Code Description\",\nP9998,\"Custom Ford Code\",FORD\n");

            DtcDescriptions.OverlayDirectory = tempDir;
            DtcDescriptions.Reload();

            var customGeneric = DtcDescriptions.Lookup("P9999");
            var customFord = DtcDescriptions.Lookup("P9998", "Ford");

            Assert.Equal("Custom Test Code Description", customGeneric);
            Assert.Equal("Custom Ford Code", customFord);
        }
        finally
        {
            DtcDescriptions.OverlayDirectory = null;
            DtcDescriptions.Reload();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
