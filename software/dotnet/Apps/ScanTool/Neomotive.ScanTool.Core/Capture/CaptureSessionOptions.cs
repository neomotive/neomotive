namespace Neomotive.ScanTool.Core.Capture;

/// <param name="PreTriggerSeconds">How much history to retain from before the trigger fires.</param>
/// <param name="ExpectedSampleRateHz">Per-signal rate, used only to size the pre-trigger ring.
/// Over-estimating costs a little memory; under-estimating silently shortens pre-trigger history,
/// so this is rounded up generously.</param>
/// <param name="MaxDurationSeconds">Hard stop after the trigger. Null means run until stopped.</param>
public record CaptureSessionOptions(
    IReadOnlyList<CaptureSignal> Signals,
    CaptureTrigger Trigger,
    double PreTriggerSeconds = 5,
    double ExpectedSampleRateHz = 20,
    double? MaxDurationSeconds = 120,
    ActivityStopCondition? ActivityStopCondition = null);
