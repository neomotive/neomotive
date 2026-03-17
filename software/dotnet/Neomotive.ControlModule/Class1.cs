using Meadow.Foundation.Telematics.OBD2;
using Meadow.Hardware;
using Meadow.Units;
using System.Text;

namespace Neomotive.ControlModule;

public interface IController
{
}

public abstract class ControllerBase(short ModuleAddress) : IController
{
    public const short TesterAddress = 0x7E0;

    private readonly List<CanBusMonitor> _busMonitors = new();
    private byte[]? _supportedPidMask;

    public abstract string Vin { get; }
    public abstract Pid[] SupportedPids { get; }

    private byte[] SupportedPidMask
    {
        get
        {
            if (_supportedPidMask is null)
            {
                uint mask = 0;
                foreach (var pid in SupportedPids)
                {
                    mask |= 1u << (32 - (byte)pid);
                }
                var maskBytes = BitConverter.GetBytes(mask);
                if (BitConverter.IsLittleEndian) Array.Reverse(maskBytes);
                _supportedPidMask = maskBytes;
            }
            return _supportedPidMask;
        }
    }

    protected ControllerBase(ICanBus[] canBuses, short moduleAddress) : this(moduleAddress)
    {
        foreach (var canBus in canBuses)
        {
            var monitor = new CanBusMonitor(canBus);
            monitor.QueryReceived += (_, query) => OnQueryReceived(monitor.Bus, query);
            _busMonitors.Add(monitor);
        }
    }

    private void OnQueryReceived(ICanBus sourceBus, Obd2QueryFrame queryFrame)
    {
        // TODO: raise an event
        Console.WriteLine($"[PCM] Query received: Service=0x{(byte)queryFrame.Service:X2}");

        if (queryFrame is SaeStandardQueryFrame saeQuery)
        {
            HandleSaeQuery(sourceBus, saeQuery);
        }
    }

    private void HandleSaeQuery(ICanBus bus, SaeStandardQueryFrame query)
    {
        Console.WriteLine($"[PCM]   SAE: Service=0x{(byte)query.Service:X2} PID=0x{(byte)query.Pid:X2}");

        switch (query.Service)
        {
            case Service.Current:
                HandleService01(bus, query.Pid);
                break;
            case Service.VehicleInfo:
                //HandleService09(bus, query.Pid);
                break;
        }
    }

    private void HandleService01(ICanBus bus, Pid pid)
    {
        switch (pid)
        {
            case Pid.SupportedPids_01_20:
                SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, SupportedPidMask, ModuleAddress));
                break;
            case Pid.SupportedPids_21_40:
                // not implemented - no PIDs > 0x20 supported
                break;

            case Pid.EngineCoolantTemperature:
                var temp = GetEngineCoolantTemperature();
                if (temp.HasValue)
                {
                    // A - 40 = °C
                    var tempValue = (byte)(temp.Value.Celsius + 40);
                    SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, [tempValue], ModuleAddress));
                }
                break;

            case Pid.EngineRpm:
                var rpm = GetEngineRpm();
                if (rpm.HasValue)
                {
                    // (A*256 + B) / 4 = RPM
                    var raw = (ushort)(rpm.Value * 4);
                    SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, [(byte)(raw >> 8), (byte)(raw & 0xFF)], ModuleAddress));
                }
                break;

            case Pid.VehicleSpeed:
                var speed = GetVehicleSpeed();
                if (speed.HasValue)
                {
                    // A = km/h
                    SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, [(byte)speed.Value.KilometersPerHour], ModuleAddress));
                }
                break;

            case Pid.ThrottlePosition:
                var throttle = GetThrottlePosition();
                if (throttle.HasValue)
                {
                    // A * 100/255 = %
                    SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, [(byte)(throttle.Value * 255f / 100f)], ModuleAddress));
                }
                break;
        }
    }

    private void HandleService09(ICanBus bus, Pid pid)
    {
        switch (pid)
        {
            case Pid.SupportedPids_01_20:
                // Service 09 supported PIDs bitmask: bit 30 = PID 02 (VIN)
                uint mask = 0;
                mask |= 1u << (32 - 0x02); // VIN
                var maskBytes = BitConverter.GetBytes(mask);
                if (BitConverter.IsLittleEndian) Array.Reverse(maskBytes);
                SendResponse(bus, new Obd2ResponseFrame(Service.VehicleInfo, pid, maskBytes, ModuleAddress));
                break;

            case (Pid)0x02: // VIN
                // Payload: [0x49, 0x02, 0x01, VIN bytes (17)]
                var vinBytes = Encoding.ASCII.GetBytes(Vin.PadRight(17).Substring(0, 17));
                var payload = new byte[3 + vinBytes.Length];
                payload[0] = 0x49; // Service 09 response (0x09 | 0x40)
                payload[1] = 0x02; // PID
                payload[2] = 0x01; // message count
                Array.Copy(vinBytes, 0, payload, 3, vinBytes.Length);
                _ = SendIsoTpResponse(bus, ModuleAddress, TesterAddress, payload);
                break;
        }
    }

    protected virtual Temperature? GetEngineCoolantTemperature() => null;
    protected virtual float? GetEngineRpm() => null;
    protected virtual Speed? GetVehicleSpeed() => null;
    protected virtual float? GetThrottlePosition() => null; // percent 0-100

    protected void SendResponse(ICanBus bus, Obd2ResponseFrame response)
    {
        bus.WriteFrame(response);
    }

    protected async Task SendIsoTpResponse(ICanBus bus, short ecuAddress, short testerAddress, byte[] data)
    {
        var frames = IsoTp.Encode(data);

        if (frames.Length == 1)
        {
            // Single frame — set address and send
            if (frames[0] is StandardDataFrame sf)
            {
                sf.ID = ecuAddress;
                bus.WriteFrame(sf);
            }
            return;
        }

        // Multi-frame: send First Frame, wait for Flow Control, send Consecutive Frames
        if (frames[0] is not StandardDataFrame firstFrame) return;
        firstFrame.ID = ecuAddress;
        bus.WriteFrame(firstFrame);

        // Wait for Flow Control (scanner sends to tester address, e.g. 0x7E0)
        var tcs = new TaskCompletionSource<bool>();
        EventHandler<ICanFrame>? fcHandler = null;
        fcHandler = (_, frame) =>
        {
            if (frame is StandardDataFrame sdf &&
                sdf.ID == testerAddress &&
                (sdf.Payload[0] & 0xF0) == 0x30) // Flow Control frame type
            {
                bus.FrameReceived -= fcHandler;
                tcs.TrySetResult(true);
            }
        };
        bus.FrameReceived += fcHandler;

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        if (completed != tcs.Task)
        {
            bus.FrameReceived -= fcHandler;
            Console.WriteLine("[PCM] ISO-TP: timeout waiting for flow control");
            return;
        }

        // Send Consecutive Frames
        for (int i = 1; i < frames.Length; i++)
        {
            if (frames[i] is StandardDataFrame cf)
            {
                cf.ID = ecuAddress;
                bus.WriteFrame(cf);
                await Task.Delay(1);
            }
        }
    }
}
