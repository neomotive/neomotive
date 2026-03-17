using Meadow.Foundation.Telematics.OBD2;
using Meadow.Hardware;

namespace Neomotive.ControlModule;

public abstract class TransmissionControlModule : ControllerBase
{
    protected TransmissionControlModule(ICanBus[] canBuses, short moduleAddress)
        : base(canBuses, moduleAddress)
    {
    }
}
