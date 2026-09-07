using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class CaptureSessionTests
{
    private const int Rpm = 0;
    private const int Rail = 1;

    private static readonly IReadOnlyList<CaptureSignal> Signals = new[]
    {
        new CaptureSignal(Rpm,  "rpm",  "Engine RPM",    "RPM", 0, 8000),
        new CaptureSignal(Rail, "rail", "Rail Pressure", "kPa", 0, 200000),
    };

    private static CaptureSession NewSession(
        CaptureTrigger? trigger = null,
        double preTriggerSeconds = 2,
        double? maxDurationSeconds = 120,
        ActivityStopCondition? stall = null)
        => new(new CaptureSessionOptions(
            Signals,
            trigger ?? new ThresholdTrigger("rpm", ThresholdComparison.Above, 150),
            preTriggerSeconds,
            ExpectedSampleRateHz: 10,
            maxDurationSeconds,
            stall));

    [Fact]
    public void A_capture_needs_at_least_one_signal()
    {
        Assert.Throws<ArgumentException>(() => new CaptureSession(
            new CaptureSessionOptions(Array.Empty<CaptureSignal>(), new ManualTrigger())));
    }

    [Fact]
    public void Samples_are_discarded_until_armed()
    {
        var session = NewSession();
        session.Ingest(new CaptureSample(Rpm, 0, 900));

        Assert.Equal(CaptureState.Idle, session.State);
        Assert.Empty(session.RecordedSamples);
    }

    [Fact]
    public void Arming_moves_to_buffering_and_records_an_event()
    {
        var session = NewSession();
        session.Arm();

        Assert.Equal(CaptureState.Buffering, session.State);
        Assert.Equal(CaptureEventKind.Armed, Assert.Single(session.Events).Kind);
    }

    [Fact]
    public void Pre_trigger_history_is_retained_and_flushed_in_order()
    {
        var session = NewSession(preTriggerSeconds: 2);
        session.Arm();

        // Key-on prime phase: RPM still zero, rail pressure building.
        for (var i = 0; i < 5; i++)
        {
            session.Ingest(new CaptureSample(Rail, i * 100, 1000 * i));
            session.Ingest(new CaptureSample(Rpm, i * 100 + 50, 0));
        }

        Assert.Empty(session.RecordedSamples);

        session.Ingest(new CaptureSample(Rpm, 1000, 200)); // cranking -> trigger

        Assert.Equal(CaptureState.Recording, session.State);

        var recorded = session.RecordedSamples;

        // 10 buffered pre-trigger samples plus the sample that fired the trigger.
        Assert.Equal(11, recorded.Count);
        Assert.Equal(0, recorded[0].TimestampMs);
        Assert.Equal(1000, recorded[^1].TimestampMs);

        // Chronological, and the pre-trigger fuel-rail history survived.
        Assert.True(recorded.Zip(recorded.Skip(1)).All(p => p.First.TimestampMs <= p.Second.TimestampMs));
        Assert.Contains(recorded, s => s.SignalIndex == Rail && s.Value == 4000);
    }

    [Fact]
    public void Trigger_timestamp_is_exposed_for_time_zero()
    {
        var session = NewSession();
        session.Arm();
        session.Ingest(new CaptureSample(Rpm, 700, 200));

        Assert.Equal(700, session.TriggerTimestampMs);
    }

    [Fact]
    public void History_older_than_the_pre_trigger_window_ages_out()
    {
        // 1 s window at 10 Hz across 2 signals = 20 slots.
        var session = NewSession(preTriggerSeconds: 1);
        session.Arm();

        for (var i = 0; i < 60; i++)
        {
            session.Ingest(new CaptureSample(Rail, i * 10, i));
        }

        session.Ingest(new CaptureSample(Rpm, 1000, 200));

        // The window holds 20 slots and the triggering sample occupies one of them, so the
        // capture opens with 19 samples of pre-trigger history plus the trigger itself.
        Assert.Equal(20, session.RecordedSamples.Count);
        Assert.Equal(1000, session.RecordedSamples[^1].TimestampMs);
    }

    [Fact]
    public void Samples_after_the_trigger_are_recorded()
    {
        var session = NewSession(preTriggerSeconds: 1);
        session.Arm();
        session.Ingest(new CaptureSample(Rpm, 0, 200));

        session.Ingest(new CaptureSample(Rail, 100, 30000));
        session.Ingest(new CaptureSample(Rail, 200, 40000));

        Assert.Equal(3, session.RecordedSamples.Count);
    }

    [Fact]
    public void Stall_detection_stops_the_capture()
    {
        var session = NewSession(
            trigger: new ThresholdTrigger("rpm", ThresholdComparison.Above, 150),
            stall: new ActivityStopCondition("rpm", 50, 1000));

        session.Arm();
        session.Ingest(new CaptureSample(Rpm, 0, 200));      // trigger: cranking
        session.Ingest(new CaptureSample(Rpm, 500, 900));    // caught
        session.Ingest(new CaptureSample(Rpm, 1500, 0));     // died
        Assert.Equal(CaptureState.Recording, session.State);

        session.Ingest(new CaptureSample(Rpm, 2500, 0));     // stall duration reached

        Assert.Equal(CaptureState.Stopped, session.State);
        Assert.Contains(session.Events, e => e.Kind == CaptureEventKind.StopConditionMet);
    }

    [Fact]
    public void Max_duration_stops_the_capture()
    {
        var session = NewSession(maxDurationSeconds: 1);
        session.Arm();
        session.Ingest(new CaptureSample(Rpm, 0, 200));      // trigger
        session.Ingest(new CaptureSample(Rpm, 500, 800));
        Assert.Equal(CaptureState.Recording, session.State);

        session.Ingest(new CaptureSample(Rpm, 1000, 800));

        Assert.Equal(CaptureState.Stopped, session.State);
        Assert.Contains(session.Events, e => e.Kind == CaptureEventKind.DurationReached);
    }

    [Fact]
    public void Samples_after_stop_are_discarded()
    {
        var session = NewSession(maxDurationSeconds: 1);
        session.Arm();
        session.Ingest(new CaptureSample(Rpm, 0, 200));
        session.Ingest(new CaptureSample(Rpm, 1000, 800));   // stops here

        var count = session.RecordedSamples.Count;
        session.Ingest(new CaptureSample(Rpm, 1100, 800));

        Assert.Equal(count, session.RecordedSamples.Count);
    }

    [Fact]
    public void Bus_loss_is_marked_without_ending_the_capture()
    {
        var session = NewSession();
        session.Arm();
        session.Ingest(new CaptureSample(Rpm, 0, 200));

        session.MarkBusLost(500);
        session.MarkBusLost(600);       // repeat is ignored while still lost
        session.MarkBusRestored(900);

        Assert.Equal(CaptureState.Recording, session.State);
        Assert.Single(session.Events, e => e.Kind == CaptureEventKind.BusLost);
        Assert.Single(session.Events, e => e.Kind == CaptureEventKind.BusRestored);
    }

    [Fact]
    public void Re_arming_clears_the_previous_capture()
    {
        var session = NewSession();
        session.Arm();
        session.Ingest(new CaptureSample(Rpm, 0, 200));
        session.Stop(100);

        session.Arm();

        Assert.Equal(CaptureState.Buffering, session.State);
        Assert.Empty(session.RecordedSamples);
        Assert.Equal(-1, session.TriggerTimestampMs);
        Assert.Equal(CaptureEventKind.Armed, Assert.Single(session.Events).Kind);
    }

    [Fact]
    public void Bus_wake_trigger_starts_recording_on_the_first_sample()
    {
        var session = NewSession(trigger: new BusWakeTrigger());
        session.Arm();
        session.Ingest(new CaptureSample(Rail, 0, 500));

        Assert.Equal(CaptureState.Recording, session.State);
        Assert.Single(session.RecordedSamples);
    }
}
