namespace Neomotive.ScanTool.Core.Capture;

public enum CaptureEventKind
{
    Armed,
    Triggered,
    BusLost,
    BusRestored,
    Stalled,
    DurationReached,
    Stopped,
}

/// <summary>
/// A marker on the capture timeline. Written to the JSON sidecar so the viewer can annotate the
/// trace — most importantly the trigger instant, which is time zero for interpretation.
/// </summary>
public record CaptureEvent(CaptureEventKind Kind, long TimestampMs, string? Detail = null);
