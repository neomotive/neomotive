using Meadow.Hardware;
using System;

namespace Neomotive.ScanTool.Core;

public class NullCanBus : ICanBus
{
    public event EventHandler<ICanFrame>? FrameReceived;
    public event EventHandler<CanErrorInfo>? BusError;
    public CanAcceptanceFilterCollection AcceptanceFilters { get; } = new(0);
    public CanBitrate BitRate { get; set; } = CanBitrate.Can_500kbps;

    public void WriteFrame(ICanFrame frame) { }
    public void ClearReceiveBuffers() { }
    public bool IsFrameAvailable() => false;
    public ICanFrame? ReadFrame() => null;
}
