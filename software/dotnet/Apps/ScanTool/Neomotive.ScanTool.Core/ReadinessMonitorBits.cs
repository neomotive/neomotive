namespace Neomotive.ScanTool.Core;

/// <summary>
/// SAE J1979 Mode $01 PID $01 bit positions for readiness monitor status.
/// Byte B covers continuous monitors; bytes C and D cover non-continuous monitors.
/// In bytes C/D, each bit position corresponds to the same monitor (C=supported, D=incomplete).
/// </summary>
internal static class ReadinessMonitorBits
{
    // ── Byte B — continuous monitors ────────────────────────────────────────
    internal const byte MisfireSupported        = 0x01;
    internal const byte FuelSystemSupported     = 0x02;
    internal const byte ComprehensiveSupported  = 0x04;

    internal const byte MisfireIncomplete       = 0x10;
    internal const byte FuelSystemIncomplete    = 0x20;
    internal const byte ComprehensiveIncomplete = 0x40;

    // ── Bytes C / D — non-continuous monitors (same bit per byte) ───────────
    internal const byte CatalystBit            = 0x01;
    internal const byte HeatedCatalystBit      = 0x02;
    internal const byte EvapSystemBit          = 0x04;
    internal const byte SecondaryAirBit        = 0x08;
    internal const byte AcRefrigerantBit       = 0x10;
    internal const byte OxygenSensorBit        = 0x20;
    internal const byte OxygenSensorHeaterBit  = 0x40;
    internal const byte EgrSystemBit           = 0x80;
}
