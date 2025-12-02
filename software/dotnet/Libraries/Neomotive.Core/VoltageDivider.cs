using Meadow.Units;

namespace Neomotive;

public class VoltageDivider
{
    /// <summary>
    /// Gets the value of Resistance R1.
    /// </summary>
    public Resistance R1 { get; }
    /// <summary>
    /// Represents the input voltage.
    /// </summary>
    public Voltage Vin { get; }

    public VoltageDivider(Voltage inputVoltage, Resistance fixedResistor)
    {
        Vin = inputVoltage;
        R1 = fixedResistor;
    }

    /// <summary>
    /// Calculates the output voltage based on given resistance and internal resistance.
    /// </summary>
    /// <param name="r2">The given resistance.</param>
    /// <returns>The calculated output voltage as Voltage object.</returns>
    /// <remarks></remarks>
    /// <exception cref="ArgumentException">Thrown if the provided resistance is invalid.</exception>
    public Voltage CalcOutputVoltage(Resistance r2)
    {
        return new Voltage(Vin.Volts * (r2.Ohms / (r2.Ohms + R1.Ohms)));
    }

    /// <summary>
    /// Calculates the R2 resistance based on the provided voltage and resistances R1 and Vin.
    /// </summary>
    /// <param name="vOut">The output voltage.</param>
    /// <remarks>This method uses Ohm's law (R = V / I) to calculate the resistance value.</remarks>
    /// <returns>A new Resistance object representing the calculated R2 resistance.</returns>
    public Resistance CalcR2Resistance(Voltage vOut)
    {
        return new Resistance(vOut.Volts * R1.Ohms / (Vin.Volts - vOut.Volts));
    }
}
