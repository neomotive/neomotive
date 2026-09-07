namespace Neomotive.ScanTool.Core.Capture;

/// <param name="ReadTimeoutMs">Per-PID deadline. The scanner's own 3 s timeout is far too long
/// here: one unanswered request would blow a three-second hole in the trace. A miss is recorded as
/// a gap and the sweep moves on.</param>
/// <param name="ConsecutiveFailuresForBusLost">Failed reads in a row before declaring the bus
/// down. Cranking browns out the ECU, so a couple of misses is normal and must not be reported.</param>
/// <param name="ConnectRetryDelayMs">Gap between connect attempts while waiting for key-on.</param>
public record CapturePollOptions(
    int ReadTimeoutMs = 150,
    int ConsecutiveFailuresForBusLost = 6,
    int ConnectRetryDelayMs = 250);
