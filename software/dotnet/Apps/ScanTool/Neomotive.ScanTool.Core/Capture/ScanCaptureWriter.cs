using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Writes vehicle scan captures to disk in JSON, human-readable text, and CSV formats.
/// </summary>
public static class ScanCaptureWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Files produced by writing a vehicle scan capture.</summary>
    public sealed record WrittenScanFiles(string JsonPath, string TextPath, string CsvPath);

    /// <summary>
    /// Writes the scan capture to <paramref name="directory"/>.
    /// If <paramref name="usbRoot"/> is non-null, copies the files to NEOMOTIVE/captures/ on the USB drive.
    /// </summary>
    public static WrittenScanFiles Write(ScanCapture capture, string directory, string? usbRoot = null)
    {
        Directory.CreateDirectory(directory);

        var stamp = capture.CapturedUtc.ToString("yyyyMMdd-HHmmss");
        var stem = Path.Combine(directory, $"scan-{stamp}");

        var jsonPath = stem + ".json";
        var txtPath = stem + ".txt";
        var csvPath = stem + ".csv";

        // 1. JSON output
        var jsonText = JsonSerializer.Serialize(capture, JsonOptions);
        File.WriteAllText(jsonPath, jsonText);

        // 2. Human-readable text report
        var report = GenerateTextReport(capture);
        File.WriteAllText(txtPath, report);

        // 3. CSV output
        var csvText = GenerateCsv(capture);
        File.WriteAllText(csvPath, csvText);

        // Optional USB copy
        if (!string.IsNullOrWhiteSpace(usbRoot) && Directory.Exists(usbRoot))
        {
            try
            {
                var targetDir = Path.Combine(usbRoot, "NEOMOTIVE", "captures");
                Directory.CreateDirectory(targetDir);

                File.Copy(jsonPath, Path.Combine(targetDir, Path.GetFileName(jsonPath)), overwrite: true);
                File.Copy(txtPath, Path.Combine(targetDir, Path.GetFileName(txtPath)), overwrite: true);
                File.Copy(csvPath, Path.Combine(targetDir, Path.GetFileName(csvPath)), overwrite: true);
            }
            catch
            {
                // Best-effort USB export; failure must not abort local capture.
            }
        }

        return new WrittenScanFiles(jsonPath, txtPath, csvPath);
    }

    /// <summary>
    /// Copies all related scan files (.json, .txt, .csv) for the given JSON file to <paramref name="destinationDir"/>.
    /// </summary>
    public static bool ExportToUsb(string jsonPath, string destinationDir)
    {
        try
        {
            Directory.CreateDirectory(destinationDir);
            var stem = Path.Combine(
                Path.GetDirectoryName(jsonPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(jsonPath));

            var copied = 0;
            foreach (var ext in new[] { ".json", ".txt", ".csv" })
            {
                var src = stem + ext;
                if (File.Exists(src))
                {
                    File.Copy(src, Path.Combine(destinationDir, Path.GetFileName(src)), overwrite: true);
                    copied++;
                }
            }

            return copied > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Generates a formatted human-readable diagnostic report.</summary>
    public static string GenerateTextReport(ScanCapture capture)
    {
        var sb = new StringBuilder();
        var localTime = capture.CapturedUtc.ToLocalTime();

        sb.AppendLine("================================================================================");
        sb.AppendLine("NEOMOTIVE VEHICLE SCAN REPORT");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Captured:     {capture.CapturedUtc:yyyy-MM-dd HH:mm:ss} UTC (Local: {localTime:yyyy-MM-dd HH:mm:ss})");
        sb.AppendLine($"VIN:          {capture.Vin ?? "—"}");

        var vehicleParts = new[] { capture.Year?.ToString(), capture.Make, capture.Model }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var vehicleStr = string.Join(" ", vehicleParts);
        sb.AppendLine($"Vehicle:      {(string.IsNullOrWhiteSpace(vehicleStr) ? "—" : vehicleStr)}");

        if (!string.IsNullOrWhiteSpace(capture.Protocol))
            sb.AppendLine($"Protocol:     {capture.Protocol}");
        if (!string.IsNullOrWhiteSpace(capture.EcuName))
            sb.AppendLine($"ECU Name:     {capture.EcuName}");
        if (!string.IsNullOrWhiteSpace(capture.CalibrationId))
            sb.AppendLine($"Calibration:  {capture.CalibrationId}" + (!string.IsNullOrWhiteSpace(capture.Cvn) ? $" (CVN: {capture.Cvn})" : ""));

        sb.AppendLine($"MIL Status:   {(capture.MilOn ? "ON (Check Engine)" : "OFF")}");
        sb.AppendLine($"Total Faults: {capture.TotalFaultCount} codes across {capture.Modules.Count(m => m.CodeCount > 0)} modules");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        if (capture.Modules.Count == 0 || capture.TotalFaultCount == 0)
        {
            sb.AppendLine("No diagnostic trouble codes detected.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("DIAGNOSTIC TROUBLE CODES BY MODULE:");
            sb.AppendLine();

            foreach (var module in capture.Modules)
            {
                sb.AppendLine($"MODULE: {module.Name} [{module.Address} · {module.Protocol}] — {module.CodeCount} DTCs");

                if (module.Codes.Count == 0)
                {
                    sb.AppendLine("  (No codes reported)");
                }
                else
                {
                    foreach (var code in module.Codes)
                    {
                        var statusTag = string.IsNullOrWhiteSpace(code.Status) ? "" : $" [{code.Status}]";
                        sb.AppendLine($"  • {code.Code} — {code.Description}{statusTag}");
                        if (!string.IsNullOrWhiteSpace(code.Detail))
                        {
                            sb.AppendLine($"    Detail: {code.Detail}");
                        }
                    }
                }
                sb.AppendLine();
            }
        }

        if (capture.ReadinessMonitors.Count > 0)
        {
            sb.AppendLine("================================================================================");
            var readyCount = capture.ReadinessMonitors.Count(m => string.Equals(m.Status, "Ready", StringComparison.OrdinalIgnoreCase) || string.Equals(m.Status, "Complete", StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"I/M READINESS MONITORS ({readyCount} of {capture.ReadinessMonitors.Count} ready/complete)");
            sb.AppendLine("================================================================================");

            foreach (var monitor in capture.ReadinessMonitors)
            {
                var icon = string.Equals(monitor.Status, "Ready", StringComparison.OrdinalIgnoreCase) || string.Equals(monitor.Status, "Complete", StringComparison.OrdinalIgnoreCase)
                    ? "[✓]"
                    : string.Equals(monitor.Status, "Not Supported", StringComparison.OrdinalIgnoreCase)
                        ? "[-]"
                        : "[✗]";

                sb.AppendLine($"  {icon} {monitor.Name}: {monitor.Status}");
            }
            sb.AppendLine("================================================================================");
        }

        return sb.ToString();
    }

    /// <summary>Generates a CSV of all codes in the scan capture.</summary>
    public static string GenerateCsv(ScanCapture capture)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,VIN,Vehicle,Module,Address,Protocol,Code,Description,Status,MIL");

        var ts = capture.CapturedUtc.ToString("o");
        var vin = EscapeCsv(capture.Vin ?? "");
        var vehicleParts = new[] { capture.Year?.ToString(), capture.Make, capture.Model }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var vehicle = EscapeCsv(string.Join(" ", vehicleParts));
        var mil = capture.MilOn ? "Yes" : "No";

        if (capture.TotalFaultCount == 0)
        {
            // Record a single row indicating clean scan
            sb.AppendLine($"{ts},{vin},{vehicle},None,-,-,NONE,\"No codes detected\",Clear,{mil}");
        }
        else
        {
            foreach (var module in capture.Modules)
            {
                var modName = EscapeCsv(module.Name);
                var addr = EscapeCsv(module.Address);
                var proto = EscapeCsv(module.Protocol);

                foreach (var code in module.Codes)
                {
                    var c = EscapeCsv(code.Code);
                    var desc = EscapeCsv(code.Description);
                    var status = EscapeCsv(code.Status);

                    sb.AppendLine($"{ts},{vin},{vehicle},{modName},{addr},{proto},{c},{desc},{status},{mil}");
                }
            }
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
