namespace Neomotive.ScanTool.Core;

/// <summary>
/// ISO 15765-2 (ISO-TP) frame type nibble values (upper nibble of the first payload byte).
/// </summary>
internal enum IsoTpFrameType : byte
{
    Single      = 0,
    First       = 1,
    Consecutive = 2,
    FlowControl = 3,
}
