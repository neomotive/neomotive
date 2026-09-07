namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Detects the engine catching and then dying: the watched signal must first rise above the floor
/// (proving it ran at all), then stay below it continuously for <see cref="DurationMs"/>.
/// </summary>
/// <remarks>
/// The "must rise first" rule matters. A capture triggered while cranking starts with RPM below
/// any sensible floor, so a naive below-threshold test would declare a stall immediately and
/// truncate the recording before the interesting part.
/// </remarks>
public sealed class StallDetector
{
    private int _signalIndex = -1;
    private bool _hasRisen;
    private long _belowSinceMs = -1;

    public StallDetector(string signalKey, double floor, int durationMs)
    {
        SignalKey = signalKey;
        Floor = floor;
        DurationMs = durationMs;
    }

    public string SignalKey { get; }

    public double Floor { get; }

    public int DurationMs { get; }

    public void Bind(IReadOnlyList<CaptureSignal> signals)
    {
        foreach (var signal in signals)
        {
            if (string.Equals(signal.Key, SignalKey, StringComparison.OrdinalIgnoreCase))
            {
                _signalIndex = signal.Index;
                return;
            }
        }

        throw new InvalidOperationException(
            $"Stall detector references signal '{SignalKey}', which is not part of the capture.");
    }

    public bool Evaluate(CaptureSample sample)
    {
        if (sample.SignalIndex != _signalIndex)
        {
            return false;
        }

        if (sample.Value > Floor)
        {
            _hasRisen = true;
            _belowSinceMs = -1;
            return false;
        }

        if (!_hasRisen)
        {
            return false;
        }

        if (_belowSinceMs < 0)
        {
            _belowSinceMs = sample.TimestampMs;
        }

        return sample.TimestampMs - _belowSinceMs >= DurationMs;
    }

    public void Reset()
    {
        _hasRisen = false;
        _belowSinceMs = -1;
    }
}
