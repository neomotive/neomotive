using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using Meadow.Peripherals.Sensors.Buttons;
using Meadow.Peripherals.Switches;
using Xunit;

namespace Neomotive.ModuleSimulator.Tests;

/// <summary>Bare stub; the start-scenario paths do not touch the input board.</summary>
internal sealed class StubInputs : ISimulatorInputs
{
    public IPotentiometer? Pot1 => null;
    public IPotentiometer? Pot2 => null;
    public IPotentiometer? Pot3 => null;
    public IPotentiometer? Pot4 => null;

    public ILed? Led1 => null;
    public ILed? Led2 => null;

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

    public double Pot1Volts { get; set; }
    public double Pot2Volts { get; set; }
    public double Pot3Volts { get; set; }
    public double Pot4Volts { get; set; }
}

public class SimulatorStateTests
{
    private static SimulatorState NewState() => new(new StubInputs());

    [Fact]
    public void Manual_values_are_used_when_no_scenario_is_running()
    {
        var state = NewState();
        state.Rpm = 800f;
        state.FuelRailPressureKpa = 35_000;
        state.ControlModuleVolts = 14.2;

        Assert.Equal(800f, state.CurrentRpm);
        Assert.Equal(35_000, state.CurrentFuelRailPressureKpa);
        Assert.Equal(14.2, state.CurrentControlModuleVolts);
    }

    [Fact]
    public void A_running_scenario_overrides_the_manual_values()
    {
        var state = NewState();
        state.Rpm = 800f;

        state.BeginStartAttempt(StartProfile.HealthyStart);

        // Scenario starts at key-on, so the engine is stationary regardless of the manual RPM.
        Assert.Equal(0, state.CurrentRpm);
    }

    [Fact]
    public void Stopping_a_scenario_restores_the_manual_values()
    {
        var state = NewState();
        state.Rpm = 800f;
        state.FuelRailPressureKpa = 35_000;

        state.BeginStartAttempt(StartProfile.HealthyStart);
        state.StopStartAttempt();

        Assert.Equal(800f, state.CurrentRpm);
        Assert.Equal(35_000, state.CurrentFuelRailPressureKpa);
    }

    [Fact]
    public void Beginning_an_attempt_returns_a_running_scenario()
    {
        var state = NewState();
        var scenario = state.BeginStartAttempt(StartProfile.RailCollapse);

        Assert.True(scenario.IsRunning);
        Assert.Equal(StartProfile.RailCollapse, scenario.Profile);
        Assert.Same(scenario, state.Scenario);
    }

    [Fact]
    public void Stopping_without_a_scenario_is_harmless()
    {
        var state = NewState();
        state.StopStartAttempt();

        Assert.Null(state.Scenario);
    }
}
