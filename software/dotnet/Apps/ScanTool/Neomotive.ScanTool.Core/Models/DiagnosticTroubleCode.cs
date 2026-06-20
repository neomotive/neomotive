namespace Neomotive.ScanTool.Core;

public enum DtcStatus { Stored, Pending, Permanent }
public enum DtcType { Generic, Manufacturer }

public record DiagnosticTroubleCode(
    string Code,
    string Description,
    DtcStatus Status,
    DtcType Type);
