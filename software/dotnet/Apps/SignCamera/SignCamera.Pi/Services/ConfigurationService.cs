using Meadow;

namespace Neomotive.SignCamera;

public class ConfigurationService : IConfigurationService
{
    public string CameraType { get; init; } = "USB";
    /// <summary>
    /// The video file to use as camera input when File is the CameraType
    /// </summary>
    public string? CameraFilePath { get; init; }
    /// <summary>
    /// The index of the camera, when USB is the CameraType
    /// </summary>
    public int CameraIndex { get; init; } = 0;

    /// <summary>
    /// Minimum confidence threshold for capturing false positives
    /// </summary>
    public float FalsePositiveThreshold { get; init; } = 0.40f;
    /// <summary>
    /// Folder path where false positive frames will be saved
    /// </summary>
    public string FalsePositiveCaptureFolder { get; init; } = "./false_positives";
    /// <summary>
    /// Maximum number of false positive captures to prevent disk overflow
    /// </summary>
    public int MaxFalsePositiveCaptures { get; init; } = 1000;

    public bool EnableManualCapture { get; init; } = false;

    public ConfigurationService()
    {
        Resolver.App.Settings.TryGetValue("Neomotive.Camera", out string? cameraType);

        if (cameraType == "File")
        {
            Resolver.Log.Info("Using File as camera input");

            Resolver.App.Settings.TryGetValue("Neomotive.CameraFilePath", out string? cameraFile);

            var path = Path.GetFullPath(cameraFile ?? string.Empty);
            if (File.Exists(path))
            {
                CameraType = "File";
                CameraFilePath = path;
                Resolver.Log.Info($"Camera File: {CameraFilePath}");
            }
            else
            {
                Resolver.Log.Warn($"Camera File not found: {path}. Defaulting to USB.");
            }
        }
        else
        {
            Resolver.Log.Info("Using USB camera");

            // anything else defaults to USB
            cameraType = "USB";
        }

        Resolver.App.Settings.TryGetValue("Neomotive.CameraIndex", out string? indexString);

        if (int.TryParse(indexString, out int cameraIndex))
        {
            Resolver.Log.Info($"Using Camera Index: {cameraIndex}");
        }

        // Load false positive capture settings
        if (Resolver.App.Settings.TryGetValue("Neomotive.FalsePositiveThreshold", out string? fpThresholdString)
            && float.TryParse(fpThresholdString, out float fpThreshold))
        {
            FalsePositiveThreshold = fpThreshold;
            Resolver.Log.Info($"False Positive Threshold: {FalsePositiveThreshold}");
        }

        if (Resolver.App.Settings.TryGetValue("Neomotive.FalsePositiveCaptureFolder", out string? fpFolder))
        {
            FalsePositiveCaptureFolder = Path.GetFullPath(fpFolder);
            Resolver.Log.Info($"False Positive Capture Folder: {FalsePositiveCaptureFolder}");
        }

        if (Resolver.App.Settings.TryGetValue("Neomotive.MaxFalsePositiveCaptures", out string? maxCapturesString)
            && int.TryParse(maxCapturesString, out int maxCaptures))
        {
            MaxFalsePositiveCaptures = maxCaptures;
            Resolver.Log.Info($"Max False Positive Captures: {MaxFalsePositiveCaptures}");
        }

        if (Resolver.App.Settings.TryGetValue("Neomotive.EnableManualCapture", out string? manualCapture)
            && bool.TryParse(manualCapture, out bool enableManualCapture))
        {
            EnableManualCapture = enableManualCapture;
            Resolver.Log.Info($"Enable Manual Capture: {EnableManualCapture}");
        }
    }
}
