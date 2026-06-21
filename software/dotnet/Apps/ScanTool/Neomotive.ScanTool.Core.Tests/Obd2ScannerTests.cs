using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;
using System.Text;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class Obd2ScannerTests
{
    private const short EcuResponseId = 0x7E8;
    private const short EcuPhysicalId = 0x7E0;
    private const short RequestId = 0x7DF;

    private static StandardDataFrame SingleFrame(short id, byte[] data)
    {
        var payload = new byte[8];
        payload[0] = (byte)data.Length; // single-frame: nibble 0 + length
        Array.Copy(data, 0, payload, 1, data.Length);
        return new StandardDataFrame { ID = id, Payload = payload };
    }

    [Fact]
    public async Task ReadStoredDtcsAsync_sends_mode03_request()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        // Inject a Mode $43 response with 0 DTCs immediately after request
        bus.FrameReceived += (_, _) => { };
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            bus.InjectFrame(SingleFrame(EcuResponseId, [0x43, 0x00]));
        });

        await scanner.ReadStoredDtcsAsync();

        Assert.True(bus.SentFrames.Count >= 1);
        var req = bus.SentFrames[0];
        Assert.Equal(RequestId, req.ID);
        Assert.Equal(0x01, req.Payload[0]); // 1 data byte
        Assert.Equal(0x03, req.Payload[1]); // mode $03
    }

    [Fact]
    public async Task ReadStoredDtcsAsync_parses_two_dtcs()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // 2 DTCs: P0300 (03 00) and P0420 (04 20)
            bus.InjectFrame(SingleFrame(EcuResponseId, [0x43, 0x02, 0x03, 0x00, 0x04, 0x20]));
        });

        var dtcs = await scanner.ReadStoredDtcsAsync();

        Assert.Equal(2, dtcs.Count);
        Assert.Equal("P0300", dtcs[0].Code);
        Assert.Equal("P0420", dtcs[1].Code);
        Assert.All(dtcs, d => Assert.Equal(DtcStatus.Stored, d.Status));
    }

    [Fact]
    public async Task ReadPendingDtcsAsync_sends_mode07_request()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            bus.InjectFrame(SingleFrame(EcuResponseId, [0x47, 0x00]));
        });

        await scanner.ReadPendingDtcsAsync();

        var req = bus.SentFrames[0];
        Assert.Equal(0x07, req.Payload[1]); // mode $07
    }

    [Fact]
    public async Task ClearDtcsAsync_sends_mode04_request()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        await scanner.ClearDtcsAsync();

        Assert.Single(bus.SentFrames);
        var req = bus.SentFrames[0];
        Assert.Equal(RequestId, req.ID);
        Assert.Equal(0x01, req.Payload[0]);
        Assert.Equal(0x04, req.Payload[1]);
    }

    [Fact]
    public async Task ReadReadinessAsync_parses_monitor_bytes()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // Response: 41 01 A B C D — all supported, all complete
            bus.InjectFrame(SingleFrame(EcuResponseId, [0x41, 0x01, 0x00, 0x07, 0xFF, 0x00]));
        });

        var monitors = await scanner.ReadReadinessAsync();

        Assert.Equal(11, monitors.Count);
        Assert.All(monitors, m => { if (m.Supported) Assert.True(m.Ready); });
    }

    [Fact]
    public async Task ReadVinAsync_sends_flow_control_and_parses_vin()
    {
        const string expectedVin = "1HGCM82633A004352";
        var vinBytes = Encoding.ASCII.GetBytes(expectedVin);

        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);

            // First frame: total=20 bytes, first 6 bytes of data (49 02 01 + 3 VIN chars)
            var firstPayload = new byte[8];
            firstPayload[0] = 0x10; // first frame, high nibble of length = 0
            firstPayload[1] = 20;   // total length: 49 02 01 + 17 vin = 20
            firstPayload[2] = 0x49; firstPayload[3] = 0x02; firstPayload[4] = 0x01;
            firstPayload[5] = vinBytes[0]; firstPayload[6] = vinBytes[1]; firstPayload[7] = vinBytes[2];
            bus.InjectFrame(new StandardDataFrame { ID = EcuResponseId, Payload = firstPayload });

            await Task.Delay(10); // let flow control be sent

            // Consecutive frame 1: next 7 bytes of VIN (indices 3-9)
            var cf1 = new byte[8];
            cf1[0] = 0x21;
            Array.Copy(vinBytes, 3, cf1, 1, 7);
            bus.InjectFrame(new StandardDataFrame { ID = EcuResponseId, Payload = cf1 });

            // Consecutive frame 2: remaining 7 bytes of VIN (indices 10-16)
            var cf2 = new byte[8];
            cf2[0] = 0x22;
            Array.Copy(vinBytes, 10, cf2, 1, 7);
            bus.InjectFrame(new StandardDataFrame { ID = EcuResponseId, Payload = cf2 });
        });

        var vin = await scanner.ReadVinAsync();

        Assert.Equal(expectedVin, vin);

        // Flow control must have been sent to ECU physical address
        var fcFrame = bus.SentFrames.FirstOrDefault(f => f.ID == EcuPhysicalId);
        Assert.NotNull(fcFrame);
        Assert.Equal(0x30, fcFrame.Payload[0]);
    }

    [Fact]
    public async Task No_response_returns_null_after_timeout()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        // Don't inject any response — scanner should time out gracefully
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await scanner.ReadVinAsync(cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadPidAsync_single_byte_vehicle_speed_scales_correctly()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // Vehicle speed = 100 km/h (0x64), scale=1, offset=0
            bus.InjectFrame(SingleFrame(EcuResponseId, [0x41, 0x0D, 0x64]));
        });

        var result = await scanner.ReadPidAsync(Pid.VehicleSpeed);

        Assert.NotNull(result);
        Assert.Equal(100.0, result!.Value, precision: 1);
        Assert.Equal("km/h", result.Descriptor.Unit);
    }

    [Fact]
    public async Task ReadPidAsync_two_byte_engine_rpm_scales_correctly()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // RPM = 850: raw = 850 / 0.25 = 3400 = 0x0D48
            bus.InjectFrame(SingleFrame(EcuResponseId, [0x41, 0x0C, 0x0D, 0x48]));
        });

        var result = await scanner.ReadPidAsync(Pid.EngineRpm);

        Assert.NotNull(result);
        Assert.Equal(850.0, result!.Value, precision: 1);
        Assert.Equal("RPM", result.Descriptor.Unit);
    }

    [Fact]
    public async Task ReadPidAsync_coolant_temp_applies_offset()
    {
        var bus = new FakeCanBus();
        var scanner = new Obd2Scanner(bus);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            // Coolant temp = 91°C: raw = 91 + 40 = 131 = 0x83
            bus.InjectFrame(SingleFrame(EcuResponseId, [0x41, 0x05, 0x83]));
        });

        var result = await scanner.ReadPidAsync(Pid.EngineCoolantTemperature);

        Assert.NotNull(result);
        Assert.Equal(91.0, result!.Value, precision: 1);
        Assert.Equal("°C", result.Descriptor.Unit);
    }
}
