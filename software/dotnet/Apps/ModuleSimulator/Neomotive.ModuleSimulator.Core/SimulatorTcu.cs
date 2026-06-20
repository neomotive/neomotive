using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;
using Meadow.Units;
using Neomotive.ControlModule;
using System.Linq;

namespace Neomotive.ModuleSimulator;

public class SimulatorTcu : TransmissionControlModule
{
    private readonly SimulatorTcuState _state;

    public override string Vin => "AWWWWWWWWWWW0YEAH";
    public override string? EcuName => "NEOMOTIVE_TCU";

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

    public event Action? DtcsCleared;

    protected override void OnDtcsCleared()
    {
        _state.StoredDtcs.Clear();
        _state.PendingDtcs.Clear();
        _state.PermanentDtcs.Clear();
        DtcsCleared?.Invoke();
    }

    public void SyncDtcsFromState()
    {
        foreach (var dtc in GetStoredDtcs().ToList())    ClearDtc(dtc);
        foreach (var dtc in GetPendingDtcs().ToList())   ClearPendingDtc(dtc);
        foreach (var dtc in GetPermanentDtcs().ToList()) ClearPermanentDtc(dtc);

        foreach (var raw in _state.StoredDtcs.Values)    SetDtc(new Dtc(raw));
        foreach (var raw in _state.PendingDtcs.Values)   SetPendingDtc(new Dtc(raw));
        foreach (var raw in _state.PermanentDtcs.Values) SetPermanentDtc(new Dtc(raw));
    }
}
