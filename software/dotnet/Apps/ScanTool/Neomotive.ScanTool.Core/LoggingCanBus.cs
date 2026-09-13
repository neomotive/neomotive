using Meadow.Hardware;
using System;
using System.Threading;

namespace Neomotive.ScanTool.Core;

/// <summary>
/// Wraps an ICanBus to log all received and transmitted frames to a CanPacketLog when logging is enabled.
/// </summary>
public class LoggingCanBus : ICanBus
{
    private readonly ICanBus _inner;
    private readonly CanPacketLog _log;

    public event EventHandler<ICanFrame>? FrameReceived;
    public event EventHandler<CanErrorInfo>? BusError;
    public CanAcceptanceFilterCollection AcceptanceFilters => _inner.AcceptanceFilters;
    public CanBitrate BitRate { get => _inner.BitRate; set => _inner.BitRate = value; }

    public CanPacketLog Log => _log;

    /// <summary>
    /// Whether frames are recorded into the packet log. Every recorded frame costs a payload copy
    /// and a <see cref="CanPacketEntry"/>, paid on both directions of every request the tool makes
    /// — including the live-data polling loop, which runs continuously and whose frames nobody is
    /// looking at unless the CAN tab is open. The CAN tab turns recording on.
    /// </summary>
    public bool CaptureEnabled { get; set; }

    private long _framesReceived;

    /// <summary>
    /// Every frame seen on the bus since start-up, recorded or not.
    /// <para>
    /// Deliberately separate from <see cref="CaptureEnabled"/>: this is a bare increment that costs
    /// nothing, and it answers a question no amount of OBD-II request/response logic can — whether
    /// there is any CAN traffic at all. A vehicle too old for CAN and a vehicle whose gateway is
    /// refusing to answer look identical from the protocol layer, and they need completely
    /// different advice.
    /// </para>
    /// </summary>
    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    public LoggingCanBus(ICanBus inner, CanPacketLog log)
    {
        _inner = inner;
        _log = log;

        _inner.FrameReceived += (s, f) =>
        {
            Interlocked.Increment(ref _framesReceived);

            if (CaptureEnabled && f is StandardDataFrame sdf)
            {
                _log.Add(new CanPacketEntry(DateTime.Now, sdf.ID, sdf.Payload.ToArray(), false));
            }
            FrameReceived?.Invoke(s, f);
        };

        _inner.BusError += (s, e) => BusError?.Invoke(s, e);
    }

    public void WriteFrame(ICanFrame frame)
    {
        if (CaptureEnabled && frame is StandardDataFrame sdf)
        {
            _log.Add(new CanPacketEntry(DateTime.Now, sdf.ID, sdf.Payload.ToArray(), true));
        }
        _inner.WriteFrame(frame);
    }

    public void ClearReceiveBuffers() => _inner.ClearReceiveBuffers();
    public bool IsFrameAvailable() => _inner.IsFrameAvailable();
    public ICanFrame? ReadFrame() => _inner.ReadFrame();
}
