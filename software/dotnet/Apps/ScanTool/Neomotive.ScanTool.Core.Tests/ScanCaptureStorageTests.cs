using System;
using System.IO;
using System.Linq;
using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class ScanCaptureStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "scantool-scancapture-tests", Guid.NewGuid().ToString("N"));

    public ScanCaptureStorageTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WriteScanCapture_ProducesJsonTextAndCsvFiles()
    {
        var capture = new ScanCapture
        {
            CapturedUtc = new DateTime(2026, 6, 15, 14, 30, 0, DateTimeKind.Utc),
            Vin = "1HGCR2F83HA000001",
            Make = "HONDA",
            Model = "ACCORD",
            Year = 2017,
            Country = "United States",
            Protocol = "ISO 15765-4 (CAN 11/500)",
            EcuName = "EngineControl",
            CalibrationId = "CALID12345",
            Cvn = "CVN9876",
            MilOn = true,
            TotalFaultCount = 2,
            Modules =
            [
                new ScanModuleRecord
                {
                    Name = "ECM",
                    Address = "0x7E0",
                    Protocol = "OBD-II + UDS",
                    CodeCount = 2,
                    Codes =
                    [
                        new ScanDtcRecord
                        {
                            Code = "P0300",
                            Description = "Random/Multiple Cylinder Misfire Detected",
                            Detail = "FTB: 00",
                            Status = "Active, Confirmed, MIL",
                            Protocol = "OBD-II + UDS"
                        },
                        new ScanDtcRecord
                        {
                            Code = "P0171",
                            Description = "System Too Lean (Bank 1)",
                            Detail = "",
                            Status = "Pending",
                            Protocol = "OBD-II"
                        }
                    ]
                },
                new ScanModuleRecord
                {
                    Name = "TCM",
                    Address = "0x7E1",
                    Protocol = "UDS",
                    CodeCount = 0,
                    Codes = []
                }
            ],
            ReadinessMonitors =
            [
                new ScanReadinessRecord { Name = "Misfire", Status = "Ready" },
                new ScanReadinessRecord { Name = "Evaporative System", Status = "Incomplete" },
                new ScanReadinessRecord { Name = "Heated Catalyst", Status = "Not Supported" }
            ]
        };

        var written = ScanCaptureWriter.Write(capture, _dir);

        Assert.True(File.Exists(written.JsonPath));
        Assert.True(File.Exists(written.TextPath));
        Assert.True(File.Exists(written.CsvPath));

        // Verify JSON roundtrip
        var loaded = CaptureReader.LoadScanCapture(written.JsonPath);
        Assert.NotNull(loaded);
        Assert.Equal("1HGCR2F83HA000001", loaded.Vin);
        Assert.Equal("HONDA", loaded.Make);
        Assert.Equal("ACCORD", loaded.Model);
        Assert.Equal(2017, loaded.Year);
        Assert.True(loaded.MilOn);
        Assert.Equal(2, loaded.TotalFaultCount);
        Assert.Equal(2, loaded.Modules.Count);
        Assert.Equal(3, loaded.ReadinessMonitors.Count);
        Assert.Equal("P0300", loaded.Modules[0].Codes[0].Code);
        Assert.Equal("P0171", loaded.Modules[0].Codes[1].Code);

        // Verify human-readable report text contains key sections
        var text = File.ReadAllText(written.TextPath);
        Assert.Contains("NEOMOTIVE VEHICLE SCAN REPORT", text);
        Assert.Contains("1HGCR2F83HA000001", text);
        Assert.Contains("2017 HONDA ACCORD", text);
        Assert.Contains("MIL Status:   ON", text);
        Assert.Contains("P0300", text);
        Assert.Contains("Random/Multiple Cylinder Misfire Detected", text);
        Assert.Contains("READINESS MONITORS", text);
        Assert.Contains("Evaporative System", text);

        // Verify CSV contains header and fault rows
        var csvLines = File.ReadAllLines(written.CsvPath);
        Assert.True(csvLines.Length >= 3);
        Assert.Contains("Module", csvLines[0]);
        Assert.Contains("Code", csvLines[0]);
        Assert.Contains("P0300", csvLines[1]);
        Assert.Contains("P0171", csvLines[2]);
    }

    [Fact]
    public void ExportToUsb_CopiesAllThreeFiles()
    {
        var capture = new ScanCapture
        {
            CapturedUtc = new DateTime(2026, 6, 15, 14, 30, 0, DateTimeKind.Utc),
            Vin = "1HGCR2F83HA000002",
            TotalFaultCount = 0
        };

        var written = ScanCaptureWriter.Write(capture, _dir);
        var usbTarget = Path.Combine(_dir, "fake-usb", "NEOMOTIVE", "scans");

        var success = ScanCaptureWriter.ExportToUsb(written.JsonPath, usbTarget);
        Assert.True(success);

        Assert.True(File.Exists(Path.Combine(usbTarget, Path.GetFileName(written.JsonPath))));
        Assert.True(File.Exists(Path.Combine(usbTarget, Path.GetFileName(written.TextPath))));
        Assert.True(File.Exists(Path.Combine(usbTarget, Path.GetFileName(written.CsvPath))));
    }

    [Fact]
    public void ListAllCaptures_FindsLiveScansAndScreenshots()
    {
        // Create a dummy live CSV, scan JSON, and screenshot PNG
        File.WriteAllText(Path.Combine(_dir, "capture-20260101-120000.csv"), "time,rpm\n0,800\n");
        File.WriteAllText(Path.Combine(_dir, "scan-20260101-120500.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "screenshot-20260101-121000.png"), "fake png bytes");

        var all = CaptureReader.ListAllCaptures(_dir);
        Assert.Equal(3, all.Count);
        Assert.Contains(all, p => p.EndsWith(".png"));
        Assert.Contains(all, p => p.EndsWith(".json"));
        Assert.Contains(all, p => p.EndsWith(".csv"));
    }
}
