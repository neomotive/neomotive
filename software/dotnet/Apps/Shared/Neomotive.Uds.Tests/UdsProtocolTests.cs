using System.Linq;
using Xunit;
using Meadow.Foundation.Telematics.Uds;
using Neomotive.Uds;

namespace Neomotive.Uds.Tests;

public class UdsProtocolTests
{
    private static readonly UdsCatalog Catalog = new();

    [Theory]
    [InlineData(0x01, 0x00, "P0100")]
    [InlineData(0x03, 0x00, "P0300")]
    [InlineData(0x41, 0x23, "C0123")]
    [InlineData(0x80, 0x01, "B0001")]
    [InlineData(0xC1, 0x00, "U0100")]
    public void DecodeBaseDtc_ValidCodes_ReturnsExpectedPrefixAndNumber(byte hi, byte lo, string expected)
    {
        var result = UdsProtocol.DecodeBaseDtc(hi, lo);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetFaultTypeDescription_KnownAndUnknownFtbs()
    {
        Assert.Equal("No Subtype Information", Catalog.GetFaultTypeDescription(0x00));
        Assert.Equal("Circuit Short to Ground", Catalog.GetFaultTypeDescription(0x11));
        Assert.Equal("Circuit Short to Battery", Catalog.GetFaultTypeDescription(0x12));
        Assert.Equal("Circuit Open", Catalog.GetFaultTypeDescription(0x13));
        Assert.Equal("Signal Stuck Low", Catalog.GetFaultTypeDescription(0x23));
        Assert.Equal("General Checksum Failure", Catalog.GetFaultTypeDescription(0x41));
        Assert.Equal("Bus Off", Catalog.GetFaultTypeDescription(0x88));
        Assert.Equal("Failure Type 0xEE", Catalog.GetFaultTypeDescription(0xEE));
    }

    [Fact]
    public void ParseDtcResponse_SingleDtc_ParsesCorrectly()
    {
        // 0x59 (Pos resp to $19), 0x02 (subfunc), 0xFF (avail mask),
        // Record 1: 0x01, 0x00 (P0100), 0x11 (Short to ground), 0x2F (TestFailed | Pending | Confirmed | TestFailedSinceLastClear)
        byte[] payload = [0x59, 0x02, 0xFF, 0x01, 0x00, 0x11, 0x2F];

        var dtcs = UdsProtocol.ParseDtcResponse(payload, Catalog);

        Assert.Single(dtcs);
        var dtc = dtcs[0];
        Assert.Equal("P0100", dtc.BaseCode);
        Assert.Equal(0x11, dtc.FaultType);
        Assert.Equal("Circuit Short to Ground", dtc.FaultTypeDescription);
        Assert.Equal("P0100-11", dtc.FullCode);
        Assert.True(dtc.IsActive);
        Assert.True(dtc.IsPending);
        Assert.True(dtc.IsConfirmed);
    }

    [Fact]
    public void ParseDtcResponse_MultipleDtcs_ParsesAllRecords()
    {
        byte[] payload =
        [
            0x59, 0x02, 0xFF,
            0x01, 0x00, 0x11, 0x09, // P0100-11 (TestFailed | Confirmed)
            0xC1, 0x00, 0x87, 0x04, // U0100-87 (Missing message, Pending)
            0x41, 0x23, 0x13, 0x89  // C0123-13 (Circuit open, Active | Confirmed | MIL)
        ];

        var dtcs = UdsProtocol.ParseDtcResponse(payload, Catalog);

        Assert.Equal(3, dtcs.Count);

        Assert.Equal("P0100-11", dtcs[0].FullCode);
        Assert.True(dtcs[0].IsActive);
        Assert.True(dtcs[0].IsConfirmed);
        Assert.False(dtcs[0].IsWarning);

        Assert.Equal("U0100-87", dtcs[1].FullCode);
        Assert.Equal("Missing Message", dtcs[1].FaultTypeDescription);
        Assert.True(dtcs[1].IsPending);
        Assert.False(dtcs[1].IsConfirmed);

        Assert.Equal("C0123-13", dtcs[2].FullCode);
        Assert.True(dtcs[2].IsActive);
        Assert.True(dtcs[2].IsConfirmed);
        Assert.True(dtcs[2].IsWarning);
    }

    [Fact]
    public void ParseDtcResponse_InvalidOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(UdsProtocol.ParseDtcResponse(null));
        Assert.Empty(UdsProtocol.ParseDtcResponse([]));
        Assert.Empty(UdsProtocol.ParseDtcResponse([0x59, 0x02]));
        Assert.Empty(UdsProtocol.ParseDtcResponse([0x7F, 0x19, 0x12]));
    }

    [Fact]
    public void ParseDidResponse_AsciiVin_DecodesString()
    {
        // 0x62, DID_HI=0xF1, DID_LO=0x90, VIN="1HGCR2F83HA000000"
        byte[] vinBytes = System.Text.Encoding.ASCII.GetBytes("1HGCR2F83HA000000");
        byte[] payload = new byte[3 + vinBytes.Length];
        payload[0] = 0x62;
        payload[1] = 0xF1;
        payload[2] = 0x90;
        vinBytes.CopyTo(payload, 3);

        var result = UdsProtocol.ParseDidResponse(0xF190, payload, Catalog);

        Assert.NotNull(result);
        Assert.Equal(0xF190, result.Did);
        Assert.Equal("Vehicle Identification Number (VIN)", result.Name);
        Assert.Equal("1HGCR2F83HA000000", result.DisplayValue);
    }

    [Fact]
    public void ParseDidResponse_ActiveSession_FormatsSessionName()
    {
        byte[] payload = [0x62, 0xF1, 0x86, 0x03];

        var result = UdsProtocol.ParseDidResponse(0xF186, payload, Catalog);

        Assert.NotNull(result);
        Assert.Equal("Extended Diagnostic Session (0x03)", result.DisplayValue);
    }

    [Fact]
    public void TryParseNegativeResponse_ValidNrc_ReturnsDecodedError()
    {
        byte[] payload = [0x7F, 0x22, 0x31]; // ReadDataByIdentifier ($22) -> RequestOutOfRange ($31)

        bool isNrc = UdsProtocol.TryParseNegativeResponse(payload, out byte reqSvc, out UdsNrc nrc, out string desc, Catalog);

        Assert.True(isNrc);
        Assert.Equal(0x22, reqSvc);
        Assert.Equal(UdsNrc.RequestOutOfRange, nrc);
        Assert.Contains("Request Out Of Range", desc);
    }

    [Fact]
    public void TryParseNegativeResponse_PositiveResponse_ReturnsFalse()
    {
        byte[] payload = [0x62, 0xF1, 0x90, 0x31];

        bool isNrc = UdsProtocol.TryParseNegativeResponse(payload, out _, out _, out _);

        Assert.False(isNrc);
    }
}
