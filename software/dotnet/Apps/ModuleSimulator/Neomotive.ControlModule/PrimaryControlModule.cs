using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;
using Meadow.Units;

namespace Neomotive.ControlModule;

public class PrimaryControlModule : ControllerBase
{
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

    public void ReportFault(Dtc fault) => SetDtc(fault);
    public void ClearFault(Dtc fault) => ClearDtc(fault);

}
