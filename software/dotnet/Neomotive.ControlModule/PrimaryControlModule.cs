using Meadow.Foundation.Telematics.OBD2;
using Meadow.Hardware;
using Meadow.Units;

namespace Neomotive.ControlModule;

public class PrimaryControlModule : ControllerBase
{
    // Standard PCM/ECM CAN addresses
    public const short TesterAddress = 0x7E0;

    //    public string Vin { get; set; } = "1NEOMOTIVE000TEST";
    public override string Vin { get; } = "AWWWWWWWWWWW0YEAH";

    public override Pid[] SupportedPids =>
        [
            Pid.EngineCoolantTemperature,
            Pid.EngineRpm,
            Pid.VehicleSpeed,
            Pid.ThrottlePosition
        ];

    public PrimaryControlModule(ICanBus canBuses)
        : this([canBuses], 0x7E8)
    {
    }

    public PrimaryControlModule(ICanBus[] canBuses, short moduleAddress)
        : base(canBuses, moduleAddress)
    {
    }

    protected override Temperature? GetEngineCoolantTemperature()
        => new Temperature(50, Temperature.UnitType.Fahrenheit);

    protected override float? GetEngineRpm()
        => 768f;

    protected override Speed? GetVehicleSpeed()
        => new Speed(0, Speed.UnitType.KilometersPerHour);

    protected override float? GetThrottlePosition()
        => 0f;

    /*
    protected override void OnQueryReceived(ICanBus sourceBus, Obd2QueryFrame queryFrame)
    {
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
                HandleService09(bus, query.Pid);
                break;
        }
    }

    private void HandleService01(ICanBus bus, Pid pid)
    {
        switch (pid)
        {
            case Pid.SupportedPids_01_20:
                SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, _supportedPidMask, ModuleAddress));
                break;

            case Pid.EngineCoolantTemperature:
                // A - 40 = °C; 0x60 = 56°C
                SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, new byte[] { 0x60 }, ModuleAddress));
                break;

            case Pid.EngineRpm:
                // (A*256 + B) / 4 = RPM; 0x0C, 0x00 = 768 RPM
                SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, new byte[] { 0x0C, 0x00 }, ModuleAddress));
                break;

            case Pid.VehicleSpeed:
                // A = km/h
                SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, new byte[] { 0x00 }, ModuleAddress));
                break;

            case Pid.ThrottlePosition:
                // A * 100/255 = %
                SendResponse(bus, new Obd2ResponseFrame(Service.Current, pid, new byte[] { 0x00 }, ModuleAddress));
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
    */
}
