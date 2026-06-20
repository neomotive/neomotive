using System.Collections.Generic;

namespace Neomotive.ScanTool.Core;

public record ModuleDtcGroup(
    VehicleModule Module,
    IReadOnlyList<DiagnosticTroubleCode> StoredDtcs,
    IReadOnlyList<DiagnosticTroubleCode> PendingDtcs)
{
    public bool HasStoredDtcs  => StoredDtcs.Count > 0;
    public bool HasPendingDtcs => PendingDtcs.Count > 0;
    public bool HasAny         => HasStoredDtcs || HasPendingDtcs;
}
