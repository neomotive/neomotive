using Neomotive.ScanTool.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neomotive.ScanTool.UI;

public class LivePidItem : INotifyPropertyChanged
{
    private const int HistoryCapacity = 120; // 60 s at 2 Hz

    public PidDescriptor Descriptor { get; }

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

    public string DisplayValue => _currentValue == double.MinValue
        ? "—"
        : $"{_currentValue:F1} {Descriptor.Unit}";

    public string ValueText => _currentValue == double.MinValue ? "—" : $"{_currentValue:F1}";

    public string SelectionIndicator => _isSelected ? "●" : "○";

    private readonly Queue<(DateTime Timestamp, double Value)> _history = new(HistoryCapacity);
    public IReadOnlyCollection<(DateTime Timestamp, double Value)> History => _history;

    public LivePidItem(PidDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public void UpdateValue(double value)
    {
        CurrentValue = value;
        if (_history.Count >= HistoryCapacity)
            _history.Dequeue();
        _history.Enqueue((DateTime.UtcNow, value));
    }

    public void Reset()
    {
        _currentValue = double.MinValue;
        _history.Clear();
        OnPropertyChanged(nameof(CurrentValue));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(ValueText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
