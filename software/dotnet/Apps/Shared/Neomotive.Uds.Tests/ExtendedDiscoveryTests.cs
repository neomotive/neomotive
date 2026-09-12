using Meadow.Foundation.Telematics.Uds;
using Meadow.Hardware;
using Xunit;

namespace Neomotive.Uds.Tests;

/// <summary>
/// A bus that delivers every frame written to it straight back to its subscribers, at either
/// addressing width.
/// </summary>
/// <remarks>
/// <see cref="FakeCanBus"/> records only <see cref="StandardDataFrame"/>, so it silently swallows
/// the 29-bit traffic these tests exist to exercise. Delivery is posted rather than re-entrant so a
/// server answering inside the handler does not recurse into it.
/// </remarks>
public sealed class LoopbackCanBus : ICanBus
{
    public event EventHandler<ICanFrame>? FrameReceived;
    public event EventHandler<CanErrorInfo>? BusError;

    public CanAcceptanceFilterCollection AcceptanceFilters { get; } = new(0);
    public CanBitrate BitRate { get; set; } = CanBitrate.Can_500kbps;

    public void WriteFrame(ICanFrame frame)
        => ThreadPool.QueueUserWorkItem(_ => FrameReceived?.Invoke(this, frame));

    public void ClearReceiveBuffers() { }
    public bool IsFrameAvailable() => false;
    public ICanFrame? ReadFrame() => null;
}

/// <summary>Minimal data source: a name, so the scanner has something to identify a module by.</summary>
internal sealed class StubDataSource(string name) : IUdsDataSource
{
    public UdsDtcStatusMask AvailabilityMask => UdsDtcStatusMask.ConfirmedDtc;

    public IReadOnlyList<UdsDtcRecord> GetDtcs() => [];

    public void ClearDtcs() { }

    public bool TryGetDid(ushort did, out byte[] data)
    {
        if (did == 0xF197)
        {
            data = System.Text.Encoding.ASCII.GetBytes(name);
            return true;
        }

        data = [];
        return false;
    }
}

public class ExtendedDiscoveryTests
{
    [Fact]
    public void NormalFixedAddressesFollowIso15765()
    {
        var address = UdsAddress.NormalFixed(0x20);

        Assert.Equal(0x18DA20F1u, address.TxId);
        Assert.Equal(0x18DAF120u, address.RxId);
        Assert.True(address.IsExtended);
        Assert.Equal(0x20, address.EcuAddress);
    }

    [Fact]
    public void ExtendedIdentifiersAreFormattedAtFullWidth()
    {
        // :X3 on a 29-bit identifier yields three nibbles of a different address entirely.
        Assert.Equal("0x18DA20F1", UdsAddress.NormalFixed(0x20).TxIdHex);
        Assert.Equal("0x760", UdsAddress.Standard(0x760).TxIdHex);
    }

    [Fact]
    public void StandardPairsAreRequestPlusEight()
    {
        var address = UdsAddress.Standard(0x760);
        Assert.Equal(0x768u, address.RxId);
        Assert.False(address.IsExtended);
    }

    [Fact]
    public void TheDefaultPlanCoversAllThreeTiers()
    {
        var plan = UdsDiscoveryPlan.Default();

        Assert.Equal(3, plan.EnabledTiers.Count());

        // 8 legislated + 223 manufacturer 11-bit + 256 normal-fixed.
        Assert.Equal(8 + 0xDF + 256, plan.TotalAddresses);
    }

    [Fact]
    public void TheLegislatedPlanIsTheOldBehaviour()
    {
        Assert.Equal(8, UdsDiscoveryPlan.LegislatedOnly().TotalAddresses);
    }

    [Fact]
    public async Task AModuleOnAManufacturer11BitAddressIsFound()
    {
        // 0x760 is outside 0x7E0-0x7E7, so the legislated-only sweep could never see it — which is
        // why a real vehicle reported one module.
        var bus = new LoopbackCanBus();
        using var server = new UdsServer([bus], UdsAddress.Standard(0x760, 0x768), new StubDataSource("PSCM"));

        var scanner = new UdsScanner(bus);
        var plan = new UdsDiscoveryPlan
        {
            Tiers = [new UdsDiscoveryTier { Name = "11-bit manufacturer", From = 0x758, To = 0x768 }]
        };

        var modules = await scanner.DiscoverModulesAsync(plan, null, CancellationToken.None);

        Assert.Contains(modules, m => m.Address.TxId == 0x760 && !m.IsExtended);
    }

    [Fact]
    public async Task AModuleOn29BitNormalFixedAddressingIsFound()
    {
        var bus = new LoopbackCanBus();
        using var server = new UdsServer([bus], UdsAddress.NormalFixed(0x20), new StubDataSource("IPC"));

        var scanner = new UdsScanner(bus);
        var plan = new UdsDiscoveryPlan
        {
            Tiers = [new UdsDiscoveryTier { Name = "29-bit", From = 0x18, To = 0x28, IsExtended = true }]
        };

        var modules = await scanner.DiscoverModulesAsync(plan, null, CancellationToken.None);

        Assert.Contains(modules, m => m.IsExtended && m.Address.EcuAddress == 0x20);
    }

    [Fact]
    public async Task ASweepReportsProgress()
    {
        var bus = new LoopbackCanBus();
        var scanner = new UdsScanner(bus);

        var reports = new List<UdsDiscoveryProgress>();
        var plan = new UdsDiscoveryPlan
        {
            Tiers = [new UdsDiscoveryTier { Name = "test", From = 0x700, To = 0x71F }]
        };

        await scanner.DiscoverModulesAsync(
            plan, new Progress<UdsDiscoveryProgress>(reports.Add), CancellationToken.None);

        // Progress is what keeps a tens-of-seconds sweep from looking like a frozen button.
        await Task.Delay(50);
        Assert.NotEmpty(reports);
        Assert.Equal(32, reports[^1].AddressesTotal);
    }

    [Fact]
    public async Task ASweepStopsWhenCancelled()
    {
        var bus = new LoopbackCanBus();
        var scanner = new UdsScanner(bus);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var modules = await scanner.DiscoverModulesAsync(UdsDiscoveryPlan.Default(), null, cts.Token);

        Assert.Empty(modules);
    }

    [Fact]
    public async Task ATargetedProbeOnlyAsksTheAddressesItIsGiven()
    {
        // The remembered-vehicle fast path: dozens of addresses, not five hundred.
        var bus = new LoopbackCanBus();
        using var server = new UdsServer([bus], UdsAddress.Standard(0x760, 0x768), new StubDataSource("PSCM"));

        var scanner = new UdsScanner(bus);

        var modules = await scanner.ProbeAddressesAsync(
            [UdsAddress.Standard(0x760, 0x768), UdsAddress.NormalFixed(0x20)],
            null,
            CancellationToken.None);

        Assert.Single(modules);
        Assert.Equal(0x760u, modules[0].Address.TxId);
    }
}
