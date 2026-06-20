using Meadow.Hardware;
using System;
using System.Collections.Generic;

namespace Neomotive.ScanTool.Core.Tests;

/// <summary>
/// Test double for ICanBus. Records sent frames and allows injecting received frames.
/// </summary>
public class FakeCanBus : ICanBus
{
    public event EventHandler<ICanFrame>? FrameReceived;
    public event EventHandler<CanErrorInfo>? BusError;

    public CanAcceptanceFilterCollection AcceptanceFilters { get; } = new(0);
    public CanBitrate BitRate { get; set; } = CanBitrate.Can_500kbps;

    public List<StandardDataFrame> SentFrames { get; } = [];

    public void WriteFrame(ICanFrame frame)
    {
        if (frame is StandardDataFrame sdf)
            SentFrames.Add(sdf);
    }

    public void InjectFrame(ICanFrame frame)
    {
        FrameReceived?.Invoke(this, frame);
    }

    public void ClearReceiveBuffers() { }
    public bool IsFrameAvailable() => false;
    public ICanFrame? ReadFrame() => null;
}
