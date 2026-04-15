using Meadow.Hardware;

namespace Neomotive.ModuleSimulator;

/// <summary>
/// Wraps an ICanBus to log all received and transmitted frames to a CanPacketLog.
/// </summary>
public class LoggingCanBus : ICanBus
{
    private readonly ICanBus _inner;
    private readonly CanPacketLog _log;

    public event EventHandler<ICanFrame>? FrameReceived;
    public event EventHandler<CanErrorInfo>? BusError;
    public CanAcceptanceFilterCollection AcceptanceFilters => _inner.AcceptanceFilters;
    public CanBitrate BitRate { get => _inner.BitRate; set => _inner.BitRate = value; }

    public LoggingCanBus(ICanBus inner, CanPacketLog log)
    {
        _inner = inner;
        _log = log;

        _inner.FrameReceived += (s, f) =>
        {
            Console.WriteLine("Frame received");
            if (f is StandardDataFrame sdf)
            {
                _log.Add(new CanPacketEntry(DateTime.Now, sdf.ID, sdf.Payload.ToArray(), false));
            }
            FrameReceived?.Invoke(s, f);
        };

        _inner.BusError += (s, e) => BusError?.Invoke(s, e);
    }

    public void WriteFrame(ICanFrame frame)
    {
        if (frame is StandardDataFrame sdf)
        {
            _log.Add(new CanPacketEntry(DateTime.Now, sdf.ID, sdf.Payload.ToArray(), true));
        }
        _inner.WriteFrame(frame);
    }

    public void ClearReceiveBuffers() => _inner.ClearReceiveBuffers();
    public bool IsFrameAvailable() => _inner.IsFrameAvailable();
    public ICanFrame? ReadFrame() => _inner.ReadFrame();
}
