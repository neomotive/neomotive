using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Polls stored DTCs on a slow cadence during a capture and injects
/// <see cref="CaptureEventKind.DtcSet"/> / <see cref="CaptureEventKind.DtcCleared"/> events
/// into the session whenever the DTC set changes.
/// </summary>
/// <remarks>
/// Runs as a background task alongside <see cref="CapturePollLoop"/>. It deliberately uses the
/// same <see cref="IObd2Scanner"/> as the poll loop, which is safe because OBD-II requests are
/// sequential on the CAN bus — the poll loop owns the bus frame by frame, and this task waits
/// between polls. A longer <paramref name="interval"/> keeps contention to a minimum.
/// </remarks>
public sealed class DtcPollingChannel
{
    private readonly IObd2Scanner _scanner;
    private readonly CaptureSession _session;
    private readonly Func<long> _clockMs;
    private readonly TimeSpan _interval;

    /// <param name="scanner">OBD-II scanner to read DTCs from.</param>
    /// <param name="session">Session to inject events into.</param>
    /// <param name="clockMs">Returns the current session-relative timestamp in milliseconds (from CapturePollLoop.ElapsedMs).</param>
    /// <param name="interval">How often to re-read DTCs. Defaults to 5 s.</param>
    public DtcPollingChannel(
        IObd2Scanner scanner,
        CaptureSession session,
        Func<long> clockMs,
        TimeSpan? interval = null)
    {
        _scanner = scanner;
        _session = session;
        _clockMs = clockMs;
        _interval = interval ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Runs until <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // Take a baseline snapshot before the first poll interval.
        var baseline = await ReadDtcCodesAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested)
                return;

            IReadOnlySet<string> current;

            try
            {
                current = await ReadDtcCodesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Transient read failure: keep baseline unchanged, retry next interval.
                continue;
            }

            var ts = _clockMs();

            // Inject an event for each code that appeared since the last read.
            foreach (var code in current.Except(baseline))
            {
                _session.InjectEvent(new CaptureEvent(CaptureEventKind.DtcSet, ts, code));
            }

            // Inject an event for each code that cleared since the last read.
            foreach (var code in baseline.Except(current))
            {
                _session.InjectEvent(new CaptureEvent(CaptureEventKind.DtcCleared, ts, code));
            }

            baseline = current;
        }
    }

    private async Task<IReadOnlySet<string>> ReadDtcCodesAsync(CancellationToken ct)
    {
        try
        {
            var dtcs = await _scanner.ReadStoredDtcsAsync(ct);
            return new HashSet<string>(dtcs.Select(d => d.Code), StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
