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

    /// <summary>
    /// Builds a session over channels bound to <paramref name="scanner"/>. The scanner has to be
    /// the same one the loop runs against: each channel owns its own source, so channels built
    /// against a different scanner would silently read from that one instead.
    /// </summary>
    private static (CaptureSession Session, IReadOnlyList<CaptureChannel> Channels) NewSession(
        IObd2Scanner scanner,
        CaptureTrigger? trigger = null)
    {
        var channels = CaptureChannelSet.FromPids(scanner, DieselSet);

        var session = new CaptureSession(new CaptureSessionOptions(
            channels.Signals(),
            trigger ?? new BusWakeTrigger(),
            PreTriggerSeconds: 2,
            ExpectedSampleRateHz: 20,
            MaxDurationSeconds: null));

        return (session, channels);
    }

    [Fact]
    public void A_pid_with_no_descriptor_is_rejected()
    {
        var scanner = new FakeObd2Scanner((_, _) => 0);

        var ex = Assert.Throws<InvalidOperationException>(
            () => CaptureChannelSet.FromPids(scanner, [Pid.FuelSystemStatus]));

        Assert.Contains("PidRegistry", ex.Message);
    }

    [Fact]
    public void Diesel_channels_are_available_in_the_registry()
    {
        var channels = CaptureChannelSet.FromPids(new FakeObd2Scanner((_, _) => 0), DieselSet);

        Assert.Equal(3, channels.Count);
        Assert.Equal(new[] { 0, 1, 2 }, channels.Select(c => c.Signal.Index));

        var rail = channels[1].Signal;
        Assert.Equal("Rail Pressure", rail.Name);
        Assert.Equal("kPa", rail.Unit);

        // Must reach common-rail pressures; the low-side FuelPressure PID caps at 765 kPa.
        Assert.True(rail.Max > 200_000);
    }

    [Fact]
    public async Task Polls_every_bound_signal_and_records_samples()
    {
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((pid, i) =>
        {
            if (i >= 30) cts.Cancel();
            return pid == Pid.EngineRpm ? 200 : 1000;
        });

        var (session, channels) = NewSession(scanner);
        var loop = new CapturePollLoop(scanner, session, channels);
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
        using var cts = new CancellationTokenSource();

        // Every other read fails, but never long enough to count as a dropout.
        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 20) cts.Cancel();
            return i % 2 == 0 ? 100 : null;
        });

        var (session, channels) = NewSession(scanner);
        var loop = new CapturePollLoop(scanner, session, channels);
        await loop.RunAsync(cts.Token);

        Assert.True(session.RecordedSamples.Count < scanner.ReadCount);
        Assert.DoesNotContain(session.Events, e => e.Kind == CaptureEventKind.BusLost);
    }

    [Fact]
    public async Task Sustained_failures_mark_the_bus_lost_then_restored()
    {
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 30) cts.Cancel();

            // Good, then a dropout longer than the tolerance, then recovery — a crank brownout.
            return i is >= 3 and < 15 ? null : 100;
        });

        var (session, channels) = NewSession(scanner);

        var loop = new CapturePollLoop(scanner, session, channels, new CapturePollOptions(
            ReadTimeoutMs: 50,
            ConsecutiveFailuresForBusLost: 6));

        await loop.RunAsync(cts.Token);

        Assert.Contains(session.Events, e => e.Kind == CaptureEventKind.BusLost);
        Assert.Contains(session.Events, e => e.Kind == CaptureEventKind.BusRestored);

        // A dropout must not end the capture.
        Assert.NotEqual(CaptureState.Stopped, session.State);
    }

    [Fact]
    public async Task Arming_waits_for_the_bus_to_come_up()
    {
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 5) cts.Cancel();
            return 100;
        })
        {
            // Dark until the third attempt, as when the key has not been turned yet.
            ConnectResult = attempt => attempt >= 3,
        };

        var (session, channels) = NewSession(scanner);

        var loop = new CapturePollLoop(scanner, session, channels, new CapturePollOptions(
            ConnectRetryDelayMs: 1));

        await loop.RunAsync(cts.Token);

        Assert.Equal(3, scanner.ConnectAttempts);
        Assert.NotEmpty(session.RecordedSamples);
    }

    [Fact]
    public async Task Metadata_reports_the_measured_rate_and_signals()
    {
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 15) cts.Cancel();
            return 100;
        });

        var (session, channels) = NewSession(scanner);
        var loop = new CapturePollLoop(scanner, session, channels);
        await loop.RunAsync(cts.Token);

        var metadata = loop.BuildMetadata("bus wake", "cold crank");

        Assert.Equal("cold crank", metadata.Name);
        Assert.Equal("bus wake", metadata.TriggerDescription);
        Assert.Equal(3, metadata.Signals.Count);
        Assert.True(metadata.AchievedSampleRateHz > 0);
        Assert.Equal(session.TriggerTimestampMs, metadata.TriggerTimestampMs);
    }

    [Fact]
    public async Task Mode22_channels_are_polled_alongside_standard_pids()
    {
        using var cts = new CancellationTokenSource();

        var scanner = new FakeObd2Scanner((_, i) =>
        {
            if (i >= 20) cts.Cancel();
            return 100;
        });

        // 0x1234 returns 0x0BB8 = 3000 raw; at 10 kPa/bit that is 30,000 kPa commanded.
        var uds = new FakeUdsScanner(did => did == 0x1234 ? [0x0B, 0xB8] : null);

        var commanded = new Mode22SignalDefinition
        {
            Key = "CommandedRail",
            Name = "Commanded Rail Pressure",
            Unit = "kPa",
            Did = 0x1234,
            ByteLength = 2,
            Scale = 10,
            Max = 250_000,
        };

        var channels = new CaptureChannelSetBuilder()
            .AddPid(scanner, Pid.EngineRpm)
            .AddMode22(uds, commanded)
            .Build();

        var session = new CaptureSession(new CaptureSessionOptions(
            channels.Signals(), new BusWakeTrigger(), MaxDurationSeconds: null));

        var loop = new CapturePollLoop(scanner, session, channels);
        await loop.RunAsync(cts.Token);

        // Commanded and actual land on one timeline, which is the whole point.
        Assert.Equal(new[] { 0, 1 }, session.RecordedSamples.Select(s => s.SignalIndex).Distinct().OrderBy(i => i));
        Assert.Contains(session.RecordedSamples, s => s.SignalIndex == 1 && s.Value == 30_000);
        Assert.True(uds.ReadCount > 0);
    }

    [Fact]
    public void Mode22_channels_need_a_key()
    {
        var uds = new FakeUdsScanner(_ => null);

        Assert.Throws<InvalidOperationException>(
            () => new CaptureChannelSetBuilder().AddMode22(uds, new Mode22SignalDefinition()));
    }
}
