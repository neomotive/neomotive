using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neomotive.ModuleSimulator.UI.Toolbox;

public class ToolboxViewModel : INotifyPropertyChanged
{
    private double _pot1Value = 512;
    private double _pot2Value = 512;
    private bool _button1Down;
    private bool _button2Down;
    private bool _led1On;
    private bool _led2On;

    /// <summary>Input: potentiometer 1 value (0–1023).</summary>
    public double Pot1Value
    {
        get => _pot1Value;
        set { _pot1Value = value; OnPropertyChanged(); }
    }

    /// <summary>Input: potentiometer 2 value (0–1023).</summary>
    public double Pot2Value
    {
        get => _pot2Value;
        set { _pot2Value = value; OnPropertyChanged(); }
    }

    /// <summary>Input: momentary push button 1 — true while held.</summary>
    public bool Button1Down
    {
        get => _button1Down;
        set { _button1Down = value; OnPropertyChanged(); }
    }

    /// <summary>Input: momentary push button 2 — true while held.</summary>
    public bool Button2Down
    {
        get => _button2Down;
        set { _button2Down = value; OnPropertyChanged(); }
    }

    /// <summary>Output: LED 1 state — driven by the simulator.</summary>
    public bool Led1On
    {
        get => _led1On;
        set { _led1On = value; OnPropertyChanged(); }
    }

    /// <summary>Output: LED 2 state — driven by the simulator.</summary>
    public bool Led2On
    {
        get => _led2On;
        set { _led2On = value; OnPropertyChanged(); }
    }

    private bool _switch1On;
    private bool _switch2On;

    /// <summary>Input: SPST switch 1 state.</summary>
    public bool Switch1On
    {
        get => _switch1On;
        set { _switch1On = value; OnPropertyChanged(); }
    }

    /// <summary>Input: SPST switch 2 state.</summary>
    public bool Switch2On
    {
        get => _switch2On;
        set { _switch2On = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
