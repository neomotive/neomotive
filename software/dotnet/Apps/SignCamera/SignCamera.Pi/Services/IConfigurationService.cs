namespace Neomotive.SignCamera;

public interface IConfigurationService
{
    string CameraType { get; }
    string? CameraFilePath { get; }
    int CameraIndex { get; }

    // False positive capture settings
    float FalsePositiveThreshold { get; }
    string FalsePositiveCaptureFolder { get; }
    int MaxFalsePositiveCaptures { get; }

    // Positive detection capture settings
    string PositiveCaptureFolder { get; }
    int MaxPositiveCaptures { get; }

    // Manual capture settings
    bool EnableManualCapture { get; }
    string ManualCaptureFolder { get; }
    int MaxManualCaptures { get; }
}