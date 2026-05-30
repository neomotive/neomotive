using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;

namespace Neomotive.ControlModule;

public abstract class TransmissionControlModule : ControllerBase
{
    protected TransmissionControlModule(ICanBus[] canBuses, short moduleAddress)
        : base(canBuses, moduleAddress)
    {
    }

}
