using System.Collections.Generic;
using System.Linq;

namespace Neomotive.ModuleSimulator;

public enum InputType { Pot, Switch, Button }

/// <summary>
/// The physical/engineering unit family shared between PIDs and quick-pick presets.
/// This is the bridge: PIDs map to a PotUnitType; quick picks are grouped by PotUnitType.
/// Adding a new PID only requires updating PidUnitTypes. Adding a new unit only requires
/// updating QuickPicksByUnitType. Neither change touches the other.
/// </summary>
public enum PotUnitType
{
    Temperature,
    Speed,
    Rpm,
    Percentage,
    Pressure,
    Voltage,
    Lambda,
}

/// <summary>
/// Describes a PID that a controller exposes and that can be driven by a pot.
/// Unit type is intentionally absent here — it comes from PidUnitTypes lookup.
/// </summary>
public record SimulatorPidDef(
    string Key,      // matches SetSimulatedValue key, e.g. "coolant_temp"
    string Name,     // human name, e.g. "Coolant Temp"
    string PidCode,  // hex string, e.g. "0x05"
    string Module);  // "PCM" or "TCU"

/// <summary>
/// How to convert pot voltage to a physical value: physical = voltage × Scale + Offset.
/// ToNativeValue converts the displayed unit back to the simulator's native unit (e.g. °F → °C).
/// </summary>
public record PotChannel(string Key, string DisplayName, string Unit, double Scale, double Offset, PotUnitType UnitType)
{
    public double MapFromVoltage(double volts) => volts * Scale + Offset;
    public string Format(double value) => $"{value:F1} {Unit}";
    public string RangeDescription => $"{Format(Offset)} – {Format(5.0 * Scale + Offset)}";

    /// <summary>
    /// Converts the physical value (in the user's chosen unit) to the native unit the
    /// simulator expects: °C for temperature, km/h for speed, native for everything else.
    /// </summary>
    public double ToNativeValue(double physical) => UnitType switch
    {
        PotUnitType.Temperature => Unit == "°F" ? (physical - 32.0) * 5.0 / 9.0 : physical,
        PotUnitType.Speed       => Unit == "mph" ? physical * 1.60934 : physical,
        _                       => physical,
    };
}

public record BoolChannel(string Key, string DisplayName);

/// <summary>
/// A named scale/offset/unit preset for one-tap configuration.
/// No unit type here — presets are grouped by unit type in QuickPicksByUnitType.
/// </summary>
public record PotQuickPick(string Label, string Unit, double Scale, double Offset);

public static class InputChannelCatalog
{
    // ── PID definitions ───────────────────────────────────────────────────────

    /// <summary>
    /// All PIDs the simulator controllers expose that can be driven by a pot.
    /// Add new PIDs here and in PidUnitTypes — nothing else needs changing.
    /// </summary>
    public static readonly IReadOnlyList<SimulatorPidDef> SupportedPotPids =
    [
        new("coolant_temp", "Coolant Temp",     "0x05", "PCM"),
        new("rpm",          "Engine RPM",       "0x0C", "PCM"),
        new("speed",        "Vehicle Speed",    "0x0D", "PCM"),
        new("throttle",     "Throttle Pos",     "0x11", "PCM"),
        new("trans_temp",   "Trans Fluid Temp", "0x5C", "TCU"),
    ];

    // ── PID → unit type map ───────────────────────────────────────────────────

    /// <summary>
    /// Maps PID hex code to the engineering unit family it represents.
    /// This is the only place that needs updating when a PID is added or changed.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, PotUnitType> PidUnitTypes =
        new Dictionary<string, PotUnitType>
        {
            ["0x05"] = PotUnitType.Temperature,  // Engine Coolant Temp
            ["0x0C"] = PotUnitType.Rpm,          // Engine RPM
            ["0x0D"] = PotUnitType.Speed,        // Vehicle Speed
            ["0x11"] = PotUnitType.Percentage,   // Throttle Position
            ["0x5C"] = PotUnitType.Temperature,  // Engine Oil / Trans Fluid Temp
        };

    // ── Unit type → quick picks map ───────────────────────────────────────────

    /// <summary>
    /// Groups quick-pick presets by engineering unit type.
    /// Add a new unit family here independently of the PID list.
    /// physical = voltage × Scale + Offset  (voltage 0–5 V)
    /// </summary>
    public static readonly IReadOnlyDictionary<PotUnitType, IReadOnlyList<PotQuickPick>> QuickPicksByUnitType =
        new Dictionary<PotUnitType, IReadOnlyList<PotQuickPick>>
        {
            [PotUnitType.Temperature] =
            [
                new("°C",  "°C",  51.0, -40.0),   // -40 … 215 °C
                new("°F",  "°F",  91.8, -40.0),   // -40 … 419 °F
            ],
            [PotUnitType.Speed] =
            [
                new("km/h", "km/h", 40.0, 0.0),   //   0 … 200 km/h
                new("mph",  "mph",  25.0, 0.0),   //   0 … 125 mph
            ],
            [PotUnitType.Rpm] =
            [
                new("RPM", "rpm", 1600.0, 0.0),   //   0 … 8 000 rpm
            ],
            [PotUnitType.Percentage] =
            [
                new("%", "%", 20.0, 0.0),          //   0 … 100 %
            ],
            [PotUnitType.Pressure] =
            [
                new("kPa", "kPa", 50.0,  0.0),    //   0 … 250 kPa
                new("PSI", "PSI", 14.5,  0.0),    //   0 … 72.5 PSI
                new("bar", "bar",  0.5,  0.0),    //   0 … 2.5 bar
            ],
            [PotUnitType.Voltage] =
            [
                new("V (raw)", "V", 1.0, 0.0),    //   0 … 5 V
            ],
            [PotUnitType.Lambda] =
            [
                new("λ", "λ", 0.4, 0.5),           // 0.5 … 2.5 λ
            ],
        };

    // ── Lookup helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the quick picks appropriate for a given PID, or empty if the PID
    /// code is not in the unit-type map.
    /// </summary>
    public static IReadOnlyList<PotQuickPick> QuickPicksFor(SimulatorPidDef pid) =>
        PidUnitTypes.TryGetValue(pid.PidCode, out var unitType)
        && QuickPicksByUnitType.TryGetValue(unitType, out var picks)
            ? picks
            : [];

    /// <summary>Returns the unit type for a PID, or null if unknown.</summary>
    public static PotUnitType? UnitTypeFor(SimulatorPidDef pid) =>
        PidUnitTypes.TryGetValue(pid.PidCode, out var t) ? t : null;

    // ── Bool channels ─────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<BoolChannel> BoolChannels =
    [
        new("key_on",     "Key On"),
        new("engine_run", "Engine Running"),
        new("brake",      "Brake Applied"),
        new("open_loop",  "Open Loop Fuel"),
        new("door_open",  "Door Open"),
        new("mil",        "MIL / Check Engine"),
    ];
}
