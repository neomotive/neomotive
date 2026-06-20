using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.Core;

public interface IObd2Scanner
{
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task<string?> ReadVinAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosticTroubleCode>> ReadStoredDtcsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosticTroubleCode>> ReadPendingDtcsAsync(CancellationToken ct = default);
    Task ClearDtcsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReadinessMonitor>> ReadReadinessAsync(CancellationToken ct = default);
}
