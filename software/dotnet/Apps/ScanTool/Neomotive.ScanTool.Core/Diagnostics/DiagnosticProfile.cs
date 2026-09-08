using System;
using System.Collections.Generic;

namespace Neomotive.ScanTool.Core.Diagnostics;

/// <summary>How a capture decides to start recording.</summary>
public enum ProfileTriggerMode
{
    /// <summary>Wait for the operator to press the trigger button.</summary>
    Manual,

    /// <summary>Fire when a signal crosses a threshold.</summary>
    Threshold,

    /// <summary>
    /// Fire when two signals simultaneously satisfy their conditions (AND logic).
    /// Use this for compound diagnostics: e.g. "RPM above 150 AND rail pressure below 5000 kPa".
    /// </summary>
    CompoundThreshold,

    /// <summary>Fire on the first response from the ECU, for arming before key-on.</summary>
    BusWake,
}

public record ProfileTrigger
{
    public ProfileTriggerMode Mode { get; init; } = ProfileTriggerMode.Manual;

    /// <summary>Signal key — a PID enum name, or the key of a Mode $22 definition.</summary>
    public string? Signal { get; init; }

    /// <summary>True for "rises above", false for "falls below".</summary>
    public bool Above { get; init; } = true;

    public double Value { get; init; }

    /// <summary>How long the condition must hold, to reject transient noise.</summary>
    public int DwellMs { get; init; }

    // ── Secondary condition (CompoundThreshold only) ─────────────────────────

    /// <summary>Second signal key for <see cref="ProfileTriggerMode.CompoundThreshold"/>.</summary>
    public string? Signal2 { get; init; }

    /// <summary>True for "rises above", false for "falls below" — for the second condition.</summary>
    public bool Above2 { get; init; } = true;

    public double Value2 { get; init; }
}


/// <summary>
/// Ends a capture once the watched signal has been active and then goes quiet.
/// </summary>
public record ProfileStop
{
    public bool Enabled { get; init; }

    public string? Signal { get; init; }

    public double Floor { get; init; }

    public int DurationMs { get; init; } = 5000;
}

/// <summary>
/// A named, self-contained capture setup: which signals to record, when to start, and when to
/// stop. Profiles are data, not code — they load from <c>config/diagnostic-profiles.json</c> so
/// any scenario can be described without a rebuild.
/// </summary>
/// <remarks>
/// The built-in library covers common cases, but no fixed set can cover every diagnostic. A
/// profile is only a starting point: applying one fills in the capture settings, which the
/// operator is then free to edit for the job in front of them.
/// </remarks>
public record DiagnosticProfile
{
    /// <summary>Stable identifier, unique within the library.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Grouping shown in the profile picker, e.g. "Fuel &amp; Air".</summary>
    public string Category { get; init; } = "General";

    public string Name { get; init; } = string.Empty;

    /// <summary>What this profile is for, and what to look at in the result.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Signal keys to record. Unknown keys are skipped rather than treated as an error, so a
    /// profile referencing a Mode $22 channel still works on a vehicle where it is not configured.
    /// </summary>
    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();

    public ProfileTrigger Trigger { get; init; } = new();

    public double PreTriggerSeconds { get; init; } = 5;

    public double MaxDurationSeconds { get; init; } = 120;

    public ProfileStop Stop { get; init; } = new();

    /// <summary>Default capture file name stem for this profile.</summary>
    public string? CaptureName { get; init; }

    public override string ToString() => Name;
}
