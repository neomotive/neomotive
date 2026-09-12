using Neomotive.ScanTool.Core.Signals;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neomotive.ScanTool.UI;

public class LivePidItem : INotifyPropertyChanged
{
    // 60 s of history at the 10 Hz the sweep now targets. The old capacity assumed 2 Hz, which
    // was not a choice so much as the ceiling the fixed 500 ms sweep delay imposed.
    private const int HistoryCapacity = 600;

    /// <summary>
    /// Consecutive misses after which the polling loop stops asking for this PID. Three sweeps is
    /// enough to tell "this PCM does not serve this PID" from a single dropped frame.
    /// </summary>
    public const int MissesBeforeDrop = 3;

    public SignalDefinition Descriptor { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionIndicator)); }
    }

    private double _currentValue = double.MinValue;
    public double CurrentValue
    {
        get => _currentValue;
        private set { _currentValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); OnPropertyChanged(nameof(ValueText)); }
    }

    public string DisplayValue => IsUnanswered
        ? "no response"
        : _currentValue == double.MinValue
            ? "—"
            : $"{_currentValue:F1} {Descriptor.Unit}";

    public string ValueText => IsUnanswered
        ? "n/r"
        : _currentValue == double.MinValue ? "—" : $"{_currentValue:F1}";

    public string SelectionIndicator => _isSelected ? "●" : "○";

    private int _consecutiveMisses;
    private DateTime _lastUpdatedUtc = DateTime.MinValue;

    /// <summary>
    /// True once the module has ignored this PID often enough that the loop stopped asking. The
    /// row says so in words rather than sitting on a stale value or a bare dash — on a touch panel
    /// "no response" and "not read yet" have to be distinguishable without hovering anything.
    /// </summary>
    public bool IsUnanswered => _consecutiveMisses >= MissesBeforeDrop;

    /// <summary>Whether the polling loop should still spend a request on this PID.</summary>
    public bool ShouldPoll => _isSelected && !IsUnanswered;

    /// <summary>How stale the displayed value is; drives the age column on the table pane.</summary>
    public string AgeText
    {
        get
        {
            if (IsUnanswered) return "no response";
            if (_lastUpdatedUtc == DateTime.MinValue) return "—";
            var age = DateTime.UtcNow - _lastUpdatedUtc;
            return age.TotalSeconds < 1 ? $"{age.TotalMilliseconds:F0} ms" : $"{age.TotalSeconds:F1} s";
        }
    }

    private readonly Queue<(DateTime Timestamp, double Value)> _history = new(HistoryCapacity);
    public IReadOnlyCollection<(DateTime Timestamp, double Value)> History => _history;

    public LivePidItem(SignalDefinition descriptor)
    {
        Descriptor = descriptor;
    }

    public void UpdateValue(double value)
    {
        bool wasUnanswered = IsUnanswered;
        _consecutiveMisses = 0;
        _lastUpdatedUtc = DateTime.UtcNow;

        CurrentValue = value;
        if (_history.Count >= HistoryCapacity)
            _history.Dequeue();
        _history.Enqueue((_lastUpdatedUtc, value));

        if (wasUnanswered)
        {
            // A PID can come back — a module that was busy, or one that only answers with the
            // engine running. Nothing here is a permanent verdict.
            OnPropertyChanged(nameof(IsUnanswered));
            OnPropertyChanged(nameof(ShouldPoll));
        }
    }

    /// <summary>
    /// Records a sweep in which the module did not answer. Returns true if this miss is the one
    /// that drops the PID from the sweep, so the caller can raise the notification once.
    /// </summary>
    public bool RecordMiss()
    {
        if (IsUnanswered) return false;

        _consecutiveMisses++;
        if (!IsUnanswered) return false;

        OnPropertyChanged(nameof(IsUnanswered));
        OnPropertyChanged(nameof(ShouldPoll));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(AgeText));
        return true;
    }

    /// <summary>Clears the miss count so a dropped PID is tried again.</summary>
    public void RetryUnanswered()
    {
        if (_consecutiveMisses == 0) return;
        _consecutiveMisses = 0;
        OnPropertyChanged(nameof(IsUnanswered));
        OnPropertyChanged(nameof(ShouldPoll));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(ValueText));
    }

    /// <summary>Raises the age notification; the polling loop ticks this, not the clock.</summary>
    public void NotifyAge() => OnPropertyChanged(nameof(AgeText));

    public void Reset()
    {
        _currentValue = double.MinValue;
        _consecutiveMisses = 0;
        _lastUpdatedUtc = DateTime.MinValue;
        _history.Clear();
        OnPropertyChanged(nameof(CurrentValue));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(IsUnanswered));
        OnPropertyChanged(nameof(ShouldPoll));
        OnPropertyChanged(nameof(AgeText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
