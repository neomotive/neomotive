namespace Neomotive.TransferCase;

public class FordSwitchSettings : ISelectorSwitchVoltageSettings
{
    /// <summary>
    /// Represents the minimum value in a range of 4 consecutive data points.
    /// </summary>
    public double Low4Min { get; set; }
    /// <summary>
    /// A property representing the low value compared to a maximum in a scale with 4 levels.
    /// </summary>
    public double Low4Max { get; set; }
    /// <summary>
    /// Represents the high value for the last 4 minutes.
    /// </summary>
    public double High4Min { get; set; }
    /// <summary>
    /// Represents the maximum value from the last four high readings.
    /// </summary>
    public double High4Max { get; set; }
    /// <summary>
    /// Gets or sets the minimum value obtained by subtracting the high value from twice the high value.
    /// </summary>
    public double High2Min { get; set; }
    /// <summary>
    /// A property representing the ratio of High value to Maximum value.
    /// </summary>
    public double High2Max { get; set; }

    public FordSwitchSettings()
    {
        Low4Min = 1.0;
        Low4Max = 1.23;
        High4Min = 1.23;
        High4Max = 1.52;
        High2Min = 1.52;
        High2Max = 2.25;
    }
}
