using Meadow.Foundation.Telematics.OBD2;
using Meadow.Hardware;
using Meadow.Units;
using Neomotive.ControlModule;

namespace Neomotive.ModuleSimulator.Desktop;

public class SimulatorTcu : TransmissionControlModule
{
    private readonly SimulatorTcuState _state;

    public override string Vin => "AWWWWWWWWWWW0YEAH";

    public override Pid[] SupportedPids =>
    [
        Pid.MonitorStatus,
        Pid.EngineOilTemperature,  // used as trans fluid temp (PID 0x5C, A-40=°C)
    ];

    public SimulatorTcu(ICanBus canBus, SimulatorTcuState state)
        : base([canBus], 0x7E9)
    {
        _state = state;
    }

    protected override Temperature? GetTransFluidTemp()
        => new Temperature(_state.TransTempCelsius, Temperature.UnitType.Celsius);

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
