using Neomotive.Video;
using NeoMotive.Services;
using OpenCvSharp;

Console.WriteLine("=== Headless Camera with Speed Limit Detection ===");
Console.WriteLine();

// Parse command line arguments
string cameraMode = "usb"; // default
int cameraIndex = 0;
string? videoFilePath = null;
bool enableDetection = false;
string? modelPath = null;
int? durationSeconds = null;
int statusIntervalSeconds = 5; // default: show status every 5 seconds

if (args.Length > 0)
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i].ToLower())
        {
            case "--usb":
                cameraMode = "usb";
                // Check if next arg is a camera index
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int index))
                {
                    cameraIndex = index;
                    i++; // Skip next arg
                }
                break;
            case "--file":
                cameraMode = "file";
                if (i + 1 < args.Length)
                {
                    videoFilePath = args[i + 1];
                    i++; // Skip next arg
                }
                else
                {
                    Console.WriteLine("Error: --file requires a file path argument");
                    Console.WriteLine();
                    PrintUsage();
                    return 1;
                }
                break;
            case "--detect":
                enableDetection = true;
                break;
            case "--model":
                if (i + 1 < args.Length)
                {
                    modelPath = args[i + 1];
                    i++; // Skip next arg
                }
                else
                {
                    Console.WriteLine("Error: --model requires a file path argument");
                    Console.WriteLine();
                    PrintUsage();
                    return 1;
                }
                break;
            case "--duration":
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int duration))
                {
                    durationSeconds = duration;
                    i++; // Skip next arg
                }
                else
                {
                    Console.WriteLine("Error: --duration requires a number (seconds)");
                    Console.WriteLine();
                    PrintUsage();
                    return 1;
                }
                break;
            case "--status-interval":
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int statusInterval))
                {
                    statusIntervalSeconds = statusInterval;
                    i++; // Skip next arg
                }
                else
                {
                    Console.WriteLine("Error: --status-interval requires a number (seconds)");
                    Console.WriteLine();
                    PrintUsage();
                    return 1;
                }
                break;
            case "--help":
            case "-h":
                PrintUsage();
                return 0;
            default:
                Console.WriteLine($"Unknown argument: {args[i]}");
                Console.WriteLine();
                PrintUsage();
                return 1;
        }
    }
}

// Display current configuration
if (cameraMode == "usb")
{
    Console.WriteLine($"Camera Mode: USB (Index: {cameraIndex})");
}
else
{
    Console.WriteLine($"Camera Mode: Video File");
    Console.WriteLine($"File Path: {videoFilePath}");
}
Console.WriteLine($"Detection: {(enableDetection ? "Enabled" : "Disabled")}");
if (enableDetection && modelPath != null)
{
    Console.WriteLine($"Model Path: {modelPath}");
}
if (durationSeconds.HasValue)
{
    Console.WriteLine($"Duration: {durationSeconds.Value} seconds");
}
else
{
    Console.WriteLine("Duration: Continuous (Ctrl+C to stop)");
}
Console.WriteLine();

Frame? latestFrame = null;
object frameLock = new object();
int frameCount = 0;
int detectionCount = 0;
DateTime startTime = DateTime.Now;
bool shouldExit = false;

// Status tracking
DateTime lastStatusUpdate = DateTime.Now;
int lastFrameCount = 0;

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true; // Prevent immediate termination
    shouldExit = true;
    Console.WriteLine();
    Console.WriteLine("Shutdown requested...");
};

ICamera? camera = null;
SpeedLimitService? detector = null;

try
{
    // Create camera based on mode
    Console.WriteLine("Initializing camera...");
    camera = cameraMode == "usb"
        ? new UsbCamera(cameraIndex: cameraIndex)
        : new VideoFileCamera(videoFilePath!);
    Console.WriteLine("Camera initialized successfully");

    // Create detector if enabled
    if (enableDetection)
    {
        Console.WriteLine("Initializing speed limit detector...");
        detector = modelPath != null ? new SpeedLimitService(modelPath) : new SpeedLimitService();
        Console.WriteLine("Detector initialized");
    }

    Console.WriteLine();
    Console.WriteLine("Starting capture...");
    Console.WriteLine();

    // Subscribe to frame captured event
    camera.FrameCaptured += (sender, frame) =>
    {
        lock (frameLock)
        {
            // Dispose old frame if exists
            latestFrame?.Dispose();
            // Store new frame
            latestFrame = frame;
            frameCount++;
        }
    };

    // Start capturing frames
    await camera.StartCapture();

    // Main processing loop
    while (!shouldExit)
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
        lock (frameLock)
        {
            if (latestFrame != null)
            {
                var mat = latestFrame.ToMat();
                matToProcess = mat.Clone();
            }
        }

        // Process frame if we have one
        if (matToProcess != null && detector != null)
        {
            try
            {
                var detections = detector.CheckForSpeedLimit(matToProcess, saveToFile: false, drawDetections: false);
                if (detections.Count > 0)
                {
                    detectionCount++;
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                    foreach (var detection in detections)
                    {
                        Console.WriteLine($"[{timestamp}] Frame #{frameCount}: {detection.SpeedLimit} mph detected (confidence: {detection.Confidence:P1})");
                    }
                }
            }
            finally
            {
                matToProcess.Dispose();
            }
        }

        // Periodic status output
        var timeSinceLastStatus = (DateTime.Now - lastStatusUpdate).TotalSeconds;
        if (timeSinceLastStatus >= statusIntervalSeconds)
        {
            var totalRuntime = (DateTime.Now - startTime).TotalSeconds;
            var framesSinceLastStatus = frameCount - lastFrameCount;
            var currentFps = framesSinceLastStatus / timeSinceLastStatus;

            Console.WriteLine($"[Status] Runtime: {totalRuntime:F1}s | Frames: {frameCount} | FPS: {currentFps:F1} | Detections: {detectionCount}");

            lastStatusUpdate = DateTime.Now;
            lastFrameCount = frameCount;
        }

        // Small delay to prevent maxing out CPU
        await Task.Delay(100);
    }

    Console.WriteLine();
    Console.WriteLine("Stopping camera...");

    // Stop capture
    await camera.StopCapture();

    // Clean up latest frame
    lock (frameLock)
    {
        latestFrame?.Dispose();
        latestFrame = null;
    }

    // Display statistics
    var totalTime = (DateTime.Now - startTime).TotalSeconds;
    var fps = frameCount / totalTime;

    Console.WriteLine();
    Console.WriteLine("=== Session Statistics ===");
    Console.WriteLine($"Total runtime: {totalTime:F1} seconds");
    Console.WriteLine($"Total frames: {frameCount}");
    Console.WriteLine($"Average FPS: {fps:F1}");
    if (enableDetection)
    {
        Console.WriteLine($"Frames with detections: {detectionCount}");
        Console.WriteLine($"Detection rate: {(detectionCount / (double)frameCount * 100):F1}%");
    }
    Console.WriteLine();
    Console.WriteLine("Session completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}
finally
{
    if (camera is IDisposable d)
    {
        d.Dispose();
    }
    detector?.Dispose();
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  App [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --usb [index]              Use USB camera (default). Optional camera index (default: 0)");
    Console.WriteLine("  --file <path>              Use video file as camera feed (loops continuously)");
    Console.WriteLine("  --detect                   Enable speed limit sign detection");
    Console.WriteLine("  --model <path>             Path to custom ONNX model (optional)");
    Console.WriteLine("  --duration <seconds>       Run for specified duration (default: continuous)");
    Console.WriteLine("  --status-interval <sec>    Status update interval in seconds (default: 5)");
    Console.WriteLine("  --help, -h                 Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  App                                          # USB camera, no detection");
    Console.WriteLine("  App --usb 0 --detect                         # USB camera with detection");
    Console.WriteLine("  App --file video.mp4 --detect                # Video file with detection");
    Console.WriteLine("  App --file video.mp4 --detect --duration 60  # Run for 60 seconds");
    Console.WriteLine("  App --usb 0 --detect --model custom.onnx     # Custom model");
    Console.WriteLine("  App --file video.mp4 --status-interval 10    # Status every 10 seconds");
}
