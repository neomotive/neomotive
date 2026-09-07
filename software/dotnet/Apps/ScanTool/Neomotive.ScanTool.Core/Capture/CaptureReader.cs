using System.Globalization;
using System.Text.Json;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>Loads a capture written by <see cref="CaptureWriter"/>.</summary>
public static class CaptureReader
{
    /// <summary>
    /// Reads a capture given the path to either of its two files.
    /// </summary>
    /// <remarks>
    /// A missing or unreadable sidecar is tolerated: signals are reconstructed from the keys in the
    /// CSV instead. A capture cut short by a flat battery is exactly when the data matters most,
    /// and that is precisely when the sidecar will not have been written.
    /// </remarks>
    public static CaptureRecording Load(string path)
    {
        var stem = Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            Path.GetFileNameWithoutExtension(path));

        var csvPath = stem + CaptureFile.CsvExtension;
        var sidecarPath = stem + CaptureFile.SidecarExtension;

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"Capture CSV not found: {csvPath}", csvPath);
        }

        var metadata = TryLoadSidecar(sidecarPath);
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var signals = new List<CaptureSignal>();

        if (metadata is not null)
        {
            foreach (var signal in metadata.Signals)
            {
                indexByKey[signal.Key] = signal.Index;
                signals.Add(signal);
            }
        }

        var samples = new List<CaptureSample>();

        foreach (var line in File.ReadLines(csvPath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');

            if (parts.Length < 3
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var key = parts[1];

            if (!indexByKey.TryGetValue(key, out var index))
            {
                index = signals.Count;
                indexByKey[key] = index;
                signals.Add(new CaptureSignal(index, key, key, string.Empty, 0, 0));
            }

            samples.Add(new CaptureSample(index, ts, value));
        }

        metadata ??= new CaptureMetadata
        {
            StartedUtc = File.GetCreationTimeUtc(csvPath),
            DurationMs = samples.Count > 0 ? samples[^1].TimestampMs : 0,
        };

        return new CaptureRecording(
            metadata with { Signals = signals, SampleCount = samples.Count },
            samples);
    }

    /// <summary>Lists captures in a directory, newest first.</summary>
    public static IReadOnlyList<string> ListCaptures(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(directory, "capture-*" + CaptureFile.CsvExtension)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    private static CaptureMetadata? TryLoadSidecar(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureMetadata>(
                File.ReadAllText(sidecarPath), CaptureFile.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
