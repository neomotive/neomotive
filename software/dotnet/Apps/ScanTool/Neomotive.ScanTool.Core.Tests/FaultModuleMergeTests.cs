using Meadow.Foundation.Telematics.Uds;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class FaultModuleMergeTests
{
    private static ModuleDtcGroup Obd2(ushort address, string name, params string[] stored) =>
        new(new VehicleModule(address, name, stored.Length, 0),
            stored.Select(c => new DiagnosticTroubleCode(c, $"{c} desc", DtcStatus.Stored, DtcType.Generic)).ToList(),
            []);

    private static UdsModuleInfo Uds(ushort txId, string name, params string[] codes) =>
        new(txId, (ushort)(txId + 8), name, null, null, null, null, null,
            codes.Select(c => new UdsDtc(c, 0x00, "No sub type", c, $"{c} desc", UdsDtcStatusMask.ConfirmedDtc)).ToList());

    [Fact]
    public void Module_answering_both_protocols_appears_once()
    {
        var merged = FaultModule.Merge([Obd2(0x7E0, "PCM", "P0300")], [Uds(0x7E0, "Engine Control Module", "P0300")]);

        var module = Assert.Single(merged);
        Assert.Equal(FaultProtocol.Both, module.Protocol);
        Assert.Equal("OBD-II + UDS", module.ProtocolText);
    }

    [Fact]
    public void A_code_seen_on_both_protocols_is_listed_once()
    {
        var merged = FaultModule.Merge([Obd2(0x7E0, "PCM", "P0300")], [Uds(0x7E0, "ECM", "P0300")]);

        var code = Assert.Single(merged[0].Codes);
        Assert.Equal("P0300", code.Code);
    }

    [Fact]
    public void Codes_only_obd2_reported_survive_the_merge()
    {
        var merged = FaultModule.Merge([Obd2(0x7E0, "PCM", "P0300", "P0171")], [Uds(0x7E0, "ECM", "P0300")]);

        Assert.Equal(2, merged[0].CodeCount);
        Assert.Contains(merged[0].Codes, c => c.Code == "P0171");
    }

    [Fact]
    public void Uds_only_module_is_kept_and_tagged_uds()
    {
        var merged = FaultModule.Merge([], [Uds(0x760, "Body Control Module", "B1234")]);

        var module = Assert.Single(merged);
        Assert.Equal(FaultProtocol.Uds, module.Protocol);
        Assert.True(module.SpeaksUds);
    }

    [Fact]
    public void Obd2_only_module_is_kept_and_cannot_answer_dids()
    {
        var merged = FaultModule.Merge([Obd2(0x7E1, "TCM", "P0700")], []);

        var module = Assert.Single(merged);
        Assert.Equal(FaultProtocol.Obd2, module.Protocol);
        Assert.False(module.SpeaksUds);
    }

    [Fact]
    public void Faulted_modules_sort_ahead_of_clean_ones()
    {
        var merged = FaultModule.Merge([], [Uds(0x7E0, "AAA clean"), Uds(0x760, "ZZZ faulted", "B1234")]);

        Assert.Equal("ZZZ faulted", merged[0].Name);
    }

    /// <summary>
    /// A stored OBD-II code says the fault was confirmed on an earlier drive cycle, not that it is
    /// failing now. Projecting it as Active would put a badge on the screen the vehicle never sent.
    /// </summary>
    [Fact]
    public void Stored_obd2_code_projects_as_confirmed_not_active()
    {
        var code = FaultCode.FromObd2(new DiagnosticTroubleCode("P0420", "Catalyst", DtcStatus.Stored, DtcType.Generic));

        Assert.True(code.IsConfirmed);
        Assert.False(code.IsActive);
        Assert.False(code.IsPending);
    }

    [Fact]
    public void Pending_obd2_code_projects_as_pending_only()
    {
        var code = FaultCode.FromObd2(new DiagnosticTroubleCode("P0442", "Evap", DtcStatus.Pending, DtcType.Generic));

        Assert.True(code.IsPending);
        Assert.False(code.IsConfirmed);
    }
}
