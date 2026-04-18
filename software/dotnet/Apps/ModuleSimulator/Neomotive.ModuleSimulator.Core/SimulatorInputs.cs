using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using Meadow.Peripherals.Sensors.Buttons;
using Meadow.Peripherals.Switches;

namespace Neomotive.ModuleSimulator;

public class SimulatorInputs
{
    public IPotentiometer? Pot1 { get; set; }
    public IPotentiometer? Pot2 { get; set; }
    public IPotentiometer? Pot3 { get; set; }
    public IPotentiometer? Pot4 { get; set; }

    public ILed? Led1 { get; set; }
    public ILed? Led2 { get; set; }

    // Hardware button interfaces (optional; set when actual hardware is available)
    public IButton? Button1 { get; set; }
    public IButton? Button2 { get; set; }
    public IButton? Button3 { get; set; }
    public IButton? Button4 { get; set; }

    // Hardware switch interfaces (optional; set when actual hardware is available)
    public ISwitch? Switch1 { get; set; }
    public ISwitch? Switch2 { get; set; }
    public ISwitch? Switch3 { get; set; }
    public ISwitch? Switch4 { get; set; }

    // Direct boolean state — used by desktop simulation when hardware ports are unavailable.
    // On real hardware, these should be updated from IButton/ISwitch event handlers.
    public bool Button1Down { get; set; }
    public bool Button2Down { get; set; }
    public bool Button3Down { get; set; }
    public bool Button4Down { get; set; }

    public bool Switch1On { get; set; }
    public bool Switch2On { get; set; }
    public bool Switch3On { get; set; }
    public bool Switch4On { get; set; }

    // Pot voltage cache (0–5 V) — updated whenever SetPot{n}Voltage is called so
    // UI code in UIShared can read current values without referencing Meadow types.
    public double Pot1Volts { get; set; } = 2.5;
    public double Pot2Volts { get; set; } = 2.5;
    public double Pot3Volts { get; set; } = 2.5;
    public double Pot4Volts { get; set; } = 2.5;
}
