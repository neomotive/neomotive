using Meadow;

namespace Neomotive.SignCamera;

public class ConfigurationService : IConfigurationService
{
    public string CameraType { get; init; } = "USB";
    public string? CameraFilePath { get; init; }

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
    }

}
