using Meadow;
using Neomotive.Video;
using NeoMotive.Services;

namespace Neomotive.SignCamera;

public class CameraApp : App<RaspberryPi>
{
    private IDisplayService _displayService = default!;
    private IConfigurationService _configurationService = default!;
    private CameraFrameProcessor? _frameProcessor;
    private IGpioService _gpioService = default!;

    private readonly float _confidenceThreshold = 0.5f; // TODO: pull from config
    private readonly int _groupingWindowSeconds = 5; // TODO: pull from config
    private readonly bool _displayTest = false; // TODO: pull from config

    public override Task Initialize()
    {
        Resolver.Log.LogLevel = Meadow.Logging.LogLevel.Trace;

        _configurationService = new ConfigurationService();
        _gpioService = new GpioService(Device.Pins.GPIO14); // TX line, pin 8
        _displayService = new DisplayService_1306(Device.CreateI2cBus());

        if (_displayTest)
        {
        }
        else
        {
            InitializeFrameProcessor();
            InitializeManualCapture();
        }

        return base.Initialize();
    }

    private void InitializeFrameProcessor()
    {
        var speedLimitService = new SpeedLimitService(
            "./models/speed-limits-us.onnx",
            _confidenceThreshold,
            TimeSpan.FromSeconds(_groupingWindowSeconds),
            enableGrouping: true
        );

        var camera = InitializeCamera();
        _frameProcessor = new CameraFrameProcessor(camera, speedLimitService, _configurationService);

        _frameProcessor.SpeedLimitSignDetected += OnSpeedLimitSignDetected;
    }

    private void InitializeManualCapture()
    {
        if (_configurationService.EnableManualCapture
            && _gpioService is not null
            && _gpioService.ManualCameraTriggerPort is not null)
        {
            _gpioService.ManualCameraTriggerPort.Changed += OnManualCameraTriggerPortChanged;
        }
    }

    private void OnManualCameraTriggerPortChanged(object? sender, Meadow.Hardware.DigitalPortResult e)
    {
        Resolver.Log.Info("Manual camera trigger activated.");
        _displayService.ShowCaptureInProgress(true);

        // Trigger manual capture of frames
        _frameProcessor.TriggerManualCapture();
    }

    private void OnSpeedLimitSignDetected(object? sender, (int Speed, float Confidence) e)
    {
        // Update display with the finalized sign detection
        _displayService.UpdateSpeedLimit(e.Speed, e.Confidence);

    }

    public override async Task Run()
    {

        if (_frameProcessor is not null)
        {
            _ = _displayService.ShowStartup();
            await _frameProcessor.StartProcessing();
        }
        else if (_displayTest)
        {
            var i = 0;

            while (true)
            {
                _displayService.ShowText($"Test {i++}");
                await Task.Delay(1000);
            }
        }
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
