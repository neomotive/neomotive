using Meadow.Foundation.Telematics.J1979;
using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class CapturePollLoopTests
{
    private static readonly Pid[] DieselSet =
    [
        Pid.EngineRpm,
        Pid.FuelRailGaugePressure,
        Pid.ControlModuleVoltage,
    ];

    private static (CaptureSession Session, IReadOnlyList<CapturePidBinding> Bindings) NewSession(
        CaptureTrigger? trigger = null)
    {
        var bindings = CaptureSignalSet.FromPids(DieselSet);

        var session = new CaptureSession(new CaptureSessionOptions(
            bindings.Signals(),
            trigger ?? new BusWakeTrigger(),
            PreTriggerSeconds: 2,
            ExpectedSampleRateHz: 20,
            MaxDurationSeconds: null));

        return (session, bindings);
    }

    [Fact]
    public void Binding_a_pid_with_no_descriptor_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CaptureSignalSet.FromPids([Pid.FuelSystemStatus]));

        Assert.Contains("PidRegistry", ex.Message);
    }

    [Fact]
    public void Diesel_channels_are_available_in_the_registry()
    {
        var bindings = CaptureSignalSet.FromPids(DieselSet);

        Assert.Equal(3, bindings.Count);
        Assert.Equal(new[] { 0, 1, 2 }, bindings.Select(b => b.Signal.Index));

        var rail = bindings[1].Signal;
        Assert.Equal("Rail Pressure", rail.Name);
        Assert.Equal("kPa", rail.Unit);

        // Must reach common-rail pressures; the low-side FuelPressure PID caps at 765 kPa.
        Assert.True(rail.Max > 200_000);
    }

    [Fact]
    public async Task Polls_every_bound_signal_and_records_samples()
    {
        var (session, bindings) = NewSession();
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((pid, i) =>
        {
            if (i >= 30)
            {
                cts.Cancel();
            }

            return pid == Pid.EngineRpm ? 200 : 1000;
        });

        var loop = new CapturePollLoop(scanner, session, bindings);
        await loop.RunAsync(cts.Token);

        Assert.Equal(CaptureState.Recording, session.State);

        // Every signal must appear, proving the sweep is round-robin rather than stuck on one PID.
        var indices = session.RecordedSamples.Select(s => s.SignalIndex).Distinct().OrderBy(i => i);
        Assert.Equal(new[] { 0, 1, 2 }, indices);
        Assert.True(loop.AchievedSampleRateHz > 0);
    }

    [Fact]
    public async Task Failed_reads_become_gaps_rather_than_samples()
    {
        var (session, bindings) = NewSession();
        using var cts = new CancellationTokenSource();

        // Every other read fails, but never long enough to count as a dropout.
        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 20)
            {
                cts.Cancel();
            }

            return i % 2 == 0 ? 100 : null;
        });

        var loop = new CapturePollLoop(scanner, session, bindings);
        await loop.RunAsync(cts.Token);

        Assert.True(session.RecordedSamples.Count < scanner.ReadCount);
        Assert.DoesNotContain(session.Events, e => e.Kind == CaptureEventKind.BusLost);
    }

    [Fact]
    public async Task Sustained_failures_mark_the_bus_lost_then_restored()
    {
        var (session, bindings) = NewSession();
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 30)
            {
                cts.Cancel();
            }

            // Good, then a dropout longer than the tolerance, then recovery — a crank brownout.
            return i is >= 3 and < 15 ? null : 100;
        });

        var loop = new CapturePollLoop(scanner, session, bindings, new CapturePollOptions(
            ReadTimeoutMs: 50,
            ConsecutiveFailuresForBusLost: 6));

        await loop.RunAsync(cts.Token);

        Assert.Contains(session.Events, e => e.Kind == CaptureEventKind.BusLost);
        Assert.Contains(session.Events, e => e.Kind == CaptureEventKind.BusRestored);

        // The dropout must not end the capture.
        Assert.NotEqual(CaptureState.Stopped, session.State);
    }

    [Fact]
    public async Task Arming_waits_for_the_bus_to_come_up()
    {
        var (session, bindings) = NewSession();
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 5)
            {
                cts.Cancel();
            }

            return 100;
        })
        {
            // Dark until the third attempt, as when the key has not been turned yet.
            ConnectResult = attempt => attempt >= 3,
        };

        var loop = new CapturePollLoop(scanner, session, bindings, new CapturePollOptions(
            ConnectRetryDelayMs: 1));

        await loop.RunAsync(cts.Token);

        Assert.Equal(3, scanner.ConnectAttempts);
        Assert.NotEmpty(session.RecordedSamples);
    }

    [Fact]
    public async Task Metadata_reports_the_measured_rate_and_signals()
    {
        var (session, bindings) = NewSession();
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 15)
            {
                cts.Cancel();
            }

            return 100;
        });

        var loop = new CapturePollLoop(scanner, session, bindings);
        await loop.RunAsync(cts.Token);

        var metadata = loop.BuildMetadata("bus wake", "cold crank");

        Assert.Equal("cold crank", metadata.Name);
        Assert.Equal("bus wake", metadata.TriggerDescription);
        Assert.Equal(3, metadata.Signals.Count);
        Assert.True(metadata.AchievedSampleRateHz > 0);
        Assert.Equal(session.TriggerTimestampMs, metadata.TriggerTimestampMs);
    }
}
