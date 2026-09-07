namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// A single channel being recorded. Deliberately decoupled from <see cref="PidDescriptor"/> so
/// that a Mode 22 / UDS ReadDataByIdentifier channel can be described identically to a Mode 01 PID.
/// </summary>
/// <param name="Index">Dense index into the session's signal list. Samples carry this rather than
/// a string so the rolling buffer stays allocation-free.</param>
/// <param name="Key">Stable identifier used in the CSV and in trigger configuration.</param>
public record CaptureSignal(
    int Index,
    string Key,
    string Name,
    string Unit,
    double Min,
    double Max);
