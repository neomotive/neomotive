using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meadow.Foundation.Telematics.Uds;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Sends a periodic TesterPresent (Service $3E) to each targeted UDS module while a capture is
/// running. Without this, an extended diagnostic session times out after the ECU's P2Server
/// window (commonly 5 s), causing Mode $22 reads to start returning null mid-capture.
/// </summary>
/// <remarks>
/// TesterPresent with suppressResponse = true is a write-only operation — it does not subscribe
/// to the FrameReceived handler and therefore does not conflict with the concurrent PID sweep
/// in <see cref="CapturePollLoop"/>.
/// </remarks>
public sealed class UdsSessionKeepAlive
{
    private readonly IUdsScanner _scanner;
    private readonly IReadOnlyList<ushort> _txIds;
    private readonly TimeSpan _interval;

    /// <param name="scanner">The UDS scanner to send TesterPresent through.</param>
    /// <param name="txIds">The transmit CAN IDs of every module that was opened in an extended session.</param>
    /// <param name="interval">How often to send. Defaults to 2.5 s, which is safely below the typical 5 s P2Server timeout.</param>
    public UdsSessionKeepAlive(
        IUdsScanner scanner,
        IEnumerable<ushort> txIds,
        TimeSpan? interval = null)
    {
        _scanner = scanner;
        _txIds = txIds.Distinct().ToArray();
        _interval = interval ?? TimeSpan.FromSeconds(2.5);
    }

    /// <summary>
    /// Returns true if there are any modules to keep alive. When false, starting the task
    /// is a no-op and the caller can skip it.
    /// </summary>
    public bool IsNeeded => _txIds.Count > 0;

    /// <summary>Runs until <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
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

            foreach (var txId in _txIds)
            {
                if (ct.IsCancellationRequested)
                    return;

                try
                {
                    // suppressResponse = true: no response is expected. The frame is simply sent.
                    // This is the standard keepalive pattern and does not interfere with the
                    // concurrent poll loop.
                    await _scanner.SendTesterPresentAsync(txId, suppressResponse: true, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // A transient bus failure during a keepalive does not end the capture.
                    // CapturePollLoop handles bus-lost detection independently.
                }
            }
        }
    }
}
