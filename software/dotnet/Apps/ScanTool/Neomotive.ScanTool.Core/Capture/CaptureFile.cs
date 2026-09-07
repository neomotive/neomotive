using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>Shared naming and serialization conventions for capture files.</summary>
public static class CaptureFile
{
    public const string CsvExtension = ".csv";
    public const string SidecarExtension = ".json";

    /// <summary>Value format for the CSV. Invariant culture is mandatory: a decimal comma would
    /// silently corrupt every row.</summary>
    public const string ValueFormat = "G15";

    public const string CsvHeader = "timestamp_ms,signal,value";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Builds a sortable, collision-resistant file stem for a new capture.</summary>
    public static string BuildStem(DateTime startedUtc, string? name = null)
    {
        var stem = $"capture-{startedUtc.ToLocalTime():yyyyMMdd-HHmmss}";

        if (string.IsNullOrWhiteSpace(name))
        {
            return stem;
        }

        var safe = new string(name.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());

        return $"{stem}-{safe}";
    }
}
