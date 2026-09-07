using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class CaptureTriggerTests
{
    private static readonly IReadOnlyList<CaptureSignal> Signals = new[]
    {
        new CaptureSignal(0, "rpm",  "Engine RPM",    "RPM", 0, 8000),
        new CaptureSignal(1, "rail", "Rail Pressure", "kPa", 0, 200000),
    };

    private static CaptureSample Sample(int index, long ms, double value) => new(index, ms, value);

    [Fact]
    public void Unknown_signal_key_is_rejected_at_bind_time()
    {
        var trigger = new ThresholdTrigger("nope", ThresholdComparison.Above, 1);

        var ex = Assert.Throws<InvalidOperationException>(() => trigger.Bind(Signals));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Above_threshold_fires_immediately_with_no_dwell()
    {
        var trigger = new ThresholdTrigger("rpm", ThresholdComparison.Above, 150);
        trigger.Bind(Signals);

        Assert.False(trigger.Evaluate(Sample(0, 0, 120)));
        Assert.True(trigger.Evaluate(Sample(0, 100, 200)));
    }

    [Fact]
    public void Other_signals_never_fire_the_trigger()
    {
        var trigger = new ThresholdTrigger("rpm", ThresholdComparison.Above, 150);
        trigger.Bind(Signals);

        // Rail pressure well above the RPM threshold value must be ignored.
        Assert.False(trigger.Evaluate(Sample(1, 0, 50000)));
    }

    [Fact]
    public void Below_threshold_comparison_fires_on_low_values()
    {
        var trigger = new ThresholdTrigger("rail", ThresholdComparison.Below, 1000);
        trigger.Bind(Signals);

        Assert.False(trigger.Evaluate(Sample(1, 0, 5000)));
        Assert.True(trigger.Evaluate(Sample(1, 100, 500)));
    }

    [Fact]
    public void Dwell_requires_the_condition_to_hold_for_the_full_duration()
    {
        var trigger = new ThresholdTrigger("rpm", ThresholdComparison.Above, 150, dwellMs: 500);
        trigger.Bind(Signals);

        Assert.False(trigger.Evaluate(Sample(0, 1000, 200)));   // condition starts
        Assert.False(trigger.Evaluate(Sample(0, 1400, 200)));   // only 400 ms elapsed
        Assert.True(trigger.Evaluate(Sample(0, 1500, 200)));    // 500 ms reached
    }

    [Fact]
    public void Dwell_timer_restarts_when_the_condition_lapses()
    {
        var trigger = new ThresholdTrigger("rpm", ThresholdComparison.Above, 150, dwellMs: 500);
        trigger.Bind(Signals);

        trigger.Evaluate(Sample(0, 1000, 200));                 // starts dwell
        Assert.False(trigger.Evaluate(Sample(0, 1200, 100)));   // drops out, resets
        Assert.False(trigger.Evaluate(Sample(0, 1400, 200)));   // restarts from 1400
        Assert.False(trigger.Evaluate(Sample(0, 1800, 200)));   // only 400 ms
        Assert.True(trigger.Evaluate(Sample(0, 1900, 200)));    // 500 ms from restart
    }

    [Fact]
    public void Manual_trigger_only_fires_once_fired()
    {
        var trigger = new ManualTrigger();

        Assert.False(trigger.Evaluate(Sample(0, 0, 0)));
        trigger.Fire();
        Assert.True(trigger.Evaluate(Sample(0, 100, 0)));

        trigger.Reset();
        Assert.False(trigger.Evaluate(Sample(0, 200, 0)));
    }

    [Fact]
    public void Bus_wake_trigger_fires_on_the_first_sample()
    {
        Assert.True(new BusWakeTrigger().Evaluate(Sample(0, 0, 0)));
    }

    [Fact]
    public void Any_trigger_fires_when_a_member_fires()
    {
        var manual = new ManualTrigger();
        var threshold = new ThresholdTrigger("rpm", ThresholdComparison.Above, 150);
        var any = new AnyTrigger(manual, threshold);
        any.Bind(Signals);

        Assert.False(any.Evaluate(Sample(0, 0, 100)));

        manual.Fire();
        Assert.True(any.Evaluate(Sample(0, 100, 100)));
    }

    [Fact]
    public void Any_trigger_feeds_every_member_so_dwell_state_keeps_advancing()
    {
        var dwell = new ThresholdTrigger("rpm", ThresholdComparison.Above, 150, dwellMs: 500);
        var manual = new ManualTrigger();

        // Manual is listed first; if AnyTrigger short-circuited, the dwell trigger would miss
        // samples and never accumulate its window.
        var any = new AnyTrigger(manual, dwell);
        any.Bind(Signals);

        Assert.False(any.Evaluate(Sample(0, 1000, 200)));
        Assert.True(any.Evaluate(Sample(0, 1500, 200)));
    }
}
