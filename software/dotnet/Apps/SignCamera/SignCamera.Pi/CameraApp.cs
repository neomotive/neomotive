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

    /// <summary>
    /// Initializes the object. Sets up necessary services and initializes camera and frame processor.
    /// </summary>
    /// <remarks>
    /// This method initializes the necessary services, creates a new instance of `CameraFrameProcessor`, and sets up event handling for speed limit sign detection.
    /// </remarks>
    /// <exception cref="Exception">Any exception that might occur during the initialization process.</exception>
    /// <seealso cref="DisplayService_1306"/>
    /// <seealso cref="ConfigurationService"/>
    /// <seealso cref="SpeedLimitService"/>
    /// <seealso cref="CameraFrameProcessor"/>
    /// <seealso cref="OnSpeedLimitSignDetected"/>
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
        _frameProcessor = new CameraFrameProcessor(camera, speedLimitService, _configurationService);

        _frameProcessor.SpeedLimitSignDetected += OnSpeedLimitSignDetected;
        return base.Initialize();
    }

    private void OnSpeedLimitSignDetected(object? sender, (int Speed, float Confidence) e)
    {
        // Update display with the finalized sign detection
        _displayService.UpdateSpeedLimit(e.Speed, e.Confidence);

    }

    /// <summary>
    /// Starts processing and shows startup display.
    /// </summary>
    /// <exception cref="Exception">Any exceptions that may occur during the method execution.</exception>
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
            return new UsbCamera(_configurationService.CameraIndex);
        }
    }
}
