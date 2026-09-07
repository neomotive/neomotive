using System.Globalization;
using System.Text;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Derived exports of a capture. These are conveniences for external tools, never the source of
/// truth — the long-format CSV written at capture time is.
/// </summary>
public static class CaptureExporter
{
    /// <summary>
    /// Writes a wide CSV: one row per distinct timestamp, one column per signal, with the last
    /// known value carried forward into gaps.
    /// </summary>
    /// <remarks>
    /// Forward-filling is lossy and slightly dishonest — it shows a value at an instant it was not
    /// actually measured. It is offered because spreadsheets need a rectangle, but rise-rate work
    /// should use the long-format file.
    /// </remarks>
    public static void WriteWideCsv(CaptureRecording recording, string path)
    {
        var signals = recording.Metadata.Signals.OrderBy(s => s.Index).ToArray();
        var latest = new double?[signals.Length];

        var builder = new StringBuilder();
        builder.Append("timestamp_ms");

        foreach (var signal in signals)
        {
            builder.Append(',').Append(signal.Key);
        }

        builder.AppendLine();

        foreach (var group in recording.Samples.GroupBy(s => s.TimestampMs).OrderBy(g => g.Key))
        {
            foreach (var sample in group)
            {
                if (sample.SignalIndex >= 0 && sample.SignalIndex < latest.Length)
                {
                    latest[sample.SignalIndex] = sample.Value;
                }
            }

            builder.Append(group.Key.ToString(CultureInfo.InvariantCulture));

            foreach (var value in latest)
            {
                builder.Append(',');

                if (value.HasValue)
                {
                    builder.Append(value.Value.ToString(CaptureFile.ValueFormat, CultureInfo.InvariantCulture));
                }
            }

            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }
}
