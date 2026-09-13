using Meadow.Foundation.Telematics.Uds;

namespace Neomotive.ScanTool.Core;

/// <summary>
/// One trouble code as the DTCs page shows it, regardless of which protocol produced it.
///
/// The status flags are the union of what the two sources can say. OBD-II reports a code as
/// stored or pending and nothing else, so a code projected from it lights exactly one flag;
/// UDS carries a full ISO 14229-1 status mask and can light several. Displaying flags rather
/// than STORED/PENDING section headings is what lets one list hold both without the headings
/// lying about half its rows.
/// </summary>
public record FaultCode(
    string Code,
    string Description,
    string Detail,
    bool IsActive,
    bool IsConfirmed,
    bool IsPending,
    bool IsMil)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>
    /// Stored maps to Confirmed, not to Active: a stored code means the fault was confirmed on a
    /// previous drive cycle, which says nothing about whether it is failing right now. Only UDS's
    /// TestFailed bit carries that, so an OBD-II code never claims to be active.
    /// </summary>
    public static FaultCode FromObd2(DiagnosticTroubleCode dtc) => new(
        Code: dtc.Code,
        Description: dtc.Description,
        Detail: "",
        IsActive: false,
        IsConfirmed: dtc.Status is DtcStatus.Stored or DtcStatus.Permanent,
        IsPending: dtc.Status == DtcStatus.Pending,
        IsMil: false);

    public static FaultCode FromUds(UdsDtc dtc) => new(
        Code: dtc.FullCode,
        Description: dtc.Description,
        Detail: string.IsNullOrEmpty(dtc.FaultTypeDescription) ? "" : $"FTB: {dtc.FaultTypeDescription}",
        IsActive: dtc.IsActive,
        IsConfirmed: dtc.IsConfirmed,
        IsPending: dtc.IsPending,
        IsMil: dtc.IsWarning);
}
