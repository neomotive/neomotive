using System.Collections.Generic;
using Meadow.Foundation.Telematics.J1979;

namespace Neomotive.ScanTool.Core.Diagnostics;

/// <summary>
/// The profiles shipped with the tool. Written to <c>config/diagnostic-profiles.json</c> on first
/// run so they can be edited, extended or replaced.
/// </summary>
/// <remarks>
/// These are starting points chosen to cover common diagnostic shapes — an event that happens
/// once and is over (cranking), a condition that has to be caught while driving (misfire, shift
/// quality), and a slow drift (overheating). They are not an attempt at completeness; the profile
/// file exists precisely because no built-in set can be.
/// </remarks>
public static class DiagnosticProfileLibrary
{
    private static string Key(Pid pid) => pid.ToString();

    public static IReadOnlyList<DiagnosticProfile> BuiltIn { get; } =
    [
        new DiagnosticProfile
        {
            Key = "manual-freeform",
            Category = "General",
            Name = "Manual capture",
            Description =
                "Records whatever signals you select, starting and stopping on the buttons. "
                + "The fallback when no other profile fits.",
            Signals = [Key(Pid.EngineRpm), Key(Pid.VehicleSpeed), Key(Pid.CalculatedEngineLoad)],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 5,
            MaxDurationSeconds = 300,
            CaptureName = "manual",
        },

        new DiagnosticProfile
        {
            Key = "intermittent-watch",
            Category = "General",
            Name = "Intermittent fault watch",
            Description =
                "Long recording for a fault that has not happened yet. Press Trigger when the "
                + "symptom occurs; the pre-trigger buffer keeps the 30 s leading up to it.",
            Signals =
            [
                Key(Pid.EngineRpm), Key(Pid.VehicleSpeed), Key(Pid.ControlModuleVoltage),
                Key(Pid.CalculatedEngineLoad), Key(Pid.EngineCoolantTemperature),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 30,
            MaxDurationSeconds = 60,
            CaptureName = "intermittent",
        },

        new DiagnosticProfile
        {
            Key = "cranking-charging",
            Category = "Starting & Charging",
            Name = "Cranking & charging",
            Description =
                "Battery and charging behaviour through a start. Watch for voltage sagging below "
                + "about 9.5 V while cranking, and for it rising above 13.5 V once running.",
            Signals =
            [
                Key(Pid.ControlModuleVoltage), Key(Pid.EngineRpm),
                Key(Pid.EngineCoolantTemperature), Key(Pid.CalculatedEngineLoad),
            ],
            Trigger = new ProfileTrigger
            {
                Mode = ProfileTriggerMode.Threshold,
                Signal = Key(Pid.EngineRpm),
                Above = true,
                Value = 150,
                DwellMs = 200,
            },
            PreTriggerSeconds = 5,
            MaxDurationSeconds = 60,
            Stop = new ProfileStop { Enabled = false },
            CaptureName = "cranking",
        },

        new DiagnosticProfile
        {
            Key = "hard-start-common-rail",
            Category = "Starting & Charging",
            Name = "Hard start — common rail diesel",
            Description =
                "Read in order: module voltage (rule out a weak battery, which mimics a fuel "
                + "fault), cranking speed, then rail pressure and how fast it builds. Stops "
                + "automatically once the engine has run and then died.",
            Signals =
            [
                Key(Pid.ControlModuleVoltage), Key(Pid.EngineRpm), Key(Pid.FuelRailGaugePressure),
                Key(Pid.EngineCoolantTemperature), Key(Pid.IntakeManifoldPressure),
            ],
            Trigger = new ProfileTrigger
            {
                Mode = ProfileTriggerMode.Threshold,
                Signal = Key(Pid.EngineRpm),
                Above = true,
                Value = 150,
                DwellMs = 200,
            },
            PreTriggerSeconds = 5,
            MaxDurationSeconds = 120,
            Stop = new ProfileStop
            {
                Enabled = true,
                Signal = Key(Pid.EngineRpm),
                Floor = 50,
                DurationMs = 5000,
            },
            CaptureName = "hard-start",
        },

        new DiagnosticProfile
        {
            Key = "fuel-trim",
            Category = "Fuel & Air",
            Name = "Fuel trim (lean / rich)",
            Description =
                "Short and long term trims against load and airflow. Large positive trims at idle "
                + "that fall off under load point at a vacuum leak; trims high across all loads "
                + "point at fuel delivery or a skewed MAF.",
            Signals =
            [
                Key(Pid.ShortTermFuelTrimBank1), Key(Pid.LongTermFuelTrimBank1),
                Key(Pid.ShortTermFuelTrimBank2), Key(Pid.LongTermFuelTrimBank2),
                Key(Pid.MafAirFlowRate), Key(Pid.CalculatedEngineLoad), Key(Pid.EngineRpm),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 10,
            MaxDurationSeconds = 300,
            CaptureName = "fuel-trim",
        },

        new DiagnosticProfile
        {
            Key = "fuel-pressure",
            Category = "Fuel & Air",
            Name = "Fuel pressure & delivery",
            Description =
                "Fuel pressure against demand. Pressure falling away as load rises indicates a "
                + "failing pump, restricted filter or a leaking regulator.",
            Signals =
            [
                Key(Pid.FuelPressure), Key(Pid.FuelRailGaugePressure), Key(Pid.EngineRpm),
                Key(Pid.CalculatedEngineLoad), Key(Pid.EngineFuelRate),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 10,
            MaxDurationSeconds = 300,
            CaptureName = "fuel-pressure",
        },

        new DiagnosticProfile
        {
            Key = "boost-airflow",
            Category = "Fuel & Air",
            Name = "Boost & airflow",
            Description =
                "Manifold pressure and airflow against throttle demand, for a lack-of-power "
                + "complaint. Look for boost failing to follow demand, or airflow flattening off.",
            Signals =
            [
                Key(Pid.IntakeManifoldPressure), Key(Pid.MafAirFlowRate), Key(Pid.ThrottlePosition),
                Key(Pid.EngineRpm), Key(Pid.IntakeAirTemperature), Key(Pid.BarometricPressure),
            ],
            Trigger = new ProfileTrigger
            {
                Mode = ProfileTriggerMode.Threshold,
                Signal = Key(Pid.ThrottlePosition),
                Above = true,
                Value = 70,
                DwellMs = 100,
            },
            PreTriggerSeconds = 10,
            MaxDurationSeconds = 60,
            CaptureName = "boost",
        },

        new DiagnosticProfile
        {
            Key = "misfire-hunt",
            Category = "Drivability",
            Name = "Misfire / rough running",
            Description =
                "Catches a stumble as it happens. Press Trigger when it misbehaves and read the "
                + "pre-trigger window for what moved first — load, timing, trims or airflow.",
            Signals =
            [
                Key(Pid.EngineRpm), Key(Pid.CalculatedEngineLoad), Key(Pid.TimingAdvance),
                Key(Pid.ShortTermFuelTrimBank1), Key(Pid.MafAirFlowRate),
                Key(Pid.IntakeManifoldPressure),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 20,
            MaxDurationSeconds = 120,
            CaptureName = "misfire",
        },

        new DiagnosticProfile
        {
            Key = "idle-quality",
            Category = "Drivability",
            Name = "Idle instability",
            Description =
                "Idle hunting or stalling. Stops automatically if the engine dies, so a stall is "
                + "captured with its lead-up intact.",
            Signals =
            [
                Key(Pid.EngineRpm), Key(Pid.CalculatedEngineLoad), Key(Pid.ThrottlePosition),
                Key(Pid.ShortTermFuelTrimBank1), Key(Pid.IntakeManifoldPressure),
                Key(Pid.EngineCoolantTemperature),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 20,
            MaxDurationSeconds = 180,
            Stop = new ProfileStop
            {
                Enabled = true,
                Signal = Key(Pid.EngineRpm),
                Floor = 200,
                DurationMs = 3000,
            },
            CaptureName = "idle",
        },

        new DiagnosticProfile
        {
            Key = "acceleration-snapshot",
            Category = "Drivability",
            Name = "Acceleration / WOT",
            Description =
                "Wide-open-throttle pull. Triggers on throttle and records the whole run for "
                + "comparing power delivery against a known-good pull.",
            Signals =
            [
                Key(Pid.ThrottlePosition), Key(Pid.EngineRpm), Key(Pid.VehicleSpeed),
                Key(Pid.CalculatedEngineLoad), Key(Pid.TimingAdvance), Key(Pid.MafAirFlowRate),
            ],
            Trigger = new ProfileTrigger
            {
                Mode = ProfileTriggerMode.Threshold,
                Signal = Key(Pid.ThrottlePosition),
                Above = true,
                Value = 80,
                DwellMs = 100,
            },
            PreTriggerSeconds = 5,
            MaxDurationSeconds = 45,
            CaptureName = "wot",
        },

        new DiagnosticProfile
        {
            Key = "catalyst-o2",
            Category = "Emissions",
            Name = "Catalyst & O2 response",
            Description =
                "Upstream against downstream oxygen sensors with catalyst temperature. A "
                + "downstream sensor mirroring the upstream one indicates a spent converter.",
            Signals =
            [
                Key(Pid.OxygenSensor1ShortTermFuelTrim), Key(Pid.OxygenSensor2ShortTermFuelTrim),
                Key(Pid.CatalystTemperatureBank1Sensor1), Key(Pid.EngineRpm),
                Key(Pid.CalculatedEngineLoad),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 10,
            MaxDurationSeconds = 300,
            CaptureName = "catalyst",
        },

        new DiagnosticProfile
        {
            Key = "egr-operation",
            Category = "Emissions",
            Name = "EGR operation",
            Description =
                "Commanded EGR against the reported error, with load and manifold pressure. A "
                + "persistent error means the valve is not following the command.",
            Signals =
            [
                Key(Pid.CommandedEgr), Key(Pid.EgrError), Key(Pid.IntakeManifoldPressure),
                Key(Pid.CalculatedEngineLoad), Key(Pid.EngineRpm),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 10,
            MaxDurationSeconds = 300,
            CaptureName = "egr",
        },

        new DiagnosticProfile
        {
            Key = "shift-quality",
            Category = "Transmission",
            Name = "Shift quality",
            Description =
                "Engine speed against road speed through gear changes. Flare shows as RPM rising "
                + "while road speed does not; slip shows as the two diverging under load.",
            Signals =
            [
                Key(Pid.EngineRpm), Key(Pid.VehicleSpeed), Key(Pid.CalculatedEngineLoad),
                Key(Pid.ThrottlePosition), Key(Pid.EngineOilTemperature),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 15,
            MaxDurationSeconds = 300,
            CaptureName = "shift",
        },

        new DiagnosticProfile
        {
            Key = "overheating",
            Category = "Thermal",
            Name = "Cooling & overheating",
            Description =
                "Slow drift capture for a temperature complaint. Long duration, low urgency — "
                + "look at how coolant temperature tracks load and ambient.",
            Signals =
            [
                Key(Pid.EngineCoolantTemperature), Key(Pid.EngineOilTemperature),
                Key(Pid.IntakeAirTemperature), Key(Pid.AmbientAirTemperature),
                Key(Pid.CalculatedEngineLoad), Key(Pid.VehicleSpeed), Key(Pid.EngineRpm),
            ],
            Trigger = new ProfileTrigger { Mode = ProfileTriggerMode.Manual },
            PreTriggerSeconds = 30,
            MaxDurationSeconds = 900,
            CaptureName = "cooling",
        },
    ];
}
