namespace Neomotive.TransferCase;

public class MP3023Settings : ITransferCaseVoltageSettings
{
    /// <summary>
    /// Represents the minimum value in a range of 4 consecutive samples.
    /// </summary>
    public double Low4Min { get; set; }
    /// <summary>
    /// Represents the ratio between the lowest value and maximum value (exclusive) in a set of four consecutive values.
    /// </summary>
    public double Low4Max { get; set; }
    /// <summary>
    /// Represents the high value of the last 4 minutes.
    /// </summary>
    public double High4Min { get; set; }
    /// <summary>
    /// Represents the maximum value among the high4 readings.
    /// </summary>
    public double High4Max { get; set; }
    /// <summary>
    /// Gets or sets the high value reduced by the minimum value.
    /// </summary>
    public double High2Min { get; set; }
    /// <summary>
    /// Represents the ratio of a given high value to its maximum allowable value.
    /// </summary>
    public double High2Max { get; set; }

    public MP3023Settings()
    {
        Low4Min = 1.2;
        Low4Max = 1.7;
        High4Min = 3.2;
        High4Max = 3.7;
        High2Min = 2.2;
        High2Max = 2.7;
    }
}
