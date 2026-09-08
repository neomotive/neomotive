using Meadow.Hardware;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Meadow.Foundation.Telematics.Uds;
using Neomotive.Uds;

namespace Neomotive.Uds.Tests;

public class UdsScannerTests
{
    private const ushort PcmTx = 0x7E0;
    private const ushort PcmRx = 0x7E8;
    private const ushort BcmTx = 0x7E2;
    private const ushort BcmRx = 0x7EA;

    private static StandardDataFrame SingleFrame(ushort id, byte[] data)
    {
        var payload = new byte[8];
        payload[0] = (byte)data.Length;
        Array.Copy(data, 0, payload, 1, Math.Min(7, data.Length));
        return new StandardDataFrame { ID = (short)id, Payload = payload };
    }

    [Fact]
    public async Task ReadModuleDtcsAsync_SendsService19Sub02_ParsesDtcList()
    {
        var bus = new FakeCanBus();
        var scanner = new UdsScanner(bus, new UdsCatalog());

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // 0x59, 0x02, 0xFF, P0100 (0x01, 0x00), FTB=0x11, Status=0x09
            bus.InjectFrame(SingleFrame(PcmRx, [0x59, 0x02, 0xFF, 0x01, 0x00, 0x11, 0x09]));
        });

        var dtcs = await scanner.ReadModuleDtcsAsync(PcmTx, PcmRx);

        Assert.Single(dtcs);
        Assert.Equal("P0100-11", dtcs[0].FullCode);
        Assert.True(dtcs[0].IsActive);
        Assert.True(dtcs[0].IsConfirmed);

        // Verify sent frame
        Assert.True(bus.SentFrames.Count >= 1);
        var req = bus.SentFrames[0];
        Assert.Equal((short)PcmTx, req.ID);
        Assert.Equal(0x03, req.Payload[0]); // 3 bytes
        Assert.Equal(0x19, req.Payload[1]); // Service $19
        Assert.Equal(0x02, req.Payload[2]); // Subfunction $02
        Assert.Equal(0xFF, req.Payload[3]); // Status mask 0xFF
    }

    [Fact]
    public async Task ClearModuleDtcsAsync_SendsService14_ReturnsTrueOnPositiveResponse()
    {
        var bus = new FakeCanBus();
        var scanner = new UdsScanner(bus, new UdsCatalog());

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // 0x54: Positive response to ClearDiagnosticInformation ($14)
            bus.InjectFrame(SingleFrame(PcmRx, [0x54]));
        });

        var ok = await scanner.ClearModuleDtcsAsync(PcmTx, PcmRx);

        Assert.True(ok);
        Assert.True(bus.SentFrames.Count >= 1);
        var req = bus.SentFrames[0];
        Assert.Equal((short)PcmTx, req.ID);
        Assert.Equal(0x04, req.Payload[0]);
        Assert.Equal(0x14, req.Payload[1]);
        Assert.Equal(0xFF, req.Payload[2]);
        Assert.Equal(0xFF, req.Payload[3]);
        Assert.Equal(0xFF, req.Payload[4]);
    }

    [Fact]
    public async Task ClearAllDtcsAsync_BroadcastsTo7DF()
    {
        var bus = new FakeCanBus();
        var scanner = new UdsScanner(bus, new UdsCatalog());

        var ok = await scanner.ClearAllDtcsAsync();

        Assert.True(ok);
        Assert.True(bus.SentFrames.Count >= 1);
        var req = bus.SentFrames[0];
        Assert.Equal(0x7DF, req.ID);
        Assert.Equal(0x14, req.Payload[1]);
    }

    [Fact]
    public async Task ReadDidAsync_SendsService22_ParsesDecodedDid()
    {
        var bus = new FakeCanBus();
        var scanner = new UdsScanner(bus, new UdsCatalog());

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // 0x62, 0xF1, 0x87, ASCII "1234"
            byte[] part = Encoding.ASCII.GetBytes("1234");
            byte[] resp = new byte[3 + part.Length];
            resp[0] = 0x62;
            resp[1] = 0xF1;
            resp[2] = 0x87;
            part.CopyTo(resp, 3);
            bus.InjectFrame(SingleFrame(PcmRx, resp));
        });

        var didVal = await scanner.ReadDidAsync(PcmTx, PcmRx, 0xF187);

        Assert.NotNull(didVal);
        Assert.Equal(0xF187, didVal.Did);
        Assert.Equal("1234", didVal.DisplayValue);
        Assert.Equal("ECU Spare Part Number", didVal.Name);
    }

    [Fact]
    public async Task SetDiagnosticSessionAsync_SendsService10_ReturnsTrueOnSuccess()
    {
        var bus = new FakeCanBus();
        var scanner = new UdsScanner(bus, new UdsCatalog());

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // 0x50 (Positive response to $10), 0x03 (Extended session echo)
            bus.InjectFrame(SingleFrame(PcmRx, [0x50, 0x03]));
        });

        var ok = await scanner.SetDiagnosticSessionAsync(PcmTx, PcmRx, UdsSessionType.ExtendedDiagnosticSession);

        Assert.True(ok);
        Assert.True(bus.SentFrames.Count >= 1);
        var req = bus.SentFrames[0];
        Assert.Equal((short)PcmTx, req.ID);
        Assert.Equal(0x10, req.Payload[1]);
        Assert.Equal(0x03, req.Payload[2]);
    }

    [Fact]
    public async Task SendTesterPresentAsync_SendsService3E()
    {
        var bus = new FakeCanBus();
        var scanner = new UdsScanner(bus, new UdsCatalog());

        await scanner.SendTesterPresentAsync(PcmTx, suppressResponse: true);

        Assert.True(bus.SentFrames.Count >= 1);
        var req = bus.SentFrames[0];
        Assert.Equal((short)PcmTx, req.ID);
        Assert.Equal(0x3E, req.Payload[1]);
        Assert.Equal(0x80, req.Payload[2]);
    }

    [Fact]
    public async Task ReadModuleDtcsAsync_MultiFrameIsoTp_AssemblesProperly()
    {
        var bus = new FakeCanBus();
        var scanner = new UdsScanner(bus, new UdsCatalog());

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // Multi-frame: First frame total 11 bytes (0x0B)
            // 0x10 0x0B 0x59 0x02 0xFF 0x01 0x00 0x11
            bus.InjectFrame(new StandardDataFrame
            {
                ID = (short)PcmRx,
                Payload = [0x10, 0x0B, 0x59, 0x02, 0xFF, 0x01, 0x00, 0x11]
            });

            // Wait for Flow Control from scanner
            await Task.Delay(20);

            // Consecutive frame: Sequence 1, [Status=0x09, P0300 (0x03, 0x00), FTB=0x13, Status=0x04]
            bus.InjectFrame(new StandardDataFrame
            {
                ID = (short)PcmRx,
                Payload = [0x21, 0x09, 0x03, 0x00, 0x13, 0x04, 0xAA, 0xAA]
            });
        });

        var dtcs = await scanner.ReadModuleDtcsAsync(PcmTx, PcmRx);

        Assert.Equal(2, dtcs.Count);
        Assert.Equal("P0100-11", dtcs[0].FullCode);
        Assert.Equal("P0300-13", dtcs[1].FullCode);
        Assert.True(dtcs[1].IsPending);

        // Verify flow control was sent
        Assert.Contains(bus.SentFrames, f => f.ID == (short)PcmTx && (f.Payload[0] & 0xF0) == 0x30);
    }
}
