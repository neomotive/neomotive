using System.Globalization;
using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class CaptureStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "scantool-capture-tests", Guid.NewGuid().ToString("N"));

    private static readonly IReadOnlyList<CaptureSignal> Signals = new[]
    {
        new CaptureSignal(0, "rpm",  "Engine RPM",    "RPM", 0, 8000),
        new CaptureSignal(1, "rail", "Rail Pressure", "kPa", 0, 200000),
    };

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string WriteCapture(CaptureMetadata? metadata = null, bool complete = true)
    {
        using var writer = new CaptureWriter(_dir, "capture-20260101-120000", Signals);

        writer.Write(new CaptureSample(0, 0, 180));
        writer.Write(new CaptureSample(1, 40, 12500.5));
        writer.Write(new CaptureSample(0, 100, 220));
        writer.Write(new CaptureSample(1, 140, 31000.25));

        if (complete)
        {
            writer.Complete(metadata ?? new CaptureMetadata
            {
                StartedUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                Vin = "AWWWWWWWWWWW0YEAH",
                CalibrationId = "CAL-STOCK-001",
                Cvn = "A1B2C3D4",
                TriggerDescription = "rpm above 150",
                TriggerTimestampMs = 100,
                AchievedSampleRateHz = 12.5,
                DurationMs = 140,
                Events = new[] { new CaptureEvent(CaptureEventKind.Triggered, 100) },
            });
        }

        return writer.CsvPath;
    }

    [Fact]
    public void Round_trips_samples_and_metadata()
    {
        var csvPath = WriteCapture();
        var recording = CaptureReader.Load(csvPath);

        Assert.Equal(4, recording.Samples.Count);
        Assert.Equal("AWWWWWWWWWWW0YEAH", recording.Metadata.Vin);
        Assert.Equal("CAL-STOCK-001", recording.Metadata.CalibrationId);
        Assert.Equal(12.5, recording.Metadata.AchievedSampleRateHz);
        Assert.Equal(4, recording.Metadata.SampleCount);
        Assert.Equal(CaptureEventKind.Triggered, Assert.Single(recording.Metadata.Events).Kind);
    }

    [Fact]
    public void Preserves_the_distinct_timestamp_of_every_sample()
    {
        var recording = CaptureReader.Load(WriteCapture());

        // The whole point of long format: rail pressure keeps its own 40 ms offset from RPM
        // rather than being snapped onto a shared row.
        Assert.Equal(new long[] { 0, 40, 100, 140 }, recording.Samples.Select(s => s.TimestampMs));
    }

    [Fact]
    public void Fractional_values_survive_the_round_trip()
    {
        var recording = CaptureReader.Load(WriteCapture());
        var rail = recording.SamplesFor("rail");

        Assert.Equal(12500.5, rail[0].Value, precision: 6);
        Assert.Equal(31000.25, rail[1].Value, precision: 6);
    }

    [Fact]
    public void Csv_is_written_in_invariant_culture()
    {
        var csvPath = WriteCapture();
        var lines = File.ReadAllLines(csvPath);

        Assert.Equal(CaptureFile.CsvHeader, lines[0]);

        // A decimal comma here would split the row into a fourth field and corrupt every parse.
        Assert.Equal("40,rail,12500.5", lines[2]);
        Assert.All(lines.Skip(1), l => Assert.Equal(3, l.Split(',').Length));
    }

    [Fact]
    public void Samples_for_a_signal_are_filtered_by_key()
    {
        var recording = CaptureReader.Load(WriteCapture());

        Assert.Equal(2, recording.SamplesFor("rpm").Count);
        Assert.Equal(2, recording.SamplesFor("RAIL").Count);   // case-insensitive
        Assert.Empty(recording.SamplesFor("missing"));
    }

    [Fact]
    public void Timestamps_can_be_rebased_onto_the_trigger()
    {
        var recording = CaptureReader.Load(WriteCapture());

        // Trigger fired at 100 ms, so pre-trigger history goes negative.
        Assert.Equal(-100, recording.ToTriggerRelative(0));
        Assert.Equal(40, recording.ToTriggerRelative(140));
    }

    [Fact]
    public void A_capture_with_no_sidecar_still_loads()
    {
        // Simulates a capture cut short before Complete() ran — a flat battery mid-crank.
        var csvPath = WriteCapture(complete: false);
        Assert.False(File.Exists(Path.ChangeExtension(csvPath, ".json")));

        var recording = CaptureReader.Load(csvPath);

        Assert.Equal(4, recording.Samples.Count);
        Assert.Equal(2, recording.Metadata.Signals.Count);
        Assert.Equal(2, recording.SamplesFor("rail").Count);
    }

    [Fact]
    public void Can_be_loaded_from_either_file_path()
    {
        var csvPath = WriteCapture();
        var jsonPath = Path.ChangeExtension(csvPath, ".json");

        Assert.Equal(4, CaptureReader.Load(jsonPath).Samples.Count);
    }

    [Fact]
    public void Listing_finds_written_captures()
    {
        WriteCapture();

        Assert.Single(CaptureReader.ListCaptures(_dir));
        Assert.Empty(CaptureReader.ListCaptures(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void Wide_export_forward_fills_and_leaves_leading_gaps_blank()
    {
        var recording = CaptureReader.Load(WriteCapture());
        var widePath = Path.Combine(_dir, "wide.csv");

        CaptureExporter.WriteWideCsv(recording, widePath);
        var lines = File.ReadAllLines(widePath);

        Assert.Equal("timestamp_ms,rpm,rail", lines[0]);
        Assert.Equal("0,180,", lines[1]);            // rail not yet sampled
        Assert.Equal("40,180,12500.5", lines[2]);    // rpm carried forward
        Assert.Equal("140,220,31000.25", lines[4]);
    }

    [Fact]
    public void Stem_is_sortable_and_sanitises_names()
    {
        var started = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var expected = $"capture-{started.ToLocalTime():yyyyMMdd-HHmmss}";

        Assert.Equal(expected, CaptureFile.BuildStem(started));
        Assert.Equal($"{expected}-cold-start-3", CaptureFile.BuildStem(started, "cold start/3"));
    }
}
