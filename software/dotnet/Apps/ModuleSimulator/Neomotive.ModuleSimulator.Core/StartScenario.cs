namespace Neomotive.ModuleSimulator;

/// <summary>
/// Synthetic start-attempt profiles for bench-testing the ScanTool capture feature.
/// </summary>
/// <remarks>
/// Each profile corresponds to one branch of the hard-start diagnostic procedure, so a capture
/// taken against the simulator can be read exactly the way a capture from the vehicle would be.
/// </remarks>
public enum StartProfile
{
    /// <summary>Rail builds quickly, engine catches and idles. The reference "good crank".</summary>
    HealthyStart,

    /// <summary>
    /// Rail pressure rises slowly and never reaches the injection threshold, so the engine never
    /// fires. Consistent with a weak lift pump, air in the fuel, or a worn high-pressure pump.
    /// </summary>
    WeakLiftPump,

    /// <summary>
    /// Rail reaches pressure, the engine catches, then pressure collapses and it dies. Consistent
    /// with a pressure relief valve unseating or excessive injector leak-off.
    /// </summary>
    RailCollapse,

    /// <summary>
    /// Battery sags hard under load: slow cranking, low rail pressure as a consequence. The
    /// electrical fault that masquerades as a fuel fault.
    /// </summary>
    WeakBattery,

    /// <summary>Starter does not turn the engine at all.</summary>
    NoCrank,
}

/// <summary>One instant of a simulated start attempt.</summary>
public readonly record struct StartSnapshot(double Rpm, double RailPressureKpa, double Volts);

/// <summary>
/// A scripted start attempt, evaluated as a pure function of elapsed time.
/// </summary>
/// <remarks>
/// Time-based rather than tick-based on purpose: the PID handlers are pulled on demand by whatever
/// is polling, so the profile stays smooth at any sample rate and the simulator needs no timer of
/// its own. It also makes the whole thing deterministic and unit-testable.
/// <para>
/// Timeline convention: t=0 is key-on. Cranking begins at <see cref="CrankStartSeconds"/>, leaving
/// a prime phase in front of it so that a capture armed before key-on has something to show in its
/// pre-trigger buffer.
/// </para>
/// </remarks>
public sealed class StartScenario
{
    /// <summary>Delay between key-on and the starter engaging.</summary>
    public const double CrankStartSeconds = 0.5;

    /// <summary>
    /// Rail pressure a 5.0 Cummins needs before the ECU will command injection, roughly 4,000 psi.
    /// Used only for documentation and tests; the profiles encode their own curves.
    /// </summary>
    public const double InjectionThresholdKpa = 28_000;

    private readonly Func<DateTime> _now;
    private DateTime _startedAt;

    public StartScenario(StartProfile profile, Func<DateTime>? clock = null)
    {
        Profile = profile;
        _now = clock ?? (() => DateTime.UtcNow);
        _startedAt = _now();
    }

    public StartProfile Profile { get; }

    public bool IsRunning { get; private set; }

    /// <summary>Restarts the profile from key-on.</summary>
    public void Begin()
    {
        _startedAt = _now();
        IsRunning = true;
    }

    public void Stop() => IsRunning = false;

    public TimeSpan Elapsed => _now() - _startedAt;

    /// <summary>The current point on the profile.</summary>
    public StartSnapshot Current => Sample(Profile, Elapsed.TotalSeconds);

    /// <summary>Evaluates a profile at an arbitrary offset from key-on.</summary>
    public static StartSnapshot Sample(StartProfile profile, double t)
    {
        if (t < 0)
        {
            t = 0;
        }

        return profile switch
        {
            StartProfile.HealthyStart => new StartSnapshot(
                Rpm: Piecewise(t, (0, 0), (0.5, 0), (0.6, 200), (1.5, 200), (1.7, 420), (2.4, 780), (3.2, 720), (5, 720)),
                RailPressureKpa: Piecewise(t, (0, 0), (0.5, 2_000), (1.0, 18_000), (1.4, 30_000), (2.4, 36_000), (5, 35_000)),
                Volts: Piecewise(t, (0, 12.6), (0.5, 12.6), (0.7, 11.2), (2.3, 11.4), (2.8, 13.8), (4, 14.2), (6, 14.2))),

            // Asymptotes well short of the injection threshold, so RPM never leaves cranking speed.
            StartProfile.WeakLiftPump => new StartSnapshot(
                Rpm: Piecewise(t, (0, 0), (0.5, 0), (0.6, 195), (10, 190)),
                RailPressureKpa: Piecewise(t, (0, 0), (0.5, 800), (2, 8_500), (5, 13_000), (10, 14_500)),
                Volts: Piecewise(t, (0, 12.6), (0.5, 12.6), (0.7, 11.0), (10, 10.7))),

            // Catches, then loses rail pressure and dies — the Titan's reported symptom.
            StartProfile.RailCollapse => new StartSnapshot(
                Rpm: Piecewise(t, (0, 0), (0.5, 0), (0.6, 200), (1.5, 200), (1.8, 480), (2.2, 640), (2.6, 520), (3.0, 240), (3.4, 0), (10, 0)),
                RailPressureKpa: Piecewise(t, (0, 0), (0.5, 2_000), (1.0, 19_000), (1.5, 30_500), (2.0, 31_000), (2.4, 17_000), (2.8, 6_000), (3.4, 1_200), (10, 0)),
                Volts: Piecewise(t, (0, 12.6), (0.5, 12.6), (0.7, 11.2), (2.2, 13.2), (3.0, 12.8), (4, 12.4), (10, 12.4))),

            // Voltage sag is the primary fault; low rail pressure is downstream of it.
            StartProfile.WeakBattery => new StartSnapshot(
                Rpm: Piecewise(t, (0, 0), (0.5, 0), (0.8, 115), (10, 95)),
                RailPressureKpa: Piecewise(t, (0, 0), (0.5, 500), (2, 5_500), (6, 8_800), (10, 9_200)),
                Volts: Piecewise(t, (0, 12.1), (0.5, 12.1), (0.9, 9.2), (4, 8.6), (10, 8.3))),

            StartProfile.NoCrank => new StartSnapshot(
                Rpm: 0,
                RailPressureKpa: 0,
                Volts: Piecewise(t, (0, 12.4), (0.5, 12.4), (0.7, 10.2), (1.5, 12.2), (10, 12.3))),

            _ => new StartSnapshot(0, 0, 12.6),
        };
    }

    /// <summary>
    /// Linear interpolation across an ordered set of (time, value) knots, clamped at both ends.
    /// </summary>
    private static double Piecewise(double t, params (double T, double V)[] points)
    {
        if (t <= points[0].T)
        {
            return points[0].V;
        }

        for (var i = 1; i < points.Length; i++)
        {
            if (t > points[i].T)
            {
                continue;
            }

            var (t0, v0) = points[i - 1];
            var (t1, v1) = points[i];
            var span = t1 - t0;

            return span <= 0 ? v1 : v0 + (v1 - v0) * ((t - t0) / span);
        }

        return points[^1].V;
    }
}
