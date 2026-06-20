using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class ReadinessParsingTests
{
    [Fact]
    public void All_supported_and_complete_returns_all_ready()
    {
        // B = 0x07 (misfire+fuel+comprehensive supported), 0x00 (none incomplete)
        // C = 0xFF (all non-continuous supported), D = 0x00 (none incomplete)
        var monitors = Obd2Protocol.ParseReadiness(a: 0x00, b: 0x07, c: 0xFF, d: 0x00);

        Assert.All(monitors, m =>
        {
            if (m.Supported) Assert.True(m.Ready);
        });
    }

    [Fact]
    public void No_monitors_supported_all_not_supported()
    {
        var monitors = Obd2Protocol.ParseReadiness(a: 0x00, b: 0x00, c: 0x00, d: 0x00);

        Assert.All(monitors, m => Assert.False(m.Supported));
    }

    [Fact]
    public void Misfire_supported_but_incomplete()
    {
        // B bit0 = supported, B bit4 = incomplete
        var monitors = Obd2Protocol.ParseReadiness(a: 0x00, b: 0x11, c: 0x00, d: 0x00);
        var misfire = monitors[0]; // Misfire is always index 0

        Assert.True(misfire.Supported);
        Assert.False(misfire.Ready);
    }

    [Fact]
    public void Catalyst_supported_and_incomplete()
    {
        // C bit0 = catalyst supported, D bit0 = catalyst incomplete
        var monitors = Obd2Protocol.ParseReadiness(a: 0x00, b: 0x00, c: 0x01, d: 0x01);
        var catalyst = monitors[3]; // Catalyst is index 3

        Assert.True(catalyst.Supported);
        Assert.False(catalyst.Ready);
    }

    [Fact]
    public void Returns_eleven_monitors()
    {
        var monitors = Obd2Protocol.ParseReadiness(0, 0, 0, 0);
        Assert.Equal(11, monitors.Count);
    }
}
