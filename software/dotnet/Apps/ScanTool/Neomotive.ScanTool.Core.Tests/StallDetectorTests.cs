using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class StallDetectorTests
{
    private static readonly IReadOnlyList<CaptureSignal> Signals = new[]
    {
        new CaptureSignal(0, "rpm",  "Engine RPM",    "RPM", 0, 8000),
        new CaptureSignal(1, "rail", "Rail Pressure", "kPa", 0, 200000),
    };

    private static StallDetector Bound(double floor = 50, int durationMs = 1000)
    {
        var detector = new StallDetector("rpm", floor, durationMs);
        detector.Bind(Signals);
        return detector;
    }

    [Fact]
    public void Unknown_signal_key_is_rejected_at_bind_time()
    {
        Assert.Throws<InvalidOperationException>(
            () => new StallDetector("nope", 50, 1000).Bind(Signals));
    }

    [Fact]
    public void Never_stalls_if_the_engine_never_rose_above_the_floor()
    {
        var detector = Bound();

        // A capture armed during cranking begins below the floor. Without the "must rise first"
        // rule this would report a stall instantly and truncate the recording.
        Assert.False(detector.Evaluate(new CaptureSample(0, 0, 0)));
        Assert.False(detector.Evaluate(new CaptureSample(0, 5000, 0)));
        Assert.False(detector.Evaluate(new CaptureSample(0, 60000, 0)));
    }

    [Fact]
    public void Stalls_after_the_duration_once_it_has_run()
    {
        var detector = Bound(durationMs: 1000);

        Assert.False(detector.Evaluate(new CaptureSample(0, 1000, 800)));   // caught
        Assert.False(detector.Evaluate(new CaptureSample(0, 2000, 0)));     // died, timer starts
        Assert.False(detector.Evaluate(new CaptureSample(0, 2900, 0)));     // 900 ms
        Assert.True(detector.Evaluate(new CaptureSample(0, 3000, 0)));      // 1000 ms reached
    }

    [Fact]
    public void Recovery_above_the_floor_cancels_a_pending_stall()
    {
        var detector = Bound(durationMs: 1000);

        detector.Evaluate(new CaptureSample(0, 1000, 800));
        detector.Evaluate(new CaptureSample(0, 2000, 0));                   // timer starts
        Assert.False(detector.Evaluate(new CaptureSample(0, 2500, 900)));   // stumbled, recovered
        Assert.False(detector.Evaluate(new CaptureSample(0, 3000, 0)));     // timer restarts here
        Assert.False(detector.Evaluate(new CaptureSample(0, 3900, 0)));
        Assert.True(detector.Evaluate(new CaptureSample(0, 4000, 0)));
    }

    [Fact]
    public void Other_signals_are_ignored()
    {
        var detector = Bound();

        detector.Evaluate(new CaptureSample(0, 1000, 800));
        Assert.False(detector.Evaluate(new CaptureSample(1, 9000, 0)));
    }

    [Fact]
    public void Reset_clears_the_has_run_state()
    {
        var detector = Bound(durationMs: 1000);

        detector.Evaluate(new CaptureSample(0, 1000, 800));
        detector.Reset();

        Assert.False(detector.Evaluate(new CaptureSample(0, 2000, 0)));
        Assert.False(detector.Evaluate(new CaptureSample(0, 9000, 0)));
    }
}
