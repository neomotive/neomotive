using System.Text.Json.Serialization;

namespace Neomotive.ScanTool.Core.Vehicles;

/// <summary>How a module is addressed on the bus.</summary>
public enum ModuleAddressing
{
    /// <summary>11-bit standard CAN identifiers.</summary>
    Std11,

    /// <summary>29-bit extended identifiers, ISO 15765-2 normal-fixed addressing.</summary>
    Ext29
}

/// <summary>What is known about a module from previous visits.</summary>
/// <remarks>
/// Addresses are stored as raw integers plus an explicit addressing mode, never as hex strings.
/// <c>UdsModuleInfo.TxIdHex</c> formats <c>:X3</c>, which silently truncates a 29-bit identifier to
/// three nibbles — a display bug that must not become a storage bug, because a truncated address
/// on disk is indistinguishable from a real 11-bit one.
/// </remarks>
public sealed class RememberedModule
{
    public uint TxId { get; set; }

    public uint RxId { get; set; }

    public ModuleAddressing Addressing { get; set; } = ModuleAddressing.Std11;

    public string Name { get; set; } = "";

    public string? EcuName { get; set; }

    public string? PartNumber { get; set; }

    public string? SoftwareVersion { get; set; }

    public string? HardwareNumber { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    /// <summary>How many scans have found this module.</summary>
    public int SeenCount { get; set; }

    /// <summary>
    /// How many full sweeps in a row have failed to find it. Reset by any sighting. A miss is never
    /// a reason to delete the module — a bus can be unhappy, a module can be asleep — so this only
    /// ever decides whether the address is still worth pre-populating.
    /// </summary>
    public int ConsecutiveMisses { get; set; }

    /// <summary>Distinct VINs this module has been seen on. Only meaningful on a class profile.</summary>
    public int VinsSeenOn { get; set; }

    /// <summary>
    /// Misses in a row before the module stops being offered up front. Three full sweeps is enough
    /// to distinguish a module that has been removed from one that missed a single scan.
    /// </summary>
    public const int MissesBeforeDemotion = 3;

    /// <summary>
    /// Whether this module should still be pre-populated on connect. Demotion needs both a run of
    /// misses and a losing overall record, so a module seen on ten visits survives a bad day.
    /// </summary>
    [JsonIgnore]
    public bool ShouldPrepopulate =>
        ConsecutiveMisses < MissesBeforeDemotion || ConsecutiveMisses <= SeenCount;

    [JsonIgnore]
    public string AddressSummary => Addressing == ModuleAddressing.Ext29
        ? $"TX: 0x{TxId:X8} → RX: 0x{RxId:X8}"
        : $"TX: 0x{TxId:X3} → RX: 0x{RxId:X3}";

    /// <summary>Records a sighting on this visit.</summary>
    public void MarkSeen(DateTime nowUtc)
    {
        if (SeenCount == 0) FirstSeenUtc = nowUtc;
        SeenCount++;
        ConsecutiveMisses = 0;
        LastSeenUtc = nowUtc;
    }

    /// <summary>Records that a full sweep did not find it.</summary>
    public void MarkMissed() => ConsecutiveMisses++;
}
