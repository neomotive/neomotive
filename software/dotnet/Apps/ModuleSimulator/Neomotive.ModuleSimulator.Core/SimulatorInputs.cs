using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using Meadow.Peripherals.Sensors.Buttons;
using Meadow.Peripherals.Switches;

namespace Neomotive.ModuleSimulator;

public class SimulatorInputs
{
    public IPotentiometer? Pot1 { get; set; }
    public IPotentiometer? Pot2 { get; set; }
    public ILed? Led1 { get; set; }
    public ILed? Led2 { get; set; }
    public IButton? Button1 { get; set; }
    public IButton? Button2 { get; set; }
    public ISwitch? Switch1 { get; set; }
    public ISwitch? Switch2 { get; set; }
}
