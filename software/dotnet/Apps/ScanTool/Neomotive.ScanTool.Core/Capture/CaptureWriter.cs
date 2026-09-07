using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Streams a capture to disk as long-format CSV plus a JSON sidecar.
/// </summary>
/// <remarks>
/// Long format (one row per sample) rather than a column per signal is deliberate. OBD polling is
/// round-robin, so signals are never sampled at the same instant; a wide grid would force
/// forward-filling, which fabricates timing and destroys exactly the rise-rate measurements this
/// feature exists to make.
/// <para>
/// Rows are flushed as they arrive so that a capture interrupted by a dead battery or a yanked
/// connector still leaves usable data on disk.
/// </para>
/// </remarks>
public sealed class CaptureWriter : IDisposable
{
    private readonly StreamWriter _csv;
    private readonly IReadOnlyList<CaptureSignal> _signals;
    private int _sampleCount;
    private bool _disposed;

    public CaptureWriter(string directory, string stem, IReadOnlyList<CaptureSignal> signals)
    {
        Directory.CreateDirectory(directory);

        _signals = signals;
        CsvPath = Path.Combine(directory, stem + CaptureFile.CsvExtension);
        SidecarPath = Path.Combine(directory, stem + CaptureFile.SidecarExtension);

        _csv = new StreamWriter(CsvPath, append: false, Encoding.UTF8) { AutoFlush = true };
        _csv.WriteLine(CaptureFile.CsvHeader);
    }

    public string CsvPath { get; }

    public string SidecarPath { get; }

    public int SampleCount => _sampleCount;

    public void Write(CaptureSample sample)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = sample.SignalIndex >= 0 && sample.SignalIndex < _signals.Count
            ? _signals[sample.SignalIndex].Key
            : sample.SignalIndex.ToString(CultureInfo.InvariantCulture);

        _csv.Write(sample.TimestampMs.ToString(CultureInfo.InvariantCulture));
        _csv.Write(',');
        _csv.Write(key);
        _csv.Write(',');
        _csv.WriteLine(sample.Value.ToString(CaptureFile.ValueFormat, CultureInfo.InvariantCulture));

        _sampleCount++;
    }

    /// <summary>Writes the sidecar. Call once the capture has stopped.</summary>
    public void Complete(CaptureMetadata metadata)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var final = metadata with
        {
            Signals = _signals,
            SampleCount = _sampleCount,
        };

        File.WriteAllText(SidecarPath, JsonSerializer.Serialize(final, CaptureFile.JsonOptions));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _csv.Dispose();
    }
}
