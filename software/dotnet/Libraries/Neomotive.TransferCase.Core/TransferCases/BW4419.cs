using Meadow.Hardware;
using System.Collections.Generic;

namespace Neomotive.TransferCase;

/// <summary>
/// The Borg Warner BW4419 3-position transfer case
/// </summary>
public class BW4419 : TransferCaseBase
{
    private readonly Dictionary<int, TransferCasePosition> _positionTruthTable = new();
    private readonly IDigitalInputPort _positionSwitch1;
    private readonly IDigitalInputPort _positionSwitch2;
    private readonly IDigitalInputPort _positionSwitch3;
    private readonly IDigitalInputPort _positionSwitch4;

    public BW4419(
        GearSelectionMotor motor,
        IDigitalInputPort positionSwitch1,
        IDigitalInputPort positionSwitch2,
        IDigitalInputPort positionSwitch3,
        IDigitalInputPort positionSwitch4,
        ISafetyInterlock? safetyInterlock)
        : base(motor, safetyInterlock, null)
    {
        _positionTruthTable.Add((1 << 0) | (1 << 2), TransferCasePosition.High2);     // 1 + 3
        _positionTruthTable.Add((1 << 1) | (1 << 2) | (1 << 3), TransferCasePosition.High4); // 2 + 3 + 4
        _positionTruthTable.Add((1 << 2) | (1 << 3), TransferCasePosition.Neutral);   // 3 + 4
        _positionTruthTable.Add((1 << 0) | (1 << 1) | (1 << 3), TransferCasePosition.Low4);  // 1 + 2 + 4

        _positionSwitch1 = positionSwitch1;
        _positionSwitch2 = positionSwitch2;
        _positionSwitch3 = positionSwitch3;
        _positionSwitch4 = positionSwitch4;
    }

    /// <summary>
    /// An array representing the supported gear positions for the transfer case.
    /// </summary>
    public override TransferCasePosition[] SupportedGears => new TransferCasePosition[]
        {
            TransferCasePosition.High2,
            TransferCasePosition.High4,
            TransferCasePosition.Neutral,
            TransferCasePosition.Low4,
        };

    /// <summary>
    /// Gets the current gear of the transfer case. This property is an override from the base class.
    /// </summary>
    public override TransferCasePosition CurrentGear
    {
        get
        {
            // switches combine to give you a gear - it's not 1 switch == 1 gear
            var position = 0;

            if (_positionSwitch1.State) position |= (1 << 0);
            if (_positionSwitch2.State) position |= (1 << 1);
            if (_positionSwitch3.State) position |= (1 << 2);
            if (_positionSwitch4.State) position |= (1 << 3);

            if (_positionTruthTable.ContainsKey(position))
            {
                return _positionTruthTable[position];
            }
            else
            {
                return TransferCasePosition.Unknown;
            }
        }
    }
}
