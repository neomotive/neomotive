using System.Text;
using Meadow.Foundation.Telematics.J1979;
using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class CalibrationParsingTests
{
    private static byte[] CalIdResponse(params string[] ids)
    {
        var bytes = new List<byte> { 0x49, 0x04, (byte)ids.Length };

        foreach (var id in ids)
        {
            var field = new byte[16];
            Encoding.ASCII.GetBytes(id).CopyTo(field, 0);
            bytes.AddRange(field);
        }

        return bytes.ToArray();
    }

    [Fact]
    public void Single_calibration_id_is_parsed_and_trimmed()
    {
        Assert.Equal("JMB*36761500", Obd2Protocol.ParseCalibrationId(CalIdResponse("JMB*36761500")));
    }

    [Fact]
    public void Multiple_calibration_ids_are_joined()
    {
        Assert.Equal("CAL-A, CAL-B", Obd2Protocol.ParseCalibrationId(CalIdResponse("CAL-A", "CAL-B")));
    }

    [Fact]
    public void Wrong_service_or_pid_is_rejected()
    {
        Assert.Null(Obd2Protocol.ParseCalibrationId([0x41, 0x04, 0x01, .. new byte[16]]));
        Assert.Null(Obd2Protocol.ParseCalibrationId([0x49, 0x02, 0x01, .. new byte[16]]));
    }

    [Fact]
    public void Truncated_and_null_responses_are_rejected()
    {
        Assert.Null(Obd2Protocol.ParseCalibrationId(null!));
        Assert.Null(Obd2Protocol.ParseCalibrationId([0x49, 0x04]));
    }

    [Fact]
    public void An_all_padding_calibration_id_is_treated_as_absent()
    {
        Assert.Null(Obd2Protocol.ParseCalibrationId([0x49, 0x04, 0x01, .. new byte[16]]));
    }

    [Fact]
    public void An_overstated_item_count_is_clamped_to_the_payload()
    {
        // Declares four items but only carries one; the payload length is the authority.
        var response = new byte[] { 0x49, 0x04, 0x04 }.Concat(new byte[16]).ToArray();
        Encoding.ASCII.GetBytes("ONLY-ONE").CopyTo(response, 3);

        Assert.Equal("ONLY-ONE", Obd2Protocol.ParseCalibrationId(response));
    }

    [Fact]
    public void Cvn_is_parsed_as_hex()
    {
        byte[] response = [0x49, 0x06, 0x01, 0xA1, 0xB2, 0xC3, 0xD4];

        Assert.Equal("A1B2C3D4", Obd2Protocol.ParseCvn(response));
    }

    [Fact]
    public void Multiple_cvns_are_joined()
    {
        byte[] response = [0x49, 0x06, 0x02, 0x00, 0x00, 0x00, 0x01, 0xDE, 0xAD, 0xBE, 0xEF];

        Assert.Equal("00000001, DEADBEEF", Obd2Protocol.ParseCvn(response));
    }
}

public class SupportedPidParsingTests
{
    [Fact]
    public void High_bit_of_the_first_byte_is_the_range_base_plus_one()
    {
        // Range $00, bit 31 set => PID 0x01 supported.
        byte[] response = [0x41, 0x00, 0x80, 0x00, 0x00, 0x00];

        Assert.Equal([Pid.MonitorStatus], Obd2Protocol.ParseSupportedPids(response));
    }

    [Fact]
    public void Bits_map_to_the_expected_pids()
    {
        // 0x00 range: RPM (0x0C) is bit 20 from the top, coolant (0x05) is bit 27.
        // 0x0C => index 11 => bit (31-11) = bit 20 of the 32-bit map.
        var bitmap = (1u << (31 - 11)) | (1u << (31 - 4));
        byte[] response =
        [
            0x41, 0x00,
            (byte)(bitmap >> 24), (byte)(bitmap >> 16), (byte)(bitmap >> 8), (byte)bitmap,
        ];

        var supported = Obd2Protocol.ParseSupportedPids(response);

        Assert.Contains(Pid.EngineRpm, supported);
        Assert.Contains(Pid.EngineCoolantTemperature, supported);
        Assert.Equal(2, supported.Count);
    }

    [Fact]
    public void A_later_range_base_offsets_the_decoded_pids()
    {
        // Range $80, bit 31 => PID 0x81.
        byte[] response = [0x41, 0x80, 0x80, 0x00, 0x00, 0x00];

        Assert.Equal([(Pid)0x81], Obd2Protocol.ParseSupportedPids(response));
    }

    [Fact]
    public void Empty_bitmap_yields_nothing()
    {
        Assert.Empty(Obd2Protocol.ParseSupportedPids([0x41, 0x00, 0x00, 0x00, 0x00, 0x00]));
    }

    [Fact]
    public void Malformed_responses_yield_nothing()
    {
        Assert.Empty(Obd2Protocol.ParseSupportedPids(null!));
        Assert.Empty(Obd2Protocol.ParseSupportedPids([0x41, 0x00, 0x00]));
        Assert.Empty(Obd2Protocol.ParseSupportedPids([0x49, 0x00, 0xFF, 0xFF, 0xFF, 0xFF]));
    }

    [Fact]
    public void The_last_bit_flags_that_another_range_follows()
    {
        Assert.True(Obd2Protocol.SupportsNextPidRange([0x41, 0x00, 0x00, 0x00, 0x00, 0x01]));
        Assert.False(Obd2Protocol.SupportsNextPidRange([0x41, 0x00, 0xFF, 0xFF, 0xFF, 0xFE]));
        Assert.False(Obd2Protocol.SupportsNextPidRange([0x41, 0x00]));
    }
}

public class TuneAnalyzerTests
{
    private static TuneFingerprint Fingerprint(
        IReadOnlyList<Pid>? supported = null,
        IReadOnlyList<ReadinessMonitor>? monitors = null,
        string? calId = "CAL-STOCK-001",
        string? cvn = "A1B2C3D4")
        => new("AWWWWWWWWWWW0YEAH", "PCM", calId, cvn,
            supported ?? [Pid.EngineRpm, Pid.EngineCoolantTemperature],
            monitors ?? []);

    [Theory]
    [InlineData(Pid.DpfTemperature, true)]
    [InlineData(Pid.ScrCatalystTemperature, true)]
    [InlineData(Pid.NoxSensor, true)]
    [InlineData(Pid.ParticulateMatterSensor, true)]
    [InlineData(Pid.EngineRpm, false)]
    [InlineData(Pid.FuelRailGaugePressure, false)]
    public void Aftertreatment_pids_are_identified_by_family(Pid pid, bool expected)
    {
        Assert.Equal(expected, TuneAnalyzer.IsAftertreatmentPid(pid));
    }

    [Fact]
    public void Egr_alone_is_not_treated_as_aftertreatment()
    {
        // Petrol engines have EGR too, so its presence says nothing about a DPF/SCR delete.
        Assert.False(TuneAnalyzer.IsAftertreatmentPid(Pid.CommandedEgr));
        Assert.False(TuneAnalyzer.IsAftertreatmentPid(Pid.EgrError));
    }

    [Fact]
    public void Aftertreatment_pids_still_reported_is_flagged_as_significant()
    {
        var assessment = TuneAnalyzer.Analyze(Fingerprint(
            [Pid.EngineRpm, Pid.DpfTemperature, Pid.NoxSensor, Pid.ScrCatalystTemperature]));

        Assert.Equal(3, assessment.AftertreatmentPids.Count);

        var finding = Assert.Single(assessment.Findings,
            f => f.Severity == TuneFindingSeverity.Significant);

        Assert.Contains("still", finding.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WITHOUT the accompanying tune", finding.Detail);
        Assert.Contains("still expects", assessment.Summary);
    }

    [Fact]
    public void No_aftertreatment_pids_reads_as_consistent_with_a_delete_tune()
    {
        var assessment = TuneAnalyzer.Analyze(Fingerprint([Pid.EngineRpm, Pid.VehicleSpeed]));

        Assert.Empty(assessment.AftertreatmentPids);

        var finding = Assert.Single(assessment.Findings,
            f => f.Severity == TuneFindingSeverity.Significant);

        Assert.Contains("No DPF/SCR/NOx PIDs", finding.Title);
    }

    [Fact]
    public void Unsupported_and_incomplete_monitors_are_reported_separately()
    {
        var assessment = TuneAnalyzer.Analyze(Fingerprint(monitors:
        [
            new ReadinessMonitor("Catalyst", Supported: false, Ready: false),
            new ReadinessMonitor("EGR System", Supported: true, Ready: false),
            new ReadinessMonitor("O2 Sensor", Supported: true, Ready: true),
        ]));

        // "Not supported" is the tell; "not complete" on a truck that will not run is expected.
        Assert.Equal(["Catalyst"], assessment.UnsupportedMonitors);
        Assert.Equal(["EGR System"], assessment.IncompleteMonitors);
    }

    [Fact]
    public void A_missing_calibration_id_or_cvn_is_called_out()
    {
        var assessment = TuneAnalyzer.Analyze(Fingerprint(calId: null, cvn: null));

        Assert.Contains(assessment.Findings, f => f.Title.Contains("No calibration ID"));
        Assert.Contains(assessment.Findings, f => f.Title.Contains("No CVN"));
    }

    [Fact]
    public void A_present_calibration_id_is_reported_without_overclaiming()
    {
        var assessment = TuneAnalyzer.Analyze(Fingerprint());

        var finding = Assert.Single(assessment.Findings, f => f.Title.StartsWith("Calibration ID:"));

        // Without a baseline the tool must not claim a CALID proves anything on its own.
        Assert.Equal(TuneFindingSeverity.Info, finding.Severity);
        Assert.Contains("proves nothing", finding.Detail);
    }
}

public class Mode22SignalDefinitionTests
{
    [Fact]
    public void Two_byte_big_endian_value_is_scaled()
    {
        var def = new Mode22SignalDefinitionBuilder().WithLength(2).WithScale(10).Build();

        Assert.Equal(30_000, def.Decode([0x0B, 0xB8]));
    }

    [Fact]
    public void Byte_offset_selects_the_field()
    {
        var def = new Mode22SignalDefinitionBuilder().WithOffset(2).WithLength(2).Build();

        Assert.Equal(0x0BB8, def.Decode([0xFF, 0xFF, 0x0B, 0xB8]));
    }

    [Fact]
    public void Signed_values_are_sign_extended()
    {
        var def = new Mode22SignalDefinitionBuilder().WithLength(2).WithSigned(true).Build();

        Assert.Equal(-1, def.Decode([0xFF, 0xFF]));
        Assert.Equal(-40, def.Decode([0xFF, 0xD8]));
    }

    [Fact]
    public void Unsigned_values_stay_positive()
    {
        var def = new Mode22SignalDefinitionBuilder().WithLength(2).Build();

        Assert.Equal(65535, def.Decode([0xFF, 0xFF]));
    }

    [Fact]
    public void Offset_is_applied_after_scaling()
    {
        var def = new Mode22SignalDefinitionBuilder().WithLength(1).WithScale(1).WithOffsetValue(-40).Build();

        Assert.Equal(50, def.Decode([90]));
    }

    [Fact]
    public void Short_or_missing_data_yields_null()
    {
        var def = new Mode22SignalDefinitionBuilder().WithOffset(2).WithLength(2).Build();

        Assert.Null(def.Decode(null));
        Assert.Null(def.Decode([0x01, 0x02, 0x03]));
    }

    [Fact]
    public void An_unsupported_width_yields_null()
    {
        Assert.Null(new Mode22SignalDefinitionBuilder().WithLength(0).Build().Decode([1, 2]));
        Assert.Null(new Mode22SignalDefinitionBuilder().WithLength(5).Build().Decode([1, 2, 3, 4, 5, 6]));
    }

    private sealed class Mode22SignalDefinitionBuilder
    {
        private int _offset;
        private int _length = 2;
        private bool _signed;
        private double _scale = 1;
        private double _offsetValue;

        public Mode22SignalDefinitionBuilder WithOffset(int v) { _offset = v; return this; }
        public Mode22SignalDefinitionBuilder WithLength(int v) { _length = v; return this; }
        public Mode22SignalDefinitionBuilder WithSigned(bool v) { _signed = v; return this; }
        public Mode22SignalDefinitionBuilder WithScale(double v) { _scale = v; return this; }
        public Mode22SignalDefinitionBuilder WithOffsetValue(double v) { _offsetValue = v; return this; }

        public Mode22SignalDefinition Build() => new()
        {
            Key = "test",
            Name = "Test",
            Did = 0x1234,
            ByteOffset = _offset,
            ByteLength = _length,
            Signed = _signed,
            Scale = _scale,
            Offset = _offsetValue,
        };
    }
}
