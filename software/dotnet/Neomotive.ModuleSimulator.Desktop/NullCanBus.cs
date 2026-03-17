using Meadow.Hardware;

namespace Neomotive.ModuleSimulator.Desktop;

/// <summary>
/// A no-op CAN bus for running the simulator without hardware.
/// </summary>
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
