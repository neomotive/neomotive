using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;
using Meadow.Units;

namespace Neomotive.ModuleSimulator;

public class SimulatorPcm : PcmBase
{
    private readonly SimulatorState _state;

    public override string Vin => _state.Vin;
    public override string? EcuName => "NEOMOTIVE_PCM";

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

    protected override EmissionsReadinessStatus GetEmissionsReadiness()
        => _state.Readiness;

    protected override TimeSpan GetTimeSinceEndineStarted()
        => DateTime.UtcNow - _state.EngineStartedAt;

    protected override TimeSpan GetTimeWithMilOn()
        => _state.TimeWithMilOn;

    protected override TimeSpan GetTimeSinceDtcsCleared()
        => DateTime.UtcNow - _state.DtcsClearedAt;

    protected override Length GetDistanceSinceDtcsCleared()
        => new Length(_state.DistanceSinceDtcsClearedKm, Length.UnitType.Kilometers);

    protected override Length GetDistanceWithMilOn()
        => new Length(_state.DistanceWithMilOnKm, Length.UnitType.Kilometers);

    protected override void OnDtcsCleared()
    {
        _state.StoredDtcs.Clear();
        _state.PendingDtcs.Clear();
        _state.PermanentDtcs.Clear();
        _state.DtcsClearedAt = DateTime.UtcNow;
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
