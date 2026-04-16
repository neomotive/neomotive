using Meadow.Foundation.Leds;
using Meadow.Foundation.Sensors;
using Meadow.Units;

namespace Neomotive.ModuleSimulator.UI;

public class DesktopInputs : SimulatorInputs
{
    private readonly SimulatedAnalogInputPort _analogInputPort1;
    private readonly SimulatedAnalogInputPort _analogInputPort2;
    private readonly SimulatedDigitalOutputPort _ledOutputPort1;
    private readonly SimulatedDigitalOutputPort _ledOutputPort2;

    public DesktopInputs()
    {
        _analogInputPort1 = new SimulatedAnalogInputPort((3.3 / 2.0).Volts()); // 0-5V range, centered at 2.5V
        _analogInputPort2 = new SimulatedAnalogInputPort(2.5.Volts()); // 0-5V range, centered at 2.5V

        _ledOutputPort1 = new SimulatedDigitalOutputPort("led1", false);
        _ledOutputPort2 = new SimulatedDigitalOutputPort("led2", false);

        Pot1 = new Potentiometer(
            _analogInputPort1,
            10_000.Ohms());
        Pot2 = new Potentiometer(
            _analogInputPort2,
            10_000.Ohms());

        Led1 = new Led(_ledOutputPort1);
        Led2 = new Led(_ledOutputPort2);

        /*
        Button1 = new PushButton(new SimulatedDigitalInterruptPort());
        Button2 = new PushButton(new SimulatedDigitalInterruptPort());

        Switch1 = new SpstSwitch(new SimulatedDigitalInterruptPort());
        Switch2 = new SpstSwitch(new SimulatedDigitalInterruptPort());
        */
    }

    public void SetPot1Voltage(Voltage voltage)
    {
        _analogInputPort1.SetSensorValue(voltage);
    }

    public void SetPot2Voltage(Voltage voltage)
    {
        _analogInputPort2.SetSensorValue(voltage);
    }

    public void SetLed1State(bool isOn)
    {
        _ledOutputPort1.State = isOn;
    }

    public void SetLed2State(bool isOn)
    {
        _ledOutputPort2.State = isOn;
    }
}