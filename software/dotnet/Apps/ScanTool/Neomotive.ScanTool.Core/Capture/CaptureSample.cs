namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// One measurement of one signal. Timestamps are milliseconds since the session epoch (recorded
/// as an absolute time in the sidecar), and each sample keeps the instant it was actually read —
/// OBD polling is round-robin, so signals never share a timestamp.
/// </summary>
public readonly record struct CaptureSample(int SignalIndex, long TimestampMs, double Value);
