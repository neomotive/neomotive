namespace Neomotive.ScanTool.Core;

public record ReadinessMonitor(string Name, bool Supported, bool Ready)
{
    public bool IsNotSupported => !Supported;
    public bool IsReady => Supported && Ready;
    public bool IsIncomplete => Supported && !Ready;
}
