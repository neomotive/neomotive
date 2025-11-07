using Meadow.Hardware;

namespace Neomotive.SignCamera;

public interface IGpioService
{
    IDigitalInterruptPort? ManualCameraTriggerPort { get; }
}
