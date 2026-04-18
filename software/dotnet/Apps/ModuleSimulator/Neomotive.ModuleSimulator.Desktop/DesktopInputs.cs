using Meadow.Foundation.Leds;
using Meadow.Foundation.Sensors;
using Meadow.Units;

namespace Neomotive.ModuleSimulator.UI;

public class DesktopInputs : SimulatorInputs
{
    private readonly SimulatedAnalogInputPort _analogInputPort1;
    private readonly SimulatedAnalogInputPort _analogInputPort2;
    private readonly SimulatedAnalogInputPort _analogInputPort3;
    private readonly SimulatedAnalogInputPort _analogInputPort4;
    private readonly SimulatedDigitalOutputPort _ledOutputPort1;
    private readonly SimulatedDigitalOutputPort _ledOutputPort2;

    public DesktopInputs()
    {
        _analogInputPort1 = new SimulatedAnalogInputPort(2.5.Volts());
        _analogInputPort2 = new SimulatedAnalogInputPort(2.5.Volts());
        _analogInputPort3 = new SimulatedAnalogInputPort(2.5.Volts());
        _analogInputPort4 = new SimulatedAnalogInputPort(2.5.Volts());

        _ledOutputPort1 = new SimulatedDigitalOutputPort("led1", false);
        _ledOutputPort2 = new SimulatedDigitalOutputPort("led2", false);

        Pot1 = new Potentiometer(_analogInputPort1, 10_000.Ohms());
        Pot2 = new Potentiometer(_analogInputPort2, 10_000.Ohms());
        Pot3 = new Potentiometer(_analogInputPort3, 10_000.Ohms());
        Pot4 = new Potentiometer(_analogInputPort4, 10_000.Ohms());

        Led1 = new Led(_ledOutputPort1);
        Led2 = new Led(_ledOutputPort2);

        // IButton and ISwitch are null on desktop — state is tracked via
        // SimulatorInputs.Button{n}Down and Switch{n}On directly.
    }

    public void SetPot1Voltage(Voltage voltage) { _analogInputPort1.SetSensorValue(voltage); Pot1Volts = voltage.Volts; }
    public void SetPot2Voltage(Voltage voltage) { _analogInputPort2.SetSensorValue(voltage); Pot2Volts = voltage.Volts; }
    public void SetPot3Voltage(Voltage voltage) { _analogInputPort3.SetSensorValue(voltage); Pot3Volts = voltage.Volts; }
    public void SetPot4Voltage(Voltage voltage) { _analogInputPort4.SetSensorValue(voltage); Pot4Volts = voltage.Volts; }

    public void SetLed1State(bool isOn) => _ledOutputPort1.State = isOn;
    public void SetLed2State(bool isOn) => _ledOutputPort2.State = isOn;
}