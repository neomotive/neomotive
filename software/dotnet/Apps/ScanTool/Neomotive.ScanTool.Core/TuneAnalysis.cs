using System;
using System.Collections.Generic;
using System.Linq;
using Meadow.Foundation.Telematics.J1979;

namespace Neomotive.ScanTool.Core;

/// <summary>A one-shot snapshot of what the ECU says about itself.</summary>
public record TuneFingerprint(
    string? Vin,
    string? EcuName,
    string? CalibrationId,
    string? Cvn,
    IReadOnlyList<Pid> SupportedPids,
    IReadOnlyList<ReadinessMonitor> Monitors)
{
    public DateTime CapturedUtc { get; init; } = DateTime.UtcNow;
}

public enum TuneFindingSeverity
{
    Info,
    Notable,
    Significant,
}

public record TuneFinding(TuneFindingSeverity Severity, string Title, string Detail);

public record TuneAssessment(
    IReadOnlyList<TuneFinding> Findings,
    IReadOnlyList<Pid> AftertreatmentPids,
    IReadOnlyList<string> UnsupportedMonitors,
    IReadOnlyList<string> IncompleteMonitors,
    string Summary);

/// <summary>
/// One specific analysis: reads a vehicle fingerprint for signs that the emissions calibration
/// does not match the hardware actually fitted.
/// </summary>
/// <remarks>
/// This is a single, narrowly scoped check, not the tool's general diagnostic mechanism — capture
/// profiles cover that. It exists because "does the calibration match the hardware" is a question
/// that can be answered from a one-shot read with the engine off, which is exactly when a vehicle
/// that will not run has nothing else to offer.
/// </remarks>
/// <remarks>
/// This reports <em>evidence</em>, not a verdict. Without a known-good baseline for this vehicle,
/// a calibration ID and CVN cannot by themselves prove a reflash — they are only decisive when
/// compared against what the vehicle shipped with. What the tool <em>can</em> settle on its own is
/// the more useful question: whether the calibration still expects aftertreatment hardware that has
/// been physically removed. That mismatch is a well-known cause of no-start and immediate stall on
/// deleted diesels.
/// </remarks>
public static class TuneAnalyzer
{
    /// <summary>
    /// Name prefixes for PIDs that only exist on a diesel with DPF/SCR aftertreatment.
    /// </summary>
    /// <remarks>
    /// EGR is deliberately excluded: petrol engines have it too, so its presence proves nothing
    /// about aftertreatment. DPF, SCR, NOx and particulate-matter PIDs are unambiguous.
    /// </remarks>
    private static readonly string[] AftertreatmentPrefixes =
    [
        "Dpf",
        "DieselParticulate",
        "Scr",
        "Nox",
        "ParticulateMatter",
    ];

    /// <summary>Monitors whose support state is informative on a deleted diesel.</summary>
    private static readonly string[] EmissionsMonitorNames =
    [
        "Catalyst",
        "Heated Catalyst",
        "EGR System",
        "O2 Sensor",
        "O2 Sensor Heater",
        "Evap System",
        "Secondary Air",
    ];

    public static bool IsAftertreatmentPid(Pid pid)
    {
        var name = Enum.GetName(pid);

        return name is not null
            && AftertreatmentPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    public static TuneAssessment Analyze(TuneFingerprint fingerprint)
    {
        var findings = new List<TuneFinding>();

        var aftertreatment = fingerprint.SupportedPids.Where(IsAftertreatmentPid).Distinct().ToArray();

        var unsupported = fingerprint.Monitors
            .Where(m => m.IsNotSupported && EmissionsMonitorNames.Contains(m.Name))
            .Select(m => m.Name)
            .ToArray();

        var incomplete = fingerprint.Monitors
            .Where(m => m.IsIncomplete && EmissionsMonitorNames.Contains(m.Name))
            .Select(m => m.Name)
            .ToArray();

        AddIdentityFindings(fingerprint, findings);
        AddAftertreatmentFindings(aftertreatment, findings);
        AddMonitorFindings(unsupported, incomplete, findings);

        return new TuneAssessment(
            findings,
            aftertreatment,
            unsupported,
            incomplete,
            BuildSummary(aftertreatment, unsupported, incomplete));
    }

    private static void AddIdentityFindings(TuneFingerprint fingerprint, List<TuneFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(fingerprint.CalibrationId))
        {
            findings.Add(new TuneFinding(
                TuneFindingSeverity.Notable,
                "No calibration ID reported",
                "The ECU did not answer Mode $09 PID $04. Stock calibrations normally report one. "
                + "Some aftermarket tunes drop this service entirely."));
        }
        else
        {
            findings.Add(new TuneFinding(
                TuneFindingSeverity.Info,
                $"Calibration ID: {fingerprint.CalibrationId}",
                "Record this. On its own it proves nothing — comparing it against the calibration "
                + "the vehicle shipped with is what makes it meaningful."));
        }

        if (string.IsNullOrWhiteSpace(fingerprint.Cvn))
        {
            findings.Add(new TuneFinding(
                TuneFindingSeverity.Notable,
                "No CVN reported",
                "The ECU did not answer Mode $09 PID $06. The CVN is a checksum over the "
                + "calibration and survives a tune that spoofs the ID string, so its absence "
                + "removes the more reliable half of the fingerprint."));
        }
        else
        {
            findings.Add(new TuneFinding(
                TuneFindingSeverity.Info,
                $"CVN: {fingerprint.Cvn}",
                "A reflash changes this even when the calibration ID is left looking stock. "
                + "Compare against a known-good value for this engine to detect a tune."));
        }
    }

    private static void AddAftertreatmentFindings(
        IReadOnlyList<Pid> aftertreatment,
        List<TuneFinding> findings)
    {
        if (aftertreatment.Count == 0)
        {
            findings.Add(new TuneFinding(
                TuneFindingSeverity.Significant,
                "No DPF/SCR/NOx PIDs reported",
                "The calibration does not claim any diesel aftertreatment channels. On a vehicle "
                + "whose aftertreatment has been physically removed this is the consistent, "
                + "expected result — it points towards a delete tune having been applied."));

            return;
        }

        var names = string.Join(", ", aftertreatment.Take(8).Select(p => Enum.GetName(p)));
        var more = aftertreatment.Count > 8 ? $" (+{aftertreatment.Count - 8} more)" : string.Empty;

        findings.Add(new TuneFinding(
            TuneFindingSeverity.Significant,
            $"{aftertreatment.Count} aftertreatment PIDs still reported",
            $"The calibration still claims: {names}{more}. If the DPF/SCR hardware has been "
            + "removed, the ECU is monitoring for feedback that no longer physically exists. "
            + "That mismatch is a well-known cause of no-start and immediate stall, and it "
            + "suggests the delete was done WITHOUT the accompanying tune."));
    }

    private static void AddMonitorFindings(
        IReadOnlyList<string> unsupported,
        IReadOnlyList<string> incomplete,
        List<TuneFinding> findings)
    {
        if (unsupported.Count > 0)
        {
            findings.Add(new TuneFinding(
                TuneFindingSeverity.Notable,
                $"Monitors reported NOT SUPPORTED: {string.Join(", ", unsupported)}",
                "\"Not supported\" is different from \"not complete\". A calibration that has been "
                + "modified to ignore emissions hardware typically drops these monitors entirely "
                + "rather than leaving them pending."));
        }

        if (incomplete.Count > 0)
        {
            findings.Add(new TuneFinding(
                TuneFindingSeverity.Info,
                $"Monitors supported but NOT COMPLETE: {string.Join(", ", incomplete)}",
                "These are still being monitored and simply have not finished their drive cycle. "
                + "On a vehicle that will not stay running, that is expected and not evidence of "
                + "anything."));
        }
    }

    private static string BuildSummary(
        IReadOnlyList<Pid> aftertreatment,
        IReadOnlyList<string> unsupported,
        IReadOnlyList<string> incomplete)
    {
        if (aftertreatment.Count > 0)
        {
            return "The calibration still expects diesel aftertreatment hardware. If that hardware "
                 + "has been removed, this is the strongest available evidence that no delete tune "
                 + "was applied, and a credible cause of no-start, stalling or derate complaints.";
        }

        if (unsupported.Count > 0)
        {
            return "No aftertreatment channels and several emissions monitors dropped: consistent "
                 + "with a delete tune having been applied. Look elsewhere for the no-start cause.";
        }

        return "No aftertreatment channels reported. Consistent with a delete tune, though with "
             + "the emissions monitors still supported the picture is mixed — compare the "
             + "calibration ID and CVN against a stock reference to confirm.";
    }
}
