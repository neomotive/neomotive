namespace Neomotive.ModuleSimulator;

public class SimulatorTcuState : ISimulatorDtcStore
{
    public double TransTempCelsius { get; set; } = 80.0;
    public string GearPosition { get; set; } = "P";

    // all keyed by uppercase code string, e.g. "P0700"
    public Dictionary<string, byte[]> StoredDtcs { get; } = new();
    public Dictionary<string, byte[]> PendingDtcs { get; } = new();
    public Dictionary<string, byte[]> PermanentDtcs { get; } = new();

    public static readonly KnownDtc[] KnownDtcs =
    {
        new("P0700", "Transmission Control System",  new byte[] { 0x07, 0x00 }),
        new("P0715", "Input/Turbine Speed Sensor",   new byte[] { 0x07, 0x15 }),
        new("P0720", "Output Speed Sensor",          new byte[] { 0x07, 0x20 }),
        new("P0730", "Incorrect Gear Ratio",         new byte[] { 0x07, 0x30 }),
        new("P0741", "Torque Converter Clutch",      new byte[] { 0x07, 0x41 }),
    };
}
