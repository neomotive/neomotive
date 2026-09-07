using System.Collections.Generic;

namespace Neomotive.ScanTool.Core;

/// <summary>
/// Represents a UDS diagnostic trouble code with 3-byte representation (base code + fault type byte)
/// and ISO 14229-1 status mask.
/// </summary>
public record UdsDtc(
    string BaseCode,
    byte FaultType,
    string FaultTypeDescription,
    string FullCode,
    string Description,
    UdsDtcStatusMask Status)
{
    public bool IsConfirmed => Status.HasFlag(UdsDtcStatusMask.ConfirmedDtc);
    public bool IsPending   => Status.HasFlag(UdsDtcStatusMask.PendingDtc);
    public bool IsActive    => Status.HasFlag(UdsDtcStatusMask.TestFailed);
    public bool IsWarning   => Status.HasFlag(UdsDtcStatusMask.WarningIndicatorRequested);

    public string StatusSummary
    {
        get
        {
            var flags = new List<string>();
            if (IsActive)    flags.Add("Active");
            if (IsConfirmed) flags.Add("Confirmed");
            if (IsPending)   flags.Add("Pending");
            if (IsWarning)   flags.Add("MIL");
            return flags.Count > 0 ? string.Join(", ", flags) : "None";
        }
    }
}

/// <summary>
/// Discovered or configured UDS module / ECU info.
/// </summary>
public record UdsModuleInfo(
    ushort TxId,
    ushort RxId,
    string Name,
    string? EcuName,
    string? PartNumber,
    string? SoftwareVersion,
    string? HardwareNumber,
    string? Vin,
    IReadOnlyList<UdsDtc> Dtcs)
{
    public int DtcCount => Dtcs.Count;
    public bool HasFaults => DtcCount > 0;
    public string TxIdHex => $"0x{TxId:X3}";
    public string RxIdHex => $"0x{RxId:X3}";
    public string AddressSummary => $"TX: 0x{TxId:X3} → RX: 0x{RxId:X3}";
}

/// <summary>
/// Decoded UDS Data Identifier (DID) value from Service $22.
/// </summary>
public record UdsDidValue(
    ushort Did,
    string Name,
    byte[] RawBytes,
    string DisplayValue)
{
    public string DidHex => $"0x{Did:X4}";
}
