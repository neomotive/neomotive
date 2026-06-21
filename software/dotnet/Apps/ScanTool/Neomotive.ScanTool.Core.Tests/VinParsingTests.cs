using System.Text;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class VinParsingTests
{
    private static byte[] BuildVinResponse(string vin)
    {
        var vinBytes = Encoding.ASCII.GetBytes(vin);
        // Layout: [0x49, 0x02, 0x01, ...17 VIN bytes...]
        var response = new byte[3 + vinBytes.Length];
        response[0] = 0x49;
        response[1] = 0x02;
        response[2] = 0x01;
        Array.Copy(vinBytes, 0, response, 3, vinBytes.Length);
        return response;
    }

    [Fact]
    public void Valid_17_char_vin_parsed_correctly()
    {
        const string vin = "1HGCM82633A004352";
        var data = BuildVinResponse(vin);
        Assert.Equal(vin, Obd2Protocol.ParseVin(data));
    }

    [Fact]
    public void Short_response_returns_null()
    {
        Assert.Null(Obd2Protocol.ParseVin([0x49, 0x02]));
    }

    [Fact]
    public void Null_input_returns_null()
    {
        Assert.Null(Obd2Protocol.ParseVin(null!));
    }

    [Fact]
    public void Wrong_service_byte_returns_null()
    {
        var data = BuildVinResponse("1HGCM82633A004352");
        data[0] = 0x41; // wrong service
        Assert.Null(Obd2Protocol.ParseVin(data));
    }

    // ── ParseEcuName ────────────────────────────────────────────────────────────

    private static byte[] BuildEcuNameResponse(string name)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name.PadRight(20)[..20]);
        var response = new byte[3 + nameBytes.Length];
        response[0] = 0x49;
        response[1] = 0x0A;
        response[2] = 0x01;
        Array.Copy(nameBytes, 0, response, 3, nameBytes.Length);
        return response;
    }

    [Fact]
    public void ParseEcuName_returns_trimmed_name()
    {
        var data = BuildEcuNameResponse("NEOMOTIVE_PCM");
        Assert.Equal("NEOMOTIVE_PCM", Obd2Protocol.ParseEcuName(data));
    }

    [Fact]
    public void ParseEcuName_null_input_returns_null()
    {
        Assert.Null(Obd2Protocol.ParseEcuName(null!));
    }

    [Fact]
    public void ParseEcuName_short_response_returns_null()
    {
        Assert.Null(Obd2Protocol.ParseEcuName([0x49, 0x0A]));
    }

    [Fact]
    public void ParseEcuName_wrong_service_returns_null()
    {
        var data = BuildEcuNameResponse("NEOMOTIVE_PCM");
        data[0] = 0x41;
        Assert.Null(Obd2Protocol.ParseEcuName(data));
    }

    [Fact]
    public void ParseEcuName_wrong_pid_returns_null()
    {
        var data = BuildEcuNameResponse("NEOMOTIVE_PCM");
        data[1] = 0x02; // VIN PID, not ECU name
        Assert.Null(Obd2Protocol.ParseEcuName(data));
    }
}
