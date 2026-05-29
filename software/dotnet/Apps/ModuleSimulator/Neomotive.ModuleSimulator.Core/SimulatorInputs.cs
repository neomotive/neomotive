using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using Meadow.Peripherals.Sensors.Buttons;
using Meadow.Peripherals.Switches;

namespace Neomotive.ModuleSimulator;

public interface ISimulatorInputs
{
    IPotentiometer? Pot1 { get; }
    IPotentiometer? Pot2 { get; }
    IPotentiometer? Pot3 { get; }
    IPotentiometer? Pot4 { get; }

    ILed? Led1 { get; }
    ILed? Led2 { get; }

    IButton? Button1 { get; }
    IButton? Button2 { get; }
    IButton? Button3 { get; }
    IButton? Button4 { get; }

    ISwitch? Switch1 { get; }
    ISwitch? Switch2 { get; }
    ISwitch? Switch3 { get; }
    ISwitch? Switch4 { get; }

    bool Button1Down { get; set; }
    bool Button2Down { get; set; }
    bool Button3Down { get; set; }
    bool Button4Down { get; set; }

    bool Switch1On { get; set; }
    bool Switch2On { get; set; }
    bool Switch3On { get; set; }
    bool Switch4On { get; set; }

    double Pot1Volts { get; set; }
    double Pot2Volts { get; set; }
    double Pot3Volts { get; set; }
    double Pot4Volts { get; set; }
}
