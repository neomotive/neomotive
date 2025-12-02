using Meadow.Hardware;
using Meadow.Units;

namespace Neomotive;

public class VoltageDividerPort : VoltageDivider
{
    /// <summary>
    /// Represents an observable analog input port.
    /// </summary>
    public IObservableAnalogInputPort Input { get; }

    public VoltageDividerPort(IObservableAnalogInputPort input, Resistance fixedResistor)
        : base(input.ReferenceVoltage, fixedResistor)
    {
        Input = input;
    }

    /// <summary>
    /// Reads R2 resistance using given input voltage, R1 resistance and voltage.
    /// </summary>
    /// <returns>A new instance of Resistance representing the calculated R2 resistance value.</returns>
    /// <remarks></remarks>
    /// <exception cref="Exception">Any exception that may occur during calculation.</exception>
    public Resistance ReadR2Resistance()
    {
        var ohms = Input.Voltage.Volts * R1.Ohms / (Vin.Volts - Input.Voltage.Volts);
        Console.WriteLine($"Resistance: {ohms:0.00}ohms");
        return new Resistance(ohms, Resistance.UnitType.Ohms);
    }
}
