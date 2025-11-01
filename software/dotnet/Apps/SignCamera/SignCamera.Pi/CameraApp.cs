using Meadow;
using Neomotive.Video;
using NeoMotive.Services;
using OpenCvSharp;

namespace Neomotive.SignCamera;

public class CameraApp : App<RaspberryPi>
{
    private IDisplayService _displayService = default!;
    private IConfigurationService _configurationService = default!;
    private SpeedLimitService _speedLimitService = default!;
    private ICamera _camera = default!;
    private Frame? _latestFrame;
    private int _frameCount;
    private int _detectionCount;
    private DateTime _lastStatusUpdate = DateTime.Now;
    private int _lastFrameCount = 0;
    private readonly int _statusIntervalSeconds = 5;
    private readonly float _confidenceThreshold = 0.5f; // TODO: pull from config
    private readonly object _syncRoot = new object();

    public override Task Initialize()
    {
        _configurationService = new ConfigurationService();
        _displayService = new DisplayService_1306(Device.CreateI2cBus());
        _speedLimitService = new SpeedLimitService("./models/speed-limits-us.onnx");
        return base.Initialize();
    }

    public override async Task Run()
    {
        _displayService.ShowStartup();

        await Task.Run(() =>
        {
            InitializeCamera();
        });

        // td
        _displayService.UpdateSpeedLimit(0, 0);

        await _camera.StartCapture();

        await CameraProcessLoop();
    }

    private async Task CameraProcessLoop()
    {
        int? durationSeconds = null;
        DateTime startTime = DateTime.Now;

        while (true)
        {
            // Check duration timeout
            if (durationSeconds.HasValue)
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (elapsed >= durationSeconds.Value)
                {
                    Console.WriteLine($"Duration limit reached ({durationSeconds.Value} seconds)");
                    break;
                }
            }

            Mat? matToProcess = null;

            // Get and clone the latest frame (thread-safe)
            lock (_syncRoot)
            {
                if (_latestFrame != null)
                {
                    var mat = _latestFrame.ToMat();
                    matToProcess = mat.Clone();
                }
            }

            // Process frame if we have one
            if (matToProcess != null && _speedLimitService != null)
            {
                try
                {
                    var detections = _speedLimitService.CheckForSpeedLimit(matToProcess, saveToFile: false, drawDetections: false);
                    if (detections.Count > 0)
                    {
                        _detectionCount++;
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                        foreach (var detection in detections)
                        {
                            Resolver.Log.Info($"[{timestamp}] Frame #{_frameCount}: {detection.SpeedLimit} mph detected (confidence: {detection.Confidence:P1})");
                            if (detection.Confidence >= _confidenceThreshold)
                            {
                                _displayService.UpdateSpeedLimit(detection.SpeedLimit, detection.Confidence);
                            }
                        }
                    }
                }
                finally
                {
                    matToProcess.Dispose();
                }
            }

            // Periodic status output
            var timeSinceLastStatus = (DateTime.Now - _lastStatusUpdate).TotalSeconds;
            if (timeSinceLastStatus >= _statusIntervalSeconds)
            {
                var totalRuntime = (DateTime.Now - startTime).TotalSeconds;
                var framesSinceLastStatus = _frameCount - _lastFrameCount;
                var currentFps = framesSinceLastStatus / timeSinceLastStatus;

                Console.WriteLine($"[Status] Runtime: {totalRuntime:F1}s | Frames: {_frameCount} | FPS: {currentFps:F1} | Detections: {_detectionCount}");

                _lastStatusUpdate = DateTime.Now;
                _lastFrameCount = _frameCount;
            }

            // Small delay to prevent maxing out CPU
            await Task.Delay(100);
        }
    }

    private void InitializeCamera()
    {
        if (_configurationService.CameraType == "File" &&
           !string.IsNullOrEmpty(_configurationService.CameraFilePath))
        {
            _camera = new VideoFileCamera(_configurationService.CameraFilePath);
        }
        else
        {
            _camera = new UsbCamera();
        }

        _camera.FrameCaptured += (sender, frame) =>
        {
            // the camera captures frames faster than we can process them,
            // so we just store the latest frame for processing in the main loop
            lock (_syncRoot)
            {
                // Dispose old frame if exists
                _latestFrame?.Dispose();
                // Store new frame
                _latestFrame = frame;
                _frameCount++;
            }
        };
    }
}
