using Meadow.Foundation.Telematics.Uds;
using Neomotive.ModuleSimulator;
using System.Text;
using Xunit;

namespace Neomotive.ModuleSimulator.Tests;

public class SimulatorUdsTests
{
    private static SimulatorState NewPcmState() => new(new StubInputs());

    private static UdsModuleProfile PcmProfile() => new()
    {
        Name = "PCM (Powertrain)",
        ResponseId = 0x7E8,
        MirrorSimulatorDtcs = true
    };

    [Fact]
    public void MirroredStore_ReportsStoredFaultsAsConfirmedAndFailing()
    {
        var state = NewPcmState();
        state.StoredDtcs["P0300"] = [0x03, 0x00];

        var source = new SimulatorUdsDataSource(PcmProfile(), state);
        var dtc = Assert.Single(source.GetDtcs());

        Assert.Equal("P0300", dtc.BaseCode);
        Assert.True(dtc.Status.HasFlag(UdsDtcStatusMask.ConfirmedDtc));
        Assert.True(dtc.Status.HasFlag(UdsDtcStatusMask.TestFailed));
    }

    [Fact]
    public void MirroredStore_DistinguishesPendingAndPermanentFaults()
    {
        var state = NewPcmState();
        state.PendingDtcs["P0171"] = [0x01, 0x71];
        state.PermanentDtcs["P0420"] = [0x04, 0x20];

        var dtcs = new SimulatorUdsDataSource(PcmProfile(), state).GetDtcs();

        var pending = Assert.Single(dtcs, d => d.BaseCode == "P0171");
        Assert.True(pending.Status.HasFlag(UdsDtcStatusMask.PendingDtc));
        Assert.False(pending.Status.HasFlag(UdsDtcStatusMask.ConfirmedDtc));

        var permanent = Assert.Single(dtcs, d => d.BaseCode == "P0420");
        Assert.True(permanent.Status.HasFlag(UdsDtcStatusMask.TestFailedSinceLastClear));
    }

    [Fact]
    public void SameFaultInTwoStores_IsReportedOnceWithBothStatusBits()
    {
        var state = NewPcmState();
        state.StoredDtcs["P0300"] = [0x03, 0x00];
        state.PermanentDtcs["P0300"] = [0x03, 0x00];

        var dtc = Assert.Single(new SimulatorUdsDataSource(PcmProfile(), state).GetDtcs());

        Assert.True(dtc.Status.HasFlag(UdsDtcStatusMask.ConfirmedDtc));
        Assert.True(dtc.Status.HasFlag(UdsDtcStatusMask.TestFailed));
        Assert.True(dtc.Status.HasFlag(UdsDtcStatusMask.TestFailedSinceLastClear));
    }

    [Fact]
    public void ProfileWithoutMirroring_IgnoresSimulatorFaults()
    {
        var state = NewPcmState();
        state.StoredDtcs["P0300"] = [0x03, 0x00];

        var profile = PcmProfile();
        profile.MirrorSimulatorDtcs = false;

        Assert.Empty(new SimulatorUdsDataSource(profile, state).GetDtcs());
    }

    [Fact]
    public void StaticProfileDtc_IsReportedWithItsFaultTypeAndStatus()
    {
        var profile = new UdsModuleProfile
        {
            Name = "ABS",
            ResponseId = 0x7EC,
            Dtcs = [new UdsDtcProfile { Code = "C0035-11", Status = "Confirmed" }]
        };

        var dtc = Assert.Single(new SimulatorUdsDataSource(profile).GetDtcs());

        Assert.Equal("C0035", dtc.BaseCode);
        Assert.Equal(0x11, dtc.FaultType);
        Assert.Equal(UdsDtcStatusMask.ConfirmedDtc, dtc.Status);
    }

    [Theory]
    [InlineData("Confirmed,TestFailed", (byte)0x09)]
    [InlineData("Pending", (byte)0x04)]
    [InlineData("Active", (byte)0x01)]
    [InlineData("MIL", (byte)0x80)]
    [InlineData("0x2F", (byte)0x2F)]
    [InlineData("", (byte)0x09)]
    public void StatusStrings_ParseToTheExpectedMask(string status, byte expected)
    {
        Assert.Equal((UdsDtcStatusMask)expected, new UdsDtcProfile { Status = status }.ParseStatus());
    }

    [Fact]
    public void Clear_EmptiesEveryStoreAndNotifiesTheHost()
    {
        var state = NewPcmState();
        state.StoredDtcs["P0300"] = [0x03, 0x00];
        state.PendingDtcs["P0171"] = [0x01, 0x71];
        state.PermanentDtcs["P0420"] = [0x04, 0x20];

        var profile = PcmProfile();
        profile.Dtcs = [new UdsDtcProfile { Code = "P0507" }];

        bool notified = false;
        var source = new SimulatorUdsDataSource(profile, state, onClear: () => notified = true);

        source.ClearDtcs();

        Assert.Empty(state.StoredDtcs);
        Assert.Empty(state.PendingDtcs);
        Assert.Empty(state.PermanentDtcs);
        Assert.Empty(source.GetDtcs());
        Assert.True(notified);
    }

    [Fact]
    public void Vin_IsServedLiveFromSimulatorState()
    {
        var state = NewPcmState();
        var source = new SimulatorUdsDataSource(PcmProfile(), state, vin: () => state.Vin);

        Assert.True(source.TryGetDid(0xF190, out var first));
        Assert.Equal(state.Vin, Encoding.ASCII.GetString(first));

        // A VIN changed at runtime must show up without rebuilding the data source.
        state.Vin = "1HGCR2F83HA000000";
        Assert.True(source.TryGetDid(0xF190, out var second));
        Assert.Equal("1HGCR2F83HA000000", Encoding.ASCII.GetString(second));
    }

    [Fact]
    public void ModuleName_AnswersSystemNameDidWhenNoneIsConfigured()
    {
        var source = new SimulatorUdsDataSource(PcmProfile());

        Assert.True(source.TryGetDid(0xF197, out var data));
        Assert.Equal("PCM (Powertrain)", Encoding.ASCII.GetString(data));
    }

    [Theory]
    [InlineData("ascii:NEO-PCM-0001", "NEO-PCM-0001")]
    [InlineData("NEO-PCM-0001", "NEO-PCM-0001")]
    public void ProfileDidValues_DecodeAsText(string configured, string expected)
    {
        var profile = PcmProfile();
        profile.Dids["0xF187"] = configured;

        var source = new SimulatorUdsDataSource(profile);

        Assert.True(source.TryGetDid(0xF187, out var data));
        Assert.Equal(expected, Encoding.ASCII.GetString(data));
    }

    [Fact]
    public void HexPrefixedDidValue_DecodesToRawBytes()
    {
        var profile = PcmProfile();
        profile.Dids["0xF1A0"] = "hex:01 02 0A";

        var source = new SimulatorUdsDataSource(profile);

        Assert.True(source.TryGetDid(0xF1A0, out var data));
        Assert.Equal([0x01, 0x02, 0x0A], data);
    }

    [Fact]
    public void UnknownDid_IsNotServed()
    {
        Assert.False(new SimulatorUdsDataSource(PcmProfile()).TryGetDid(0x1234, out _));
    }

    [Fact]
    public void DefaultConfig_CoversThePcmTcuAndExtraModules()
    {
        var config = UdsConfig.CreateDefault();

        Assert.True(config.Enabled);
        Assert.Contains(config.Modules, m => m.ResponseId == 0x7E8 && m.MirrorSimulatorDtcs);
        Assert.Contains(config.Modules, m => m.ResponseId == 0x7E9 && m.MirrorSimulatorDtcs);
        Assert.Contains(config.Modules, m => m.ResponseId == 0x7EA);
        Assert.Contains(config.Modules, m => m.ResponseId == 0x7EC);
        Assert.Contains(config.Modules, m => m.ResponseId == 0x7ED);

        // Every extra module carries a fault, so discovery has something real to show.
        Assert.All(config.Modules.Where(m => !m.MirrorSimulatorDtcs), m => Assert.NotEmpty(m.Dtcs));
    }

    [Fact]
    public void UdsConfig_RoundTripsThroughTheConfigFileSerializer()
    {
        // ConfigManager writes the whole SimulatorConfig with these options; the UDS section has
        // to survive a save/load or a bench setup would silently reset on restart.
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        var original = UdsConfig.CreateDefault();
        original.Modules.Add(new UdsModuleProfile
        {
            Name = "IC (Instrument Cluster)",
            ResponseId = 0x7EE,
            Dids = new() { ["0xF197"] = "IC", ["0xF1A0"] = "hex:01 02" },
            Dtcs = [new UdsDtcProfile { Code = "U0155-87", Status = "Confirmed" }]
        });

        var json = System.Text.Json.JsonSerializer.Serialize(original, options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<UdsConfig>(json, options)!;

        Assert.Equal(original.Modules.Count, restored.Modules.Count);

        var cluster = restored.Modules.Single(m => m.ResponseId == 0x7EE);
        Assert.Equal("IC (Instrument Cluster)", cluster.Name);
        Assert.Equal("hex:01 02", cluster.Dids["0xF1A0"]);
        Assert.Equal("U0155-87", Assert.Single(cluster.Dtcs).Code);
        Assert.True(restored.Modules.Single(m => m.ResponseId == 0x7E8).MirrorSimulatorDtcs);
    }
}
