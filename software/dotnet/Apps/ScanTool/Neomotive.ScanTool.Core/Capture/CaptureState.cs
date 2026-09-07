namespace Neomotive.ScanTool.Core.Capture;

public enum CaptureState
{
    /// <summary>Not armed; samples are discarded.</summary>
    Idle,

    /// <summary>Armed and filling the pre-trigger rolling buffer, waiting on the trigger.</summary>
    Buffering,

    /// <summary>Triggered; samples are being recorded.</summary>
    Recording,

    /// <summary>Capture complete.</summary>
    Stopped,
}
