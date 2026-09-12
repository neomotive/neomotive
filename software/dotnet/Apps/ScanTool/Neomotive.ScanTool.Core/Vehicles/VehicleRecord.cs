using System.Text.Json.Serialization;

namespace Neomotive.ScanTool.Core.Vehicles;

/// <summary>A calibration reading from one visit.</summary>
public sealed class CalibrationEntry
{
    public string? CalibrationId { get; set; }
    public string? Cvn { get; set; }
    public string? EcuName { get; set; }
    public DateTime SeenUtc { get; set; }

    /// <summary>Whether two readings describe the same calibration.</summary>
    public bool SameAs(string? calId, string? cvn) =>
        string.Equals(CalibrationId, calId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Cvn, cvn, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One code present at one visit.</summary>
public sealed class DtcSnapshotCode
{
    public string Code { get; set; } = "";
    public string Module { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>The codes present at one visit.</summary>
public sealed class DtcSnapshot
{
    public DateTime SeenUtc { get; set; }
    public List<DtcSnapshotCode> Codes { get; set; } = [];
}

/// <summary>What the vehicle was set up to capture last time.</summary>
public sealed class CaptureSetup
{
    public string? ProfileId { get; set; }
    public List<string> SignalKeys { get; set; } = [];
}

/// <summary>The decoded identity, kept as display text so a record reads sensibly on its own.</summary>
public sealed class VehicleIdentity
{
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Trim { get; set; }
    public string? Country { get; set; }
    public string? PlantCity { get; set; }
    public string? EngineType { get; set; }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var parts = new List<string>(4);
            if (Year is { } y) parts.Add(y.ToString());
            if (!string.IsNullOrWhiteSpace(Make)) parts.Add(Make!);
            if (!string.IsNullOrWhiteSpace(Model)) parts.Add(Model!);
            if (!string.IsNullOrWhiteSpace(Trim)) parts.Add(Trim!);
            return parts.Count > 0 ? string.Join(" ", parts) : "Unidentified vehicle";
        }
    }
}

/// <summary>Everything remembered about one specific vehicle, keyed by its VIN.</summary>
public sealed class VehicleRecord
{
    /// <summary>How many calibration readings and DTC snapshots a record keeps.</summary>
    public const int HistoryLimit = 10;

    public string Vin { get; set; } = "";

    public string ClassKey { get; set; } = "";

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public int VisitCount { get; set; }

    public VehicleIdentity Identity { get; set; } = new();

    public List<RememberedModule> Modules { get; set; } = [];

    /// <summary>Mode 01 PID numbers the vehicle reported serving, and when it said so.</summary>
    public List<byte> SupportedPids { get; set; } = [];

    public DateTime? SupportedPidsReadUtc { get; set; }

    /// <summary>Newest last. Only appended to when the calibration actually differs.</summary>
    public List<CalibrationEntry> Calibrations { get; set; } = [];

    /// <summary>Newest last.</summary>
    public List<DtcSnapshot> DtcSnapshots { get; set; } = [];

    public CaptureSetup? LastCapture { get; set; }

    [JsonIgnore]
    public CalibrationEntry? LatestCalibration =>
        Calibrations.Count > 0 ? Calibrations[^1] : null;

    /// <summary>
    /// The calibration reading before the current one, if any — the baseline a change is reported
    /// against.
    /// </summary>
    [JsonIgnore]
    public CalibrationEntry? PreviousCalibration =>
        Calibrations.Count > 1 ? Calibrations[^2] : null;

    /// <summary>
    /// Appends a calibration reading, but only when it differs from the last one. A vehicle that
    /// visits twenty times without being reflashed should have one entry, not twenty.
    /// </summary>
    public void RecordCalibration(string? calId, string? cvn, string? ecuName, DateTime nowUtc)
    {
        if (calId is null && cvn is null) return;
        if (LatestCalibration?.SameAs(calId, cvn) == true) return;

        Calibrations.Add(new CalibrationEntry
        {
            CalibrationId = calId,
            Cvn = cvn,
            EcuName = ecuName,
            SeenUtc = nowUtc
        });

        Trim(Calibrations);
    }

    public void RecordDtcSnapshot(DtcSnapshot snapshot)
    {
        DtcSnapshots.Add(snapshot);
        Trim(DtcSnapshots);
    }

    private static void Trim<T>(List<T> list)
    {
        if (list.Count > HistoryLimit) list.RemoveRange(0, list.Count - HistoryLimit);
    }
}

/// <summary>
/// What vehicles of one class have in common — the union across every VIN of that class seen.
/// <para>
/// A union, never an intersection. A scan that missed a module because the bus was struggling must
/// not remove it from what the class is known to carry; the evidence that a module is genuinely
/// absent is that it keeps not answering on vehicles where it is looked for, which is what
/// <see cref="RememberedModule.VinsSeenOn"/> against <see cref="VinCount"/> lets a reader judge.
/// </para>
/// </summary>
public sealed class ClassProfile
{
    public string ClassKey { get; set; } = "";

    public VehicleIdentity Identity { get; set; } = new();

    /// <summary>Distinct VINs of this class that have been scanned.</summary>
    public int VinCount { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public List<RememberedModule> Modules { get; set; } = [];

    public List<byte> SupportedPids { get; set; } = [];

    /// <summary>How many of this class's vehicles agreed on the supported-PID set above.</summary>
    public int SupportedPidsAgreement { get; set; }
}
