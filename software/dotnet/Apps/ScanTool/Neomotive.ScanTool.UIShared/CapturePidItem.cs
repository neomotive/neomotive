using System.ComponentModel;
using System.Runtime.CompilerServices;
using Neomotive.ScanTool.Core;

namespace Neomotive.ScanTool.UI;

/// <summary>A PID offered for capture, with its selection state.</summary>
public class CapturePidItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public CapturePidItem(PidDescriptor descriptor) => Descriptor = descriptor;

    public PidDescriptor Descriptor { get; }

    public string Name => Descriptor.Name;

    public string Unit => Descriptor.Unit;

    /// <summary>Matches <see cref="Core.Capture.CaptureSignal.Key"/> for this PID.</summary>
    public string Key => Descriptor.Id.ToString();

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
