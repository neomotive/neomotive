namespace Neomotive.ScanTool.Core.Capture;

/// <summary>A capture loaded back from disk: sidecar metadata plus every sample.</summary>
public record CaptureRecording(CaptureMetadata Metadata, IReadOnlyList<CaptureSample> Samples)
{
    /// <summary>Samples for one signal in chronological order, ready to plot as a trace.</summary>
    public IReadOnlyList<CaptureSample> SamplesFor(string signalKey)
    {
        var signal = Metadata.Signals.FirstOrDefault(
            s => string.Equals(s.Key, signalKey, StringComparison.OrdinalIgnoreCase));

        if (signal is null)
        {
            return Array.Empty<CaptureSample>();
        }

        return Samples.Where(s => s.SignalIndex == signal.Index).ToArray();
    }

    /// <summary>
    /// Timestamps relative to the trigger, so multiple captures of the same event can be overlaid
    /// on a common x-axis. Falls back to session-relative time when there was no trigger.
    /// </summary>
    public long ToTriggerRelative(long timestampMs)
        => Metadata.TriggerTimestampMs < 0 ? timestampMs : timestampMs - Metadata.TriggerTimestampMs;
}
