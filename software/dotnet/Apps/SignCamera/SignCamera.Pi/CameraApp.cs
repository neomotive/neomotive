using Meadow;
using Neomotive.Video;
using NeoMotive.Services;

namespace Neomotive.SignCamera;

public class CameraApp : App<RaspberryPi>
{
    private IDisplayService _displayService = default!;
    private IConfigurationService _configurationService = default!;
    private CameraFrameProcessor _frameProcessor = default!;


    private readonly float _confidenceThreshold = 0.5f; // TODO: pull from config
    private readonly int _groupingWindowSeconds = 5; // TODO: pull from config

    public override Task Initialize()
    {
        _configurationService = new ConfigurationService();
        _displayService = new DisplayService_1306(Device.CreateI2cBus());
        var speedLimitService = new SpeedLimitService(
            "./models/speed-limits-us.onnx",
            _confidenceThreshold,
            TimeSpan.FromSeconds(_groupingWindowSeconds),
            enableGrouping: true
        );

        var camera = InitializeCamera();
        _frameProcessor = new CameraFrameProcessor(camera, speedLimitService);

        _frameProcessor.SpeedLimitSignDetected += OnSpeedLimitSignDetected;
        return base.Initialize();
    }

    private void OnSpeedLimitSignDetected(object? sender, (int Speed, float Confidence) e)
    {
        // Update display with the finalized sign detection
        _displayService.UpdateSpeedLimit(e.Speed, e.Confidence);

    }

    public override async Task Run()
    {
        _ = _displayService.ShowStartup();

        await _frameProcessor.StartProcessing();

    }

    private ICamera InitializeCamera()
    {
        if (_configurationService.CameraType == "File" &&
           !string.IsNullOrEmpty(_configurationService.CameraFilePath))
        {
            return new VideoFileCamera(_configurationService.CameraFilePath);
        }
        else
        {
            return new UsbCamera();
        }
    }
}
