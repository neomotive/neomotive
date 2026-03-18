using Meadow.Foundation.Telematics.OBD2;
using Meadow.Hardware;
using Meadow.Units;

namespace Neomotive.ModuleSimulator.Desktop;

public class SimulatorPcm : ControllerBase
{
    private readonly SimulatorState _state;

    public override string Vin => _state.Vin;
    public override string? EcuName => "NEOMOTIVE_PCM";

    public override Pid[] SupportedPids =>
    [
        Pid.MonitorStatus,
        Pid.EngineCoolantTemperature,
        Pid.EngineRpm,
        Pid.VehicleSpeed,
        Pid.ThrottlePosition,
    ];

    public SimulatorPcm(ICanBus canBus, SimulatorState state)
        : base([canBus], 0x7E8)
    {
        _state = state;
    }

    protected override Temperature? GetEngineCoolantTemperature()
        => new Temperature(_state.CoolantTempCelsius, Temperature.UnitType.Celsius);

    protected override float? GetEngineRpm()
        => _state.Rpm;

    protected override Speed? GetVehicleSpeed()
        => new Speed(_state.SpeedKph, Speed.UnitType.KilometersPerHour);

    protected override float? GetThrottlePosition()
        => _state.ThrottlePercent;

    protected override EmissionsReadinessStatus GetEmissionsReadiness() => _state.Readiness;

    protected override void OnDtcsCleared()
    {
        _state.StoredDtcs.Clear();
        _state.PendingDtcs.Clear();
        _state.PermanentDtcs.Clear();
    }

    public void SyncDtcsFromState()
    {
        ClearAllDtcs();
        foreach (var raw in _state.StoredDtcs.Values)
            SetDtc(new Dtc(raw));
        foreach (var raw in _state.PendingDtcs.Values)
            SetPendingDtc(new Dtc(raw));
        foreach (var raw in _state.PermanentDtcs.Values)
            SetPermanentDtc(new Dtc(raw));
    }
}
