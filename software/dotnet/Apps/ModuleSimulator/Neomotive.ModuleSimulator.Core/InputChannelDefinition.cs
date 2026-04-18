namespace Neomotive.ModuleSimulator;

public enum InputType { Pot, Switch, Button }

/// <summary>
/// Defines what a potentiometer controls and how to convert its voltage.
/// physical = voltage * Scale + Offset
/// </summary>
public record PotChannel(string Key, string DisplayName, string Unit, double Scale, double Offset)
{
    public double MapFromVoltage(double volts) => volts * Scale + Offset;
    public string Format(double value) => $"{value:F1} {Unit}";
    public string RangeDescription => $"{Format(Offset)} – {Format(5.0 * Scale + Offset)}";
}

public record BoolChannel(string Key, string DisplayName);

/// <summary>
/// A named preset that sets Scale/Offset/Unit in one tap.
/// </summary>
public record PotQuickPick(string Label, string Key, string Unit, double Scale, double Offset)
{
    public PotChannel ToChannel(string displayName) =>
        new(Key, displayName, Unit, Scale, Offset);
}

public static class InputChannelCatalog
{
    /// <summary>
    /// Quick-pick presets for common automotive/sensor ranges.
    /// physical = voltage × Scale + Offset  (voltage 0–5 V)
    /// </summary>
    public static readonly IReadOnlyList<PotQuickPick> PotQuickPicks =
    [
        //                  label              key             unit    scale    offset
        new("Temp °C",     "coolant_temp",    "°C",           51.0,   -40.0),  // -40 … 215 °C
        new("Temp °F",     "coolant_temp",    "°F",           91.8,   -40.0),  // -40 … 419 °F
        new("Percent %",   "throttle",        "%",            20.0,     0.0),  //   0 … 100 %
        new("RPM",         "rpm",             "rpm",        1600.0,     0.0),  //   0 … 8000 rpm
        new("Speed km/h",  "speed",           "km/h",         40.0,     0.0),  //   0 … 200 km/h
        new("Speed mph",   "speed",           "mph",          25.0,     0.0),  //   0 … 125 mph
        new("Pres kPa",    "pressure",        "kPa",          50.0,     0.0),  //   0 … 250 kPa
        new("Pres PSI",    "pressure",        "PSI",          14.5,     0.0),  //   0 … 72.5 PSI
        new("Voltage V",   "voltage",         "V",             1.0,     0.0),  //   0 … 5 V (raw)
        new("Lambda λ",    "lambda",          "λ",             0.4,     0.5),  // 0.5 … 2.5 λ
    ];

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
