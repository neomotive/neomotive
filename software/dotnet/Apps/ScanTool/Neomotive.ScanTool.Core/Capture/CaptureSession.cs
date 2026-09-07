namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// The capture state machine: Idle -> Buffering -> Recording -> Stopped.
/// </summary>
/// <remarks>
/// Deliberately clock-free. Callers stamp each sample, which keeps the session fully testable
/// against synthetic sample streams and keeps the real timestamps tied to when the OBD response
/// actually arrived rather than when it was processed.
/// </remarks>
public sealed class CaptureSession
{
    private readonly CaptureSessionOptions _options;
    private readonly RollingBuffer<CaptureSample> _preTrigger;
    private readonly List<CaptureSample> _recorded = new();
    private readonly List<CaptureEvent> _events = new();
    private readonly object _sync = new();

    private long _triggerMs = -1;
    private bool _busLost;

    public CaptureSession(CaptureSessionOptions options)
    {
        _options = options;

        if (options.Signals.Count == 0)
        {
            throw new ArgumentException("A capture needs at least one signal.", nameof(options));
        }

        // Ring holds every signal's history for the pre-trigger window, hence the signal-count factor.
        var capacity = (int)Math.Ceiling(
            options.PreTriggerSeconds * options.ExpectedSampleRateHz * options.Signals.Count);

        _preTrigger = new RollingBuffer<CaptureSample>(Math.Max(capacity, options.Signals.Count));
    }

    public CaptureState State { get; private set; } = CaptureState.Idle;

    public IReadOnlyList<CaptureSignal> Signals => _options.Signals;

    /// <summary>Session-relative timestamp at which the trigger fired, or -1 if it has not.</summary>
    public long TriggerTimestampMs => _triggerMs;

    public DateTime? StartedUtc { get; private set; }

    public event Action<CaptureSample>? SampleRecorded;

    public event Action<CaptureEvent>? EventRecorded;

    public event Action<CaptureState>? StateChanged;

    public IReadOnlyList<CaptureSample> RecordedSamples
    {
        get { lock (_sync) { return _recorded.ToArray(); } }
    }

    public IReadOnlyList<CaptureEvent> Events
    {
        get { lock (_sync) { return _events.ToArray(); } }
    }

    public void Arm(long timestampMs = 0)
    {
        lock (_sync)
        {
            _options.Trigger.Bind(_options.Signals);
            _options.Trigger.Reset();
            _options.ActivityStopCondition?.Bind(_options.Signals);
            _options.ActivityStopCondition?.Reset();

            _preTrigger.Clear();
            _recorded.Clear();
            _events.Clear();
            _triggerMs = -1;
            _busLost = false;
            StartedUtc = DateTime.UtcNow;

            AddEvent(new CaptureEvent(CaptureEventKind.Armed, timestampMs));
            SetState(CaptureState.Buffering);
        }
    }

    /// <summary>Feeds one measurement into the session. Safe to call from the poll loop only.</summary>
    public void Ingest(CaptureSample sample)
    {
        lock (_sync)
        {
            switch (State)
            {
                case CaptureState.Buffering:
                    _preTrigger.Add(sample);

                    if (_options.Trigger.Evaluate(sample))
                    {
                        PromoteToRecording(sample.TimestampMs);
                    }

                    break;

                case CaptureState.Recording:
                    Record(sample);
                    EvaluateStopConditions(sample);
                    break;

                // Idle and Stopped discard.
            }
        }
    }

    /// <summary>
    /// Notes that the bus dropped. Cranking browns out the ECU on a struggling vehicle, so this is
    /// an expected event mid-capture and must not end the recording.
    /// </summary>
    public void MarkBusLost(long timestampMs)
    {
        lock (_sync)
        {
            if (_busLost || State is CaptureState.Idle or CaptureState.Stopped)
            {
                return;
            }

            _busLost = true;
            AddEvent(new CaptureEvent(CaptureEventKind.BusLost, timestampMs));
        }
    }

    public void MarkBusRestored(long timestampMs)
    {
        lock (_sync)
        {
            if (!_busLost)
            {
                return;
            }

            _busLost = false;
            AddEvent(new CaptureEvent(CaptureEventKind.BusRestored, timestampMs));
        }
    }

    public void Stop(long timestampMs, CaptureEventKind reason = CaptureEventKind.Stopped)
    {
        lock (_sync)
        {
            if (State == CaptureState.Stopped)
            {
                return;
            }

            AddEvent(new CaptureEvent(reason, timestampMs));
            SetState(CaptureState.Stopped);
        }
    }

    private void PromoteToRecording(long timestampMs)
    {
        _triggerMs = timestampMs;
        SetState(CaptureState.Recording);

        // Flush the pre-trigger history first so the recording reads chronologically. The trigger
        // sample itself is already in the ring, so it is not re-added below.
        foreach (var buffered in _preTrigger.ToArray())
        {
            Record(buffered);
        }

        _preTrigger.Clear();
        AddEvent(new CaptureEvent(CaptureEventKind.Triggered, timestampMs));
    }

    private void Record(CaptureSample sample)
    {
        _recorded.Add(sample);
        SampleRecorded?.Invoke(sample);
    }

    private void EvaluateStopConditions(CaptureSample sample)
    {
        if (_options.ActivityStopCondition?.Evaluate(sample) == true)
        {
            Stop(sample.TimestampMs, CaptureEventKind.StopConditionMet);
            return;
        }

        if (_options.MaxDurationSeconds is { } maxSeconds
            && sample.TimestampMs - _triggerMs >= maxSeconds * 1000)
        {
            Stop(sample.TimestampMs, CaptureEventKind.DurationReached);
        }
    }

    private void AddEvent(CaptureEvent captureEvent)
    {
        _events.Add(captureEvent);
        EventRecorded?.Invoke(captureEvent);
    }

    private void SetState(CaptureState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
