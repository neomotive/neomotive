using Meadow;
using Meadow.Foundation.ICs.ADC;
using Meadow.Foundation.Sensors;
using Meadow.Foundation.Sensors.Buttons;
using Meadow.Foundation.Sensors.Switches;
using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using Meadow.Peripherals.Sensors.Buttons;
using Meadow.Peripherals.Switches;
using Meadow.Units;
using System;

namespace Neomotive.ModuleSimulator;

public class SimulatorInputBoard : ISimulatorInputs
{
    private readonly Mcp3004 _adc;
    private readonly Potentiometer _pot1;
    private readonly Potentiometer _pot2;
    private readonly Potentiometer _pot3;
    private readonly Potentiometer _pot4;

    public IPotentiometer? Pot1 => _pot1;
    public IPotentiometer? Pot2 => _pot2;
    public IPotentiometer? Pot3 => _pot3;
    public IPotentiometer? Pot4 => _pot4;

    public ILed? Led1 => null;
    public ILed? Led2 => null;

    public IButton? Button1 { get; private set; }
    public IButton? Button2 { get; private set; }
    public IButton? Button3 => null;
    public IButton? Button4 => null;

    public ISwitch? Switch1 { get; private set; }
    public ISwitch? Switch2 { get; private set; }
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

    public double Pot1Volts { get; set; }
    public double Pot2Volts { get; set; }
    public double Pot3Volts { get; set; }
    public double Pot4Volts { get; set; }

    public SimulatorInputBoard(RaspberryPi device)
    {
        Console.WriteLine($"[DBG] SimulatorInputBoard ctor: device is {(device == null ? "NULL" : device.GetType().Name)}");
        if (device == null) throw new ArgumentNullException(nameof(device), "Device is null — Meadow may not have finished initializing");

        Resolver.Log.Info("Initializing SPI...");
        var spi = device.CreateSpiBus(0, 5_000_000.Hertz());
        Resolver.Log.Info("Initializing CS...");
        var cs = device.Pins.GPIO5.CreateDigitalOutputPort();
        Resolver.Log.Info("Initializing ADC...");
        _adc = new Mcp3004(spi, cs);

        Resolver.Log.Info("Initializing potentiometers...");

        var refVolts = _adc.DefaultReferenceVoltage.Volts;

        var ain1 = _adc.Pins.CH0.CreateAnalogInputPort(1, TimeSpan.FromMilliseconds(250), _adc.DefaultReferenceVoltage);
        _pot1 = new Potentiometer(ain1, 10_000.Ohms()) { UpdateInterval = TimeSpan.FromMilliseconds(250) };
        _pot1.Changed += (s, e) => Pot1Volts = _pot1.GetCurrentPosition() * refVolts;

        var ain2 = _adc.Pins.CH1.CreateAnalogInputPort();
        _pot2 = new Potentiometer(ain2, 10_000.Ohms());
        _pot2.Changed += (s, e) => Pot2Volts = _pot2.GetCurrentPosition() * refVolts;

        var ain3 = _adc.Pins.CH2.CreateAnalogInputPort();
        _pot3 = new Potentiometer(ain3, 10_000.Ohms());
        _pot3.Changed += (s, e) => Pot3Volts = _pot3.GetCurrentPosition() * refVolts;

        var ain4 = _adc.Pins.CH3.CreateAnalogInputPort();
        _pot4 = new Potentiometer(ain4, 10_000.Ohms());
        _pot4.Changed += (s, e) => Pot4Volts = _pot4.GetCurrentPosition() * refVolts;

        Resolver.Log.Info("Initializing discrete inputs...");

        // Switches on first 2 GPIO pins: Pin7 (GPIO4), Pin11 (GPIO17)
        var sw1 = new SpstSwitch(device.Pins.Pin7, InterruptMode.EdgeBoth, ResistorMode.ExternalPullDown);
        sw1.Changed += (s, e) => Switch1On = sw1.IsOn;
        Switch1On = sw1.IsOn;
        Switch1 = sw1;

        var sw2 = new SpstSwitch(device.Pins.Pin11, InterruptMode.EdgeBoth, ResistorMode.ExternalPullDown);
        sw2.Changed += (s, e) => Switch2On = sw2.IsOn;
        Switch2On = sw2.IsOn;
        Switch2 = sw2;

        // Buttons on second 2 GPIO pins: Pin13 (GPIO27), Pin15 (GPIO22)
        var btn1 = new PushButton(device.Pins.Pin13, ResistorMode.ExternalPullDown, TimeSpan.FromMilliseconds(10));
        btn1.PressStarted += (s, e) => Button1Down = true;
        btn1.PressEnded += (s, e) => Button1Down = false;
        Button1 = btn1;

        var btn2 = new PushButton(device.Pins.Pin15, ResistorMode.ExternalPullDown, TimeSpan.FromMilliseconds(10));
        btn2.PressStarted += (s, e) => Button2Down = true;
        btn2.PressEnded += (s, e) => Button2Down = false;
        Button2 = btn2;
    }
}
/* PINOUTS (v1)
PIN  | GPIO   | Function
--------------------
| 7  | GPIO4  | SW1
| 11 | GPIO17 | SW2  (conflicts with dual CAN hat on v1 board)
| 13 | GPIO27 | BTN1
| 15 | GPIO22 | BTN2 (conflicts with dual CAN hat on v1 board)
| 19 | MOSI   | SPI0
| 21 | MISO   | SPI0
| 23 | SCK    | SPI0
| 29 | GPIO5  | MCP3004 CS
*/
