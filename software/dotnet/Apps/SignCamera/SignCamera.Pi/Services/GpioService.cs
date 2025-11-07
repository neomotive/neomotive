using Meadow;
using Meadow.Hardware;

namespace Neomotive.SignCamera;

public class GpioService : IGpioService
{
    public IDigitalInterruptPort? ManualCameraTriggerPort { get; private set; }

    public GpioService(IPin manualCameraTriggerPin)
    {
        ManualCameraTriggerPort = manualCameraTriggerPin.CreateDigitalInterruptPort(InterruptMode.EdgeRising, ResistorMode.InternalPullDown, TimeSpan.FromMilliseconds(100));
    }
}
