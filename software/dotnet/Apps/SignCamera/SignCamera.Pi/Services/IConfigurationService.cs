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
}