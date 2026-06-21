namespace Neomotive.ScanTool.Core;

public record PidDescriptor(
    Meadow.Foundation.Telematics.J1979.Pid Id,
    string Name,
    string Unit,
    double Scale,
    double Offset,
    int ByteCount,
    double Min,
    double Max);
