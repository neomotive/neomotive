using System;
using System.Collections.Generic;

namespace Neomotive.TransferCase;

public interface ITransferCaseGearSelector : IEnumerable<TransferCaseSwitchSelectionBounds>
{
    event EventHandler<TransferCasePosition>? RequestedPositionChanged;

    TransferCasePosition RequestedPosition { get; }
    TransferCasePosition CurrentSwitchPosition { get; }
}
