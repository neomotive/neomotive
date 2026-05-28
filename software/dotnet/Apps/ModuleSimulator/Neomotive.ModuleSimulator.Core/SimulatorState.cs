using Meadow.Foundation.Telematics.J1979;

namespace Neomotive.ModuleSimulator;

public record KnownDtc(string Code, string Description, byte[] RawBytes);

public class SimulatorState
{
    public string Vin { get; set; } = "AWWWWWWWWWWW0YEAH";
    public double CoolantTempCelsius { get; set; } = 90.0;
    public float Rpm { get; set; } = 800f;
    public double SpeedKph { get; set; } = 0.0;
    public float ThrottlePercent { get; set; } = 0f;

    // PCM time/distance metrics
    public DateTime EngineStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime DtcsClearedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan TimeWithMilOn { get; set; } = TimeSpan.Zero;
    public double DistanceSinceDtcsClearedKm { get; set; } = 0.0;
    public double DistanceWithMilOnKm { get; set; } = 0.0;

    public EmissionsReadinessStatus Readiness { get; } = new EmissionsReadinessStatus();

    // all keyed by uppercase code string, e.g. "P0300"
    public readonly Dictionary<string, byte[]> StoredDtcs = new();
    public readonly Dictionary<string, byte[]> PendingDtcs = new();
    public readonly Dictionary<string, byte[]> PermanentDtcs = new();

    public SimulatorState(SimulatorInputs inputs)
    {
        Inputs = inputs;
    }

    public SimulatorInputs Inputs { get; }

    public static readonly KnownDtc[] KnownDtcs =
    {
        new("P0300", "Random/Multiple Misfire",              new byte[] { 0x03, 0x00 }),
        new("P0171", "System Too Lean (Bank 1)",             new byte[] { 0x01, 0x71 }),
        new("P0420", "Catalyst Efficiency Below Threshold",  new byte[] { 0x04, 0x20 }),
        new("P0442", "EVAP System Small Leak",               new byte[] { 0x04, 0x42 }),
        new("P0507", "Idle Air Control RPM High",            new byte[] { 0x05, 0x07 }),
    };
}
