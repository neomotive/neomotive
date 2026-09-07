using System.ComponentModel;
using System.Runtime.CompilerServices;
using Neomotive.ScanTool.Core.Capture;

namespace Neomotive.ScanTool.UI;

/// <summary>A user-defined Mode $22 channel offered for capture, with its selection state.</summary>
public class Mode22SignalItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public Mode22SignalItem(Mode22SignalDefinition definition) => Definition = definition;

    public Mode22SignalDefinition Definition { get; }

    public string Name => Definition.Name;

    public string Unit => Definition.Unit;

    public string Detail => Definition.Describe();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionIndicator));
        }
    }

    public string SelectionIndicator => _isSelected ? "●" : "○";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
