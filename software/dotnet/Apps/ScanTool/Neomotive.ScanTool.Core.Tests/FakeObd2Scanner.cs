using Meadow.Foundation.Telematics.J1979;

namespace Neomotive.ScanTool.Core.Tests;

/// <summary>Scriptable scanner for capture tests. Unused members throw so gaps are obvious.</summary>
internal sealed class FakeObd2Scanner : IObd2Scanner
{
    private readonly Func<Pid, int, double?> _read;
    private int _readCount;

    public FakeObd2Scanner(Func<Pid, int, double?> read) => _read = read;

    public int ReadCount => _readCount;

    public int ConnectAttempts { get; private set; }

    /// <summary>Given the 1-based attempt number, decides whether the bus answers.</summary>
    public Func<int, bool> ConnectResult { get; set; } = _ => true;

    public bool IsSimulated => true;

    public Task<bool> ConnectAsync(CancellationToken ct = default)
        => Task.FromResult(ConnectResult(++ConnectAttempts));

    public Task<PidValue?> ReadPidAsync(Pid pid, CancellationToken ct = default)
    {
        var index = _readCount++;
        var value = _read(pid, index);

        if (value is null)
        {
            return Task.FromResult<PidValue?>(null);
        }

        var descriptor = PidRegistry.CommonPids.First(d => d.Id == pid);
        return Task.FromResult<PidValue?>(new PidValue(descriptor, value.Value, DateTime.UtcNow));
    }

    /// <summary>
    /// Raw-byte reads used by the capture channels. Returns two big-endian bytes of whatever the
    /// script yields, so a definition with scale 1 reads back the scripted number.
    /// </summary>
    public Task<byte[]?> ReadPidDataAsync(byte pid, CancellationToken ct = default)
    {
        var index = _readCount++;
        var value = _readRaw is not null ? _readRaw(pid, index) : _read((Pid)pid, index);

        if (value is null)
        {
            return Task.FromResult<byte[]?>(null);
        }

        var raw = (int)value.Value;
        return Task.FromResult<byte[]?>([(byte)(raw >> 8), (byte)raw, 0, 0]);
    }

    /// <summary>Optional override addressed by raw PID rather than the enum.</summary>
    public Func<byte, int, double?>? RawScript
    {
        get => _readRaw;
        set => _readRaw = value;
    }

    private Func<byte, int, double?>? _readRaw;

    /// <summary>Scripted vehicle fingerprint, for tune-check tests.</summary>
    public string? CalibrationId { get; set; }

    public string? Cvn { get; set; }

    public IReadOnlyList<Pid> SupportedPids { get; set; } = Array.Empty<Pid>();

    public Task<string?> ReadCalibrationIdAsync(CancellationToken ct = default)
        => Task.FromResult(CalibrationId);

    public Task<string?> ReadCvnAsync(CancellationToken ct = default)
        => Task.FromResult(Cvn);

    public Task<IReadOnlyList<Pid>> ReadSupportedPidsAsync(CancellationToken ct = default)
        => Task.FromResult(SupportedPids);

    public Task<string?> ReadVinAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<string?> ReadEcuNameAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Func<IReadOnlyList<DiagnosticTroubleCode>>? StoredDtcsFunc { get; set; }
    public IReadOnlyList<DiagnosticTroubleCode> StoredDtcs { get; set; } = Array.Empty<DiagnosticTroubleCode>();

    public Task<IReadOnlyList<DiagnosticTroubleCode>> ReadStoredDtcsAsync(CancellationToken ct = default)
        => Task.FromResult(StoredDtcsFunc is not null ? StoredDtcsFunc() : StoredDtcs);

    public Task<IReadOnlyList<DiagnosticTroubleCode>> ReadPendingDtcsAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task ClearDtcsAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<ReadinessMonitor>> ReadReadinessAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<VehicleModule>> ScanModulesAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<ModuleDtcGroup>> ReadDtcsByModuleAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task ClearModuleDtcsAsync(ushort moduleResponseAddress, CancellationToken ct = default)
        => throw new NotSupportedException();
}
