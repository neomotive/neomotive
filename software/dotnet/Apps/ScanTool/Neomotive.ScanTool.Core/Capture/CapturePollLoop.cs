using System.Diagnostics;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Drives a <see cref="CaptureSession"/> by polling the bound PIDs back to back.
/// </summary>
/// <remarks>
/// Distinct from the live-data loop, which sleeps 500 ms between sweeps for a calm on-screen
/// readout. A hard-start event lasts two or three seconds, so at that rate it would be described by
/// about four samples. This loop removes the delay and polls only the armed signals, leaving ECU
/// response latency as the limit — roughly 10-30 ms per PID, so five signals land near 10 Hz.
/// </remarks>
public sealed class CapturePollLoop
{
    private readonly IObd2Scanner _scanner;
    private readonly CaptureSession _session;
    private readonly IReadOnlyList<CapturePidBinding> _bindings;
    private readonly CapturePollOptions _options;
    private readonly Stopwatch _clock = new();

    private int _consecutiveFailures;
    private long _sampleCount;

    public CapturePollLoop(
        IObd2Scanner scanner,
        CaptureSession session,
        IReadOnlyList<CapturePidBinding> bindings,
        CapturePollOptions? options = null)
    {
        _scanner = scanner;
        _session = session;
        _bindings = bindings;
        _options = options ?? new CapturePollOptions();

        if (bindings.Count == 0)
        {
            throw new ArgumentException("Nothing to poll.", nameof(bindings));
        }
    }

    /// <summary>Per-signal rate actually achieved. Measured, not assumed.</summary>
    public double AchievedSampleRateHz
    {
        get
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            return seconds <= 0 ? 0 : _sampleCount / seconds / _bindings.Count;
        }
    }

    public long ElapsedMs => _clock.ElapsedMilliseconds;

    /// <summary>
    /// Waits for the vehicle to answer, arms the session, then polls until the capture stops or the
    /// caller cancels.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _clock.Restart();
        _sampleCount = 0;
        _consecutiveFailures = 0;

        await WaitForBusAsync(ct);

        _session.Arm(_clock.ElapsedMilliseconds);

        while (!ct.IsCancellationRequested && _session.State != CaptureState.Stopped)
        {
            foreach (var binding in _bindings)
            {
                if (ct.IsCancellationRequested || _session.State == CaptureState.Stopped)
                {
                    break;
                }

                await PollOnceAsync(binding, ct);
            }
        }

        _clock.Stop();
    }

    private async Task PollOnceAsync(CapturePidBinding binding, CancellationToken ct)
    {
        var value = await ReadWithDeadlineAsync(binding, ct);

        // Stamp on arrival, not on dispatch: round-robin polling means each signal has its own
        // instant, and preserving that is the point of the long-format capture.
        var timestampMs = _clock.ElapsedMilliseconds;

        if (value is null)
        {
            _consecutiveFailures++;

            if (_consecutiveFailures == _options.ConsecutiveFailuresForBusLost)
            {
                _session.MarkBusLost(timestampMs);
            }

            return;
        }

        if (_consecutiveFailures >= _options.ConsecutiveFailuresForBusLost)
        {
            _session.MarkBusRestored(timestampMs);
        }

        _consecutiveFailures = 0;
        _sampleCount++;

        _session.Ingest(new CaptureSample(binding.Signal.Index, timestampMs, value.Value));
    }

    private async Task<double?> ReadWithDeadlineAsync(CapturePidBinding binding, CancellationToken ct)
    {
        // The scanner's internal timeout is fixed at 3 s, so the deadline is imposed here instead.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_options.ReadTimeoutMs);

        try
        {
            var result = await _scanner.ReadPidAsync(binding.Pid, deadline.Token);
            return result?.Value;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Missed this one; treated as a gap rather than an error.
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task WaitForBusAsync(CancellationToken ct)
    {
        // On a hard-start vehicle the ECU may be dark until the key is turned. Retrying here lets
        // the operator arm the tool first and capture the key-on prime phase.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await _scanner.ConnectAsync(ct))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Bus not up yet.
            }

            await Task.Delay(_options.ConnectRetryDelayMs, ct);
        }
    }

    /// <summary>Metadata describing this run, for the sidecar.</summary>
    public CaptureMetadata BuildMetadata(string? triggerDescription = null, string? name = null)
        => new()
        {
            Name = name,
            StartedUtc = _session.StartedUtc ?? DateTime.UtcNow,
            Signals = _bindings.Signals(),
            TriggerDescription = triggerDescription,
            TriggerTimestampMs = _session.TriggerTimestampMs,
            AchievedSampleRateHz = AchievedSampleRateHz,
            DurationMs = _clock.ElapsedMilliseconds,
            Events = _session.Events,
        };
}
