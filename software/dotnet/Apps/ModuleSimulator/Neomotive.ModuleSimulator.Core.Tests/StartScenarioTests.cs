using Xunit;

namespace Neomotive.ModuleSimulator.Tests;

/// <summary>
/// These assert the diagnostic character of each profile rather than exact curve values. The
/// simulator is the reference the ScanTool capture feature is verified against, so what matters is
/// that "healthy" genuinely reaches injection pressure and "weak lift pump" genuinely does not.
/// </summary>
public class StartScenarioTests
{
    private const double Threshold = StartScenario.InjectionThresholdKpa;

    private static IEnumerable<double> Timeline(double until = 10, double step = 0.05)
    {
        for (var t = 0.0; t <= until; t += step)
        {
            yield return t;
        }
    }

    private static double PeakRail(StartProfile profile)
        => Timeline().Max(t => StartScenario.Sample(profile, t).RailPressureKpa);

    private static double PeakRpm(StartProfile profile)
        => Timeline().Max(t => StartScenario.Sample(profile, t).Rpm);

    private static double MinVolts(StartProfile profile)
        => Timeline().Min(t => StartScenario.Sample(profile, t).Volts);

    [Theory]
    [InlineData(StartProfile.HealthyStart)]
    [InlineData(StartProfile.WeakLiftPump)]
    [InlineData(StartProfile.RailCollapse)]
    [InlineData(StartProfile.WeakBattery)]
    [InlineData(StartProfile.NoCrank)]
    public void Engine_is_stationary_through_the_prime_phase(StartProfile profile)
    {
        // Cranking must not begin before the starter engages, so a capture armed at key-on has a
        // genuine quiet period in its pre-trigger buffer.
        Assert.Equal(0, StartScenario.Sample(profile, 0).Rpm);
        Assert.Equal(0, StartScenario.Sample(profile, StartScenario.CrankStartSeconds).Rpm);
    }

    [Theory]
    [InlineData(StartProfile.HealthyStart)]
    [InlineData(StartProfile.WeakLiftPump)]
    [InlineData(StartProfile.RailCollapse)]
    [InlineData(StartProfile.WeakBattery)]
    [InlineData(StartProfile.NoCrank)]
    public void Values_are_clamped_outside_the_scripted_window(StartProfile profile)
    {
        var atZero = StartScenario.Sample(profile, 0);
        var wayPast = StartScenario.Sample(profile, 600);

        Assert.Equal(atZero, StartScenario.Sample(profile, -5));
        Assert.Equal(wayPast, StartScenario.Sample(profile, 6000));
    }

    [Fact]
    public void Healthy_start_reaches_injection_pressure_and_idles()
    {
        Assert.True(PeakRail(StartProfile.HealthyStart) > Threshold);

        // Catches and settles into an idle well above cranking speed.
        var idle = StartScenario.Sample(StartProfile.HealthyStart, 5).Rpm;
        Assert.InRange(idle, 600, 900);

        // Voltage dips under starter load, then the alternator takes over.
        Assert.InRange(MinVolts(StartProfile.HealthyStart), 10.5, 12.0);
        Assert.True(StartScenario.Sample(StartProfile.HealthyStart, 5).Volts > 13.5);
    }

    [Fact]
    public void Weak_lift_pump_never_reaches_injection_pressure()
    {
        Assert.True(PeakRail(StartProfile.WeakLiftPump) < Threshold);

        // Never leaves cranking speed, because injection is never commanded.
        Assert.True(PeakRpm(StartProfile.WeakLiftPump) < 250);
    }

    [Fact]
    public void Weak_lift_pump_builds_pressure_slowly()
    {
        var healthy = StartScenario.Sample(StartProfile.HealthyStart, 1.4).RailPressureKpa;
        var weak = StartScenario.Sample(StartProfile.WeakLiftPump, 1.4).RailPressureKpa;

        // The rise-rate difference is the measurement that separates these two on a capture.
        Assert.True(weak < healthy / 2);
    }

    [Fact]
    public void Rail_collapse_catches_then_dies()
    {
        Assert.True(PeakRail(StartProfile.RailCollapse) > Threshold);

        // Fires and picks up speed...
        Assert.True(PeakRpm(StartProfile.RailCollapse) > 600);

        // ...then loses pressure and stops. This is the profile that exercises StallDetector.
        Assert.Equal(0, StartScenario.Sample(StartProfile.RailCollapse, 5).Rpm);
        Assert.True(StartScenario.Sample(StartProfile.RailCollapse, 5).RailPressureKpa < 2_000);
    }

    [Fact]
    public void Rail_collapse_loses_pressure_before_rpm_falls()
    {
        // Cause precedes effect: pressure must already be dropping while the engine is still
        // turning, otherwise the capture would not point at fuel delivery.
        var railFalling = StartScenario.Sample(StartProfile.RailCollapse, 2.4).RailPressureKpa
            < StartScenario.Sample(StartProfile.RailCollapse, 2.0).RailPressureKpa;

        Assert.True(railFalling);
        Assert.True(StartScenario.Sample(StartProfile.RailCollapse, 2.4).Rpm > 0);
    }

    [Fact]
    public void Weak_battery_sags_below_the_cranking_limit()
    {
        // The electrical fault that mimics a fuel fault: read voltage first.
        Assert.True(MinVolts(StartProfile.WeakBattery) < 9.5);

        // Slow cranking, and rail pressure suffers as a consequence rather than as a cause.
        Assert.True(PeakRpm(StartProfile.WeakBattery) < 150);
        Assert.True(PeakRail(StartProfile.WeakBattery) < Threshold);
    }

    [Fact]
    public void No_crank_never_turns_the_engine()
    {
        Assert.All(Timeline(), t => Assert.Equal(0, StartScenario.Sample(StartProfile.NoCrank, t).Rpm));
        Assert.Equal(0, PeakRail(StartProfile.NoCrank));
    }

    [Fact]
    public void Scenario_tracks_an_injected_clock()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var scenario = new StartScenario(StartProfile.HealthyStart, () => now);

        scenario.Begin();
        Assert.True(scenario.IsRunning);
        Assert.Equal(0, scenario.Current.Rpm);

        now = now.AddSeconds(5);
        Assert.Equal(StartScenario.Sample(StartProfile.HealthyStart, 5), scenario.Current);

        scenario.Stop();
        Assert.False(scenario.IsRunning);
    }

    [Fact]
    public void Begin_restarts_the_profile()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var scenario = new StartScenario(StartProfile.HealthyStart, () => now);

        scenario.Begin();
        now = now.AddSeconds(5);
        Assert.True(scenario.Current.Rpm > 600);

        scenario.Begin();
        Assert.Equal(0, scenario.Current.Rpm);
    }
}
