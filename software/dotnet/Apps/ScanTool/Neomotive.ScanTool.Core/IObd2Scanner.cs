using Meadow.Foundation.Telematics.J1979;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.Core;

public interface IObd2Scanner
{
    bool IsSimulated { get; }
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task<string?> ReadVinAsync(CancellationToken ct = default);
    Task<string?> ReadEcuNameAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosticTroubleCode>> ReadStoredDtcsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosticTroubleCode>> ReadPendingDtcsAsync(CancellationToken ct = default);
    Task ClearDtcsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReadinessMonitor>> ReadReadinessAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VehicleModule>> ScanModulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ModuleDtcGroup>> ReadDtcsByModuleAsync(CancellationToken ct = default);
    Task ClearModuleDtcsAsync(ushort moduleResponseAddress, CancellationToken ct = default);
    Task<PidValue?> ReadPidAsync(Pid pid, CancellationToken ct = default);

    /// <summary>Mode $09 PID $04 — the ECU's calibration ID string.</summary>
    Task<string?> ReadCalibrationIdAsync(CancellationToken ct = default);

    /// <summary>Mode $09 PID $06 — the calibration verification number.</summary>
    Task<string?> ReadCvnAsync(CancellationToken ct = default);

    /// <summary>
    /// Walks the Mode $01 supported-PID bitmaps ($00, $20, $40, ...) and returns every PID the
    /// ECU claims to support.
    /// </summary>
    Task<IReadOnlyList<Pid>> ReadSupportedPidsAsync(CancellationToken ct = default);
}
