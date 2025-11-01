namespace Neomotive.SignCamera;

public interface IConfigurationService
{
    string CameraType { get; }
    string? CameraFilePath { get; }

}