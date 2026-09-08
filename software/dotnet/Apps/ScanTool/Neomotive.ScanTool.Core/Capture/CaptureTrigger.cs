namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Decides when buffering becomes recording. Triggers are bound to the session's signal list
/// once, at arm time, so evaluation on the sample path stays an index comparison.
/// </summary>
public abstract class CaptureTrigger
{
    /// <summary>Resolves signal keys to indices. Throws if a configured key is not being captured.</summary>
    public virtual void Bind(IReadOnlyList<CaptureSignal> signals) { }

    /// <summary>Returns true when this sample should fire the trigger.</summary>
    public abstract bool Evaluate(CaptureSample sample);

    public virtual void Reset() { }

    /// <summary>
    /// True when this trigger's condition is currently met, regardless of dwell or whether it has
    /// already fired. Used by <see cref="AllTrigger"/> to check simultaneous satisfaction across
    /// multiple signals.
    /// </summary>
    public virtual bool IsConditionMet => false;

    protected static int ResolveIndex(IReadOnlyList<CaptureSignal> signals, string key)
    {
        foreach (var signal in signals)
        {
            if (string.Equals(signal.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return signal.Index;
            }
        }

        throw new InvalidOperationException(
            $"Trigger references signal '{key}', which is not part of the capture.");
    }
}

public enum ThresholdComparison
{
    Above,
    Below,
}

/// <summary>
/// Fires when a signal crosses a threshold and stays there for <see cref="DwellMs"/>. The dwell
/// exists to reject the noise spike a starter motor puts on sensor readings at initial engagement.
/// </summary>
public sealed class ThresholdTrigger : CaptureTrigger
{
    private int _signalIndex = -1;
    private long _conditionSinceMs = -1;
    private bool _conditionMet;

    public ThresholdTrigger(string signalKey, ThresholdComparison comparison, double value, int dwellMs = 0)
    {
        SignalKey = signalKey;
        Comparison = comparison;
        Value = value;
        DwellMs = dwellMs;
    }

    public string SignalKey { get; }

    public ThresholdComparison Comparison { get; }

    public double Value { get; }

    public int DwellMs { get; }

    public override bool IsConditionMet => _conditionMet;

    public override void Bind(IReadOnlyList<CaptureSignal> signals)
        => _signalIndex = ResolveIndex(signals, SignalKey);

    public override bool Evaluate(CaptureSample sample)
    {
        if (sample.SignalIndex != _signalIndex)
        {
            return false;
        }

        var satisfied = Comparison == ThresholdComparison.Above
            ? sample.Value > Value
            : sample.Value < Value;

        _conditionMet = satisfied;

        if (!satisfied)
        {
            _conditionSinceMs = -1;
            return false;
        }

        if (_conditionSinceMs < 0)
        {
            _conditionSinceMs = sample.TimestampMs;
        }

        return sample.TimestampMs - _conditionSinceMs >= DwellMs;
    }

    public override void Reset()
    {
        _conditionSinceMs = -1;
        _conditionMet = false;
    }
}

/// <summary>Fires only when <see cref="Fire"/> is called, for the on-screen START button.</summary>
public sealed class ManualTrigger : CaptureTrigger
{
    private volatile bool _fired;

    public void Fire() => _fired = true;

    // Once fired, the condition stays met for the lifetime of the capture.
    public override bool IsConditionMet => _fired;

    public override bool Evaluate(CaptureSample sample) => _fired;

    public override void Reset() => _fired = false;
}

/// <summary>
/// Fires on the first sample of any signal. Combined with connect-retry arming this begins
/// recording the instant the ECU answers, which on a hard-start vehicle is the moment of key-on.
/// </summary>
public sealed class BusWakeTrigger : CaptureTrigger
{
    // Always satisfied: any sample means the bus is awake.
    public override bool IsConditionMet => true;

    public override bool Evaluate(CaptureSample sample) => true;
}

/// <summary>Fires when any of the supplied triggers fires.</summary>
public sealed class AnyTrigger : CaptureTrigger
{
    private readonly CaptureTrigger[] _triggers;

    public AnyTrigger(params CaptureTrigger[] triggers) => _triggers = triggers;

    public override void Bind(IReadOnlyList<CaptureSignal> signals)
    {
        foreach (var trigger in _triggers)
        {
            trigger.Bind(signals);
        }
    }

    public override bool Evaluate(CaptureSample sample)
    {
        // Evaluate all of them: a dwell-based trigger needs to see every sample to track state.
        var fired = false;

        foreach (var trigger in _triggers)
        {
            if (trigger.Evaluate(sample))
            {
                fired = true;
            }
        }

        return fired;
    }

    public override void Reset()
    {
        foreach (var trigger in _triggers)
        {
            trigger.Reset();
        }
    }
}

/// <summary>
/// Fires when ALL of the supplied triggers have their condition simultaneously satisfied.
/// Unlike <see cref="AnyTrigger"/> which is edge-triggered (fires on the sample that crosses the
/// threshold), AllTrigger fires on whichever sample brings the last condition into satisfaction —
/// any prior conditions that were already met stay "active" until their signal drops below the
/// threshold again.
/// </summary>
/// <remarks>
/// This is the right trigger for compound diesel diagnostics: "RPM above cranking speed AND rail
/// pressure below target" cannot be answered from a single sample because RPM and rail pressure
/// arrive on separate samples. Each sub-trigger maintains its own <see cref="CaptureTrigger.IsConditionMet"/>
/// state, and AllTrigger fires when all report true simultaneously.
/// </remarks>
public sealed class AllTrigger : CaptureTrigger
{
    private readonly CaptureTrigger[] _triggers;

    public AllTrigger(params CaptureTrigger[] triggers) => _triggers = triggers;

    public override void Bind(IReadOnlyList<CaptureSignal> signals)
    {
        foreach (var trigger in _triggers)
            trigger.Bind(signals);
    }

    public override bool Evaluate(CaptureSample sample)
    {
        // Evaluate ALL triggers on every sample — dwell-based triggers need to see every
        // sample for their signal to track state correctly. The return value here means
        // "this trigger fired on this sample", which for a ThresholdTrigger only happens
        // when the condition first becomes satisfied (after dwell).
        var anyFired = false;

        foreach (var trigger in _triggers)
        {
            if (trigger.Evaluate(sample))
                anyFired = true;
        }

        // Fire AllTrigger when at least one child fired on this sample and ALL are currently met.
        if (!anyFired)
            return false;

        foreach (var trigger in _triggers)
        {
            if (!trigger.IsConditionMet)
                return false;
        }

        return true;
    }

    public override void Reset()
    {
        foreach (var trigger in _triggers)
            trigger.Reset();
    }
}
