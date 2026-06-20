namespace Neomotive.ScanTool.Core;

public record VehicleModule(
    ushort Address,
    string Name,
    int StoredDtcCount,
    int PendingDtcCount)
{
    public bool HasFaults       => StoredDtcCount > 0;
    public bool HasPendingDtcs  => PendingDtcCount > 0;
    public string AddressHex    => $"0x{Address:X3}";
}
