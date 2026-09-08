namespace Neomotive.ScanTool.Core.Capture;

public enum CaptureEventKind
{
    Armed,
    Triggered,
    BusLost,
    BusRestored,
    DtcSet,
    DtcCleared,
    StopConditionMet,
    DurationReached,
    Stopped,
}

/// <summary>
/// A marker on the capture timeline. Written to the JSON sidecar so the viewer can annotate the
/// trace — most importantly the trigger instant, which is time zero for interpretation.
/// The <see cref="Detail"/> field carries additional context: for <see cref="CaptureEventKind.DtcSet"/>
/// and <see cref="CaptureEventKind.DtcCleared"/> it is the DTC code string (e.g. "P0087").
/// </summary>
public record CaptureEvent(CaptureEventKind Kind, long TimestampMs, string? Detail = null);

