using OpenCvSharp;

namespace NeoMotive.Services;

public class SpeedLimitDetection
{
    public int SpeedLimit { get; set; }
    public float Confidence { get; set; }
    public Rect2d BoundingBox { get; set; }
    public string Label { get; set; } = string.Empty;
}
