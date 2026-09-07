namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Everything about a capture that the CSV has no room for. Written alongside it as a sidecar.
/// </summary>
/// <remarks>
/// The vehicle fingerprint fields (<see cref="CalibrationId"/>, <see cref="Cvn"/>) are recorded
/// with every capture on purpose: when comparing a good start against a bad one months apart, the
/// first question is whether the calibration changed in between.
/// </remarks>
public record CaptureMetadata
{
    public int SchemaVersion { get; init; } = 1;

    public string? Name { get; init; }

    public DateTime StartedUtc { get; init; }

    public string? Vin { get; init; }

    public string? CalibrationId { get; init; }

    public string? Cvn { get; init; }

    public string? EcuName { get; init; }

    public IReadOnlyList<CaptureSignal> Signals { get; init; } = Array.Empty<CaptureSignal>();

    /// <summary>Human-readable description of the arming/trigger configuration.</summary>
    public string? TriggerDescription { get; init; }

    /// <summary>Session-relative millisecond offset of the trigger; -1 if it never fired.</summary>
    public long TriggerTimestampMs { get; init; } = -1;

    /// <summary>
    /// Per-signal rate actually achieved, measured rather than assumed. Interpreting a rise rate
    /// requires knowing how fast the samples really arrived.
    /// </summary>
    public double AchievedSampleRateHz { get; init; }

    public long DurationMs { get; init; }

    public int SampleCount { get; init; }

    public IReadOnlyList<CaptureEvent> Events { get; init; } = Array.Empty<CaptureEvent>();
}
