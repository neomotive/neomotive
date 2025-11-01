namespace Neomotive.SignCamera;

public interface IDisplayService
{
    void ShowStartup();
    void UpdateSpeedLimit(int speedLimit, double confidence);
}
