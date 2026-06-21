using Meadow.Foundation.Telematics.J1979;

namespace Neomotive.ScanTool.Core;

public static class PidRegistry
{
    public static IReadOnlyList<PidDescriptor> CommonPids { get; } =
    [
        new(Pid.CalculatedEngineLoad,        "Engine Load",        "%",    100.0 / 255, 0,    1, 0,    100),
        new(Pid.EngineCoolantTemperature,    "Coolant Temp",       "°C",   1,           -40,  1, -40,  215),
        new(Pid.ShortTermFuelTrimBank1,      "Short FT B1",        "%",    100.0 / 128, -100, 1, -100, 99.2),
        new(Pid.LongTermFuelTrimBank1,       "Long FT B1",         "%",    100.0 / 128, -100, 1, -100, 99.2),
        new(Pid.FuelPressure,                "Fuel Pressure",      "kPa",  3,           0,    1, 0,    765),
        new(Pid.IntakeManifoldPressure,      "Intake MAP",         "kPa",  1,           0,    1, 0,    255),
        new(Pid.EngineRpm,                   "Engine RPM",         "RPM",  0.25,        0,    2, 0,    8000),
        new(Pid.VehicleSpeed,                "Vehicle Speed",      "km/h", 1,           0,    1, 0,    255),
        new(Pid.TimingAdvance,               "Timing Advance",     "°",    0.5,         -64,  1, -64,  63.5),
        new(Pid.IntakeAirTemperature,        "Intake Air Temp",    "°C",   1,           -40,  1, -40,  215),
        new(Pid.MafAirFlowRate,              "MAF Air Flow",       "g/s",  0.01,        0,    2, 0,    655),
        new(Pid.ThrottlePosition,            "Throttle Pos",       "%",    100.0 / 255, 0,    1, 0,    100),
        // O2 sensors: 2-byte response, byte A = voltage (0.005 V/bit), byte B = STFT — read voltage only
        new(Pid.OxygenSensor1ShortTermFuelTrim, "O2 Sensor B1S1", "V",    0.005,       0,    1, 0,    1.275),
        new(Pid.OxygenSensor2ShortTermFuelTrim, "O2 Sensor B1S2", "V",    0.005,       0,    1, 0,    1.275),
        new(Pid.BarometricPressure,          "Baro Pressure",      "kPa",  1,           0,    1, 0,    255),
    ];
}
