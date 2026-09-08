using Meadow.Foundation.Telematics.Uds;
using Meadow.Hardware;
using Neomotive.ModuleSimulator;
using Neomotive.Uds;
using Xunit;

namespace Neomotive.ModuleSimulator.Tests;

/// <summary>
/// The simulator's UDS host answering the real ScanTool client over a loopback bus — the same
/// exchange that happens on the bench, minus the PCAN adapter.
/// </summary>
public class UdsModuleHostTests
{
    /// <summary>Hands every transmitted frame back to all subscribers, like a two-node bus.</summary>
    private sealed class LoopbackCanBus : ICanBus
    {
        public event EventHandler<ICanFrame>? FrameReceived;
        public event EventHandler<CanErrorInfo>? BusError;

        public CanAcceptanceFilterCollection AcceptanceFilters { get; } = new(0);
        public CanBitrate BitRate { get; set; } = CanBitrate.Can_500kbps;

        public void WriteFrame(ICanFrame frame) => FrameReceived?.Invoke(this, frame);
        public void ClearReceiveBuffers() { }
        public bool IsFrameAvailable() => false;
        public ICanFrame? ReadFrame() => null;
        public void ReportError(CanErrorInfo info) => BusError?.Invoke(this, info);
    }

    private static (LoopbackCanBus bus, UdsModuleHost host, SimulatorState pcm, SimulatorTcuState tcu)
        StartSimulator(UdsConfig? config = null, Action? onCleared = null)
    {
        var bus = new LoopbackCanBus();
        var pcmState = new SimulatorState(new StubInputs());
        var tcuState = new SimulatorTcuState();

        var host = new UdsModuleHost();
        host.Start(bus, config ?? UdsConfig.CreateDefault(), pcmState, tcuState, onCleared);

        return (bus, host, pcmState, tcuState);
    }

    [Fact]
    public async Task Discovery_FindsEveryConfiguredModuleWithItsName()
    {
        var (bus, host, _, _) = StartSimulator();
        using var _h = host;

        var modules = await new UdsScanner(bus, UdsCatalog.Shared).DiscoverModulesAsync();

        Assert.Equal([0x7E8, 0x7E9, 0x7EA, 0x7EC, 0x7ED], modules.Select(m => m.RxId).ToArray());
        Assert.Equal("PCM (Powertrain)", modules[0].Name);
        Assert.Equal("ABS (Brakes)", modules.Single(m => m.RxId == 0x7EC).Name);
    }

    [Fact]
    public async Task Discovery_ReadsTheIdentificationDidsAndTheLiveVin()
    {
        var (bus, host, pcmState, _) = StartSimulator();
        using var _h = host;

        pcmState.Vin = "1HGCR2F83HA000000";

        var modules = await new UdsScanner(bus, UdsCatalog.Shared).DiscoverModulesAsync();
        var pcm = modules.Single(m => m.RxId == 0x7E8);

        Assert.Equal("1HGCR2F83HA000000", pcm.Vin);
        Assert.Equal("NEO-PCM-0001", pcm.PartNumber);
        Assert.Equal("SW 1.4.2", pcm.SoftwareVersion);
        Assert.Equal("HW REV C", pcm.HardwareNumber);
    }

    [Fact]
    public async Task FaultSetInTheSimulator_ShowsUpOverUds()
    {
        var (bus, host, pcmState, tcuState) = StartSimulator();
        using var _h = host;

        // What the toolbox Quick-DTC buttons do.
        pcmState.StoredDtcs["P0300"] = [0x03, 0x00];
        tcuState.PendingDtcs["P0715"] = [0x07, 0x15];

        var scanner = new UdsScanner(bus, UdsCatalog.Shared);

        var pcmDtc = Assert.Single(await scanner.ReadModuleDtcsAsync(0x7E0, 0x7E8));
        Assert.Equal("P0300", pcmDtc.BaseCode);
        Assert.Equal("Random/Multiple Cylinder Misfire Detected", pcmDtc.Description);
        Assert.True(pcmDtc.IsConfirmed);

        var tcuDtc = Assert.Single(await scanner.ReadModuleDtcsAsync(0x7E1, 0x7E9));
        Assert.Equal("P0715", tcuDtc.BaseCode);
        Assert.True(tcuDtc.IsPending);
    }

    [Fact]
    public async Task ClearFromScanTool_EmptiesTheSimulatorStoresAndNotifiesTheHost()
    {
        bool resynced = false;
        var (bus, host, pcmState, _) = StartSimulator(onCleared: () => resynced = true);
        using var _h = host;

        pcmState.StoredDtcs["P0300"] = [0x03, 0x00];
        pcmState.PendingDtcs["P0171"] = [0x01, 0x71];

        var scanner = new UdsScanner(bus, UdsCatalog.Shared);
        Assert.True(await scanner.ClearModuleDtcsAsync(0x7E0, 0x7E8));

        Assert.Empty(pcmState.StoredDtcs);
        Assert.Empty(pcmState.PendingDtcs);
        Assert.True(resynced);
        Assert.Empty(await scanner.ReadModuleDtcsAsync(0x7E0, 0x7E8));
    }

    [Fact]
    public async Task ModuleAddedToConfig_IsDiscoveredWithoutACodeChange()
    {
        // The bench check: edit neoteric.config.json, restart, see a new ECU.
        var config = UdsConfig.CreateDefault();
        config.Modules.Add(new UdsModuleProfile
        {
            Name = "IC (Instrument Cluster)",
            ResponseId = 0x7EE,
            Dids = new() { ["0xF197"] = "IC (Instrument Cluster)" },
            Dtcs = [new UdsDtcProfile { Code = "U0155-87", Status = "Confirmed" }]
        });

        var (bus, host, _, _) = StartSimulator(config);
        using var _h = host;

        var modules = await new UdsScanner(bus, UdsCatalog.Shared).DiscoverModulesAsync();
        var cluster = Assert.Single(modules, m => m.RxId == 0x7EE);

        Assert.Equal("IC (Instrument Cluster)", cluster.Name);
        Assert.Equal("U0155-87", Assert.Single(cluster.Dtcs).FullCode);
    }

    [Fact]
    public async Task DisabledConfig_ServesNothing()
    {
        var config = UdsConfig.CreateDefault();
        config.Enabled = false;

        var (bus, host, _, _) = StartSimulator(config);
        using var _h = host;

        // Nothing answers, so the client would otherwise walk its whole address sweep;
        // two seconds of silence is all this needs to prove.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Empty(await new UdsScanner(bus).DiscoverModulesAsync(cts.Token));
    }

    [Fact]
    public async Task DisabledModule_IsSkipped()
    {
        var config = UdsConfig.CreateDefault();
        config.Modules.Single(m => m.ResponseId == 0x7EA).Enabled = false;

        var (bus, host, _, _) = StartSimulator(config);
        using var _h = host;

        var modules = await new UdsScanner(bus, UdsCatalog.Shared).DiscoverModulesAsync();

        Assert.DoesNotContain(modules, m => m.RxId == 0x7EA);
    }

    [Fact]
    public async Task StoppedHost_LeavesTheBusQuiet()
    {
        var (bus, host, _, _) = StartSimulator();
        host.Stop();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Empty(await new UdsScanner(bus).DiscoverModulesAsync(cts.Token));
    }
}
