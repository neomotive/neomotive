using Meadow;
using Neomotive.Video;
using NeoMotive.Services;
using OpenCvSharp;

namespace Neomotive.SignCamera;

public class CameraFrameProcessor
{
    /// <summary>
    /// Raised when a Speed Limit Sign is detected, along with its speed and confidence level.
    /// </summary>
    public event EventHandler<(int Speed, float Confidence)>? SpeedLimitSignDetected;
    public event EventHandler<bool>? ManualCaptureStateChanged;

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
    private int _positiveCaptureCount = 0;
    private int _manualCaptureSessionCount = 0;

    // Manual capture state
    private bool _manualCaptureActive = false;
    private const int MANUAL_CAPTURE_DURATION_SECONDS = 5;
    private readonly List<Mat> _manualCaptureFrames = new List<Mat>();
    private DateTime _manualCaptureStartTime;

    public CameraFrameProcessor(
        ICamera camera,
        SpeedLimitService speedLimitService,
        IConfigurationService configurationService)
    {
        _camera = camera;
        _speedLimitService = speedLimitService;
        _configurationService = configurationService;

        // Ensure capture folders exist
        if (!Directory.Exists(_configurationService.FalsePositiveCaptureFolder))
        {
            Directory.CreateDirectory(_configurationService.FalsePositiveCaptureFolder);
            Resolver.Log.Info($"Created false positive capture folder: {_configurationService.FalsePositiveCaptureFolder}");
        }

        if (!Directory.Exists(_configurationService.PositiveCaptureFolder))
        {
            Directory.CreateDirectory(_configurationService.PositiveCaptureFolder);
            Resolver.Log.Info($"Created positive capture folder: {_configurationService.PositiveCaptureFolder}");
        }

        if (_configurationService.EnableManualCapture && !Directory.Exists(_configurationService.ManualCaptureFolder))
        {
            Directory.CreateDirectory(_configurationService.ManualCaptureFolder);
            Resolver.Log.Info($"Created manual capture folder: {_configurationService.ManualCaptureFolder}");
        }
    }

    /// <summary>
    /// Starts processing by subscribing to grouped sign detection events and capturing camera frames.
    /// </summary>
    /// <exception cref="Exception">Any exception that might be thrown during the processing loop.</exception>
    /// <remarks>
    /// The method stores the latest captured frame for processing in the main loop, as the camera captures frames faster than it can process them.
    /// </remarks>
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

            // Handle manual capture if active
            if (_manualCaptureActive && matToProcess != null)
            {
                try
                {
                    // Check if capture duration has elapsed
                    var captureElapsed = (DateTime.Now - _manualCaptureStartTime).TotalSeconds;

                    if (captureElapsed < MANUAL_CAPTURE_DURATION_SECONDS)
                    {
                        // Still within capture window - capture frame
                        var frameClone = matToProcess.Clone();
                        _manualCaptureFrames.Add(frameClone);

                        // Log every 10th frame to avoid spam
                        if (_manualCaptureFrames.Count % 10 == 0)
                        {
                            Resolver.Log.Info($"[MANUAL CAPTURE] Captured {_manualCaptureFrames.Count} frames ({captureElapsed:F1}s / {MANUAL_CAPTURE_DURATION_SECONDS}s)");
                        }
                    }
                    else
                    {
                        // Capture window expired - save all captured frames
                        Resolver.Log.Info($"[MANUAL CAPTURE] Capture window expired. Captured {_manualCaptureFrames.Count} frames in {captureElapsed:F1}s");
                        SaveManualCaptureFrames();
                        _manualCaptureActive = false;
                        ManualCaptureStateChanged?.Invoke(this, _manualCaptureActive);
                    }
                }
                catch (Exception ex)
                {
                    Resolver.Log.Error($"Error during manual capture: {ex.Message}");
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

                        // Check for false positives and positives to capture
                        bool hasFalsePositive = false;
                        bool hasPositiveDetection = false;

                        foreach (var detection in detections)
                        {
                            Resolver.Log.Info($"[{timestamp}] Frame #{_frameCount}: {detection.SpeedLimit} mph detected (confidence: {detection.Confidence:P1})");

                            // Check if this is a positive detection (above threshold)
                            if (detection.Confidence >= _speedLimitService.ConfidenceThreshold)
                            {
                                hasPositiveDetection = true;
                            }
                            // Check if this is a false positive (between thresholds)
                            else if (detection.Confidence >= _configurationService.FalsePositiveThreshold)
                            {
                                hasFalsePositive = true;
                            }
                        }

                        // Capture positive detections if within limit
                        if (hasPositiveDetection && _positiveCaptureCount < _configurationService.MaxPositiveCaptures)
                        {
                            CapturePositiveDetection(matToProcess, detections, timestamp);
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
            // Get the highest confidence false positive for the filename
            var falsePositiveDetections = detections
                .Where(d => d.Confidence >= _configurationService.FalsePositiveThreshold
                            && d.Confidence < _speedLimitService.ConfidenceThreshold)
                .OrderByDescending(d => d.Confidence)
                .ToList();

            if (falsePositiveDetections.Count > 0)
            {
                var primaryDetection = falsePositiveDetections.First();
                var dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var confidenceStr = (primaryDetection.Confidence * 100).ToString("F1");

                // Save raw frame (no annotations)
                var rawFilename = $"{dateStr}_{primaryDetection.Label}_{confidenceStr}.jpg";
                var rawPath = Path.Combine(_configurationService.FalsePositiveCaptureFolder, rawFilename);
                Cv2.ImWrite(rawPath, frame);

                // Create annotated frame with red bounding boxes
                using var annotatedFrame = frame.Clone();
                foreach (var detection in falsePositiveDetections)
                {
                    var box = detection.BoundingBox;
                    var rect = new Rect((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height);

                    // Draw red bounding box
                    Cv2.Rectangle(annotatedFrame, rect, OpenCvSharp.Scalar.Red, 3);

                    // Draw label with class and confidence
                    var label = $"{detection.Label} {detection.Confidence:P1}";
                    var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.6, 2, out _);
                    var labelPos = new Point((int)box.X, (int)box.Y - 10);

                    // Draw background rectangle for text
                    Cv2.Rectangle(annotatedFrame,
                        new Point(labelPos.X, labelPos.Y - textSize.Height - 5),
                        new Point(labelPos.X + textSize.Width, labelPos.Y + 5),
                        OpenCvSharp.Scalar.Red, -1);

                    // Draw text
                    Cv2.PutText(annotatedFrame, label, labelPos,
                        HersheyFonts.HersheySimplex, 0.6, OpenCvSharp.Scalar.White, 2);
                }

                // Save annotated frame
                var annotatedFilename = $"{dateStr}_{primaryDetection.Label}_{confidenceStr}-bb.jpg";
                var annotatedPath = Path.Combine(_configurationService.FalsePositiveCaptureFolder, annotatedFilename);
                Cv2.ImWrite(annotatedPath, annotatedFrame);

                _falsePositiveCaptureCount++;
                Resolver.Log.Info($"[FALSE POSITIVE] Captured {_falsePositiveCaptureCount}/{_configurationService.MaxFalsePositiveCaptures}: {rawFilename} + {annotatedFilename}");
            }
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"Error capturing false positive: {ex.Message}");
        }
    }

    private void CapturePositiveDetection(Mat frame, List<SpeedLimitDetection> detections, string timestamp)
    {
        try
        {
            // Get the highest confidence positive detection for the filename
            var positiveDetections = detections
                .Where(d => d.Confidence >= _speedLimitService.ConfidenceThreshold)
                .OrderByDescending(d => d.Confidence)
                .ToList();

            if (positiveDetections.Count > 0)
            {
                var primaryDetection = positiveDetections.First();
                var dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Save raw frame (no annotations)
                var rawFilename = $"{dateStr}_{primaryDetection.Label}.jpg";
                var rawPath = Path.Combine(_configurationService.PositiveCaptureFolder, rawFilename);
                Cv2.ImWrite(rawPath, frame);

                // Create annotated frame with green bounding boxes
                using var annotatedFrame = frame.Clone();
                foreach (var detection in positiveDetections)
                {
                    var box = detection.BoundingBox;
                    var rect = new Rect((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height);

                    // Draw green bounding box
                    Cv2.Rectangle(annotatedFrame, rect, new OpenCvSharp.Scalar(0, 255, 0), 3);

                    // Draw label with class and confidence
                    var label = $"{detection.SpeedLimit} mph {detection.Confidence:P1}";
                    var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.8, 2, out _);
                    var labelPos = new Point((int)box.X, (int)box.Y - 10);

                    // Draw background rectangle for text
                    Cv2.Rectangle(annotatedFrame,
                        new Point(labelPos.X, labelPos.Y - textSize.Height - 5),
                        new Point(labelPos.X + textSize.Width, labelPos.Y + 5),
                        new OpenCvSharp.Scalar(0, 255, 0), -1);

                    // Draw text
                    Cv2.PutText(annotatedFrame, label, labelPos,
                        HersheyFonts.HersheySimplex, 0.8, new OpenCvSharp.Scalar(0, 0, 0), 2);
                }

                // Save annotated frame
                var annotatedFilename = $"{dateStr}_{primaryDetection.Label}-bb.jpg";
                var annotatedPath = Path.Combine(_configurationService.PositiveCaptureFolder, annotatedFilename);
                Cv2.ImWrite(annotatedPath, annotatedFrame);

                _positiveCaptureCount++;
                Resolver.Log.Info($"[POSITIVE] Captured {_positiveCaptureCount}/{_configurationService.MaxPositiveCaptures}: {rawFilename} + {annotatedFilename}");
            }
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"Error capturing positive detection: {ex.Message}");
        }
    }

    private void SaveManualCaptureFrames()
    {
        try
        {
            if (_manualCaptureSessionCount >= _configurationService.MaxManualCaptures)
            {
                Resolver.Log.Warn($"[MANUAL CAPTURE] Max capture sessions reached ({_configurationService.MaxManualCaptures}), skipping save");
                // Dispose frames
                foreach (var mat in _manualCaptureFrames)
                {
                    mat.Dispose();
                }
                _manualCaptureFrames.Clear();
                return;
            }

            var dateStr = _manualCaptureStartTime.ToString("yyyyMMdd_HHmmss");

            for (int i = 0; i < _manualCaptureFrames.Count; i++)
            {
                var filename = $"{dateStr}_frame{(i + 1):D2}.jpg";
                var outputPath = Path.Combine(_configurationService.ManualCaptureFolder, filename);
                Cv2.ImWrite(outputPath, _manualCaptureFrames[i]);
            }

            _manualCaptureSessionCount++;
            Resolver.Log.Info($"[MANUAL CAPTURE] Saved {_manualCaptureFrames.Count} frames to {_configurationService.ManualCaptureFolder} (session {_manualCaptureSessionCount}/{_configurationService.MaxManualCaptures})");

            // Dispose all frames
            foreach (var mat in _manualCaptureFrames)
            {
                mat.Dispose();
            }
            _manualCaptureFrames.Clear();
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"Error saving manual capture frames: {ex.Message}");
        }
    }

    public void TriggerManualCapture()
    {
        if (!_configurationService.EnableManualCapture)
        {
            Resolver.Log.Warn("[MANUAL CAPTURE] Manual capture is disabled in configuration");
            return;
        }

        if (_manualCaptureActive)
        {
            Resolver.Log.Warn("[MANUAL CAPTURE] Capture already in progress, ignoring trigger");
            return;
        }

        _manualCaptureActive = true;
        ManualCaptureStateChanged?.Invoke(this, _manualCaptureActive);
        _manualCaptureStartTime = DateTime.Now;
        _manualCaptureFrames.Clear();
        Resolver.Log.Info($"[MANUAL CAPTURE] Started - will capture frames for {MANUAL_CAPTURE_DURATION_SECONDS} seconds");
    }
}
