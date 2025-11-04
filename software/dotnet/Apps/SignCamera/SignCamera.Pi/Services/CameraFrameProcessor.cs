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
    private readonly IConfigurationService _configurationService = default!;

    private readonly object _syncRoot = new object();

    private Frame? _latestFrame;
    private int _frameCount;
    private DateTime _lastStatusUpdate = DateTime.Now;
    private int _lastFrameCount = 0;
    private readonly int _statusIntervalSeconds = 5;
    private int _detectionCount;
    private int _signDetectionCount; // Grouped/finalized sign detections
    private int _falsePositiveCaptureCount = 0;

    public CameraFrameProcessor(
        ICamera camera,
        SpeedLimitService speedLimitService,
        IConfigurationService configurationService)
    {
        _camera = camera;
        _speedLimitService = speedLimitService;
        _configurationService = configurationService;

        // Ensure false positive capture folder exists
        if (!Directory.Exists(_configurationService.FalsePositiveCaptureFolder))
        {
            Directory.CreateDirectory(_configurationService.FalsePositiveCaptureFolder);
            Resolver.Log.Info($"Created false positive capture folder: {_configurationService.FalsePositiveCaptureFolder}");
        }
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

                        // Check for false positives to capture
                        bool hasFalsePositive = false;
                        foreach (var detection in detections)
                        {
                            Resolver.Log.Info($"[{timestamp}] Frame #{_frameCount}: {detection.SpeedLimit} mph detected (confidence: {detection.Confidence:P1})");

                            // Check if this is a false positive (between thresholds)
                            if (detection.Confidence >= _configurationService.FalsePositiveThreshold
                                && detection.Confidence < _speedLimitService.ConfidenceThreshold)
                            {
                                hasFalsePositive = true;
                            }
                        }

                        // Capture false positives if within limit
                        if (hasFalsePositive && _falsePositiveCaptureCount < _configurationService.MaxFalsePositiveCaptures)
                        {
                            CaptureFalsePositive(matToProcess, detections, timestamp);
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

    private void CaptureFalsePositive(Mat frame, List<SpeedLimitDetection> detections, string timestamp)
    {
        try
        {
            // Clone the frame to avoid modifying the original
            using var annotatedFrame = frame.Clone();

            // Draw bounding boxes for all false positive detections
            foreach (var detection in detections)
            {
                // Only draw detections that are in the false positive range
                if (detection.Confidence >= _configurationService.FalsePositiveThreshold
                    && detection.Confidence < _speedLimitService.ConfidenceThreshold)
                {
                    var box = detection.BoundingBox;
                    var rect = new Rect((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height);

                    // Draw red bounding box
                    Cv2.Rectangle(annotatedFrame, rect, OpenCvSharp.Scalar.Red, 2);

                    // Draw label with class and confidence
                    var label = $"{detection.Label} {detection.Confidence:P1}";
                    Cv2.PutText(annotatedFrame, label, new Point((int)box.X, (int)box.Y - 5),
                        HersheyFonts.HersheySimplex, 0.6, OpenCvSharp.Scalar.Yellow, 2);
                }
            }

            // Generate filename with date, class, and confidence
            // Format: YYYYMMDD_HHmmss_mph30_45.5.jpg
            var dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Get the highest confidence false positive for the filename
            var falsePositiveDetections = detections
                .Where(d => d.Confidence >= _configurationService.FalsePositiveThreshold
                            && d.Confidence < _speedLimitService.ConfidenceThreshold)
                .OrderByDescending(d => d.Confidence)
                .ToList();

            if (falsePositiveDetections.Count > 0)
            {
                var primaryDetection = falsePositiveDetections.First();
                var confidenceStr = (primaryDetection.Confidence * 100).ToString("F1");
                var filename = $"{dateStr}_{primaryDetection.Label}_{confidenceStr}.jpg";
                var outputPath = Path.Combine(_configurationService.FalsePositiveCaptureFolder, filename);

                // Save the annotated frame
                Cv2.ImWrite(outputPath, annotatedFrame);

                _falsePositiveCaptureCount++;
                Resolver.Log.Info($"[FALSE POSITIVE] Captured frame {_falsePositiveCaptureCount}/{_configurationService.MaxFalsePositiveCaptures}: {filename}");
            }
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"Error capturing false positive: {ex.Message}");
        }
    }
}
