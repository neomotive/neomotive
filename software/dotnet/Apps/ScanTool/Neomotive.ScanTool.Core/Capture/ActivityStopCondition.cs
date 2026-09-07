namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Ends a capture once the watched signal has been active and then goes quiet: the signal must
/// first rise above <see cref="Floor"/>, then stay below it continuously for
/// <see cref="DurationMs"/>.
/// </summary>
/// <remarks>
/// Deliberately generic. An engine catching and then dying is one use; so is road speed returning
/// to zero at the end of a road test, a pump cycling off, or current draw ending. The signal and
/// thresholds come from the diagnostic profile.
/// <para>
/// The "must rise first" rule matters. A capture triggered before the signal has done anything
/// starts below any sensible floor, so a naive below-threshold test would fire immediately and
/// truncate the recording before the interesting part.
/// </para>
/// </remarks>
public sealed class ActivityStopCondition
{
    private int _signalIndex = -1;
    private bool _hasRisen;
    private long _belowSinceMs = -1;

    public ActivityStopCondition(string signalKey, double floor, int durationMs)
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
            $"Stop condition references signal '{SignalKey}', which is not part of the capture.");
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
