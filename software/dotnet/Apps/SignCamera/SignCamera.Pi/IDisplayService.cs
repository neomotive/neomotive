namespace Neomotive.SignCamera;

public interface IDisplayService
{
    Task ShowStartup();
    Task UpdateSpeedLimit(int speedLimit, double confidence);
    void ShowCaptureInProgress(bool show);
}
