namespace Neomotive.ModuleSimulator;

/// <summary>
/// The DTC stores a simulated module keeps, keyed by uppercase code ("P0300") with the raw
/// two-byte code as the value. Implemented by both <see cref="SimulatorState"/> and
/// <see cref="SimulatorTcuState"/> so UDS can mirror either module's faults.
/// </summary>
public interface ISimulatorDtcStore
{
    /// <summary>Confirmed faults — what Mode $03 reports.</summary>
    Dictionary<string, byte[]> StoredDtcs { get; }

    /// <summary>Faults seen once but not yet confirmed — Mode $07.</summary>
    Dictionary<string, byte[]> PendingDtcs { get; }

    /// <summary>Faults that survive a clear until the monitor re-runs — Mode $0A.</summary>
    Dictionary<string, byte[]> PermanentDtcs { get; }
}
