using Meadow.Foundation.Telematics.Uds;

namespace Neomotive.ScanTool.Core.Tests;

/// <summary>Scriptable UDS client for Mode $22 capture tests.</summary>
internal sealed class FakeUdsScanner : IUdsScanner
{
    private readonly Func<ushort, byte[]?> _readDid;
    private int _readCount;

    public FakeUdsScanner(Func<ushort, byte[]?> readDid) => _readDid = readDid;

    public int ReadCount => _readCount;

    /// <summary>Addresses handed to <see cref="ProbeAddressesAsync"/>, in order.</summary>
    public List<UdsAddress> ProbedAddresses { get; } = new();

    /// <summary>Addresses that will answer a probe. Anything else stays silent.</summary>
    public HashSet<UdsAddress> RespondingAddresses { get; } = new();

    public Task<UdsDidValue?> ReadDidAsync(UdsAddress address, ushort did, CancellationToken ct = default)
    {
        _readCount++;
        var raw = _readDid(did);

        return Task.FromResult(raw is null
            ? null
            : new UdsDidValue(did, $"DID 0x{did:X4}", raw, Convert.ToHexString(raw)));
    }

    public Task<UdsDidValue?> ReadDidAsync(ushort txId, ushort rxId, ushort did, CancellationToken ct = default)
        => ReadDidAsync(UdsAddress.Standard(txId, rxId), did, ct);

    public Task<IReadOnlyList<UdsModuleInfo>> DiscoverModulesAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<UdsModuleInfo>> DiscoverModulesAsync(
        UdsDiscoveryPlan plan, IProgress<UdsDiscoveryProgress>? progress, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<UdsModuleInfo>> ProbeAddressesAsync(
        IEnumerable<UdsAddress> addresses,
        IProgress<UdsDiscoveryProgress>? progress = null,
        CancellationToken ct = default)
    {
        var list = addresses.ToList();
        ProbedAddresses.AddRange(list);

        IReadOnlyList<UdsModuleInfo> found = list
            .Where(RespondingAddresses.Contains)
            .Select(a => new UdsModuleInfo(a, $"ECU {a.TxIdHex}", null, null, null, null, null, []))
            .ToList();

        progress?.Report(new UdsDiscoveryProgress(
            "Remembered addresses", 0, 1, list.Count, list.Count, found.Count));

        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<UdsDtc>> ReadModuleDtcsAsync(UdsAddress address, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UdsDtc>>([]);

    public Task<IReadOnlyList<UdsDtc>> ReadModuleDtcsAsync(ushort txId, ushort rxId, CancellationToken ct = default)
        => ReadModuleDtcsAsync(UdsAddress.Standard(txId, rxId), ct);

    public Task<bool> ClearModuleDtcsAsync(UdsAddress address, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> ClearModuleDtcsAsync(ushort txId, ushort rxId, CancellationToken ct = default)
        => ClearModuleDtcsAsync(UdsAddress.Standard(txId, rxId), ct);

    public Task<bool> ClearAllDtcsAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> SetDiagnosticSessionAsync(UdsAddress address, UdsSessionType session, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> SetDiagnosticSessionAsync(ushort txId, ushort rxId, UdsSessionType session, CancellationToken ct = default)
        => SetDiagnosticSessionAsync(UdsAddress.Standard(txId, rxId), session, ct);

    public List<uint> TesterPresentTxIds { get; } = new();

    public Task SendTesterPresentAsync(UdsAddress address, bool suppressResponse = true, CancellationToken ct = default)
    {
        TesterPresentTxIds.Add(address.TxId);
        return Task.CompletedTask;
    }

    public Task SendTesterPresentAsync(ushort txId, bool suppressResponse = true, CancellationToken ct = default)
        => SendTesterPresentAsync(UdsAddress.Standard(txId), suppressResponse, ct);
}
