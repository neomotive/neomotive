using System;
using System.Collections.Generic;

namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// A persistent snapshot of all trouble codes, module statuses, and readiness monitors
/// from a vehicle scan.
/// </summary>
public sealed class ScanCapture
{
    public int SchemaVersion { get; set; } = 1;

    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

    public string? Vin { get; set; }

    public string? Make { get; set; }

    public string? Model { get; set; }

    public int? Year { get; set; }

    public string? Country { get; set; }

    public string? Protocol { get; set; }

    public string? EcuName { get; set; }

    public string? CalibrationId { get; set; }

    public string? Cvn { get; set; }

    public bool MilOn { get; set; }

    public int TotalFaultCount { get; set; }

    public List<ScanModuleRecord> Modules { get; set; } = [];

    public List<ScanReadinessRecord> ReadinessMonitors { get; set; } = [];
}

/// <summary>Details of one module in a vehicle scan capture.</summary>
public sealed class ScanModuleRecord
{
    public string Name { get; set; } = "";

    public string Address { get; set; } = "";

    public string Protocol { get; set; } = "";

    public int CodeCount { get; set; }

    public List<ScanDtcRecord> Codes { get; set; } = [];
}

/// <summary>One diagnostic trouble code in a vehicle scan capture.</summary>
public sealed class ScanDtcRecord
{
    public string Code { get; set; } = "";

    public string Description { get; set; } = "";

    public string Detail { get; set; } = "";

    public string Status { get; set; } = "";

    public string Protocol { get; set; } = "";
}

/// <summary>One readiness monitor in a vehicle scan capture.</summary>
public sealed class ScanReadinessRecord
{
    public string Name { get; set; } = "";

    public string Status { get; set; } = "";
}
