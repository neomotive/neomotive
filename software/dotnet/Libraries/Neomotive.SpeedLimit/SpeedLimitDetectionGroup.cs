namespace NeoMotive.Services;

public class SpeedLimitDetectionGroup
{
    public int SpeedLimit { get; set; }
    public float HighestConfidence { get; set; }
    public int DetectionCount { get; set; }
    public DateTime FirstDetected { get; set; }
    public DateTime LastDetected { get; set; }
}
