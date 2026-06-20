namespace Neomotive.ScanTool.Core;

/// <summary>
/// ISO 15765-4 CAN addressing constants for OBD2 functional/physical communication.
/// </summary>
internal static class Obd2Addresses
{
    /// <summary>Functional broadcast address — requests go here to reach all ECUs.</summary>
    internal const short FunctionalRequest = 0x7DF;

    /// <summary>First ECU positive-response ID (primary PCM).</summary>
    internal const short EcuResponseBase = 0x7E8;

    /// <summary>Last ECU positive-response ID in the standard range.</summary>
    internal const short EcuResponseMax = 0x7EF;

    /// <summary>
    /// Offset from ECU response ID to its physical (tester-to-ECU) address.
    /// Physical = Response - EcuPhysicalOffset  (e.g. 0x7E8 → 0x7E0).
    /// </summary>
    internal const short EcuPhysicalOffset = 8;

    /// <summary>
    /// OBD2 positive-response service offset.
    /// The ECU echoes the request service byte ORed with 0x40  (e.g. $03 → $43).
    /// </summary>
    internal const byte ResponseOffset = 0x40;

    /// <summary>
    /// Bits 5:4 of a DTC high-byte select the sub-type.
    /// Zero means generic (SAE); any other value means manufacturer-specific.
    /// </summary>
    internal const byte DtcManufacturerMask = 0x30;

    /// <summary>Mask to extract the two-bit DTC category from the high byte (bits 7:6).</summary>
    internal const byte DtcCategoryMask = 0xC0;
}
