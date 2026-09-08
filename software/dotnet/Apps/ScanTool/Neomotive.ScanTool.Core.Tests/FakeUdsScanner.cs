namespace Neomotive.ScanTool.Core.Tests;

/// <summary>Scriptable UDS client for Mode $22 capture tests.</summary>
internal sealed class FakeUdsScanner : IUdsScanner
{
    private readonly Func<ushort, byte[]?> _readDid;
    private int _readCount;

    public FakeUdsScanner(Func<ushort, byte[]?> readDid) => _readDid = readDid;

    public int ReadCount => _readCount;

    public Task<UdsDidValue?> ReadDidAsync(ushort txId, ushort rxId, ushort did, CancellationToken ct = default)
    {
        _readCount++;
        var raw = _readDid(did);

        return Task.FromResult(raw is null
            ? null
            : new UdsDidValue(did, $"DID 0x{did:X4}", raw, Convert.ToHexString(raw)));
    }

    public Task<IReadOnlyList<UdsModuleInfo>> DiscoverModulesAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<UdsDtc>> ReadModuleDtcsAsync(ushort txId, ushort rxId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> ClearModuleDtcsAsync(ushort txId, ushort rxId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> ClearAllDtcsAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> SetDiagnosticSessionAsync(ushort txId, ushort rxId, UdsSessionType session, CancellationToken ct = default)
        => throw new NotSupportedException();

    public List<ushort> TesterPresentTxIds { get; } = new();

    public Task SendTesterPresentAsync(ushort txId, bool suppressResponse = true, CancellationToken ct = default)
    {
        TesterPresentTxIds.Add(txId);
        return Task.CompletedTask;
    }
}
