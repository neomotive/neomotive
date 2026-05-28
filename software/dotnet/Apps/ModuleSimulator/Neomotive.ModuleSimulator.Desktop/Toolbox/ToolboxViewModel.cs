using Meadow.Units;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neomotive.ModuleSimulator.UI.Toolbox;

public class ToolboxViewModel : INotifyPropertyChanged
{
    private readonly DesktopInputs _inputs;
    private readonly InputsViewModel _inputsVm;

    private double _pot1Value = 512;
    private double _pot2Value = 512;
    private double _pot3Value = 512;
    private double _pot4Value = 512;
    private bool _button1Down;
    private bool _button2Down;
    private bool _button3Down;
    private bool _button4Down;
    private bool _switch1On;
    private bool _switch2On;
    private bool _switch3On;
    private bool _switch4On;

    public ToolboxViewModel(DesktopInputs inputs, InputsViewModel inputsVm)
    {
        _inputs = inputs;
        _inputsVm = inputsVm;
    }

    // ── Pot values (0–1023 knob range → 0–5V) ───────────────────────────────

    public string Pot1DisplayValue => $"{_pot1Value / 1023.0 * 5.0:F2}V";
    public string Pot2DisplayValue => $"{_pot2Value / 1023.0 * 5.0:F2}V";
    public string Pot3DisplayValue => $"{_pot3Value / 1023.0 * 5.0:F2}V";
    public string Pot4DisplayValue => $"{_pot4Value / 1023.0 * 5.0:F2}V";

    public double Pot1Value
    {
        get => _pot1Value;
        set { _pot1Value = value; _inputs.SetPot1Voltage((_pot1Value / 1023.0 * 5.0).Volts()); OnPropertyChanged(); OnPropertyChanged(nameof(Pot1DisplayValue)); }
    }

    public double Pot2Value
    {
        get => _pot2Value;
        set { _pot2Value = value; _inputs.SetPot2Voltage((_pot2Value / 1023.0 * 5.0).Volts()); OnPropertyChanged(); OnPropertyChanged(nameof(Pot2DisplayValue)); }
    }

    public double Pot3Value
    {
        get => _pot3Value;
        set { _pot3Value = value; _inputs.SetPot3Voltage((_pot3Value / 1023.0 * 5.0).Volts()); OnPropertyChanged(); OnPropertyChanged(nameof(Pot3DisplayValue)); }
    }

    public double Pot4Value
    {
        get => _pot4Value;
        set { _pot4Value = value; _inputs.SetPot4Voltage((_pot4Value / 1023.0 * 5.0).Volts()); OnPropertyChanged(); OnPropertyChanged(nameof(Pot4DisplayValue)); }
    }

    // ── LEDs (outputs — driven by simulator) ─────────────────────────────────

    public bool Led1On
    {
        get => _inputs.Led1?.IsOn ?? false;
        set { if (_inputs.Led1 is not null) { _inputs.Led1.IsOn = value; OnPropertyChanged(); } }
    }

    public bool Led2On
    {
        get => _inputs.Led2?.IsOn ?? false;
        set { if (_inputs.Led2 is not null) { _inputs.Led2.IsOn = value; OnPropertyChanged(); } }
    }

    // ── Buttons (momentary, true while held) ─────────────────────────────────

    public bool Button1Down
    {
        get => _button1Down;
        set { _button1Down = value; _inputsVm.SetButtonState(1, value); OnPropertyChanged(); }
    }

    public bool Button2Down
    {
        get => _button2Down;
        set { _button2Down = value; _inputsVm.SetButtonState(2, value); OnPropertyChanged(); }
    }

    public bool Button3Down
    {
        get => _button3Down;
        set { _button3Down = value; _inputsVm.SetButtonState(3, value); OnPropertyChanged(); }
    }

    public bool Button4Down
    {
        get => _button4Down;
        set { _button4Down = value; _inputsVm.SetButtonState(4, value); OnPropertyChanged(); }
    }

    // ── Switches (SPST, latching) ─────────────────────────────────────────────

    public bool Switch1On
    {
        get => _switch1On;
        set { _switch1On = value; _inputsVm.SetSwitchState(1, value); OnPropertyChanged(); }
    }

    public bool Switch2On
    {
        get => _switch2On;
        set { _switch2On = value; _inputsVm.SetSwitchState(2, value); OnPropertyChanged(); }
    }

    public bool Switch3On
    {
        get => _switch3On;
        set { _switch3On = value; _inputsVm.SetSwitchState(3, value); OnPropertyChanged(); }
    }

    public bool Switch4On
    {
        get => _switch4On;
        set { _switch4On = value; _inputsVm.SetSwitchState(4, value); OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
