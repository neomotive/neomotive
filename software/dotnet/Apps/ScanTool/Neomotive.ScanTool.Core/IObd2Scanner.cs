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

    /// <summary>
    /// Reads a Mode $01 PID and returns its raw data bytes, with the service and PID echo stripped.
    /// </summary>
    /// <remarks>
    /// Decoding is left to the caller so one PID can carry several signals — oxygen sensor PIDs
    /// pack voltage and fuel trim into one response, and a fixed one-value-per-PID read cannot
    /// express that.
    /// </remarks>
    Task<byte[]?> ReadPidDataAsync(byte pid, CancellationToken ct = default);

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
