using Meadow.Units;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neomotive.ModuleSimulator.UI.Toolbox;

public record PotPidItem(string Key, string DisplayName, string Unit, double MinValue, double MaxValue)
{
    public double Map(double knobValue) => MinValue + (knobValue / 1023.0) * (MaxValue - MinValue);
    public string Format(double physicalValue) => $"{physicalValue:F1} {Unit}";
}

public class ToolboxViewModel : INotifyPropertyChanged
{
    private readonly DesktopInputs _inputs;
    private readonly Action<string, double>? _applyPotValue;

    public static readonly IReadOnlyList<PotPidItem> AvailablePids =
    [
        new("coolant_temp", "Coolant Temp",   "°C",   -40,   215),
        new("rpm",          "Engine RPM",     "rpm",    0,  8000),
        new("speed",        "Vehicle Speed",  "km/h",   0,   200),
        new("throttle",     "Throttle",       "%",      0,   100),
    ];

    private double _pot1Value = 512;
    private double _pot2Value = 512;
    private PotPidItem _pot1Pid = AvailablePids[0]; // Coolant Temp
    private PotPidItem _pot2Pid = AvailablePids[1]; // Engine RPM
    private bool _button1Down;
    private bool _button2Down;
    private bool _led2On;
    private bool _switch1On;
    private bool _switch2On;

    public ToolboxViewModel(DesktopInputs inputs, Action<string, double>? applyPotValue = null)
    {
        _inputs = inputs;
        _applyPotValue = applyPotValue;
    }

    private void ApplyPot1()
    {
        _inputs.SetPot1Voltage((_pot1Value / 1023.0 * 5.0).Volts());
        _applyPotValue?.Invoke(_pot1Pid.Key, _pot1Pid.Map(_pot1Value));
        OnPropertyChanged(nameof(Pot1DisplayValue));
    }

    private void ApplyPot2()
    {
        _inputs.SetPot2Voltage((_pot2Value / 1023.0 * 5.0).Volts());
        _applyPotValue?.Invoke(_pot2Pid.Key, _pot2Pid.Map(_pot2Value));
        OnPropertyChanged(nameof(Pot2DisplayValue));
    }

    // ── Pot PID selection ────────────────────────────────────────────────────

    public PotPidItem Pot1Pid
    {
        get => _pot1Pid;
        set
        {
            if (_pot1Pid == value) return;
            _pot1Pid = value;
            ApplyPot1();
            OnPropertyChanged();
        }
    }

    public PotPidItem Pot2Pid
    {
        get => _pot2Pid;
        set
        {
            if (_pot2Pid == value) return;
            _pot2Pid = value;
            ApplyPot2();
            OnPropertyChanged();
        }
    }

    public string Pot1DisplayValue => _pot1Pid.Format(_pot1Pid.Map(_pot1Value));
    public string Pot2DisplayValue => _pot2Pid.Format(_pot2Pid.Map(_pot2Value));

    // ── Pot values ───────────────────────────────────────────────────────────

    public double Pot1Value
    {
        get => _pot1Value;
        set
        {
            _pot1Value = value;
            ApplyPot1();
            OnPropertyChanged();
        }
    }

    public double Pot2Value
    {
        get => _pot2Value;
        set
        {
            _pot2Value = value;
            ApplyPot2();
            OnPropertyChanged();
        }
    }

    // ── LEDs ─────────────────────────────────────────────────────────────────

    /// <summary>Output: LED 1 state — driven by the simulator.</summary>
    public bool Led1On
    {
        get => _inputs.Led1?.IsOn ?? false;
        set
        {
            if (_inputs.Led1 is not null)
            {
                _inputs.Led1.IsOn = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Output: LED 2 state — driven by the simulator.</summary>
    public bool Led2On
    {
        get => _led2On;
        set { _led2On = value; OnPropertyChanged(); }
    }

    // ── Buttons ──────────────────────────────────────────────────────────────

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

    // ── Switches ─────────────────────────────────────────────────────────────

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
