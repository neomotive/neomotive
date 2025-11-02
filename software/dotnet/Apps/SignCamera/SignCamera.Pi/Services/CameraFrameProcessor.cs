using Meadow;
using Neomotive.Video;
using NeoMotive.Services;
using OpenCvSharp;

namespace Neomotive.SignCamera;

public class CameraFrameProcessor
{
    public event EventHandler<(int Speed, float Confidence)>? SpeedLimitSignDetected;
    private readonly ICamera _camera;
    private readonly SpeedLimitService _speedLimitService = default!;

    private readonly object _syncRoot = new object();

    private Frame? _latestFrame;
    private int _frameCount;
    private DateTime _lastStatusUpdate = DateTime.Now;
    private int _lastFrameCount = 0;
    private readonly int _statusIntervalSeconds = 5;
    private int _detectionCount;
    private int _signDetectionCount; // Grouped/finalized sign detections

    public CameraFrameProcessor(
        ICamera camera,
        SpeedLimitService speedLimitService)
    {
        _camera = camera;
        _speedLimitService = speedLimitService;
    }

    public async Task StartProcessing()
    {
        // Subscribe to grouped sign detection events
        _speedLimitService.SignDetected += OnSignDetected;

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

        await _camera.StartCapture();

        await CameraProcessLoop();
    }

    private void OnSignDetected(object? sender, SpeedLimitDetectionGroup group)
    {
        _signDetectionCount++;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Resolver.Log.Info($"[SIGN DETECTED] {timestamp} | {group.SpeedLimit} mph | Confidence: {group.HighestConfidence:P1} | Detections: {group.DetectionCount} | Duration: {(group.LastDetected - group.FirstDetected).TotalSeconds:F1}s");

        SpeedLimitSignDetected?.Invoke(this, (group.SpeedLimit, (float)group.HighestConfidence));
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
                            // Note: Display update now happens in OnSignDetected when group is finalized
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

                Console.WriteLine($"[Status] Runtime: {totalRuntime:F1}s | Frames: {_frameCount} | FPS: {currentFps:F1} | Raw Detections: {_detectionCount} | Signs: {_signDetectionCount}");

                _lastStatusUpdate = DateTime.Now;
                _lastFrameCount = _frameCount;
            }

            // Small delay to prevent maxing out CPU
            await Task.Delay(100);
        }
    }
}
