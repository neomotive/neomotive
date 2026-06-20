using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class DtcDecodingTests
{
    [Fact]
    public void Zero_bytes_returns_null()
    {
        Assert.Null(Obd2Protocol.DecodeDtcCode(0x00, 0x00));
    }

    [Theory]
    [InlineData(0x01, 0x00, "P0100")]  // P category, sub 0, 100
    [InlineData(0x03, 0x00, "P0300")]  // P0300 random misfire
    [InlineData(0x04, 0x20, "P0420")]  // P0420 catalyst efficiency
    [InlineData(0x01, 0x71, "P0171")]  // P0171 system lean
    public void Powertrain_codes_decoded_correctly(byte hi, byte lo, string expected)
    {
        Assert.Equal(expected, Obd2Protocol.DecodeDtcCode(hi, lo));
    }

    [Theory]
    [InlineData(0x41, 0x23, "C0123")]  // C category
    [InlineData(0x81, 0x23, "B0123")]  // B category
    [InlineData(0xC1, 0x23, "U0123")]  // U category
    public void Non_powertrain_categories_decoded(byte hi, byte lo, string expected)
    {
        Assert.Equal(expected, Obd2Protocol.DecodeDtcCode(hi, lo));
    }

    [Fact]
    public void Manufacturer_code_subtype_decoded()
    {
        // P1XXX: hi byte = 0x10 = category P, subtype 1
        var result = Obd2Protocol.DecodeDtcCode(0x11, 0x23);
        Assert.Equal("P1123", result);
    }
}
