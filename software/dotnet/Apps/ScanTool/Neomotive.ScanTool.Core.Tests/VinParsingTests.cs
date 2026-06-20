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
}
