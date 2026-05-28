using Meadow;
using Meadow.Foundation.ICs.ADC;
using Meadow.Foundation.Sensors;
using Meadow.Foundation.Sensors.Buttons;
using Meadow.Foundation.Sensors.Switches;
using Meadow.Hardware;
using Meadow.Units;
using System;

namespace Neomotive.ModuleSimulator;

public class SimulatorInputBoard
{
    private Mcp3004 _adc;

    public Potentiometer Pot1 { get; }
    public Potentiometer Pot2 { get; }
    public Potentiometer Pot3 { get; }
    public Potentiometer Pot4 { get; }

    public PushButton Button1 => GetButton(0);
    public PushButton Button2 => GetButton(1);
    public PushButton Button3 => GetButton(2);
    public PushButton Button4 => GetButton(3);

    public SpstSwitch Switch1 => GetSwitch(0);
    public SpstSwitch Switch2 => GetSwitch(1);
    public SpstSwitch Switch3 => GetSwitch(2);
    public SpstSwitch Switch4 => GetSwitch(3);

    private readonly Lazy<PushButton>[] _buttons;
    private readonly Lazy<SpstSwitch>[] _switches;
    private readonly IPin[] _pins;

    public SimulatorInputBoard(RaspberryPi device)
    {
        var spi = device.CreateSpiBus(0, 5_000_000.Hertz());
        var cs = device.Pins.GPIO5.CreateDigitalOutputPort();
        _adc = new Mcp3004(spi, cs);

        var ain1 = _adc.Pins.CH0.CreateAnalogInputPort(1, TimeSpan.FromMilliseconds(250), _adc.DefaultReferenceVoltage);
        Pot1 = new Potentiometer(ain1, 10_000.Ohms()) { UpdateInterval = TimeSpan.FromMilliseconds(250) };
        var ain2 = _adc.Pins.CH1.CreateAnalogInputPort();
        Pot2 = new Potentiometer(ain2, 10_000.Ohms());
        var ain3 = _adc.Pins.CH2.CreateAnalogInputPort();
        Pot3 = new Potentiometer(ain3, 10_000.Ohms());
        var ain4 = _adc.Pins.CH3.CreateAnalogInputPort();
        Pot4 = new Potentiometer(ain4, 10_000.Ohms());

        _pins =
        [
            device.Pins.Pin7,
            device.Pins.Pin11,
            device.Pins.Pin13,
            device.Pins.Pin15
        ];

        _buttons = new Lazy<PushButton>[4];
        _switches = new Lazy<SpstSwitch>[4];

        for (int i = 0; i < 4; i++)
        {
            int index = i;

            // v1 input board conflicts with the dual CAN board on pins 11 and 15 (index 1 and 3).

            _buttons[index] = new Lazy<PushButton>(() =>
            {
                Resolver.Log.Info($"Initializing Button {index + 1} on GPIO {index + 1} (Pin {_pins[index].Name})");
                var btn = new PushButton(_pins[index], ResistorMode.ExternalPullDown, TimeSpan.FromMilliseconds(10));
                btn.ConfirmationDelay = TimeSpan.FromMilliseconds(10);
                return btn;
            });

            _switches[index] = new Lazy<SpstSwitch>(() =>
                new SpstSwitch(_pins[index], InterruptMode.EdgeBoth, ResistorMode.ExternalPullDown));
        }
    }

    private PushButton GetButton(int index)
    {
        if (_switches[index].IsValueCreated)
        {
            throw new NotSupportedException($"GPIO {index + 1} (Pin {_pins[index].Name}) is already in use for a Switch");
        }
        return _buttons[index].Value;
    }

    private SpstSwitch GetSwitch(int index)
    {
        if (_buttons[index].IsValueCreated)
        {
            throw new NotSupportedException($"GPIO {index + 1} (Pin {_pins[index].Name}) is already in use for a Button");
        }
        return _switches[index].Value;
    }
}
/* PINOUTS (v1)
PIN  | GPIO   | Function
--------------------
| 7  | GPIO4  | SW1 
| 11 | GPIO17 | SW2 
| 13 | GPIO27 | SW3 
| 15 | GPIO22 | SW4 
| 19 | MOSI   | SPI0
| 21 | MISO   | SPI0
| 23 | SCK    | SPI0
| 29 | GPIO5  | MCP3004 CS
        
! v1 input board conflicts with the dual CAN board.  We can't use switch 2 or 4 (and can't populate the pull-downs!)
*/