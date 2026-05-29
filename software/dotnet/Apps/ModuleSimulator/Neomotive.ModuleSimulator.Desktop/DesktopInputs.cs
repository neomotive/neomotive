using Meadow.Foundation.Leds;
using Meadow.Foundation.Sensors;
using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using Meadow.Peripherals.Sensors.Buttons;
using Meadow.Peripherals.Switches;
using Meadow.Units;

namespace Neomotive.ModuleSimulator.UI;

public class DesktopInputs : ISimulatorInputs
{
    private readonly SimulatedAnalogInputPort _analogInputPort1;
    private readonly SimulatedAnalogInputPort _analogInputPort2;
    private readonly SimulatedAnalogInputPort _analogInputPort3;
    private readonly SimulatedAnalogInputPort _analogInputPort4;
    private readonly SimulatedDigitalOutputPort _ledOutputPort1;
    private readonly SimulatedDigitalOutputPort _ledOutputPort2;
    private readonly Potentiometer _pot1;
    private readonly Potentiometer _pot2;
    private readonly Potentiometer _pot3;
    private readonly Potentiometer _pot4;
    private readonly Led _led1;
    private readonly Led _led2;

    public IPotentiometer? Pot1 => _pot1;
    public IPotentiometer? Pot2 => _pot2;
    public IPotentiometer? Pot3 => _pot3;
    public IPotentiometer? Pot4 => _pot4;

    public ILed? Led1 => _led1;
    public ILed? Led2 => _led2;

    // Desktop has no hardware buttons or switches — state tracked via Button/SwitchXxx properties
    public IButton? Button1 => null;
    public IButton? Button2 => null;
    public IButton? Button3 => null;
    public IButton? Button4 => null;

    public ISwitch? Switch1 => null;
    public ISwitch? Switch2 => null;
    public ISwitch? Switch3 => null;
    public ISwitch? Switch4 => null;

    public bool Button1Down { get; set; }
    public bool Button2Down { get; set; }
    public bool Button3Down { get; set; }
    public bool Button4Down { get; set; }

    public bool Switch1On { get; set; }
    public bool Switch2On { get; set; }
    public bool Switch3On { get; set; }
    public bool Switch4On { get; set; }

    public double Pot1Volts { get; set; } = 2.5;
    public double Pot2Volts { get; set; } = 2.5;
    public double Pot3Volts { get; set; } = 2.5;
    public double Pot4Volts { get; set; } = 2.5;

    public DesktopInputs()
    {
        _analogInputPort1 = new SimulatedAnalogInputPort(2.5.Volts());
        _analogInputPort2 = new SimulatedAnalogInputPort(2.5.Volts());
        _analogInputPort3 = new SimulatedAnalogInputPort(2.5.Volts());
        _analogInputPort4 = new SimulatedAnalogInputPort(2.5.Volts());

        _ledOutputPort1 = new SimulatedDigitalOutputPort("led1", false);
        _ledOutputPort2 = new SimulatedDigitalOutputPort("led2", false);

        _pot1 = new Potentiometer(_analogInputPort1, 10_000.Ohms());
        _pot2 = new Potentiometer(_analogInputPort2, 10_000.Ohms());
        _pot3 = new Potentiometer(_analogInputPort3, 10_000.Ohms());
        _pot4 = new Potentiometer(_analogInputPort4, 10_000.Ohms());

        _led1 = new Led(_ledOutputPort1);
        _led2 = new Led(_ledOutputPort2);
    }

    public void SetPot1Voltage(Voltage voltage) { _analogInputPort1.SetSensorValue(voltage); Pot1Volts = voltage.Volts; }
    public void SetPot2Voltage(Voltage voltage) { _analogInputPort2.SetSensorValue(voltage); Pot2Volts = voltage.Volts; }
    public void SetPot3Voltage(Voltage voltage) { _analogInputPort3.SetSensorValue(voltage); Pot3Volts = voltage.Volts; }
    public void SetPot4Voltage(Voltage voltage) { _analogInputPort4.SetSensorValue(voltage); Pot4Volts = voltage.Volts; }

    public void SetLed1State(bool isOn) => _ledOutputPort1.State = isOn;
    public void SetLed2State(bool isOn) => _ledOutputPort2.State = isOn;
}
