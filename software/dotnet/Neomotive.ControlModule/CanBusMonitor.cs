using Meadow.Foundation.Telematics.OBD2;
using Meadow.Hardware;

namespace Neomotive.ControlModule;

internal class CanBusMonitor
{
    public event EventHandler<Obd2QueryFrame>? QueryReceived;

    public ICanBus Bus { get; }

    public CanBusMonitor(ICanBus bus)
    {
        Bus = bus;
        bus.FrameReceived += OnFrameReceived;
    }

    private void OnFrameReceived(object? sender, ICanFrame frame)
    {
        if (frame is not StandardDataFrame sdf) return;
        if (sdf.ID != Obd2Frame.Obd2RequestID) return;

        try
        {
            if (Obd2Frame.FromCanFrame(sdf) is Obd2QueryFrame query)
            {
                QueryReceived?.Invoke(this, query);
            }
        }
        catch
        {
            // not a valid OBD2 frame - ignore
        }
    }
}
