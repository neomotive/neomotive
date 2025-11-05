using Meadow.Foundation.Motors;
using System;

namespace Neomotive.TransferCase;

public interface IGearSelectionMotor
{
    bool IsMoving { get; }

    event EventHandler<BidirectionalDcMotor.MotorState>? StateChanged;

    void BeginShiftDown();
    void BeginShiftUp();
    void StopShift();
}