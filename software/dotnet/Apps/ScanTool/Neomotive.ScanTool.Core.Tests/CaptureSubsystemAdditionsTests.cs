using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neomotive.ScanTool.Core;
using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class CaptureSubsystemAdditionsTests
{
    private static readonly IReadOnlyList<CaptureSignal> Signals = new[]
    {
        new CaptureSignal(0, "rpm", "Engine RPM", "RPM", 0, 8000),
        new CaptureSignal(1, "rail", "Rail Pressure", "kPa", 0, 200000),
    };

    [Fact]
    public async Task UdsSessionKeepAlive_sends_periodic_tester_presents()
    {
        var fakeUds = new FakeUdsScanner(_ => null);
        var keepAlive = new UdsSessionKeepAlive(fakeUds, [0x7E0, 0x7E1], TimeSpan.FromMilliseconds(50));

        Assert.True(keepAlive.IsNeeded);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(180));
        await keepAlive.RunAsync(cts.Token);

        Assert.Contains((ushort)0x7E0, fakeUds.TesterPresentTxIds);
        Assert.Contains((ushort)0x7E1, fakeUds.TesterPresentTxIds);
        Assert.True(fakeUds.TesterPresentTxIds.Count >= 2);
    }

    [Fact]
    public async Task DtcPollingChannel_detects_new_and_cleared_dtcs_and_injects_events()
    {
        var currentDtcs = new List<DiagnosticTroubleCode>();
        var fakeObd = new FakeObd2Scanner((pid, idx) => 0)
        {
            StoredDtcsFunc = () => currentDtcs.ToArray(),
        };

        var trigger = new ManualTrigger();
        var session = new CaptureSession(new CaptureSessionOptions(Signals, trigger));
        session.Arm(0);

        long clockMs = 1000;
        var dtcChannel = new DtcPollingChannel(fakeObd, session, () => clockMs, TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(220));
        var task = dtcChannel.RunAsync(cts.Token);

        // After baseline read, simulate a DTC being set:
        await Task.Delay(40);
        currentDtcs.Add(new DiagnosticTroubleCode("P0087", "Fuel Rail/System Pressure - Too Low", DtcStatus.Stored, DtcType.Generic));
        clockMs = 1500;

        await Task.Delay(80);
        // Then simulate DTC clearing:
        currentDtcs.Clear();
        clockMs = 2000;

        await Task.Delay(80);
        await task;

        var dtcSetEvent = session.Events.FirstOrDefault(e => e.Kind == CaptureEventKind.DtcSet);
        Assert.NotNull(dtcSetEvent);
        Assert.Equal("P0087", dtcSetEvent.Detail);

        var dtcClearedEvent = session.Events.FirstOrDefault(e => e.Kind == CaptureEventKind.DtcCleared);
        Assert.NotNull(dtcClearedEvent);
        Assert.Equal("P0087", dtcClearedEvent.Detail);
    }
}
