using System.Collections.Generic;
using System.Linq;

namespace Neomotive.ScanTool.Core.Signals;

/// <summary>
/// The standard SAE J1979 Mode $01 signal table shipped with the tool, written to
/// <c>config/pid-table.json</c> on first run so it can be corrected and extended without a rebuild.
/// </summary>
/// <remarks>
/// <para>
/// Every entry here has scaling taken from the SAE definition. PIDs whose scaling could not be
/// stated with confidence were <em>omitted rather than guessed</em> — a wrong scale factor is worse
/// than a missing signal, because it produces a plausible-looking number that quietly misleads.
/// </para>
/// <para>
/// Deliberately not included: status bitmaps and enumerations ($01, $03, $12, $13, $1C, $1D, $1E,
/// $41, $51, $5F), and the composite PIDs that pack three or more sensors into one response
/// ($64, $67, $68, $6B, $6C, $70, $73, $77, $78, $79, $7A-$7C, $83, $86). Both groups need
/// presentation the numeric-signal model does not cover; the file format can express them once
/// that exists.
/// </para>
/// </remarks>
public static class SignalLibrary
{
    // Percent of full scale (A * 100 / 255) and the fuel-trim scale (A / 1.28 - 100).
    private const double PctFull = 100.0 / 255;
    private const double Trim = 100.0 / 128;

    public const string Engine = "Engine";
    public const string Fuel = "Fuel";
    public const string Air = "Air";
    public const string Emissions = "Emissions";
    public const string Electrical = "Electrical";
    public const string Thermal = "Thermal";
    public const string Vehicle = "Vehicle";
    public const string Diagnostics = "Diagnostics";

    /// <summary>Preferred ordering for the system filters in the picker.</summary>
    public static IReadOnlyList<string> SystemOrder { get; } =
        [Engine, Fuel, Air, Emissions, Thermal, Electrical, Vehicle, Diagnostics];

    private static SignalDefinition P(
        int pid,
        string key,
        string name,
        string? shortName,
        string unit,
        string[] systems,
        int len = 1,
        double scale = 1,
        double offset = 0,
        double min = 0,
        double max = 100,
        int decimals = 1,
        int byteOffset = 0,
        bool signed = false,
        string[]? tags = null)
        => new()
        {
            Key = key,
            Name = name,
            ShortName = shortName,
            Unit = unit,
            Systems = systems,
            Tags = tags ?? [],
            Source = SignalSource.Mode01,
            Address = pid,
            ByteOffset = byteOffset,
            ByteLength = len,
            Signed = signed,
            Scale = scale,
            Offset = offset,
            Min = min,
            Max = max,
            Decimals = decimals,
        };

    /// <summary>
    /// Oxygen sensor PIDs $14-$1B: byte A is sensor voltage, byte B is the associated short term
    /// fuel trim. Two signals sharing one address — the case the byte-offset model exists for.
    /// </summary>
    private static IEnumerable<SignalDefinition> OxygenSensors()
    {
        for (var i = 0; i < 8; i++)
        {
            var pid = 0x14 + i;
            var bank = i / 2 + 1;
            var sensor = i % 2 + 1;
            var label = $"B{bank}S{sensor}";

            yield return P(pid, $"O2Sensor{i + 1}Voltage", $"O2 Sensor {label} Voltage",
                $"O2 {label}", "V", [Emissions, Fuel],
                scale: 0.005, max: 1.275, decimals: 3,
                tags: ["oxygen", "lambda", "o2"]);

            yield return P(pid, $"O2Sensor{i + 1}Trim", $"O2 Sensor {label} Short Term Trim",
                $"O2 {label} STFT", "%", [Emissions, Fuel],
                scale: Trim, offset: -100, min: -100, max: 99.2, byteOffset: 1,
                tags: ["oxygen", "trim", "o2"]);
        }
    }

    /// <summary>
    /// Wide-range oxygen sensor PIDs $24-$2B: equivalence ratio in A/B, sensor voltage in C/D.
    /// </summary>
    private static IEnumerable<SignalDefinition> WidebandVoltage()
    {
        for (var i = 0; i < 8; i++)
        {
            var pid = 0x24 + i;
            var label = $"B{i / 2 + 1}S{i % 2 + 1}";

            yield return P(pid, $"O2Wr{i + 1}Lambda", $"O2 Sensor {label} Equivalence Ratio",
                $"λ {label}", "ratio", [Emissions, Fuel],
                len: 2, scale: 1.0 / 32768, max: 2, decimals: 3,
                tags: ["lambda", "wideband", "afr", "equivalence"]);

            yield return P(pid, $"O2Wr{i + 1}Voltage", $"O2 Sensor {label} Voltage (wide range)",
                $"λV {label}", "V", [Emissions, Fuel],
                len: 2, scale: 1.0 / 8192, max: 8, decimals: 3, byteOffset: 2,
                tags: ["lambda", "wideband"]);
        }
    }

    /// <summary>
    /// Wide-range oxygen sensor PIDs $34-$3B: equivalence ratio in A/B, sensor current in C/D.
    /// </summary>
    private static IEnumerable<SignalDefinition> WidebandCurrent()
    {
        for (var i = 0; i < 8; i++)
        {
            var pid = 0x34 + i;
            var label = $"B{i / 2 + 1}S{i % 2 + 1}";

            yield return P(pid, $"O2Wc{i + 1}Lambda", $"O2 Sensor {label} Equivalence Ratio (current)",
                $"λi {label}", "ratio", [Emissions, Fuel],
                len: 2, scale: 1.0 / 32768, max: 2, decimals: 3,
                tags: ["lambda", "wideband"]);

            yield return P(pid, $"O2Wc{i + 1}Current", $"O2 Sensor {label} Current",
                $"λA {label}", "mA", [Emissions, Fuel],
                len: 2, scale: 1.0 / 256, offset: -128, min: -128, max: 128, decimals: 3, byteOffset: 2,
                tags: ["lambda", "wideband"]);
        }
    }

    /// <summary>Secondary oxygen sensor trims, $55-$58: byte A and byte B are different banks.</summary>
    private static IEnumerable<SignalDefinition> SecondaryTrims()
    {
        (int Pid, string Term, string BankA, string BankB)[] entries =
        [
            (0x55, "Short", "1", "3"),
            (0x56, "Long", "1", "3"),
            (0x57, "Short", "2", "4"),
            (0x58, "Long", "2", "4"),
        ];

        foreach (var (pid, term, bankA, bankB) in entries)
        {
            yield return P(pid, $"Secondary{term}TrimBank{bankA}",
                $"Secondary {term} Term O2 Trim Bank {bankA}", $"{term[0]}TT B{bankA}", "%",
                [Emissions, Fuel], scale: Trim, offset: -100, min: -100, max: 99.2,
                tags: ["trim", "secondary", "oxygen"]);

            yield return P(pid, $"Secondary{term}TrimBank{bankB}",
                $"Secondary {term} Term O2 Trim Bank {bankB}", $"{term[0]}TT B{bankB}", "%",
                [Emissions, Fuel], scale: Trim, offset: -100, min: -100, max: 99.2, byteOffset: 1,
                tags: ["trim", "secondary", "oxygen"]);
        }
    }

    public static IReadOnlyList<SignalDefinition> BuiltIn { get; } =
    [
        P(0x04, "CalculatedEngineLoad", "Calculated Engine Load", "Load", "%", [Engine],
            scale: PctFull, tags: ["load"]),
        P(0x05, "EngineCoolantTemperature", "Engine Coolant Temperature", "Coolant", "°C", [Thermal, Engine],
            offset: -40, min: -40, max: 215, decimals: 0, tags: ["ect", "temp", "water"]),

        P(0x06, "ShortTermFuelTrimBank1", "Short Term Fuel Trim Bank 1", "STFT B1", "%", [Fuel],
            scale: Trim, offset: -100, min: -100, max: 99.2, tags: ["trim", "stft", "lean", "rich"]),
        P(0x07, "LongTermFuelTrimBank1", "Long Term Fuel Trim Bank 1", "LTFT B1", "%", [Fuel],
            scale: Trim, offset: -100, min: -100, max: 99.2, tags: ["trim", "ltft", "lean", "rich"]),
        P(0x08, "ShortTermFuelTrimBank2", "Short Term Fuel Trim Bank 2", "STFT B2", "%", [Fuel],
            scale: Trim, offset: -100, min: -100, max: 99.2, tags: ["trim", "stft"]),
        P(0x09, "LongTermFuelTrimBank2", "Long Term Fuel Trim Bank 2", "LTFT B2", "%", [Fuel],
            scale: Trim, offset: -100, min: -100, max: 99.2, tags: ["trim", "ltft"]),

        P(0x0A, "FuelPressure", "Fuel Pressure (gauge)", "Fuel Press", "kPa", [Fuel],
            scale: 3, max: 765, decimals: 0, tags: ["low side", "lift pump"]),
        P(0x0B, "IntakeManifoldPressure", "Intake Manifold Absolute Pressure", "MAP", "kPa", [Air, Engine],
            max: 255, decimals: 0, tags: ["map", "boost", "vacuum"]),
        P(0x0C, "EngineRpm", "Engine RPM", "RPM", "RPM", [Engine],
            len: 2, scale: 0.25, max: 8000, decimals: 0, tags: ["tach", "crank", "speed"]),
        P(0x0D, "VehicleSpeed", "Vehicle Speed", "Speed", "km/h", [Vehicle],
            max: 255, decimals: 0, tags: ["vss", "road speed"]),
        P(0x0E, "TimingAdvance", "Timing Advance", "Timing", "°", [Engine],
            scale: 0.5, offset: -64, min: -64, max: 63.5, tags: ["spark", "advance"]),
        P(0x0F, "IntakeAirTemperature", "Intake Air Temperature", "IAT", "°C", [Air, Thermal],
            offset: -40, min: -40, max: 215, decimals: 0, tags: ["iat", "temp"]),
        P(0x10, "MafAirFlowRate", "MAF Air Flow Rate", "MAF", "g/s", [Air],
            len: 2, scale: 0.01, max: 655.35, decimals: 2, tags: ["maf", "airflow"]),
        P(0x11, "ThrottlePosition", "Throttle Position", "Throttle", "%", [Air, Engine],
            scale: PctFull, tags: ["tps", "pedal"]),

        P(0x1F, "RunTimeSinceEngineStart", "Run Time Since Engine Start", "Run Time", "s", [Engine],
            len: 2, max: 65535, decimals: 0, tags: ["uptime"]),
        P(0x21, "DistanceWithMilOn", "Distance Travelled with MIL On", "Dist MIL", "km", [Diagnostics],
            len: 2, max: 65535, decimals: 0, tags: ["mil", "cel"]),
        P(0x22, "FuelRailPressureRelative", "Fuel Rail Pressure (rel. to manifold)", "Rail Rel", "kPa", [Fuel],
            len: 2, scale: 0.079, max: 5177.3, tags: ["rail"]),
        P(0x23, "FuelRailGaugePressure", "Fuel Rail Gauge Pressure", "Rail Press", "kPa", [Fuel, Engine],
            len: 2, scale: 10, max: 655350, decimals: 0, tags: ["rail", "common rail", "diesel", "hpfp"]),

        P(0x2C, "CommandedEgr", "Commanded EGR", "Cmd EGR", "%", [Emissions],
            scale: PctFull, tags: ["egr"]),
        P(0x2D, "EgrError", "EGR Error", "EGR Err", "%", [Emissions],
            scale: Trim, offset: -100, min: -100, max: 99.2, tags: ["egr"]),
        P(0x2E, "CommandedEvapPurge", "Commanded Evaporative Purge", "Evap Purge", "%", [Emissions],
            scale: PctFull, tags: ["evap", "purge"]),
        P(0x2F, "FuelTankLevel", "Fuel Tank Level", "Fuel Level", "%", [Fuel, Vehicle],
            scale: PctFull, tags: ["tank", "gauge"]),
        P(0x30, "WarmupsSinceCodesCleared", "Warm-ups Since Codes Cleared", "Warm-ups", "count", [Diagnostics],
            max: 255, decimals: 0),
        P(0x31, "DistanceSinceCodesCleared", "Distance Since Codes Cleared", "Dist Cleared", "km", [Diagnostics],
            len: 2, max: 65535, decimals: 0),
        P(0x32, "EvapVaporPressure", "Evap System Vapor Pressure", "Evap Press", "Pa", [Emissions],
            len: 2, scale: 0.25, min: -8192, max: 8191.75, signed: true, tags: ["evap", "leak"]),
        P(0x33, "BarometricPressure", "Absolute Barometric Pressure", "Baro", "kPa", [Air],
            max: 255, decimals: 0, tags: ["baro", "altitude"]),

        P(0x3C, "CatalystTemperatureB1S1", "Catalyst Temperature Bank 1 Sensor 1", "Cat B1S1", "°C", [Emissions, Thermal],
            len: 2, scale: 0.1, offset: -40, min: -40, max: 6513.5, tags: ["catalyst", "cat"]),
        P(0x3D, "CatalystTemperatureB2S1", "Catalyst Temperature Bank 2 Sensor 1", "Cat B2S1", "°C", [Emissions, Thermal],
            len: 2, scale: 0.1, offset: -40, min: -40, max: 6513.5, tags: ["catalyst", "cat"]),
        P(0x3E, "CatalystTemperatureB1S2", "Catalyst Temperature Bank 1 Sensor 2", "Cat B1S2", "°C", [Emissions, Thermal],
            len: 2, scale: 0.1, offset: -40, min: -40, max: 6513.5, tags: ["catalyst", "cat"]),
        P(0x3F, "CatalystTemperatureB2S2", "Catalyst Temperature Bank 2 Sensor 2", "Cat B2S2", "°C", [Emissions, Thermal],
            len: 2, scale: 0.1, offset: -40, min: -40, max: 6513.5, tags: ["catalyst", "cat"]),

        P(0x42, "ControlModuleVoltage", "Control Module Voltage", "Module V", "V", [Electrical],
            len: 2, scale: 0.001, max: 65.535, decimals: 2, tags: ["battery", "voltage", "charging", "alternator"]),
        P(0x43, "AbsoluteLoadValue", "Absolute Load Value", "Abs Load", "%", [Engine],
            len: 2, scale: PctFull, max: 25700, tags: ["load"]),
        P(0x44, "CommandedEquivalenceRatio", "Commanded Equivalence Ratio", "λ Cmd", "ratio", [Fuel, Emissions],
            len: 2, scale: 1.0 / 32768, max: 2, decimals: 3, tags: ["lambda", "afr"]),
        P(0x45, "RelativeThrottlePosition", "Relative Throttle Position", "Rel Throttle", "%", [Air],
            scale: PctFull, tags: ["tps"]),
        P(0x46, "AmbientAirTemperature", "Ambient Air Temperature", "Ambient", "°C", [Thermal],
            offset: -40, min: -40, max: 215, decimals: 0, tags: ["outside", "temp"]),
        P(0x47, "AbsoluteThrottlePositionB", "Absolute Throttle Position B", "Throttle B", "%", [Air],
            scale: PctFull, tags: ["tps"]),
        P(0x48, "AbsoluteThrottlePositionC", "Absolute Throttle Position C", "Throttle C", "%", [Air],
            scale: PctFull, tags: ["tps"]),
        P(0x49, "AcceleratorPedalPositionD", "Accelerator Pedal Position D", "Pedal D", "%", [Engine],
            scale: PctFull, tags: ["pedal", "app"]),
        P(0x4A, "AcceleratorPedalPositionE", "Accelerator Pedal Position E", "Pedal E", "%", [Engine],
            scale: PctFull, tags: ["pedal", "app"]),
        P(0x4B, "AcceleratorPedalPositionF", "Accelerator Pedal Position F", "Pedal F", "%", [Engine],
            scale: PctFull, tags: ["pedal", "app"]),
        P(0x4C, "CommandedThrottleActuator", "Commanded Throttle Actuator", "Cmd Throttle", "%", [Air],
            scale: PctFull, tags: ["etc", "drive by wire"]),
        P(0x4D, "TimeRunWithMilOn", "Time Run with MIL On", "Time MIL", "min", [Diagnostics],
            len: 2, max: 65535, decimals: 0, tags: ["mil", "cel"]),
        P(0x4E, "TimeSinceCodesCleared", "Time Since Codes Cleared", "Time Cleared", "min", [Diagnostics],
            len: 2, max: 65535, decimals: 0),
        P(0x50, "MaxMafRate", "Maximum MAF Rate", "Max MAF", "g/s", [Air],
            scale: 10, max: 2550, decimals: 0, tags: ["maf"]),
        P(0x52, "EthanolFuelPercent", "Ethanol Fuel Percentage", "Ethanol", "%", [Fuel],
            scale: PctFull, tags: ["e85", "flex"]),
        P(0x53, "AbsoluteEvapPressure", "Absolute Evap System Vapor Pressure", "Evap Abs", "kPa", [Emissions],
            len: 2, scale: 1.0 / 200, max: 327.675, decimals: 3, tags: ["evap"]),
        P(0x54, "EvapPressureAlternate", "Evap System Vapor Pressure (alt)", "Evap Alt", "Pa", [Emissions],
            len: 2, offset: -32767, min: -32767, max: 32768, decimals: 0, tags: ["evap"]),
        P(0x59, "FuelRailAbsolutePressure", "Fuel Rail Absolute Pressure", "Rail Abs", "kPa", [Fuel],
            len: 2, scale: 10, max: 655350, decimals: 0, tags: ["rail"]),
        P(0x5A, "RelativeAcceleratorPedalPosition", "Relative Accelerator Pedal Position", "Rel Pedal", "%", [Engine],
            scale: PctFull, tags: ["pedal"]),
        P(0x5B, "HybridBatteryRemaining", "Hybrid Battery Pack Remaining Life", "Hybrid Batt", "%", [Electrical],
            scale: PctFull, tags: ["hybrid", "battery"]),
        P(0x5C, "EngineOilTemperature", "Engine Oil Temperature", "Oil Temp", "°C", [Thermal, Engine],
            offset: -40, min: -40, max: 210, decimals: 0, tags: ["oil", "temp"]),
        P(0x5D, "FuelInjectionTiming", "Fuel Injection Timing", "Inj Timing", "°", [Fuel, Engine],
            len: 2, scale: 1.0 / 128, offset: -210, min: -210, max: 301.99, decimals: 2,
            tags: ["injection", "timing", "diesel"]),
        P(0x5E, "EngineFuelRate", "Engine Fuel Rate", "Fuel Rate", "L/h", [Fuel],
            len: 2, scale: 0.05, max: 3212.75, decimals: 2, tags: ["consumption", "economy"]),

        P(0x61, "DriverDemandTorque", "Driver's Demand Engine Torque", "Demand Tq", "%", [Engine],
            offset: -125, min: -125, max: 130, decimals: 0, tags: ["torque"]),
        P(0x62, "ActualEngineTorque", "Actual Engine Torque", "Actual Tq", "%", [Engine],
            offset: -125, min: -125, max: 130, decimals: 0, tags: ["torque"]),
        P(0x63, "EngineReferenceTorque", "Engine Reference Torque", "Ref Tq", "Nm", [Engine],
            len: 2, max: 65535, decimals: 0, tags: ["torque"]),

        P(0x9E, "ExhaustFlowRate", "Engine Exhaust Flow Rate", "Exh Flow", "kg/h", [Emissions, Air],
            len: 2, scale: 0.2, max: 13107, tags: ["exhaust"]),
        P(0xA2, "CylinderFuelRate", "Cylinder Fuel Rate", "Cyl Fuel", "mg/stroke", [Fuel],
            len: 2, scale: 1.0 / 32, max: 2047.97, decimals: 2, tags: ["injection"]),
        P(0xA6, "Odometer", "Odometer", "Odometer", "km", [Vehicle],
            len: 4, scale: 0.1, max: 429496729.5, tags: ["mileage", "distance"]),

        .. OxygenSensors(),
        .. WidebandVoltage(),
        .. WidebandCurrent(),
        .. SecondaryTrims(),
    ];

    /// <summary>All system names present in the built-in table, in the preferred display order.</summary>
    public static IReadOnlyList<string> Systems(IEnumerable<SignalDefinition> signals)
    {
        var present = signals.SelectMany(s => s.Systems).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();

        return SystemOrder.Where(present.Contains)
            .Concat(present.Where(p => !SystemOrder.Contains(p)).OrderBy(p => p))
            .ToArray();
    }
}
